// Stations, their parts and the buildings around them, drawn in each race's hand as the forged
// ships are: plates lit from the upper left, panel lines, rivets, lit windows. Not in the web
// version, whose stations are assembled from the Space Breaker pack, one look recolored for all.
using Godot;
using SandboxShip.Sim.Data;

namespace SandboxShip.Render;

public static partial class ShipForge
{
    /// <summary>
    /// World units a structure's pixel stands for, at the size its type is designed for: about
    /// the packs' ships' own, so that everything on the field shares one pixel size.
    /// </summary>
    public const double StructurePixel = 1.5;

    static double P(double world) => world / StructurePixel;

    /// <summary>What a turret carries, as drawn: one silhouette per kind of weapon.</summary>
    public enum Gun { Laser, HeavyLaser, Ray, Flak, Beam, Burst, Swarm, Ion, Torpedo }

    /// <summary>The silhouette of a weapon, on a part of `role`.</summary>
    public static Gun GunOf(WeaponDef? w, HardpointRole role) => w?.Kind switch
    {
        WeaponKind.Bullet when w.PointDefense => Gun.Flak,
        WeaponKind.Bullet when w.Burst > 1 => Gun.Burst,
        WeaponKind.Bullet => Gun.Flak,
        WeaponKind.Ray => Gun.Ray,
        WeaponKind.Beam => Gun.Beam,
        WeaponKind.Missile => Gun.Swarm,
        WeaponKind.Ion => Gun.Ion,
        WeaponKind.Torpedo => Gun.Torpedo,
        _ => role == HardpointRole.HeavyTurret ? Gun.HeavyLaser : Gun.Laser,
    };

    /// <summary>
    /// A light on a structure: pixels from the sprite's center, its color, where in its cycle it
    /// starts (0 to 1), and whether it blinks rather than breathes.
    /// </summary>
    public readonly record struct Lamp(float X, float Y, Color Color, float Phase, bool Blink);

    /// <summary>A structure's image and the lights the battle view makes live on it.</summary>
    public sealed record Built(Image Image, IReadOnlyList<Lamp> Lamps);

    /// <summary>
    /// A structure on the drawing board: an odd number of columns and rows, so that the middle
    /// falls on a pixel, the lamps noted as they are lit.
    /// </summary>
    sealed class Yard
    {
        public readonly Canvas C;
        public readonly Faction F;
        public readonly Kit K;
        public readonly Hand H;
        public readonly Random Rand;
        /// <summary>The middle, in the canvas's coordinates (pixel row y spans y to y + 1).</summary>
        public readonly double Cy;
        readonly List<(double Dx, int Row, M M, bool Blink)> lamps = [];

        public Yard(int halfW, int halfH, Faction f, int seed)
        {
            C = new Canvas(halfW * 2 + 1, halfH * 2 + 1);
            F = f;
            K = KitOf(f, C);
            Rand = new Random(seed);
            Cy = halfH + 0.5;
            H = f switch
            {
                Faction.Klaed => new KlaedHand(this),
                Faction.Nairan => new NairanHand(this),
                Faction.Errant => new ErrantHand(this),
                _ => new NautolanHand(this),
            };
        }

        /// <summary>A lit pixel `dx` from the middle (and its mirror) on row `row`, live in battle.</summary>
        public void Lamp(double dx, double row, M m = M.Light, bool blink = false)
        {
            var (x, r) = (Math.Round(dx), (int)Math.Floor(row));
            C.Glow(C.Rect(x - 0.5, r, x + 0.5, r + 1), m, 1);
            lamps.Add((x, r, m, blink));
        }

        /// <summary>Lamps round a circle `radius` from (0, cy), `count` of them, starting at `start` radians.</summary>
        public void LampRing(double cy, double radius, int count, double start = 0, M m = M.Light, bool blink = false)
        {
            for (var i = 0; i < count; i++)
            {
                var a = start + i * Math.Tau / count;
                // The mirror draws the left half: only the right half's lamps are placed.
                if (Math.Cos(a) < -1e-6) continue;
                Lamp(Math.Cos(a) * radius, cy + Math.Sin(a) * radius, m, blink);
            }
        }

        public Built Paint(int shade = 0)
        {
            var img = ShipForge.Paint(C, F, shade);
            var colors = Colors(F);
            var list = new List<Lamp>();
            var halfH = C.H / 2.0;
            foreach (var (dx, row, m, blink) in lamps)
            {
                var color = colors[m][^1];
                foreach (var x in dx < 0.25 ? [dx] : new[] { -dx, dx }) list.Add(new Lamp((float)x, (float)(row + 0.5 - halfH), color, (float)Rand.NextDouble(), blink));
            }
            return new Built(img, list);
        }
    }

    /// <summary>A race's hand for structures: how it builds a hub, a ring, a spar, a module, a gun housing.</summary>
    abstract class Hand(Yard y)
    {
        protected readonly Yard Y = y;
        protected Canvas C => Y.C;

        /// <summary>The body of a station or a building, about `r` round, centered on row `cy`.</summary>
        public abstract void Hub(double cy, double r);
        /// <summary>A ring of hull `r` from the middle, `w` thick.</summary>
        public abstract void Ring(double cy, double r, double w);
        /// <summary>A strut from one point to another, `w` thick, and its mirror.</summary>
        public abstract void Spar(double dx0, double y0, double dx1, double y1, double w);
        /// <summary>A block `hw` by `hh` (half sizes) centered `dx` from the middle on row `y`, and its mirror.</summary>
        public abstract void Module(double dx, double y, double hw, double hh);
        /// <summary>The body a gun sits in, `r` round, on row `cy`.</summary>
        public abstract void Housing(double cy, double r);
        /// <summary>The heart of a station: its eye, lit.</summary>
        public abstract void Core(double cy, double r);

        /// <summary>A solar array: dark cells in a frame of plate.</summary>
        public virtual void Panel(double dx, double y, double hw, double hh)
        {
            var frame = C.Rect(dx - hw, y - hh, dx + hw, y + hh);
            C.Raise(frame, M.Plate, 2, 1);
            for (var x = Math.Floor(dx - hw) + 1; x + 3 <= dx + hw; x += 4)
            {
                for (var yy = Math.Floor(y - hh) + 1; yy + 2 <= y + hh; yy += 3)
                {
                    C.Glow(C.Rect(x, yy, x + 3, yy + 2), M.Glass, 0);
                    C.Glow(C.Rect(x, yy, x + 3, yy + 1), M.Glass, 1);
                }
            }
        }

