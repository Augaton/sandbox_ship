// The star lance in the campaign (not in the web version): a planet building at the price of a
// fleet, one per faction, on a level 4 station. Every five minutes it can strike an enemy
// planet up to three lanes away: its platforms, ion cannon and repair dock go, its station
// loses a level, a mine is lost, and the fleets docked there lose half their ships.
using SandboxShip.Sim.Data;

namespace SandboxShip.Sim;

/// <summary>A star lance fired from one planet at another.</summary>
public sealed record LanceFired(int From, int Target, Side Side) : GalaxyEvent;

public static partial class Galaxies
{
    /// <summary>Seconds of galaxy time to charge, once built and after each shot.</summary>
    public const double LanceRecharge = 300;
    /// <summary>How many lanes away it reaches.</summary>
    public const int LanceReach = 3;

    /// <summary>Whether the faction has a star lance, built or under construction.</summary>
    static bool HasLance(Galaxy g, Side side) =>
        g.Planets.Exists(p => p.Owner == side && (p.StarLance > 0 || p.Buildings.Exists(q => q.Kind == GalaxyBuildKind.StarLance)));

    /// <summary>Lanes between two planets, whoever holds those on the way.</summary>
    public static int LanesBetween(Galaxy g, int from, int to)
    {
        if (from == to) return 0;
        var seen = new Dictionary<int, int> { [from] = 0 };
        var todo = new Queue<int>([from]);
        while (todo.Count > 0)
        {
            var cur = todo.Dequeue();
            foreach (var n in Neighbours(g, cur))
            {
                if (seen.ContainsKey(n)) continue;
                seen[n] = seen[cur] + 1;
                if (n == to) return seen[n];
                todo.Enqueue(n);
            }
        }
        return int.MaxValue;
    }

    public static Result CanFireLance(Galaxy g, Side side, int from, int target)
    {
        var p = g.Planets[from];
        if (p.Owner != side || p.StarLance == 0) return Result.Fail("Aucune Lance stellaire ici");
        if (p.LanceCharge > 0) return Result.Fail($"En charge : {Math.Ceiling(p.LanceCharge)} s");
        var t = g.Planets[target];
        if (t.Owner is not Side owner || Allies(g, owner, side)) return Result.Fail("Choisis une planète ennemie");
        if (AtPeace(g, side, owner)) return Result.Fail("Trêve en cours");
        if (LanesBetween(g, from, target) > LanceReach) return Result.Fail($"Hors de portée ({LanceReach} voies au plus)");
        return Result.OK;
    }

    /// <summary>Fires the lance: what stands in orbit of the target is broken, its fleets halved.</summary>
    public static Result FireLance(Galaxy g, Side side, int from, int target)
    {
        var r = CanFireLance(g, side, from, target);
        if (!r.Ok) return r;
        g.Planets[from].LanceCharge = LanceRecharge;
        var t = g.Planets[target];
        t.Platforms = 0;
        t.IonCannon = 0;
        t.RepairDock = 0;
        t.Station = Math.Max(0, t.Station - 1);
        t.Mines = Math.Max(0, t.Mines - 1);
        foreach (var f in FleetsAt(g, target).Where(f => !Allies(g, f.Side, side)).ToList())
        {
            var left = new ShipCounts();
            foreach (var c in Data.Ships.All) if (f.Ships.Of(c) / 2 > 0) left[c] = f.Ships.Of(c) / 2;
            f.Ships = left;
            f.Condition *= 0.7;
            if (Battle.CountShips(left) == 0 && f.Heroes.Count == 0) g.Fleets.Remove(f.Id);
        }
        g.Events.Add(new LanceFired(from, target, side));
        return Result.OK;
    }

    /// <summary>The rival planet worth striking the most within reach, for the AI: its defenses and docked fleets.</summary>
    static int? LanceTarget(Galaxy g, Side side, int from) => g.Planets
        .Where(p => p.Owner != Faction.Errant && CanFireLance(g, side, from, p.Id).Ok)
        .OrderByDescending(p => DefenseValue(g, p))
        .Select(p => (int?)p.Id).FirstOrDefault();

    /// <summary>
    /// Lances charge. An AI fires as soon as one is ready, and builds one once it has a level 4
    /// station and money well beyond the price.
    /// </summary>
    static void UpdateLances(Galaxy g, double dt)
    {
        foreach (var p in g.Planets)
        {
            if (p.StarLance == 0 || p.Owner is not Side owner) continue;
            p.LanceCharge = Math.Max(0, p.LanceCharge - dt);
            if (p.LanceCharge > 0 || g.Factions.GetValueOrDefault(owner)?.AI is null) continue;
            if (LanceTarget(g, owner, p.Id) is int target) FireLance(g, owner, p.Id, target);
        }
        foreach (var f in g.Factions.Values)
        {
            if (f.AI is null || f.Eliminated || HasLance(g, f.Id)) continue;
            var cost = Buildables.Defs[BuildableType.StarLance].Cost;
            if (f.Credits < cost * 1.5) continue;
            var site = g.Planets.Where(p => p.Owner == f.Id && p.Station >= Station.Levels.Count).OrderBy(p => p.Id).FirstOrDefault();
            if (site is not null) EnqueueBuild(g, f.Id, site.Id, GalaxyBuild.Of(GalaxyBuildKind.StarLance));
        }
    }
}
