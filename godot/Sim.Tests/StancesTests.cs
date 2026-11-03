// Stances and fleet orders, rules of this version only: how a ship fights between orders
// (aggressive, defensive, holding, passive), patrols, guards, and orders queued with Shift; their
// commands, on the wire and in a replay, and what a player's replica sees of them.
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.Movement;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim.Tests;

public class StancesTests
{
    static readonly Side Klaed = Faction.Klaed, Nairan = Faction.Nairan;

    static World NewWorld()
    {
        var w = CreateWorld(4000, 4000);
        AddFaction(w, Faction.Klaed, 0);
        AddFaction(w, Faction.Nairan, 0);
        return w;
    }

    /// <summary>Steps `seconds`, calling `each` after every step; the events dropped.</summary>
    static void Run(World w, double seconds, Action? each = null)
    {
        for (var i = Js.Round(seconds / Steps.Step); i > 0; i--)
        {
            Steps.StepWorld(w);
            each?.Invoke();
            w.Events.Clear();
        }
    }

    /// <summary>A foe that stays where it is and does not shoot: something to look at.</summary>
    static Entity Dummy(World w, double x, double y)
    {
        var foe = SpawnShip(w, Nairan, ShipClass.Frigate, x, y);
        foe.Stance = Stance.Passive;
        foe.Order = Order.Move(x, y);
        return foe;
    }

    // Stances.

    [Fact]
    public void A_ship_holding_its_place_does_not_go_for_a_foe_out_of_reach_where_a_defensive_one_does()
    {
        var w = NewWorld();
        var holding = SpawnShip(w, Klaed, ShipClass.Frigate, 1000, 1000);
        var defensive = SpawnShip(w, Klaed, ShipClass.Frigate, 1000, 3000);
        holding.Stance = Stance.Hold;
        // Out of reach, but well within sight of a ship that goes for what comes near.
        var gap = holding.Range + 100;
        Dummy(w, 1000 + gap, 1000);
        Dummy(w, 1000 + gap, 3000);
        Run(w, 10);
        Expect.Less(Hypot(holding.X - 1000, holding.Y - 1000), 5);
        Expect.Greater(defensive.X, 1050);
    }

    [Fact]
    public void A_ship_holding_its_place_still_shoots_what_comes_within_reach()
    {
        var w = NewWorld();
        var holding = SpawnShip(w, Klaed, ShipClass.Frigate, 1000, 1000);
        holding.Stance = Stance.Hold;
        var foe = Dummy(w, 1000 + holding.Range * 0.7, 1000);
        var hull = foe.Hull + foe.Shield;
        Run(w, 5);
        Expect.Less(foe.Hull + foe.Shield, hull);
        Expect.Less(Hypot(holding.X - 1000, holding.Y - 1000), 5);
    }

    [Fact]
    public void A_passive_ship_never_fires_unless_ordered_to_attack()
    {
        var w = NewWorld();
        var scout = SpawnShip(w, Klaed, ShipClass.Frigate, 1000, 1000);
        scout.Stance = Stance.Passive;
        var foe = Dummy(w, 1000 + scout.Range * 0.5, 1000);
        var hull = foe.Hull + foe.Shield;
        Run(w, 5);
        Assert.Null(scout.TargetId);
        Assert.Equal(hull, foe.Hull + foe.Shield);
        Assert.True(Commands.Apply(w, Klaed, new Command.Attack([scout.Id], foe.Id)).Ok);
        Run(w, 5);
        Expect.Less(foe.Hull + foe.Shield, hull);
    }

