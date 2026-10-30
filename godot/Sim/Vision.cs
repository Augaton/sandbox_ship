// Fog of war in battles: each faction only sees what its ships and structures see, and
// only targets what it sees. The AI is bound by the same rule. Port of src/sim/vision.ts.
using SandboxShip.Sim.Data;

namespace SandboxShip.Sim;

public static class Vision
{
    static readonly Dictionary<string, double> ShipVision = new()
    {
        ["scout"] = 1100, ["fighter"] = 550, ["bomber"] = 550, ["support"] = 700, ["torpedo"] = 700,
        ["frigate"] = 750, ["battlecruiser"] = 850, ["dreadnought"] = 900, ["titan"] = 1000,
        ["bulwark"] = 750, ["carrier"] = 1000, ["leviathan"] = 1100, ["colossus"] = 1300,
    };
    static readonly Dictionary<string, double> StructureVision = new()
    {
        ["station"] = 1100, ["platform"] = 850, ["mine"] = 500, ["outpost"] = 900, ["repairDock"] = 550, ["ionCannon"] = 1000, ["starLance"] = 1000,
    };
    /// <summary>Seconds between two vision updates.</summary>
    const double Interval = 0.2;
    /// <summary>At least the longest sight (hero scouts) plus a large target's radius.</summary>
    const double EyeCell = 1400;

    static int EyeKey(int cx, int cy) => ((cx + 64) << 10) | (cy + 64);

    /// <summary>How far an entity sees; hardpoints see through their parent.</summary>
    public static double VisionOf(Entity e) => e.Kind switch
    {
        EntityKind.Ship => ShipVision[e.Type] + (e.Hero is not null ? 200 : 0),
        EntityKind.Structure => e.Build < 1 ? 300 : StructureVision.GetValueOrDefault(e.Type, 500),
        _ => 0,
    };

    public static bool IsVisible(World world, Faction faction, Entity e)
    {
        if (e.Faction == faction) return true;
        // A cloaked ship hides from enemies, fog or not.
        if (Cloaked(e)) return false;
        return !world.Fog || (world.Visible.TryGetValue(faction, out var v) && v.Contains(e.Id));
    }

    /// <summary>Whether its cloak is on. A plain loop: this is asked for every pair that might see each other.</summary>
    public static bool Cloaked(Entity e)
    {
        foreach (var a in e.Abilities)
        {
            if (a.Id == AbilityId.Cloak && a.Active > 0) return true;
        }
        return false;
    }

    /// <summary>An enemy structure seen once stays on the map, greyed out, even in the fog.</summary>
    public static bool WasSeen(World world, Faction faction, Entity e) =>
        world.Seen.TryGetValue(faction, out var s) && s.Contains(e.Id);

    public static void UpdateVision(World world, double dt)
    {
        if (!world.Fog) return;
        world.VisionTimer -= dt;
        if (world.VisionTimer > 0) return;
        world.VisionTimer = Interval;
        // Nothing joins or leaves the battle while vision updates: the store is walked as it is.
        var all = world.Entities;
        // Eyes binned in cells as wide as the longest sight: a target only checks the 3x3 cells
        // around it, and stops at the first eye that sees it. Each eye keeps its sight, looked
        // up once rather than for every target it is tested against.
        var eyes = new Dictionary<int, List<Eye>>();
        foreach (var faction in world.Factions.Keys)
        {
            var visible = new HashSet<int>();
            var seen = world.Seen.TryGetValue(faction, out var s) ? s : [];
            foreach (var list in eyes.Values) list.Clear();
            foreach (var e in all)
            {
                if (e.Faction != faction) continue;
                var sight = VisionOf(e);
                if (sight <= 0) continue;
                var (cx, cy) = ((int)Math.Floor(e.X / EyeCell), (int)Math.Floor(e.Y / EyeCell));
                var k = EyeKey(cx, cy);
                if (!eyes.TryGetValue(k, out var list)) eyes[k] = list = [];
                list.Add(new Eye(e, sight, cx, cy));
            }
            Eye? last = null;
            foreach (var t in all)
            {
                if (t.Faction == faction || t.Kind == EntityKind.Hardpoint) continue;
                var cx = (int)Math.Floor(t.X / EyeCell);
                var cy = (int)Math.Floor(t.Y / EyeCell);
                if (Sees(eyes, t, cx, cy, ref last)) visible.Add(t.Id);
            }
            // Parts are seen along with what they are attached to.
            foreach (var t in all)
            {
                if (t.Kind == EntityKind.Hardpoint && t.Faction != faction && t.ParentId is int p && visible.Contains(p)) visible.Add(t.Id);
            }
            foreach (var id in visible)
            {
                if (world.Entities.Get(id)!.Kind != EntityKind.Ship) seen.Add(id);
            }
            world.Visible[faction] = visible;
            world.Seen[faction] = seen;
        }
    }

    /// <summary>An entity that sees, how far, and the cell it is binned in.</summary>
    readonly record struct Eye(Entity E, double Sight, int Cx, int Cy)
    {
        public bool Sees(Entity t) => Worlds.Hypot(E.X - t.X, E.Y - t.Y) <= Sight + t.Radius;
    }

    /// <summary>
    /// Whether an eye of the 3x3 cells around (cx, cy) sees `t`. Whichever eye answers, the
    /// answer is the same, so the likeliest are asked first: the one that saw the target before
    /// (targets come fleet by fleet), then those of the target's own cell.
    /// </summary>
    static bool Sees(Dictionary<int, List<Eye>> eyes, Entity t, int cx, int cy, ref Eye? last)
    {
        if (last is { } l && Math.Abs(l.Cx - cx) <= 1 && Math.Abs(l.Cy - cy) <= 1 && l.Sees(t)) return true;
        if (Scan(eyes, t, cx, cy, ref last)) return true;
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                if ((dx != 0 || dy != 0) && Scan(eyes, t, cx + dx, cy + dy, ref last)) return true;
            }
        }
        return false;
    }

    static bool Scan(Dictionary<int, List<Eye>> eyes, Entity t, int cx, int cy, ref Eye? last)
    {
        if (!eyes.TryGetValue(EyeKey(cx, cy), out var list)) return false;
        foreach (var e in list)
        {
            if (!e.Sees(t)) continue;
            last = e;
            return true;
        }
        return false;
    }
}
