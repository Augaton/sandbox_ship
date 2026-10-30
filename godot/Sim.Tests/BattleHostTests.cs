// A battle played over the network, host and players in one process (not in the web version):
// a player's order is applied at the start of the next step, and answered when refused; each
// player's replica follows the host twenty times a second and glides in between; a player who
// leaves hands its side to the AI. The last test measures what a host sends.
using SandboxShip.Sim.Data;
using SandboxShip.Sim.Net;
using Xunit;
using Xunit.Abstractions;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim.Tests;

public class BattleHostTests(ITestOutputHelper output)
{
    const int Bob = 1001, Eve = 1002;
    static readonly Side Klaed = Faction.Klaed, Nairan = Faction.Nairan, Nautolan = Faction.Nautolan;

    /// <summary>The host plays Kla'ed; Bob the Nairan and Eve the Nautolan, from afar. Every message goes through its bytes.</summary>
    sealed class Table
    {
        public readonly World World;
        public readonly BattleHost Host;
        public readonly Dictionary<int, World> Replicas = new() { [Bob] = new World(), [Eve] = new World() };
        public readonly Dictionary<int, ReplicaMotion> Motions = new() { [Bob] = new ReplicaMotion(), [Eve] = new ReplicaMotion() };
        public readonly Dictionary<int, List<string>> Refusals = new() { [Bob] = [], [Eve] = [] };
        public readonly Dictionary<int, List<SimEvent>> Heard = new() { [Bob] = [], [Eve] = [] };
        public double Now;

        public Table(World world)
        {
            World = world;
            Host = new BattleHost(world, Deliver);
            Host.Add(Bob, Nairan);
            Host.Add(Eve, Nautolan);
        }

        void Deliver(int peer, Message m)
        {
            switch (Protocol.Decode(Protocol.Encode(m)))
            {
                case Message.State s:
                    var replica = Replicas[peer];
                    Motions[peer].Read(new BinaryReader(new MemoryStream(s.Snapshot)), replica, Now);
                    Heard[peer].AddRange(replica.Events);
                    replica.Events.Clear();
                    break;
                case Message.Refused r: Refusals[peer].Add(r.Reason); break;
            }
        }

        public void Order(int peer, Command c) => Host.Receive(peer, ((Message.Order)Protocol.Decode(Protocol.Encode(new Message.Order(c)))).Command);

        /// <summary>One frame of the host's screen at 60 images a second: one step, then the events kept and the snapshots due.</summary>
        public void Frame()
        {
            Host.BeforeStep();
            Steps.StepWorld(World);
            Host.Collect();
            World.Events.Clear();
            Now += Steps.Step;
            Host.Tick(Steps.Step);
            foreach (var (peer, m) in Motions) m.Advance(Replicas[peer], Steps.Step);
        }

        public void Run(double seconds)
        {
            for (var i = Js.Round(seconds / Steps.Step); i > 0; i--) Frame();
        }
    }

    /// <summary>A small skirmish of three players, three frigates by each station.</summary>
    static World Battle()
    {
        var w = Scenario.CreateSkirmish(new SkirmishOptions
        {
            Slots = [new Slot(Klaed, Faction.Klaed), new Slot(Nairan, Faction.Nairan), new Slot(Nautolan, Faction.Nautolan)], Seed = 3, Size = MapSize.Small,
        });
        foreach (var side in new[] { Klaed, Nairan, Nautolan })
        {
            var home = StationOf(w, side)!;
            for (var i = 0; i < 3; i++) SpawnShip(w, side, ShipClass.Frigate, home.X + 250, home.Y + (i - 1) * 80);
        }
        return w;
    }