    [Fact]
    public void An_aggressive_ship_goes_for_a_foe_further_than_a_defensive_one_and_stays_where_the_fight_took_it()
    {
        var w = NewWorld();
        var aggressive = SpawnShip(w, Klaed, ShipClass.Frigate, 1000, 1000);
        var defensive = SpawnShip(w, Klaed, ShipClass.Frigate, 1000, 3000);
        aggressive.Stance = Stance.Aggressive;
        // Past what a defensive ship looks for, within what an aggressive one does.
        var gap = Math.Max(aggressive.Range * 1.6, 350) + 60;
        Assert.True(gap < Math.Max(aggressive.Range * 2.2, 550));
        Dummy(w, 1000 + gap, 1000);
        Dummy(w, 1000 + gap, 3000);
        Run(w, 15);
        Expect.Greater(aggressive.X, 1100);
        Expect.Less(Hypot(defensive.X - 1000, defensive.Y - 3000), 5);
        // Its post moved with it.
        Expect.Less(Hypot(aggressive.GuardX - aggressive.X, aggressive.GuardY - aggressive.Y), 40);
    }

    [Fact]
    public void A_stance_is_set_by_command_on_own_ships_only_and_known_values_only()
    {
        var w = NewWorld();
        var own = SpawnShip(w, Klaed, ShipClass.Frigate, 1000, 1000);
        var other = SpawnShip(w, Nairan, ShipClass.Frigate, 2000, 1000);
        Assert.True(Commands.Apply(w, Klaed, new Command.SetStance([own.Id], Stance.Hold)).Ok);
        Assert.Equal(Stance.Hold, own.Stance);
        Assert.False(Commands.Apply(w, Klaed, new Command.SetStance([other.Id], Stance.Passive)).Ok);
        Assert.False(Commands.Apply(w, Klaed, new Command.SetStance([own.Id], (Stance)42)).Ok);
        Assert.Equal((Stance.Hold, Stance.Defensive), (own.Stance, other.Stance));
    }

    // Patrols.

    [Fact]
    public void A_patrol_goes_to_its_point_and_back_for_good()
    {
        var w = NewWorld();
        var s = SpawnShip(w, Klaed, ShipClass.Frigate, 500, 500);
        Assert.True(Commands.Apply(w, Klaed, new Command.Patrol([s.Id], [new Point(1500, 500)])).Ok);
        var (there, back) = (false, false);
        Run(w, 90, () =>
        {
            there |= Hypot(s.X - 1500, s.Y - 500) < 20;
            back |= there && Hypot(s.X - 500, s.Y - 500) < 20;
        });
        Assert.True(there && back);
        Assert.Equal(OrderType.Patrol, s.Order?.Type);
    }

    [Fact]
    public void A_patrol_fights_what_it_meets_then_goes_on()
    {
        var w = NewWorld();
        var s = SpawnShip(w, Klaed, ShipClass.Battlecruiser, 500, 500);
        IssuePatrol(w, [s.Id], [new Point(3000, 500)]);
        var foe = SpawnShip(w, Nairan, ShipClass.Scout, 1500, 520);
        foe.Stance = Stance.Hold;
        Run(w, 60);
        Assert.False(w.Entities.Has(foe.Id));
        Assert.Equal(OrderType.Patrol, s.Order?.Type);
    }

    [Fact]
    public void A_patrol_across_a_belt_goes_round_it_rather_than_sticking_to_the_rocks()
    {
        var w = NewWorld();
        w.Terrain.Add(new TerrainPatch(1, TerrainKind.Belt, 2000, 1300, 70) { X2 = 2000, Y2 = 2700 });
        var s = SpawnShip(w, Klaed, ShipClass.Frigate, 1400, 2000);
        IssuePatrol(w, [s.Id], [new Point(2600, 2000)]);
        var (there, back) = (false, false);
        Run(w, 240, () =>
        {
            there |= Hypot(s.X - 2600, s.Y - 2000) < 30;
            back |= there && Hypot(s.X - 1400, s.Y - 2000) < 30;
        });
        Assert.True(there && back);
    }

