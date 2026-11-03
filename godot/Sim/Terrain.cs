// Battle terrain (not in the web version): nebulae that hide the ships inside them and slow
// their shields, asteroid belts that block ships and light shots, neutral minefields, gravity
// wells that slow down and bend whatever comes near. Drawn from the seed on a stream of its own
// (the map itself stays the web version's), the same around every seat; sent once to players.
using SandboxShip.Sim.Data;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim;

public enum TerrainKind : byte { Nebula, Belt, Minefield, Well }

/// <summary>How much terrain a generated battle map gets: none, a little, or a lot.</summary>
public enum TerrainDensity : byte { None, Light, Dense }

/// <summary>
/// A piece of terrain. A circle (X, Y, Radius), save a belt: a band of rocks from (X, Y) to
/// (X2, Y2), Radius wide on each side. A minefield's mines go one by one.
/// </summary>
public sealed class TerrainPatch(int id, TerrainKind kind, double x, double y, double radius) : IPoint
{
    public int Id = id;
    public TerrainKind Kind = kind;
    public double X { get; set; } = x;
    public double Y { get; set; } = y;
    public double X2 = x, Y2 = y;
    public double Radius = radius;
    public int Mines, MaxMines;

    /// <summary>How far (x, y) lies from the patch's core: its center, or a belt's spine.</summary>
    public double Distance(double x, double y)
    {
        if (Kind != TerrainKind.Belt) return Hypot(x - X, y - Y);
        var (bx, by) = (X2 - X, Y2 - Y);
        var len2 = bx * bx + by * by;
        var k = len2 > 0 ? Math.Clamp(((x - X) * bx + (y - Y) * by) / len2, 0, 1) : 0;
        return Hypot(x - (X + bx * k), y - (Y + by * k));
    }

    /// <summary>Whether (x, y) lies within `pad` of the patch.</summary>
    public bool Holds(double x, double y, double pad = 0) => Distance(x, y) < Radius + pad;

    /// <summary>A minefield still armed.</summary>
    public bool Armed => Kind == TerrainKind.Minefield && Mines > 0;
}

/// <summary>A mine went off at (X, Y).</summary>
public sealed record MineBlast(double X, double Y) : SimEvent;

public static class Terrains
{
    /// <summary>A ship in a nebula is seen only this close, whatever looks at it; its shields there come back at this share.</summary>
    public const double NebulaSight = 320, NebulaShields = 0.35;
    /// <summary>Seconds between two blasts of a minefield on each ship inside it; a blast's damage, plus a share of the hull.</summary>
    public const double MineEvery = 1.0, MineDamage = 70, MineHullShare = 0.04;
    /// <summary>
    /// A gravity well: a ship at its center goes at this share of its speed, and is pulled in at
    /// up to this many pixels a second (less the heavier it is); a shot passing by is bent by up to
    /// this much a second.
    /// </summary>
    public const double WellSlow = 0.5, WellPull = 70, WellBend = 320;
    /// <summary>Room a ship keeps from a belt or an armed minefield when it goes around.</summary>
    const double Clearance = 30;

    /// <summary>The side the terrain hurts with: no one's, so everyone's foe, and no one is paid for its kills.</summary>
    public static readonly Side Neutral = new(-1);

    // Effects.

    /// <summary>The piece of terrain at (x, y), for the view: a belt, a minefield or a well before the nebula it lies in.</summary>
    public static TerrainPatch? At(World world, double x, double y)
    {
        TerrainPatch? found = null;
        foreach (var p in world.Terrain)
        {
            if (!p.Holds(x, y)) continue;
            if (p.Kind != TerrainKind.Nebula) return p;
            found ??= p;
        }
        return found;
    }

    public static bool InNebula(World world, double x, double y)
    {
        foreach (var p in world.Terrain)
        {
            if (p.Kind == TerrainKind.Nebula && p.Holds(x, y)) return true;
        }
        return false;
    }

    /// <summary>How far an enemy sees `e`: no limit, but close in a nebula.</summary>
    public static double SightCap(World world, Entity e) =>
        world.Terrain.Count > 0 && e.Kind == EntityKind.Ship && InNebula(world, e.X, e.Y) ? NebulaSight : double.PositiveInfinity;

