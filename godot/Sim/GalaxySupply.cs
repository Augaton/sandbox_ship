// Supply (not in the web version): a fleet far from its faction's planets wears out slowly, and
// biomechanical hulls stop mending on the way. The Errants live off their own.
using SandboxShip.Sim.Data;

namespace SandboxShip.Sim;

public static partial class Galaxies
{
    /// <summary>Share of its hull a fleet out of supply loses per second (10 % a minute), down to this floor.</summary>
    public const double SupplyWear = 0.1 / 60, SupplyFloor = 0.3;

    /// <summary>Planets a faction supplies: its own and their neighbours.</summary>
    public static HashSet<int> SupplyZone(Galaxy g, Faction faction)
    {
        var zone = new HashSet<int>();
        foreach (var p in g.Planets) if (p.Owner == faction) zone.Add(p.Id);
        foreach (var (a, b) in g.Lanes)
        {
            if (g.Planets[a].Owner == faction) zone.Add(b);
            if (g.Planets[b].Owner == faction) zone.Add(a);
        }
        return zone;
    }

    /// <summary>
    /// Whether a fleet is supplied: its planet, or either end of its lane, is one of its faction's
    /// or next to one. Fleets of no galaxy faction, the Errants', always are.
    /// </summary>
    public static bool Supplied(Galaxy g, Fleet fleet, HashSet<int>? zone = null)
    {
        if (!g.Factions.ContainsKey(fleet.Faction)) return true;
        zone ??= SupplyZone(g, fleet.Faction);
        return fleet.At is int at ? zone.Contains(at) : zone.Contains(fleet.From) || (fleet.To is int to && zone.Contains(to));
    }

    /// <summary>Fleets out of supply wear out, down to the floor; fighting ones are left to their battle.</summary>
    static void WearUnsupplied(Galaxy g, double dt)
    {
        var zones = new Dictionary<Faction, HashSet<int>>();
        foreach (var f in g.Fleets.Values)
        {
            if (f.BattleId is not null || f.Condition <= SupplyFloor || !g.Factions.ContainsKey(f.Faction)) continue;
            if (!zones.TryGetValue(f.Faction, out var zone)) zones[f.Faction] = zone = SupplyZone(g, f.Faction);
            if (!Supplied(g, f, zone)) f.Condition = Math.Max(SupplyFloor, f.Condition - SupplyWear * dt);
        }
    }
}