    [Fact]
    public void A_patrol_is_refused_without_points_with_too_many_or_with_one_off_any_map()
    {
        var w = NewWorld();
        var s = SpawnShip(w, Klaed, ShipClass.Frigate, 500, 500);
        Assert.False(Commands.Apply(w, Klaed, new Command.Patrol([s.Id], [])).Ok);
        Assert.False(Commands.Apply(w, Klaed, new Command.Patrol([s.Id], [.. Enumerable.Repeat(new Point(1, 1), MaxRoute)])).Ok);
        Assert.False(Commands.Apply(w, Klaed, new Command.Patrol([s.Id], [new Point(double.NaN, 3)])).Ok);
        Assert.Null(s.Order);
        Assert.True(Commands.Apply(w, Klaed, new Command.Patrol([s.Id], [.. Enumerable.Repeat(new Point(1, 1), MaxRoute - 1)])).Ok);
        Assert.Equal(MaxRoute, s.Order!.Route!.Count);
    }

    // Guards.

    [Fact]
    public void A_guard_follows_its_ward_and_stops_when_it_falls()
    {
        var w = NewWorld();
        var ward = SpawnShip(w, Klaed, ShipClass.Battlecruiser, 1000, 1000);
        var guard = SpawnShip(w, Klaed, ShipClass.Frigate, 700, 1000);
        Assert.True(Commands.Apply(w, Klaed, new Command.Guard([guard.Id], ward.Id)).Ok);
        IssueMove(w, [ward.Id], 2500, 2500);
        Run(w, 60);
        Expect.Less(Dist(guard, ward), ward.Radius + 150);
        ward.Hull = 0;
        Run(w, 1);
        Assert.Null(guard.Order);
    }

    [Fact]
    public void A_guard_goes_for_what_comes_at_its_ward()
    {
        var w = NewWorld();
        var ward = SpawnShip(w, Klaed, ShipClass.Battlecruiser, 1000, 1000);
        ward.Stance = Stance.Passive;
        var guard = SpawnShip(w, Klaed, ShipClass.Frigate, 700, 1000);
        IssueGuard(w, [guard.Id], ward.Id);
        var raider = SpawnShip(w, Nairan, ShipClass.Scout, 1400, 1000);
        IssueAttack(w, [raider.Id], ward.Id);
        Run(w, 40);
        Assert.False(w.Entities.Has(raider.Id));
    }

    [Fact]
    public void Only_an_own_or_allied_ward_may_be_guarded_and_never_by_itself()
    {
        var w = NewWorld();
        var own = SpawnShip(w, Klaed, ShipClass.Frigate, 1000, 1000);
        var foe = SpawnShip(w, Nairan, ShipClass.Frigate, 2000, 1000);
        Assert.False(Commands.Apply(w, Klaed, new Command.Guard([own.Id], foe.Id)).Ok);
        Assert.False(Commands.Apply(w, Klaed, new Command.Guard([own.Id], 9999)).Ok);
        Assert.True(Commands.Apply(w, Klaed, new Command.Guard([own.Id], own.Id)).Ok);
        Assert.Null(own.Order);
        // A station is guarded as well, through its parts too.
        var station = SpawnStation(w, Klaed, 500, 3000);
        var part = ChildrenOf(w, station.Id).First();
        IssueGuard(w, [own.Id], part.Id);
        Assert.Equal((OrderType.Guard, station.Id), (own.Order!.Type, own.Order.TargetId));
    }

    [Fact]
    public void A_ship_does_not_guard_the_one_that_guards_it()
    {
        var w = NewWorld();
        var a = SpawnShip(w, Klaed, ShipClass.Frigate, 1000, 1000);
        var b = SpawnShip(w, Klaed, ShipClass.Frigate, 1200, 1000);
        IssueGuard(w, [a.Id], b.Id);
        IssueGuard(w, [b.Id], a.Id);
        Assert.Equal(OrderType.Guard, a.Order?.Type);
        Assert.Null(b.Order);
    }

    // Orders queued.