        /// <summary>A dish aerial facing up, its feed lit.</summary>
        public virtual void Dish(double dx, double y, double r)
        {
            C.Raise(C.Oval(dx, y, r, r * 0.8), M.Plate, 5, 2);
            C.Carve(C.Oval(dx, y + r * 0.1, r * 0.7, r * 0.55), 1.8f);
            C.Raise(C.Line(dx, y, dx, y - r * 0.8, 1), M.Trim, 6, 1);
            Y.Lamp(dx, y - r * 0.8, M.Light, true);
        }

        /// <summary>Studs along a line.</summary>
        protected void Rivets(double dx0, double y0, double dx1, double y1, double step)
        {
            var len = Math.Sqrt((dx1 - dx0) * (dx1 - dx0) + (y1 - y0) * (y1 - y0));
            for (var t = 0.0; t <= len; t += step)
            {
                var (x, yy) = (dx0 + (dx1 - dx0) * t / Math.Max(len, 1e-6), y0 + (y1 - y0) * t / Math.Max(len, 1e-6));
                C.Lift(C.Rect(Math.Round(x) - 0.5, Math.Floor(yy), Math.Round(x) + 0.5, Math.Floor(yy) + 1), 0.9f);
            }
        }

    }

    /// <summary>A ring round (0, cy), `r` to its middle line, `w` wide.</summary>
    static bool[] Annulus(Canvas c, double cy, double r, double w) =>
        Canvas.Minus(c.Oval(0, cy, r + w / 2, r + w / 2), c.Oval(0, cy, r - w / 2, r - w / 2));

    /// <summary>Kla'ed: octagons with cut corners, ribbed red caissons, grey girders, yellow dashed trims, cyan eyes.</summary>
    sealed class KlaedHand(Yard y) : Hand(y)
    {
        bool[] Octagon(double cy, double r) => C.Body((r * 0.414, cy - r), (r, cy - r * 0.414), (r, cy + r * 0.414), (r * 0.414, cy + r));

        void Ribs(bool[] shape, double cy, double r, double step)
        {
            for (var dx = 3.0; dx < r - 2; dx += step) C.CarveLine(shape, dx, cy - r, dx, cy + r, 1.1f);
            for (var yy = cy - r + step * 3; yy < cy + r - 2; yy += step * 4) C.CarveLine(shape, 0, yy, r, yy, 1.2f);
        }

        void Dashes(double dx0, double y0, double dx1, double y1)
        {
            var len = Math.Max(Math.Abs(dx1 - dx0), Math.Abs(y1 - y0));
            for (var t = 0.0; t <= len; t += 2)
            {
                var (x, yy) = (dx0 + (dx1 - dx0) * t / Math.Max(len, 1), y0 + (y1 - y0) * t / Math.Max(len, 1));
                C.Glow(C.Rect(Math.Round(x) - 0.5, Math.Floor(yy), Math.Round(x) + 0.5, Math.Floor(yy) + 1), M.Accent, (int)t % 6 < 2 ? 2 : 1);
            }
        }

        public override void Hub(double cy, double r)
        {
            var shape = Octagon(cy, r);
            C.Raise(shape, M.Hull, 4, 2.5f);
            C.Inset(shape, 2.2f);
            Ribs(shape, cy, r, 4);
            // A grey deck on top, its rim dashed yellow, rivets along it.
            var deck = Octagon(cy, r * 0.62);
            C.Raise(deck, M.Plate, 6, 1.5f);
            C.Inset(deck, 1.8f, 0.9f);
            Dashes(r * 0.62 * 0.414, cy - r * 0.62 + 1, r * 0.62 - 1, cy - r * 0.62 * 0.414);
            Dashes(r * 0.62 * 0.414, cy + r * 0.62 - 2, r * 0.62 - 1, cy + r * 0.62 * 0.414);
            Rivets(r * 0.62 - 2, cy - r * 0.3, r * 0.62 - 2, cy + r * 0.3, 3);
            // Lit ports round the rim.
            Y.LampRing(cy, r * 0.84, Math.Clamp((int)(r / 7), 4, 8), Math.PI / 8, M.Light);
        }

        public override void Ring(double cy, double r, double w)
        {
            var outer = Octagon(cy, r + w / 2);
            var ring = Canvas.Minus(outer, Octagon(cy, r - w / 2));
            C.Raise(ring, M.Hull, 4, 1.5f);
            C.Inset(Octagon(cy, r), 0.6f, 1f);
            for (var a = 0.0; a < Math.Tau; a += Math.PI / 12)
            {
                var (c, s) = (Math.Cos(a), Math.Sin(a));
                if (c < 0) continue;
                C.CarveLine(ring, c * (r - w / 2), cy + s * (r - w / 2), c * (r + w / 2), cy + s * (r + w / 2), 1.1f);
            }
            Y.LampRing(cy, r, 8, Math.PI / 8, M.Accent, true);
        }

        public override void Spar(double dx0, double y0, double dx1, double y1, double w)
        {
            var bar = C.Line(dx0, y0, dx1, y1, w);
            C.Raise(bar, M.Plate, 3, 1.5f);
            // A girder: its edges raised, cross braces between them.
            var len = Math.Sqrt((dx1 - dx0) * (dx1 - dx0) + (y1 - y0) * (y1 - y0));
            var (ux, uy) = ((dx1 - dx0) / Math.Max(len, 1e-6), (y1 - y0) / Math.Max(len, 1e-6));
            var (nx, ny) = (-uy * (w / 2 - 1), ux * (w / 2 - 1));
            for (var t = 0.0; t < len; t += w)
            {
                var (ax, ay) = (dx0 + ux * t, y0 + uy * t);
                var (bx, by) = (dx0 + ux * Math.Min(len, t + w), y0 + uy * Math.Min(len, t + w));
                C.CarveLine(bar, ax + nx, ay + ny, bx - nx, by - ny, 1f);
                C.CarveLine(bar, ax - nx, ay - ny, bx + nx, by + ny, 1f);
            }
            if (len > w * 3) Y.Lamp(dx0 + ux * len / 2, y0 + uy * len / 2, M.Accent, true);
        }

        public override void Module(double dx, double y, double hw, double hh)
        {
            var cut = Math.Min(hw, hh) * 0.45;
            var shape = C.Pair((dx - hw + cut, y - hh), (dx + hw - cut, y - hh), (dx + hw, y - hh + cut), (dx + hw, y + hh - cut),
                (dx + hw - cut, y + hh), (dx - hw + cut, y + hh), (dx - hw, y + hh - cut), (dx - hw, y - hh + cut));
            C.Raise(shape, M.Hull, 5, 2);
            C.Inset(shape, 2f);
            for (var x = dx - hw + 3; x < dx + hw - 2; x += 3) C.CarveLine(shape, x, y - hh + 2, x, y + hh - 2, 1f);
            Dashes(dx + hw - 1, y - hh + cut, dx + hw - 1, y + hh - cut);
            Y.Lamp(dx, y - hh + 2, M.Light, true);
        }