    /// <summary>At what share a ship's shields come back: less in a nebula.</summary>
    public static double ShieldShare(World world, Entity e) =>
        world.Terrain.Count > 0 && e.Kind == EntityKind.Ship && InNebula(world, e.X, e.Y) ? NebulaShields : 1;

    /// <summary>At what share of its speed a ship goes: less the nearer it is to a well's center.</summary>
    public static double SpeedShare(World world, Entity e)
    {
        var share = 1.0;
        foreach (var p in world.Terrain)
        {
            if (p.Kind != TerrainKind.Well || Math.Abs(e.X - p.X) > p.Radius || Math.Abs(e.Y - p.Y) > p.Radius) continue;
            var d = p.Distance(e.X, e.Y);
            if (d < p.Radius) share = Math.Min(share, WellSlow + (1 - WellSlow) * d / p.Radius);
        }
        return share;
    }

    /// <summary>Whether a shot of this kind crashes on the rocks at (x, y): every one but the heavy rays and the beams.</summary>
    public static bool Stops(World world, WeaponKind kind, double x, double y)
    {
        if (kind is WeaponKind.Ray or WeaponKind.Beam) return false;
        foreach (var p in world.Terrain)
        {
            if (p.Kind == TerrainKind.Belt && p.Holds(x, y)) return true;
        }
        return false;
    }

    /// <summary>Whether a structure of this radius may stand at (x, y): not on rocks, mines or in a well.</summary>
    public static bool Clear(World world, double x, double y, double radius)
    {
        foreach (var p in world.Terrain)
        {
            if (p.Kind != TerrainKind.Nebula && (p.Kind != TerrainKind.Minefield || p.Armed) && p.Holds(x, y, radius)) return false;
        }
        return true;
    }

    /// <summary>
    /// Every step, after the ships moved: wells pull ships in and bend shots, minefields blow up
    /// under the ships in them, once a second each.
    /// </summary>
    public static void Update(World world, double dt)
    {
        if (world.Terrain.Count == 0) return;
        var blast = Math.Floor(world.Time / MineEvery) != Math.Floor((world.Time - dt) / MineEvery);
        foreach (var p in world.Terrain)
        {
            if (p.Kind == TerrainKind.Well) Pull(world, p, dt);
            else if (blast && p.Armed) Blow(world, p);
        }
    }

    static void Pull(World world, TerrainPatch p, double dt)
    {
        foreach (var e in world.Grid.Near(p.X, p.Y, p.Radius))
        {
            if (e.Kind != EntityKind.Ship) continue;
            var (dx, dy) = (p.X - e.X, p.Y - e.Y);
            var d = Hypot(dx, dy);
            if (d >= p.Radius || d < 1) continue;
            var heft = e.Size switch { SizeClass.Small => 1.0, SizeClass.Medium => 0.7, _ => 0.45 };
            var pull = WellPull * heft * (1 - d / p.Radius) * dt;
            e.X += dx / d * Math.Min(pull, d);
            e.Y += dy / d * Math.Min(pull, d);
        }
        foreach (var s in world.Projectiles)
        {
            if (s.Homing || s.Ttl <= 0) continue;
            var (dx, dy) = (p.X - s.X, p.Y - s.Y);
            var d = Hypot(dx, dy);
            if (d >= p.Radius || d < 1) continue;
            // Turned toward the center, its speed kept.
            var bend = WellBend * (1 - d / p.Radius) * dt;
            var (vx, vy) = (s.Vx + dx / d * bend, s.Vy + dy / d * bend);
            var v = Hypot(vx, vy);
            if (v <= 0) continue;
            (s.Vx, s.Vy) = (vx / v * s.Speed, vy / v * s.Speed);
        }
    }