    [Fact]
    public void Orders_queued_start_one_after_the_other()
    {
        var w = NewWorld();
        var s = SpawnShip(w, Klaed, ShipClass.Frigate, 500, 500);
        Assert.True(Commands.Apply(w, Klaed, new Command.Move([s.Id], 1500, 500)).Ok);
        Assert.True(Commands.Apply(w, Klaed, new Command.Move([s.Id], 1500, 1500, Queued: true)).Ok);
        Assert.True(Commands.Apply(w, Klaed, new Command.Move([s.Id], 500, 1500, Queued: true)).Ok);
        Assert.Equal(2, s.Queue!.Count);
        var passed = false;
        Run(w, 90, () => passed |= Hypot(s.X - 1500, s.Y - 1500) < 20);
        Assert.True(passed);
        Assert.Null(s.Order);
        Expect.Less(Hypot(s.X - 500, s.Y - 1500), 20);
    }

    [Fact]
    public void An_order_not_queued_or_a_stop_drops_the_queue_and_a_full_queue_takes_no_more()
    {
        var w = NewWorld();
        var s = SpawnShip(w, Klaed, ShipClass.Frigate, 500, 500);
        IssueMove(w, [s.Id], 1500, 500);
        for (var i = 0; i < MaxQueue + 5; i++) IssueMove(w, [s.Id], 600 + i, 900, queued: true);
        Assert.Equal(MaxQueue, s.Queue!.Count);
        IssueMove(w, [s.Id], 800, 800);
        Assert.Empty(s.Queue);
        IssueMove(w, [s.Id], 900, 900, queued: true);
        IssueStop(w, [s.Id]);
        Assert.Null(s.Order);
        Assert.Empty(s.Queue);
        // Queued with nothing to wait for, an order starts at once.
        IssueAttack(w, [s.Id], SpawnShip(w, Nairan, ShipClass.Scout, 900, 500).Id, queued: true);
        Assert.Equal(OrderType.Attack, s.Order?.Type);
    }

    [Fact]
    public void A_patrol_queued_after_a_move_starts_from_where_the_move_ends()
    {
        var w = NewWorld();
        var s = SpawnShip(w, Klaed, ShipClass.Frigate, 500, 500);
        IssueMove(w, [s.Id], 2000, 500);
        IssuePatrol(w, [s.Id], [new Point(2000, 1500)], queued: true);
        var patrol = Assert.Single(s.Queue!);
        Expect.Less(Hypot(patrol.Route![0].X - 2000, patrol.Route[0].Y - 500), 1);
    }

    // On the wire, in a replay and on a replica.

