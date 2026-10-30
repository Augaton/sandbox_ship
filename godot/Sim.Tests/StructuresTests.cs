// Port of tests/structures.test.ts.
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.Steps;
using static SandboxShip.Sim.Structures;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim.Tests;

public class StructuresTests
{
    static World NewWorld(double credits = 0)
    {
        var w = CreateWorld(6000, 4000);
        AddFaction(w, Faction.Klaed, credits);
        AddFaction(w, Faction.Nairan, 0);
        return w;
    }

    static List<Entity> ShipsOf(World w, Faction faction, string? type = null) =>
        w.Entities.Where(e => e.Kind == EntityKind.Ship && e.Faction == faction && (type is null || e.Type == type)).ToList();

    // Production.

    [Fact]
    public void Debits_credits_refuses_when_short_and_delivers_the_ship()
    {
        var w = NewWorld(700);
        SpawnStation(w, Faction.Klaed, 700, 2000);
        Assert.True(EnqueueShip(w, Faction.Klaed, ShipClass.Frigate).Ok);
        Assert.Equal(50, w.Factions[Faction.Klaed].Credits);
        var r = EnqueueShip(w, Faction.Klaed, ShipClass.Frigate);
        Assert.Equal(Result.Fail("Crédits insuffisants"), r);
        Run(w, Ships.Defs[ShipClass.Frigate].BuildTime + 0.5);
        Assert.Single(ShipsOf(w, Faction.Klaed, "frigate"));
    }

    [Fact]
    public void Refunds_a_cancelled_item()
    {
        var w = NewWorld(1000);
        SpawnStation(w, Faction.Klaed, 700, 2000);
        EnqueueShip(w, Faction.Klaed, ShipClass.Frigate);
        CancelQueued(w, Faction.Klaed, 0);
        Assert.Equal(1000, w.Factions[Faction.Klaed].Credits);
        Assert.Empty(w.Factions[Faction.Klaed].Queue);
    }

    [Fact]
    public void Locks_ship_classes_behind_station_levels()
    {
        var w = NewWorld(100000);
        SpawnStation(w, Faction.Klaed, 700, 2000);
        Assert.False(EnqueueShip(w, Faction.Klaed, ShipClass.Battlecruiser).Ok);
        Assert.False(EnqueueShip(w, Faction.Klaed, ShipClass.Leviathan).Ok);
        // Fighters only came from hangars in the web version; the shipyard builds them here.
        Assert.True(EnqueueShip(w, Faction.Klaed, ShipClass.Fighter).Ok);
        Assert.True(EnqueueUpgrade(w, Faction.Klaed).Ok);
        Assert.False(EnqueueUpgrade(w, Faction.Klaed).Ok);
        Run(w, Station.Levels[1].UpgradeTime + 0.5);
        Assert.True(EnqueueShip(w, Faction.Klaed, ShipClass.Battlecruiser).Ok);
    }

    [Fact]
    public void An_upgrade_raises_the_hull_and_adds_hardpoints()
    {
        var w = NewWorld(100000);
        var st = SpawnStation(w, Faction.Klaed, 700, 2000);
        var before = ChildrenOf(w, st.Id).Count;
        EnqueueUpgrade(w, Faction.Klaed);
        Run(w, Station.Levels[1].UpgradeTime + 0.5);
        Assert.Equal(2, st.Level);
        Assert.Equal(Station.Levels[1].Hull, st.MaxHull);
        Assert.Equal(before + Station.Levels[1].Hardpoints.Count, ChildrenOf(w, st.Id).Count);
    }

    [Fact]
    public void A_destroyed_shipyard_stops_ship_production()
    {
        var w = NewWorld(5000);
        var st = SpawnStation(w, Faction.Klaed, 700, 2000);
        EnqueueShip(w, Faction.Klaed, ShipClass.Scout);
        ChildrenOf(w, st.Id).Find(c => c.Type == "shipyard")!.Hull = 0;
        Run(w, Ships.Defs[ShipClass.Scout].BuildTime + 1);
        Assert.Empty(ShipsOf(w, Faction.Klaed, "scout"));
        Assert.Equal(Result.Fail("Chantier naval détruit"), EnqueueShip(w, Faction.Klaed, ShipClass.Scout));
    }

    // Hangars.

    [Fact]
    public void Hangars_never_exceed_their_cap_of_living_craft()
    {
        var w = NewWorld();
        var st = SpawnStation(w, Faction.Klaed, 700, 2000);
        var hangar = ChildrenOf(w, st.Id).Find(c => c.Type == "hangar")!;
        Run(w, 300);
        Assert.True(hangar.Hangar!.Launched.Count <= hangar.Hangar.Def.Cap);
        Assert.NotEmpty(ShipsOf(w, Faction.Klaed, "fighter"));
        Assert.NotEmpty(ShipsOf(w, Faction.Klaed, "bomber"));
    }