        public override void Housing(double cy, double r)
        {
            var hex = C.Body((r * 0.5, cy - r), (r, cy - r * 0.2), (r, cy + r * 0.55), (r * 0.6, cy + r));
            C.Raise(hex, M.Hull, 6, 1.5f);
            C.Inset(hex, 1.5f, 0.8f);
            for (var dx = 2.0; dx < r - 1; dx += 3) C.CarveLine(hex, dx, cy - r * 0.2, dx, cy + r * 0.6, 0.9f);
            C.Glow(C.Line(r - 0.5, cy - r * 0.1, r - 0.5, cy + r * 0.45), M.Accent, 1);
        }

        public override void Core(double cy, double r) => Y.K.Mount(0, cy, r);
    }

    /// <summary>Nairan: pointed gothic arches, green hull with panel lines following them, beige ridges, violet slots, a teal heart.</summary>
    sealed class NairanHand(Yard y) : Hand(y)
    {
        bool[] Vesica(double cy, double r) => C.Body((0.5, cy - r * 1.12), (r * 0.55, cy - r * 0.86), (r * 0.9, cy - r * 0.42), (r, cy),
            (r * 0.9, cy + r * 0.42), (r * 0.55, cy + r * 0.86), (0.5, cy + r * 1.12));

        void Slots(double dx0, double dx1, double y)
        {
            C.Carve(C.Rect(dx0 - 0.5, y - 1, dx1 + 0.5, y + 4), 1.2f);
            C.Glow(C.Rect(dx0, y, dx1, y + 3), M.Accent, 1);
            C.Glow(C.Rect(dx0, y, dx1, y + 1), M.Accent, 2);
        }

        public override void Hub(double cy, double r)
        {
            var shape = Vesica(cy, r);
            C.Raise(shape, M.Hull, 4, 4);
            C.Inset(shape, 2.5f);
            C.Inset(shape, 7f, 0.9f);
            // Four flying buttresses and a pale ridge in a cross, violet slots in pairs between them.
            foreach (var s in new[] { -1.0, 1.0 }) C.Raise(C.Stroke((r * 0.2, cy + s * r * 0.2), (r * 0.65, cy + s * r * 0.25), (r * 0.95, cy + s * r * 0.75), 4, 2), M.Hull, 6, 2);
            C.Raise(C.Rect(-1.5, cy - r * 1.05, 1.5, cy + r * 1.05), M.Trim, 7, 1.5f);
            C.Raise(C.Rect(0, cy - 1.5, r * 0.95, cy + 1.5), M.Trim, 7, 1.5f);
            for (var yy = cy - r * 0.62; yy < cy + r * 0.55; yy += 9)
            {
                if (Math.Abs(yy - cy) < 5) continue;
                Slots(r * 0.42, r * 0.66, yy);
            }
            Y.LampRing(cy, r * 0.86, Math.Clamp((int)(r / 7), 4, 8), Math.PI / 8, M.Light);
        }

        public override void Ring(double cy, double r, double w)
        {
            var ring = Annulus(C, cy, r, w);
            C.Raise(ring, M.Hull, 4, 2.5f);
            C.Inset(ring, (float)(w / 2 - 0.5), 0.8f);
            // Pointed arches along it: pale ridges every twelfth of a turn.
            for (var a = Math.PI / 12; a < Math.Tau; a += Math.PI / 6)
            {
                var (c, s) = (Math.Cos(a), Math.Sin(a));
                if (c < 0) continue;
                C.Lift(C.Line(c * (r - w / 2), cy + s * (r - w / 2), c * (r + w / 2), cy + s * (r + w / 2), 2), 1.6f);
                C.Dye(C.Line(c * (r - w / 2), cy + s * (r - w / 2), c * (r + w / 2), cy + s * (r + w / 2), 2), M.Trim);
            }
            Y.LampRing(cy, r, 8, Math.PI / 8, M.Accent, false);
        }

        public override void Spar(double dx0, double y0, double dx1, double y1, double w)
        {
            // An arched flying buttress: a curved band, its ridge pale, its edge lit violet.
            var (mx, my) = ((dx0 + dx1) / 2 + (y1 - y0) * 0.12, (y0 + y1) / 2 - (dx1 - dx0) * 0.12);
            C.Raise(C.Stroke((dx0, y0), (mx, my), (dx1, y1), w, w * 0.8), M.Hull, 4, 2.5f);
            C.Raise(C.Stroke((dx0, y0), (mx, my), (dx1, y1), 1.6, 1.4), M.Trim, 6, 1);
            var len = Math.Sqrt((dx1 - dx0) * (dx1 - dx0) + (y1 - y0) * (y1 - y0));
            if (len > w * 3)
            {
                var p = Canvas.Bezier((dx0, y0), (mx, my), (dx1, y1), 0.5);
                Y.Lamp(p.Dx, p.Y, M.Accent, false);
            }
        }

        public override void Module(double dx, double y, double hw, double hh)
        {
            var shape = C.Pair((dx, y - hh), (dx + hw, y - hh * 0.35), (dx + hw, y + hh - 2), (dx + hw - 2, y + hh), (dx - hw + 2, y + hh), (dx - hw, y + hh - 2), (dx - hw, y - hh * 0.35));
            C.Raise(shape, M.Hull, 5, 3);
            C.Inset(shape, 2.5f);
            C.Raise(C.Rect(dx - 0.5, y - hh * 0.7, dx + 0.5, y + hh - 3), M.Trim, 7, 1);
            C.Glow(C.Line(dx + hw, y - hh * 0.25, dx + hw, y + hh - 3), M.Accent, 2);
            Y.Lamp(dx - hw * 0.45, y + hh * 0.3, M.Light);
        }

        public override void Housing(double cy, double r)
        {
            var dome = C.Body((0.5, cy - r * 1.1), (r * 0.65, cy - r * 0.7), (r, cy), (r * 0.8, cy + r * 0.75), (0.5, cy + r));
            C.Raise(dome, M.Hull, 6, 2.5f);
            C.Inset(dome, 1.8f, 0.8f);
            C.Raise(C.Rect(-0.5, cy - r * 0.9, 0.5, cy + r * 0.8), M.Trim, 8, 1);
            C.Glow(C.Rect(r * 0.4, cy + r * 0.05, r * 0.7, cy + r * 0.4), M.Accent, 2);
        }