    [Fact]
    public void A_players_order_waits_for_the_next_step_and_its_replica_sees_it_obeyed()
    {
        var t = new Table(Battle());
        t.Run(0.2);
        var bobs = t.World.Entities.Where(e => e.Side == Nairan && e.Kind == EntityKind.Ship).Select(e => e.Id).ToArray();
        Assert.NotEmpty(bobs);
        // Bob sees his own ships, not Eve's far station.
        var replica = t.Replicas[Bob];
        Assert.All(bobs, id => Assert.True(replica.Entities.Has(id)));
        Assert.False(replica.Entities.Has(StationOf(t.World, Nautolan)!.Id));
        var (x, y) = (t.World.Width / 2, t.World.Height / 2);
        t.Order(Bob, new Command.Move(bobs, x, y, true));
        Assert.All(bobs, id => Assert.Null(t.World.Entities.Get(id)!.Order));
        t.Frame();
        Assert.All(bobs, id => Assert.Equal(OrderType.AttackMove, t.World.Entities.Get(id)!.Order!.Type));
        var start = Dist(t.World.Entities.Get(bobs[0])!, new Point(x, y));
        t.Run(3);
        var host = t.World.Entities.Get(bobs[0])!;
        Expect.Less(Dist(host, new Point(x, y)), start - 50);
        // The replica shows it a snapshot behind, at most: the ship is near where the host has it.
        var shown = replica.Entities.Get(bobs[0])!;
        Expect.Less(Dist(shown, host), host.Speed / Protocol.Rate + 1);
        Assert.Equal(OrderType.AttackMove, shown.Order!.Type);
        // Eve may not order Bob's ships: her order is refused, and she is told why.
        t.Order(Eve, new Command.Stop(bobs));
        t.Frame();
        Assert.Equal(["Pas sous tes ordres"], t.Refusals[Eve]);
        Assert.Empty(t.Refusals[Bob]);
        Assert.Equal(OrderType.AttackMove, host.Order!.Type);
    }

    [Fact]
    public void Each_player_gets_twenty_snapshots_a_second_with_the_events_of_its_team()
    {
        var t = new Table(Battle());
        t.Run(1);
        Assert.InRange(t.Host.Snapshots, 2 * 19, 2 * 21);
        // Bob's station is hit: he hears of it, Eve far away does not.
        var station = StationOf(t.World, Nairan)!;
        t.Heard[Bob].Clear();
        t.Heard[Eve].Clear();
        var bomber = SpawnShip(t.World, Klaed, ShipClass.Bomber, station.X + 200, station.Y);
        Movement.IssueAttack(t.World, [bomber.Id], station.Id);
        t.Run(4);
        Assert.Contains(t.Heard[Bob], e => e is Hit h && h.TargetId == station.Id || e is Destroyed d && d.Id == bomber.Id);
        Assert.DoesNotContain(t.Heard[Eve], e => e is Hit h && Allies(t.World, h.Side, Nairan));
    }

    [Fact]
    public void A_player_who_leaves_hands_its_side_to_the_AI()
    {
        var t = new Table(Battle());
        var nairan = t.World.Factions[Nairan];
        Assert.Null(nairan.AI);
        var ship = t.World.Entities.First(e => e.Side == Nairan && e.Kind == EntityKind.Ship);
        t.Order(Bob, new Command.StartPilot(ship.Id));
        t.Frame();
        Assert.NotNull(ship.Pilot);
        t.Host.Drop(Bob);
        Assert.NotNull(nairan.AI);
        Assert.True(nairan.AutoAbilities);
        Assert.Null(ship.Pilot);
        // Bob is gone: what still comes from him goes nowhere, and he gets no more snapshots.
        var sent = t.Host.Snapshots;
        t.Order(Bob, new Command.Stop([ship.Id]));
        t.Run(1);
        Assert.Equal([Eve], t.Host.Peers);
        Assert.InRange(t.Host.Snapshots - sent, 19, 21);
        // The AI plays it: it queues ships by itself.
        t.Run(20);
        Assert.True(nairan.Queue.Count > 0 || nairan.Stats.Built > 0);
    }

