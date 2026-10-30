// A space battle fought over a planet of the galaxy map: built from fleets and planet
// defenses, run by the tactical simulation, and read back as survivors. Port of
// src/sim/battle.ts.
using SandboxShip.Sim.Data;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim;

/// <summary>Ships by class; a missing class counts as none.</summary>
public sealed class ShipCounts : Dictionary<ShipClass, int>
{
    public ShipCounts() { }
    public ShipCounts(IDictionary<ShipClass, int> from) : base(from) { }

    public int Of(ShipClass c) => TryGetValue(c, out var n) ? n : 0;
    public void Increase(ShipClass c, int n = 1) => this[c] = Of(c) + n;
    public ShipCounts Clone() => new(this);
}

public sealed record PlanetDefense
{
    /// <summary>Station level, 0 without a station.</summary>
    public int Station { get; set; }
    public int Platforms { get; set; }
    public int Mines { get; set; }
    public int RepairDock { get; set; }
    public int IonCannon { get; set; }
    /// <summary>Level of the platforms, 1 to 3.</summary>
    public int PlatformTier { get; set; } = 1;

    public static PlanetDefense None => new();
}

public sealed class BattleSide
{
    public Side Side;
    /// <summary>Null: the race whose own side it is (see Side).</summary>
    public Faction? Race;
    /// <summary>Null: the side's default color.</summary>
    public uint? Color;
    public ShipCounts Ships = [];
    public List<HeroId> Heroes = [];
    public ResearchLevels? Research;
    /// <summary>Share of their hull the ships come in with, after earlier battles; heroes come in whole.</summary>
    public double Condition = 1;
}

/// <summary>The attacker took the planet, the defender held it, or the attacker fell back.</summary>
public enum BattleResult { Attacker, Defender, Retreat }

public static class Battle
{
    public const double Width = 4000;
    public const double Height = 2600;

    /// <summary>
    /// Seconds without a single hit after which a planet battle ends with the attacker falling
    /// back: ships that cannot hurt what is left would otherwise circle forever.
    /// </summary>
    public const double Stalemate = 240;

    public static int CountShips(ShipCounts ships) => Data.Ships.All.Sum(ships.Of);

    /// <summary>Total build cost: a rough measure of fighting strength.</summary>
    public static double FleetValue(ShipCounts ships, IEnumerable<HeroId>? heroes = null) =>
        Data.Ships.All.Sum(c => ships.Of(c) * Data.Ships.UnitValue(c)) + (heroes ?? []).Sum(h => Data.Heroes.Defs[h].Cost);

    static void PlaceShips(World world, BattleSide side, double x, double y, double angle)
    {
        var list = side.Heroes.Select(h => (Cls: Data.Heroes.Defs[h].Base, Hero: (HeroId?)h))
            .Concat(Data.Ships.All.SelectMany(c => Enumerable.Repeat((Cls: c, Hero: (HeroId?)null), side.Ships.Of(c))))
            // Heavy ships in the middle, as for a move order.
            .OrderByDescending(k => Data.Ships.Defs[k.Cls].Radius).ToList();
        var offsets = Movement.FormationOffsets(list.Count, 90).OrderBy(p => Hypot(p.X, p.Y)).ToList();
        for (var i = 0; i < list.Count; i++)
        {
            var px = x + offsets[i].X;
            var py = y + offsets[i].Y;
            if (list[i].Hero is HeroId hero)
            {
                SpawnHero(world, side.Side, hero, px, py, angle);
                continue;
            }
            var s = SpawnShip(world, side.Side, list[i].Cls, px, py, angle);
            var c = Math.Max(0.05, side.Condition);
            foreach (var part in ChildrenOf(world, s.Id).Prepend(s)) part.Hull = part.MaxHull * c;
        }
    }

    /// <summary>
    /// Defender on the left around its station, attacker arriving from the right. Both sides
    /// bring their research, and the AI handles their ship abilities unless told otherwise.
    /// </summary>
    public static World CreatePlanetBattle(BattleSide attacker, BattleSide defender, PlanetDefense defense, int seed = 1)
    {
        var w = CreateWorld(Width, Height, seed);
        w.Fog = true;
        foreach (var side in new[] { defender, attacker })
        {
            var f = AddSide(w, side.Side, side.Race ?? (Faction)side.Side.Id, 0, color: side.Color);
            f.AutoAbilities = true;
            if (side.Research is not null) f.Research = side.Research.Clone();
        }
        var home = new Point(800, Height / 2);
        Scenario.ScatterFields(w, 5 + (int)Math.Floor(Rand(w) * 4), [home, new Point(Width - 500, home.Y)], 380);
        if (defense.Station > 0) SpawnStation(w, defender.Side, home.X, home.Y, defense.Station);
        var fields = w.Fields.OrderBy(a => Dist(a, home)).Take(defense.Mines).ToList();
        foreach (var field in fields)
        {
            var mine = SpawnStructure(w, defender.Side, BuildableType.Mine, field.X, field.Y);
            mine.FieldId = field.Id;
            field.MineId = mine.Id;
        }
        for (var i = 0; i < defense.Platforms; i++)
        {
            var a = (i - (defense.Platforms - 1) / 2.0) * 0.5;
            var platform = SpawnStructure(w, defender.Side, BuildableType.Platform, home.X + Math.Cos(a) * 420, home.Y + Math.Sin(a) * 420);
            for (var t = 1; t < defense.PlatformTier; t++) Structures.ApplyTier(w, platform);
        }
        if (defense.RepairDock > 0) SpawnStructure(w, defender.Side, BuildableType.RepairDock, home.X - 260, home.Y + 260);
        if (defense.IonCannon > 0) SpawnStructure(w, defender.Side, BuildableType.IonCannon, home.X - 260, home.Y - 260);
        PlaceShips(w, defender, home.X + 550, home.Y, 0);
        PlaceShips(w, attacker, Width - 500, home.Y, Math.PI);
        return w;
    }

