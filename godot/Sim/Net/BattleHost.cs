// The host's side of a battle played over the network (not in the web version). Whoever steps
// the world (the battle screen of a host who plays, or a dedicated server) calls BeforeStep
// before each step, then once a frame Collect, before the view clears the events, and Tick.
namespace SandboxShip.Sim.Net;

/// <summary>
/// Serves the remote players of a battle: their commands wait for the start of the next step,
/// in the order they came, and those refused are answered; each player gets, Protocol.Rate
/// times a second, a snapshot of what its team sees with the events since the last one, less
/// often when snapshots grow past its Budget; a player who leaves hands its side to the AI.
/// </summary>
public sealed class BattleHost(World world, Action<int, Message> send)
{
    /// <summary>
    /// Bytes a second a player gets at most, before compression. A duel stays far below it at
    /// every snapshot; a huge battle (50 KB a snapshot) comes five times a second instead of
    /// twenty, which a home connection can carry for several players, and which costs the host
    /// a quarter of the writing.
    /// </summary>
    public const double Budget = 256 * 1024;

    sealed class Remote(Side side, DelayedSink<Command> sink)
    {
        public readonly Side Side = side;
        public readonly DelayedSink<Command> Sink = sink;
        public readonly List<SimEvent> Events = [];
        public long Bytes;
        /// <summary>Seconds until this player's next snapshot.</summary>
        public double Wait;
    }

    readonly Dictionary<int, Remote> remotes = [];
    readonly MemoryStream buffer = new();
    readonly System.Diagnostics.Stopwatch clock = new();

    /// <summary>Snapshots written so far, and the time spent writing them, in milliseconds.</summary>
    public int Snapshots { get; private set; }
    public double WriteMs => clock.Elapsed.TotalMilliseconds;

    public World World => world;
    public IEnumerable<int> Peers => remotes.Keys;

    /// <summary>A remote player, on the side its seat plays.</summary>
    public void Add(int peer, Side side) => remotes[peer] = new Remote(side, new DelayedSink<Command>(c => Commands.Apply(world, side, c)))
    {
        // Players take turns, a frame apart: their snapshots are not all written in the same frame.
        Wait = remotes.Count * Steps.Step % (1 / Protocol.Rate),
    };

    public Side? SideOf(int peer) => remotes.TryGetValue(peer, out var r) ? r.Side : null;

    /// <summary>Bytes of snapshots sent to a player so far.</summary>
    public long BytesTo(int peer) => remotes.TryGetValue(peer, out var r) ? r.Bytes : 0;

    /// <summary>A player's command, for the next step; from a peer not (or no longer) in the battle, nothing.</summary>
    public void Receive(int peer, Command c)
    {
        if (remotes.TryGetValue(peer, out var r)) r.Sink.Send(c);
    }

    /// <summary>Applies the commands that came since the last step, each player's in order, and tells each player those refused.</summary>
    public void BeforeStep()
    {
        foreach (var (peer, r) in remotes)
        {
            if (r.Sink.Pending == 0) continue;
            foreach (var result in r.Sink.Flush()) if (!result.Ok) send(peer, new Message.Refused(result.Reason));
        }
    }

    /// <summary>Keeps this frame's events for each player's next snapshot: the world's own are cleared by whoever shows them.</summary>
    public void Collect()
    {
        if (world.Events.Count == 0) return;
        foreach (var r in remotes.Values) r.Events.AddRange(world.Events);
    }

    /// <summary>Real time goes by: each player whose snapshot is due gets one.</summary>
    public void Tick(double dt)
    {
        foreach (var (peer, r) in remotes)
        {
            r.Wait -= dt;
            // A millisecond early is on time: three frames at 60 images a second add up to a hair under 1/20 s.
            if (r.Wait > 0.001) continue;
            var bytes = SendSnapshot(peer, r);
            // Late by more than a snapshot (a long frame): one now, not a burst after it.
            r.Wait = Math.Max(r.Wait, -1 / Protocol.Rate) + Math.Max(1 / Protocol.Rate, bytes / Budget);
        }
    }

    /// <summary>A snapshot to a player now, with the events since its last one; its size.</summary>
    int SendSnapshot(int peer, Remote r)
    {
        clock.Start();
        buffer.SetLength(0);
        using (var w = new BinaryWriter(buffer, System.Text.Encoding.UTF8, true)) Snapshot.Write(world, r.Side, w, r.Events);
        var bytes = buffer.ToArray();
        clock.Stop();
        Snapshots++;
        r.Bytes += bytes.Length;
        r.Events.Clear();
        send(peer, new Message.State(bytes));
        return bytes.Length;
    }

    /// <summary>A player left: the AI plays its side from now on, its hand-flown ship given back first.</summary>
    public void Drop(int peer)
    {
        if (!remotes.Remove(peer, out var r) || !world.Factions.TryGetValue(r.Side, out var f)) return;
        foreach (var e in world.Entities.Where(e => e.Side == r.Side && e.Pilot is not null).ToList()) Commands.Apply(world, r.Side, new Command.StopPilot(e.Id));
        f.AI ??= Worlds.NewAI();
        f.AutoAbilities = true;
    }
}
