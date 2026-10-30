// The galaxy AI beyond the web version: it gathers its fleets to strike together, sends help
// where an enemy fleet draws near, keeps its truces, claims derelicts and brings worn fleets home.
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.Galaxies;
using static SandboxShip.Sim.Tests.SupplyTests;

namespace SandboxShip.Sim.Tests;

public class GalaxyAiTests
{
    /// <summary>The classic galaxy with Nairan on the AI against a passive Kla'ed, watched from off the map.</summary>
    static Galaxy Versus(params string[] nairan)
    {
        var g = CreateGalaxy(Faction.Klaed, [Faction.Nairan], true, errants: false, incidents: false);
        g.Player = Faction.Nautolan;
        foreach (var name in nairan) g.Planets[Id(name)].Owner = Faction.Nairan;
        return g;
    }

    static ShipCounts Frigates(int n) => new() { [ShipClass.Frigate] = n };

    static bool Heading(Fleet f, int planet) => f.At == planet || f.To == planet || f.Path.Contains(planet);

    [Fact]
    public void A_planet_an_enemy_fleet_draws_near_gets_help_from_within_two_lanes()
    {
        foreach (var strong in new[] { true, false })
        {
            var g = Versus("Nadir", "Calyx");
            var nadir = g.Planets[Id("Nadir")];
            nadir.Station = 1;
            // A Kla'ed fleet next to Nadir, at Sorel, two lanes from Calyx.
            AddShipsAt(g, Faction.Klaed, Id("Sorel"), Frigates(strong ? 6 : 1));
            var help = AddShipsAt(g, Faction.Nairan, Id("Calyx"), new ShipCounts { [ShipClass.Battlecruiser] = 4 })!;
            RunGalaxy(g, 1);
            // Stronger than the station alone: the Calyx fleet goes to Nadir, whole. A lone
            // frigate is no danger, and the fleet goes about its business.
            Assert.Equal(strong, Heading(help, nadir.Id) && help.Ships.Of(ShipClass.Battlecruiser) == 4);
        }
    }

    [Fact]
    public void Fleets_too_weak_alone_gather_at_a_staging_planet_and_strike_together()
    {
        // Nairan holds every planet around Nadir, the Kla'ed target; Calyx has a station.
        var g = Versus("Kaldra", "Thessa", "Pélion", "Yvra", "Tarsis", "Brume", "Calyx");
        g.Planets[Id("Calyx")].Station = 1;
        var nadir = g.Planets[Id("Nadir")];
        nadir.Owner = Faction.Klaed;
        nadir.Station = 1;
        var a = AddShipsAt(g, Faction.Nairan, Id("Calyx"), Frigates(4))!;
        var b = AddShipsAt(g, Faction.Nairan, Id("Tarsis"), Frigates(4))!;
        // Neither could take Nadir alone.
        foreach (var f in new[] { a, b }) Expect.Less(FleetStrength(f), DefenseValue(g, nadir) * 1.4 + 300);
        GalaxyBattle? battle = null;
        for (var t = 0; t < 90 && battle is null; t++)
        {
            RunGalaxy(g, 1);
            battle = g.Battles.Find(x => x.PlanetId == nadir.Id);
        }
        Assert.NotNull(battle);
        Assert.Equal(Faction.Nairan, battle.Attacker);
        Expect.GreaterOrEqual(battle.World.Entities.Count(e => e.Faction == Faction.Nairan && e.Type == "frigate"), 8);
        var ai = g.Factions[Faction.Nairan].AI!;
        Assert.Equal((Id("Calyx"), nadir.Id), (ai.Staging, ai.Target));
    }

    [Fact]
    public void The_AI_keeps_its_truce_and_strikes_once_it_runs_out()
    {
        var g = Versus("Calyx", "Thessa", "Brume", "Kaldra", "Tarsis");
        var nadir = g.Planets[Id("Nadir")];
        nadir.Owner = Faction.Klaed;
        nadir.Station = 1;
        AddShipsAt(g, Faction.Nairan, Id("Calyx"), new ShipCounts { [ShipClass.Battlecruiser] = 8 });
        g.Truces.Add(new Truce { A = Faction.Klaed, B = Faction.Nairan, Remaining = 60 });
        RunGalaxy(g, 55);
        Assert.DoesNotContain(g.Events, e => e is BattleStarted);
        Assert.Equal(Faction.Klaed, nadir.Owner);
        RunGalaxy(g, 60);
        Assert.Contains(g.Events, e => e is BattleEnded { Attacker: Faction.Nairan } ended && ended.PlanetId == nadir.Id);
    }

    [Fact]
    public void The_AI_sends_a_ship_to_claim_a_derelict_two_lanes_away()
    {
        var g = Versus("Calyx", "Thessa");
        var maru = Id("Maru");
        g.Ongoing.Add(new Incident { Kind = IncidentKind.Derelict, Planet = maru, Remaining = DerelictTime, Ships = Frigates(3) });
        AddShipsAt(g, Faction.Nairan, Id("Calyx"), Frigates(3));
        RunGalaxy(g, 1);
        var sent = Assert.Single(g.Fleets.Values, f => f.Faction == Faction.Nairan && f.At is null && Heading(f, maru));
        Assert.Equal(1, Battle.CountShips(sent.Ships));
        RunGalaxy(g, 20);
        Assert.Contains(g.Events, e => e is IncidentEnded { Kind: IncidentKind.Derelict, Faction: Faction.Nairan });
    }

    [Fact]
    public void A_worn_fleet_beyond_its_supply_lines_heads_home_to_mend()
    {
        var g = Versus();
        var worn = AddShipsAt(g, Faction.Nairan, Id("Sorel"), new ShipCounts { [ShipClass.Battlecruiser] = 1 }, condition: 0.4)!;
        Assert.False(Supplied(g, worn));
        RunGalaxy(g, 1);
        Assert.Equal(Id("Ostra"), worn.Path.Count > 0 ? worn.Path[^1] : worn.To);
    }
}
