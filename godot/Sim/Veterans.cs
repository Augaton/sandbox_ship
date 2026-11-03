// Veterans (not in the web version): a ship earns experience by hurting foes and more by
// finishing them, measured against its own toughness, so that a frigate that downs a cruiser
// learns more than a cruiser that downs a frigate. Each rank makes it a little deadlier, tougher
// and quicker to mend. Mines teach nothing, nor do foes of no faction.
using SandboxShip.Sim.Data;

namespace SandboxShip.Sim;

public static class Veterans
{
    public const int MaxRank = 3;

    /// <summary>Experience each rank takes, in the ship's own toughness destroyed.</summary>
    static readonly double[] Thresholds = [3, 8, 16];

    /// <summary>Per rank: more damage dealt (aim and rate of fire), more hull, faster shields and self-repair.</summary>
    public const double DamagePerRank = 0.05, HullPerRank = 0.05, RegenPerRank = 0.10;

    /// <summary>Share of a foe's toughness earned for the blow that finishes it, on top of the damage.</summary>
    const double KillShare = 0.5;

    public static double DamageMult(Entity e) => 1 + DamagePerRank * e.Rank;

    public static double RegenMult(Entity e) => 1 + RegenPerRank * e.Rank;

    static double HullMult(int rank) => 1 + HullPerRank * rank;

    /// <summary>Experience rank `rank` takes (1 to MaxRank), for the selection panel's bar.</summary>
    public static double Needed(int rank) => Thresholds[Math.Clamp(rank, 1, MaxRank) - 1];

    static double Toughness(Entity e) => e.MaxHull + e.MaxShield;

    /// <summary>
    /// The ship credited with what `by` does: the ship a turret is on, the ship a craft flies for;
    /// none for a structure, its guns and its craft.
    /// </summary>
    public static Entity? Shooter(World world, Entity by)
    {
        var ship = AbilitySystem.OwnerOf(world, by);
        if (ship.Launched && Movement.CarrierOf(world, ship) is { } carrier) ship = carrier;
        return ship.Kind == EntityKind.Ship && !ship.Launched ? ship : null;
    }

    /// <summary>What a blow of `ship` earns it: `taken`, the hull and shield points it took off `t`, with `killed` when it finished it.</summary>
    public static void Credit(World world, Entity ship, Entity t, double taken, bool killed)
    {
        if (taken <= 0 || ship.Rank >= MaxRank || !world.Factions.ContainsKey(t.Side)) return;
        var own = Toughness(ship);
        if (own <= 0) return;
        ship.Xp += (taken + (killed ? KillShare * Toughness(t) : 0)) / own;
        var rank = ship.Rank;
        while (rank < MaxRank && ship.Xp >= Thresholds[rank]) rank++;
        if (rank > ship.Rank) Promote(world, ship, rank);
    }

    /// <summary>
    /// Gives a ship a rank, its hull grown to match, as much more of it left: as earned, or for a
    /// campaign's veteran coming back to battle, without `news`.
    /// </summary>
    public static void Promote(World world, Entity ship, int rank, bool news = true)
    {
        rank = Math.Clamp(rank, 0, MaxRank);
        if (rank == ship.Rank) return;
        var ratio = HullMult(rank) / HullMult(ship.Rank);
        ship.Hull *= ratio;
        ship.MaxHull *= ratio;
        // At least the rank's experience: what is earned from there counts towards the next.
        if (rank > 0) ship.Xp = Math.Max(ship.Xp, Thresholds[rank - 1]);
        ship.Rank = rank;
        if (news && rank > 0) world.Events.Add(new Promoted(ship.Id, ship.Side, rank));
    }

    /// <summary>The ranks of the ships a battle leaves to a side: those that go back to the galaxy map, as Battle.Survivors counts them.</summary>
    public static RankCounts Survivors(World world, Side side)
    {
        var out_ = new RankCounts();
        foreach (var e in world.Entities)
        {
            if (e.Kind != EntityKind.Ship || e.Side != side || e.Hero is not null || e.Launched || e.Rank == 0) continue;
            if (!Data.Ships.Defs[e.Cls].FromHangar) out_.Add(e.Cls, e.Rank);
        }
        return out_;
    }
}

/// <summary>
/// A campaign fleet's veterans (not in the web version): for each class, how many of its ships
/// are of rank 1, 2 and 3; the others are rookies. A fleet that loses ships loses its rookies
/// first (Fit), and ships that leave it take its best (Take). Counts as a save may hold them,
/// of any length or below zero, read as none.
/// </summary>
public sealed class RankCounts : Dictionary<ShipClass, int[]>
{
    public int Of(ShipClass c, int rank) => TryGetValue(c, out var n) && rank >= 1 && rank <= n.Length ? Math.Max(0, n[rank - 1]) : 0;

    /// <summary>All the veterans of a class.</summary>
    public int Of(ShipClass c)
    {
        var all = 0;
        for (var rank = 1; rank <= Veterans.MaxRank; rank++) all += Of(c, rank);
        return all;
    }

    public void Add(ShipClass c, int rank, int count = 1)
    {
        if (rank < 1 || rank > Veterans.MaxRank || count == 0) return;
        var n = new int[Veterans.MaxRank];
        for (var r = 1; r <= Veterans.MaxRank; r++) n[r - 1] = Of(c, r);
        n[rank - 1] += count;
        if (n.All(x => x <= 0)) Remove(c);
        else this[c] = n;
    }

    public void AddAll(RankCounts other)
    {
        foreach (var (c, _) in other)
        {
            for (var rank = 1; rank <= Veterans.MaxRank; rank++) Add(c, rank, other.Of(c, rank));
        }
    }

    public RankCounts Clone()
    {
        var copy = new RankCounts();
        copy.AddAll(this);
        return copy;
    }

    /// <summary>No more veterans of a class than `ships` has of it: those over it go, the lowest ranks first.</summary>
    public void Fit(ShipCounts ships)
    {
        foreach (var c in Keys.ToList())
        {
            var over = Of(c) - ships.Of(c);
            for (var rank = 1; rank <= Veterans.MaxRank && over > 0; rank++)
            {
                var gone = Math.Min(over, Of(c, rank));
                Add(c, rank, -gone);
                over -= gone;
            }
            // Read from a save: below zero is none.
            if (ContainsKey(c) && Of(c) == 0) Remove(c);
        }
    }

    /// <summary>The best of each class for `ships` leaving, highest rank first, taken out of these.</summary>
    public RankCounts Take(ShipCounts ships)
    {
        var taken = new RankCounts();
        foreach (var c in Keys.ToList())
        {
            var want = ships.Of(c);
            for (var rank = Veterans.MaxRank; rank >= 1 && want > 0; rank--)
            {
                var n = Math.Min(want, Of(c, rank));
                Add(c, rank, -n);
                taken.Add(c, rank, n);
                want -= n;
            }
        }
        return taken;
    }

    /// <summary>The rank of the `k`-th ship of a class placed in battle, the best placed first.</summary>
    public int RankOf(ShipClass c, int k)
    {
        for (var rank = Veterans.MaxRank; rank >= 1; rank--)
        {
            k -= Of(c, rank);
            if (k < 0) return rank;
        }
        return 0;
    }
}