    /// <summary>One mine goes off under each ship in the field, while it has some; a light craft rarely lives through one.</summary>
    static void Blow(World world, TerrainPatch p)
    {
        foreach (var e in world.Grid.Near(p.X, p.Y, p.Radius))
        {
            if (p.Mines <= 0) break;
            if (e.Kind != EntityKind.Ship || e.Hull <= 0 || !p.Holds(e.X, e.Y)) continue;
            p.Mines--;
            AbilitySystem.Hurt(world, e, MineDamage + MineHullShare * e.MaxHull, Neutral);
            world.Events.Add(new MineBlast(e.X, e.Y));
        }
    }

    /// <summary>A blow landing (an orbital strike, the star lance) sets off the mines under it.</summary>
    public static void Blast(World world, double x, double y, double radius)
    {
        foreach (var p in world.Terrain)
        {
            if (!p.Armed || Hypot(p.X - x, p.Y - y) > p.Radius + radius) continue;
            // As many mines as the blow covers of the field, at least one.
            var share = Math.Min(1, radius * radius / (p.Radius * p.Radius));
            var gone = Math.Max(1, (int)Math.Ceiling(p.MaxMines * share));
            p.Mines = Math.Max(0, p.Mines - gone);
            world.Events.Add(new MineBlast((p.X + x) / 2, (p.Y + y) / 2));
        }
    }

    /// <summary>Pushes a ship out of the rocks it ran into: they do not move.</summary>
    public static void PushOut(World world, Entity e)
    {
        foreach (var p in world.Terrain)
        {
            if (p.Kind != TerrainKind.Belt) continue;
            var min = p.Radius + e.Radius * 0.8;
            var (bx, by) = (p.X2 - p.X, p.Y2 - p.Y);
            var len2 = bx * bx + by * by;
            var k = len2 > 0 ? Math.Clamp(((e.X - p.X) * bx + (e.Y - p.Y) * by) / len2, 0, 1) : 0;
            var (nx, ny) = (p.X + bx * k, p.Y + by * k);
            var (dx, dy) = (e.X - nx, e.Y - ny);
            var d = Hypot(dx, dy);
            if (d >= min) continue;
            if (d < 1e-6)
            {
                // On the spine itself: out to one side, square to it.
                var len = Math.Sqrt(len2);
                (dx, dy, d) = len > 0 ? (-by / len, bx / len, 1) : (1, 0, 1);
            }
            e.X = nx + dx / d * min;
            e.Y = ny + dy / d * min;
        }
    }

    /// <summary>Moves a destination off the rocks: a point inside them could never be reached.</summary>
    public static Point OffRocks(World world, double x, double y, double radius)
    {
        foreach (var p in world.Terrain)
        {
            if (p.Kind != TerrainKind.Belt) continue;
            var min = p.Radius + radius + 4;
            var (bx, by) = (p.X2 - p.X, p.Y2 - p.Y);
            var len2 = bx * bx + by * by;
            var k = len2 > 0 ? Math.Clamp(((x - p.X) * bx + (y - p.Y) * by) / len2, 0, 1) : 0;
            var (nx, ny) = (p.X + bx * k, p.Y + by * k);
            var d = Hypot(x - nx, y - ny);
            if (d >= min) continue;
            var len = Math.Sqrt(len2);
            var (ux, uy) = d > 1e-6 ? ((x - nx) / d, (y - ny) / d) : len > 0 ? (-by / len, bx / len) : (1, 0);
            (x, y) = (nx + ux * min, ny + uy * min);
        }
        return new Point(x, y);
    }