        public override void Core(double cy, double r)
        {
            C.Raise(C.Oval(0, cy, r, r), M.Trim, 8, 2);
            C.Carve(C.Oval(0, cy, r * 0.72, r * 0.72), 1.4f);
            C.Glow(C.Oval(0, cy, r * 0.55, r * 0.55), M.Light, 1);
            C.Glow(C.Oval(0, cy, r * 0.25, r * 0.25), M.Light, 2);
        }
    }

    /// <summary>Nautolan: stone grown into lobes and etched with mazes, flesh-colored ridges, green crescents, blue chevrons.</summary>
    sealed class NautolanHand(Yard y) : Hand(y)
    {
        void Maze(bool[] shape)
        {
            for (var yy = 2; yy < C.H; yy += 4)
            {
                for (var x = 2; x < C.Cx; x += 4)
                {
                    var dx = C.Cx - x;
                    switch (Y.Rand.Next(3))
                    {
                        case 0: C.CarveLine(shape, dx, yy, dx - 3, yy, 0.9f); break;
                        case 1: C.CarveLine(shape, dx, yy, dx, yy + 3, 0.9f); break;
                        default: C.CarveLine(shape, dx, yy, dx - 2, yy + 2, 0.9f); break;
                    }
                }
            }
        }

        void Crescent(double dx, double y, double r, double tilt)
        {
            for (var t = -0.9; t <= 0.9; t += 0.06)
            {
                var a = tilt + t;
                C.Glow(C.Rect(Math.Round(dx + Math.Cos(a) * r) - 0.5, Math.Floor(y + Math.Sin(a) * r), Math.Round(dx + Math.Cos(a) * r) + 0.5, Math.Floor(y + Math.Sin(a) * r) + 1), M.Accent, Math.Abs(t) < 0.4 ? 2 : 1);
            }
        }

        public override void Hub(double cy, double r)
        {
            // A body grown of five lobes, as a sand dollar.
            var shape = C.Oval(0, cy, r * 0.82, r * 0.82);
            foreach (var a in new[] { -Math.PI / 2, -Math.PI / 10, 3 * Math.PI / 10 })
                shape = Canvas.Or(shape, C.Oval(Math.Abs(Math.Cos(a)) * r * 0.5, cy + Math.Sin(a) * r * 0.5, r * 0.5, r * 0.5));
            C.Raise(shape, M.Hull, 4, 5);
            C.Inset(shape, 2.5f);
            Maze(shape);
            // Flesh ridges out to the lobes, green crescents between them, blue chevrons at the heart.
            foreach (var a in new[] { -Math.PI / 2, -Math.PI / 10, 3 * Math.PI / 10 })
            {
                var (c, s) = (Math.Abs(Math.Cos(a)), Math.Sin(a));
                C.Raise(C.Stroke((c * r * 0.2, cy + s * r * 0.2), (c * r * 0.55, cy + s * r * 0.5 + 2), (c * r * 0.88, cy + s * r * 0.86), 3, 1.5), M.Trim, 7, 1.5f);
            }
            foreach (var a in new[] { -3 * Math.PI / 10, Math.PI / 10, Math.PI / 2 }) Crescent(Math.Abs(Math.Cos(a)) * r * 0.66, cy + Math.Sin(a) * r * 0.66, r * 0.2, a + Math.PI);
            for (var i = 0; i < 3; i++) C.Glow(C.Line(r * 0.22 - i * 2, cy - r * 0.28 + i * 3, 0, cy - r * 0.34 + i * 3, 1.2), M.Glass, i == 0 ? 2 : 1);
            Y.LampRing(cy, r * 0.72, Math.Clamp((int)(r / 7), 4, 8), 0.3, M.Glass);
        }

        public override void Ring(double cy, double r, double w)
        {
            // A broken ring: four crescents of stone, their ridges flesh, lit at their tips.
            foreach (var (a0, a1) in new[] { (-Math.PI / 2 + 0.25, -0.25), (0.25, Math.PI / 2 - 0.25) })
            {
                var am = (a0 + a1) / 2;
                var p0 = (Math.Cos(a0) * r, cy + Math.Sin(a0) * r);
                var p1 = (Math.Cos(a1) * r, cy + Math.Sin(a1) * r);
                var pm = (Math.Cos(am) * r * 1.42, cy + Math.Sin(am) * r * 1.42);
                var band = C.Stroke(p0, pm, p1, w * 0.5, w * 0.5);
                var fat = C.Stroke(p0, (Math.Cos(am) * r * 1.35, cy + Math.Sin(am) * r * 1.35), p1, w * 0.4, w * 0.4);
                C.Raise(Canvas.Or(band, fat), M.Hull, 4, 3);
                Maze(band);
                C.Raise(C.Stroke(p0, pm, p1, 1.5, 1.5), M.Trim, 6, 1);
                Y.Lamp(p0.Item1, p0.Item2, M.Accent, true);
                Y.Lamp(p1.Item1, p1.Item2, M.Accent, true);
            }
        }

        public override void Spar(double dx0, double y0, double dx1, double y1, double w)
        {
            // A tendril, thicker at its root, curling a little.
            var (mx, my) = ((dx0 + dx1) / 2 - (y1 - y0) * 0.08, (y0 + y1) / 2 + (dx1 - dx0) * 0.08);
            var band = C.Stroke((dx0, y0), (mx, my), (dx1, y1), w * 1.15, w * 0.65);
            C.Raise(band, M.Hull, 4, 3);
            Maze(band);
            C.Raise(C.Stroke((dx0, y0), (mx, my), (dx1, y1), 1.6, 1), M.Trim, 6, 1);
        }

        public override void Module(double dx, double y, double hw, double hh)
        {
            var shape = C.Pair((dx, y - hh), (dx + hw, y - hh * 0.1), (dx + hw * 0.55, y + hh), (dx - hw * 0.55, y + hh), (dx - hw, y - hh * 0.1));
            C.Raise(shape, M.Hull, 5, 3);
            C.Inset(shape, 2f);
            Maze(shape);
            C.Raise(C.Line(dx, y - hh + 2, dx, y + hh - 2, 1.5), M.Trim, 7, 1);
            Crescent(dx, y + hh * 0.1, Math.Min(hw, hh) * 0.55, Math.PI / 2);
        }

        public override void Housing(double cy, double r)
        {
            var body = Canvas.Or(C.Oval(0, cy + r * 0.1, r * 0.9, r * 0.85), C.Oval(0, cy - r * 0.45, r * 0.55, r * 0.6));
            C.Raise(body, M.Hull, 6, 2.5f);
            C.Inset(body, 1.6f, 0.8f);
            C.Raise(C.Line(0, cy - r * 0.9, 0, cy + r * 0.8, 1.5), M.Trim, 8, 1);
            Crescent(0, cy + r * 0.15, r * 0.6, Math.PI / 2);
        }

