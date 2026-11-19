// The reserve (not in the web version): every ship a station builds waits in reserve, and its
// side calls it in by hyperspace where it holds the field: around its structures and its
// warships, never close to a foe's structure. A squadron comes in together. A jump takes a few
// seconds, longer the bigger the ship, its point marked for all to see. Coming out, a large hull
// throws the craft around it aside.
using SandboxShip.Sim.Data;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim;

/// <summary>
/// A ship built and waiting in reserve: its class, and the hero it is, if one (null: the default
/// HeroId is a hero too, the first race's first role).
/// </summary>
public readonly record struct ReserveShip(ShipClass Cls, HeroId? Hero = null);

/// <summary>
/// A campaign's fleet cap in its battles (World.FleetCap, Reserves.CapPoints), chosen in the
/// lobby: low for a small machine, high for a big one. Normal first: the default, older saves'.
/// </summary>
public enum FleetCap { Normal, Low, High }

/// <summary>A ship in hyperspace towards its point, facing `Angle`: it comes out when `Left` runs out.</summary>
public sealed class Jump
{
    public int Id;
    public Side Side;
    public ShipClass Cls;
    public HeroId? Hero;
    public double X, Y, Angle, Left, Total;
}

public static class Reserves
{
    /// <summary>How far round one's own built structures a ship may be called in, past their hull.</summary>
    public const double StructureReach = 900;
    /// <summary>How far round one's own warships (not craft), past their hull.</summary>
    public const double ShipReach = 600;
    /// <summary>Never this close to a foe's structure.</summary>
    public const double EnemyClearance = 700;
    /// <summary>
    /// Most ships a snapshot may list in a reserve or in hyperspace: one shipyard building a large
    /// hull every half-minute at best never comes near it, and a forged list is refused.
    /// </summary>
    public const int MaxListed = 4096;
    /// <summary>
    /// Most ships a snapshot may list in a reserve: a campaign fleet held back by the fleet cap
    /// may keep thousands waiting. And the highest fleet cap one may give (CapPoints stays far under).
    /// </summary>
    public const int MaxReserve = 1 << 16, MaxCap = 1 << 16;
    /// <summary>Seconds in hyperspace: small craft, medium ships, the line's large hulls, and the capitals, who announce themselves longer.</summary>
    public const double SmallJumpTime = 1.5, MediumJumpTime = 2, JumpTime = 3, CapitalJumpTime = 6;
    /// <summary>Room between the craft of a squadron coming in together.</summary>
    public const double SquadGap = 30;
    /// <summary>A ship coming out throws craft aside within its girth and this much more, at this speed at most.</summary>
    public const double ShockReach = 220;
    const double ShockSpeed = 320;

    /// <summary>How far round its arrival point (FactionState.Arrival) a side's reserve may come in.</summary>
    public const double ArrivalReach = 800;

    /// <summary>The fleet points of each cap, measured in PLAN.md (entities on the field, milliseconds a step).</summary>
    public static int CapPoints(FleetCap cap) => cap switch
    {
        FleetCap.Low => 150,
        FleetCap.High => 500,
        _ => 300,
    };

    /// <summary>
    /// What a ship weighs against the fleet cap, by its size and its weight: 1 for a small craft, 2
    /// for a medium ship, 4 for a battlecruiser or a bulwark, 6 for a carrier or a dreadnought (their
    /// craft and turrets), 12 for a titan, 20 for a leviathan or a colossus. A hero weighs as its hull.
    /// </summary>
    public static int Points(ShipClass cls) => cls switch
    {
        ShipClass.Titan => 12,
        ShipClass.Leviathan or ShipClass.Colossus => 20,
        ShipClass.Carrier or ShipClass.Dreadnought => 6,
        _ => Ships.Defs[cls].Size switch { SizeClass.Small => 1, SizeClass.Medium => 2, _ => 4 },
    };

