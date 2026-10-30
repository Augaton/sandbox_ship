// Random galactic events (not in the web version): derelict fleets, Errant war bands, ion
// storms and trade booms, drawn from a random stream of their own.
using System.Text.Json.Nodes;
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.Galaxies;
using static SandboxShip.Sim.Tests.SupplyTests;

namespace SandboxShip.Sim.Tests;

public class IncidentsTests
{
    /// <summary>A generated galaxy where nobody moves, watched from off the map: it never ends, and events keep coming.</summary>
    static Galaxy Passive(int seed, bool incidents = true)
    {
        var g = CreateGalaxy(Faction.Klaed, [Faction.Nairan], false, seed, GalaxyGen.GenerateLayout(seed), incidents: incidents);
        g.Player = (Faction)99;
        return g;
    }

    static List<(double Time, IncidentStarted Event)> Watch(Galaxy g, double seconds)
    {
        var seen = new List<(double, IncidentStarted)>();
        for (var t = 0.0; t < seconds && g.Winner is null; t += 1)
        {
            RunGalaxy(g, 1);
            seen.AddRange(g.Events.OfType<IncidentStarted>().Select(e => (g.Time, e)));
            g.Events.Clear();
        }
        return seen;
    }

    [Fact]
    public void Events_come_every_few_minutes_the_same_for_the_same_seed_and_never_when_off()
    {
        var a = Watch(Passive(3), 900);
        var b = Watch(Passive(3), 900);
        Assert.Equal(a.Select(x => x.Event), b.Select(x => x.Event));
        Assert.InRange(a.Count, 2, 4);
        Assert.InRange(a[0].Time, FirstIncident, FirstIncident + IncidentSpread + 1);
        for (var i = 1; i < a.Count; i++) Assert.InRange(a[i].Time - a[i - 1].Time, IncidentGap - 1, IncidentGap + IncidentSpread + 1);
        Assert.Empty(Watch(Passive(3, incidents: false), 900));
    }

    [Fact]
    public void Events_draw_from_their_own_stream()
    {
        var g = Quiet();
        var seed = g.Seed;
        var draws = Enumerable.Range(0, 5).Select(_ => IncidentRand(g)).ToList();
        Assert.Equal(seed, g.Seed);
        Assert.All(draws, d => Assert.InRange(d, 0, 1));
        Assert.Equal(5, draws.Distinct().Count());
    }

    [Fact]
    public void A_derelict_goes_to_the_first_fleet_to_dock_there_or_drifts_off()
    {
        var g = Quiet();
        var orin = Id("Orin");
        var wreck = new ShipCounts { [ShipClass.Battlecruiser] = 1 };
        g.Ongoing.Add(new Incident { Kind = IncidentKind.Derelict, Planet = orin, Remaining = DerelictTime, Ships = wreck });
        var fleet = FleetsAt(g, Id("Vesper"), Faction.Klaed)[0];
        var before = fleet.Ships.Clone();
        Assert.True(SendFleet(g, fleet.Id, orin).Ok);
        RunGalaxy(g, 10);
        Assert.Equal(orin, fleet.At);
        Assert.Equal(1, fleet.Ships.Of(ShipClass.Battlecruiser));
        Assert.Equal(before.Of(ShipClass.Frigate), fleet.Ships.Of(ShipClass.Frigate));
        // Its hulls are worn: the fleet's condition is their average, by value.
        Expect.Less(fleet.Condition, 1);
        Expect.Greater(fleet.Condition, DerelictCondition);
        Assert.Contains(g.Events, e => e is IncidentEnded { Kind: IncidentKind.Derelict } i && i.Side == Faction.Klaed);
        Assert.Empty(g.Ongoing);
        // Nobody comes: it drifts off.
        g.Ongoing.Add(new Incident { Kind = IncidentKind.Derelict, Planet = Id("Thessa"), Remaining = DerelictTime, Ships = wreck });
        RunGalaxy(g, DerelictTime + 1);
        Assert.Empty(g.Ongoing);
        Assert.Contains(g.Events, e => e is IncidentEnded { Kind: IncidentKind.Derelict, Side: null });
    }

    /// <summary>An Errant war band gathered at Kaldra, about to strike Orin.</summary>
    static Galaxy WarBand(ShipCounts band)
    {
        var g = Quiet();
        var orin = g.Planets[Id("Orin")];
        orin.Owner = Faction.Klaed;
        orin.Mines = 2;
        AddShipsAt(g, Faction.Errant, Id("Kaldra"), band);
        g.Ongoing.Add(new Incident { Kind = IncidentKind.WarBand, Planet = Id("Kaldra"), Other = orin.Id, Remaining = WarBandWarning });
        return g;
    }

    [Fact]
    public void A_war_band_waits_then_pillages_an_undefended_planet_and_vanishes_without_taking_it()
    {
        var g = WarBand(new ShipCounts { [ShipClass.Frigate] = 3 });
        var orin = g.Planets[Id("Orin")];
        RunGalaxy(g, WarBandWarning - 1);
        Assert.Single(FleetsAt(g, Id("Kaldra"), Faction.Errant));
        RunGalaxy(g, 20);
        Assert.Equal((Faction.Klaed, 0), (orin.Owner!.Value, orin.Mines));
        Assert.DoesNotContain(g.Fleets.Values, f => f.Side == Faction.Errant);
        Assert.Contains(g.Events, e => e is IncidentEnded { Kind: IncidentKind.WarBand } ended && ended.Side == Faction.Klaed && ended.Planet == orin.Id);
        Assert.DoesNotContain(g.Events, e => e is Captured);
    }

