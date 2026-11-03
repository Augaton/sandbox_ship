// A battle over time, for its end screen (not in the web version): each side's army, economy and
// losses, sampled every few seconds, and the ships of each side that destroyed the most. It only
// reads the world: a battle goes the same with it or without, and a replay records it again.
using SandboxShip.Sim.Data;

namespace SandboxShip.Sim;

/// <summary>
/// A side at one sample: the value of its ships in flight, the credits it earned and spent so far,
/// the ships it built and lost, what it destroyed, the damage it dealt.
/// </summary>
public readonly record struct HistoryPoint(float Army, float Earned, float Spent, int Built, int Lost, int Kills, float Damage);

/// <summary>A ship that destroyed something: how many and for how much, and whether it still flies.</summary>
public sealed class Ace
{
    public int Id;
    public ShipClass Cls;
    public HeroId? Hero;
    public int Kills;
    public double Value;
    public bool Alive = true;
}

public sealed class BattleHistory
{
    /// <summary>Samples kept at most: past that, every other one goes and the pace halves, so a long battle weighs no more than a short one.</summary>
    public const int MaxSamples = 120;
    /// <summary>The best ships kept for each side.</summary>
    public const int MaxAces = 5;
    /// <summary>Seconds between two samples, at first.</summary>
    public const double FirstEvery = 5;
    /// <summary>How far a destroyed ship's killer is looked for, when none of the killing side was aiming at it.</summary>
    const double CreditReach = 900;

    public double Every = FirstEvery;
    public double NextAt;
    /// <summary>The battle is decided and its last sample taken: nothing more comes.</summary>
    public bool Closed;
    public readonly List<double> Times = [];
    /// <summary>Each side's points, one per time, sides in the order they first came.</summary>
    public readonly Dictionary<Side, List<HistoryPoint>> Sides = [];
    /// <summary>Each side's ships that destroyed the most, the best first.</summary>
    public readonly Dictionary<Side, List<Ace>> Aces = [];
    /// <summary>What each side had when the record began: what it spent is what it had and earned, less what it has left.</summary>
    readonly Dictionary<Side, double> start = [];

    /// <summary>Once a step, last: a sample when one is due, and the last one as soon as there is a winner.</summary>
    public void Step(World world)
    {
        if (Closed) return;
        if (world.Winner is not null)
        {
            Close(world);
            return;
        }
        if (world.Time < NextAt) return;
        Sample(world);
        NextAt = world.Time + Every;
    }

    /// <summary>The last sample, once the battle is over: at its winner, or when it ends otherwise (a retreat, the AI left to finish it).</summary>
    public void Close(World world)
    {
        if (Closed) return;
        Sample(world);
        Closed = true;
    }

    /// <summary>Every side's point now.</summary>
    public void Sample(World world)
    {
        if (Times.Count > 0 && Times[^1] == world.Time) return;
        Times.Add(world.Time);
        var army = new Dictionary<Side, double>();
        foreach (var e in world.Entities)
        {
            if (e.Kind == EntityKind.Ship && e.Hull > 0) army[e.Side] = army.GetValueOrDefault(e.Side) + ValueOf(e);
        }
        foreach (var f in world.Factions.Values)
        {
            if (!start.ContainsKey(f.Id)) start[f.Id] = f.Credits - f.Stats.Earned;
            if (!Sides.TryGetValue(f.Id, out var points))
            {
                // A side that comes late starts at nothing for the times before it.
                Sides[f.Id] = points = [.. Enumerable.Repeat(default(HistoryPoint), Times.Count - 1)];
            }
            var s = f.Stats;
            var spent = Math.Max(0, start[f.Id] + s.Earned - f.Credits);
            points.Add(new HistoryPoint((float)army.GetValueOrDefault(f.Id), (float)s.Earned, (float)spent, s.Built, s.Lost, s.Kills, (float)s.Damage));
        }
        if (Times.Count > MaxSamples) Compact();
    }

