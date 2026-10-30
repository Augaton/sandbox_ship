// Port of tests/movement.test.ts.
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.Movement;
using static SandboxShip.Sim.Steps;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim.Tests;

public class MovementTests
{
    static World NewWorld()
    {
        var w = CreateWorld(4000, 4000);
        AddFaction(w, Faction.Klaed, 0);
        AddFaction(w, Faction.Nairan, 0);
        return w;
    }

    [Fact]
    public void Wraps_angles_into_minus_pi_pi()
    {
        Expect.Close(Math.PI, WrapAngle(3 * Math.PI));
        Expect.Close(-Math.PI / 2, WrapAngle(-Math.PI / 2 - 2 * Math.PI));
        Expect.Close(0.5, WrapAngle(0.5));
    }

    [Fact]
    public void Flies_a_ship_to_its_destination_and_stops_there()
    {
        var w = NewWorld();
        // Facing away from the target: the ship has to turn around first.
        var s = SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 500, 500, Math.PI);
        IssueMove(w, [s.Id], 1500, 900);
        Run(w, 40);
        Assert.Null(s.Order);
        Expect.Less(Dist(s, new Point(1500, 900)), 15);
        Expect.Less(s.Speed, 1);
    }

    [Fact]
    public void Spreads_a_group_over_distinct_formation_slots()
    {
        var offsets = FormationOffsets(7, 50);
        Assert.Equal(7, offsets.Count);
        Assert.Equal(7, offsets.Select(o => (o.X, o.Y)).Distinct().Count());
        var w = NewWorld();
        var ids = Enumerable.Range(0, 6).Select(i => SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 300 + i * 70, 300).Id).ToList();
        IssueMove(w, ids, 2000, 2000);
        Run(w, 60);
        var ships = ids.Select(id => w.Entities.Get(id)!).ToList();
        foreach (var a in ships)
        {
            foreach (var b in ships) if (a != b) Expect.Greater(Dist(a, b), a.Radius);
        }
    }

    [Fact]
    public void Pushes_ships_out_of_structures()
    {
        var w = NewWorld();
        var st = SpawnStructure(w, Faction.Klaed, BuildableType.Platform, 1000, 1000);
        var s = SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 1005, 1000);
        IssueStop(w, [s.Id]);
        Run(w, 0.1);
        Expect.GreaterOrEqual(Dist(s, st), s.Radius + st.Radius - 0.01);
    }
}
