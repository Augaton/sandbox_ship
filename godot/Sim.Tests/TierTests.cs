// Ship classes of this version only (not in the web version): the bulwark, the carrier and
// the leviathan.
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.Steps;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim.Tests;

public class TierTests
{
    static World Open()
    {
        var w = CreateWorld(6000, 4000);
        AddFaction(w, Faction.Klaed, 100000);
        AddFaction(w, Faction.Nairan, 100000);
        return w;
    }

    [Fact]
    public void A_bulwark_draws_the_fire_of_the_enemies_around_it()
    {
        var w = Open();
        var bulwark = SpawnShip(w, Faction.Klaed, ShipClass.Bulwark, 1000, 2000);
        // A frail ally right by the enemies: what they would shoot without the taunt.
        SpawnShip(w, Faction.Klaed, ShipClass.Support, 1300, 2000);
        var enemies = Enumerable.Range(0, 3).Select(i => SpawnShip(w, Faction.Nairan, ShipClass.Frigate, 1400, 1900 + i * 100, Math.PI)).ToList();
        Run(w, 1);
        Assert.True(AbilitySystem.UseAbility(w, bulwark).Ok);
        Run(w, 0.6);
        foreach (var e in enemies) Assert.Equal(bulwark.Id, e.TargetId);
        Run(w, Abilities.Taunted);
        foreach (var e in enemies) Assert.Equal(0, e.Effects.Taunted);
    }

    static int Craft(World w, Entity carrier) => w.Entities.Count(e => e.CarrierId == carrier.Id);

    [Fact]
    public void A_carrier_launches_more_craft_than_a_dreadnought_and_faster_with_its_catapults()
    {
        var w = Open();
        var carrier = SpawnShip(w, Faction.Klaed, ShipClass.Carrier, 1000, 1000);
        var dreadnought = SpawnShip(w, Faction.Klaed, ShipClass.Dreadnought, 1000, 3000);
        Run(w, 40);
        Expect.Greater(Craft(w, carrier), Craft(w, dreadnought) + 6);

        var quick = Open();
        var c = SpawnShip(quick, Faction.Klaed, ShipClass.Carrier, 1000, 1000);
        var slow = Open();
        var s = SpawnShip(slow, Faction.Klaed, ShipClass.Carrier, 1000, 1000);
        Run(quick, 3);
        Run(slow, 3);
        Assert.True(AbilitySystem.UseAbility(quick, c).Ok);
        Run(quick, 8);
        Run(slow, 8);
        Expect.Greater(Craft(quick, c), Craft(slow, s));
    }

    [Fact]
    public void One_leviathan_at_most_from_a_level_4_station()
    {
        var w = Open();
        var station = SpawnStation(w, Faction.Klaed, 700, 2000, 3);
        Assert.Equal(Result.Fail("Station niveau 4 requise"), Structures.CanBuildShip(w, Faction.Klaed, ShipClass.Leviathan));
        Assert.True(Structures.EnqueueUpgrade(w, Faction.Klaed).Ok);
        Run(w, Station.Levels[3].UpgradeTime + 0.5);
        Assert.Equal(4, station.Level);
        Assert.True(Structures.EnqueueShip(w, Faction.Klaed, ShipClass.Leviathan).Ok);
        Assert.Equal(Result.Fail("Un seul Léviathan à la fois"), Structures.CanBuildShip(w, Faction.Klaed, ShipClass.Leviathan));
    }

    [Fact]
    public void Platforms_are_capped_per_build_area()
    {
        var w = Open();
        SpawnStation(w, Faction.Klaed, 1000, 2000);
        for (var i = 0; i < Buildables.StationPlatforms; i++)
        {
            var a = i * Math.PI * 2 / 5;
            Assert.True(Structures.PlaceStructure(w, Faction.Klaed, BuildableType.Platform, 1000 + Math.Cos(a) * 300, 2000 + Math.Sin(a) * 300).Ok);
        }
        Assert.Equal(Result.Fail("Plateformes au maximum ici (3)"),
            Structures.PlaceStructure(w, Faction.Klaed, BuildableType.Platform, 1000, 2350));
        // A mine opens room for one more, next to it.
        var field = AddField(w, 2400, 2000, 150);
        var mine = SpawnStructure(w, Faction.Klaed, BuildableType.Mine, field.X, field.Y);
        field.MineId = mine.Id;
        Assert.Equal(1, Structures.PlatformRoom(w, Faction.Klaed));
        Assert.True(Structures.PlaceStructure(w, Faction.Klaed, BuildableType.Platform, 2400, 2250).Ok);
        Assert.False(Structures.PlaceStructure(w, Faction.Klaed, BuildableType.Platform, 2400, 1750).Ok);
        Assert.Equal(0, Structures.PlatformRoom(w, Faction.Klaed));
    }
}
