// Skirmish maps: stations, asteroid fields, sides. Port of src/sim/scenario.ts.
using SandboxShip.Sim.Data;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim;

public enum MapSize { Small, Medium, Large }

public sealed record MapSizeDef(double W, double H, string Name);

/// <summary>`Classic`: the hand-made, mirrored map for two. `Random`: drawn from the seed.</summary>
public enum MapKind { Random, Classic }

/// <summary>
/// The rules of a battle (not in the web version beyond Standard): Standard, the last station
/// standing wins; Survival, every seat together against waves of Errants; Zones, beacons score
/// for whoever holds them; Assault, a fortress to hold against a fleet on a clock; Regicide,
/// losing one's flagship loses the game.
/// </summary>
public enum BattleMode { Standard, Survival, Zones, Assault, Regicide }

public sealed class SkirmishOptions
{
    public BattleMode Mode = BattleMode.Standard;
    /// <summary>Zones: the score that wins.</summary>
    public int ZoneTarget = 100;
    /// <summary>Assault: minutes the defenders must hold out; the defenders are team 1.</summary>
    public int AssaultMinutes = 15;
    /// <summary>
    /// Who plays, 2 to 6 seats, the local player's first; each AI plays at its own difficulty.
    /// Side 3 is the Errants' (see Raid): seats take 0 to 2, then 4 and up. Empty: the
    /// shorthand below, Player against Enemies.
    /// </summary>
    public List<Slot> Slots = [];
    /// <summary>Shorthand without Slots: the player's race, on its own side.</summary>
    public Faction Player;
    /// <summary>Shorthand without Slots: one or two opponents, each on its race's side; every one fights every other one.</summary>
    public List<Faction> Enemies = [];
    /// <summary>Shorthand without Slots; off in sandbox mode: the enemies then only do what the player spawns.</summary>
    public bool EnemyAI;
    public double Credits = Scenario.StartCredits;
    public int Seed = 1;
    public MapKind Map = MapKind.Random;
    public MapSize Size = MapSize.Medium;
    /// <summary>Fog of war, on by default.</summary>
    public bool Fog = true;
    /// <summary>The enemies' AI, for the shorthand without Slots.</summary>
    public Difficulty Difficulty = Difficulty.Normal;
    /// <summary>A neutral colossus guards the far side of the map (not in the web version).</summary>
    public bool Raid;
    /// <summary>Nebulae, asteroid belts, minefields and gravity wells (Terrain.cs, not in the web version).</summary>
    public TerrainDensity Terrain = TerrainDensity.None;
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

    /// <summary>
    /// Where each station goes: left and right for two; from three on, evenly around an ellipse
    /// about the map center, turned at random (a rough triangle for three).
    /// </summary>
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

    /// <summary>The seats of a skirmish: its slots, or the player and the enemies of the shorthand.</summary>
    public static List<Slot> SlotsOf(SkirmishOptions o) => o.Slots.Count > 0 ? o.Slots
        : [new Slot(o.Player, o.Player), .. o.Enemies.Select(e => new Slot(e, e, 0, o.EnemyAI ? o.Difficulty : null))];

