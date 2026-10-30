// Planet specials not in the web version: nebulae, relays and ancient ruins. Generated galaxies
// get them on top of the web version's layout, which stays as the fixtures expect it.
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.Galaxies;
using static SandboxShip.Sim.Tests.SupplyTests;

namespace SandboxShip.Sim.Tests;

public class SpecialsTests
{
    static readonly PlanetSpecial[] New = [PlanetSpecial.Nebula, PlanetSpecial.Relay, PlanetSpecial.Ruins];

    [Fact]
    public void Generated_galaxies_get_new_specials_apart_from_the_web_layout_and_the_classic_map_none()
    {
        foreach (var seed in new[] { 1, 2, 21, 42, 99 })
        {
            foreach (var size in new[] { MapSize.Small, MapSize.Medium, MapSize.Large })
            {
                var layout = GalaxyGen.GenerateLayout(seed, 2, size);
                // The layout itself keeps the web version's draws: only the four old kinds.
                Assert.DoesNotContain(layout.Planets, p => p.Special is PlanetSpecial s && New.Contains(s));
                var g = CreateGalaxy(Faction.Klaed, [Faction.Nairan], true, seed, layout);
                var added = g.Planets.Where(p => p.Special is PlanetSpecial s && New.Contains(s)).ToList();
                Assert.Equal(Math.Max(2, g.Planets.Count / 5), added.Count);
                foreach (var p in added)
                {
                    Assert.Null(layout.Planets[p.Id].Special);
                    Assert.DoesNotContain(p.Id, layout.Capitals);
                }
                // Every old special is where the layout put it.
                foreach (var p in g.Planets.Where(p => !added.Contains(p))) Assert.Equal(layout.Planets[p.Id].Special, p.Special);
                // The same seed gives the same ones.
                var again = CreateGalaxy(Faction.Klaed, [Faction.Nairan], true, seed, GalaxyGen.GenerateLayout(seed, 2, size));
                Assert.Equal(g.Planets.Select(p => p.Special), again.Planets.Select(p => p.Special));
            }
        }
        Assert.DoesNotContain(CreateGalaxy(Faction.Klaed, [Faction.Nairan]).Planets, p => p.Special is PlanetSpecial s && New.Contains(s));
    }

    [Fact]
    public void A_nebula_hides_the_fleets_docked_in_it_from_their_enemies()
    {
        var g = Quiet();
        var orin = g.Planets[Id("Orin")];
        orin.Special = PlanetSpecial.Nebula;
        // Orin is next to the Kla'ed capital: in sight.
        var hidden = AddShipsAt(g, Faction.Nairan, orin.Id, new ShipCounts { [ShipClass.Frigate] = 3 })!;
        Assert.Contains(orin.Id, VisiblePlanets(g, Faction.Klaed));
        Assert.False(FleetVisible(g, Faction.Klaed, hidden));
        Assert.True(FleetVisible(g, Faction.Nairan, hidden));
        // Once it leaves, it shows.
        Assert.True(SendFleet(g, hidden.Id, Id("Vesper")).Ok);
        Assert.True(FleetVisible(g, Faction.Klaed, hidden));
        // The Errants are known to all, nebula or not.
        var guard = AddShipsAt(g, Faction.Errant, orin.Id, new ShipCounts { [ShipClass.Frigate] = 1 })!;
        Assert.True(FleetVisible(g, Faction.Klaed, guard));
    }

    [Fact]
    public void A_relay_speeds_its_owner_s_fleets_on_the_lane_they_leave_it_by()
    {
        var g = Quiet();
        var orin = g.Planets[Id("Orin")];
        orin.Special = PlanetSpecial.Relay;
        orin.Owner = Faction.Klaed;
        var ships = new ShipCounts { [ShipClass.Frigate] = 1 };
        var own = AddShipsAt(g, Faction.Klaed, orin.Id, ships)!;
        var other = AddShipsAt(g, Faction.Nairan, Id("Maru"), ships)!;
        // Kla'ed leaves the relay; Nairan, the same ship, leaves a planet without one.
        Assert.True(SendFleet(g, own.Id, Id("Sorel")).Ok);
        Assert.True(SendFleet(g, other.Id, Id("Thessa")).Ok);
        StepGalaxy(g);
        var speed = FleetSpeed(ships) * Step;
        Expect.Close(speed * RelaySpeed / Worlds.Dist(orin, g.Planets[Id("Sorel")]), own.Progress, 9);
        Expect.Close(speed / Worlds.Dist(g.Planets[Id("Maru")], g.Planets[Id("Thessa")]), other.Progress, 9);
        // Someone else's relay does nothing for a fleet.
        orin.Owner = Faction.Nairan;
        Assert.Equal(1, LaneSpeed(g, own));
    }

    [Fact]
    public void Ancient_ruins_pay_the_first_faction_to_take_them_and_only_it()
    {
        var g = Quiet();
        var orin = g.Planets[Id("Orin")];
        orin.Special = PlanetSpecial.Ruins;
        var f = g.Factions[Faction.Klaed];
        f.Credits = 0;
        var levels = Techs.All.Sum(t => f.Research[t]);
        Assert.True(SendFleet(g, FleetsAt(g, Id("Vesper"), Faction.Klaed)[0].Id, orin.Id).Ok);
        RunGalaxy(g, 15);
        Assert.Equal(Faction.Klaed, orin.Owner);
        var found = Assert.Single(g.Events.OfType<RuinsExplored>());
        Assert.Equal((orin.Id, Faction.Klaed, RuinsCredits), (found.PlanetId, found.Faction, found.Credits));
        Assert.Equal(levels + 1, Techs.All.Sum(t => f.Research[t]));
        Assert.Equal(1, f.Research[found.Tech!.Value]);
        Expect.GreaterOrEqual(f.Credits, RuinsCredits);
        // Spent: whoever takes the planet next finds nothing.
        Assert.Null(orin.Special);
        orin.Owner = Faction.Nairan;
        RunGalaxy(g, 1);
        Assert.Single(g.Events.OfType<RuinsExplored>());
    }

    [Fact]
    public void Ruins_never_raise_a_tech_past_its_last_level_counting_what_is_queued()
    {
        var g = Quiet();
        var f = g.Factions[Faction.Klaed];
        // Every tech at the top but one, queued to the top too: nothing left to give.
        foreach (var t in Techs.All) f.Research[t] = Techs.Defs[t].MaxLevel;
        f.Research[TechId.Engines] = Techs.Defs[TechId.Engines].MaxLevel - 1;
        f.Researching = new ResearchJob(TechId.Engines, 10, 0);
        var orin = g.Planets[Id("Orin")];
        orin.Special = PlanetSpecial.Ruins;
        orin.Owner = Faction.Klaed;
        StepGalaxy(g);
        Assert.Null(Assert.Single(g.Events.OfType<RuinsExplored>()).Tech);
        Assert.Equal(Techs.Defs[TechId.Engines].MaxLevel - 1, f.Research[TechId.Engines]);
    }
}