    [Fact]
    public void A_replica_glides_between_snapshots_and_jumps_when_it_must()
    {
        var host = Battle();
        var ship = host.Entities.First(e => e.Side == Klaed && e.Kind == EntityKind.Ship);
        Movement.IssueMove(host, [ship.Id], ship.X + 1000, ship.Y);
        Steps.Run(host, 1);
        var replica = new World();
        var motion = new ReplicaMotion();
        byte[] Shot()
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms)) Snapshot.Write(host, Klaed, w, []);
            return ms.ToArray();
        }
        void Read(double now) => motion.Read(new BinaryReader(new MemoryStream(Shot())), replica, now);
        Read(0);
        var shown = replica.Entities.Get(ship.Id)!;
        // A first snapshot: shown where it is.
        Assert.Equal((float)ship.X, (float)shown.X);
        var from = shown.X;
        Steps.Run(host, 3 * Steps.Step);
        Read(1 / Protocol.Rate);
        // Read: still where it was shown; halfway through the interval, halfway there; then there.
        Assert.Equal(from, shown.X);
        motion.Advance(replica, motion.Interval / 2);
        Assert.Equal((from + (float)ship.X) / 2, shown.X, 3);
        motion.Advance(replica, motion.Interval);
        Assert.Equal((float)ship.X, (float)shown.X);
        // Teleported far away: it does not fly there.
        ship.X += 2000;
        Read(2 / Protocol.Rate);
        Assert.Equal((float)ship.X, (float)shown.X);
    }

    [Fact]
    public void What_a_host_sends_each_player_in_a_duel_and_in_a_huge_battle()
    {
        // A duel between AIs at five minutes, Bob playing the Nairan.
        var duel = Scenario.CreateSkirmish(new SkirmishOptions { Player = Faction.Klaed, Enemies = [Faction.Nairan], EnemyAI = true, Seed = 1 });
        duel.Factions[Klaed].AI = NewAI();
        Steps.Run(duel, 300);
        duel.Events.Clear();
        var small = Measure(duel, Nairan);
        // The stress battle, fleets engaged.
        var stress = Stress.Create();
        Steps.Run(stress, 10);
        stress.Events.Clear();
        var big = Measure(stress, Nairan);
        output.WriteLine($"Duel at 5 min ({duel.Entities.Count} entities): {small.Rate:0.0} snapshots/s, {small.Bytes / 1024:0.0} KiB/s per player, {small.Ms:0.000} ms per snapshot");
        output.WriteLine($"Stress ({stress.Entities.Count} entities): {big.Rate:0.0} snapshots/s, {big.Bytes / 1024:0.0} KiB/s per player, {big.Ms:0.000} ms per snapshot");
        // A duel gets every snapshot; a huge battle, as many as the budget allows.
        Assert.InRange(small.Rate, 19, 21);
        Expect.Less(small.Bytes, 100 * 1024);
        Expect.Less(big.Rate, 8);
        Expect.Less(big.Bytes, BattleHost.Budget * 1.1);
    }

    /// <summary>Five seconds of a battle served to one player: the snapshots a second it gets, their bytes a second, the time each takes to write.</summary>
    static (double Rate, double Bytes, double Ms) Measure(World w, Side player)
    {
        long bytes = 0;
        var host = new BattleHost(w, (_, m) => bytes += Protocol.Encode(m).Length);
        host.Add(Bob, player);
        void Frame()
        {
            Steps.StepWorld(w);
            host.Collect();
            w.Events.Clear();
            host.Tick(Steps.Step);
        }
        // Warm: the first snapshots pay for the JIT.
        for (var i = 0; i < 60; i++) Frame();
        (bytes, var ms, var count) = (0, host.WriteMs, host.Snapshots);
        const double Seconds = 5;
        for (var i = Js.Round(Seconds / Steps.Step); i > 0; i--) Frame();
        return ((host.Snapshots - count) / Seconds, bytes / Seconds, (host.WriteMs - ms) / (host.Snapshots - count));
    }
}
