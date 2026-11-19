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
    /// <summary>The veterans among them (Veterans.cs): they come in with their rank, the best placed first.</summary>
    public RankCounts Ranks = [];
    public List<HeroId> Heroes = [];
    public ResearchLevels? Research;
    /// <summary>Share of their hull the ships come in with, after earlier battles; heroes come in whole.</summary>
    public double Condition = 1;
}

/// <summary>The attacker took the planet, the defender held it, or the attacker fell back.</summary>
public enum BattleResult { Attacker, Defender, Retreat }

/// <summary>How a battle looks likely to end for one side (Battle.Outlook).</summary>
public enum BattleOutlook { CertainDefeat, Unfavorable, Even, Favorable, CertainVictory }

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

    /// <summary>
    /// A side's ships round (x, y), facing `angle`. Under the world's fleet cap, only those that
    /// open the battle (Opening): the others wait in its reserve, with the veterans among them and
    /// the fleet's wear. With `jump`, the opening ships come in by hyperspace instead, there.
    /// </summary>
    internal static void PlaceShips(World world, BattleSide side, double x, double y, double angle, bool jump = false)
    {
        var list = side.Heroes.Select(h => new ReserveShip(Data.Heroes.Defs[h].Base, h))
            .Concat(Data.Ships.All.SelectMany(c => Enumerable.Repeat(new ReserveShip(c), side.Ships.Of(c))))
            // Heavy ships in the middle, as for a move order.
            .OrderByDescending(k => Data.Ships.Defs[k.Cls].Radius).ToList();
        // Survival's waves and the assault's fleets come here too, never under a cap.
        var quota = world.FleetCap > 0 ? Opening(side, world.FleetCap) : null;
        var f = quota is not null ? world.Factions[side.Side] : null;
        var opening = new List<ReserveShip>(list.Count);
        var taken = new ShipCounts();
        foreach (var k in list)
        {
            if (k.Hero is null && quota is not null && taken.Of(k.Cls) >= quota.Of(k.Cls)) f!.Reserve.Add(k);
            else
            {
                opening.Add(k);
                if (k.Hero is null) taken.Increase(k.Cls);
            }
        }
        var offsets = Movement.FormationOffsets(opening.Count, 90).OrderBy(p => Hypot(p.X, p.Y)).ToList();
        var placed = new ShipCounts();
        for (var i = 0; i < opening.Count; i++)
        {
            var (px, py) = (x + offsets[i].X, y + offsets[i].Y);
            if (jump)
            {
                Reserves.JumpIn(world, side.Side, opening[i], px, py, angle);
                continue;
            }
            if (opening[i].Hero is HeroId hero)
            {
                SpawnHero(world, side.Side, hero, px, py, angle);
                continue;
            }
            var s = SpawnShip(world, side.Side, opening[i].Cls, px, py, angle);
            Enlist(world, s, side.Ranks.RankOf(s.Cls, placed.Of(s.Cls)), side.Condition);
            placed.Increase(s.Cls);
        }
        if (f is null) return;
        // The best veterans of each class were placed first: the others wait, and come in with their rank (Reserves.Update).
        f.ReserveRanks = side.Ranks.Clone();
        f.ReserveRanks.Take(placed);
        f.ReserveCondition = side.Condition;
    }

    /// <summary>A campaign ship comes into battle as its fleet left it: its veteran rank, and its hull (its parts too) worn to `condition`.</summary>
    internal static void Enlist(World world, Entity ship, int rank, double condition)
    {
        Veterans.Promote(world, ship, rank, news: false);
        var c = Math.Max(0.05, condition);
        foreach (var part in ChildrenOf(world, ship.Id).Prepend(ship)) part.Hull = part.MaxHull * c;
    }

    /// <summary>
    /// How many ships of each class open a battle under a fleet cap of `cap` points: the heroes
    /// always (a few points at most); then each class in proportion to its share of the fleet's
    /// points, so that the opening force looks like the fleet; then, while there is room, one more
    /// of each class in turn, the heaviest first. The others wait in reserve.
    /// </summary>
    internal static ShipCounts Opening(BattleSide side, int cap)
    {
        var room = cap - side.Heroes.Sum(h => Reserves.Points(Data.Heroes.Defs[h].Base));
        var classes = Data.Ships.All.Where(c => side.Ships.Of(c) > 0).OrderByDescending(c => Data.Ships.Defs[c].Radius).ToList();
        var total = classes.Sum(c => Reserves.Points(c) * side.Ships.Of(c));
        if (total <= room) return side.Ships.Clone();
        var quota = new ShipCounts();
        foreach (var c in classes)
        {
            quota[c] = room <= 0 ? 0 : (int)((long)side.Ships.Of(c) * room / total);
        }
        var left = room - classes.Sum(c => Reserves.Points(c) * quota[c]);
        for (var more = true; more;)
        {
            more = false;
            foreach (var c in classes)
            {
                if (quota[c] >= side.Ships.Of(c) || Reserves.Points(c) > left) continue;
                quota[c]++;
                left -= Reserves.Points(c);
                more = true;
            }
        }
        return quota;
    }

    /// <summary>
    /// Defender on the left around its station, attacker arriving from the right. Both sides
    /// bring their research, and the AI handles their ship abilities unless told otherwise.
    /// Under a fleet cap of `fleetCap` points (a campaign's, World.FleetCap), the defender starts
    /// with what fits on the field and the attacker comes in by hyperspace at once; the rest of
    /// each fleet waits in its reserve, called in at its end of the map as room frees up.
    /// </summary>
    public static World CreatePlanetBattle(BattleSide attacker, BattleSide defender, PlanetDefense defense, int seed = 1, bool nebula = false,
        int fleetCap = 0)
    {
        var w = CreateWorld(Width, Height, seed);
        w.Fog = true;
        w.FleetCap = Math.Max(0, fleetCap);
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
        var (front, entry) = (new Point(home.X + 550, home.Y), new Point(Width - 500, home.Y));
        if (w.FleetCap > 0)
        {
            // Each side's reserve comes in where its fleet stands, and gathers there.
            foreach (var (side, at) in new[] { (defender.Side, front), (attacker.Side, entry) })
            {
                var f = w.Factions[side];
                f.Arrival = at;
                (f.RallyX, f.RallyY) = (at.X, at.Y);
            }
        }
        PlaceShips(w, defender, front.X, front.Y, 0);
        PlaceShips(w, attacker, entry.X, entry.Y, Math.PI, jump: w.FleetCap > 0);
        if (nebula) Terrains.PlanetNebula(w, home, new Point(Width - 500, home.Y));
        return w;
    }

    /// <summary>
    /// The defender holds while it keeps a station or a ship; the attacker while it keeps a ship.
    /// A ship still in its side's reserve or in hyperspace counts: it may yet come in. Returns
    /// null while both sides still stand.
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
        if (!att && !Waits(world, attacker)) return BattleResult.Defender;
        if (!def && !Waits(world, defender)) return BattleResult.Attacker;
        if (world.Time - world.LastDamage > Stalemate) return BattleResult.Retreat;
        return null;
    }

    /// <summary>Whether a side has ships in its reserve or in hyperspace.</summary>
    static bool Waits(World world, Side side) =>
        (world.Factions.TryGetValue(side, out var f) && f.Reserve.Count > 0) || world.Jumps.Exists(j => j.Side == side);

    /// <summary>
    /// Ships that can go back to the galaxy map: hangar craft stay behind, heroes are counted apart.
    /// Those still in the reserve or in hyperspace go back as they came.
    /// </summary>
    public static ShipCounts Survivors(World world, Side side)
    {
        var out_ = new ShipCounts();
        foreach (var e in world.Entities)
        {
            if (e.Kind != EntityKind.Ship || e.Side != side || e.Hero is not null || e.Launched) continue;
            if (!Data.Ships.Defs[e.Cls].FromHangar) out_.Increase(e.Cls);
        }
        foreach (var s in Reserves.Pending(world, side)) if (s.Hero is null) out_.Increase(s.Cls);
        return out_;
    }

    /// <summary>
    /// Share of their hull the surviving ships keep, 1 when none survive. Those that never came in
    /// keep what they came with (FactionState.ReserveCondition), weighed by their hull.
    /// </summary>
    public static double SurvivorCondition(World world, Side side)
    {
        double hull = 0, max = 0;
        foreach (var e in world.Entities)
        {
            if (e.Kind != EntityKind.Ship || e.Side != side || e.Hero is not null || e.Launched) continue;
            hull += Math.Max(0, e.Hull);
            max += e.MaxHull;
        }
        double waiting = 0;
        foreach (var s in Reserves.Pending(world, side)) if (s.Hero is null) waiting += Data.Ships.Defs[s.Cls].Hull;
        if (waiting > 0 && world.Factions.TryGetValue(side, out var f))
        {
            waiting *= HullMult(world, side);
            hull += waiting * f.ReserveCondition;
            max += waiting;
        }
        return max > 0 ? hull / max : 1;
    }

    /// <summary>The heroes of a side still in the battle: on the field, in its reserve or in hyperspace.</summary>
    public static List<HeroId> HeroSurvivors(World world, Side side)
    {
        var out_ = new List<HeroId>();
        foreach (var e in world.Entities) if (e.Hero is HeroId h && e.Side == side) out_.Add(h);
        foreach (var s in Reserves.Pending(world, side)) if (s.Hero is HeroId h) out_.Add(h);
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

    /// <summary>
    /// What a side opposes to `foe` in a fight to the end, by Lanchester's square law: the damage it
    /// keeps up against what the foe is made of, times the hull and shields that stand in the way.
    /// As Outcome judges a battle, mines and docks neither fight nor have to fall, and do not count.
    /// What stands still counts for less (FixedHull, FixedRate): a fleet fights it last, out of reach
    /// of most of it. Measured against the automatic resolution in OutlookTests. The ships still in
    /// a reserve or in hyperspace count as they will come in (Pending).
    /// </summary>
    public static double Power(World world, Side side, Side foe)
    {
        var (scratch, times) = Pending(world, side, foe);
        double hull = 0, foeTotal = 0;
        var foeBySize = new Dictionary<SizeClass, double>();
        void Weigh(World w, Entity e, int n)
        {
            if (!Stands(e) || (e.Side != side && e.Side != foe)) return;
            var body = (e.Hull + e.Shield) * n;
            if (e.Side == side) hull += body * (Moves(w, e) ? 1 : FixedHull);
            else
            {
                foeBySize[e.Size] = foeBySize.GetValueOrDefault(e.Size) + body;
                foeTotal += body;
            }
        }
        foreach (var e in world.Entities) Weigh(world, e, 1);
        if (scratch is not null) foreach (var e in scratch.Entities) Weigh(scratch, e, times[e.ParentId ?? e.Id]);
        double Against(WeaponDef w)
        {
            if (foeTotal <= 0) return Combat.Rate(w);
            double hit = 0;
            foreach (var (size, body) in foeBySize) hit += body / foeTotal * w.Multipliers.Of(size);
            return Combat.Rate(w) * hit;
        }
        double rate = 0;
        void Fire(World w, Entity e, int n)
        {
            if (e.Side != side || e.Launched) return;
            var mult = Combat.DamageMult(w, e) * (Moves(w, e) ? 1 : FixedRate) * n;
            foreach (var weapon in e.Weapons) rate += Against(weapon.Def) * mult;
            if (e.Hangar is not { } hangar) return;
            // A hangar keeps its craft up to its cap: the average of what it launches, that many times.
            rate += hangar.Def.Squadrons.Average(q => Data.Ships.Defs[q.Cls].Weapons.Sum(Against)) * hangar.Def.Cap * mult;
        }
        foreach (var e in world.Entities) Fire(world, e, 1);
        if (scratch is not null) foreach (var e in scratch.Entities) Fire(scratch, e, times[e.ParentId ?? e.Id]);
        return rate * hull;
    }

    /// <summary>
    /// The ships of `side` and `foe` not on the field yet, each kind once, as it will come in (its
    /// side's race, research and wear), in a world of their own; with, by the id of each, how many
    /// of its kind wait. No world when none waits.
    /// </summary>
    static (World? Scratch, Dictionary<int, int> Times) Pending(World world, Side side, Side foe)
    {
        var times = new Dictionary<int, int>();
        World? scratch = null;
        var kinds = new Dictionary<(Side, ReserveShip), int>();
        foreach (var s in new[] { side, foe })
        {
            foreach (var r in Reserves.Pending(world, s)) kinds[(s, r)] = kinds.GetValueOrDefault((s, r)) + 1;
        }
        foreach (var ((s, r), n) in kinds)
        {
            if (!world.Factions.TryGetValue(s, out var f)) continue;
            if (scratch is null) scratch = CreateWorld(world.Width, world.Height);
            if (!scratch.Factions.ContainsKey(s)) AddSide(scratch, s, f.Race, 0, f.Team, f.Color).Research = f.Research.Clone();
            var e = r.Hero is HeroId hero ? SpawnHero(scratch, s, hero, 0, 0) : SpawnShip(scratch, s, r.Cls, 0, 0);
            if (r.Hero is null) Enlist(scratch, e, 0, f.ReserveCondition);
            times[e.Id] = n;
        }
        return (scratch, times);
    }

    /// <summary>Whether fire must go through it: a ship, the station, or what carries guns.</summary>
    static bool Stands(Entity e) => e.Kind == EntityKind.Ship || e.Type == "station" || e.Weapons.Count > 0;

    /// <summary>A ship or a part of one, against a station, a platform and their parts.</summary>
    static bool Moves(World world, Entity e) =>
        e.Kind == EntityKind.Ship || (e.Kind == EntityKind.Hardpoint && world.Entities.Get(e.ParentId)?.Kind == EntityKind.Ship);

    /// <summary>What a structure's hull and guns count for against a ship's, fitted on 2 211 battles (OutlookTests).</summary>
    const double FixedHull = 0.3, FixedRate = 0.5;

    /// <summary>
    /// The attacker's power over the defender's from which a battle looks unfavorable to it, even,
    /// favorable, almost won; under Doomed, almost lost. At equal power a defender wins more often
    /// than not. Measured against the automatic resolution in OutlookTests.
    /// </summary>
    const double Doomed = 0.25, Unfavorable = 2.0 / 3, Favorable = 2, Crushing = 6;

    /// <summary>A battle in words for its attacker, from each side's Power; sides that cannot hurt each other are even.</summary>
    public static BattleOutlook Outlook(double attacker, double defender)
    {
        if (attacker <= 0 && defender <= 0) return BattleOutlook.Even;
        var edge = defender > 0 ? attacker / defender : double.PositiveInfinity;
        return edge >= Crushing ? BattleOutlook.CertainVictory
            : edge >= Favorable ? BattleOutlook.Favorable
            : edge >= Unfavorable ? BattleOutlook.Even
            : edge >= Doomed ? BattleOutlook.Unfavorable
            : BattleOutlook.CertainDefeat;
    }

    /// <summary>
    /// Under a fleet cap, a side's reinforcements gather by its fleet on the field (its warship
    /// nearest the middle of them all), where the reserve may come in, and not at its end of the
    /// map: coming from there one after the other, they were beaten one at a time, and an AI
    /// with the stronger fleet no longer took a fortified planet (a campaign between AIs, 37
    /// minutes without the cap, took 208 under it, 99 with this). With nothing of its own on the
    /// field, its arrival point.
    /// </summary>
    static void RallyAtFleet(World world, Side side)
    {
        if (!world.Factions.TryGetValue(side, out var f)) return;
        double sx = 0, sy = 0;
        var n = 0;
        foreach (var e in world.Entities)
        {
            if (e.Kind != EntityKind.Ship || e.Side != side || e.Launched || e.Size == SizeClass.Small) continue;
            (sx, sy, n) = (sx + e.X, sy + e.Y, n + 1);
        }
        Entity? lead = null;
        if (n > 0)
        {
            var (cx, cy) = (sx / n, sy / n);
            foreach (var e in world.Entities)
            {
                if (e.Kind != EntityKind.Ship || e.Side != side || e.Launched || e.Size == SizeClass.Small) continue;
                if (lead is null || Hypot(e.X - cx, e.Y - cy) < Hypot(lead.X - cx, lead.Y - cy)) lead = e;
            }
        }
        if (lead is not null) (f.RallyX, f.RallyY) = (lead.X, lead.Y);
        else if (f.Arrival is Point at) (f.RallyX, f.RallyY) = (at.X, at.Y);
    }

    /// <summary>
    /// Once per second, each side calls in its reserve as far as the fleet cap leaves room, and
    /// its idle warships attack-move towards the nearest foe they can see; seeing none, towards a
    /// foe's station, or else to scout the middle.
    /// </summary>
    public static void UpdateBattleAI(World world, IEnumerable<Side> sides, double dt)
    {
        if (Math.Floor(world.Time) == Math.Floor(world.Time - dt)) return;
        foreach (var side in sides)
        {
            if (world.FleetCap > 0) RallyAtFleet(world, side);
            Reserves.CallAll(world, side);
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