        public override void Core(double cy, double r)
        {
            C.Raise(C.Oval(0, cy, r, r * 0.9), M.Trim, 8, 2);
            C.Carve(C.Oval(0, cy, r * 0.7, r * 0.62), 1.4f);
            C.Glow(C.Oval(0, cy, r * 0.5, r * 0.42), M.Glass, 1);
            C.Glow(C.Oval(0, cy - r * 0.1, r * 0.22, r * 0.18), M.Light, 2);
            Crescent(0, cy, r * 0.85, Math.PI / 2);
        }
    }

    /// <summary>Errants: bronze terraces as old temples, every rim gilded, machinery green with patina, turquoise channels, a violet eye.</summary>
    sealed class ErrantHand(Yard y) : Hand(y)
    {
        void Rim(bool[] shape, int depth = 1)
        {
            var d = C.Distance(shape);
            var band = new bool[shape.Length];
            for (var i = 0; i < band.Length; i++) band[i] = shape[i] && d[i] <= depth + 0.01f;
            C.Dye(band, M.Trim);
            C.Lift(band, 0.8f);
        }

        public override void Hub(double cy, double r)
        {
            // Three terraces, each higher than the one around it, each with its gold rim.
            foreach (var (k, m, h) in new[] { (1.0, M.Hull, 3f), (0.76, M.Hull, 5f), (0.52, M.Plate, 7f) })
            {
                var t = C.Oval(0, cy, r * k, r * k);
                C.Raise(t, m, h, 1.5f);
                Rim(t, k > 0.9 ? 2 : 1);
            }
            // Turquoise channels in a cross across the lower terraces, steps cut into them.
            C.Carve(C.Rect(-1.5, cy - r * 0.98, 1.5, cy + r * 0.98), 1.6f);
            C.Glow(C.Rect(-0.5, cy - r * 0.95, 0.5, cy + r * 0.95), M.Accent, 1);
            C.Carve(C.Rect(0, cy - 1.5, r * 0.98, cy + 1.5), 1.6f);
            C.Glow(C.Rect(0, cy - 0.5, r * 0.95, cy + 0.5), M.Accent, 1);
            for (var a = Math.PI / 8; a < Math.Tau; a += Math.PI / 4)
            {
                var (c, s) = (Math.Cos(a), Math.Sin(a));
                if (c < 0) continue;
                C.CarveLine(C.Oval(0, cy, r, r), c * r * 0.78, cy + s * r * 0.78, c * r * 0.98, cy + s * r * 0.98, 1.2f);
            }
            Y.LampRing(cy, r * 0.88, Math.Clamp((int)(r / 7), 4, 8), Math.PI / 8, M.Accent);
        }

        public override void Ring(double cy, double r, double w)
        {
            var ring = Annulus(C, cy, r, w);
            C.Raise(ring, M.Hull, 4, 1.5f);
            Rim(ring);
            for (var a = 0.0; a < Math.Tau; a += Math.PI / 10)
            {
                var (c, s) = (Math.Cos(a), Math.Sin(a));
                if (c < 0) continue;
                C.CarveLine(ring, c * (r - w / 2), cy + s * (r - w / 2), c * (r + w / 2), cy + s * (r + w / 2), 1.1f);
            }
            Y.LampRing(cy, r, 8, Math.PI / 8, M.Accent, true);
        }

        public override void Spar(double dx0, double y0, double dx1, double y1, double w)
        {
            var bar = C.Line(dx0, y0, dx1, y1, w);
            C.Raise(bar, M.Hull, 3, 1.5f);
            Rim(bar);
            C.Glow(C.Line(dx0, y0, dx1, y1, 1), M.Accent, 1);
        }

        public override void Module(double dx, double y, double hw, double hh)
        {
            var outer = C.Rect(dx - hw, y - hh, dx + hw, y + hh);
            C.Raise(outer, M.Hull, 4, 1.5f);
            Rim(outer);
            var inner = C.Rect(dx - hw * 0.62, y - hh * 0.62, dx + hw * 0.62, y + hh * 0.62);
            C.Raise(inner, M.Plate, 6, 1.5f);
            Rim(inner);
            Y.Lamp(dx, y, M.Accent);
        }

        public override void Housing(double cy, double r)
        {
            var body = C.Oval(0, cy, r, r);
            C.Raise(body, M.Hull, 6, 1.5f);
            Rim(body);
            C.Raise(C.Oval(0, cy, r * 0.6, r * 0.6), M.Plate, 8, 1.5f);
            C.Glow(C.Rect(-0.5, cy - r * 0.55, 0.5, cy + r * 0.55), M.Accent, 1);
        }

        public override void Core(double cy, double r)
        {
            C.Raise(C.Oval(0, cy, r, r), M.Trim, 9, 1.5f);
            C.Carve(C.Oval(0, cy, r * 0.72, r * 0.72), 1.4f);
            C.Glow(C.Oval(0, cy, r * 0.55, r * 0.55), M.Core, 1);
            C.Glow(C.Oval(-r * 0.12, cy - r * 0.15, r * 0.18, r * 0.18), M.Core, 2);
        }
    }

    // The station, level by level: the same plan for every race, each building it its own way.

