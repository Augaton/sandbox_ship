// Smooth motion on a replica (not in the web version). Snapshots come twenty times a second;
// shown as they come, ships would jump three frames out of four. Each snapshot is read through
// here instead: every entity glides from where it is shown to where the snapshot puts it, over
// the time snapshots take to come, and shots fly on along their speed. What is shown is written
// into the replica itself, which nothing else steps, so the view, the minimap and the pointer
// agree. The view is thus a snapshot behind the host, about 50 ms.
namespace SandboxShip.Sim.Net;

public sealed class ReplicaMotion
{
    /// <summary>Farther than this in one snapshot, an entity jumps there: it did not fly that way.</summary>
    const double Jump = 400;

    struct Glide
    {
        public double FromX, FromY, FromAngle, ToX, ToY, ToAngle;
    }

    readonly Dictionary<int, Glide> glides = [];
    readonly Dictionary<int, (double X, double Y, double Angle)> shown = [];
    readonly List<(Projectile P, double X, double Y)> shots = [];
    /// <summary>Seconds since the last snapshot, and between the last two (smoothed).</summary>
    double since, interval = 1 / Protocol.Rate;
    double lastAt = double.NaN;

    /// <summary>The time snapshots take to come, as measured: what a glide lasts.</summary>
    public double Interval => interval;

    /// <summary>Reads a snapshot into the replica (Snapshot.Read), arrived at `now` seconds; everything is still shown where it was.</summary>
    public void Read(BinaryReader r, World replica, double now)
    {
        shown.Clear();
        foreach (var e in replica.Entities) shown[e.Id] = (e.X, e.Y, e.Angle);
        Snapshot.Read(r, replica);
        glides.Clear();
        foreach (var e in replica.Entities)
        {
            var g = new Glide { ToX = e.X, ToY = e.Y, ToAngle = e.Angle };
            if (shown.TryGetValue(e.Id, out var was) && Worlds.Hypot(e.X - was.X, e.Y - was.Y) < Jump) (g.FromX, g.FromY, g.FromAngle) = was;
            else (g.FromX, g.FromY, g.FromAngle) = (g.ToX, g.ToY, g.ToAngle);
            glides[e.Id] = g;
        }
        shots.Clear();
        foreach (var p in replica.Projectiles) shots.Add((p, p.X, p.Y));
        if (!double.IsNaN(lastAt)) interval += (Math.Clamp(now - lastAt, 1 / 60.0, 0.25) - interval) * 0.25;
        lastAt = now;
        since = 0;
        Advance(replica, 0);
    }

    /// <summary>`dt` seconds later: each entity that far along its glide, each shot that far along its flight.</summary>
    public void Advance(World replica, double dt)
    {
        since += dt;
        var k = Math.Min(1, since / interval);
        foreach (var e in replica.Entities)
        {
            if (!glides.TryGetValue(e.Id, out var g)) continue;
            e.X = g.FromX + (g.ToX - g.FromX) * k;
            e.Y = g.FromY + (g.ToY - g.FromY) * k;
            e.Angle = g.FromAngle + Movement.WrapAngle(g.ToAngle - g.FromAngle) * k;
        }
        // Shots are shown as far back as ships are, and fly on a little if the next snapshot is late.
        var t = Math.Min(since, interval + 0.25) - interval;
        foreach (var (p, x, y) in shots)
        {
            p.X = x + p.Vx * t;
            p.Y = y + p.Vy * t;
        }
    }
}
