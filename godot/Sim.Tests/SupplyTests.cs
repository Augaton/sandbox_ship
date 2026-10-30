// Supply (not in the web version): fleets more than a lane away from their faction's planets
// wear out slowly, down to a floor; the Errants are exempt.
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

    [Fact]
    public void A_fleet_is_supplied_within_a_lane_of_its_planets_and_on_lanes_touching_that_zone()
    {
        var g = Quiet();
        // Vesper is Kla'ed; Orin is next to it, Kaldra two lanes away.
        var home = AddShipsAt(g, Faction.Klaed, Id("Vesper"), new ShipCounts { [ShipClass.Frigate] = 1 })!;
        var near = AddShipsAt(g, Faction.Klaed, Id("Orin"), new ShipCounts { [ShipClass.Frigate] = 1 })!;
        var far = AddShipsAt(g, Faction.Klaed, Id("Kaldra"), new ShipCounts { [ShipClass.Frigate] = 1 })!;
        Assert.True(Supplied(g, home));
        Assert.True(Supplied(g, near));
        Assert.False(Supplied(g, far));
        // On the lane from Kaldra back to Orin: one end is in the zone.
        Assert.True(SendFleet(g, far.Id, Id("Orin")).Ok);
        Assert.True(Supplied(g, far));
        // Kaldra to Nadir: neither end is.
        var deeper = AddShipsAt(g, Faction.Klaed, Id("Kaldra"), new ShipCounts { [ShipClass.Frigate] = 1 })!;
        Assert.True(SendFleet(g, deeper.Id, Id("Nadir")).Ok);
        Assert.False(Supplied(g, deeper));
        // Taking Kaldra brings the zone up to it.
        g.Planets[Id("Kaldra")].Owner = Faction.Klaed;
        Assert.True(Supplied(g, deeper));
    }

    [Fact]
    public void Out_of_supply_a_fleet_loses_a_tenth_of_its_hull_a_minute_down_to_the_floor()
    {
        var g = Quiet();
        var far = AddShipsAt(g, Faction.Klaed, Id("Kaldra"), new ShipCounts { [ShipClass.Frigate] = 2 })!;
        var near = FleetsAt(g, Id("Vesper"), Faction.Klaed)[0];
        // A Nairan planet under truce: the fleet can neither take it, and so supply itself, nor fight there.
        g.Planets[Id("Kaldra")].Owner = Faction.Nairan;
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
    public void Biomechanical_hulls_stop_mending_out_of_supply_and_the_Errants_never_wear()
    {
        var g = CreateGalaxy(Faction.Nautolan, [Faction.Nairan], false, errants: false, incidents: false);
        g.Player = Faction.Klaed;
        // Orin is next to the Nautolan capital, Kaldra two lanes away; a second is too short to take either.
        var mending = AddShipsAt(g, Faction.Nautolan, Id("Orin"), new ShipCounts { [ShipClass.Frigate] = 1 }, condition: 0.5)!;
        var stranded = AddShipsAt(g, Faction.Nautolan, Id("Kaldra"), new ShipCounts { [ShipClass.Frigate] = 1 }, condition: 0.5)!;
        RunGalaxy(g, 1);
        Expect.Close(0.5 + RepairRegen, mending.Condition, 6);
        Expect.Close(0.5 - SupplyWear, stranded.Condition, 6);
        // An Errant war band far from anything, on its way.
        var band = AddShipsAt(g, Faction.Errant, Id("Thessa"), new ShipCounts { [ShipClass.Frigate] = 1 }, condition: 0.8)!;
        Assert.True(SendFleet(g, band.Id, Id("Ostra")).Ok);
        Assert.True(Supplied(g, band));
        RunGalaxy(g, 2);
        Assert.Equal(0.8, band.Condition);
    }
}