    /// <summary>
    /// A station of `level`: its hub, then (level 2) a ring through its turrets with docks on its
    /// flanks, (level 3) spars up and down to its heavy turrets, (level 4) the same to the sides.
    /// Every part's socket lies where the station's data puts it, for the parts drawn over them.
    /// </summary>
    public static Built Station(Faction f, int level)
    {
        var halfW = level >= 4 ? 122 : 90;
        var halfH = level >= 3 ? 122 : 90;
        var y = new Yard(halfW, halfH, f, 100 + level);
        var (c, h, k) = (y.C, y.H, y.K);
        var cy = y.Cy;
        const double hub = 50, ring = 68.5;
        // Arms first, the rest laid over them: up to the shipyard's dock, down to the hangar's.
        foreach (var s in new[] { -1.0, 1.0 }) h.Spar(0, cy + s * (hub - 6), 0, cy + s * (P(100) - 6), 14);
        if (level >= 3) foreach (var s in new[] { -1.0, 1.0 }) h.Spar(0, cy + s * P(100), 0, cy + s * P(150), 12);
        if (level >= 4) h.Spar(hub - 6, cy, P(150), cy, 12);
        // Out to the bastions that carry the first turrets.
        foreach (var s in new[] { -1.0, 1.0 }) h.Spar(hub * 0.62, cy + s * hub * 0.62, P(75), cy + s * P(70), 9);
        if (level >= 3) h.Spar(P(75), cy - P(70), P(108), cy - P(108), 4);
        if (level >= 4) h.Spar(P(75), cy + P(70), P(108), cy + P(108), 4);
        if (level >= 2) h.Ring(cy, ring, 10);
        // Solar arrays beside the docks, and from level 3 beside the heavy bastions.
        foreach (var s in new[] { -1.0, 1.0 }) h.Panel(31, cy + s * P(100), 11, 7);
        if (level >= 3) foreach (var s in new[] { -1.0, 1.0 }) h.Panel(32, cy + s * P(150), 12, 7);
        if (level >= 4) foreach (var s in new[] { -1.0, 1.0 }) h.Panel(P(150), cy + s * 32, 7, 12);
        // Bastions and docks: the shipyard's above, the hangar's below, on the flanks from level 2.
        foreach (var s in new[] { -1.0, 1.0 }) h.Module(P(75), cy + s * P(70), 13, 13);
        foreach (var s in new[] { -1.0, 1.0 }) h.Module(0, cy + s * P(100), 22, 13);
        if (level >= 2) h.Module(P(105), cy, 13, 18);
        if (level >= 3) foreach (var s in new[] { -1.0, 1.0 }) h.Module(0, cy + s * P(150), 19, 16);
        if (level >= 4) h.Module(P(150), cy, 16, 19);
        h.Hub(cy, hub);
        if (level >= 3) h.Dish(P(108), cy - P(108), 9);
        if (level >= 4) h.Dish(P(108), cy + P(108), 9);
        h.Core(cy, 9 + level * 1.5);
        // The sockets of every part the station has at this level.
        for (var l = 0; l < level; l++)
        {
            foreach (var slot in Sim.Data.Station.Levels[l].Hardpoints)
                k.Base(slot.Role, P(Math.Abs(slot.X)), cy + P(slot.Y), P(Hardpoints.Defs[slot.Role].Radius) * 0.95);
        }
        return y.Paint();
    }

    // The parts: a collar in the race's hand, and over it what the part does.

    /// <summary>A part of a station or a ship, as designed for its role's radius, pointing up.</summary>
    public static Built Part(Faction f, HardpointRole role, Gun gun)
    {
        var r = P(Hardpoints.Defs[role].Radius);
        var armed = role is HardpointRole.Turret or HardpointRole.HeavyTurret;
        var halfW = (int)Math.Ceiling(r * 1.15) + 1;
        var halfH = armed ? (int)Math.Ceiling(r * 1.75) + 1 : halfW;
        var y = new Yard(halfW, halfH, f, 7 + (int)role * 13 + (int)gun);
        var (c, h, k) = (y.C, y.H, y.K);
        var cy = y.Cy;
        switch (role)
        {
            case HardpointRole.Turret or HardpointRole.HeavyTurret:
                k.Base(HardpointRole.Turret, 0, cy, r);
                if (role == HardpointRole.HeavyTurret) c.Inset(c.Oval(0, cy, r, r), 2.5f, 1f);
                Weapon(y, gun, cy, r * 0.72, role == HardpointRole.HeavyTurret);
                break;
            case HardpointRole.Hangar:
                k.Base(HardpointRole.Turret, 0, cy, r);
                var bay = c.Rect(-r * 0.62, cy - r * 0.55, r * 0.62, cy + r * 0.7);
                c.Raise(bay, M.Plate, 7, 1.5f);
                c.Carve(c.Rect(-r * 0.45, cy - r * 0.35, r * 0.45, cy + r * 0.62), 3f);
                c.Glow(c.Rect(-r * 0.4, cy + r * 0.5, r * 0.4, cy + r * 0.62), M.Light, 1);
                y.Lamp(r * 0.35, cy - r * 0.45, M.Light, true);
                break;
            case HardpointRole.Shield:
                k.Base(HardpointRole.Turret, 0, cy, r);
                c.Raise(c.Oval(0, cy, r * 0.62, r * 0.62), M.Plate, 7, 1.5f);
                c.Glow(c.Oval(0, cy, r * 0.48, r * 0.48), M.Glass, 1);
                c.Glow(c.Oval(-r * 0.1, cy - r * 0.12, r * 0.22, r * 0.22), M.Glass, 2);
                y.LampRing(cy, r * 0.8, 6, Math.PI / 6, M.Glass);
                break;
            case HardpointRole.Shipyard:
                // A gantry: four girders round an open berth, a crane across it, warning lights at its corners.
                k.Base(HardpointRole.Turret, 0, cy, r);
                c.Carve(c.Rect(-r * 0.55, cy - r * 0.55, r * 0.55, cy + r * 0.55), 2.5f);
                foreach (var s in new[] { -1.0, 1.0 }) h.Spar(r * 0.7, cy + s * r * 0.7, -r * 0.7, cy + s * r * 0.7, 3);
                h.Spar(r * 0.7, cy - r * 0.7, r * 0.7, cy + r * 0.7, 3);
                c.Raise(c.Rect(-r * 0.7, cy - 1, r * 0.7, cy + 1), M.Trim, 8, 1);
                y.Lamp(r * 0.7, cy - r * 0.7, M.Accent, true);
                y.Lamp(r * 0.7, cy + r * 0.7, M.Accent, true);
                break;
            default:
                // An engine: a block of plate, its nozzle lit.
                c.Raise(c.Rect(-r * 0.75, cy - r, r * 0.75, cy + r * 0.6), M.Plate, 5, 1.5f);
                c.Glow(c.Oval(0, cy + r * 0.55, r * 0.55, r * 0.35), M.Engine, 1);
                c.Glow(c.Oval(0, cy + r * 0.6, r * 0.25, r * 0.18), M.Engine, 2);
                break;
        }
        return y.Paint();
    }

