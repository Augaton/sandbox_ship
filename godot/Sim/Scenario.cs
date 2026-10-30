// Skirmish maps: stations, asteroid fields, factions. Port of src/sim/scenario.ts.
using SandboxShip.Sim.Data;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim;

public enum MapSize { Small, Medium, Large }

public sealed record MapSizeDef(double W, double H, string Name);

/// <summary>`Classic`: the hand-made, mirrored map for two. `Random`: drawn from the seed.</summary>
public enum MapKind { Random, Classic }

public sealed class SkirmishOptions
{
    public Faction Player;
    /// <summary>One or two opponents: every faction fights every other one.</summary>
    public List<Faction> Enemies = [];
    /// <summary>Off in sandbox mode: the enemies then only do what the player spawns.</summary>
    public bool EnemyAI;
    public double Credits = Scenario.StartCredits;
    public int Seed = 1;
    public MapKind Map = MapKind.Random;
    public MapSize Size = MapSize.Medium;
    /// <summary>Fog of war, on by default.</summary>
    public bool Fog = true;
    public Difficulty Difficulty = Difficulty.Normal;
    /// <summary>A neutral colossus guards the far side of the map (not in the web version).</summary>
    public bool Raid;
}

public static class Scenario
{
    public static readonly IReadOnlyDictionary<MapSize, MapSizeDef> MapSizes = new Dictionary<MapSize, MapSizeDef>
    {
        [MapSize.Small] = new(4500, 3000, "Petite"),
        [MapSize.Medium] = new(6000, 4000, "Moyenne"),
        [MapSize.Large] = new(8000, 5400, "Grande"),
    };

    public const double StartCredits = 1500;

    /// <summary>
    /// Scatters asteroid fields at random, clear of the given spots and of each other.
    /// Used for random skirmishes and for the battles of the galaxy campaign.
    /// </summary>
    public static void ScatterFields(World world, int count, IReadOnlyList<Point> keepClear, double clearance)
    {
        for (int tries = 0, placed = 0; placed < count && tries < 500; tries++)
        {
            var r = 120 + Rand(world) * 100;
            var px = 250 + Rand(world) * (world.Width - 500);
            var py = 250 + Rand(world) * (world.Height - 500);
            var p = new Point(px, py);
            if (keepClear.Any(c => Dist(c, p) < clearance + r)) continue;
            if (world.Fields.Exists(f => Dist(f, p) < f.Radius + r + 120)) continue;
            AddField(world, p.X, p.Y, r);
            placed++;
        }
    }

    /// <summary>Two fields tucked behind a station, on the side away from the map center: every player gets them.</summary>
    static void BackFields(World world, Point s)
    {
        var out_ = Math.Atan2(s.Y - world.Height / 2, s.X - world.Width / 2);
        foreach (var side in new[] { -0.6, 0.6 })
        {
            var a = out_ + side;
            var x = Math.Max(220, Math.Min(world.Width - 220, s.X + Math.Cos(a) * 430));
            var y = Math.Max(220, Math.Min(world.Height - 220, s.Y + Math.Sin(a) * 430));
            AddField(world, x, y, 130);
        }
    }

    static void ClassicFields(World world)
    {
        var sx = world.Width / 6000;
        var sy = world.Height / 4000;
        (double X, double Y, double R)[] half = [(1100, 1200, 140), (1200, 2900, 140), (2200, 700, 170), (2400, 2300, 160)];
        foreach (var (x, y, r) in half)
        {
            AddField(world, x * sx, y * sy, r);
            AddField(world, world.Width - x * sx, world.Height - y * sy, r);
        }
        AddField(world, world.Width / 2, world.Height / 2 - 900 * sy, 200);
        AddField(world, world.Width / 2, world.Height / 2 + 900 * sy, 200);
    }

    /// <summary>Where each station goes: left and right for two, a rough triangle for three.</summary>
    static List<Point> StartPositions(World world, int count)
    {
        var w = world.Width;
        var h = world.Height;
        if (count == 2)
        {
            // Argument order is draw order: x, then y.
            var x0 = 500 + Rand(world) * 500;
            var y0 = 600 + Rand(world) * (h - 1200);
            var x1 = w - 500 - Rand(world) * 500;
            var y1 = 600 + Rand(world) * (h - 1200);
            return [new(x0, y0), new(x1, y1)];
        }
        var start = Rand(world) * Math.PI * 2;
        return Enumerable.Range(0, count).Select(i =>
        {
            var a = start + i * Math.PI * 2 / count;
            return new Point(
                Math.Max(600, Math.Min(w - 600, w / 2 + Math.Cos(a) * w * 0.38)),
                Math.Max(600, Math.Min(h - 600, h / 2 + Math.Sin(a) * h * 0.36)));
        }).ToList();
    }

    /// <summary>Every faction gets a station and two fields behind it; the rest of the fields are scattered.</summary>
    public static World CreateSkirmish(SkirmishOptions o)
    {
        var size = MapSizes[o.Size];
        var world = CreateWorld(size.W, size.H, o.Seed);
        world.Fog = o.Fog;
        AddFaction(world, o.Player, o.Credits, false);
        foreach (var e in o.Enemies) AddFaction(world, e, o.Credits, o.EnemyAI, o.Difficulty);
        List<Faction> factions = [o.Player, .. o.Enemies];
        if (o.Map == MapKind.Classic && factions.Count == 2)
        {
            SpawnStation(world, o.Player, 700 * (size.W / 6000), size.H / 2);
            SpawnStation(world, o.Enemies[0], size.W - 700 * (size.W / 6000), size.H / 2);
            ClassicFields(world);
            GiveLabs(world);
            if (o.Raid) Raids.Place(world);
            return world;
        }
        var spots = StartPositions(world, factions.Count);
        for (var i = 0; i < factions.Count; i++) SpawnStation(world, factions[i], spots[i].X, spots[i].Y);
        foreach (var s in spots) BackFields(world, s);
        var area = size.W * size.H / (6000 * 4000);
        ScatterFields(world, (int)Js.Round((6 + Math.Floor(Rand(world) * 5)) * area), spots, 550);
        GiveLabs(world);
        if (o.Raid) Raids.Place(world);
        return world;
    }

    /// <summary>
    /// A lab behind each station, on the side away from the map center (not in the web version).
    /// Spawned last, so that the map itself is the web version's.
    /// </summary>
    static void GiveLabs(World world)
    {
        foreach (var f in world.Factions.Values)
        {
            if (StationOf(world, f.Id) is not { } s) continue;
            var a = Math.Atan2(s.Y - world.Height / 2, s.X - world.Width / 2);
            var x = Math.Max(100, Math.Min(world.Width - 100, s.X + Math.Cos(a) * 260));
            var y = Math.Max(100, Math.Min(world.Height - 100, s.Y + Math.Sin(a) * 260));
            SpawnStructure(world, f.Id, BuildableType.Lab, x, y);
        }
    }
}
