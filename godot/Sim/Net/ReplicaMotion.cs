// Smooth motion on a replica (not in the web version). Snapshots come twenty times a second;
// shown as they come, ships would jump three frames out of four. Each snapshot is read through
// here instead: every entity glides from where it is shown to where the snapshot puts it, over
// the time snapshots take to come, and shots fly on along their speed. What is shown is written
// into the replica itself, which nothing else steps, so the view, the minimap and the pointer
// agree. The view is thus a snapshot behind the host, about 50 ms. A snapshot comes in parts
// (SnapshotParts.cs): each entity and each shot glides from when the part that moved it came.
namespace SandboxShip.Sim.Net;

public sealed class ReplicaMotion
{
    /// <summary>Farther than this in one snapshot, an entity jumps there: it did not fly that way.</summary>
    const double Jump = 400;

    struct Glide
    {
        public double FromX, FromY, FromAngle, ToX, ToY, ToAngle, Start;
    }

    readonly Dictionary<int, Glide> glides = [];
    /// <summary>Each shot: where the snapshot put it, and when.</summary>
    readonly Dictionary<int, (double X, double Y, double Start)> shots = [];
    /// <summary>Seconds of this replica's life, what glides are timed by; between two snapshots (smoothed).</summary>
    double clock, interval = 1 / Protocol.Rate;
    double lastAt = double.NaN;
    int lastSeq;

    /// <summary>The time snapshots take to come, as measured: what a glide lasts.</summary>
    public double Interval => interval;

    /// <summary>The snapshots read, baselines of the next ones: the player tells the host the newest (Message.Ack).</summary>
    public SnapshotFrames Frames { get; } = new();

    /// <summary>
    /// Reads a whole snapshot into the replica (Snapshot.Read), arrived at `now` seconds; everything is
    /// still shown where it was. False when it could not be read for want of its baseline: nothing changed.
    /// </summary>
    public bool Read(BinaryReader r, World replica, double now)
    {
        var before = Shown(replica);
        if (!Snapshot.Read(r, replica, Frames)) return false;
        Came(now, Frames.Latest);
        foreach (var e in replica.Entities) Restart(e, before.GetValueOrDefault(e.Id));
        Forget(replica, _ => true);
        shots.Clear();
        foreach (var p in replica.Projectiles) shots[p.Id] = (p.X, p.Y, clock);
        Advance(replica, 0);
        return true;
    }

    /// <summary>
    /// Reads one part of a snapshot into the replica (Snapshot.ReadPart), arrived at `now`
    /// seconds: the entities or the shots it covers glide on from where they are shown, the
    /// others go on as they were. Once a part completes its snapshot, the player acknowledges it.
    /// </summary>
    public Snapshot.PartRead ReadPart(BinaryReader r, World replica, double now)
    {
        var part = Snapshot.ReadPart(r, replica, Frames);
        if (!part.Read) return part;
        Came(now, part.Seq);
        if (part.Entities)
        {
            foreach (var e in replica.Entities) if (part.Covers(e.Id)) Restart(e, glides.TryGetValue(e.Id, out var g) ? At(g) : null);
            Forget(replica, part.Covers);
        }
        else if (part.Shots)
        {
            foreach (var id in shots.Keys.Where(id => part.Covers(id)).ToList()) shots.Remove(id);
            foreach (var p in replica.Projectiles) if (part.Covers(p.Id)) shots[p.Id] = (p.X, p.Y, clock);
        }
        Advance(replica, 0);
        return part;
    }

    /// <summary>A snapshot's first part came: how long snapshots take to come, smoothed.</summary>
    void Came(double now, int seq)
    {
        if (seq == lastSeq) return;
        lastSeq = seq;
        if (!double.IsNaN(lastAt)) interval += (Math.Clamp(now - lastAt, 1 / 60.0, 0.25) - interval) * 0.25;
        lastAt = now;
    }

    static Dictionary<int, (double X, double Y, double Angle)> Shown(World replica)
    {
        var shown = new Dictionary<int, (double, double, double)>(replica.Entities.Count);
        foreach (var e in replica.Entities) shown[e.Id] = (e.X, e.Y, e.Angle);
        return shown;
    }

    /// <summary>Where a glide shows its entity now.</summary>
    (double X, double Y, double Angle) At(Glide g)
    {
        var k = Math.Min(1, (clock - g.Start) / interval);
        return (g.FromX + (g.ToX - g.FromX) * k, g.FromY + (g.ToY - g.FromY) * k, g.FromAngle + Movement.WrapAngle(g.ToAngle - g.FromAngle) * k);
    }

    /// <summary>An entity the snapshot just put somewhere: it glides there from where it was shown, unless it jumped or is new.</summary>
    void Restart(Entity e, (double X, double Y, double Angle)? was)
    {
        var g = new Glide { ToX = e.X, ToY = e.Y, ToAngle = e.Angle, Start = clock };
        if (was is { } w && Worlds.Hypot(e.X - w.X, e.Y - w.Y) < Jump) (g.FromX, g.FromY, g.FromAngle) = w;
        else (g.FromX, g.FromY, g.FromAngle) = (g.ToX, g.ToY, g.ToAngle);
        glides[e.Id] = g;
    }

    /// <summary>The glides of entities gone from what a snapshot covers.</summary>
    void Forget(World replica, Func<int, bool> covered)
    {
        foreach (var id in glides.Keys.Where(id => covered(id) && !replica.Entities.Has(id)).ToList()) glides.Remove(id);
    }

    /// <summary>`dt` seconds later: each entity that far along its glide, each shot that far along its flight.</summary>
    public void Advance(World replica, double dt)
    {
        clock += dt;
        foreach (var e in replica.Entities)
        {
            if (!glides.TryGetValue(e.Id, out var g)) continue;
            (e.X, e.Y, e.Angle) = At(g);
        }
        // The parts of a ship stay on it as it turns, as they do on the host.
        Worlds.AttachHardpoints(replica);
        // Shots are shown as far back as ships are, and fly on a little if the next snapshot is late.
        foreach (var p in replica.Projectiles)
        {
            if (!shots.TryGetValue(p.Id, out var s)) continue;
            var t = Math.Min(clock - s.Start, interval + 0.25) - interval;
            p.X = s.X + p.Vx * t;
            p.Y = s.Y + p.Vy * t;
        }
    }
}