    /// <summary>
    /// Where a ship heading for (x, y) should steer instead, to go around what is in its way: the
    /// nearest belt or armed minefield across its path. Around a belt, by the end that makes the
    /// shorter way: the farthest point it can see on an arc around that end, so that it rounds
    /// the end as a string pulled tight would. A belt with the ship and the point on the same side
    /// of it is not in the way: the ship slides along. Around a field, by the side away from its
    /// center. A field the ship heads into, or is in already, is not avoided: it was sent there,
    /// or must get out. (x, y) itself when nothing is in the way. No path search: a fleet never
    /// gets stuck, it slides along.
    /// </summary>
    public static Point Around(World world, Entity e, double x, double y)
    {
        if (world.Terrain.Count == 0) return new Point(x, y);
        var (dx, dy) = (x - e.X, y - e.Y);
        var len = Hypot(dx, dy);
        if (len < 1) return new Point(x, y);
        var (ux, uy) = (dx / len, dy / len);
        TerrainPatch? first = null;
        var firstAt = double.PositiveInfinity;
        var pad = e.Radius + Clearance;
        var (minX, maxX, minY, maxY) = (Math.Min(e.X, x) - pad, Math.Max(e.X, x) + pad, Math.Min(e.Y, y) - pad, Math.Max(e.Y, y) + pad);
        foreach (var p in world.Terrain)
        {
            var mines = p.Armed;
            if (p.Kind != TerrainKind.Belt && !mines) continue;
            // Far from the way, at a glance: most pieces, for most ships.
            if (Math.Min(p.X, p.X2) - p.Radius > maxX || Math.Max(p.X, p.X2) + p.Radius < minX
                || Math.Min(p.Y, p.Y2) - p.Radius > maxY || Math.Max(p.Y, p.Y2) + p.Radius < minY) continue;
            if (mines && (p.Holds(x, y, e.Radius) || p.Holds(e.X, e.Y))) continue;
            if (!mines && SameSide(p, e.X, e.Y, x, y)) continue;
            var at = Crossing(p, e.X, e.Y, ux, uy, len, pad);
            if (at < firstAt)
            {
                firstAt = at;
                first = p;
            }
        }
        if (first is null) return new Point(x, y);
        var room = first.Radius + e.Radius + Clearance;
        if (first.Kind == TerrainKind.Belt) return RoundEnd(first, e, x, y, room);
        return RoundField(first, e, ux, uy, room);
    }

    /// <summary>
    /// The way around a field, on the side of the path away from its center: along the tangent
    /// to a ring `room` out from it, or once on that ring, along it and back out to it.
    /// </summary>
    static Point RoundField(TerrainPatch p, Entity e, double ux, double uy, double room)
    {
        var (cx, cy) = (p.X - e.X, p.Y - e.Y);
        var d = Math.Max(1, Hypot(cx, cy));
        // The center on the path's left: the ship passes on its right, turning about it the other way.
        var turn = cx * -uy + cy * ux >= 0 ? -1 : 1;
        if (d > room)
        {
            var a = turn * Math.Asin(room / d);
            var (tx, ty) = ((cx * Math.Cos(a) - cy * Math.Sin(a)) / d, (cx * Math.Sin(a) + cy * Math.Cos(a)) / d);
            var along = Math.Sqrt(d * d - room * room) + room;
            return new Point(e.X + tx * along, e.Y + ty * along);
        }
        var (ox, oy) = (-cx / d, -cy / d);
        var (mx, my) = (turn * oy, -turn * ox);
        return new Point(e.X + mx * room + ox * (room - d), e.Y + my * room + oy * (room - d));
    }

    /// <summary>
    /// The way around a belt's end: of the points on an arc `room` out from the end, from the
    /// ship's side over the tip to the far side, the farthest the ship can reach in a straight
    /// line clear of the rocks; the first of them if it sees none, hugging the band.
    /// </summary>
    static Point RoundEnd(TerrainPatch p, Entity e, double x, double y, double room)
    {
        var (ax, ay, bx, by) = (p.X, p.Y, p.X2, p.Y2);
        var viaA = Hypot(ax - e.X, ay - e.Y) + Hypot(x - ax, y - ay);
        var viaB = Hypot(bx - e.X, by - e.Y) + Hypot(x - bx, y - by);
        var (ex, ey, ox, oy) = viaA <= viaB ? (ax, ay, bx, by) : (bx, by, ax, ay);
        var span = Hypot(ex - ox, ey - oy);
        // Out of the band at that end, and square to it toward the ship's side.
        var (sx, sy) = span > 0 ? ((ex - ox) / span, (ey - oy) / span) : (1.0, 0.0);
        var (nx, ny) = (-sy, sx);
        if ((e.X - ex) * nx + (e.Y - ey) * ny < 0) (nx, ny) = (-nx, -ny);
        var clear = p.Radius + e.Radius + Clearance / 2;
        var best = default(Point);
        for (var i = 0; i <= ArcSteps; i++)
        {
            var a = Math.PI / 2 - i * Math.PI / ArcSteps;
            var q = new Point(ex + (Math.Cos(a) * sx + Math.Sin(a) * nx) * room, ey + (Math.Cos(a) * sy + Math.Sin(a) * ny) * room);
            if (i == 0 || SpineDistance(p, e.X, e.Y, q.X, q.Y) >= clear) best = q;
        }
        return best;
    }

