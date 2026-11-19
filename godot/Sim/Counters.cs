// Who beats whom, for the codex (Game/scripts/Ui/Codex.cs): worked out from the data rather than
// written by hand, so that it follows the weapons as they change. Not in the web version.
//
// What a class deals to another per second comes from its weapons, on its hull and its turrets,
// and from the craft of its hangars: each weapon's damage per second, times its multiplier for
// the target's size, times how hard it hits shields and hulls for the target's share of each;
// torpedoes, missiles and shells lose part of it to a target with point defense. Taken in
// logarithms over every pair, what a class deals in general and what a target takes in general
// are set aside (a titan hits everything hard, a fighter dies to everything): what is left is
// how much better a class does against this target than its own average and than the target's.
// A matchup's score is that for the class against the target, less that for the target against
// the class: the rock, paper and scissors of the fleet, its overall strength left out.
using SandboxShip.Sim.Data;

namespace SandboxShip.Sim;

public static class Counters
{
    /// <summary>A matchup scoring past this, either way, is worth saying: about one and a half times better than the rest.</summary>
    public const double Threshold = 0.4;
    /// <summary>The codex names this many at most, the strongest matchups first.</summary>
    public const int Most = 3;
    /// <summary>Share of their damage torpedoes, missiles and shells keep against a target with point defense.</summary>
    const double Intercepted = 0.6;

    /// <summary>What a class may face: its size, its shields' share of what it can take, whether it shoots projectiles down.</summary>
    readonly record struct Target(SizeClass Size, double ShieldShare, bool PointDefense);

    static readonly ShipClass[] Classes = [.. Ships.All];

    /// <summary>The buildings as a target: a station's, the sturdiest of them, flak included.</summary>
    static readonly Target Buildings = new(SizeClass.Structure,
        Station.Levels[0].Shield / (Station.Levels[0].Shield + Station.Levels[0].Hull), Station.Weapons.Any(w => w.PointDefense));

    /// <summary>Interaction terms: attackers by row, targets by column (the classes, then the buildings).</summary>
    static readonly double[,] Interaction = Interactions();

    /// <summary>Damage a weapon deals per second: a beam's is given so, a burst's is spread over its reload.</summary>
    public static double Dps(WeaponDef w) =>
        w.Kind == WeaponKind.Beam ? w.Damage : w.Damage * w.Burst / Math.Max(0.05, w.Cooldown + (w.Burst - 1) * w.BurstGap);

    /// <summary>Every weapon of a class: on its hull, and on its turrets (a slot without weapons of its own carries its role's).</summary>
    public static IEnumerable<WeaponDef> WeaponsOf(ShipClass cls)
    {
        var def = Ships.Defs[cls];
        return def.Weapons.Concat(def.Hardpoints?.SelectMany(h => h.Weapons ?? Hardpoints.Defs[h.Role].Weapons) ?? []);
    }

    /// <summary>Its longest reach, turrets included.</summary>
    public static double RangeOf(ShipClass cls) => Weapons.MaxRange(WeaponsOf(cls));

    static Target TargetOf(ShipClass cls)
    {
        var def = Ships.Defs[cls];
        return new(def.Size, def.Shield / (def.Shield + def.Hull), WeaponsOf(cls).Any(w => w.PointDefense));
    }

    /// <summary>What a class deals per second to a target, its hangars' craft counted as half their number in the air.</summary>
    static double Against(ShipClass cls, Target t)
    {
        var dealt = WeaponsOf(cls).Sum(w => Dps(w) * w.Multipliers.Of(t.Size)
            * (t.ShieldShare * w.ShieldMult + (1 - t.ShieldShare) * w.HullMult)
            * (t.PointDefense && w.Kind is WeaponKind.Torpedo or WeaponKind.Missile ? Intercepted : 1));
        foreach (var slot in Ships.Defs[cls].Hardpoints ?? [])
        {
            if (slot.Role != HardpointRole.Hangar || (slot.Hangar ?? Hardpoints.Defs[slot.Role].Hangar) is not { } hangar) continue;
            var launched = hangar.Squadrons.Sum(s => s.Size);
            if (launched == 0) continue;
            dealt += hangar.Cap / 2.0 * hangar.Squadrons.Sum(s => s.Size * Against(s.Cls, t)) / launched;
        }
        return dealt;
    }

    static double[,] Interactions()
    {
        var n = Classes.Length;
        var targets = Classes.Select(TargetOf).Append(Buildings).ToArray();
        var log = new double[n, targets.Length];
        for (var a = 0; a < n; a++)
        {
            for (var t = 0; t < targets.Length; t++) log[a, t] = Math.Log(Math.Max(0.01, Against(Classes[a], targets[t])));
        }
        var rows = new double[n];
        var cols = new double[targets.Length];
        var all = 0.0;
        for (var a = 0; a < n; a++)
        {
            for (var t = 0; t < targets.Length; t++)
            {
                rows[a] += log[a, t] / targets.Length;
                cols[t] += log[a, t] / n;
                all += log[a, t] / (n * targets.Length);
            }
        }
        var s = new double[n, targets.Length];
        for (var a = 0; a < n; a++)
        {
            for (var t = 0; t < targets.Length; t++) s[a, t] = log[a, t] - rows[a] - cols[t] + all;
        }
        return s;
    }

    static int Index(ShipClass cls) => Array.IndexOf(Classes, cls);

    /// <summary>
    /// How `cls` fares against `foe` (null: the buildings): above 0, it does better against it
    /// than against the rest while the foe does worse against it; below 0, the other way round.
    /// </summary>
    public static double Score(ShipClass cls, ShipClass? foe)
    {
        var a = Index(cls);
        if (foe is not ShipClass f) return Interaction[a, Classes.Length];
        var t = Index(f);
        return Interaction[a, t] - Interaction[t, a];
    }

    /// <summary>What `cls` beats, the clearest first, Most at most: classes, or null for the buildings.</summary>
    public static IReadOnlyList<ShipClass?> StrongAgainst(ShipClass cls) =>
        [.. Classes.Where(c => c != cls).Select(c => (ShipClass?)c).Append(null)
            .Select(f => (Foe: f, Score: Score(cls, f))).Where(m => m.Score > Threshold)
            .OrderByDescending(m => m.Score).Take(Most).Select(m => m.Foe)];

    /// <summary>The classes that beat `cls`, the clearest first, Most at most.</summary>
    public static IReadOnlyList<ShipClass> WeakAgainst(ShipClass cls) =>
        [.. Classes.Where(c => c != cls).Select(c => (Foe: c, Score: Score(cls, c))).Where(m => m.Score < -Threshold)
            .OrderBy(m => m.Score).Take(Most).Select(m => m.Foe)];

    /// <summary>The longest reach of the defenses a station, its platforms, outposts and ion cannon hold, each by its name.</summary>
    public static IReadOnlyList<(string Name, double Range)> Defenses()
    {
        var station = Station.Levels.SelectMany(l => l.Hardpoints).SelectMany(h => h.Weapons ?? Hardpoints.Defs[h.Role].Weapons).Concat(Station.Weapons);
        var list = new List<(string, double)> { (Data.Station.Name, Weapons.MaxRange(station)) };
        foreach (var def in Buildables.Defs.Values)
        {
            var weapons = def.Weapons.Concat(def.Tiers?.SelectMany(t => (t.Weapons ?? []).Concat(t.Hardpoints?.SelectMany(h => h.Weapons ?? Hardpoints.Defs[h.Role].Weapons) ?? [])) ?? []).ToList();
            if (weapons.Count > 0) list.Add((def.Name, Weapons.MaxRange(weapons)));
        }
        return list;
    }
}