    [Fact]
    public void A_war_band_that_wins_a_battle_still_never_takes_the_planet()
    {
        var g = WarBand(new ShipCounts { [ShipClass.Dreadnought] = 3 });
        var orin = g.Planets[Id("Orin")];
        orin.Station = 1;
        RunGalaxy(g, WarBandWarning + 90);
        Assert.Contains(g.Events, e => e is BattleEnded { Result: BattleResult.Attacker } b && b.Attacker == Faction.Errant);
        Assert.Equal((Faction.Klaed, 0, 0), (orin.Owner!.Value, orin.Station, orin.Mines));
        Assert.DoesNotContain(g.Fleets.Values, f => f.Side == Faction.Errant);
    }

    [Fact]
    public void A_war_band_beaten_on_its_way_strikes_nobody()
    {
        var g = WarBand(new ShipCounts { [ShipClass.Scout] = 1 });
        var orin = g.Planets[Id("Orin")];
        AddShipsAt(g, Faction.Klaed, orin.Id, new ShipCounts { [ShipClass.Battlecruiser] = 2 });
        RunGalaxy(g, WarBandWarning + 60);
        Assert.Contains(g.Events, e => e is BattleEnded { Result: BattleResult.Defender } b && b.Attacker == Faction.Errant);
        Assert.Equal(2, orin.Mines);
        Assert.DoesNotContain(g.Fleets.Values, f => f.Side == Faction.Errant);
    }

    [Fact]
    public void An_ion_storm_halves_speed_on_its_lane_for_a_while()
    {
        var g = Quiet();
        var (orin, kaldra, maru) = (Id("Orin"), Id("Kaldra"), Id("Maru"));
        var ships = new ShipCounts { [ShipClass.Frigate] = 1 };
        var slow = AddShipsAt(g, Faction.Klaed, orin, ships)!;
        var fast = AddShipsAt(g, Faction.Nairan, maru, ships)!;
        g.Ongoing.Add(new Incident { Kind = IncidentKind.IonStorm, Planet = kaldra, Other = orin, Remaining = StormTime });
        Assert.True(SendFleet(g, slow.Id, kaldra).Ok);
        Assert.True(SendFleet(g, fast.Id, kaldra).Ok);
        StepGalaxy(g);
        var step = FleetSpeed(ships) * Step;
        Expect.Close(step * StormSpeed / Worlds.Dist(g.Planets[orin], g.Planets[kaldra]), slow.Progress, 9);
        Expect.Close(step / Worlds.Dist(g.Planets[maru], g.Planets[kaldra]), fast.Progress, 9);
        g.Ongoing[0].Remaining = 1;
        RunGalaxy(g, 2);
        Assert.Empty(g.Ongoing);
        Assert.Contains(g.Events, e => e is IncidentEnded { Kind: IncidentKind.IonStorm });
    }

    [Fact]
    public void A_trade_boom_doubles_a_planet_s_income_for_a_while()
    {
        var g = Quiet();
        var vesper = g.Planets[Id("Vesper")];
        var normal = IncomeOf(g, Faction.Klaed);
        g.Ongoing.Add(new Incident { Kind = IncidentKind.TradeBoom, Planet = vesper.Id, Remaining = BoomTime });
        Assert.Equal(normal * BoomIncome, IncomeOf(g, Faction.Klaed));
        RunGalaxy(g, BoomTime + 1);
        Assert.Equal(normal, IncomeOf(g, Faction.Klaed));
        Assert.Contains(g.Events, e => e is IncidentEnded { Kind: IncidentKind.TradeBoom });
    }

    [Fact]
    public void Every_kind_of_event_comes_up()
    {
        var kinds = new HashSet<IncidentKind>();
        foreach (var seed in new[] { 1, 2, 3, 4 })
        {
            foreach (var (_, e) in Watch(Passive(seed), 1500))
            {
                kinds.Add(e.Kind);
                if (e.Kind == IncidentKind.WarBand) Assert.NotNull(e.Side);
            }
        }
        Assert.Equal(Enum.GetValues<IncidentKind>(), kinds.Order());
    }

    [Fact]
    public void Running_events_survive_a_save_and_older_saves_get_events_from_then_on()
    {
        var g = Passive(5);
        RunGalaxy(g, FirstIncident + IncidentSpread + 5);
        g.Ongoing.Add(new Incident { Kind = IncidentKind.Derelict, Planet = 1, Remaining = 12, Ships = new ShipCounts { [ShipClass.Frigate] = 2 } });
        var back = GalaxySave.Deserialize(GalaxySave.Serialize(g))!;
        Assert.Equal(GalaxySave.Serialize(g, 0), GalaxySave.Serialize(back, 0));
        Assert.Equal((g.NextIncident, g.IncidentSeed, g.Incidents), (back.NextIncident, back.IncidentSeed, back.Incidents));
        // A save from before random events: none running, the first one drawn once loaded.
        var old = JsonNode.Parse(GalaxySave.Serialize(Quiet()))!.AsObject();
        foreach (var key in new[] { "incidents", "nextIncident", "incidentSeed", "ongoing" }) Assert.True(old.Remove(key));
        var loaded = GalaxySave.Deserialize(old.ToJsonString())!;
        Assert.True(loaded.Incidents);
        Assert.Empty(loaded.Ongoing);
        StepGalaxy(loaded);
        Assert.InRange(loaded.NextIncident, loaded.Time + FirstIncident, loaded.Time + FirstIncident + IncidentSpread);
    }
}
