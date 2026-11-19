// The fleet cap of campaign battles (not in the web version): each side fields so many fleet points
// at once, its ships and those in hyperspace; the rest waits in its reserve and comes in as room
// frees up. The attacker comes in by hyperspace from the start, the defender starts placed. What
// never came in goes back to the galaxy as it was. Skirmishes and modes have no cap.
//   FLEETCAP=1 dotnet test Sim.Tests -c Release --filter Measures_the_fleet_cap --logger "console;verbosity=detailed"
using System.Diagnostics;
using SandboxShip.Sim.Data;
using SandboxShip.Sim.Net;
using Xunit;
using Xunit.Abstractions;
using static SandboxShip.Sim.Galaxies;

namespace SandboxShip.Sim.Tests;

public class FleetCapTests(ITestOutputHelper output)
{
    static readonly Side A = Lobby.SideOf(0), D = Lobby.SideOf(1);
    const double AutoDt = 1.0 / 30;

    /// <summary>40 points of battlecruisers, 80 of frigates, 40 of fighters: 160 points.</summary>
    static ShipCounts Mixed() => new() { [ShipClass.Battlecruiser] = 10, [ShipClass.Frigate] = 40, [ShipClass.Fighter] = 40 };

    static World Fight(ShipCounts attackers, ShipCounts defenders, int cap, double condition = 1, RankCounts? ranks = null, List<HeroId>? heroes = null) =>
        Battle.CreatePlanetBattle(
            new BattleSide { Side = A, Race = Faction.Klaed, Ships = attackers, Condition = condition, Ranks = ranks ?? [], Heroes = heroes ?? [] },
            new BattleSide { Side = D, Race = Faction.Nairan, Ships = defenders }, PlanetDefense.None, 5, fleetCap: cap);

    static List<Entity> ShipsOf(World w, Side side) => [.. w.Entities.Where(e => e.Kind == EntityKind.Ship && e.Side == side && !e.Launched)];

    static int Points(IEnumerable<ShipClass> classes) => classes.Sum(Reserves.Points);

    [Fact]
    public void Points_go_by_size_and_weight_and_any_ship_fits_under_the_lowest_cap()
    {
        Assert.Equal(1, Reserves.Points(ShipClass.Fighter));
        Assert.Equal(1, Reserves.Points(ShipClass.Corvette));
        Assert.Equal(2, Reserves.Points(ShipClass.Frigate));
        Assert.Equal(2, Reserves.Points(ShipClass.Artillery));
        Assert.Equal(4, Reserves.Points(ShipClass.Battlecruiser));
        Assert.Equal(6, Reserves.Points(ShipClass.Dreadnought));
        Assert.Equal(12, Reserves.Points(ShipClass.Titan));
        Assert.Equal(20, Reserves.Points(ShipClass.Colossus));
        Assert.True(Reserves.CapPoints(FleetCap.Low) < Reserves.CapPoints(FleetCap.Normal));
        Assert.True(Reserves.CapPoints(FleetCap.Normal) < Reserves.CapPoints(FleetCap.High));
        Assert.All(Ships.All, c => Assert.True(Reserves.Points(c) <= Reserves.CapPoints(FleetCap.Low)));
    }

    [Fact]
    public void The_defender_starts_placed_up_to_the_cap_and_the_attacker_comes_in_by_hyperspace()
    {
        var w = Fight(Mixed(), Mixed(), 60);
        Assert.Equal(60, w.FleetCap);
        // The defender: as much of its fleet as fits, every class in it, the rest in reserve.
        var placed = ShipsOf(w, D);
        Assert.Equal(60, Reserves.Used(w, D));
        Assert.Equal((3, 16, 16), (placed.Count(e => e.Cls == ShipClass.Battlecruiser), placed.Count(e => e.Cls == ShipClass.Frigate), placed.Count(e => e.Cls == ShipClass.Fighter)));
        Assert.Equal(160 - 60, Points(w.Factions[D].Reserve.Select(s => s.Cls)));
        // The attacker: nothing on the field yet, as much in hyperspace at its end of the map.
        Assert.Empty(ShipsOf(w, A));
        Assert.Equal(60, Points(w.Jumps.Where(j => j.Side == A).Select(j => j.Cls)));
        Assert.All(w.Jumps, j => Assert.True(j.X > Battle.Width / 2));
        Assert.Equal(new Point(Battle.Width - 500, Battle.Height / 2), w.Factions[A].Arrival);
        Assert.Equal(Battle.Survivors(w, A), Mixed());
        // In hyperspace, it has not lost.
        Assert.Null(Battle.Outcome(w, A, D));
        Steps.Run(w, Reserves.JumpTimeOf(ShipClass.Battlecruiser) + 0.1);
        Assert.Empty(w.Jumps);
        Assert.Equal(60, Reserves.Used(w, A));
        Assert.Equal(60, Points(ShipsOf(w, A).Select(e => e.Cls)));
    }