    /// <summary>The fleet points a side has on the field: its ships (not the craft their hangars launch) and those in hyperspace.</summary>
    public static int Used(World world, Side side)
    {
        var n = 0;
        foreach (var e in world.Entities) if (e.Kind == EntityKind.Ship && e.Side == side && !e.Launched) n += Points(e.Cls);
        foreach (var j in world.Jumps) if (j.Side == side) n += Points(j.Cls);
        return n;
    }

    /// <summary>The fleet points a side may still call in under the cap; without one, as many as it likes.</summary>
    public static int Room(World world, Side side) => world.FleetCap > 0 ? Math.Max(0, world.FleetCap - Used(world, side)) : int.MaxValue;

    /// <summary>Whether any side still has ships in its reserve or in hyperspace: a battle under a fleet cap is still being fought.</summary>
    public static bool AnyPending(World world)
    {
        if (world.Jumps.Count > 0) return true;
        foreach (var f in world.Factions.Values) if (f.Reserve.Count > 0) return true;
        return false;
    }

    /// <summary>A side's ships not on the field yet: in its reserve, then in hyperspace.</summary>
    public static IEnumerable<ReserveShip> Pending(World world, Side side)
    {
        if (world.Factions.TryGetValue(side, out var f)) foreach (var s in f.Reserve) yield return s;
        foreach (var j in world.Jumps) if (j.Side == side) yield return new ReserveShip(j.Cls, j.Hero);
    }

    public static bool Capital(ShipClass cls) => cls is ShipClass.Titan or ShipClass.Leviathan or ShipClass.Colossus;

    public static double JumpTimeOf(ShipClass cls) => Capital(cls) ? CapitalJumpTime : Ships.Defs[cls].Size switch
    {
        SizeClass.Small => SmallJumpTime,
        SizeClass.Medium => MediumJumpTime,
        _ => JumpTime,
    };

    /// <summary>How far round an entity of the side its reserve may be called in: its built structures and its warships; 0 for anything else.</summary>
    static double ReachOf(Entity e) =>
        e.Kind == EntityKind.Structure && e.Build >= 1 ? e.Radius + StructureReach
        : e.Kind == EntityKind.Ship && e.Size != SizeClass.Small ? e.Girth + ShipReach : 0;

    /// <summary>Which way a ship called in at (x, y) faces: away from its station, as if come from home; toward the middle of the map near it.</summary>
    public static double Heading(World world, Side side, double x, double y)
    {
        var station = StationOf(world, side);
        if (station is not null && Hypot(x - station.X, y - station.Y) > 300) return Math.Atan2(y - station.Y, x - station.X);
        var (cx, cy) = (world.Width / 2 - x, world.Height / 2 - y);
        return cx == 0 && cy == 0 ? 0 : Math.Atan2(cy, cx);
    }