    /// <summary>Points on the arc around a belt's end, a sixth of a half turn apart: each sees the next past the rocks.</summary>
    const int ArcSteps = 6;

    /// <summary>Whether two points lie on the same side of a band's line, strictly: then it does not stand between them.</summary>
    static bool SameSide(TerrainPatch p, double ax, double ay, double bx, double by)
    {
        var (sx, sy) = (p.X2 - p.X, p.Y2 - p.Y);
        var da = sx * (ay - p.Y) - sy * (ax - p.X);
        var db = sx * (by - p.Y) - sy * (bx - p.X);
        return da * db > 0;
    }

    /// <summary>The shortest distance between the segment from (ax, ay) to (bx, by) and a band's spine.</summary>
    static double SpineDistance(TerrainPatch p, double ax, double ay, double bx, double by)
    {
        static double Cross(double ox, double oy, double px, double py, double qx, double qy) => (px - ox) * (qy - oy) - (py - oy) * (qx - ox);
        // Crossing segments touch.
        var d1 = Cross(ax, ay, bx, by, p.X, p.Y);
        var d2 = Cross(ax, ay, bx, by, p.X2, p.Y2);
        var d3 = Cross(p.X, p.Y, p.X2, p.Y2, ax, ay);
        var d4 = Cross(p.X, p.Y, p.X2, p.Y2, bx, by);
        if (d1 * d2 < 0 && d3 * d4 < 0) return 0;
        return Math.Min(Math.Min(ToSegment(ax, ay, p.X, p.Y, p.X2, p.Y2), ToSegment(bx, by, p.X, p.Y, p.X2, p.Y2)),
            Math.Min(ToSegment(p.X, p.Y, ax, ay, bx, by), ToSegment(p.X2, p.Y2, ax, ay, bx, by)));
    }

    /// <summary>The distance from (x, y) to the segment from (ax, ay) to (bx, by).</summary>
    static double ToSegment(double x, double y, double ax, double ay, double bx, double by)
    {
        var (vx, vy) = (bx - ax, by - ay);
        var len2 = vx * vx + vy * vy;
        var k = len2 > 0 ? Math.Clamp(((x - ax) * vx + (y - ay) * vy) / len2, 0, 1) : 0;
        return Hypot(x - ax - vx * k, y - ay - vy * k);
    }

    /// <summary>How far along a path (from (x, y), along (ux, uy), `len` long) it first comes within `pad` of the patch; infinity if never.</summary>
    static double Crossing(TerrainPatch p, double x, double y, double ux, double uy, double len, double pad)
    {
        var reach = p.Radius + pad;
        if (p.Kind != TerrainKind.Belt) return CircleHit(p.X - x, p.Y - y, ux, uy, len, reach);
        // A path that never comes near the band needs no closer look.
        if (SpineDistance(p, x, y, x + ux * len, y + uy * len) >= reach) return double.PositiveInfinity;
        // A band: the nearest of its spine's points to the path, sampled along the spine; a band is short.
        var best = double.PositiveInfinity;
        var span = Hypot(p.X2 - p.X, p.Y2 - p.Y);
        var n = Math.Max(1, (int)Math.Ceiling(span / Math.Max(20, reach * 0.5)));
        for (var i = 0; i <= n; i++)
        {
            var k = (double)i / n;
            var (cx, cy) = (p.X + (p.X2 - p.X) * k, p.Y + (p.Y2 - p.Y) * k);
            best = Math.Min(best, CircleHit(cx - x, cy - y, ux, uy, len, reach));
        }
        return best;
    }

