// The host's side of a campaign played over the network (not in the web version). Whoever steps
// the galaxy calls BeforeStep before each step, and once a frame when none runs (a battle
// waiting for its players' choice still needs their answer), then Collect with the frame's
// events and Tick. Its battles in the battle view are served by a BattleHost of their own.
using System.Diagnostics;

namespace SandboxShip.Sim.Net;

/// <summary>
/// Serves the remote players of a campaign: their galaxy commands wait for the next step, in the
/// order they came, and those refused are answered; each player gets, Protocol.GalaxyRate times
/// a second, a snapshot of what its team knows with the events since the last one, the notices
/// sent apart and reliably (Tidings); a player who leaves hands its side to the AI.
/// </summary>
public sealed class GalaxyHost(Galaxy galaxy, Action<int, Message> send)
{
    sealed class Remote(Side side, DelayedSink<GalaxyCommand> sink)
    {
        public readonly Side Side = side;
        public readonly DelayedSink<GalaxyCommand> Sink = sink;
        public readonly List<GalaxyEvent> Events = [];
        public long Bytes;
        /// <summary>Seconds until this player's next snapshot.</summary>
        public double Wait;
        /// <summary>The player got the chart, what never changes: it goes with its first snapshot.</summary>
        public bool Charted;
    }

    readonly Dictionary<int, Remote> remotes = [];
    readonly MemoryStream buffer = new(), news = new();
    readonly Stopwatch clock = new();
    byte[]? chart;

    /// <summary>Snapshots written so far, and the time spent writing them, in milliseconds.</summary>
    public int Snapshots { get; private set; }
    public double WriteMs => clock.Elapsed.TotalMilliseconds;

    public Galaxy Galaxy => galaxy;
    public IEnumerable<int> Peers => remotes.Keys;
    /// <summary>The galaxy's speed and pause, as the host sets them for everyone: every snapshot tells them.</summary>
    public GalaxyClock Clock { get; set; } = new(1, false);

    /// <summary>A remote player, on the side its seat plays.</summary>
    public void Add(int peer, Side side) => remotes[peer] = new Remote(side, new DelayedSink<GalaxyCommand>(c => Commands.Apply(galaxy, side, c)))
    {
        // Players take turns, a frame apart: their snapshots are not all written in the same frame.
        Wait = remotes.Count / 60.0 % (1 / Protocol.GalaxyRate),
    };

    public Side? SideOf(int peer) => remotes.TryGetValue(peer, out var r) ? r.Side : null;

    /// <summary>The peer playing a side, if a remote player does.</summary>
    public int? PeerOf(Side side) => remotes.FirstOrDefault(p => p.Value.Side == side) is { Value: not null } p ? p.Key : null;

    /// <summary>Bytes of snapshots sent to a player so far.</summary>
    public long BytesTo(int peer) => remotes.TryGetValue(peer, out var r) ? r.Bytes : 0;

    /// <summary>A player's command, for the next step; from a peer not (or no longer) in the campaign, nothing.</summary>
    public void Receive(int peer, GalaxyCommand c)
    {
        if (remotes.TryGetValue(peer, out var r)) r.Sink.Send(c);
    }

    /// <summary>Applies the commands that came since the last call, each player's in order, and tells each player those refused.</summary>
    public void BeforeStep()
    {
        foreach (var (peer, r) in remotes)
        {
            if (r.Sink.Pending == 0) continue;
            foreach (var result in r.Sink.Flush()) if (!result.Ok) send(peer, new Message.Refused(result.Reason));
        }
    }

    /// <summary>Keeps these events (the galaxy's since the last call) for each player's next snapshot.</summary>
    public void Collect(IEnumerable<GalaxyEvent> events)
    {
        foreach (var ev in events) foreach (var r in remotes.Values) r.Events.Add(ev);
    }

    /// <summary>Real time goes by: each player whose snapshot is due gets one.</summary>
    public void Tick(double dt)
    {
        foreach (var (peer, r) in remotes)
        {
            r.Wait -= dt;
            if (r.Wait > 0.001) continue;
            SendSnapshot(peer, r);
            // Late by more than a snapshot (a long frame): one now, not a burst after it.
            r.Wait = Math.Max(r.Wait, -1 / Protocol.GalaxyRate) + 1 / Protocol.GalaxyRate;
        }
    }