    /// <summary>Halves the series, keeping the first and the last sample, and the pace with it.</summary>
    void Compact()
    {
        static void Halve<T>(List<T> list)
        {
            var kept = 0;
            for (var i = 0; i < list.Count; i += 2) list[kept++] = list[i];
            if ((list.Count - 1) % 2 != 0) list[kept++] = list[^1];
            list.RemoveRange(kept, list.Count - kept);
        }
        Halve(Times);
        foreach (var points in Sides.Values) Halve(points);
        Every *= 2;
    }

    /// <summary>What a ship or structure is worth to whoever destroys it.</summary>
    public static double ValueOf(Entity e) => e.Kind switch
    {
        EntityKind.Ship => e.Hero is HeroId h ? Heroes.Defs[h].Cost : Ships.UnitValue(e.Cls),
        EntityKind.Structure when Ids.IsBuildable(e.Type, out var b) => Buildables.Defs[b].Cost,
        _ => 0,
    };

    /// <summary>
    /// A ship or structure of the other side down: its killer is the ship of side `by` nearest to
    /// it among those aiming at it (a turret counting as its ship), else the nearest one close by;
    /// it is credited with it, and its side's best ships are kept up.
    /// </summary>
    public void Credit(World world, Entity victim, Side by)
    {
        Entity? best = null;
        var bestAiming = false;
        var bestD = double.PositiveInfinity;
        foreach (var t in world.Grid.Near(victim.X, victim.Y, CreditReach * 1.5))
        {
            if (t.Side != by || t.Hull <= 0) continue;
            var ship = t.Kind == EntityKind.Ship ? t : t.Kind == EntityKind.Hardpoint ? world.Entities.Get(t.ParentId) : null;
            if (ship is not { Kind: EntityKind.Ship } || ship.Hull <= 0) continue;
            var aiming = t.TargetId == victim.Id || ship.Order is { Type: OrderType.Attack } o && o.TargetId == victim.Id;
            var d = Worlds.Dist(ship, victim);
            if (!aiming && d > CreditReach) continue;
            if (best is not null && (bestAiming && !aiming || bestAiming == aiming && (d > bestD || d == bestD && ship.Id >= best.Id))) continue;
            (best, bestAiming, bestD) = (ship, aiming, d);
        }
        if (best is null) return;
        best.Kills++;
        best.KilledValue += ValueOf(victim);
        if (!Aces.TryGetValue(by, out var aces)) Aces[by] = aces = [];
        var ace = aces.Find(a => a.Id == best.Id);
        if (ace is null) aces.Add(ace = new Ace { Id = best.Id, Cls = best.Cls, Hero = best.Hero });
        (ace.Kills, ace.Value) = (best.Kills, best.KilledValue);
        aces.Sort((a, b) => a.Kills != b.Kills ? b.Kills.CompareTo(a.Kills) : a.Value != b.Value ? b.Value.CompareTo(a.Value) : a.Id.CompareTo(b.Id));
        if (aces.Count > MaxAces) aces.RemoveRange(MaxAces, aces.Count - MaxAces);
    }

    /// <summary>A ship down: if it was one of its side's best, it is marked lost.</summary>
    public void Lost(Entity e)
    {
        if (e.Kills == 0 || !Aces.TryGetValue(e.Side, out var aces)) return;
        foreach (var a in aces) if (a.Id == e.Id) a.Alive = false;
    }

    // Over the network: the host sends it once the battle is decided (Message.Record).