    [Fact]
    public void Without_a_cap_a_planet_battle_is_as_it_was()
    {
        var w = Fight(Mixed(), Mixed(), 0);
        Assert.Equal(160, Reserves.Used(w, A));
        Assert.Equal(160, Reserves.Used(w, D));
        Assert.Empty(w.Jumps);
        Assert.Empty(w.Factions[A].Reserve);
        Assert.Null(w.Factions[A].Arrival);
        Assert.Equal(int.MaxValue, Reserves.Room(w, A));
    }

    [Fact]
    public void A_ship_waits_for_room_under_the_cap_and_is_told_why()
    {
        var w = Fight(Mixed(), Mixed(), 60);
        var f = w.Factions[D];
        var (x, y) = (f.Arrival!.Value.X, f.Arrival!.Value.Y - 300);
        var frigate = new ReserveShip(ShipClass.Frigate);
        Assert.Equal(Result.Fail("Plafond de flotte atteint"), Reserves.CanDeploy(w, D, frigate, x, y));
        // The command goes through the same guard.
        Assert.Equal("Plafond de flotte atteint", Commands.Apply(w, D, new Command.Deploy(ShipClass.Frigate, null, x, y)).Reason);
        // Two fighters lost: room for one frigate, or for two of a squadron of four.
        foreach (var e in ShipsOf(w, D).Where(e => e.Cls == ShipClass.Fighter).Take(2).ToList()) w.Entities.Remove(e.Id);
        Assert.True(Reserves.CanDeploy(w, D, frigate, x, y).Ok);
        Assert.True(Reserves.Deploy(w, D, new ReserveShip(ShipClass.Fighter), x, y).Ok);
        Assert.Equal(2, w.Jumps.Count(j => j.Side == D));
        Assert.Equal(60, Reserves.Used(w, D));
        Assert.False(Reserves.CanDeploy(w, D, new ReserveShip(ShipClass.Fighter), x, y + 200).Ok);
    }

    [Fact]
    public void A_side_comes_in_at_its_end_of_the_map_even_with_nothing_there()
    {
        var w = Fight(Mixed(), [], 60);
        var f = w.Factions[A];
        var entry = f.Arrival!.Value;
        // Its whole opening force is still in hyperspace: no ship of its own to come in by.
        w.Jumps.Clear();
        f.Reserve.Add(new ReserveShip(ShipClass.Frigate));
        Assert.True(Reserves.CanDeploy(w, A, new ReserveShip(ShipClass.Frigate), entry.X - 300, entry.Y + 200).Ok);
        Assert.Equal(Result.Fail("Trop loin de tes forces"), Reserves.CanDeploy(w, A, new ReserveShip(ShipClass.Frigate), Battle.Width / 2, entry.Y));
    }

    [Fact]
    public void A_side_with_a_reserve_has_not_lost_and_calls_it_in_once_its_field_is_empty()
    {
        var w = Fight(new ShipCounts { [ShipClass.Frigate] = 4 }, Mixed(), 60);
        Steps.Run(w, 3);
        // The defender loses every ship on the field: its reserve is still there.
        foreach (var e in ShipsOf(w, D)) w.Entities.Remove(e.Id);
        Assert.Null(Battle.Outcome(w, A, D));
        Steps.Run(w, 1.1);
        Assert.True(Reserves.Used(w, D) > 0);
        Assert.InRange(Reserves.Used(w, D), 1, 60);
        Assert.Null(Battle.Outcome(w, A, D));
        // With nothing anywhere, it has.
        w.Jumps.Clear();
        w.Factions[D].Reserve.Clear();
        foreach (var e in ShipsOf(w, D)) w.Entities.Remove(e.Id);
        Assert.Equal(BattleResult.Attacker, Battle.Outcome(w, A, D));
    }