    /// <summary>
    /// Where along a ray (from the origin, along (ux, uy), `len` long) it enters the circle of
    /// radius r at (cx, cy); infinity if it does not, or if it heads away from the center: a ship
    /// on its way out of a circle is not stopped by it.
    /// </summary>
    static double CircleHit(double cx, double cy, double ux, double uy, double len, double r)
    {
        var along = cx * ux + cy * uy;
        if (along <= 0) return double.PositiveInfinity;
        var off2 = cx * cx + cy * cy - along * along;
        if (off2 >= r * r) return double.PositiveInfinity;
        var at = along - Math.Sqrt(r * r - off2);
        return at > len ? double.PositiveInfinity : Math.Max(0, at);
    }

    // The map.

    /// <summary>
    /// Lays the terrain of a generated battle map: the same pieces around every seat, in its own
    /// frame (toward the map's center, and across), and between the seats; a piece that does not
    /// fit for one seat goes for none, and is tried again a little farther off, the same for all.
    /// Nothing on the stations and their surroundings, on the mining fields, the beacons or the
    /// raid's colossus. Its draws are its own, from the seed: the map without terrain is the same
    /// as before, and a dense map is the light one with more.
    /// </summary>
    public static void Generate(World world, TerrainDensity density, IReadOnlyList<Point> spots, int seed)
    {
        if (density == TerrainDensity.None || spots.Count == 0) return;
        var state = unchecked(seed * 7919 + 0x2F6E2B);
        double Draw() => Rng.Mulberry32(ref state);
        var c = new Point(world.Width / 2, world.Height / 2);
        // The same choices for every seat: which side gets the band, and a little play in the layout.
        var flip = Draw() < 0.5 ? 1 : -1;
        double Jitter(double amount) => (Draw() * 2 - 1) * amount;
        // Sizes to the map: a small one has less room.
        var k = Math.Clamp(Math.Min(world.Width, world.Height) / 4000, 0.75, 1.2);
        Piece[] light =
        [
            new(TerrainKind.Belt, 0.5 + Jitter(0.05), flip * (0.45 + Jitter(0.05)), 70, (600 + Jitter(100)) * k, 1.2 + Jitter(0.2)),
            new(TerrainKind.Minefield, 0.55 + Jitter(0.05), -flip * (0.42 + Jitter(0.05)), (200 + Jitter(25)) * k, 0, 0),
        ];
        Piece[] dense =
        [
            new(TerrainKind.Nebula, 0.25 + Jitter(0.05), flip * (0.9 + Jitter(0.08)), (340 + Jitter(40)) * k, 0, 0),
            new(TerrainKind.Belt, 0.9 + Jitter(0.04), -flip * (0.75 + Jitter(0.05)), 70, (480 + Jitter(80)) * k, 1.8 + Jitter(0.2)),
            new(TerrainKind.Minefield, 0.35 + Jitter(0.05), -flip * (1.0 + Jitter(0.08)), (170 + Jitter(25)) * k, 0, 0),
        ];
        // The middle: a well or a nebula.
        var middle = Draw() < 0.5 ? TerrainKind.Well : TerrainKind.Nebula;
        var middleRadius = (middle == TerrainKind.Well ? 340 + Jitter(30) : 460 + Jitter(50)) * k;
        foreach (var piece in light) Place(world, spots, c, piece, Draw);
        TryAdd(world, spots, [Make(world, middle, c.X, c.Y, middleRadius)]);
        if (density == TerrainDensity.Dense)
        {
            foreach (var piece in dense) Place(world, spots, c, piece, Draw);
            // Nebulae between the seats as well.
            var between = Between(world, spots, c, 0.55);
            TryAdd(world, spots, [.. between.Select(p => Make(world, TerrainKind.Nebula, p.X, p.Y, 320 * k))]);
        }
        Number(world);
    }

    /// <summary>
    /// The terrain of the classic map, the hand-made one for two: always the same, mirrored about
    /// the center, as fair as the map. It does not change with the seed: the classic map never does.
    /// </summary>
    public static void Classic(World world, TerrainDensity density, IReadOnlyList<Point> spots)
    {
        if (density == TerrainDensity.None) return;
        Generate(world, density, spots, 1);
    }

