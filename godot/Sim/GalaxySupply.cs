// Supply (not in the web version): a fleet far from its side's planets and its allies' wears
// out slowly, and biomechanical hulls stop mending on the way. The Errants live off their own.
using SandboxShip.Sim.Data;

namespace SandboxShip.Sim;

public static partial class Galaxies
{
    /// <summary>Share of its hull a fleet out of supply loses per second (10 % a minute), down to this floor.</summary>
    public const double SupplyWear = 0.1 / 60, SupplyFloor = 0.3;

    /// <summary>
    /// Whether a fleet is supplied: its planet, or either end of its lane, is one of its side's or
    /// its allies', or next to one. Fleets of no galaxy faction, the Errants', always are. Asked of
    /// every fleet each step: the lanes are walked rather than a set of planets made each time.
    /// </summary>
    public static bool Supplied(Galaxy g, Fleet fleet)
    {
        if (!g.Factions.ContainsKey(fleet.Side)) return true;
        var side = fleet.Side;
        return fleet.At is int at ? InSupply(g, side, at) : InSupply(g, side, fleet.From) || (fleet.To is int to && InSupply(g, side, to));
    }

    static bool InSupply(Galaxy g, Side side, int planet)
    {
        if (Held(g, side, planet)) return true;
        foreach (var (a, b) in g.Lanes)
        {
            if ((a == planet && Held(g, side, b)) || (b == planet && Held(g, side, a))) return true;
        }
        return false;
    }

    static bool Held(Galaxy g, Side side, int planet) => g.Planets[planet].Owner is Side o && Allies(g, o, side);

    /// <summary>Fleets out of supply wear out, down to the floor; fighting ones are left to their battle.</summary>
    static void WearUnsupplied(Galaxy g, double dt)
    {
        foreach (var f in g.Fleets.Values)
        {
            if (f.BattleId is not null || f.Condition <= SupplyFloor) continue;
            if (!Supplied(g, f)) f.Condition = Math.Max(SupplyFloor, f.Condition - SupplyWear * dt);
        }
    }
}
