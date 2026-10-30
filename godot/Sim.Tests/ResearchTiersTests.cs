// Research of this version only: four more techs, and levels past the web's third that need
// the last station level.
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.AbilitySystem;
using static SandboxShip.Sim.Steps;
using static SandboxShip.Sim.Structures;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim.Tests;

public class ResearchTiersTests
{
    static World NewWorld()
    {
        var w = CreateWorld(6000, 4000);
        AddFaction(w, Faction.Klaed, 100000);
        AddFaction(w, Faction.Nairan, 100000);
        return w;
    }

    [Fact]
    public void Combat_techs_go_to_level_5_on_the_last_station_and_hangars_stop_at_3()
    {
        var w = NewWorld();
        var station = SpawnStation(w, Faction.Klaed, 700, 2000, 3);
        SpawnStructure(w, Faction.Klaed, BuildableType.Lab, 900, 2400);
        var f = w.Factions[Faction.Klaed];
        f.Research[TechId.Weapons] = 3;
        Assert.Equal(Result.Fail("Station niveau 4 requise"), CanResearch(w, Faction.Klaed, TechId.Weapons));
        station.Level = 4;
        Assert.True(CanResearch(w, Faction.Klaed, TechId.Weapons).Ok);
        // The fifth level asks for no fifth station level: there is none.
        f.Research[TechId.Weapons] = 4;
        Assert.True(CanResearch(w, Faction.Klaed, TechId.Weapons).Ok);
        f.Research[TechId.Weapons] = 5;
        Assert.Equal(Result.Fail("Niveau maximum"), CanResearch(w, Faction.Klaed, TechId.Weapons));
        f.Research[TechId.Hangars] = 3;
        Assert.Equal(Result.Fail("Niveau maximum"), CanResearch(w, Faction.Klaed, TechId.Hangars));
        Assert.True(CanResearch(w, Faction.Klaed, TechId.Energy).Ok);
    }

    [Fact]
    public void Logistics_builds_ships_faster()
    {
        var w = NewWorld();
        SpawnStation(w, Faction.Klaed, 700, 2000, 2);
        w.Factions[Faction.Klaed].Research[TechId.Logistics] = 2;
        Assert.True(EnqueueShip(w, Faction.Klaed, ShipClass.Frigate).Ok);
        Expect.Close(Ships.Defs[ShipClass.Frigate].BuildTime * 0.86, w.Factions[Faction.Klaed].Queue[0].Total, 6);
    }

    [Fact]
    public void Repair_research_mends_faster()
    {
        double Mended(int level)
        {
            var w = NewWorld();
            w.Factions[Faction.Klaed].Research[TechId.Repair] = level;
            SpawnShip(w, Faction.Klaed, ShipClass.Support, 1000, 1000);
            var hurt = SpawnShip(w, Faction.Klaed, ShipClass.Battlecruiser, 1120, 1000);
            hurt.Hull = hurt.MaxHull / 2;
            var before = hurt.Hull;
            Run(w, 2);
            return hurt.Hull - before;
        }
        Expect.Greater(Mended(0), 0);
        Expect.Close(Mended(0) * 1.5, Mended(2), 3);
    }

    [Fact]
    public void Fortifications_strengthen_platforms_and_station_guns_not_ships()
    {
        var w = NewWorld();
        var station = SpawnStation(w, Faction.Klaed, 700, 2000);
        var gun = w.Entities.First(e => e.Kind == EntityKind.Hardpoint && e.ParentId == station.Id && e.Weapons.Count > 0);
        var platform = SpawnStructure(w, Faction.Klaed, BuildableType.Platform, 1000, 2000);
        var ship = SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 1500, 2000);
        var (g0, p0, s0) = (Combat.DamageMult(w, gun), Combat.DamageMult(w, platform), Combat.DamageMult(w, ship));
        w.Factions[Faction.Klaed].Research[TechId.Fortification] = 3;
        Expect.Close(g0 * 1.3, Combat.DamageMult(w, gun), 6);
        Expect.Close(p0 * 1.3, Combat.DamageMult(w, platform), 6);
        Assert.Equal(s0, Combat.DamageMult(w, ship));
    }

    [Fact]
    public void Energy_recharges_abilities_faster()
    {
        var w = NewWorld();
        w.Factions[Faction.Klaed].Research[TechId.Energy] = 2;
        var ship = SpawnShip(w, Faction.Klaed, ShipClass.Battlecruiser, 1000, 1000);
        Assert.True(UseAbility(w, ship).Ok);
        var a = ship.Abilities[0];
        Expect.Close(Abilities.Defs[a.Id].Cooldown * 0.88, a.Cooldown, 6);
    }

    [Fact]
    public void In_the_campaign_logistics_speeds_production_and_the_last_levels_need_a_level_4_station()
    {
        var g = Galaxies.CreateGalaxy(Faction.Klaed, [Faction.Nairan], false, errants: false);
        var capital = g.Planets.First(p => p.Owner == Faction.Klaed);
        var frigate = GalaxyBuild.Ship(ShipClass.Frigate);
        var before = Galaxies.BuildTime(g, capital, frigate);
        var f = g.Factions[Faction.Klaed];
        f.Research[TechId.Logistics] = 3;
        Expect.Close(before * 0.79, Galaxies.BuildTime(g, capital, frigate), 6);
        f.Credits = 1e6;
        capital.Lab = 1;
        capital.Station = 3;
        f.Research[TechId.Weapons] = 3;
        Assert.Equal(Result.Fail("Station niveau 4 requise"), Galaxies.CanResearchTech(g, Faction.Klaed, TechId.Weapons));
        capital.Station = 4;
        Assert.True(Galaxies.CanResearchTech(g, Faction.Klaed, TechId.Weapons).Ok);
    }
}