    public static void Write(BinaryWriter w, BattleHistory h)
    {
        w.Write(h.Every);
        w.Write7BitEncodedInt(h.Times.Count);
        foreach (var t in h.Times) w.Write(t);
        w.Write7BitEncodedInt(h.Sides.Count);
        foreach (var (side, points) in h.Sides)
        {
            w.Write7BitEncodedInt(side.Id);
            foreach (var p in points)
            {
                w.Write(p.Army);
                w.Write(p.Earned);
                w.Write(p.Spent);
                w.Write7BitEncodedInt(p.Built);
                w.Write7BitEncodedInt(p.Lost);
                w.Write7BitEncodedInt(p.Kills);
                w.Write(p.Damage);
            }
        }
        w.Write7BitEncodedInt(h.Aces.Count);
        foreach (var (side, aces) in h.Aces)
        {
            w.Write7BitEncodedInt(side.Id);
            w.Write7BitEncodedInt(aces.Count);
            foreach (var a in aces)
            {
                w.Write7BitEncodedInt(a.Id);
                w.Write((byte)a.Cls);
                if (a.Hero is HeroId hero)
                {
                    w.Write((byte)hero.Faction);
                    w.Write((byte)hero.Role);
                }
                else w.Write(byte.MaxValue);
                w.Write7BitEncodedInt(a.Kills);
                w.Write(a.Value);
                w.Write(a.Alive);
            }
        }
    }

    /// <summary>A history as the host sent it, every count, value and number checked: a wrong one throws InvalidDataException.</summary>
    public static BattleHistory Read(BinaryReader r)
    {
        var h = new BattleHistory { Every = Finite(r.ReadDouble()), Closed = true };
        var times = Bounded(r, MaxSamples + 1, "Échantillons");
        for (var i = 0; i < times; i++) h.Times.Add(Finite(r.ReadDouble()));
        var sides = Bounded(r, Lobby.MaxSeats + 1, "Camps");
        for (var s = 0; s < sides; s++)
        {
            var side = r.ReadSide();
            var points = new List<HistoryPoint>(times);
            for (var i = 0; i < times; i++)
            {
                points.Add(new HistoryPoint(Finite(r.ReadSingle()), Finite(r.ReadSingle()), Finite(r.ReadSingle()),
                    r.ReadVarint(), r.ReadVarint(), r.ReadVarint(), Finite(r.ReadSingle())));
            }
            if (!h.Sides.TryAdd(side, points)) throw new InvalidDataException($"Camp en double : {side.Id}");
        }
        var aceSides = Bounded(r, Lobby.MaxSeats + 1, "Camps");
        for (var s = 0; s < aceSides; s++)
        {
            var side = r.ReadSide();
            var count = Bounded(r, MaxAces, "Vaisseaux");
            var aces = new List<Ace>(count);
            for (var i = 0; i < count; i++)
            {
                var ace = new Ace { Id = r.ReadVarint(), Cls = r.ReadEnum<ShipClass>() };
                var faction = r.ReadByte();
                if (faction != byte.MaxValue)
                {
                    var hero = new HeroId(Wire.Known<Faction>(faction), r.ReadEnum<HeroRole>());
                    ace.Hero = Heroes.Defs.ContainsKey(hero) ? hero : throw new InvalidDataException($"Héros inconnu : {hero}");
                }
                ace.Kills = r.ReadVarint();
                ace.Value = Finite(r.ReadDouble());
                ace.Alive = r.ReadBoolean();
                aces.Add(ace);
            }
            if (!h.Aces.TryAdd(side, aces)) throw new InvalidDataException($"Camp en double : {side.Id}");
        }
        return h;
    }

    public static byte[] Bytes(BattleHistory h)
    {
        using var m = new MemoryStream();
        using (var w = new BinaryWriter(m)) Write(w, h);
        return m.ToArray();
    }

    static int Bounded(BinaryReader r, int max, string what)
    {
        var n = r.ReadVarint();
        return n >= 0 && n <= max ? n : throw new InvalidDataException($"{what} : {n}");
    }

    static double Finite(double v) => double.IsFinite(v) ? v : throw new InvalidDataException("Nombre invalide");

    static float Finite(float v) => float.IsFinite(v) ? v : throw new InvalidDataException("Nombre invalide");
}
