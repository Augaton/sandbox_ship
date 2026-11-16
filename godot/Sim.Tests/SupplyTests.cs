// Supply (not in the web version): fleets more than two lanes away from a station of their
// faction wear out slowly, down to a floor, and mend no more; the Errants are exempt.
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.Galaxies;

namespace SandboxShip.Sim.Tests;

public class SupplyTests
{
    internal static int Id(string name) => GalaxyMap.Planets.ToList().FindIndex(p => p.Name == name);

    /// <summary>The classic galaxy without Errants, random events or AI, watched from off the map.</summary>
    internal static Galaxy Quiet()
    {
        var g = CreateGalaxy(Faction.Klaed, [Faction.Nairan], false, errants: false, incidents: false);
        g.Player = Faction.Nautolan;
        return g;
    }

    static ShipCounts Frigate => new() { [ShipClass.Frigate] = 1 };

    [Fact]
    public void A_fleet_is_supplied_within_two_lanes_of_a_station_and_on_lanes_touching_that_zone()
    {
        var g = Quiet();
        // Vesper has the Kla'ed station; Orin is a lane from it, Kaldra two, Nadir three.
        var home = AddShipsAt(g, Faction.Klaed, Id("Vesper"), Frigate)!;
        var near = AddShipsAt(g, Faction.Klaed, Id("Orin"), Frigate)!;
        var edge = AddShipsAt(g, Faction.Klaed, Id("Kaldra"), Frigate)!;
        var far = AddShipsAt(g, Faction.Klaed, Id("Nadir"), Frigate)!;
        Assert.True(Supplied(g, home));
        Assert.True(Supplied(g, near));
        Assert.True(Supplied(g, edge));
        Assert.False(Supplied(g, far));
        // On the lane from Nadir back to Kaldra: one end is in the zone.
        Assert.True(SendFleet(g, far.Id, Id("Kaldra")).Ok);
        Assert.True(Supplied(g, far));
        // Nadir to Calyx: neither end is.
        var deeper = AddShipsAt(g, Faction.Klaed, Id("Nadir"), Frigate)!;
        Assert.True(SendFleet(g, deeper.Id, Id("Calyx")).Ok);
        Assert.False(Supplied(g, deeper));
    }

    [Fact]
    public void A_planet_of_its_own_is_no_supply_only_a_station_is_its_own_or_an_ally_s()
    {
        var g = Quiet();
        var fleet = AddShipsAt(g, Faction.Klaed, Id("Nadir"), Frigate)!;
        g.Planets[Id("Nadir")].Owner = Faction.Klaed;
        Assert.False(Supplied(g, fleet));
        // A station under construction does not count yet.
        g.Planets[Id("Nadir")].Buildings.Add(new GalaxyQueueItem { Kind = GalaxyBuildKind.Station, Remaining = 40, Total = 40 });
        Assert.False(Supplied(g, fleet));
        g.Planets[Id("Nadir")].Buildings.Clear();
        g.Planets[Id("Nadir")].Station = 1;
        Assert.True(Supplied(g, fleet));
        // An ally's serves too; a rival's does not.
        g.Planets[Id("Nadir")].Station = 0;
        g.Planets[Id("Calyx")].Owner = Faction.Nairan;
        g.Planets[Id("Calyx")].Station = 1;
        Assert.False(Supplied(g, fleet));
        g.Pacts.Add(new Pact { Kind = DealKind.Alliance, A = Faction.Klaed, B = Faction.Nairan });
        Assert.True(Supplied(g, fleet));
    }

    [Fact]
    public void Out_of_supply_a_fleet_loses_a_tenth_of_its_hull_a_minute_down_to_the_floor()
    {
        var g = Quiet();
        var far = AddShipsAt(g, Faction.Klaed, Id("Nadir"), new ShipCounts { [ShipClass.Frigate] = 2 })!;
        var near = FleetsAt(g, Id("Vesper"), Faction.Klaed)[0];
        // A Nairan planet under truce: the fleet can neither take it, and so supply itself, nor fight there.
        g.Planets[Id("Nadir")].Owner = Faction.Nairan;
        g.Truces.Add(new Truce { A = Faction.Klaed, B = Faction.Nairan, Remaining = 3600 });
        RunGalaxy(g, 60);
        Expect.Close(0.9, far.Condition, 3);
        Assert.Equal(1, near.Condition);
        RunGalaxy(g, 600);
        Assert.Equal(SupplyFloor, far.Condition);
        // A fleet worn below the floor in battle stays as it is.
        far.Condition = 0.2;
        RunGalaxy(g, 10);
        Assert.Equal(0.2, far.Condition);
    }

    [Fact]
    public void Out_of_supply_a_fleet_mends_no_more_even_at_home_until_a_station_stands_near()
    {
        var g = Quiet();
        var nadir = g.Planets[Id("Nadir")];
        nadir.Owner = Faction.Klaed;
        var fleet = AddShipsAt(g, Faction.Klaed, nadir.Id, Frigate, condition: 0.7)!;
        RunGalaxy(g, 60);
        Expect.Close(0.6, fleet.Condition, 3);
        nadir.Station = 1;
        RunGalaxy(g, 10);
        Expect.Close(0.6 + 10 * RepairStation, fleet.Condition, 3);
    }

    [Fact]
    public void Biomechanical_hulls_stop_mending_out_of_supply_and_the_Errants_never_wear()
    {
        var g = CreateGalaxy(Faction.Nautolan, [Faction.Nairan], false, errants: false, incidents: false);
        g.Player = Faction.Klaed;
        // Orin is next to the Nautolan capital, Nadir three lanes away; a second is too short to take either.
        var mending = AddShipsAt(g, Faction.Nautolan, Id("Orin"), Frigate, condition: 0.5)!;
        var stranded = AddShipsAt(g, Faction.Nautolan, Id("Nadir"), Frigate, condition: 0.5)!;
        RunGalaxy(g, 1);
        Expect.Close(0.5 + RepairRegen, mending.Condition, 6);
        Expect.Close(0.5 - SupplyWear, stranded.Condition, 6);
        // An Errant war band far from anything, on its way.
        var band = AddShipsAt(g, Faction.Errant, Id("Thessa"), Frigate, condition: 0.8)!;
        Assert.True(SendFleet(g, band.Id, Id("Ostra")).Ok);
        Assert.True(Supplied(g, band));
        RunGalaxy(g, 2);
        Assert.Equal(0.8, band.Condition);
    }
}