    /// <summary>
    /// The defender holds while it keeps a station or a ship; the attacker while it keeps a ship.
    /// Returns null while both sides still stand.
    /// </summary>
    public static BattleResult? Outcome(World world, Side attacker, Side defender)
    {
        var att = false;
        var def = StationOf(world, defender) is not null;
        foreach (var e in world.Entities)
        {
            if (e.Kind != EntityKind.Ship) continue;
            if (e.Side == attacker) att = true;
            else if (e.Side == defender) def = true;
        }
        if (!att) return BattleResult.Defender;
        if (!def) return BattleResult.Attacker;
        if (world.Time - world.LastDamage > Stalemate) return BattleResult.Retreat;
        return null;
    }

    /// <summary>Ships that can go back to the galaxy map: hangar craft stay behind, heroes are counted apart.</summary>
    public static ShipCounts Survivors(World world, Side side)
    {
        var out_ = new ShipCounts();
        foreach (var e in world.Entities)
        {
            if (e.Kind != EntityKind.Ship || e.Side != side || e.Hero is not null || e.Launched) continue;
            if (!Data.Ships.Defs[e.Cls].FromHangar) out_.Increase(e.Cls);
        }
        return out_;
    }

    /// <summary>Share of their hull the surviving ships keep, 1 when none survive.</summary>
    public static double SurvivorCondition(World world, Side side)
    {
        double hull = 0, max = 0;
        foreach (var e in world.Entities)
        {
            if (e.Kind != EntityKind.Ship || e.Side != side || e.Hero is not null || e.Launched) continue;
            hull += Math.Max(0, e.Hull);
            max += e.MaxHull;
        }
        return max > 0 ? hull / max : 1;
    }

    public static List<HeroId> HeroSurvivors(World world, Side side)
    {
        var out_ = new List<HeroId>();
        foreach (var e in world.Entities) if (e.Hero is HeroId h && e.Side == side) out_.Add(h);
        return out_;
    }

    public static PlanetDefense DefenseLeft(World world, Side side)
    {
        var left = new PlanetDefense { Station = StationOf(world, side)?.Level ?? 0 };
        foreach (var e in world.Entities)
        {
            if (e.Side != side || e.Kind != EntityKind.Structure) continue;
            if (e.Type == "platform")
            {
                left.Platforms++;
                left.PlatformTier = Math.Max(left.PlatformTier, e.Level);
            }
            if (e.Type == "mine") left.Mines++;
            if (e.Type == "repairDock") left.RepairDock++;
            if (e.Type == "ionCannon") left.IonCannon++;
        }
        return left;
    }

    /// <summary>Hull and shields a side still has in the battle: what the battle prompt compares.</summary>
    public static double SideStrength(World world, Side side)
    {
        double total = 0;
        foreach (var e in world.Entities) if (e.Side == side) total += e.Hull + e.Shield;
        return Js.Round(total);
    }

    /// <summary>
    /// Once per second, idle warships of each side attack-move towards the nearest foe they can
    /// see; seeing none, towards a foe's station, or else to scout the middle.
    /// </summary>
    public static void UpdateBattleAI(World world, IEnumerable<Side> sides, double dt)
    {
        if (Math.Floor(world.Time) == Math.Floor(world.Time - dt)) return;
        foreach (var side in sides)
        {
            var idle = new List<Entity>();
            foreach (var e in world.Entities)
            {
                if (e.Kind == EntityKind.Ship && e.Side == side && e.Order is null && !e.Launched) idle.Add(e);
            }
            if (idle.Count == 0) continue;
            var c = new Point(idle.Sum(e => e.X) / idle.Count, idle.Sum(e => e.Y) / idle.Count);
            IPoint? target = null;
            foreach (var e in world.Entities)
            {
                if (Foes(world, side, e.Side) && Vision.IsVisible(world, side, e) && (target is null || Dist(e, c) < Dist(target, c))) target = e;
            }
            if (target is null)
            {
                var station = world.Factions.Keys.Where(f => Foes(world, side, f)).Select(f => StationOf(world, f)).FirstOrDefault(s => s is not null);
                var center = new Point(world.Width / 2, world.Height / 2);
                target = station ?? (StationOf(world, side) is not null || Dist(c, center) < 200 ? null : (IPoint)center);
            }
            if (target is not null) Movement.IssueMove(world, idle.Select(e => e.Id), target.X, target.Y, true);
        }
    }
}