    /// <summary>
    /// A battle over a planet in a nebula (PlanetSpecial.Nebula): the cloud fills the middle of
    /// the field, between the defender's home and where the attacker comes in.
    /// </summary>
    public static void PlanetNebula(World world, Point home, Point away)
    {
        world.Terrain.Add(Make(world, TerrainKind.Nebula, (home.X + away.X) / 2, (home.Y + away.Y) / 2, 560));
        Number(world);
    }

    /// <summary>A piece of the layout around each seat: its place in the seat's frame, as shares of the way to the center, its size, a band's length and turn.</summary>
    readonly record struct Piece(TerrainKind Kind, double Ahead, double Across, double Radius, double Length, double Turn);

    /// <summary>Tries of a piece: where the layout puts it, then up to this many times again, each a little farther off.</summary>
    const int Tries = 30;

    static void Place(World world, IReadOnlyList<Point> spots, Point c, Piece piece, Func<double> draw)
    {
        for (var t = 0; t <= Tries; t++)
        {
            var spread = Math.Min(0.5, 0.03 * t);
            var at = t == 0 ? piece : piece with
            {
                // Drawn in from the flanks, where the map's edges are, as the tries go.
                Ahead = Math.Clamp(piece.Ahead + (draw() * 2 - 1) * spread, 0.2, 0.95),
                Across = Math.Clamp(piece.Across * Math.Max(0.4, 1 - 0.02 * t) + (draw() * 2 - 1) * spread, -1.2, 1.2),
                Turn = piece.Turn + (draw() * 2 - 1) * spread * 3,
            };
            if (TryAdd(world, spots, Lay(world, spots, c, at))) return;
        }
    }

    /// <summary>The piece around every seat, in its frame.</summary>
    static List<TerrainPatch> Lay(World world, IReadOnlyList<Point> spots, Point c, Piece piece)
    {
        var made = new List<TerrainPatch>();
        foreach (var s in spots)
        {
            var (fx, fy) = (c.X - s.X, c.Y - s.Y);
            var reach = Hypot(fx, fy);
            if (reach < 1) return [];
            var (ux, uy) = (fx / reach, fy / reach);
            var (vx, vy) = (-uy, ux);
            var (px, py) = (s.X + ux * reach * piece.Ahead + vx * reach * piece.Across, s.Y + uy * reach * piece.Ahead + vy * reach * piece.Across);
            var p = Make(world, piece.Kind, px, py, piece.Radius);
            if (piece.Kind == TerrainKind.Belt)
            {
                // A band turned from the seat's axis, centered on its place.
                var a = Math.Atan2(uy, ux) + piece.Turn;
                var (hx, hy) = (Math.Cos(a) * piece.Length / 2, Math.Sin(a) * piece.Length / 2);
                (p.X, p.Y, p.X2, p.Y2) = (px - hx, py - hy, px + hx, py + hy);
            }
            made.Add(p);
        }
        return made;
    }

    /// <summary>Points between neighbouring seats, around the center, `share` of the way out toward them.</summary>
    static List<Point> Between(World world, IReadOnlyList<Point> spots, Point c, double share)
    {
        var angles = spots.Select(s => Math.Atan2(s.Y - c.Y, s.X - c.X)).OrderBy(a => a).ToList();
        var reach = spots.Average(s => Hypot(s.X - c.X, s.Y - c.Y)) * share;
        var out_ = new List<Point>();
        for (var i = 0; i < angles.Count; i++)
        {
            var a = angles[i];
            var b = i + 1 < angles.Count ? angles[i + 1] : angles[0] + Math.Tau;
            var mid = (a + b) / 2;
            out_.Add(new Point(c.X + Math.Cos(mid) * reach, c.Y + Math.Sin(mid) * reach));
        }
        return out_;
    }

    static TerrainPatch Make(World world, TerrainKind kind, double x, double y, double radius)
    {
        var p = new TerrainPatch(0, kind, x, y, radius);
        if (kind == TerrainKind.Minefield) p.Mines = p.MaxMines = (int)Math.Round(radius / 16);
        return p;
    }

