// Supply (not in the web version): a fleet too far from a station of its side or its allies wears
// out slowly and mends no more, at home neither: an offensive that outruns its stations pays for
// it, until one stands nearer. The Errants live off their own.
using SandboxShip.Sim.Data;

namespace SandboxShip.Sim;

public static partial class Galaxies
{
    /// <summary>Share of its hull a fleet out of supply loses per second (10 % a minute), down to this floor.</summary>
    public const double SupplyWear = 0.1 / 60, SupplyFloor = 0.3;
    /// <summary>
    /// How many lanes from a station a fleet stays supplied. Counted from any planet, fleets
    /// were out of supply 1 % of the time, and it changed nothing; so, 9 %.
    /// </summary>
    public const int SupplyLanes = 2;

    /// <summary>
    /// Whether a fleet is supplied: its planet, or either end of its lane, is within SupplyLanes
    /// of a station of its side or its allies. Fleets of no galaxy faction, the Errants', always
    /// are. Asked of every fleet each step: the lanes are walked rather than a set of planets
    /// made each time.
    /// </summary>
    public static bool Supplied(Galaxy g, Fleet fleet)
    {
        if (!g.Factions.ContainsKey(fleet.Side)) return true;
        var side = fleet.Side;
        return fleet.At is int at ? InSupply(g, side, at) : InSupply(g, side, fleet.From) || (fleet.To is int to && InSupply(g, side, to));
    }

    static bool InSupply(Galaxy g, Side side, int planet)
    {
        if (Base(g, side, planet)) return true;
        foreach (var (a, b) in g.Lanes)
        {
            var next = a == planet ? b : b == planet ? a : -1;
            if (next < 0) continue;
            if (Base(g, side, next)) return true;
            foreach (var (c, d) in g.Lanes)
            {
                if ((c == next && Base(g, side, d)) || (d == next && Base(g, side, c))) return true;
            }
        }
        return false;
    }

    /// <summary>A station of the side or its allies stands there. One being built does not count yet.</summary>
    static bool Base(Galaxy g, Side side, int planet) => g.Planets[planet].Station > 0 && g.Planets[planet].Owner is Side o && Allies(g, o, side);

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