    [Fact]
    public void Hangars_relaunch_once_craft_are_lost_stop_when_destroyed_and_start_again_once_rebuilt()
    {
        var w = NewWorld();
        var st = SpawnStation(w, Faction.Klaed, 700, 2000);
        var hangar = ChildrenOf(w, st.Id).Find(c => c.Type == "hangar")!;
        Run(w, 100);
        foreach (var s in ShipsOf(w, Faction.Klaed)) s.Hull = 0;
        Run(w, 25);
        Assert.NotEmpty(ShipsOf(w, Faction.Klaed));
        hangar.Hull = 0;
        StepWorld(w);
        foreach (var s in ShipsOf(w, Faction.Klaed)) s.Hull = 0;
        Run(w, Buildables.RebuildTime - 10);
        Assert.Empty(ShipsOf(w, Faction.Klaed));
        Run(w, 60);
        Assert.Contains(ChildrenOf(w, st.Id), c => c.Type == "hangar");
        Assert.NotEmpty(ShipsOf(w, Faction.Klaed));
    }

    // Economy and placement.

    [Fact]
    public void A_station_and_its_built_mines_earn_credits()
    {
        var w = NewWorld();
        SpawnStation(w, Faction.Klaed, 700, 2000);
        SpawnStructure(w, Faction.Klaed, BuildableType.Mine, 1500, 1500);
        Run(w, 10);
        var expected = (Station.Levels[0].Income + Buildables.Defs[BuildableType.Mine].Income) * 10;
        Expect.Close(expected, w.Factions[Faction.Klaed].Credits, 0);
    }

    [Fact]
    public void A_mine_under_construction_earns_nothing()
    {
        var w = NewWorld(Buildables.Defs[BuildableType.Mine].Cost);
        SpawnStation(w, Faction.Klaed, 700, 2000);
        AddField(w, 1000, 1700, 150);
        Assert.True(PlaceStructure(w, Faction.Klaed, BuildableType.Mine, 1020, 1690).Ok);
        Run(w, 5);
        Expect.Close(Station.Levels[0].Income * 5, w.Factions[Faction.Klaed].Credits, 0);
    }

    [Fact]
    public void Mines_need_a_free_asteroid_field_and_snap_onto_it()
    {
        var w = NewWorld(10000);
        SpawnStation(w, Faction.Klaed, 700, 2000);
        var field = AddField(w, 3000, 1000, 150);
        Assert.False(CheckPlacement(w, Faction.Klaed, BuildableType.Mine, 2000, 2000).Ok);
        // Far from the station, a ship has to hold the field first.
        var p = CheckPlacement(w, Faction.Klaed, BuildableType.Mine, 3000, 1000);
        Assert.False(p.Ok);
        Assert.Equal("Un vaisseau doit tenir la position", p.Reason);
        SpawnShip(w, Faction.Klaed, ShipClass.Scout, 3100, 1100);
        // And no fight may be going on there.
        var raider = SpawnShip(w, Faction.Nairan, ShipClass.Scout, 3300, 1000);
        p = CheckPlacement(w, Faction.Klaed, BuildableType.Mine, 3000, 1000);
        Assert.False(p.Ok);
        Assert.Equal("Combat trop proche", p.Reason);
        w.Entities.Remove(raider.Id);
        p = CheckPlacement(w, Faction.Klaed, BuildableType.Mine, 3050, 1020);
        Assert.True(p.Ok);
        Assert.Equal(field.X, p.X);
        Assert.Equal(field.Y, p.Y);
        PlaceStructure(w, Faction.Klaed, BuildableType.Mine, 3050, 1020);
        p = CheckPlacement(w, Faction.Klaed, BuildableType.Mine, 3000, 1000);
        Assert.False(p.Ok);
        Assert.Equal("Champ déjà exploité", p.Reason);
    }

    [Fact]
    public void Other_structures_stay_inside_the_construction_radius_and_off_occupied_spots()
    {
        var w = NewWorld(10000);
        SpawnStation(w, Faction.Klaed, 700, 2000);
        Assert.False(CheckPlacement(w, Faction.Klaed, BuildableType.Platform, 700 + Station.ConstructionRadius + 50, 2000).Ok);
        Assert.False(CheckPlacement(w, Faction.Klaed, BuildableType.Platform, 700, 2000).Ok);
        Assert.True(CheckPlacement(w, Faction.Klaed, BuildableType.Platform, 1000, 2300).Ok);
        var p = CheckPlacement(w, Faction.Klaed, BuildableType.IonCannon, 1000, 2300);
        Assert.False(p.Ok);
        Assert.Equal("Station niveau 3 requise", p.Reason);
    }

    [Fact]
    public void A_structure_only_works_once_built()
    {
        var w = NewWorld(Buildables.Defs[BuildableType.Platform].Cost);
        SpawnStation(w, Faction.Klaed, 700, 2000);
        PlaceStructure(w, Faction.Klaed, BuildableType.Platform, 1000, 2300);
        var site = w.Entities.First(e => e.Type == "platform");
        Assert.Equal(0, site.Build);
        Run(w, Buildables.Defs[BuildableType.Platform].BuildTime + 0.5);
        Assert.Equal(1, site.Build);
        Expect.Close(site.MaxHull, site.Hull, 0);
        Assert.Contains(w.Events, e => e is Built b && b.Id == site.Id);
    }
}