    /// <summary>Adds the pieces, all or none: each must fit the map and keep clear of everything that matters.</summary>
    static bool TryAdd(World world, IReadOnlyList<Point> spots, List<TerrainPatch> made)
    {
        if (made.Count == 0) return false;
        foreach (var p in made)
        {
            if (!Fits(world, spots, p)) return false;
        }
        // And clear of each other.
        for (var i = 0; i < made.Count; i++)
        {
            for (var j = i + 1; j < made.Count; j++)
            {
                if (Apart(made[i], made[j]) && Overlap(made[i], made[j], 120)) return false;
            }
        }
        world.Terrain.AddRange(made);
        return true;
    }

    static bool Fits(World world, IReadOnlyList<Point> spots, TerrainPatch p)
    {
        const double Margin = 220;
        var hard = p.Kind != TerrainKind.Nebula;
        foreach (var (x, y) in Ends(p))
        {
            if (x < Margin || y < Margin || x > world.Width - Margin || y > world.Height - Margin) return false;
        }
        // Stations, their surroundings and the way out of them.
        var keep = hard ? Station.ConstructionRadius + 150 : Station.ConstructionRadius - 150;
        foreach (var s in spots) if (p.Holds(s.X, s.Y, keep)) return false;
        foreach (var e in world.Entities)
        {
            if (e.Kind == EntityKind.Structure && p.Holds(e.X, e.Y, e.Radius + (hard ? 200 : 0))) return false;
            // The raid's colossus and whatever a mode put on the map.
            if (e.Kind == EntityKind.Ship && p.Holds(e.X, e.Y, e.Radius + 500)) return false;
        }
        if (hard)
        {
            foreach (var f in world.Fields) if (p.Holds(f.X, f.Y, f.Radius + 120)) return false;
        }
        // A beacon may be fought over in a cloud, not on rocks or mines.
        if (hard)
        {
            foreach (var b in world.Mode.Beacons) if (p.Holds(b.X, b.Y, ZoneControl.Radius + 60)) return false;
        }
        foreach (var q in world.Terrain) if (Apart(p, q) && Overlap(p, q, 120)) return false;
        return true;
    }

    /// <summary>Whether two pieces must keep apart: a nebula may hold rocks or mines, not another cloud or a well.</summary>
    static bool Apart(TerrainPatch a, TerrainPatch b) =>
        (a.Kind != TerrainKind.Nebula && b.Kind != TerrainKind.Nebula) || a.Kind is TerrainKind.Nebula or TerrainKind.Well && b.Kind is TerrainKind.Nebula or TerrainKind.Well;

    static IEnumerable<(double X, double Y)> Ends(TerrainPatch p)
    {
        if (p.Kind == TerrainKind.Belt)
        {
            yield return (p.X, p.Y);
            yield return (p.X2, p.Y2);
            yield break;
        }
        yield return (p.X - p.Radius, p.Y);
        yield return (p.X + p.Radius, p.Y);
        yield return (p.X, p.Y - p.Radius);
        yield return (p.X, p.Y + p.Radius);
    }

    /// <summary>Whether two patches come within `gap` of each other (a band sampled along its spine).</summary>
    static bool Overlap(TerrainPatch a, TerrainPatch b, double gap)
    {
        if (a.Kind == TerrainKind.Belt && b.Kind == TerrainKind.Belt)
        {
            for (var i = 0; i <= 8; i++)
            {
                var k = i / 8.0;
                if (b.Holds(a.X + (a.X2 - a.X) * k, a.Y + (a.Y2 - a.Y) * k, a.Radius + gap)) return true;
            }
            return false;
        }
        if (a.Kind == TerrainKind.Belt) (a, b) = (b, a);
        return b.Holds(a.X, a.Y, a.Radius + gap);
    }

    /// <summary>Ids in the order laid down.</summary>
    static void Number(World world)
    {
        for (var i = 0; i < world.Terrain.Count; i++) world.Terrain[i].Id = i + 1;
    }
}