    /// <summary>
    /// Whether a skirmish can be played as set up, and if not, why, for the lobby to say: 2 to
    /// 6 seats (survival from 1), each on a side of its own and of a playable race, side 3 left
    /// to the Errants when they come (raid, survival), two camps at war (survival aside: all
    /// play together), and for an assault, every seat defending (team 1) or attacking (team 2),
    /// at least one of each.
    /// </summary>
    public static Result Check(SkirmishOptions o)
    {
        var slots = SlotsOf(o);
        var survival = o.Mode == BattleMode.Survival;
        if (slots.Count < (survival ? 1 : 2) || slots.Count > 6) return Result.Fail(survival ? "De 1 à 6 joueurs" : "De 2 à 6 joueurs");
        if (slots.Select(s => s.Side).Distinct().Count() != slots.Count) return Result.Fail("Deux places jouent le même camp");
        if (slots.Exists(s => s.Race == Faction.Errant)) return Result.Fail("Les Errants ne sont pas jouables");
        if ((survival || (o.Raid && Modes.AllowsRaid(o.Mode))) && slots.Exists(s => s.Side == Faction.Errant)) return Result.Fail("Le camp 3 revient aux Errants");
        if (o.Mode == BattleMode.Zones && o.ZoneTarget < 1) return Result.Fail("Le score à atteindre doit être d'au moins 1");
        if (o.Mode == BattleMode.Assault)
        {
            if (o.AssaultMinutes < 1) return Result.Fail("L'assaut doit durer au moins 1 min");
            if (slots.Exists(s => s.Team is not (Assault.Defenders or Assault.Attackers))) return Result.Fail("En assaut, chaque joueur défend (équipe 1) ou attaque (équipe 2)");
            if (!slots.Exists(s => s.Team == Assault.Defenders)) return Result.Fail("L'assaut demande au moins un défenseur (équipe 1)");
            if (!slots.Exists(s => s.Team == Assault.Attackers)) return Result.Fail("L'assaut demande au moins un attaquant (équipe 2)");
        }
        if (!survival && !slots.Exists(a => slots.Exists(b => b != a && (a.Team == 0 || a.Team != b.Team)))) return Result.Fail("Il faut au moins deux camps adverses");
        return Result.OK;
    }

    /// <summary>
    /// Every side gets a station and two fields behind it; the rest of the fields are scattered.
    /// A battle mode other than the standard one must pass Check, and adds its own (Modes.Setup).
    /// </summary>
    public static World CreateSkirmish(SkirmishOptions o)
    {
        var slots = SlotsOf(o);
        var raid = o.Raid && Modes.AllowsRaid(o.Mode);
        if (slots.Select(s => s.Side).Distinct().Count() != slots.Count) throw new ArgumentException("Two slots share a side.");
        if (raid && slots.Exists(s => s.Side == Faction.Errant)) throw new ArgumentException("The raid needs the Errants' side.");
        if (o.Mode != BattleMode.Standard && Check(o) is { Ok: false } bad) throw new ArgumentException(bad.Reason);
        // Survival: every seat on one team, against the waves.
        if (o.Mode == BattleMode.Survival) slots = [.. slots.Select(s => s with { Team = 1 })];
        var size = MapSizes[o.Size];
        var world = CreateWorld(size.W, size.H, o.Seed);
        world.Fog = o.Fog;
        foreach (var s in slots) AddSide(world, s.Side, s.Race, o.Credits, s.Team, s.Color, s.AI is not null, s.AI ?? Difficulty.Normal);
        if (o.Map == MapKind.Classic && slots.Count == 2)
        {
            SpawnStation(world, slots[0].Side, 700 * (size.W / 6000), size.H / 2, Modes.StartLevel(o, slots[0]));
            SpawnStation(world, slots[1].Side, size.W - 700 * (size.W / 6000), size.H / 2, Modes.StartLevel(o, slots[1]));
            ClassicFields(world);
            GiveLabs(world);
            if (raid) Raids.Place(world);
            Modes.Setup(world, o);
            Terrains.Classic(world, o.Terrain, [.. world.Factions.Keys.Select(s => StationOf(world, s)).OfType<Entity>().Select(e => new Point(e.X, e.Y))]);
            world.Origin = o;
            return world;
        }
        var spots = o.Mode == BattleMode.Assault ? Assault.Spots(world, slots) : StartPositions(world, slots.Count);
        for (var i = 0; i < slots.Count; i++) SpawnStation(world, slots[i].Side, spots[i].X, spots[i].Y, Modes.StartLevel(o, slots[i]));
        foreach (var s in spots) BackFields(world, s);
        var area = size.W * size.H / (6000 * 4000);
        ScatterFields(world, (int)Js.Round((6 + Math.Floor(Rand(world) * 5)) * area), spots, 550);
        GiveLabs(world);
        if (raid) Raids.Place(world);
        Modes.Setup(world, o);
        // Last, on its own draws: the map without terrain stays the web version's.
        Terrains.Generate(world, o.Terrain, spots, o.Seed);
        world.Origin = o;
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
