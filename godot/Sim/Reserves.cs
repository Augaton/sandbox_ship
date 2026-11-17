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
    /// <summary>Seconds in hyperspace: small craft, medium ships, the line's large hulls, and the capitals, who announce themselves longer.</summary>
    public const double SmallJumpTime = 1.5, MediumJumpTime = 2, JumpTime = 3, CapitalJumpTime = 6;
    /// <summary>Room between the craft of a squadron coming in together.</summary>
    public const double SquadGap = 30;
    /// <summary>A ship coming out throws craft aside within its girth and this much more, at this speed at most.</summary>
    public const double ShockReach = 220;
    const double ShockSpeed = 320;

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
        var def = Ships.Defs[ship.Cls];
        var girth = def.Beam > 0 ? def.Beam : def.Radius;
        if (x < girth || y < girth || x > world.Width - girth || y > world.Height - girth) return Result.Fail("Hors de la carte");
        foreach (var j in world.Jumps)
        {
            var other = Ships.Defs[j.Cls];
            if (Hypot(j.X - x, j.Y - y) < girth + (other.Beam > 0 ? other.Beam : other.Radius)) return Result.Fail("Point d'arrivée déjà pris");
        }
        // One pass over the battle, nothing allocated: the AI tries several points, the view one a frame.
        var near = false;
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
        var time = JumpTimeOf(ship.Cls);
        var angle = Heading(world, side, x, y);
        var (ax, ay) = (-Math.Sin(angle), Math.Cos(angle));
        for (var i = 0; i < many; i++)
        {
            reserve.Remove(ship);
            var off = (i - (many - 1) / 2.0) * SquadGap;
            world.Jumps.Add(new Jump
            {
                Id = world.NextJumpId++, Side = side, Cls = ship.Cls, Hero = ship.Hero,
                X = Math.Clamp(x + ax * off, 0, world.Width), Y = Math.Clamp(y + ay * off, 0, world.Height),
                Angle = angle, Left = time, Total = time,
            });
        }
        return Result.OK;
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
    /// with nowhere to go waits. The AI does so at every thought.
    /// </summary>
    public static void CallAll(World world, Side side)
    {
        if (!world.Factions.TryGetValue(side, out var f) || f.Reserve.Count == 0) return;
        var station = StationOf(world, side);
        foreach (var ship in f.Reserve.ToList())
        {
            // Gone already with its squadron.
            if (!f.Reserve.Contains(ship)) continue;
            foreach (var (x, y) in Gathering(f, station))
            {
                if (Deploy(world, side, ship, x, y).Ok) break;
            }
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

    /// <summary>Jumps count down; a ship whose jump is over comes out, and the craft about it are thrown aside.</summary>
    public static void Update(World world, double dt)
    {
        for (var i = 0; i < world.Jumps.Count; i++)
        {
            var j = world.Jumps[i];
            if ((j.Left -= dt) > 0) continue;
            world.Jumps.RemoveAt(i--);
            // A side out of the battle meanwhile loses the ship it was calling in.
            if (!world.Factions.ContainsKey(j.Side)) continue;
            var s = j.Hero is HeroId hero ? SpawnHero(world, j.Side, hero, j.X, j.Y, j.Angle) : SpawnShip(world, j.Side, j.Cls, j.X, j.Y, j.Angle);
            world.Events.Add(new Delivered(s.Id));
            if (s.Size == SizeClass.Large) Shock(world, s);
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