    /// <summary>Whether the side may call this ship in at (x, y) now, and if not, why.</summary>
    public static Result CanDeploy(World world, Side side, ReserveShip ship, double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)) return Result.Fail("Point invalide");
        if (!world.Factions.TryGetValue(side, out var f) || !f.Reserve.Contains(ship)) return Result.Fail("Pas en réserve");
        // Under a fleet cap (a campaign battle), a ship waits for room on the field.
        if (world.FleetCap > 0 && Points(ship.Cls) > Room(world, side)) return Result.Fail("Plafond de flotte atteint");
        var def = Ships.Defs[ship.Cls];
        var girth = def.Beam > 0 ? def.Beam : def.Radius;
        if (x < girth || y < girth || x > world.Width - girth || y > world.Height - girth) return Result.Fail("Hors de la carte");
        foreach (var j in world.Jumps)
        {
            var other = Ships.Defs[j.Cls];
            if (Hypot(j.X - x, j.Y - y) < girth + (other.Beam > 0 ? other.Beam : other.Radius)) return Result.Fail("Point d'arrivée déjà pris");
        }
        // One pass over the battle, nothing allocated: the AI tries several points, the view one a frame.
        // A campaign battle's sides may always come in at their end of the map.
        var near = f.Arrival is Point arrival && Hypot(arrival.X - x, arrival.Y - y) <= ArrivalReach;
        foreach (var e in world.Entities)
        {
            // Never on top of a building, one's own included: it could not get out.
            if (e.Kind == EntityKind.Structure && Gap(e, x, y) < girth) return Result.Fail("Emplacement occupé");
            if (e.Side == side) near = near || ReachOf(e) is var r && r > 0 && Hypot(e.X - x, e.Y - y) <= r;
            else if (e.Kind == EntityKind.Structure && Foes(world, e.Side, side) && Gap(e, x, y) < EnemyClearance) return Result.Fail("Trop près d'un bâtiment ennemi");
        }
        if (!near) return Result.Fail("Trop loin de tes forces");
        if (world.Terrain.Count > 0 && !Terrains.Clear(world, x, y, girth)) return Result.Fail("Terrain impraticable");
        return Result.OK;
    }

    /// <summary>
    /// Sends a ship of the reserve into hyperspace towards (x, y); a squadron's class brings as
    /// many of its kind as make a squadron, side by side across its heading.
    /// </summary>
    public static Result Deploy(World world, Side side, ReserveShip ship, double x, double y)
    {
        var r = CanDeploy(world, side, ship, x, y);
        if (!r.Ok) return r;
        var reserve = world.Factions[side].Reserve;
        var many = ship.Hero is null ? Math.Min(Ships.Defs[ship.Cls].Squad ?? 1, reserve.Count(s => s == ship)) : 1;
        // Under a cap, as many of the squadron as there is room for: CanDeploy found room for one.
        if (world.FleetCap > 0) many = Math.Min(many, Room(world, side) / Points(ship.Cls));
        var angle = Heading(world, side, x, y);
        var (ax, ay) = (-Math.Sin(angle), Math.Cos(angle));
        for (var i = 0; i < many; i++)
        {
            reserve.Remove(ship);
            var off = (i - (many - 1) / 2.0) * SquadGap;
            JumpIn(world, side, ship, x + ax * off, y + ay * off, angle);
        }
        return Result.OK;
    }

    /// <summary>A ship into hyperspace towards (x, y), facing `angle`: out after its class's jump time.</summary>
    public static void JumpIn(World world, Side side, ReserveShip ship, double x, double y, double angle)
    {
        var time = JumpTimeOf(ship.Cls);
        world.Jumps.Add(new Jump
        {
            Id = world.NextJumpId++, Side = side, Cls = ship.Cls, Hero = ship.Hero,
            X = Math.Clamp(x, 0, world.Width), Y = Math.Clamp(y, 0, world.Height), Angle = angle, Left = time, Total = time,
        });
    }

    /// <summary>
    /// Points round (x, y), nearest first, on a spiral whose turns are `gap` apart: where to call
    /// a whole reserve in about one point, each on a spot of its own.
    /// </summary>
    public static IEnumerable<(double X, double Y)> Around(double x, double y, double gap, int count = 60)
    {
        for (var i = 0; i < count; i++)
        {
            var r = gap * Math.Sqrt(i);
            var a = i * 2.39996;
            yield return (x + Math.Cos(a) * r, y + Math.Sin(a) * r);
        }
    }

    /// <summary>
    /// Calls the whole reserve in where the side's fleet gathers: at the rally point or round it,
    /// else on the way there from the station, else by the station; a squadron at a time. A ship
    /// with nowhere to go, or no room under the fleet cap, waits. The AI does so at every thought.
    /// </summary>
    public static void CallAll(World world, Side side)
    {
        if (!world.Factions.TryGetValue(side, out var f) || f.Reserve.Count == 0) return;
        var station = StationOf(world, side);
        // A campaign's reserve may hold hundreds of ships: one there is no room for is not tried,
        // nor one of a kind that just found no point (nothing frees one meanwhile).
        var room = Room(world, side);
        List<ReserveShip>? stuck = null;
        foreach (var ship in f.Reserve.ToList())
        {
            // Gone already with its squadron.
            if (Points(ship.Cls) > room || stuck?.Contains(ship) == true || !f.Reserve.Contains(ship)) continue;
            var called = false;
            foreach (var (x, y) in Gathering(f, station))
            {
                if (!Deploy(world, side, ship, x, y).Ok) continue;
                called = true;
                break;
            }
            if (!called) (stuck ??= []).Add(ship);
            else if (world.FleetCap > 0) room = Room(world, side);
        }
    }

    static IEnumerable<(double X, double Y)> Gathering(FactionState f, Entity? station)
    {
        yield return (f.RallyX, f.RallyY);
        for (var i = 0; i < 6; i++) yield return (f.RallyX + Math.Cos(i * Math.Tau / 6) * 260, f.RallyY + Math.Sin(i * Math.Tau / 6) * 260);
        if (station is null) yield break;
        foreach (var k in new[] { 0.6, 0.3 }) yield return (station.X + (f.RallyX - station.X) * k, station.Y + (f.RallyY - station.Y) * k);
        for (var i = 0; i < 8; i++) yield return (station.X + Math.Cos(i * Math.Tau / 8) * 450, station.Y + Math.Sin(i * Math.Tau / 8) * 450);
    }

    /// <summary>How many ships of this class the side has in reserve or on their way.</summary>
    public static int Waiting(World world, Side side, ShipClass cls)
    {
        var n = 0;
        if (world.Factions.TryGetValue(side, out var f)) foreach (var s in f.Reserve) if (s.Cls == cls) n++;
        foreach (var j in world.Jumps) if (j.Side == side && j.Cls == cls) n++;
        return n;
    }

    /// <summary>
    /// Jumps count down; a ship whose jump is over comes out, and the craft about it are thrown
    /// aside. Under a fleet cap, a side with nothing left on the field calls its reserve in by
    /// itself: held back, it would only stall the battle until the attacker fell back.
    /// </summary>
    public static void Update(World world, double dt)
    {
        for (var i = 0; i < world.Jumps.Count; i++)
        {
            var j = world.Jumps[i];
            if ((j.Left -= dt) > 0) continue;
            world.Jumps.RemoveAt(i--);
            // A side out of the battle meanwhile loses the ship it was calling in.
            if (!world.Factions.TryGetValue(j.Side, out var f)) continue;
            var s = j.Hero is HeroId hero ? SpawnHero(world, j.Side, hero, j.X, j.Y, j.Angle) : SpawnShip(world, j.Side, j.Cls, j.X, j.Y, j.Angle);
            // A campaign fleet's veterans and worn hulls come in as they were; a hero comes in whole.
            if (j.Hero is null && (f.ReserveRanks.Count > 0 || f.ReserveCondition < 1))
            {
                var rank = f.ReserveRanks.RankOf(s.Cls, 0);
                f.ReserveRanks.Add(s.Cls, rank, -1);
                Battle.Enlist(world, s, rank, f.ReserveCondition);
            }
            world.Events.Add(new Delivered(s.Id));
            if (s.Size == SizeClass.Large) Shock(world, s);
        }
        if (world.FleetCap <= 0 || Math.Floor(world.Time) == Math.Floor(world.Time - dt)) return;
        foreach (var f in world.Factions.Values)
        {
            if (f.Reserve.Count > 0 && Used(world, f.Id) == 0) CallAll(world, f.Id);
        }
    }

    /// <summary>Craft near a ship just out of hyperspace are thrown away from it, the nearer the harder.</summary>
    static void Shock(World world, Entity s)
    {
        var reach = s.Girth + ShockReach;
        foreach (var e in world.Grid.Near(s.X, s.Y, reach + s.Radius))
        {
            if (e.Kind != EntityKind.Ship || e.Size != SizeClass.Small) continue;
            var (kx, ky) = Keel(s, e.X, e.Y);
            var (dx, dy) = (e.X - kx, e.Y - ky);
            var d = Hypot(dx, dy);
            if (d >= reach) continue;
            if (d < 1) (dx, dy, d) = (Math.Cos(s.Angle + Math.PI / 2), Math.Sin(s.Angle + Math.PI / 2), 1);
            var push = ShockSpeed * (1 - d / reach);
            e.KnockX += dx / d * push;
            e.KnockY += dy / d * push;
        }
    }
}