    /// <summary>Every player gets a snapshot now: the galaxy just changed for all (a battle decided, the pause).</summary>
    public void SendAll()
    {
        foreach (var (peer, r) in remotes)
        {
            SendSnapshot(peer, r);
            r.Wait = 1 / Protocol.GalaxyRate;
        }
    }

    void SendSnapshot(int peer, Remote r)
    {
        clock.Start();
        buffer.SetLength(0);
        news.SetLength(0);
        using (var w = new BinaryWriter(buffer, System.Text.Encoding.UTF8, true))
        using (var v = new BinaryWriter(news, System.Text.Encoding.UTF8, true))
        {
            GalaxySnapshot.Write(galaxy, r.Side, w, r.Events, v, Clock);
        }
        var bytes = buffer.ToArray();
        clock.Stop();
        Snapshots++;
        r.Bytes += bytes.Length;
        r.Events.Clear();
        // Reliably, after the Start that opened the campaign; a snapshot that passes it waits for the next.
        if (!r.Charted)
        {
            send(peer, new Message.Chart(chart ??= GalaxySnapshot.Chart(galaxy)));
            r.Charted = true;
        }
        // A count of none is a single byte.
        if (news.Length > 1) send(peer, new Message.Tidings(news.ToArray()));
        send(peer, new Message.Map(bytes));
    }

    /// <summary>A player left: the AI plays its side from now on, and the battles that waited for it go on.</summary>
    public void Drop(int peer)
    {
        if (remotes.Remove(peer, out var r)) Galaxies.HandToAI(galaxy, r.Side);
    }

    /// <summary>
    /// A player who left comes back to its side, under its name: a human's again, the AI that held
    /// it let go; it is served from the chart on, as when the campaign began.
    /// </summary>
    public void Return(int peer, Side side, string name)
    {
        galaxy.Humans[side] = name;
        if (galaxy.Factions.TryGetValue(side, out var f)) f.AI = null;
        Add(peer, side);
    }
}

/// <summary>
/// Smooth fleets on a galaxy replica (not in the web version). Snapshots come five times a
/// second; shown as they come, fleets would hop along their lanes. Each fleet on the move glides
/// from where it is shown to where the snapshot puts it, over the time snapshots take to come,
/// as ReplicaMotion does for a battle; one that changed lanes jumps to its new one.
/// </summary>
public sealed class GalaxyMotion
{
    readonly Dictionary<int, (int From, int? To, double Progress)> shown = [];
    readonly Dictionary<int, (double From, double To)> glides = [];
    double since, interval = 1 / Protocol.GalaxyRate;
    double lastAt = double.NaN;

    /// <summary>The host's clock, as the last snapshot told it.</summary>
    public GalaxyClock Clock { get; private set; } = new(1, false);

    /// <summary>
    /// Reads a snapshot into the replica (GalaxySnapshot.Read), arrived at `now` seconds; every
    /// fleet is still shown where it was. False, and nothing changes, before the chart came.
    /// </summary>
    public bool Read(BinaryReader r, Galaxy replica, double now)
    {
        shown.Clear();
        foreach (var f in replica.Fleets.Values) shown[f.Id] = (f.From, f.To, f.Progress);
        if (GalaxySnapshot.Read(r, replica) is not { } clock) return false;
        Clock = clock;
        glides.Clear();
        foreach (var f in replica.Fleets.Values)
        {
            if (f.To is null) continue;
            var same = shown.TryGetValue(f.Id, out var was) && was.From == f.From && was.To == f.To && was.Progress <= f.Progress;
            glides[f.Id] = (same ? was.Progress : f.Progress, f.Progress);
        }
        if (!double.IsNaN(lastAt)) interval += (Math.Clamp(now - lastAt, 1 / 60.0, 1) - interval) * 0.25;
        lastAt = now;
        since = 0;
        Advance(replica, 0);
        return true;
    }

    /// <summary>`dt` seconds later: each fleet that far along its glide.</summary>
    public void Advance(Galaxy replica, double dt)
    {
        since += dt;
        var k = Math.Min(1, since / interval);
        foreach (var (id, (from, to)) in glides)
        {
            if (replica.Fleets.TryGetValue(id, out var f)) f.Progress = from + (to - from) * k;
        }
    }
}
