// Port of tests/galaxy.test.ts: the galaxy campaign, its AI, saves and generated maps.
using System.Text.Json.Nodes;
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.Galaxies;

namespace SandboxShip.Sim.Tests;

public class GalaxyTests
{
    static readonly int West = GalaxyMap.Capitals[0];
    static readonly int East = GalaxyMap.Capitals[1];

    static int Id(string name) => GalaxyMap.Planets.ToList().FindIndex(p => p.Name == name);

    /// <summary>
    /// The player watches from a faction that is not on the map: every battle resolves on its own.
    /// Without the Errants and random events, as in the web version: the planets keep the owners
    /// these tests expect.
    /// </summary>
    static Galaxy Spectated()
    {
        var g = CreateGalaxy(Faction.Klaed, [Faction.Nairan], false, errants: false, incidents: false);
        g.Player = Faction.Nautolan;
        return g;
    }

    static void SameShips(ShipCounts actual, params (ShipClass Cls, int N)[] expected)
    {
        var have = actual.Where(kv => kv.Value != 0).OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value));
        Assert.Equal(expected.OrderBy(e => e.Cls).Select(e => (e.Cls, e.N)), have);
    }

    static Fleet FirstFleet(Galaxy g, int planet, Faction f) => FleetsAt(g, planet, f)[0];

    internal static int Reachable(Galaxy g)
    {
        var seen = new HashSet<int> { 0 };
        var todo = new Stack<int>([0]);
        while (todo.Count > 0) foreach (var n in Neighbours(g, todo.Pop())) if (seen.Add(n)) todo.Push(n);
        return seen.Count;
    }

    [Fact]
    public void Connects_every_planet_with_mirrored_lanes()
    {
        var g = CreateGalaxy(Faction.Klaed, [Faction.Nairan]);
        Assert.Equal(GalaxyMap.Planets.Count, Reachable(g));
        for (var i = 0; i < GalaxyMap.Planets.Count; i++) Assert.Equal(Neighbours(g, (i + 7) % 14).Count, Neighbours(g, i).Count);
    }

    [Fact]
    public void Finds_the_shortest_route_between_planets()
    {
        var g = CreateGalaxy(Faction.Klaed, [Faction.Nairan]);
        var path = FindPath(g, West, East)!;
        Assert.Equal(East, path[^1]);
        var from = West;
        foreach (var p in path)
        {
            Assert.Contains(p, Neighbours(g, from));
            from = p;
        }
        Assert.Null(FindPath(g, West, West));
    }

    [Fact]
    public void Starts_each_side_with_a_capital_a_station_and_a_fleet()
    {
        var g = CreateGalaxy(Faction.Klaed, [Faction.Nairan]);
        Assert.Equal((Faction.Klaed, 1, 1), (g.Planets[West].Owner!.Value, g.Planets[West].Station, g.Planets[West].Mines));
        Assert.Equal((Faction.Nairan, 1, 1), (g.Planets[East].Owner!.Value, g.Planets[East].Station, g.Planets[East].Mines));
        SameShips(FirstFleet(g, West, Faction.Klaed).Ships, (ShipClass.Frigate, 2), (ShipClass.Scout, 2));
    }

    [Fact]
    public void Fleets_fly_to_a_neutral_planet_and_take_it()
    {
        var g = Spectated();
        var fleet = FirstFleet(g, West, Faction.Klaed);
        Assert.True(SendFleet(g, fleet.Id, Id("Orin")).Ok);
        RunGalaxy(g, 15);
        Assert.Equal(Faction.Klaed, g.Planets[Id("Orin")].Owner);
        Assert.Equal(Id("Orin"), fleet.At);
    }

    [Fact]
    public void Fleets_pass_through_neutral_planets_on_the_way()
    {
        var g = Spectated();
        var fleet = FirstFleet(g, West, Faction.Klaed);
        SendFleet(g, fleet.Id, Id("Nadir"));
        RunGalaxy(g, 40);
        Assert.Equal(Id("Nadir"), fleet.At);
        Assert.Equal(Faction.Klaed, g.Planets[Id("Nadir")].Owner);
        Assert.Null(g.Planets[Id("Kaldra")].Owner);
    }

    [Fact]
    public void Fleets_split_when_only_part_of_the_fleet_is_sent()
    {
        var g = Spectated();
        var fleet = FirstFleet(g, West, Faction.Klaed);
        Assert.True(SendFleet(g, fleet.Id, Id("Maru"), new ShipCounts { [ShipClass.Scout] = 1 }).Ok);
        SameShips(fleet.Ships, (ShipClass.Frigate, 2), (ShipClass.Scout, 1));
        Assert.Equal(Result.Fail("Vaisseaux insuffisants"), SendFleet(g, fleet.Id, Id("Maru"), new ShipCounts { [ShipClass.Scout] = 5 }));
        RunGalaxy(g, 15);
        SameShips(FirstFleet(g, Id("Maru"), Faction.Klaed).Ships, (ShipClass.Scout, 1));
    }

    [Fact]
    public void Fleets_merge_when_they_meet_at_a_planet()
    {
        var g = Spectated();
        AddShipsAt(g, Faction.Klaed, Id("Orin"), new ShipCounts { [ShipClass.Frigate] = 1 });
        g.Planets[Id("Orin")].Owner = Faction.Klaed;
        SendFleet(g, FirstFleet(g, West, Faction.Klaed).Id, Id("Orin"));
        RunGalaxy(g, 15);
        var here = FleetsAt(g, Id("Orin"), Faction.Klaed);
        Assert.Single(here);
        SameShips(here[0].Ships, (ShipClass.Frigate, 3), (ShipClass.Scout, 2));
    }

    [Fact]
    public void Builds_mines_stations_and_ships_and_refunds_cancelled_work()
    {
        var g = Spectated();
        var f = g.Factions[Faction.Klaed];
        f.Credits = 100000;
        var orin = Id("Orin");
        g.Planets[orin].Owner = Faction.Klaed;
        Assert.Equal(Result.Fail("Station requise"), CanBuild(g, Faction.Klaed, orin, GalaxyBuild.Ship(ShipClass.Frigate)));
        Assert.True(EnqueueBuild(g, Faction.Klaed, orin, GalaxyBuild.Of(GalaxyBuildKind.Mine)).Ok);
        Assert.True(EnqueueBuild(g, Faction.Klaed, orin, GalaxyBuild.Of(GalaxyBuildKind.Station)).Ok);
        Assert.True(EnqueueBuild(g, Faction.Klaed, orin, GalaxyBuild.Of(GalaxyBuildKind.Station)).Ok);
        // The second station order is an upgrade, priced at level 2.
        Assert.Equal(Station.Levels[1].UpgradeCost, g.Planets[orin].Buildings[2].Cost);
        var before = f.Credits;
        CancelBuild(g, orin, 2);
        Assert.Equal(before + Station.Levels[1].UpgradeCost, f.Credits);
        RunGalaxy(g, 20 + GalaxyRules.StationTime + 1);
        Assert.Equal((1, 1), (g.Planets[orin].Mines, g.Planets[orin].Station));
        Assert.True(EnqueueBuild(g, Faction.Klaed, orin, GalaxyBuild.Ship(ShipClass.Frigate)).Ok);
        RunGalaxy(g, Ships.Defs[ShipClass.Frigate].BuildTime + 1);
        SameShips(FirstFleet(g, orin, Faction.Klaed).Ships, (ShipClass.Frigate, 1));
    }

    [Fact]
    public void A_station_at_the_top_level_shows_no_upgrade_price_and_refuses_one()
    {
        var g = Spectated();
        var capital = g.Planets.Find(p => p.Owner == Faction.Klaed)!;
        capital.Station = Station.Levels.Count;
        BuildTime(g, capital, GalaxyBuild.Of(GalaxyBuildKind.Station));
        Assert.Equal(Result.Fail("Niveau maximum"), CanBuild(g, Faction.Klaed, capital.Id, GalaxyBuild.Of(GalaxyBuildKind.Station)));
    }

    [Fact]
    public void Owned_planets_and_mines_bring_income()
    {
        var g = Spectated();
        var f = g.Factions[Faction.Klaed];
        f.Credits = 0;
        RunGalaxy(g, 10);
        var capital = GalaxyMap.Planets[West];
        // The capital starts with a level 1 station, which brings a little too.
        Expect.Close((capital.Income + GalaxyRules.StationIncome + GalaxyRules.MineIncome) * 10, f.Credits, 0);
    }

    [Fact]
    public void A_strong_fleet_takes_a_weakly_held_planet()
    {
        var g = Spectated();
        var kaldra = Id("Kaldra");
        g.Planets[kaldra].Owner = Faction.Nairan;
        g.Planets[kaldra].Station = 1;
        AddShipsAt(g, Faction.Klaed, Id("Orin"), new ShipCounts { [ShipClass.Battlecruiser] = 4, [ShipClass.Frigate] = 6 });
        SendFleet(g, FirstFleet(g, Id("Orin"), Faction.Klaed).Id, kaldra);
        RunGalaxy(g, 120);
        Assert.Empty(g.Battles);
        Assert.Equal((Faction.Klaed, 0), (g.Planets[kaldra].Owner!.Value, g.Planets[kaldra].Station));
        var winners = FirstFleet(g, kaldra, Faction.Klaed);
        Expect.Greater(winners.Ships.Of(ShipClass.Battlecruiser) + winners.Ships.Of(ShipClass.Frigate), 0);
        Assert.Contains(g.Events, e => e is BattleEnded { Result: BattleResult.Attacker });
    }

    [Fact]
    public void A_fleet_that_loses_everything_leaves_the_planet_to_its_owner()
    {
        var g = Spectated();
        var kaldra = Id("Kaldra");
        g.Planets[kaldra].Owner = Faction.Nairan;
        g.Planets[kaldra].Station = 3;
        g.Planets[kaldra].Platforms = 4;
        AddShipsAt(g, Faction.Klaed, Id("Orin"), new ShipCounts { [ShipClass.Scout] = 2 });
        SendFleet(g, FirstFleet(g, Id("Orin"), Faction.Klaed).Id, kaldra);
        RunGalaxy(g, 120);
        Assert.Equal(Faction.Nairan, g.Planets[kaldra].Owner);
        Assert.DoesNotContain(g.Fleets.Values, f => f.Faction == Faction.Klaed && f.At == kaldra);
    }

    [Fact]
    public void Battles_wait_for_the_player_to_choose_then_resolve()
    {
        var g = CreateGalaxy(Faction.Klaed, [Faction.Nairan], false);
        var kaldra = Id("Kaldra");
        g.Planets[kaldra].Owner = Faction.Nairan;
        g.Planets[kaldra].Station = 1;
        SendFleet(g, FirstFleet(g, West, Faction.Klaed).Id, kaldra);
        RunGalaxy(g, 30);
        Assert.True(WaitingForPlayer(g));
        var t = g.Time;
        StepGalaxy(g);
        Assert.Equal(t, g.Time);
        ChooseBattle(g, g.Battles[0].Id, BattleStatus.Auto);
        RunGalaxy(g, 120);
        Assert.Empty(g.Battles);
    }

    [Fact]
    public void A_retreat_sends_the_attacker_back_where_it_came_from()
    {
        var g = CreateGalaxy(Faction.Klaed, [Faction.Nairan], false);
        var kaldra = Id("Kaldra");
        g.Planets[kaldra].Owner = Faction.Nairan;
        g.Planets[kaldra].Station = 1;
        var fleet = FirstFleet(g, West, Faction.Klaed);
        SendFleet(g, fleet.Id, Id("Orin"));
        RunGalaxy(g, 15);
        SendFleet(g, fleet.Id, kaldra);
        RunGalaxy(g, 15);
        var b = g.Battles[0];
        Assert.Equal(Id("Orin"), b.From);
        ConcludeBattle(g, b.Id, BattleResult.Retreat);
        RunGalaxy(g, 15);
        SameShips(FirstFleet(g, Id("Orin"), Faction.Klaed).Ships, (ShipClass.Frigate, 2), (ShipClass.Scout, 2));
        Assert.Equal(Faction.Nairan, g.Planets[kaldra].Owner);
    }

    [Fact]
    public void The_AI_expands_builds_up_and_fights_another_AI()
    {
        var g = CreateGalaxy(Faction.Klaed, [Faction.Nairan], true, 3);
        g.Factions[Faction.Klaed].AI = new GalaxyAIState { Difficulty = Difficulty.Normal };
        g.Player = Faction.Nautolan;
        RunGalaxy(g, 90);
        // Early skirmishes may favour one side: count the expansion of both together.
        Expect.GreaterOrEqual(g.Planets.Count(p => p.Owner is not null), 6);
        RunGalaxy(g, 1200);
        Assert.Contains(g.Planets, p => p.Station > 1);
        Expect.GreaterOrEqual(g.Events.Count(e => e is BattleEnded), 5);
    }

    [Fact]
    public void The_AI_beats_a_passive_opponent()
    {
        var g = CreateGalaxy(Faction.Klaed, [Faction.Nairan], true, 5);
        g.Player = Faction.Nautolan;
        for (var minute = 0; minute < 40 && g.Winner is null; minute++) RunGalaxy(g, 60);
        Assert.Equal(Faction.Nairan, g.Winner);
        Assert.True(g.Factions[Faction.Klaed].Eliminated);
        Assert.DoesNotContain(g.Fleets.Values, f => f.Faction == Faction.Klaed);
    }

    [Fact]
    public void Saves_restore_the_map_fleets_and_treasury()
    {
        var g = Spectated();
        SendFleet(g, FirstFleet(g, West, Faction.Klaed).Id, Id("Nadir"));
        RunGalaxy(g, 25);
        var back = GalaxySave.Deserialize(GalaxySave.Serialize(g))!;
        Assert.Equal(g.Time, back.Time);
        Assert.Equal(GalaxySave.Serialize(g, 0), GalaxySave.Serialize(back, 0));
        Assert.Equal(g.Factions[Faction.Klaed].Credits, back.Factions[Faction.Klaed].Credits);
        // The game goes on from there.
        RunGalaxy(back, 30);
        Assert.Equal(Faction.Klaed, back.Planets[Id("Nadir")].Owner);
    }

    [Fact]
    public void Saves_drop_battles_and_the_fleets_meet_again_once_loaded()
    {
        var g = CreateGalaxy(Faction.Klaed, [Faction.Nairan], false);
        g.Planets[Id("Kaldra")].Owner = Faction.Nairan;
        g.Planets[Id("Kaldra")].Station = 1;
        SendFleet(g, FirstFleet(g, West, Faction.Klaed).Id, Id("Kaldra"));
        RunGalaxy(g, 30);
        Assert.Single(g.Battles);
        var back = GalaxySave.Deserialize(GalaxySave.Serialize(g))!;
        Assert.Empty(back.Battles);
        StepGalaxy(back);
        Assert.Single(back.Battles);
    }

    [Fact]
    public void Saves_reject_what_is_not_a_save()
    {
        Assert.Null(GalaxySave.Deserialize("not json"));
        Assert.Null(GalaxySave.Deserialize("{\"version\":0}"));
    }

    [Fact]
    public void Saves_go_to_a_file_and_come_back()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sandbox-ship-{Guid.NewGuid()}.json");
        try
        {
            var g = CreateGalaxy(Faction.Klaed, [Faction.Nairan]);
            Assert.True(GalaxySave.Save(g, path));
            Assert.Equal(new SaveInfo(GalaxySave.Info(path)!.SavedAt, Faction.Klaed, 1), GalaxySave.Info(path));
            Assert.NotNull(GalaxySave.Load(path));
            GalaxySave.Clear(path);
            Assert.Null(GalaxySave.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Builds_buildings_and_ships_side_by_side()
    {
        var g = Spectated();
        g.Factions[Faction.Klaed].Credits = 100000;
        Assert.True(EnqueueBuild(g, Faction.Klaed, West, GalaxyBuild.Of(GalaxyBuildKind.Mine)).Ok);
        Assert.True(EnqueueBuild(g, Faction.Klaed, West, GalaxyBuild.Ship(ShipClass.Scout)).Ok);
        RunGalaxy(g, Ships.Defs[ShipClass.Scout].BuildTime + 0.5);
        Assert.Equal(2 + Ships.Defs[ShipClass.Scout].Squad!.Value, FirstFleet(g, West, Faction.Klaed).Ships.Of(ShipClass.Scout));
        Assert.Single(g.Planets[West].Buildings);
    }

    [Fact]
    public void Research_needs_a_lab_then_raises_the_level()
    {
        var g = Spectated();
        var f = g.Factions[Faction.Klaed];
        f.Credits = 100000;
        Assert.Equal(Result.Fail("Laboratoire requis"), CanResearchTech(g, Faction.Klaed, TechId.Weapons));
        g.Planets[West].Lab = 1;
        Assert.True(StartTech(g, Faction.Klaed, TechId.Weapons).Ok);
        RunGalaxy(g, Techs.Defs[TechId.Weapons].Times[0] + 0.5);
        Assert.Equal(1, f.Research[TechId.Weapons]);
    }

    [Fact]
    public void Research_queues_too_and_a_save_without_a_queue_still_loads()
    {
        var g = Spectated();
        var f = g.Factions[Faction.Klaed];
        f.Credits = 100000;
        g.Planets[West].Lab = 1;
        Assert.True(StartTech(g, Faction.Klaed, TechId.Engines).Ok);
        Assert.True(StartTech(g, Faction.Klaed, TechId.Mining).Ok);
        Assert.Single(f.ResearchQueue);
        var old = JsonNode.Parse(GalaxySave.Serialize(g))!;
        foreach (var faction in old["factions"]!.AsArray()) faction!.AsObject().Remove("researchQueue");
        var loaded = GalaxySave.Deserialize(old.ToJsonString())!;
        Assert.Empty(loaded.Factions[Faction.Klaed].ResearchQueue);
        RunGalaxy(g, Techs.Defs[TechId.Engines].Times[0] + Techs.Defs[TechId.Mining].Times[0] + 1);
        Assert.Equal((1, 1), (f.Research[TechId.Engines], f.Research[TechId.Mining]));
    }

    [Fact]
    public void A_production_loop_requeues_its_class_and_waits_when_short_of_credits()
    {
        var g = Spectated();
        var f = g.Factions[Faction.Klaed];
        var orin = Id("Orin");
        g.Planets[orin].Owner = Faction.Klaed;
        // Only a station that builds the class takes the loop.
        Assert.Equal(Result.Fail("Station requise"), SetLoop(g, Faction.Klaed, orin, ShipClass.Frigate, true));
        Assert.Equal(Result.Fail("Planète non contrôlée"), SetLoop(g, Faction.Klaed, East, ShipClass.Frigate, true));
        Assert.Empty(g.Planets[orin].Repeat);
        f.Credits = 0;
        Assert.True(SetLoop(g, Faction.Klaed, West, ShipClass.Frigate, true).Ok);
        StepGalaxy(g);
        Assert.Empty(g.Planets[West].Units);
        f.Credits = 100000;
        StepGalaxy(g);
        Assert.Equal(ShipClass.Frigate, Assert.Single(g.Planets[West].Units).Cls);
        // Once one is built, the next one is queued at once, never two at a time.
        var time = BuildTime(g, g.Planets[West], GalaxyBuild.Ship(ShipClass.Frigate));
        RunGalaxy(g, time + 0.5);
        Assert.Equal(3, FirstFleet(g, West, Faction.Klaed).Ships.Of(ShipClass.Frigate));
        Assert.Equal(ShipClass.Frigate, Assert.Single(g.Planets[West].Units).Cls);
        Assert.True(SetLoop(g, Faction.Klaed, West, ShipClass.Frigate, false).Ok);
        RunGalaxy(g, time + 0.5);
        Assert.Equal(4, FirstFleet(g, West, Faction.Klaed).Ships.Of(ShipClass.Frigate));
        Assert.Empty(g.Planets[West].Units);
    }

    [Fact]
    public void Production_loops_survive_a_save_and_a_save_without_them_still_loads()
    {
        var g = Spectated();
        g.Factions[Faction.Klaed].Credits = 100000;
        Assert.True(SetLoop(g, Faction.Klaed, West, ShipClass.Frigate, true).Ok);
        Assert.True(SetLoop(g, Faction.Klaed, West, ShipClass.Scout, true).Ok);
        var back = GalaxySave.Deserialize(GalaxySave.Serialize(g))!;
        Assert.Equal(new[] { ShipClass.Frigate, ShipClass.Scout }, back.Planets[West].Repeat);
        StepGalaxy(back);
        Assert.Equal(new[] { ShipClass.Frigate, ShipClass.Scout }, back.Planets[West].Units.Select(q => q.Cls));
        var old = JsonNode.Parse(GalaxySave.Serialize(g))!;
        foreach (var planet in old["planets"]!.AsArray()) planet!.AsObject().Remove("repeat");
        Assert.Empty(GalaxySave.Deserialize(old.ToJsonString())!.Planets[West].Repeat);
    }

    [Fact]
    public void A_battle_fought_at_a_planet_uses_both_sides_research()
    {
        var g = Spectated();
        g.Factions[Faction.Klaed].Research[TechId.Armor] = 2;
        var kaldra = Id("Kaldra");
        g.Planets[kaldra].Owner = Faction.Nairan;
        g.Planets[kaldra].Station = 1;
        SendFleet(g, FirstFleet(g, West, Faction.Klaed).Id, kaldra);
        RunGalaxy(g, 12);
        Assert.Equal(2, g.Battles[0].World.Factions[Faction.Klaed].Research[TechId.Armor]);
    }

    [Fact]
    public void Special_planets()
    {
        var g = Spectated();
        // Rich deposit: double mine income.
        var nadir = g.Planets[Id("Nadir")];
        nadir.Owner = Faction.Klaed;
        nadir.Mines = 1;
        var capital = g.Planets[West];
        Assert.Equal(capital.Income + nadir.Income + capital.Station * GalaxyRules.StationIncome + GalaxyRules.MineIncome * (capital.Mines + 2),
            IncomeOf(g, Faction.Klaed));
        // Orbital shipyard: ships 30 % faster.
        var kaldra = g.Planets[Id("Kaldra")];
        kaldra.Owner = Faction.Klaed;
        kaldra.Station = 1;
        Expect.Close(Ships.Defs[ShipClass.Frigate].BuildTime * 0.7, BuildTime(g, kaldra, GalaxyBuild.Ship(ShipClass.Frigate)), 5);
        // Fortress: two more platforms.
        var sorel = g.Planets[Id("Sorel")];
        sorel.Owner = Faction.Klaed;
        sorel.Station = 1;
        Assert.Equal(4, PlatformCap(sorel));
        // Archives: research a quarter faster.
        Assert.Equal(1, ResearchSpeed(g, Faction.Klaed));
        g.Planets[Id("Orin")].Owner = Faction.Klaed;
        Expect.Close(4.0 / 3, ResearchSpeed(g, Faction.Klaed), 5);
    }

    [Fact]
    public void Taking_a_planet_keeps_its_mines_and_lab_but_not_what_is_in_orbit()
    {
        var g = Spectated();
        var kaldra = Id("Kaldra");
        g.Planets[kaldra].Owner = Faction.Nairan;
        g.Planets[kaldra].Mines = 2;
        g.Planets[kaldra].Lab = 1;
        SendFleet(g, FirstFleet(g, West, Faction.Klaed).Id, kaldra);
        RunGalaxy(g, 40);
        var p = g.Planets[kaldra];
        Assert.Equal((Faction.Klaed, 2, 1, 0), (p.Owner!.Value, p.Mines, p.Lab, p.Station));
    }

    [Fact]
    public void Generated_galaxies_are_the_same_for_the_same_seed_and_differ_between_seeds()
    {
        Assert.Equal(GalaxyGen.GenerateLayout(42).Planets, GalaxyGen.GenerateLayout(42).Planets);
        Assert.Equal(GalaxyGen.GenerateLayout(42).Lanes, GalaxyGen.GenerateLayout(42).Lanes);
        Assert.NotEqual(GalaxyGen.GenerateLayout(42).Planets, GalaxyGen.GenerateLayout(43).Planets);
    }

    [Fact]
    public void Generated_galaxies_are_connected_spread_out_without_crossing_lanes_with_distant_capitals()
    {
        foreach (var seed in new[] { 1, 2, 3, 99, 12345 })
        {
            var layout = GalaxyGen.GenerateLayout(seed);
            var (planets, lanes, capitals) = (layout.Planets, layout.Lanes, layout.Capitals);
            Expect.GreaterOrEqual(planets.Count, 12);
            var g = CreateGalaxy(Faction.Klaed, [Faction.Nairan], false, seed, layout);
            Assert.Equal(planets.Count, Reachable(g));
            Point At(int i) => new(planets[i].X, planets[i].Y);
            for (var i = 0; i < lanes.Count; i++)
            {
                for (var j = i + 1; j < lanes.Count; j++)
                {
                    Assert.False(GalaxyGen.Crosses(At(lanes[i].A), At(lanes[i].B), At(lanes[j].A), At(lanes[j].B)));
                }
            }
            Expect.GreaterOrEqual(FindPath(g, capitals[0], capitals[1])!.Count, 3);
            Assert.Equal(Faction.Klaed, g.Planets[capitals[0]].Owner);
        }
    }

    [Fact]
    public void Saves_keep_the_generated_lanes()
    {
        var layout = GalaxyGen.GenerateLayout(7);
        var g = CreateGalaxy(Faction.Klaed, [Faction.Nairan], true, 7, layout);
        Assert.Equal(layout.Lanes, GalaxySave.Deserialize(GalaxySave.Serialize(g))!.Lanes);
    }

    [Fact]
    public void Heroes_are_built_travel_with_fleets_or_stay_behind_and_fall_in_battle()
    {
        var g = Spectated();
        var ace = new HeroId(Faction.Klaed, HeroRole.Ace);
        g.Factions[Faction.Klaed].Credits = 100000;
        Assert.True(EnqueueBuild(g, Faction.Klaed, West, GalaxyBuild.HeroOf(ace)).Ok);
        Assert.Equal(Result.Fail("Déjà en service"), CanBuild(g, Faction.Klaed, West, GalaxyBuild.HeroOf(ace)));
        RunGalaxy(g, 26);
        var fleet = FirstFleet(g, West, Faction.Klaed);
        Assert.Equal([ace], fleet.Heroes);
        // A partial order leaves the hero home unless it is picked.
        SendFleet(g, fleet.Id, Id("Orin"), new ShipCounts { [ShipClass.Scout] = 1 });
        Assert.Equal([ace], fleet.Heroes);
        // Sent alone against a strong planet, the hero falls and must wait to be rebuilt.
        var kaldra = Id("Kaldra");
        g.Planets[kaldra].Owner = Faction.Nairan;
        g.Planets[kaldra].Station = 3;
        g.Planets[kaldra].Platforms = 6;
        SendFleet(g, fleet.Id, kaldra, [], [ace]);
        RunGalaxy(g, 40);
        Assert.DoesNotContain(g.Fleets.Values, fl => fl.Heroes.Contains(ace));
        Assert.False(CanBuild(g, Faction.Klaed, West, GalaxyBuild.HeroOf(ace)).Ok);
        RunGalaxy(g, Heroes.Defs[ace].Respawn);
        Assert.True(CanBuild(g, Faction.Klaed, West, GalaxyBuild.HeroOf(ace)).Ok);
    }

    [Fact]
    public void Fog_of_war_shows_nearby_planets_and_fleets_only()
    {
        var g = CreateGalaxy(Faction.Klaed, [Faction.Nairan], false);
        var seen = VisiblePlanets(g, Faction.Klaed);
        Assert.Contains(West, seen);
        foreach (var n in Neighbours(g, West)) Assert.Contains(n, seen);
        Assert.DoesNotContain(East, seen);
        var enemy = FirstFleet(g, East, Faction.Nairan);
        Assert.False(FleetVisible(g, Faction.Klaed, enemy));
        Assert.True(FleetVisible(g, Faction.Nairan, enemy));
    }

    [Fact]
    public void Galaxies_for_three_give_each_a_far_capital_and_run_with_three_AIs()
    {
        var layout = GalaxyGen.GenerateLayout(21, 3);
        Assert.Equal(3, layout.Capitals.Distinct().Count());
        var g = CreateGalaxy(Faction.Klaed, [Faction.Nairan, Faction.Nautolan], true, 21, layout);
        g.Factions[Faction.Klaed].AI = new GalaxyAIState { Difficulty = Difficulty.Normal };
        g.Player = Faction.Nautolan;
        Faction[] order = [Faction.Klaed, Faction.Nairan, Faction.Nautolan];
        for (var i = 0; i < 3; i++) Assert.Equal(order[i], g.Planets[layout.Capitals[i]].Owner);
        for (var i = 0; i < 3; i++) for (var j = i + 1; j < 3; j++) Expect.GreaterOrEqual(FindPath(g, layout.Capitals[i], layout.Capitals[j])!.Count, 2);
        g.Player = Faction.Klaed;
        g.Factions[Faction.Klaed].AI = null;
        RunGalaxy(g, 300);
        Expect.Greater(g.Planets.Count(p => p.Owner == Faction.Nautolan), 1);
    }

    [Fact]
    public void Ships_enter_a_battle_as_worn_as_their_fleet_and_come_out_with_what_they_kept()
    {
        var w = Battle.CreatePlanetBattle(
            new BattleSide { Faction = Faction.Klaed, Ships = new ShipCounts { [ShipClass.Frigate] = 2 }, Condition = 0.5 },
            new BattleSide { Faction = Faction.Nairan }, PlanetDefense.None);
        var frigates = w.Entities.Where(e => e.Type == "frigate").ToList();
        foreach (var f in frigates) Expect.Close(f.MaxHull * 0.5, f.Hull, 5);
        frigates[0].Hull = frigates[0].MaxHull * 0.2;
        Expect.Close(0.35, Battle.SurvivorCondition(w, Faction.Klaed), 5);
    }

    [Fact]
    public void Fleets_mend_at_their_own_planets_faster_with_a_dock_and_average_out_when_merged()
    {
        var g = Spectated();
        var capital = g.Planets.Find(p => p.Owner == Faction.Klaed)!;
        var fleet = FirstFleet(g, capital.Id, Faction.Klaed);
        fleet.Condition = 0.5;
        RunGalaxy(g, 10);
        var slow = fleet.Condition - 0.5;
        Expect.Close(10 * RepairStation, slow, 2);
        capital.RepairDock = 1;
        fleet.Condition = 0.5;
        RunGalaxy(g, 10);
        Expect.Greater(fleet.Condition - 0.5, slow * 2);
        // Fresh ships of the same value bring the average halfway up.
        fleet.Condition = 0.4;
        capital.RepairDock = 0;
        AddShipsAt(g, Faction.Klaed, capital.Id, fleet.Ships.Clone());
        Expect.Close(0.7, fleet.Condition, 1);
    }

    // From tests/extremes.test.ts: nothing may leave a campaign stuck.

    [Fact]
    public void A_battle_the_player_leaves_plays_on_by_itself_to_the_end()
    {
        var g = CreateGalaxy(Faction.Klaed, [Faction.Nairan], false);
        var target = g.Planets.Find(p => p.Owner == Faction.Nairan)!;
        var fleet = g.Fleets.Values.First(f => f.Faction == Faction.Klaed);
        AddShipsAt(g, Faction.Klaed, fleet.At!.Value, new ShipCounts { [ShipClass.Frigate] = 6 });
        SendFleet(g, fleet.Id, target.Id);
        for (var i = 0; i < 6000 && g.Battles.Count == 0; i++) RunGalaxy(g, 0.1);
        var b = g.Battles[0];
        ChooseBattle(g, b.Id, BattleStatus.Tactical);
        DelegateBattle(g, b.Id);
        Assert.Equal(BattleStatus.Auto, b.Status);
        RunGalaxy(g, 120);
        Assert.DoesNotContain(g.Battles, x => x.Id == b.Id);
    }

    [Fact]
    public void A_campaign_of_three_AIs_keeps_resolving_its_battles()
    {
        var g = CreateGalaxy(Faction.Klaed, [Faction.Nairan, Faction.Nautolan], true, 55, GalaxyGen.GenerateLayout(55, 3));
        // A player who is not on the map: every battle resolves on its own.
        g.Player = (Faction)99;
        g.Factions[Faction.Klaed].AI = new GalaxyAIState { Difficulty = Difficulty.Normal };
        var opened = new Dictionary<int, double>();
        for (var t = 0; t < 900 && g.Winner is null; t++)
        {
            RunGalaxy(g, 1);
            foreach (var b in g.Battles) opened.TryAdd(b.Id, g.Time);
            // No battle lasts past the auto-resolution limit, plus a little slack.
            foreach (var b in g.Battles) Expect.Less(g.Time - opened[b.Id], 80);
            foreach (var fleet in g.Fleets.Values) Assert.True(double.IsFinite(fleet.Progress) && fleet.Condition > 0);
        }
        Expect.Greater(opened.Count, 0);
    }

    // The Errants (not in the web version): a neutral raid site on the most isolated planet.

    internal static Planet ErrantPlanet(Galaxy g) => g.Planets.Single(p => p.Owner == Faction.Errant);

    static void FullGuard(Galaxy g, Planet site)
    {
        Assert.Equal((3, 4, 3, 1, 1), (site.Station, site.Platforms, site.PlatformTier, site.IonCannon, site.RepairDock));
        SameShips(FirstFleet(g, site.Id, Faction.Errant).Ships,
            (ShipClass.Colossus, 1), (ShipClass.Dreadnought, 1), (ShipClass.Battlecruiser, 2), (ShipClass.Frigate, 4));
    }

    /// <summary>Fights a battle out by hand, as the battle view would, without the galaxy moving on.</summary>
    static BattleResult FightOut(GalaxyBattle b)
    {
        for (var t = 0.0; t < 900; t += Steps.Step)
        {
            Steps.StepWorld(b.World);
            Battle.UpdateBattleAI(b.World, [b.Attacker, b.Defender], Steps.Step);
            if (Battle.Outcome(b.World, b.Attacker, b.Defender) is BattleResult r) return r;
        }
        return BattleResult.Retreat;
    }

    [Fact]
    public void The_Errants_hold_the_most_isolated_planet_with_their_guard_and_never_a_capital()
    {
        var maps = new List<(GalaxyLayout Layout, Faction[] Enemies)> { (GalaxyGen.Classic, [Faction.Nairan]) };
        foreach (var seed in new[] { 1, 2, 3, 21, 55, 99 })
        {
            foreach (var size in new[] { MapSize.Small, MapSize.Medium, MapSize.Large })
            {
                maps.Add((GalaxyGen.GenerateLayout(seed, 2, size), [Faction.Nairan]));
                maps.Add((GalaxyGen.GenerateLayout(seed, 3, size), [Faction.Nairan, Faction.Nautolan]));
            }
        }
        foreach (var (layout, enemies) in maps)
        {
            var g = CreateGalaxy(Faction.Klaed, enemies, true, 1, layout);
            var site = ErrantPlanet(g);
            var capitals = layout.Capitals.ToList();
            Assert.DoesNotContain(site.Id, capitals);
            var hops = Hops(g, capitals);
            var others = g.Planets.Where(p => !capitals.Contains(p.Id)).ToList();
            Assert.Equal(others.Max(p => hops[p.Id]), hops[site.Id]);
            // Next to a capital only on a map where every other planet is.
            if (others.Exists(p => hops[p.Id] >= 2)) Expect.GreaterOrEqual(hops[site.Id], 2);
            // Among the farthest in lanes, the farthest in distance.
            double Away(Planet p) => capitals.Min(c => Worlds.Dist(p, g.Planets[c]));
            Assert.Equal(others.Where(p => hops[p.Id] == hops[site.Id]).Max(Away), Away(site));
            FullGuard(g, site);
            Assert.False(g.Factions.ContainsKey(Faction.Errant));
        }
        // On the classic map: one of the two planets three lanes from both capitals.
        Assert.Contains(ErrantPlanet(CreateGalaxy(Faction.Klaed, [Faction.Nairan])).Name, new[] { "Nadir", "Calyx" });
        Assert.DoesNotContain(CreateGalaxy(Faction.Klaed, [Faction.Nairan], errants: false).Planets, p => p.Owner == Faction.Errant);
    }

    [Fact]
    public void The_Errants_never_build_research_or_move()
    {
        // Their guard, that is: war bands come with random events (IncidentsTests).
        var g = CreateGalaxy(Faction.Klaed, [Faction.Nairan, Faction.Nautolan], true, 55, GalaxyGen.GenerateLayout(55, 3), incidents: false);
        g.Factions[Faction.Klaed].AI = new GalaxyAIState { Difficulty = Difficulty.Normal };
        g.Player = (Faction)99;
        var site = ErrantPlanet(g);
        for (var t = 0; t < 600; t++)
        {
            RunGalaxy(g, 1);
            Assert.False(g.Factions.ContainsKey(Faction.Errant));
            Assert.True(site.Units.Count == 0 && site.Buildings.Count == 0 && site.Repeat.Count == 0);
            // Their station only ever loses levels, in battle.
            Assert.True(site.Station <= 3 && site.Platforms <= 4);
            foreach (var f in g.Fleets.Values.Where(f => f.Faction == Faction.Errant)) Assert.True(f.At == site.Id && f.To is null);
        }
        Assert.DoesNotContain(g.Events, e => e is PlanetBuilt { Faction: Faction.Errant } or TechResearched { Faction: Faction.Errant }
            or GalaxyVictory { Faction: Faction.Errant } or BattleEnded { Attacker: Faction.Errant });
    }

    [Fact]
    public void The_last_playable_faction_wins_with_the_Errants_still_on_their_planet()
    {
        var g = CreateGalaxy(Faction.Klaed, [Faction.Nairan], false);
        foreach (var f in g.Fleets.Values.Where(f => f.Faction == Faction.Nairan).ToList()) g.Fleets.Remove(f.Id);
        g.Planets[East].Owner = Faction.Klaed;
        StepGalaxy(g);
        Assert.Equal(Faction.Klaed, g.Winner);
        Assert.True(g.Factions[Faction.Nairan].Eliminated);
        Assert.Equal(Faction.Klaed, Assert.Single(g.Events.OfType<GalaxyVictory>()).Faction);
        FullGuard(g, ErrantPlanet(g));
    }

    [Fact]
    public void Bounties_earned_in_a_battle_go_to_each_side_s_galaxy_credits()
    {
        var g = CreateGalaxy(Faction.Klaed, [Faction.Nairan], false, errants: false);
        var kaldra = Id("Kaldra");
        g.Planets[kaldra].Owner = Faction.Nairan;
        AddShipsAt(g, Faction.Nairan, kaldra, new ShipCounts { [ShipClass.Frigate] = 3 });
        AddShipsAt(g, Faction.Klaed, West, new ShipCounts { [ShipClass.Battlecruiser] = 3 });
        SendFleet(g, FirstFleet(g, West, Faction.Klaed).Id, kaldra);
        RunGalaxy(g, 30);
        // The player is in it: the battle waits for them, and the galaxy with it.
        var b = Assert.Single(g.Battles);
        var result = FightOut(b);
        Faction[] sides = [Faction.Klaed, Faction.Nairan];
        var earned = sides.ToDictionary(f => f, f => b.World.Factions[f].Bounties);
        var credits = sides.ToDictionary(f => f, f => g.Factions[f].Credits);
        Expect.Greater(earned[Faction.Klaed], 0);
        ConcludeBattle(g, b.Id, result);
        foreach (var f in sides) Assert.Equal(credits[f] + earned[f], g.Factions[f].Credits);
        Assert.Contains(g.Events, e => e is BountyPaid { Faction: Faction.Klaed, Tech: null } paid && paid.Credits == earned[Faction.Klaed]);
    }

    [Fact]
    public void A_colossus_brought_down_at_the_Errants_planet_pays_its_bounty_and_tech_to_the_galaxy()
    {
        var g = CreateGalaxy(Faction.Klaed, [Faction.Nairan], false);
        var site = ErrantPlanet(g);
        var next = Neighbours(g, site.Id)[0];
        AddShipsAt(g, Faction.Klaed, next, new ShipCounts { [ShipClass.Frigate] = 4 });
        SendFleet(g, FirstFleet(g, next, Faction.Klaed).Id, site.Id);
        RunGalaxy(g, 30);
        // The planet battle, with the Errants defending.
        var b = Assert.Single(g.Battles);
        Assert.Equal((Faction.Klaed, Faction.Errant, site.Id), (b.Attacker, b.Defender, b.PlanetId));
        Assert.Equal(3, Battle.DefenseLeft(b.World, Faction.Errant).Station);
        var colossus = b.World.Entities.First(e => e.Type == "colossus");
        colossus.Hull = 0;
        colossus.LastHitBy = Faction.Klaed;
        Worlds.RemoveDead(b.World);
        var f = g.Factions[Faction.Klaed];
        var (credits, levels) = (f.Credits, Techs.All.Sum(t => f.Research[t]));
        ConcludeBattle(g, b.Id, BattleResult.Retreat);
        Assert.Equal(credits + Raids.Bounty, f.Credits);
        Assert.Equal(levels + 1, Techs.All.Sum(t => f.Research[t]));
        var paid = Assert.Single(g.Events.OfType<BountyPaid>());
        Assert.Equal((Faction.Klaed, Raids.Bounty), (paid.Faction, paid.Credits));
        Assert.NotNull(paid.Tech);
        // The Errants hold on, their guard without its colossus; nothing is paid to them.
        Assert.Equal(Faction.Errant, site.Owner);
        Assert.Equal(0, FirstFleet(g, site.Id, Faction.Errant).Ships.Of(ShipClass.Colossus));
        Assert.False(g.Factions.ContainsKey(Faction.Errant));
    }

    [Fact]
    public void Taking_the_Errants_planet_gives_it_to_the_attacker()
    {
        var g = CreateGalaxy(Faction.Klaed, [Faction.Nairan], false);
        var site = ErrantPlanet(g);
        var next = Neighbours(g, site.Id)[0];
        AddShipsAt(g, Faction.Klaed, next, new ShipCounts { [ShipClass.Frigate] = 2 });
        SendFleet(g, FirstFleet(g, next, Faction.Klaed).Id, site.Id);
        RunGalaxy(g, 30);
        var b = Assert.Single(g.Battles);
        // Everything the Errants have in orbit falls.
        foreach (var e in b.World.Entities.Where(e => e.Faction == Faction.Errant && e.Kind != EntityKind.Hardpoint)) e.Hull = 0;
        Worlds.RemoveDead(b.World);
        Assert.Equal(BattleResult.Attacker, Battle.Outcome(b.World, b.Attacker, b.Defender));
        ConcludeBattle(g, b.Id, BattleResult.Attacker);
        Assert.Equal((Faction.Klaed, 0), (site.Owner!.Value, site.Station));
        Assert.DoesNotContain(g.Fleets.Values, fl => fl.Faction == Faction.Errant);
        Assert.Contains(g.Events, e => e is Captured { Faction: Faction.Klaed, From: Faction.Errant });
        Assert.Null(g.Winner);
    }

    [Fact]
    public void The_AI_leaves_the_Errants_alone_until_it_can_clearly_beat_them()
    {
        var g = CreateGalaxy(Faction.Klaed, [Faction.Nairan]);
        g.Player = Faction.Nautolan;
        // Passive Klaed holds its capital: the fleet below would otherwise take it and win the
        // galaxy before the test is over (a won galaxy no longer moves).
        g.Planets[West].Station = 3;
        g.Planets[West].Platforms = 6;
        var site = ErrantPlanet(g);
        // A fleet next door, at a planet it does not hold yet: the Errants' site is out of its sight.
        var next = Neighbours(g, site.Id).First(id => g.Planets[id].Owner is null);
        AddShipsAt(g, Faction.Nairan, next, new ShipCounts { [ShipClass.Frigate] = 6, [ShipClass.Battlecruiser] = 2 });
        Assert.DoesNotContain(site.Id, VisiblePlanets(g, Faction.Nairan));
        RunGalaxy(g, 60);
        Assert.Null(g.Winner);
        Assert.DoesNotContain(g.Events, e => e is BattleEnded { Defender: Faction.Errant });
        Assert.DoesNotContain(g.Battles, b => b.PlanetId == site.Id);
        FullGuard(g, site);
        // Far stronger than the site, it goes for it.
        AddShipsAt(g, Faction.Nairan, next, new ShipCounts { [ShipClass.Leviathan] = 6 });
        RunGalaxy(g, 120);
        Assert.Contains(g.Events, e => e is BattleEnded { Attacker: Faction.Nairan, Defender: Faction.Errant });
    }

    [Fact]
    public void The_Errants_and_their_planet_survive_a_save()
    {
        var g = CreateGalaxy(Faction.Klaed, [Faction.Nairan]);
        RunGalaxy(g, 20);
        var site = ErrantPlanet(g);
        var back = GalaxySave.Deserialize(GalaxySave.Serialize(g))!;
        Assert.Equal(GalaxySave.Serialize(g, 0), GalaxySave.Serialize(back, 0));
        Assert.Equal(site.Id, ErrantPlanet(back).Id);
        FullGuard(back, ErrantPlanet(back));
        Assert.False(back.Factions.ContainsKey(Faction.Errant));
        RunGalaxy(back, 30);
        FullGuard(back, ErrantPlanet(back));
    }
}
