// Planet specials not in the web version: nebulae hide the fleets in them, relays speed their
// owner's fleets on, ancient ruins reward the first faction to take them. Generated galaxies
// get them on top of the web version's draws, from a random stream of their own: the layouts
// stay the web's, and the classic map keeps its hand-made specials.
using SandboxShip.Sim.Data;

namespace SandboxShip.Sim;

/// <summary>A faction took ancient ruins first: what it found there.</summary>
public sealed record RuinsExplored(int PlanetId, Side Side, double Credits, TechId? Tech) : GalaxyEvent;

public static partial class Galaxies
{
    /// <summary>Speed of a relay owner's fleets on the lane they leave it by.</summary>
    public const double RelaySpeed = 1.5;
    /// <summary>Credits ancient ruins give, with a research level.</summary>
    public const double RuinsCredits = 1500;

    static readonly PlanetSpecial[] NewSpecials = [PlanetSpecial.Nebula, PlanetSpecial.Relay, PlanetSpecial.Ruins];

    /// <summary>
    /// A fifth of the planets (two at least), drawn among those with no special and no capital,
    /// get one of the new specials, the kinds in turn from a random one.
    /// </summary>
    static void AddNewSpecials(Galaxy g, IReadOnlyList<int> capitals, long seed)
    {
        var rnd = GalaxyGen.SeededRandom(unchecked((int)seed * 7919 + 104729));
        var free = g.Planets.Where(p => p.Special is null && !capitals.Contains(p.Id)).Select(p => p.Id).ToList();
        for (var i = free.Count - 1; i > 0; i--)
        {
            var j = (int)Math.Floor(rnd() * (i + 1));
            (free[i], free[j]) = (free[j], free[i]);
        }
        var first = (int)Math.Floor(rnd() * NewSpecials.Length);
        var count = Math.Min(free.Count, Math.Max(2, g.Planets.Count / 5));
        for (var i = 0; i < count; i++) g.Planets[free[i]].Special = NewSpecials[(first + i) % NewSpecials.Length];
    }

    /// <summary>Whether a fleet hides in a nebula, docked there. The Errants are known to all.</summary>
    public static bool InNebula(Galaxy g, Fleet fleet) =>
        fleet.At is int at && g.Planets[at].Special == PlanetSpecial.Nebula && fleet.Side != Faction.Errant;

    /// <summary>Speed factor of a fleet on its lane: faster from a relay its faction holds, slower in an ion storm.</summary>
    public static double LaneSpeed(Galaxy g, Fleet f)
    {
        var from = g.Planets[f.From];
        var speed = from.Special == PlanetSpecial.Relay && from.Owner == f.Side ? RelaySpeed : 1;
        return f.To is int to && StormOn(g, f.From, to) ? speed * StormSpeed : speed;
    }

    /// <summary>
    /// The first galaxy faction to hold ancient ruins finds credits there, and a free level of a
    /// tech it has not planned to the top yet, drawn at random. The ruins are then spent.
    /// </summary>
    static void ExploreRuins(Galaxy g)
    {
        foreach (var p in g.Planets)
        {
            if (p.Special != PlanetSpecial.Ruins || p.Owner is not Side owner || !g.Factions.TryGetValue(owner, out var f)) continue;
            p.Special = null;
            f.Credits += RuinsCredits;
            var open = Techs.All.Where(t => ResearchQueue.PlannedLevel(f, t) < Techs.Defs[t].MaxLevel).ToList();
            TechId? tech = null;
            if (open.Count > 0)
            {
                var t = open[(int)Math.Floor(IncidentRand(g) * open.Count)];
                f.Research[t]++;
                tech = t;
            }
            g.Events.Add(new RuinsExplored(p.Id, owner, RuinsCredits, tech));
        }
    }
}
