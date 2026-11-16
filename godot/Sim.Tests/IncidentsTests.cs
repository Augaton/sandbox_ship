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
    public void A_war_band_leaves_a_planet_with_nothing_but_platforms_in_orbit()
    {
        var g = WarBand(new ShipCounts { [ShipClass.Frigate] = 3 });
        var orin = g.Planets[Id("Orin")];
        orin.Platforms = 1;
        orin.RepairDock = 1;
        RunGalaxy(g, WarBandWarning + 20);
        // Without a station, they hold nothing: a battle over at once, over and over, had the band stay for good.
        Assert.DoesNotContain(g.Fleets.Values, f => f.Side == Faction.Errant);
        Assert.Equal((Faction.Klaed, 0), (orin.Owner!.Value, orin.Mines));
        Expect.Less(g.Events.Count(e => e is BattleStarted), 3);
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
    public void A_war_band_comes_out_of_the_dark_on_its_lane_when_no_neutral_planet_is_left()
    {
        var g = Quiet();
        var (orin, kaldra) = (g.Planets[Id("Orin")], g.Planets[Id("Kaldra")]);
        orin.Owner = Faction.Klaed;
        orin.Mines = 2;
        kaldra.Owner = Faction.Nairan;
        g.Ongoing.Add(new Incident
        {
            Kind = IncidentKind.WarBand, Planet = kaldra.Id, Other = orin.Id, Remaining = WarBandWarning, Ships = new ShipCounts { [ShipClass.Frigate] = 3 },
        });
        // Nothing stands anywhere for now: nobody can strike it, and it strikes nobody on the way.
        RunGalaxy(g, WarBandWarning - 1);
        Assert.DoesNotContain(g.Fleets.Values, f => f.Side == Faction.Errant);
        RunGalaxy(g, 1.5);
        var band = Assert.Single(g.Fleets.Values, f => f.Side == Faction.Errant);
        Assert.Equal((kaldra.Id, orin.Id), (band.From, band.To!.Value));
        RunGalaxy(g, 20);
        Assert.Equal((Faction.Klaed, 0), (orin.Owner!.Value, orin.Mines));
        Assert.DoesNotContain(g.Fleets.Values, f => f.Side == Faction.Errant);
        Assert.Contains(g.Events, e => e is IncidentEnded { Kind: IncidentKind.WarBand } ended && ended.Side == Faction.Klaed && ended.Planet == orin.Id);
    }

    [Fact]
    public void With_no_neutral_planet_left_derelicts_and_war_bands_keep_coming_at_the_borders()
    {
        var kinds = new HashSet<IncidentKind>();
        foreach (var seed in new[] { 1, 2, 3, 4 })
        {
            var g = Passive(seed);
            foreach (var p in g.Planets) p.Owner ??= p.Id % 2 == 0 ? Faction.Klaed : Faction.Nairan;
            foreach (var (_, e) in Watch(g, 1500))
            {
                kinds.Add(e.Kind);
                if (e.Kind == IncidentKind.Derelict) Assert.Contains(Neighbours(g, e.Planet), n => g.Planets[n].Owner != g.Planets[e.Planet].Owner);
                if (e.Kind == IncidentKind.WarBand) Assert.Contains(e.Other, Neighbours(g, e.Planet));
            }
        }
        Assert.Contains(IncidentKind.Derelict, kinds);
        Assert.Contains(IncidentKind.WarBand, kinds);
    }

    [Fact]
    public void An_ion_storm_shuts_its_planet_off_fleets_go_around_it_or_stay()
    {
        var g = Quiet();
        var (vesper, orin, maru, kaldra, nadir) = (Id("Vesper"), Id("Orin"), Id("Maru"), Id("Kaldra"), Id("Nadir"));
        void Storm(int at) => g.Ongoing.Add(new Incident { Kind = IncidentKind.IonStorm, Planet = at, Remaining = StormTime });
        Assert.Equal(orin, FindPath(g, vesper, kaldra)![0]);
        Storm(orin);
        Assert.Equal(maru, FindPath(g, vesper, kaldra)![0]);
        Assert.Equal(orin, FindPath(g, vesper, kaldra, true)![0]);
        g.Ongoing.Clear();
        // A fleet that set out before it goes around once it gets there.
        var fleet = AddShipsAt(g, Faction.Klaed, vesper, new ShipCounts { [ShipClass.Scout] = 1 })!;
        Assert.True(SendFleet(g, fleet.Id, nadir).Ok);
        Assert.Equal(orin, fleet.To);
        var next = fleet.Path[0];
        Storm(next);
        for (var t = 0; t < 200 && fleet.To == orin; t++) StepGalaxy(g);
        Assert.Equal(orin, fleet.From);
        Assert.NotEqual(next, fleet.To);
        Assert.Equal(nadir, fleet.Path[^1]);
        g.Ongoing.Clear();
        // With no way around, it stays where it got to.
        var stays = AddShipsAt(g, Faction.Klaed, vesper, new ShipCounts { [ShipClass.Scout] = 1 })!;
        Assert.True(SendFleet(g, stays.Id, nadir).Ok);
        Storm(nadir);
        for (var t = 0; t < 400 && stays.At is null; t++) StepGalaxy(g);
        var there = stays.At!.Value;
        Assert.Contains(nadir, Neighbours(g, there));
        // A planet shut off says why, from outside as from inside; once the storm is over, the way is open again.
        Assert.Equal("Tempête ionique : voie fermée", SendFleet(g, stays.Id, nadir).Reason);
        g.Ongoing[0].Planet = there;
        Assert.Equal("Tempête ionique : voie fermée", SendFleet(g, stays.Id, vesper).Reason);
        g.Ongoing[0].Remaining = 0.05;
        StepGalaxy(g);
        Assert.Empty(g.Ongoing);
        Assert.True(SendFleet(g, stays.Id, vesper).Ok);
    }

    [Fact]
    public void A_fleet_falling_back_goes_through_the_storm()
    {
        var g = Quiet();
        var (orin, kaldra) = (Id("Orin"), g.Planets[Id("Kaldra")]);
        kaldra.Owner = Faction.Nairan;
        kaldra.Station = 1;
        var fleet = AddShipsAt(g, Faction.Klaed, orin, new ShipCounts { [ShipClass.Frigate] = 3 })!;
        Assert.True(SendFleet(g, fleet.Id, kaldra.Id).Ok);
        for (var t = 0; t < 200 && g.Battles.Count == 0; t++) StepGalaxy(g);
        g.Ongoing.Add(new Incident { Kind = IncidentKind.IonStorm, Planet = kaldra.Id, Remaining = StormTime });
        ConcludeBattle(g, g.Battles[0].Id, BattleResult.Retreat);
        var back = Assert.Single(g.Fleets.Values, f => f.Side == Faction.Klaed && f.Ships.Of(ShipClass.Frigate) > 0 && f.From == kaldra.Id);
        Assert.Equal((null, orin), (back.At, back.To!.Value));
    }

    [Fact]
    public void An_ion_storm_slows_the_fleets_it_catches_on_its_lane_for_a_while()
    {
        var g = Quiet();
        var (orin, kaldra, maru) = (Id("Orin"), Id("Kaldra"), Id("Maru"));
        var ships = new ShipCounts { [ShipClass.Frigate] = 1 };
        var slow = AddShipsAt(g, Faction.Klaed, orin, ships)!;
        var fast = AddShipsAt(g, Faction.Nairan, maru, ships)!;
        Assert.True(SendFleet(g, slow.Id, kaldra).Ok);
        Assert.True(SendFleet(g, fast.Id, kaldra).Ok);
        g.Ongoing.Add(new Incident { Kind = IncidentKind.IonStorm, Planet = orin, Remaining = StormTime });
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