    /// <summary>A gun's housing and barrels, pointing up from (0, cy), `r` round; `heavy` for the heavy turrets' bigger guns.</summary>
    static void Weapon(Yard y, Gun gun, double cy, double r, bool heavy)
    {
        var (c, h) = (y.C, y.H);
        var hr = r * 0.78;
        void Barrel(double dx, double from, double to, double w, M tip = M.Light)
        {
            c.Raise(c.Rect(dx - w / 2, cy - to, dx + w / 2, cy - from), M.Plate, 9, 1);
            if (w >= 2) c.Carve(c.Rect(dx - 0.5, cy - to, dx + 0.5, cy - to + 1), 1.5f);
            c.Glow(c.Rect(dx - w / 2, cy - to, dx + w / 2, cy - to + 1), tip, 1);
        }
        switch (gun)
        {
            case Gun.Laser or Gun.HeavyLaser:
                h.Housing(cy, hr);
                var w = gun == Gun.HeavyLaser || heavy ? 3 : 2;
                Barrel(r * 0.38, r * 0.3, r * 2.3, w);
                if (gun == Gun.HeavyLaser) for (var t = r * 1.1; t < r * 2.1; t += 4) c.Glow(c.Rect(r * 0.38 - w / 2.0, cy - t, r * 0.38 + w / 2.0, cy - t + 1), M.Accent, 1);
                break;
            case Gun.Ray:
                h.Housing(cy, hr * 1.05);
                c.Raise(c.Rect(-2.5, cy - r * 2.35, 2.5, cy - r * 0.3), M.Plate, 10, 1.2f);
                for (var t = r * 1.05; t < r * 2.05; t += 3) c.Raise(c.Rect(-3.5, cy - t - 1, 3.5, cy - t), M.Trim, 11, 0.8f);
                c.Glow(c.Rect(-1.5, cy - r * 2.35, 1.5, cy - r * 2.35 + 2), M.Core, 2);
                c.Glow(c.Rect(-0.5, cy - r * 2.2, 0.5, cy - r * 0.5), M.Core, 1);
                break;
            case Gun.Flak:
                h.Housing(cy, hr * 0.9);
                foreach (var dx in new[] { r * 0.2, r * 0.55 }) Barrel(dx, r * 0.3, r * 1.75, 1, M.Accent);
                c.Raise(c.Rect(-r * 0.6, cy + r * 0.25, r * 0.6, cy + r * 0.7), M.Trim, 7, 1);
                break;
            case Gun.Beam:
                // A focusing fork: two prongs round a crystal lit from within.
                h.Housing(cy, hr);
                foreach (var t in new[] { 0.0 }) c.Raise(c.Stroke((r * 0.5, cy - r * 0.2 - t), (r * 0.7, cy - r * 1.25), (r * 0.3, cy - r * 2.1), 2.5, 1.5), M.Plate, 9, 1);
                c.Glow(c.Oval(0, cy - r * 1.35, r * 0.26, r * 0.4), M.Glass, 1);
                c.Glow(c.Oval(0, cy - r * 1.4, r * 0.11, r * 0.18), M.Light, 2);
                break;
            case Gun.Burst:
                // A rotary cannon: barrels in a cluster round a drum.
                h.Housing(cy, hr);
                c.Raise(c.Oval(0, cy - r * 0.45, r * 0.45, r * 0.4), M.Plate, 9, 1.2f);
                foreach (var dx in new[] { 0.0, 1.5 }) Barrel(dx, r * 0.7, r * 2.2, 1, M.Accent);
                c.Glow(c.Rect(-r * 0.45, cy - r * 0.48, r * 0.45, cy - r * 0.42), M.Accent, 1);
                break;
            case Gun.Swarm:
                // A missile pod: a block of tubes, their mouths ringed with the race's light.
                h.Housing(cy, hr * 0.9);
                var pod = c.Rect(-r * 0.8, cy - r * 1.6, r * 0.8, cy + r * 0.05);
                c.Raise(pod, M.Plate, 9, 1.2f);
                for (var row = 0; row < 3; row++)
                {
                    foreach (var dx in new[] { r * 0.0, r * 0.45 })
                    {
                        var yy = cy - r * 1.4 + row * r * 0.48;
                        c.Carve(c.Rect(dx - 1, yy, dx + 1, yy + 2), 2f);
                        c.Glow(c.Rect(dx - 0.5, yy, dx + 0.5, yy + 1), M.Accent, row == 0 ? 2 : 1);
                    }
                }
                break;
            case Gun.Ion:
                // A ring projector held out on two arms, its eye blue-white.
                h.Housing(cy, hr);
                c.Raise(c.Line(r * 0.45, cy - r * 0.3, r * 0.5, cy - r * 1.35, 2), M.Plate, 9, 1);
                c.Raise(Annulus(c, cy - r * 1.7, r * 0.55, 2.2), M.Trim, 10, 1);
                c.Glow(c.Oval(0, cy - r * 1.7, r * 0.36, r * 0.36), M.Glass, 1);
                c.Glow(c.Oval(0, cy - r * 1.7, r * 0.15, r * 0.15), M.Light, 2);
                break;
            default:
                // A torpedo tube, wide and short, its mouth dark, lit round its lip.
                h.Housing(cy, hr);
                c.Raise(c.Rect(-r * 0.42, cy - r * 1.9, r * 0.42, cy - r * 0.2), M.Plate, 9, 1.2f);
                c.Carve(c.Rect(-r * 0.26, cy - r * 1.9, r * 0.26, cy - r * 1.55), 2.5f);
                c.Glow(c.Rect(-r * 0.42, cy - r * 1.9, r * 0.42, cy - r * 1.9 + 1), M.Accent, 1);
                break;
        }
    }

    // The buildings: a base that stays, and for those that fight or dig, a part that turns over it.