    [Fact]
    public void What_never_came_in_goes_back_as_it_was_ranks_wear_and_heroes()
    {
        var hero = Heroes.Of(Faction.Klaed).First(h => h.Base == ShipClass.Scout).Id;
        var ranks = new RankCounts();
        ranks.Add(ShipClass.Frigate, 3, 2);
        ranks.Add(ShipClass.Frigate, 1, 30);
        var w = Fight(Mixed(), Mixed(), 60, condition: 0.5, ranks: ranks, heroes: [hero]);
        // Nothing fought yet: the fleet's ships, its wear, its veterans and its hero, all of them.
        Assert.Equal(Mixed(), Battle.Survivors(w, A));
        Assert.Equal(0.5, Battle.SurvivorCondition(w, A), 9);
        Assert.Equal([hero], Battle.HeroSurvivors(w, A));
        var back = Veterans.Survivors(w, A);
        Assert.Equal((30, 2), (back.Of(ShipClass.Frigate, 1), back.Of(ShipClass.Frigate, 3)));
        // Out of hyperspace, the opening frigates take the best ranks and the fleet's wear.
        Steps.Run(w, Reserves.JumpTimeOf(ShipClass.Battlecruiser) + 0.1);
        var frigates = ShipsOf(w, A).Where(e => e.Cls == ShipClass.Frigate).ToList();
        Assert.Equal(2, frigates.Count(e => e.Rank == 3));
        Assert.All(frigates, e => Assert.Equal(0.5, e.Hull / e.MaxHull, 9));
        Assert.True(ShipsOf(w, A).Single(e => e.Hero == hero).Hull > 0.99 * ShipsOf(w, A).Single(e => e.Hero == hero).MaxHull);
        back = Veterans.Survivors(w, A);
        Assert.Equal((30, 2), (back.Of(ShipClass.Frigate, 1), back.Of(ShipClass.Frigate, 3)));
        Assert.Equal(Mixed(), Battle.Survivors(w, A));
        Assert.Equal(0.5, Battle.SurvivorCondition(w, A), 9);
    }

    [Fact]
    public void A_fleet_waiting_weighs_as_much_as_on_the_field()
    {
        foreach (var condition in new[] { 1.0, 0.6 })
        {
            var open = Fight(Mixed(), Mixed(), 0, condition);
            var capped = Fight(Mixed(), Mixed(), 60, condition);
            Assert.Equal(Battle.Power(open, A, D), Battle.Power(capped, A, D), Battle.Power(open, A, D) * 1e-9);
            Assert.Equal(Battle.Power(open, D, A), Battle.Power(capped, D, A), Battle.Power(open, D, A) * 1e-9);
        }
    }

    [Fact]
    public void The_battle_ai_brings_its_reserve_in_as_room_frees_up_never_past_the_cap()
    {
        var w = Fight(Mixed(), Mixed(), 60);
        var called = false;
        for (var t = 0.0; t < 120 && Battle.Outcome(w, A, D) is null; t += AutoDt)
        {
            var before = w.Factions[A].Reserve.Count + w.Factions[D].Reserve.Count;
            Steps.StepWorld(w, AutoDt);
            Battle.UpdateBattleAI(w, [A, D], AutoDt);
            Assert.InRange(Reserves.Used(w, A), 0, 60);
            Assert.InRange(Reserves.Used(w, D), 0, 60);
            called |= w.Factions[A].Reserve.Count + w.Factions[D].Reserve.Count < before;
        }
        Assert.True(called);
    }

