// Long hulls (the leviathan, a capsule along its heading) and craft flying over the ships: rules
// of this version, not in the web one.
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.Movement;
using static SandboxShip.Sim.Steps;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim.Tests;

public class LongHullTests
{
    static World NewWorld()
    {
        var w = CreateWorld(4000, 4000);
        AddFaction(w, Faction.Klaed, 0);
        AddFaction(w, Faction.Nairan, 0);
        return w;
    }

    /// <summary>A leviathan at (1000, 1000), bow to the right (+x), held still.</summary>
    static Entity Leviathan(World w)
    {
        var l = SpawnShip(w, Faction.Klaed, ShipClass.Leviathan, 1000, 1000, 0);
        IssueStop(w, [l.Id]);
        return l;
    }

    [Fact]
    public void A_leviathan_is_long_and_narrow()
    {
        var def = Ships.Defs[ShipClass.Leviathan];
        Expect.Greater(def.Beam, 0);
        // Three times longer than wide at least, its keel inside its length.
        Expect.Greater(def.Radius, def.Beam * 3);
        Assert.All(Ships.All.Where(c => c != ShipClass.Leviathan), c => Assert.Equal(0, Ships.Defs[c].Beam));
    }

    [Fact]
    public void The_gap_to_a_long_hull_is_measured_from_its_keel()
    {
        var w = NewWorld();
        var l = Leviathan(w);
        var keel = l.Radius - l.Beam;
        // Abeam: only its beam away from the keel; ahead: past the end of its keel.
        Expect.Close(100 - l.Beam, Gap(l, 1000, 1100));
        Expect.Close(100 - l.Beam, Gap(l, 1000 + keel * 0.5, 900));
        Expect.Close(300 - keel - l.Beam, Gap(l, 1300, 1000));
        Expect.Close(-l.Beam, Gap(l, 1000, 1000));
        // Turned bow up, what was abeam is now ahead.
        l.Angle = -Math.PI / 2;
        Expect.Close(100 - l.Beam, Gap(l, 1000, 1000 - 100 - keel));
        // Round hulls keep their radius.
        var f = SpawnShip(w, Faction.Nairan, ShipClass.Frigate, 2000, 2000);
        Expect.Close(100 - f.Radius, Gap(f, 2100, 2000));
    }

    [Fact]
    public void A_ship_abeam_of_a_long_hull_keeps_its_place_when_clear_of_it()
    {
        var w = NewWorld();
        var l = Leviathan(w);
        // Well inside the circle round the leviathan, but clear of its hull.
        var f = SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 1040, 1000 + l.Beam + Ships.Defs[ShipClass.Frigate].Radius + 4);
        IssueStop(w, [f.Id]);
        var (x, y) = (f.X, f.Y);
        Run(w, 0.1);
        Expect.Close(x, f.X, 0);
        Expect.Close(y, f.Y, 0);
    }

    [Fact]
    public void A_ship_overlapping_a_long_hull_is_pushed_off_its_keel()
    {
        var w = NewWorld();
        var l = Leviathan(w);
        var f = SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 1060, 1020);
        IssueStop(w, [f.Id]);
        Run(w, 0.1);
        // Out across the hull, not along it: the leviathan's own keel point is where it was.
        Expect.GreaterOrEqual(Gap(l, f.X, f.Y) + l.Beam, (l.Beam + f.Radius) * 0.9 - 0.5);
        Assert.InRange(f.X, 1054, 1066);
    }

    [Fact]
    public void A_long_hull_is_pushed_out_of_structures_by_its_keel()
    {
        var w = NewWorld();
        var l = Leviathan(w);
        // A platform under the leviathan's stern, far from its middle.
        var st = SpawnStructure(w, Faction.Klaed, BuildableType.Platform, 1000 - (l.Radius - l.Beam), 1010);
        Run(w, 0.1);
        Expect.GreaterOrEqual(Gap(l, st.X, st.Y), st.Radius - 0.5);
    }

    [Fact]
    public void Craft_fly_over_ships_and_out_of_structures()
    {
        var w = NewWorld();
        var cruiser = SpawnShip(w, Faction.Klaed, ShipClass.Battlecruiser, 1000, 1000);
        var fighter = SpawnShip(w, Faction.Klaed, ShipClass.Fighter, 1010, 1000);
        var st = SpawnStructure(w, Faction.Klaed, BuildableType.Platform, 3000, 3000);
        var over = SpawnShip(w, Faction.Klaed, ShipClass.Fighter, 3005, 3000);
        IssueStop(w, [cruiser.Id, fighter.Id, over.Id]);
        Run(w, 0.1);
        // Over the cruiser it stays, neither pushing nor pushed; over a structure it is put out.
        Expect.Close(1010, fighter.X, 0);
        Expect.Close(1000, cruiser.X, 0);
        Expect.GreaterOrEqual(Dist(over, st), over.Radius + st.Radius - 0.01);
    }
}
