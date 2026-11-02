// The endgame of this version only: the star lance, a weapon striking anywhere on the map,
// the buildable colossus, and warships made dearer so that small craft stay worth it longer.
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.AbilitySystem;
using static SandboxShip.Sim.Steps;
using static SandboxShip.Sim.Structures;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim.Tests;

public class EndgameTests
{
    static World NewWorld(int level = 4)
    {
        var w = CreateWorld(6000, 4000);
        AddFaction(w, Faction.Klaed, 200000);
        AddFaction(w, Faction.Nairan, 200000);
        SpawnStation(w, Faction.Klaed, 700, 2000, level);
        SpawnStation(w, Faction.Nairan, 5300, 2000, level);
        return w;
    }

    static Entity Lance(World w, Faction f = Faction.Klaed, double x = 700, double y = 1550) =>
        SpawnStructure(w, f, BuildableType.StarLance, x, y);

    [Fact]
    public void The_star_lance_needs_the_last_station_level_and_comes_one_per_faction()
    {
        var w = NewWorld(3);
        Assert.Equal("Station niveau 4 requise", CheckPlacement(w, Faction.Klaed, BuildableType.StarLance, 700, 1550).Reason);
        w = NewWorld();
        Assert.True(PlaceStructure(w, Faction.Klaed, BuildableType.StarLance, 700, 1550).Ok);
        Assert.Equal(200000 - 30000, w.Factions[Faction.Klaed].Credits);
        Assert.Equal("Une seule Lance stellaire", CheckPlacement(w, Faction.Klaed, BuildableType.StarLance, 700, 2450).Reason);
    }

    [Fact]
    public void It_charges_only_once_built_then_strikes_anywhere_after_a_warning()
    {
        var w = NewWorld();
        Assert.True(PlaceStructure(w, Faction.Klaed, BuildableType.StarLance, 700, 1550).Ok);
        var lance = w.Entities.First(e => e.Type == "starLance");
        var full = Abilities.Defs[AbilityId.StarLance].Cooldown;
        Run(w, 10);
        Assert.Equal(full, lance.Abilities[0].Cooldown);
        lance.Build = 1;
        Run(w, 10);
        Expect.Close(full - 10, lance.Abilities[0].Cooldown, 3);
        Assert.StartsWith("Prête dans", UseAbility(w, lance, new Aim(5000, 2000)).Reason);

        lance.Abilities[0].Cooldown = 0;
        var station = StationOf(w, Faction.Nairan)!;
        var frigate = SpawnShip(w, Faction.Nairan, ShipClass.Frigate, 5150, 2150);
        // Flown by hand with nothing pressed: it stays under the blow instead of getting out of the way.
        frigate.Pilot = new Pilot();
        // Across the whole map: no range to speak of.
        Assert.True(UseAbility(w, lance, new Aim(station.X, station.Y)).Ok);
        var strike = Assert.Single(w.Strikes);
        Assert.Equal(StrikeKind.Stellar, strike.Kind);
        Run(w, Abilities.StarLanceDelay - 0.5);
        Assert.True(frigate.Hull > 0);
        Run(w, 1);
        Assert.Empty(w.Strikes);
        Assert.DoesNotContain(frigate, w.Entities);
        // A level 4 station comes out of it standing, short of most of its parts.
        Assert.True(station.Hull > 0);
        Assert.True(w.Entities.Count(e => e.ParentId == station.Id) < 4);
    }

    [Fact]
    public void An_ai_fires_its_lance_at_what_is_worth_the_most_but_a_player_fires_their_own()
    {
        var w = NewWorld();
        w.Fog = false;
        w.Factions[Faction.Nairan].AI = NewAI();
        w.Factions[Faction.Nairan].AutoAbilities = true;
        w.Factions[Faction.Klaed].AutoAbilities = true;
        var theirs = Lance(w, Faction.Nairan, 5300, 1550);
        theirs.Abilities[0].Cooldown = 0;
        for (var i = 0; i < 6; i++) SpawnShip(w, Faction.Klaed, ShipClass.Battlecruiser, 2500 + i * 60, 3000);
        SpawnShip(w, Faction.Klaed, ShipClass.Scout, 1500, 600);
        Run(w, 1.5);
        var strike = Assert.Single(w.Strikes);
        Assert.Equal(Faction.Nairan, strike.Side);
        Assert.InRange(strike.X, 2400, 2900);
        // The player's own, charged, waits for an order even with automatic abilities on.
        var mine = Lance(w);
        mine.Abilities[0].Cooldown = 0;
        Run(w, 1.5);
        Assert.Equal(0, mine.Abilities[0].Cooldown);
    }

    [Fact]
    public void The_colossus_is_the_ultimate_ship_one_at_a_time_and_only_the_errants_one_pays_the_raid_bounty()
    {
        var w = NewWorld();
        Assert.True(EnqueueShip(w, Faction.Klaed, ShipClass.Colossus).Ok);
        Assert.Equal(200000 - Ships.Cost(ShipClass.Colossus, Faction.Klaed), w.Factions[Faction.Klaed].Credits);
        Assert.Equal(Ships.Defs[ShipClass.Colossus].LimitReason, CanBuildShip(w, Faction.Klaed, ShipClass.Colossus).Reason);
        Assert.Equal("Station niveau 4 requise", CanBuildShip(NewWorld(3), Faction.Klaed, ShipClass.Colossus).Reason);

        var built = SpawnShip(w, Faction.Klaed, ShipClass.Colossus, 3000, 2000);
        built.LastHitBy = Faction.Nairan;
        built.Hull = 0;
        StepWorld(w);
        Assert.DoesNotContain(w.Events, e => e is RaidWon);
    }

    [Fact]
    public void Warships_cost_more_and_take_longer_the_bigger_they_are()
    {
        // Price per second of build time climbs with size: small craft stay the cheap answer for longer.
        double Rate(ShipClass c) => Ships.Defs[c].Cost / Ships.Defs[c].BuildTime;
        Assert.True(Rate(ShipClass.Frigate) < Rate(ShipClass.Battlecruiser));
        Assert.True(Rate(ShipClass.Battlecruiser) < Rate(ShipClass.Dreadnought));
        Assert.True(Rate(ShipClass.Dreadnought) < Rate(ShipClass.Titan));
        Assert.True(Rate(ShipClass.Titan) < Rate(ShipClass.Leviathan));
        Assert.True(Rate(ShipClass.Leviathan) < Rate(ShipClass.Colossus));
        Assert.Equal(14000, Ships.Defs[ShipClass.Titan].Cost);
    }
}