    /// <summary>A new campaign of the lobby's, at war at once, nobody thinking: a fleet of `ships` attacks a capital, the battle resolving on its own.</summary>
    static (Galaxy G, GalaxyBattle B) Attacked(Lobby lobby, ShipCounts ships)
    {
        var g = CreateGalaxy(lobby.Slots(), 4, errants: false, incidents: false, setup: lobby.Setup());
        g.Player = (Faction)99;
        foreach (var f in g.Factions.Values) f.AI = null;
        g.Truces.Clear();
        var home = g.Planets.First(p => p.Owner == A);
        AddShipsAt(g, D, home.Id, ships)!.ArrivedAt = 1;
        StepGalaxy(g);
        return (g, Assert.Single(g.Battles));
    }

    [Fact]
    public void A_campaign_keeps_its_fleet_cap_in_its_battles_its_saves_and_its_lobby()
    {
        var lobby = Lobby.Create(LobbyMode.Campaign, 3);
        lobby.FleetCap = FleetCap.Low;
        Assert.True(lobby.Sane());
        var (g, b) = Attacked(lobby, Mixed());
        Assert.Equal(FleetCap.Low, g.FleetCap);
        Assert.Equal(Reserves.CapPoints(FleetCap.Low), b.World.FleetCap);
        var text = GalaxySave.Serialize(g);
        Assert.Equal(FleetCap.Low, GalaxySave.Deserialize(text)!.FleetCap);
        // A save from before the cap: normal.
        Assert.Contains("\"fleetCap\":\"low\"", text);
        Assert.Equal(FleetCap.Normal, GalaxySave.Deserialize(text.Replace(",\"fleetCap\":\"low\"", ""))!.FleetCap);
        // An unknown cap is a broken save, and a broken lobby.
        Assert.Null(GalaxySave.Deserialize(text.Replace("\"fleetCap\":\"low\"", "\"fleetCap\":\"huge\"")));
        lobby.FleetCap = (FleetCap)7;
        Assert.False(lobby.Sane());
        Assert.False(LobbySettings.Of(lobby).Sane);
        // A galaxy made without a setup, as the tests make them: normal.
        Assert.Equal(FleetCap.Normal, CreateGalaxy(Faction.Klaed, [Faction.Nairan]).FleetCap);
    }

    [Fact]
    public void A_battle_resolving_on_its_own_goes_on_past_its_time_while_reserves_wait()
    {
        var lobby = Lobby.Create(LobbyMode.Campaign, 3);
        lobby.FleetCap = FleetCap.Low;
        var (g, b) = Attacked(lobby, new ShipCounts { [ShipClass.Frigate] = 120 });
        Assert.NotEmpty(b.World.Factions[D].Reserve);
        b.Elapsed = GalaxyRules.AutoTimeout + 1;
        StepGalaxy(g);
        Assert.Contains(b, g.Battles);
        // Nothing left to come in: it falls back as before.
        b.World.Factions[D].Reserve.Clear();
        b.World.Jumps.Clear();
        StepGalaxy(g);
        Assert.DoesNotContain(b, g.Battles);
        Assert.Equal(BattleResult.Retreat, g.Events.OfType<BattleEnded>().Last().Result);
    }