    /// <summary>A building of `type` at `level`, as designed for its type's radius.</summary>
    public static Built Building(Faction f, BuildableType type, int level)
    {
        var r = P(Buildables.Defs[type].Radius);
        var half = (int)Math.Ceiling(r * 1.25) + 2;
        var y = new Yard(half, half, f, 300 + (int)type * 7 + level);
        var (c, h, k) = (y.C, y.H, y.K);
        var cy = y.Cy;
        switch (type)
        {
            case BuildableType.Platform:
                // A gun deck on legs; armored and wider at each level, its mounts more numerous.
                foreach (var a in new[] { -Math.PI / 2, Math.PI / 6 })
                    h.Spar(Math.Abs(Math.Cos(a)) * r * 0.4, cy + Math.Sin(a) * r * 0.4, Math.Abs(Math.Cos(a)) * r * 1.1, cy + Math.Sin(a) * r * 1.1, level >= 2 ? 4 : 3);
                foreach (var a in new[] { -Math.PI / 2, Math.PI / 6 })
                    h.Module(Math.Abs(Math.Cos(a)) * r * 1.05, cy + Math.Sin(a) * r * 1.05, 3 + level * 0.5, 3 + level * 0.5);
                h.Hub(cy, r * (0.62 + level * 0.06));
                if (level >= 3) foreach (var s in new[] { -1.0, 1.0 }) k.Mount(r * 0.55, cy + s * r * 0.3, 3);
                break;
            case BuildableType.Mine:
                // A drilling rig clamped on its rock: three legs, a hopper, a lit head where the drill turns.
                foreach (var a in new[] { -Math.PI / 2, Math.PI / 6 })
                    h.Spar(Math.Abs(Math.Cos(a)) * r * 0.25, cy + Math.Sin(a) * r * 0.25, Math.Abs(Math.Cos(a)) * r * 0.95, cy + Math.Sin(a) * r * 0.95, 3);
                foreach (var a in new[] { -Math.PI / 2, Math.PI / 6 })
                    c.Raise(c.Oval(Math.Abs(Math.Cos(a)) * r * 0.95, cy + Math.Sin(a) * r * 0.95, 2.5, 2.5), M.Plate, 6, 1);
                h.Module(r * 0.42, cy + r * 0.38, r * 0.2, r * 0.16);
                c.Raise(c.Oval(0, cy, r * 0.42, r * 0.42), M.Plate, 7, 1.5f);
                c.Carve(c.Oval(0, cy, r * 0.3, r * 0.3), 2f);
                y.Lamp(r * 0.42, cy + r * 0.38, M.Accent, true);
                break;
            case BuildableType.Outpost:
                // A small station: a hub, docks either side, an aerial; a ring from level 2, solar wings at 3.
                if (level >= 2) h.Ring(cy, r * 0.95, 4);
                h.Spar(r * 0.9, cy, 0, cy, 4);
                h.Module(r * 0.9, cy, r * 0.22, r * 0.3);
                if (level >= 3) foreach (var s in new[] { -1.0, 1.0 }) h.Panel(r * 0.62, cy + s * r * 0.82, r * 0.25, r * 0.14);
                h.Hub(cy, r * 0.62);
                h.Dish(0, cy - r * 0.72, r * 0.22);
                break;
            case BuildableType.RepairDock:
                // A cradle: two arms round an open berth, mending arms lit green, a crane across.
                h.Spar(r * 0.8, cy - r * 0.85, r * 0.8, cy + r * 0.7, 8);
                h.Spar(r * 0.8, cy + r * 0.75, 0, cy + r * 0.75, 8);
                foreach (var t0 in new[] { -0.3, 0.25 })
                {
                    c.Raise(c.Line(r * 0.72, cy + r * t0, r * 0.38, cy + r * (t0 + 0.12), 2), M.Plate, 6, 1);
                    c.Glow(c.Rect(r * 0.36 - 0.5, cy + r * (t0 + 0.12), r * 0.36 + 0.5, cy + r * (t0 + 0.12) + 1), M.Light, 2);
                }
                h.Module(r * 0.8, cy - r * 0.85, r * 0.2, r * 0.18);
                h.Module(0, cy + r * 0.82, r * 0.3, r * 0.18);
                c.Raise(c.Rect(-r * 0.8, cy - r * 0.62, r * 0.8, cy - r * 0.5), M.Trim, 7, 1);
                y.Lamp(r * 0.36, cy + r * -0.18, M.Light, true);
                y.Lamp(r * 0.36, cy + r * 0.37, M.Light, true);
                y.Lamp(0, cy - r * 0.56, M.Accent, false);
                break;
            case BuildableType.IonCannon:
                // A heavy foundation, four capacitors humming at its corners.
                foreach (var s in new[] { -1.0, 1.0 })
                {
                    c.Raise(c.Oval(r * 0.72, cy + s * r * 0.72, r * 0.24, r * 0.24), M.Plate, 6, 1.5f);
                    c.Glow(c.Oval(r * 0.72, cy + s * r * 0.72, r * 0.11, r * 0.11), M.Glass, 1);
                    y.Lamp(r * 0.72, cy + s * r * 0.72, M.Glass, false);
                }
                h.Hub(cy, r * 0.78);
                break;
            default:
                // The lab: a hub under a glass dome, modules either side, a dish.
                h.Spar(r * 0.95, cy, 0, cy, 4);
                h.Module(r * 0.95, cy, r * 0.2, r * 0.32);
                h.Hub(cy, r * 0.66);
                c.Raise(c.Oval(0, cy, r * 0.36, r * 0.36), M.Plate, 8, 1.5f);
                c.Glow(c.Oval(0, cy, r * 0.28, r * 0.28), M.Glass, 1);
                c.Glow(c.Oval(-r * 0.08, cy - r * 0.1, r * 0.1, r * 0.1), M.Glass, 2);
                h.Dish(r * 0.42, cy - r * 0.48, r * 0.2);
                break;
        }
        return y.Paint();
    }

    /// <summary>
    /// What turns over a building: its gun, aimed where it shoots, or the mine's drill head,
    /// spinning; null for the buildings with neither.
    /// </summary>
    public static Built? BuildingGun(Faction f, BuildableType type, int level)
    {
        var gun = (type, level) switch
        {
            (BuildableType.Platform, 1) => Gun.Laser,
            (BuildableType.Platform, 2) => Gun.HeavyLaser,
            (BuildableType.Platform, _) => Gun.Ray,
            (BuildableType.Outpost, 1) => Gun.Flak,
            (BuildableType.Outpost, 2) => Gun.Laser,
            (BuildableType.Outpost, _) => Gun.HeavyLaser,
            (BuildableType.IonCannon, _) => Gun.Ion,
            (BuildableType.Mine, _) => (Gun?)null,
            _ => (Gun?)null,
        };
        var r = P(Buildables.Defs[type].Radius);
        if (type == BuildableType.Mine)
        {
            // The drill head: a toothed wheel round a hub, lit at its middle.
            var y = new Yard((int)Math.Ceiling(r * 0.42) + 1, (int)Math.Ceiling(r * 0.42) + 1, f, 900 + level);
            var (c, cy) = (y.C, y.Cy);
            var rr = r * 0.4;
            c.Raise(c.Oval(0, cy, rr * 0.8, rr * 0.8), M.Plate, 6, 1.5f);
            for (var a = 0.0; a < Math.Tau; a += Math.Tau / 6)
            {
                if (Math.Cos(a) < -1e-6) continue;
                c.Raise(c.Line(Math.Cos(a) * rr * 0.5, cy + Math.Sin(a) * rr * 0.5, Math.Cos(a + 0.4) * rr, cy + Math.Sin(a + 0.4) * rr, 2), M.Trim, 8, 1);
            }
            c.Glow(c.Oval(0, cy, rr * 0.3, rr * 0.3), M.Accent, 2);
            return y.Paint();
        }
        if (gun is not Gun g) return null;
        var size = type == BuildableType.IonCannon ? r * 0.75 : r * (0.45 + level * 0.06);
        var yard = new Yard((int)Math.Ceiling(size * 1.1) + 1, (int)Math.Ceiling(size * 2.2) + 1, f, 950 + (int)type * 5 + level);
        Weapon(yard, g, yard.Cy, size, type == BuildableType.IonCannon || level >= 3);
        if (type == BuildableType.Platform && level >= 3) yard.C.Dye(yard.C.Rect(-0.5, yard.Cy - size * 0.2, 0.5, yard.Cy + size * 0.5), M.Trim);
        return yard.Paint();
    }
}