    static byte[] Bytes(Command c)
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms)) Commands.Write(bw, c);
        return ms.ToArray();
    }

    [Fact]
    public void The_new_commands_read_back_as_written_and_the_old_ones_keep_their_bytes()
    {
        Command[] commands =
        [
            new Command.Move([1, 2], 10.5, 20.25, true, Queued: true), new Command.Attack([3], 4, Queued: true),
            new Command.Patrol([5, 6], [new Point(1, 2), new Point(3.5, 4)], true), new Command.Guard([7], 8, true),
            new Command.SetStance([9, 10], Stance.Passive),
        ];
        foreach (var c in commands)
        {
            var bytes = Bytes(c);
            Assert.Equal(bytes, Bytes(Commands.ReadCommand(new BinaryReader(new MemoryStream(bytes)))));
        }
        Assert.Equal(1, Bytes(new Command.Move([1], 2, 3))[0]);
        Assert.Equal(2, Bytes(new Command.Attack([1], 2))[0]);
        // A patrol of more points than any takes is refused before anything is made of them.
        var forged = Bytes(new Command.Patrol([1], [new Point(1, 2)]));
        forged[3] = 0x7f;
        Assert.Throws<InvalidDataException>(() => Commands.ReadCommand(new BinaryReader(new MemoryStream(forged))));
        var stance = Bytes(new Command.SetStance([1], Stance.Hold));
        stance[^1] = 9;
        Assert.Throws<InvalidDataException>(() => Commands.ReadCommand(new BinaryReader(new MemoryStream(stance))));
    }

    [Fact]
    public void A_battle_with_stances_patrols_guards_and_orders_queued_plays_again_exactly()
    {
        var o = new SkirmishOptions { Player = Faction.Klaed, Enemies = [Faction.Nairan], EnemyAI = true, Seed = 4, Size = MapSize.Small };
        var w = Scenario.CreateSkirmish(o);
        Assert.True(Replays.Record(w));
        int[] Ships() => [.. w.Entities.Where(e => e.Side == Klaed && e.Kind == EntityKind.Ship).Select(e => e.Id)];
        for (var i = 0; i < 3600; i++)
        {
            var ships = Ships();
            Command? c = (i, ships.Length) switch
            {
                (60, > 0) => new Command.SetStance(ships, Stance.Aggressive),
                (120, > 0) => new Command.Patrol(ships, [new Point(w.Width / 2, w.Height / 2), new Point(w.Width / 3, w.Height / 2)]),
                (1200, > 1) => new Command.Guard(ships[1..], ships[0]),
                (1800, > 0) => new Command.Move(ships, w.Width / 2, w.Height / 3, true),
                (1801, > 0) => new Command.Move(ships, w.Width / 3, w.Height / 3, Queued: true),
                (2400, > 0) => new Command.SetStance(ships, Stance.Hold),
                _ => null,
            };
            if (c is not null) Commands.Apply(w, Klaed, c);
            Steps.StepWorld(w);
            w.Events.Clear();
        }
        var replay = Replays.Of(w, Klaed, "test", 1_700_000_000_000)!;
        using var file = new MemoryStream();
        Replays.Write(file, replay);
        file.Position = 0;
        var player = new ReplayPlayer(Replays.Read(file));
        while (!player.Over) player.Step();
        Assert.True(player.Faithful);
        Assert.Equal(Replays.Digest(w), Replays.Digest(player.World));
    }

    [Fact]
    public void A_replica_sees_its_own_ships_stance_patrol_and_queue_and_only_the_rank_of_others()
    {
        var w = NewWorld();
        var own = SpawnShip(w, Klaed, ShipClass.Frigate, 500, 500);
        var foe = SpawnShip(w, Nairan, ShipClass.Frigate, 900, 500);
        own.Stance = foe.Stance = Stance.Hold;
        Veterans.Promote(w, foe, 2);
        Veterans.Promote(w, own, 1);
        own.Xp = 4.5;
        IssuePatrol(w, [own.Id], [new Point(1500, 500), new Point(1500, 1500)]);
        IssueMove(w, [own.Id], 300, 300, queued: true);
        foreach (var (viewer, ship) in new[] { (Klaed, own), (Nairan, foe) })
        {
            using var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) Snapshot.Write(w, viewer, bw, []);
            ms.Position = 0;
            var replica = new World();
            Snapshot.Read(new BinaryReader(ms), replica);
            var mine = replica.Entities.Get(ship.Id)!;
            var theirs = replica.Entities.Get(ship == own ? foe.Id : own.Id)!;
            Assert.Equal((Stance.Hold, ship.Rank, ship.Xp), (mine.Stance, mine.Rank, (double)(float)mine.Xp));
            Assert.Equal((Stance.Defensive, ship == own ? 2 : 1, 0.0), (theirs.Stance, theirs.Rank, theirs.Xp));
            Assert.Null(theirs.Order);
            if (ship != own) continue;
            Assert.Equal(OrderType.Patrol, mine.Order!.Type);
            Assert.Equal(own.Order!.Route!.Count, mine.Order.Route!.Count);
            Assert.Equal(own.Order.Leg, mine.Order.Leg);
            Assert.Equal(OrderType.Move, Assert.Single(mine.Queue!).Type);
        }
    }
}