    [Fact]
    public void A_snapshot_carries_the_cap_the_arrival_and_a_long_reserve_and_refuses_forged_ones()
    {
        var w = Fight(new ShipCounts { [ShipClass.Fighter] = 3000, [ShipClass.Frigate] = 5 }, Mixed(), 60);
        byte[] Write(World world)
        {
            using var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms)) Snapshot.Write(world, A, bw);
            return ms.ToArray();
        }
        var bytes = Write(w);
        var replica = new World();
        Snapshot.Read(new BinaryReader(new MemoryStream(bytes)), replica);
        Assert.Equal(60, replica.FleetCap);
        Assert.Equal(w.Factions[A].Reserve, replica.Factions[A].Reserve);
        Assert.Equal(w.Factions[A].Arrival, replica.Factions[A].Arrival);
        // The viewer's HUD counts what the host does, and checks the same.
        Assert.Equal(Reserves.Used(w, A), Reserves.Used(replica, A));
        Assert.Equal(Write(replica), bytes);
        // Thousands of fighters in a few bytes: one run of a kind.
        Assert.True(bytes.Length < 4000, $"{bytes.Length} octets");
        // A cap past any, a run of no ship, a reserve past any.
        foreach (var (cap, reserve, empty) in new[] { (Reserves.MaxCap + 1, 1, false), (60, 1, true), (60, Reserves.MaxReserve + 1, false) })
        {
            var forged = Fight(new ShipCounts { [ShipClass.Frigate] = 4 }, [], cap);
            forged.Factions[A].Reserve.Clear();
            for (var i = 0; i < reserve; i++) forged.Factions[A].Reserve.Add(new ReserveShip(ShipClass.Stealth));
            var data = Write(forged);
            if (empty) data[RunCount(data, forged.Factions[A].Arrival!.Value)] = 0;
            Assert.Throws<InvalidDataException>(() => Snapshot.Read(new BinaryReader(new MemoryStream(data)), new World()));
        }
    }

    /// <summary>
    /// Where the count of a reserve's only run sits in a snapshot: one run, a stealth cruiser, no
    /// hero, one of it, then the arrival point.
    /// </summary>
    static int RunCount(byte[] data, Point arrival)
    {
        byte[] run = [1, (byte)ShipClass.Stealth, 0, 1, 1, .. BitConverter.GetBytes((float)arrival.X), .. BitConverter.GetBytes((float)arrival.Y)];
        for (var i = 0; i + run.Length <= data.Length; i++)
        {
            if (data.AsSpan(i, run.Length).SequenceEqual(run)) return i + 3;
        }
        throw new InvalidOperationException("Pas de réserve dans l'instantané");
    }

    /// <summary>
    /// The measure behind the three caps (PLAN.md): two fleets of more than a thousand ships over a
    /// fortified planet, each cap in turn, then none; entities on the field and milliseconds a step.
    /// </summary>
    [Fact]
    public void Measures_the_fleet_cap()
    {
        if (Environment.GetEnvironmentVariable("FLEETCAP") is null) return;
        var seconds = double.Parse(Environment.GetEnvironmentVariable("FLEETCAP_SECONDS") ?? "90", System.Globalization.CultureInfo.InvariantCulture);
        foreach (var cap in new[] { FleetCap.Low, FleetCap.Normal, FleetCap.High }.Select(Reserves.CapPoints).Append(0))
        {
            var w = Stress.Planet(7, cap);
            var steps = new List<double>();
            var entities = new List<int>();
            var clock = new Stopwatch();
            for (var t = 0.0; t < seconds && Battle.Outcome(w, A, D) is null; t += Steps.Step)
            {
                clock.Restart();
                Steps.StepWorld(w);
                Battle.UpdateBattleAI(w, [A, D], Steps.Step);
                steps.Add(clock.Elapsed.TotalMilliseconds);
                entities.Add(w.Entities.Count);
                w.Events.Clear();
            }
            // The first seconds pay for the JIT and the jumps: measured past them.
            var settled = steps.Skip(300).Order().ToList();
            var fielded = entities.Skip(300).ToList();
            output.WriteLine($"plafond {(cap == 0 ? "aucun" : cap.ToString())} : {fielded.Average():0} entités en moyenne ({fielded.Max()} au plus), "
                + $"{settled.Average():0.00} ms par pas (médiane {settled[settled.Count / 2]:0.00}, 95e centile {settled[settled.Count * 95 / 100]:0.00}), "
                + $"{w.Time:0} s, réserves {w.Factions[A].Reserve.Count} et {w.Factions[D].Reserve.Count}");
            // Then to the end, as the galaxy resolves a battle on its own: how long it takes, and how it ends.
            var auto = Stress.Planet(7, cap);
            BattleResult? result = null;
            for (var t = 0.0; result is null && t < 1800; t += AutoDt)
            {
                Steps.StepWorld(auto, AutoDt);
                Battle.UpdateBattleAI(auto, [A, D], AutoDt);
                auto.Events.Clear();
                result = Battle.Outcome(auto, A, D);
            }
            output.WriteLine($"  résolue seule : {result?.ToString() ?? "pas d'issue"} à {auto.Time:0} s (la galaxie se replie à {GalaxyRules.AutoTimeout:0} s), "
                + $"survivants {Battle.CountShips(Battle.Survivors(auto, A))} et {Battle.CountShips(Battle.Survivors(auto, D))}");
        }
    }
}
