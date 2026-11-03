// Original sprites for the ship classes the packs lack (bulwark, carrier, titan, leviathan,
// colossus), and for every class of the neutral Errants, who have no pack, drawn from scratch in
// the packs' manner. Each ship is sculpted as a height map (bevelled plates, carved grooves, inset
// panels that follow the shape, raised mounts and rivets), lit from the upper left, and the light
// is brought down to the packs' own color ramps, four or five tones per material; trims, slots,
// lights and engines glow on their own. A black outline closes it, as in the packs. Nothing is
// taken from the pack sprites but their colors.
using Godot;
using SandboxShip.Sim.Data;

namespace SandboxShip.Render;

public static partial class ShipForge
{
    /// <summary>What a pixel is made of. Trim is a hull's second color; the last four glow.</summary>
    enum M : byte { None, Hull, Trim, Plate, Accent, Light, Glass, Engine, Core }

    static bool Glows(M m) => m >= M.Accent;

    /// <summary>A sculpture, symmetric about its middle column: every shape is drawn on both sides.</summary>
    sealed class Canvas(int w, int h)
    {
        public readonly int W = w, H = h;
        public readonly M[] Mat = new M[w * h];
        /// <summary>Height for lit materials; brightness (0 to 2) for glowing ones.</summary>
        public readonly float[] Z = new float[w * h];
        public double Cx => (W - 1) / 2.0;
        /// <summary>Engine nozzles (offset from the middle, y), each on both sides: where flames go.</summary>
        public readonly List<(double Dx, double Y, double Size)> Nozzles = [];

        public M At(int x, int y) => x < 0 || y < 0 || x >= W || y >= H ? M.None : Mat[y * W + x];
        public float Height(int x, int y) => At(x, y) == M.None ? -2 : Z[y * W + x];

        // Masks: which pixels a shape covers, with its mirror image.

        bool[] Empty() => new bool[W * H];

        void Fill(bool[] mask, (double X, double Y)[] p)
        {
            var (x0, x1) = ((int)Math.Floor(p.Min(q => q.X)), (int)Math.Ceiling(p.Max(q => q.X)));
            var (y0, y1) = ((int)Math.Floor(p.Min(q => q.Y)), (int)Math.Ceiling(p.Max(q => q.Y)));
            for (var y = Math.Max(0, y0); y <= Math.Min(H - 1, y1); y++)
            {
                for (var x = Math.Max(0, x0); x <= Math.Min(W - 1, x1); x++)
                {
                    var (px, py) = (x + 0.5, y + 0.5);
                    var c = false;
                    for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
                    {
                        if ((p[i].Y > py) != (p[j].Y > py) && px < (p[j].X - p[i].X) * (py - p[i].Y) / (p[j].Y - p[i].Y) + p[i].X) c = !c;
                    }
                    if (c) mask[y * W + x] = true;
                }
            }
        }

        /// <summary>
        /// The middle in polygon coordinates, where pixel x spans x to x + 1: the center of pixel
        /// Cx, so that polygons share their axis with ovals and lines.
        /// </summary>
        double Mid => Cx + 0.5;

        /// <summary>The shape whose left side runs through `left` (offsets from the middle, nose to stern), closed by its mirror.</summary>
        public bool[] Body(params (double Dx, double Y)[] left)
        {
            var m = Empty();
            Fill(m, [.. left.Select(p => (Mid - p.Dx, p.Y)), .. left.Reverse().Select(p => (Mid + p.Dx, p.Y))]);
            return m;
        }

        /// <summary>A polygon on the left, and its mirror on the right.</summary>
        public bool[] Pair(params (double Dx, double Y)[] pts)
        {
            var m = Empty();
            Fill(m, [.. pts.Select(p => (Mid - p.Dx, p.Y))]);
            Fill(m, [.. pts.Select(p => (Mid + p.Dx, p.Y))]);
            return m;
        }

        /// <summary>
        /// A band along a curve (a quadratic Bézier from `a` through the pull of `b` to `c`), `w0`
        /// wide at its start and `w1` at its end, and its mirror: horns, crescents, blades.
        /// </summary>
        public bool[] Stroke((double Dx, double Y) a, (double Dx, double Y) b, (double Dx, double Y) c, double w0, double w1)
        {
            const int n = 32;
            var (left, right) = (new List<(double, double)>(), new List<(double, double)>());
            for (var i = 0; i <= n; i++)
            {
                var t = (double)i / n;
                var p = Bezier(a, b, c, t);
                var (tx, ty) = (2 * (1 - t) * (b.Dx - a.Dx) + 2 * t * (c.Dx - b.Dx), 2 * (1 - t) * (b.Y - a.Y) + 2 * t * (c.Y - b.Y));
                var len = Math.Max(1e-6, Math.Sqrt(tx * tx + ty * ty));
                var half = (w0 + (w1 - w0) * t) / 2;
                left.Add((p.Dx - ty / len * half, p.Y + tx / len * half));
                right.Add((p.Dx + ty / len * half, p.Y - tx / len * half));
            }
            right.Reverse();
            return Pair([.. left, .. right]);
        }

        public static (double Dx, double Y) Bezier((double Dx, double Y) a, (double Dx, double Y) b, (double Dx, double Y) c, double t) =>
            ((1 - t) * (1 - t) * a.Dx + 2 * t * (1 - t) * b.Dx + t * t * c.Dx, (1 - t) * (1 - t) * a.Y + 2 * t * (1 - t) * b.Y + t * t * c.Y);

        /// <summary>Points along a quadratic Bézier, ends included: the outline of a curved edge.</summary>
        public static (double Dx, double Y)[] Curve((double Dx, double Y) a, (double Dx, double Y) b, (double Dx, double Y) c, int n = 16) =>
            [.. Enumerable.Range(0, n + 1).Select(i => Bezier(a, b, c, (double)i / n))];

        public bool[] Rect(double dx0, double y0, double dx1, double y1) => Pair((dx0, y0), (dx1, y0), (dx1, y1), (dx0, y1));

        public bool[] Oval(double dx, double y, double rx, double ry)
        {
            var m = Empty();
            for (var yy = 0; yy < H; yy++)
            {
                for (var xx = 0; xx < W; xx++)
                {
                    var u = (Math.Abs(xx - Cx) - dx) / rx;
                    var v = (yy + 0.5 - y) / ry;
                    if (u * u + v * v <= 1) m[yy * W + xx] = true;
                }
            }
            return m;
        }

        /// <summary>A line `width` pixels wide, and its mirror.</summary>
        public bool[] Line(double dx0, double y0, double dx1, double y1, double width = 1)
        {
            var m = Empty();
            foreach (var i in LinePixels(dx0, y0, dx1, y1, width)) m[i] = true;
            return m;
        }

        /// <summary>The pixels of `Line`, each once.</summary>
        HashSet<int> LinePixels(double dx0, double y0, double dx1, double y1, double width)
        {
            var pixels = new HashSet<int>();
            var n = (int)Math.Ceiling(Math.Max(Math.Abs(dx1 - dx0), Math.Abs(y1 - y0)) * 2) + 1;
            for (var i = 0; i <= n; i++)
            {
                var (dx, y) = (dx0 + (dx1 - dx0) * i / n, y0 + (y1 - y0) * i / n);
                for (var k = 0; k < width; k++)
                {
                    foreach (var x in new[] { (int)Math.Floor(Cx - dx + k - (width - 1) / 2), (int)Math.Floor(Cx + dx + k - (width - 1) / 2 + 0.5) })
                    {
                        if (x >= 0 && x < W && y >= 0 && y < H) pixels.Add((int)y * W + x);
                    }
                }
            }
            return pixels;
        }

        /// <summary>
        /// Carve(And(within, Line(...))) for a short line, without building masks of the whole
        /// canvas: etching many small strokes stays cheap on the biggest hulls.
        /// </summary>
        public void CarveLine(bool[] within, double dx0, double y0, double dx1, double y1, float depth)
        {
            foreach (var i in LinePixels(dx0, y0, dx1, y1, 1))
            {
                if (within[i] && Mat[i] != M.None && !Glows(Mat[i])) Z[i] -= depth;
            }
        }

        public static bool[] And(bool[] a, bool[] b) => Combine(a, b, (u, v) => u && v);

        public static bool[] Or(bool[] a, bool[] b) => Combine(a, b, (u, v) => u || v);

        public static bool[] Minus(bool[] a, bool[] b) => Combine(a, b, (u, v) => u && !v);

        static bool[] Combine(bool[] a, bool[] b, Func<bool, bool, bool> op)
        {
            var m = new bool[a.Length];
            for (var i = 0; i < m.Length; i++) m[i] = op(a[i], b[i]);
            return m;
        }

        /// <summary>
        /// The band `depth` pixels deep along the edges of a shape that face the bow: where its
        /// outline, read from the distance field two pixels around, turns less than about 70°
        /// from facing straight ahead. Steep edges stay continuous; the flanks are left out.
        /// </summary>
        public bool[] Front(bool[] shape, int depth = 1)
        {
            var d = Distance(shape);
            float D(int x, int y) => x < 0 || y < 0 || x >= W || y >= H ? 0 : d[y * W + x];
            var m = Empty();
            for (var i = 0; i < m.Length; i++)
            {
                if (!shape[i] || d[i] > depth + 0.01f) continue;
                var (x, y) = (i % W, i / W);
                var (gx, gy) = (D(x + 2, y) - D(x - 2, y), D(x, y + 2) - D(x, y - 2));
                m[i] = gy > 0.35f * Math.Abs(gx);
            }
            return m;
        }

        /// <summary>Distance of each pixel of a mask to its edge, in pixels (chamfer 3-4).</summary>
        public float[] Distance(bool[] mask)
        {
            var d = new int[W * H];
            const int Far = 1 << 20;
            for (var i = 0; i < d.Length; i++) d[i] = mask[i] ? Far : 0;
            int D(int x, int y) => x < 0 || y < 0 || x >= W || y >= H ? 0 : d[y * W + x];
            for (var y = 0; y < H; y++)
            {
                for (var x = 0; x < W; x++)
                {
                    var i = y * W + x;
                    if (d[i] == 0) continue;
                    d[i] = Math.Min(d[i], Math.Min(Math.Min(D(x - 1, y) + 3, D(x, y - 1) + 3), Math.Min(D(x - 1, y - 1) + 4, D(x + 1, y - 1) + 4)));
                }
            }
            for (var y = H - 1; y >= 0; y--)
            {
                for (var x = W - 1; x >= 0; x--)
                {
                    var i = y * W + x;
                    if (d[i] == 0) continue;
                    d[i] = Math.Min(d[i], Math.Min(Math.Min(D(x + 1, y) + 3, D(x, y + 1) + 3), Math.Min(D(x + 1, y + 1) + 4, D(x - 1, y + 1) + 4)));
                }
            }
            return [.. d.Select(v => v / 3f)];
        }

        // Sculpting.

        /// <summary>Lays a plate over what is there: `height` on top, bevelled down over `bevel` pixels at its edges.</summary>
        public void Raise(bool[] mask, M m, float height, float bevel = 2.5f)
        {
            var d = Distance(mask);
            for (var i = 0; i < mask.Length; i++)
            {
                if (!mask[i]) continue;
                Mat[i] = m;
                Z[i] = height - 1.5f * Math.Max(0, 1 - d[i] / bevel) * Math.Min(height, 2.5f);
            }
        }

        /// <summary>Lifts what is under the mask (a rib, a ridge), keeping its material.</summary>
        public void Lift(bool[] mask, float by)
        {
            for (var i = 0; i < mask.Length; i++) if (mask[i] && Mat[i] != M.None && !Glows(Mat[i])) Z[i] += by;
        }

        /// <summary>Carves a groove where the mask lies on lit material.</summary>
        public void Carve(bool[] mask, float depth = 1.3f) => Lift(mask, -depth);

        /// <summary>A panel line `inset` pixels inside a shape's edge, following its outline.</summary>
        public void Inset(bool[] shape, float inset, float depth = 1.2f)
        {
            var d = Distance(shape);
            for (var i = 0; i < d.Length; i++) if (shape[i] && Math.Abs(d[i] - inset) < 0.5f && !Glows(Mat[i]) && Mat[i] != M.None) Z[i] -= depth;
        }

        /// <summary>Turns the lit material under the mask into another, keeping its relief: a gilded edge.</summary>
        public void Dye(bool[] mask, M m)
        {
            for (var i = 0; i < mask.Length; i++) if (mask[i] && Mat[i] != M.None && !Glows(Mat[i])) Mat[i] = m;
        }

        /// <summary>Glowing pixels: a trim, a slot, a light; `bright` 0 to 2.</summary>
        public void Glow(bool[] mask, M m, float bright = 1)
        {
            for (var i = 0; i < mask.Length; i++)
            {
                if (!mask[i]) continue;
                Mat[i] = m;
                Z[i] = bright;
            }
        }
    }

    /// <summary>A faction's colors: per material, from the deepest tone to the brightest.</summary>
    static Dictionary<M, Color[]> Colors(Faction f)
    {
        static Color[] R(params uint[] rgb) => [.. rgb.Select(c => new Color(((c >> 16) & 255) / 255f, ((c >> 8) & 255) / 255f, (c & 255) / 255f))];
        var plate = R(0x1c151d, 0x2e222f, 0x3e3546, 0x625565, 0x7f708a);
        var core = R(0xae2334, 0xe83b3b, 0xff9b8a);
        return f switch
        {
            Faction.Klaed => new()
            {
                [M.Hull] = R(0x2c0e0e, 0x471818, 0x6e2727, 0xb33831, 0xea4f36), [M.Trim] = R(0x2e222f, 0x3e3546, 0x625565, 0x7f708a, 0x9babb2),
                [M.Plate] = plate, [M.Accent] = R(0xc8d70e, 0xf1ff47, 0xfdffbd), [M.Light] = R(0x30e1b9, 0x8ff8e2, 0xffffff),
                [M.Glass] = R(0x484a77, 0x4d65b4, 0x4d9be6), [M.Engine] = R(0x30e1b9, 0x8ff8e2, 0xffffff), [M.Core] = core,
            },
            Faction.Nairan => new()
            {
                [M.Hull] = R(0x171a1b, 0x313638, 0x374e4a, 0x547e64, 0x92a984), [M.Trim] = R(0x374e4a, 0x547e64, 0x92a984, 0xb2ba90, 0xd6dcc0),
                [M.Plate] = plate, [M.Accent] = R(0x6b3e75, 0x905ea9, 0xa884f3), [M.Light] = R(0x30e1b9, 0x8ff8e2, 0xffffff),
                [M.Glass] = R(0x4d65b4, 0x4d9be6, 0x8fd3ff), [M.Engine] = R(0x30e1b9, 0x8ff8e2, 0xffffff), [M.Core] = core,
            },
            // The Errants: ancient bronze and gold, its machinery green with patina, turquoise lights, a violet heart.
            Faction.Errant => new()
            {
                [M.Hull] = R(0x1c130e, 0x33231a, 0x4f3624, 0x76512f, 0xa87a44), [M.Trim] = R(0x5c3f12, 0x936a1f, 0xcf9f30, 0xf0cf62, 0xfff0b0),
                [M.Plate] = R(0x13221f, 0x1f3b36, 0x2e5a4f, 0x45806f, 0x6aa892), [M.Accent] = R(0x1f8f8a, 0x30e1b9, 0x8ff8e2), [M.Light] = R(0x30e1b9, 0x8ff8e2, 0xffffff),
                [M.Glass] = R(0x4d65b4, 0x4d9be6, 0x8fd3ff), [M.Engine] = R(0xf79617, 0xf9c22b, 0xfff0c0), [M.Core] = R(0x6b3e75, 0xa884f3, 0xe8d8ff),
            },
            _ => new()
            {
                [M.Hull] = R(0x2e222f, 0x3e3546, 0x514554, 0x625565, 0x8d7a91), [M.Trim] = R(0x493744, 0x7e5a5a, 0x966c6c, 0xab947a, 0xc9b49a),
                [M.Plate] = R(0x2e222f, 0x3e3546, 0x625565, 0x7f708a, 0x907d94), [M.Accent] = R(0x165a4c, 0x239063, 0x1ebc73),
                [M.Light] = R(0x484a77, 0x4d65b4, 0x4d9be6), [M.Glass] = R(0x484a77, 0x4d65b4, 0x4d9be6), [M.Engine] = R(0x239063, 0x1ebc73, 0x8ef0b8), [M.Core] = core,
            },
        };
    }

    /// <summary>
    /// Lights the sculpture from the upper left and brings each pixel to its material's ramp; a
    /// black outline around it all. `shade` darkens the lit materials for a more menacing hull.
    /// </summary>
    static Image Paint(Canvas c, Faction f, int shade = 0)
    {
        var colors = Colors(f);
        var img = Image.CreateEmpty(c.W + 2, c.H + 2, false, Image.Format.Rgba8);
        // Light from the upper left, towards the viewer.
        var (lx, ly, lz) = (-0.55, -0.62, 0.56);
        var flat = lz;
        for (var y = 0; y < c.H; y++)
        {
            for (var x = 0; x < c.W; x++)
            {
                var m = c.At(x, y);
                if (m == M.None) continue;
                var z = c.Z[y * c.W + x];
                var ramp = colors[m];
                if (Glows(m))
                {
                    img.SetPixel(x + 1, y + 1, ramp[Math.Clamp((int)Math.Round(z), 0, ramp.Length - 1)]);
                    continue;
                }
                var gx = (c.Height(x + 1, y) - c.Height(x - 1, y)) / 2;
                var gy = (c.Height(x, y + 1) - c.Height(x, y - 1)) / 2;
                var len = Math.Sqrt(gx * gx + gy * gy + 0.36);
                var delta = (-gx * lx - gy * ly + 0.6 * lz) / len - flat;
                var tone = 2 + (delta > 0.08 ? 1 : 0) + (delta > 0.3 ? 1 : 0) - (delta < -0.08 ? 1 : 0) - (delta < -0.3 ? 1 : 0);
                // Crevices are darker, whatever faces the light.
                var around = (c.Height(x + 1, y) + c.Height(x - 1, y) + c.Height(x, y + 1) + c.Height(x, y - 1)) / 4;
                if (z < around - 0.7) tone--;
                // The silhouette: lit along its upper left, dark along its lower right.
                if (c.At(x - 1, y) == M.None || c.At(x, y - 1) == M.None) tone = Math.Max(tone, 3);
                if (c.At(x + 1, y) == M.None || c.At(x, y + 1) == M.None) tone = Math.Min(tone, 1);
                tone -= shade;
                img.SetPixel(x + 1, y + 1, ramp[Math.Clamp(tone, 0, ramp.Length - 1)]);
            }
        }
        var black = new Color(0, 0, 0);
        bool Filled(int u, int v) => u >= 1 && v >= 1 && u <= c.W && v <= c.H && c.At(u - 1, v - 1) != M.None;
        for (var y = 0; y < c.H + 2; y++)
        {
            for (var x = 0; x < c.W + 2; x++)
            {
                if (!Filled(x, y) && (Filled(x - 1, y) || Filled(x + 1, y) || Filled(x, y - 1) || Filled(x, y + 1))) img.SetPixel(x, y, black);
            }
        }
        return img;
    }

    // Each faction's hand: how it builds a hull, a pod, a wing, a jaw, a mount. Offsets from the
    // middle column; y runs from the nose down.

    abstract class Kit(Canvas c)
    {
        protected readonly Canvas C = c;
        protected readonly Random Rand = new(11);

        public abstract void Spine(double top, double bottom, double half);
        public abstract void Pod(double dx, double top, double bottom, double half);
        public abstract void Wing(double root, double y, double span, double depth);
        public abstract void Jaw(double dx, double top, double bottom, double half);

        /// <summary>A gun mount: a raised ring of plate around a lit eye.</summary>
        public virtual void Mount(double dx, double y, double r)
        {
            C.Raise(C.Oval(dx, y, r, r), M.Plate, 6, 1.5f);
            C.Carve(Canvas.And(C.Oval(dx, y, r * 0.7, r * 0.7), C.Oval(dx, y, r * 0.7, r * 0.7)), 0.8f);
            C.Glow(C.Oval(dx, y, r * 0.42, r * 0.42), M.Glass, 1);
            C.Glow(C.Oval(dx - r * 0.12, y - r * 0.15, r * 0.15, r * 0.15), M.Light, 2);
        }

        /// <summary>A socket for a part: a raised collar, sunk in the middle; a shield generator's shows its lens, a hangar's its lit mouth.</summary>
        public virtual void Base(HardpointRole role, double dx, double y, double r)
        {
            C.Raise(C.Oval(dx, y, r, r), M.Plate, 6, 1.5f);
            C.Carve(C.Oval(dx, y, r * 0.65, r * 0.65), 1.6f);
            if (role == HardpointRole.Shield) C.Glow(C.Oval(dx, y, r * 0.35, r * 0.35), M.Glass, 1);
            if (role == HardpointRole.Hangar) C.Glow(C.Line(dx - r * 0.5, y + r * 0.45, dx + r * 0.5, y + r * 0.45), M.Light, 1);
        }

        /// <summary>Hangar mouths down a flank: dark recesses with launch lights.</summary>
        public void Bays(double dx, double top, int count, double h, double w)
        {
            for (var i = 0; i < count; i++)
            {
                var y = top + i * (h + 3);
                C.Carve(C.Rect(dx - w / 2, y, dx + w / 2, y + h), 2.5f);
                C.Glow(C.Line(dx - w / 2 + 1, y + h - 1, dx + w / 2 - 1, y + h - 1), M.Light, 1);
                C.Glow(C.Rect(dx - 0.5, y + 1, dx + 0.5, y + 2), M.Light, 2);
            }
        }

        /// <summary>Engine blocks along the stern line, their nozzles lit.</summary>
        public virtual void Engines(double y, params double[] dxs)
        {
            foreach (var dx in dxs)
            {
                C.Nozzles.Add((dx, y + 1, 2));
                C.Raise(C.Rect(dx - 3, y - 6, dx + 3, y), M.Plate, 4, 1.5f);
                C.Glow(C.Rect(dx - 2, y - 1, dx + 2, y + 1), M.Engine, 1);
                C.Glow(C.Rect(dx - 1, y, dx + 1, y + 1), M.Engine, 2);
            }
        }

        /// <summary>A row of small raised studs along a line.</summary>
        protected void Rivets(double dx, double y0, double y1, double step)
        {
            for (var y = y0; y <= y1; y += step) C.Lift(C.Rect(dx, y, dx + 1, y + 1), 0.9f);
        }
    }

    /// <summary>Kla'ed: ribbed red caissons with cut corners, grey mechanical pylons, glowing yellow trims, cyan eyes.</summary>
    sealed class KlaedKit(Canvas c) : Kit(c)
    {
        void Ribbed(bool[] shape, double from, double to, double half, double top)
        {
            for (var dx = from; dx < to; dx += 4) C.Carve(Canvas.And(shape, C.Line(dx, top, dx, C.H)), 1.1f);
            for (var y = top + 14; y < C.H; y += 16) C.Carve(Canvas.And(shape, C.Line(0, y, half, y)), 1.2f);
        }

        public override void Spine(double top, double bottom, double half)
        {
            var cut = half * 0.6;
            var shape = C.Body((half - cut, top), (half, top + cut), (half, bottom - 4), (half - 3, bottom));
            C.Raise(shape, M.Hull, 4);
            C.Inset(shape, 2.2f);
            Ribbed(shape, 4, half - 3, half, top + cut);
            // A raised keel with its glowing stripe, dashed.
            C.Lift(C.Rect(-2, top + cut * 0.6, 2, bottom - 6), 1.5f);
            for (var y = top + cut + 2; y < bottom - 8; y += 2) C.Glow(C.Rect(-1, y, 1, y + 1), M.Accent, (int)(y - top) % 6 < 2 ? 2 : 1);
        }

        public override void Pod(double dx, double top, double bottom, double half)
        {
            // A grey pylon to the hull, then the pod: a ribbed caisson, yellow trim on its outer edge.
            var mid = (top + bottom) / 2;
            C.Raise(C.Rect(dx - half - 9, mid - 4, dx - half + 1, mid + 4), M.Plate, 3, 1.5f);
            C.Carve(C.Line(dx - half - 8, mid, dx - half, mid), 0.8f);
            var shape = C.Pair((dx - half, top + half), (dx - half * 0.3, top), (dx + half * 0.3, top), (dx + half, top + half), (dx + half, bottom - 2), (dx + half - 2, bottom), (dx - half, bottom));
            C.Raise(shape, M.Hull, 4);
            C.Inset(shape, 2f);
            for (var x = dx - half + 3; x < dx + half - 2; x += 3) C.Carve(Canvas.And(shape, C.Line(x, top + half + 2, x, bottom - 3)), 1f);
            for (var y = top + half + 2; y < bottom - 3; y += 2) C.Glow(C.Line(dx + half - 1, y, dx + half - 1, y + 1), M.Accent, (int)(y - top) % 6 < 2 ? 2 : 1);
        }

        public override void Wing(double root, double y, double span, double depth)
        {
            var slab = C.Pair((root, y), (span, y + depth * 0.25), (span, y + depth), (root, y + depth * 0.85));
            C.Raise(slab, M.Plate, 3, 2);
            for (var x = root + 3; x < span - 1; x += 4) C.Carve(Canvas.And(slab, C.Line(x, y, x, y + depth)), 0.9f);
            C.Raise(C.Pair((span - 3, y + depth * 0.2), (span + 2, y + depth * 0.3), (span + 2, y + depth + 3), (span - 3, y + depth + 1)), M.Hull, 4, 1.5f);
            C.Glow(C.Line(span + 1, y + depth * 0.35, span + 1, y + depth + 1), M.Accent, 1);
        }

        public override void Jaw(double dx, double top, double bottom, double half)
        {
            // A square-cut fang leaning in to its point, ribbed, a yellow edge down its inner side.
            var len = bottom - top;
            var shape = C.Pair((dx + half, bottom), (dx + half, top + len * 0.3), (dx - half * 0.3, top), (dx - half, top + len * 0.22), (dx - half, bottom));
            C.Raise(shape, M.Hull, 4);
            C.Inset(shape, 2f);
            for (var y = top + len * 0.3; y < bottom - 2; y += 6) C.Carve(Canvas.And(shape, C.Line(dx - half, y, dx + half, y)), 1.1f);
            for (var y = top + len * 0.25; y < bottom - 3; y += 2) C.Glow(C.Line(dx - half + 1, y, dx - half + 1, y + 1), M.Accent, (int)(y - top) % 6 < 2 ? 2 : 1);
        }

        public override void Base(HardpointRole role, double dx, double y, double r)
        {
            // Hexagonal collars, as everything Kla'ed.
            C.Raise(C.Pair((dx - r * 0.5, y - r), (dx + r * 0.5, y - r), (dx + r, y), (dx + r * 0.5, y + r), (dx - r * 0.5, y + r), (dx - r, y)), M.Plate, 6, 1.5f);
            C.Carve(C.Oval(dx, y, r * 0.6, r * 0.6), 1.6f);
            if (role == HardpointRole.Shield) C.Glow(C.Oval(dx, y, r * 0.3, r * 0.3), M.Light, 1);
            if (role == HardpointRole.Hangar) C.Glow(C.Line(dx - r * 0.5, y + r * 0.45, dx + r * 0.5, y + r * 0.45), M.Light, 1);
        }

        public override void Mount(double dx, double y, double r)
        {
            // Hexagonal, a cyan ring round a dark eye.
            C.Raise(C.Pair((dx - r * 0.5, y - r), (dx + r * 0.5, y - r), (dx + r, y), (dx + r * 0.5, y + r), (dx - r * 0.5, y + r), (dx - r, y)), M.Plate, 6, 1.5f);
            C.Glow(Canvas.And(C.Oval(dx, y, r * 0.62, r * 0.62), C.Oval(dx, y, r * 0.62, r * 0.62)), M.Light, 1);
            C.Raise(C.Oval(dx, y, r * 0.36, r * 0.36), M.Plate, 4, 1);
            C.Glow(C.Rect(dx - r * 0.1, y - r * 0.1, dx + r * 0.1, y + r * 0.1), M.Light, 2);
        }
    }

    /// <summary>Nairan: green gothic arches with panel lines following them, beige ridges, violet slots, a teal heart.</summary>
    sealed class NairanKit(Canvas c) : Kit(c)
    {
        public override void Spine(double top, double bottom, double half)
        {
            var len = bottom - top;
            var shape = C.Body((0.5, top), (half * 0.42, top + len * 0.14), (half * 0.82, top + len * 0.32), (half, top + len * 0.48), (half, bottom - 5), (half * 0.7, bottom));
            C.Raise(shape, M.Hull, 4, 4);
            C.Inset(shape, 2.5f);
            C.Inset(shape, 6.5f, 0.9f);
            // A pale ridge down the middle, violet slots in pairs, a blue arc, the teal heart astern.
            C.Raise(C.Rect(-1.5, top + len * 0.08, 1.5, bottom - 8), M.Trim, 6, 1.5f);
            for (var y = top + len * 0.36; y < bottom - len * 0.22; y += 8)
            {
                C.Carve(C.Rect(half * 0.38, y - 1, half * 0.74, y + 5), 1.2f);
                C.Glow(C.Rect(half * 0.42, y, half * 0.7, y + 4), M.Accent, 1);
                C.Glow(C.Rect(half * 0.42, y, half * 0.7, y + 1), M.Accent, 2);
            }
            C.Glow(C.Line(half * 0.55, bottom - len * 0.15, 0, bottom - len * 0.2, 1.5), M.Glass, 1);
            C.Glow(C.Oval(0, bottom - len * 0.08, half * 0.28, 3), M.Light, 1);
            C.Glow(C.Oval(0, bottom - len * 0.08, half * 0.12, 1.5), M.Light, 2);
        }

        public override void Pod(double dx, double top, double bottom, double half)
        {
            var len = bottom - top;
            var shape = C.Pair((dx, top), (dx + half, top + len * 0.34), (dx + half, bottom - 2), (dx + half - 2, bottom), (dx - half, bottom), (dx - half, top + len * 0.34));
            C.Raise(shape, M.Hull, 4, 3);
            C.Inset(shape, 2.5f);
            C.Raise(C.Rect(dx - 0.5, top + len * 0.15, dx + 0.5, bottom - 4), M.Trim, 6, 1);
            C.Glow(C.Line(dx + half, top + len * 0.36, dx + half, bottom - 3), M.Accent, 2);
        }

        public override void Wing(double root, double y, double span, double depth)
        {
            // A grey blade swept back, ribbed, its outer edge lit violet.
            var blade = C.Pair((root, y), (span, y + depth * 0.45), (span, y + depth * 1.25), (root, y + depth));
            C.Raise(blade, M.Plate, 3, 2);
            for (var k = 0.25; k < 0.95; k += 0.18) C.Carve(Canvas.And(blade, C.Line(root + (span - root) * k, y, root + (span - root) * k + 3, y + depth * 1.3)), 1f);
            C.Glow(C.Line(span, y + depth * 0.5, span, y + depth * 1.2), M.Accent, 2);
        }

        public override void Jaw(double dx, double top, double bottom, double half)
        {
            var len = bottom - top;
            var shape = C.Pair((dx - half * 0.6, top), (dx + half, top + len * 0.45), (dx + half, bottom), (dx - half, bottom), (dx - half, top + len * 0.25));
            C.Raise(shape, M.Hull, 4, 3);
            C.Inset(shape, 2.5f);
            C.Raise(C.Line(dx - half * 0.2, top + len * 0.15, dx, bottom - 3, 1.5), M.Trim, 6, 1);
            C.Glow(C.Line(dx - half, top + len * 0.27, dx - half, bottom - 2), M.Accent, 2);
        }
    }

    /// <summary>Nautolan: ray-like stone hulls etched with mazes, flesh-colored ridges, green crescents, blue chevrons.</summary>
    sealed class NautolanKit(Canvas c) : Kit(c)
    {
        /// <summary>Etches a labyrinth into the stone under a shape.</summary>
        void Maze(bool[] shape)
        {
            for (var y = 3; y < C.H; y += 4)
            {
                for (var x = 2; x < C.Cx; x += 4)
                {
                    var dx = C.Cx - x;
                    switch (Rand.Next(3))
                    {
                        case 0: C.CarveLine(shape, dx, y, dx - 3, y, 0.9f); break;
                        case 1: C.CarveLine(shape, dx, y, dx, y + 3, 0.9f); break;
                        default: C.CarveLine(shape, dx, y, dx - 2, y + 2, 0.9f); break;
                    }
                }
            }
        }

        public override void Spine(double top, double bottom, double half)
        {
            var len = bottom - top;
            var shape = C.Body((0.5, top), (half * 0.35, top + len * 0.1), (half * 0.78, top + len * 0.32), (half, top + len * 0.52), (half * 0.8, top + len * 0.76), (half * 0.35, bottom - 3), (0.5, bottom));
            C.Raise(shape, M.Hull, 4, 5);
            C.Inset(shape, 2.5f);
            Maze(shape);
            // A flesh-colored backbone, blue chevrons, green crescents along the flanks.
            C.Raise(C.Body((0.5, top + 2), (2.5, top + len * 0.2), (2.5, bottom - len * 0.15), (0.5, bottom - 4)), M.Trim, 6, 1.5f);
            for (var i = 0; i < 4; i++)
            {
                var y = top + len * (0.28 + i * 0.055);
                C.Glow(C.Line(half * 0.3, y + 3, 0, y, 1.2), M.Glass, i == 0 ? 2 : 1);
            }
            for (var t = 0.0; t <= 1; t += 0.02)
            {
                var y = top + len * (0.36 + 0.36 * t);
                C.Glow(C.Rect(half * (0.86 - 0.22 * Math.Sin(t * Math.PI)), y, half * (0.86 - 0.22 * Math.Sin(t * Math.PI)) + 1, y + 1), M.Accent, t > 0.3 && t < 0.7 ? 2 : 1);
            }
        }

        public override void Pod(double dx, double top, double bottom, double half)
        {
            var len = bottom - top;
            var shape = C.Pair((dx, top), (dx + half, top + len * 0.48), (dx + half * 0.45, bottom), (dx - half * 0.45, bottom), (dx - half, top + len * 0.48));
            C.Raise(shape, M.Hull, 4, 3);
            C.Inset(shape, 2f);
            Maze(shape);
            C.Raise(C.Line(dx, top + 3, dx, bottom - 4, 1.5), M.Trim, 6, 1);
            C.Glow(C.Line(dx + half * 0.72, top + len * 0.35, dx + half * 0.32, top + len * 0.78), M.Accent, 2);
        }

        public override void Wing(double root, double y, double span, double depth)
        {
            // A ray's fin: swept to a point, its trailing edge curved in, a green crescent near the edge.
            var fin = C.Pair((root, y), (span * 0.8, y + depth * 0.5), (span, y + depth * 1.15), (span * 0.72, y + depth), (root, y + depth * 1.35));
            C.Raise(fin, M.Hull, 3, 3);
            C.Inset(fin, 2f);
            Maze(fin);
            for (var t = 0.15; t < 0.9; t += 0.03)
                C.Glow(C.Rect(root + (span - root) * t, y + depth * (0.5 + 0.45 * t) - 2 * Math.Sin(t * Math.PI), root + (span - root) * t + 1, y + depth * (0.5 + 0.45 * t) - 2 * Math.Sin(t * Math.PI) + 1), M.Accent, 1);
        }

        public override void Jaw(double dx, double top, double bottom, double half)
        {
            // A curved horn, a flesh ridge down it.
            var len = bottom - top;
            var shape = C.Pair((dx + half * 0.6, top), (dx + half, top + len * 0.5), (dx + half * 0.35, bottom), (dx - half, bottom), (dx - half * 0.15, top + len * 0.5));
            C.Raise(shape, M.Hull, 4, 3);
            Maze(shape);
            C.Raise(C.Line(dx + half * 0.45, top + 3, dx - half * 0.1, bottom - 3, 1.5), M.Trim, 6, 1);
        }

        public override void Engines(double y, params double[] dxs)
        {
            foreach (var dx in dxs)
            {
                C.Nozzles.Add((dx, y, 1.6));
                C.Raise(C.Oval(dx, y - 3, 3, 3), M.Trim, 4, 1.5f);
                C.Glow(C.Oval(dx, y - 1, 1.6, 1.6), M.Engine, 1);
                C.Glow(C.Rect(dx - 0.5, y - 1, dx + 0.5, y), M.Engine, 2);
            }
        }
    }

    /// <summary>
    /// Errants: ancient bronze hulls in terraces, as old temples, every edge facing the bow gilded;
    /// pods of gold-rimmed plates lapping one over the next; crescent horns, collars and wings;
    /// machinery green with patina; a turquoise line down the keel; a violet eye at the bow of the
    /// warships, a crown astern of the greatest.
    /// </summary>
    sealed class ErrantKit(Canvas c) : Kit(c)
    {
        /// <summary>Gilds the edges of a shape that face the bow, `depth` pixels deep.</summary>
        public void Gild(bool[] shape, int depth = 1) => C.Dye(C.Front(shape, depth), M.Trim);

        /// <summary>
        /// Plates across a shape, `step` pixels long, from `from` to `to`: gold rims convex to the
        /// bow, centered `dx` from the middle, each raised over a dark seam where it laps over the
        /// plate behind.
        /// </summary>
        public void Plates(bool[] shape, double dx, double from, double to, double half, double step)
        {
            var rim = half >= 7 ? 2 : 1;
            var (rx, ry) = (half * 1.25, half * 0.75);
            for (var y = from; y < to; y += step)
            {
                var (a, b, s) = (C.Oval(dx, y + ry, rx, ry), C.Oval(dx, y + ry + rim, rx, ry), C.Oval(dx, y + ry + rim + 1, rx, ry));
                var band = Canvas.And(shape, Canvas.Minus(a, b));
                C.Dye(band, M.Trim);
                C.Lift(band, 1);
                C.Carve(Canvas.And(shape, Canvas.Minus(b, s)), 1.4f);
            }
        }

        /// <summary>The turquoise line down the keel, sunk in a channel.</summary>
        public void Keel(double top, double bottom)
        {
            C.Carve(C.Rect(-1.5, top, 1.5, bottom), 0.9f);
            C.Glow(C.Rect(-0.5, top, 0.5, bottom), M.Accent, 1);
        }

        /// <summary>A canopy of blue glass as the packs have them, cut as a pointed gem in a gold setting: lit at its point, dark at its back.</summary>
        public void Canopy(double y, double rx, double ry)
        {
            bool[] Gem(double grow) => C.Body((0.5, y - ry - grow), (rx + grow, y - ry * 0.25), (rx + grow, y + ry * 0.45), (0.5, y + ry + grow));
            C.Raise(Gem(1), M.Trim, 5, 1);
            var gem = Gem(0);
            C.Glow(gem, M.Glass, 1);
            C.Glow(Canvas.And(gem, C.Rect(-rx, y + ry * 0.35, rx, y + ry + 1)), M.Glass, 0);
            C.Glow(Canvas.And(gem, C.Rect(-0.5, y - ry - 1, 0.5, y - ry * 0.2)), M.Glass, 2);
        }

        /// <summary>The violet eye: a gem sunk in a gold ring, crowned on its bow side with gold points when there is room.</summary>
        public void Eye(double y, double r)
        {
            if (r >= 4)
            {
                // Five points fanning out over its bow side; each drawn with its mirror.
                for (var i = 0; i <= 2; i++)
                {
                    var (sin, cos) = Math.SinCos(i * 0.55);
                    var (bx, by, w) = (sin * r * 0.9, y - cos * r * 0.9, r * 0.32);
                    C.Raise(C.Pair((bx + cos * w, by + sin * w), (sin * r * 1.7, y - cos * r * 1.7), (bx - cos * w, by - sin * w)), M.Trim, 6, 1);
                }
            }
            C.Raise(C.Oval(0, y, r + 1, r + 1), M.Trim, 6, 1.2f);
            C.Carve(C.Oval(0, y, r, r), 1.5f);
            C.Glow(C.Oval(0, y, r - 0.5, r - 0.5), M.Core, 0);
            C.Glow(C.Oval(0, y - r * 0.1, r * 0.6, r * 0.6), M.Core, 1);
            C.Glow(C.Oval(0, y - r * 0.35, Math.Max(0.6, r * 0.2), Math.Max(0.6, r * 0.2)), M.Core, 2);
        }

        /// <summary>A crown laid on the deck astern: a gold band `half` wide at `y`, `points` gold points rising from it toward the bow, gems between them.</summary>
        public void Crown(double y, double half, int points, double height)
        {
            var w = half / points;
            for (var i = 0; i < points; i++)
            {
                var dx = -half + w * (2 * i + 1);
                C.Raise(C.Pair((dx - w * 0.8, y), (dx, y - height), (dx + w * 0.8, y)), M.Trim, 6.5f, 1);
            }
            C.Raise(C.Rect(-half, y - 1, half, y + 1.5), M.Trim, 6.5f, 1);
            for (var i = 1; i < points; i++) C.Glow(C.Rect(-half + w * 2 * i - 0.5, y - 0.5, -half + w * 2 * i + 0.5, y + 0.5), M.Accent, 2);
        }

        /// <summary>One engine and its mirror: a bell green with patina, a gold lip round its glowing mouth.</summary>
        public void Bell(double dx, double y, double r)
        {
            C.Nozzles.Add((dx, y + 0.5, Math.Max(1, r * 0.6)));
            C.Raise(C.Pair((dx - r * 0.7, y - r * 2.2), (dx + r * 0.7, y - r * 2.2), (dx + r, y), (dx - r, y)), M.Plate, 4, 1.2f);
            C.Dye(C.Rect(dx - r, y - 1, dx + r, y), M.Trim);
            C.Glow(C.Rect(dx - r * 0.6, y - 0.5, dx + r * 0.6, y + 0.5), M.Engine, 1);
            if (r >= 2) C.Glow(C.Rect(dx - 0.5, y - 0.5, dx + 0.5, y + 0.5), M.Engine, 2);
        }

        public override void Engines(double y, params double[] dxs)
        {
            foreach (var dx in dxs) Bell(dx, y, 3);
        }

        /// <summary>A lance, `half` wide: an ogive bow, straight flanks, the stern drawn in.</summary>
        bool[] Lance(double top, double bottom, double half)
        {
            var len = bottom - top;
            return C.Body((0.5, top), (half * 0.4, top + len * 0.08), (half * 0.72, top + len * 0.19), (half * 0.93, top + len * 0.31), (half, top + len * 0.42),
                (half, bottom - len * 0.1), (half * 0.8, bottom - 1), (half * 0.6, bottom));
        }

        public override void Spine(double top, double bottom, double half)
        {
            // Terraces, as an old temple: the hull, a narrower deck on it, a gold ridge on that
            // carrying the keel's light; turquoise windows down the flanks between hull and deck.
            var len = bottom - top;
            var hull = Lance(top, bottom, half);
            C.Raise(hull, M.Hull, 3, (float)Math.Clamp(half * 0.15, 2, 3));
            if (half >= 20) C.Inset(hull, 2.5f, 1f);
            Gild(hull, half >= 14 ? 2 : 1);
            var deck = Lance(top + len * 0.1, bottom - len * 0.05, half * 0.55);
            var window = half * 0.78;
            for (var y = top + len * 0.36; y < bottom - len * 0.14; y += Math.Max(4, half * 0.45))
            {
                C.Carve(C.Rect(window - 1, y - 0.5, window + 1, y + 2.5), 1f);
                C.Glow(C.Rect(window - 0.5, y, window + 0.5, y + 2), M.Accent, 1);
            }
            C.Raise(deck, M.Hull, 5.5f, 2);
            if (half >= 20) C.Inset(deck, 2f, 1f);
            Gild(deck);
            var ridge = C.Body((0.5, top + len * 0.15), (Math.Max(1.5, half * 0.16), top + len * 0.22), (Math.Max(1.5, half * 0.16), bottom - len * 0.1), (0.5, bottom - len * 0.07));
            C.Raise(ridge, M.Trim, 7, 1);
            C.Glow(C.Rect(-0.5, top + len * 0.22, 0.5, bottom - len * 0.11), M.Accent, 1);
        }

        public override void Pod(double dx, double top, double bottom, double half)
        {
            // A slender hull whose bow curls in to a horn, as a crescent's, clear of the spine;
            // gold-rimmed plates down it, a light at the tip.
            var len = bottom - top;
            var shape = C.Pair((dx - half * 0.55, top), (dx + half * 0.25, top + len * 0.1), (dx + half, top + len * 0.26), (dx + half, bottom - 3),
                (dx + half * 0.6, bottom), (dx - half * 0.8, bottom), (dx - half * 0.8, top + len * 0.2));
            C.Raise(shape, M.Hull, 4, (float)Math.Clamp(half * 0.15, 1.2, 2));
            C.Inset(shape, half < 8 ? 1.5f : 2f, 1f);
            Plates(shape, dx, top + len * 0.2, bottom - 4, half, Math.Clamp(half * 1.3, 6, 24));
            Gild(shape, half >= 12 ? 2 : 1);
            C.Glow(C.Rect(dx - half * 0.55 - 0.5, top + 1, dx - half * 0.55 + 0.5, top + 2), M.Light, 2);
        }

        public override void Wing(double root, double y, double span, double depth)
        {
            // A crescent blade: its leading edge sweeps out and forward to a horn, the trailing edge curls back in.
            var w = span - root;
            var tip = (span, y - depth * 0.35);
            var blade = C.Pair([.. Canvas.Curve((root, y), (root + w * 0.8, y + depth * 0.05), tip), .. Canvas.Curve(tip, (root + w * 0.7, y + depth * 0.7), (root, y + depth * 1.1)).Skip(1)]);
            C.Raise(blade, M.Hull, 3, (float)Math.Clamp(depth * 0.12, 2, 3));
            if (depth >= 30) C.Inset(blade, 2.2f);
            // Feathers: grooves fanning from the root.
            for (var k = 0.3; k < 0.95; k += 0.22) C.Carve(Canvas.And(blade, C.Line(root + 2, y + depth * 0.5, root + w * k, y + depth * (0.1 - 0.3 * k))), 0.9f);
            Gild(blade, depth >= 30 ? 2 : 1);
            C.Glow(C.Rect(span - 1.5, y - depth * 0.35 + 1, span - 0.5, y - depth * 0.35 + 2), M.Light, 2);
        }

        public override void Jaw(double dx, double top, double bottom, double half)
        {
            // A crescent horn rising from its root and curling in at its tip, turquoise along its inner edge.
            var len = bottom - top;
            var (root, pull, tip) = ((dx, bottom), (dx + half * 0.7, top + len * 0.35), (dx - half * 0.9, top));
            var horn = C.Stroke(root, pull, tip, half * 2, 1);
            C.Raise(horn, M.Hull, 4, (float)Math.Clamp(half * 0.3, 2, 3));
            if (half >= 10) C.Inset(horn, 2.2f);
            for (var t = 0.2; t < 0.85; t += 0.16)
            {
                var p = Canvas.Bezier(root, pull, tip, t);
                C.Carve(Canvas.And(horn, C.Line(p.Dx - half, p.Y + half * 0.3, p.Dx + half, p.Y - half * 0.3)), 0.9f);
            }
            Gild(horn, half >= 10 ? 2 : 1);
            for (var t = 0.3; t < 0.9; t += 0.08)
            {
                var p = Canvas.Bezier(root, pull, tip, t);
                var w = (half * 2 + (1 - half * 2) * t) / 2;
                C.Glow(C.Rect(p.Dx - w * 0.6 - 0.5, p.Y, p.Dx - w * 0.6 + 0.5, p.Y + 1), M.Accent, (int)(t * 12) % 3 == 0 ? 2 : 1);
            }
        }

        public override void Mount(double dx, double y, double r)
        {
            // A gold ring round a turquoise gem.
            C.Raise(C.Oval(dx, y, r, r), M.Trim, 6, 1.5f);
            C.Carve(C.Oval(dx, y, r * 0.7, r * 0.7), 1.2f);
            C.Glow(C.Oval(dx, y, r * 0.45, r * 0.45), M.Accent, 1);
            C.Glow(C.Oval(dx - r * 0.12, y - r * 0.15, r * 0.15, r * 0.15), M.Light, 2);
        }

        public override void Base(HardpointRole role, double dx, double y, double r)
        {
            // A collar green with patina, ringed in gold, sunk in the middle.
            C.Raise(C.Oval(dx, y, r, r), M.Plate, 6, 1.5f);
            C.Dye(Canvas.Minus(C.Oval(dx, y, r, r), C.Oval(dx, y, r - 1, r - 1)), M.Trim);
            C.Carve(C.Oval(dx, y, r * 0.65, r * 0.65), 1.6f);
            if (role == HardpointRole.Shield) C.Glow(C.Oval(dx, y, r * 0.35, r * 0.35), M.Accent, 1);
            if (role == HardpointRole.Hangar) C.Glow(C.Line(dx - r * 0.5, y + r * 0.45, dx + r * 0.5, y + r * 0.45), M.Light, 1);
        }
    }

    static Kit KitOf(Faction f, Canvas c) => f switch
    {
        Faction.Klaed => new KlaedKit(c),
        Faction.Nairan => new NairanKit(c),
        Faction.Errant => new ErrantKit(c),
        _ => new NautolanKit(c),
    };

    // The plans, the same for every faction, each faction's hand building them its own way.

    /// <summary>Bulwark: a squat block behind tiers of frontal armor, one small gun.</summary>
    static Canvas Bulwark(Faction f)
    {
        var c = new Canvas(70, 76);
        var k = KitOf(f, c);
        k.Pod(24, 18, 62, 8);
        k.Spine(8, 68, 19);
        // Tiers of armor across the bow, each wider and lower than the one before.
        for (var i = 2; i >= 0; i--)
        {
            var y = 10 + i * 7.0;
            var tier = c.Body((12 + i * 5, y), (17 + i * 5, y + 3), (17 + i * 5, y + 8), (12 + i * 5, y + 8));
            c.Raise(tier, M.Hull, 6 - i, 2);
            c.Inset(tier, 1.5f, 0.9f);
        }
        k.Mount(0, 46, 5);
        k.Engines(68, 7, 15);
        return c;
    }

    /// <summary>Carrier: a long flight deck between two hangar hulls lined with launch bays.</summary>
    static Canvas Carrier(Faction f)
    {
        var c = new Canvas(84, 124);
        var k = KitOf(f, c);
        k.Wing(21, 86, 40, 16);
        k.Pod(26, 14, 110, 10);
        k.Spine(2, 116, 14);
        k.Bays(26, 34, 6, 7, 9);
        // The flight deck: a dark lane down the spine, landing lights dashed along it.
        c.Carve(c.Rect(0, 38, 4.5, 104), 2f);
        for (var y = 40; y < 102; y += 6) c.Glow(c.Rect(-0.5, y, 0.5, y + 2), M.Light, y % 12 < 6 ? 2 : 1);
        k.Engines(116, 6);
        k.Engines(110, 26);
        return c;
    }

    /// <summary>Titan: a massive spine between two pods, great wings, a battery of mounts.</summary>
    static Canvas Titan(Faction f)
    {
        var c = new Canvas(130, 170);
        var k = KitOf(f, c);
        k.Wing(32, 72, 64, 34);
        k.Pod(38, 30, 150, 13);
        k.Spine(2, 162, 25);
        k.Engines(162, 7, 17);
        k.Engines(150, 38);
        return c;
    }

    /// <summary>
    /// Leviathan: a spine thrust between two great jaws, pods and wings astern, a red core
    /// burning amidships, the whole hull darker.
    /// </summary>
    static Canvas Leviathan(Faction f)
    {
        var c = new Canvas(184, 240);
        var k = KitOf(f, c);
        k.Wing(46, 124, 90, 48);
        k.Pod(62, 74, 218, 15);
        k.Jaw(35, 0, 124, 12);
        for (var y = 30; y < 112; y += 12) c.Glow(c.Rect(35, y, 36, y + 1), M.Core, 2);
        k.Spine(16, 230, 31);
        // The core: a red eye amidships in a sunken ring.
        c.Raise(c.Oval(0, 120, 13, 15), M.Plate, 6, 2);
        c.Carve(c.Oval(0, 120, 10, 12), 2f);
        c.Glow(c.Oval(0, 120, 8, 10), M.Core, 0);
        c.Glow(c.Oval(0, 119, 5, 7), M.Core, 1);
        c.Glow(c.Oval(-1.5, 116, 2, 3), M.Core, 2);
        k.Engines(230, 8, 21);
        k.Engines(218, 62);
        return c;
    }

    /// <summary>
    /// Sockets where the game puts the class's parts (turrets, hangars, shield generators): the
    /// parts are separate sprites that turn on their own, so each sits on its own socket. Their
    /// places come from the class's data, in world units, brought to the sprite's pixels the way
    /// the battle view sizes it (its longest side shown at 2.5 radii).
    /// </summary>
    static void Bases(Canvas c, Kit k, ShipClass cls)
    {
        var def = Ships.Defs[cls];
        if (def.Hardpoints is null) return;
        int top = c.H, bottom = 0, left = c.W, right = 0;
        for (var i = 0; i < c.Mat.Length; i++)
        {
            if (c.Mat[i] == M.None) continue;
            var (x, y) = (i % c.W, i / c.W);
            (top, bottom, left, right) = (Math.Min(top, y), Math.Max(bottom, y), Math.Min(left, x), Math.Max(right, x));
        }
        var perPixel = def.Radius * 2 * 1.25 / (Math.Max(bottom - top, right - left) + 3);
        foreach (var slot in def.Hardpoints)
        {
            if (slot.Role == HardpointRole.Engine) continue;
            var r = (slot.Radius ?? Hardpoints.Defs[slot.Role].Radius) / perPixel + 1;
            // Ship frame: x forward (up on the sprite), y to starboard (right).
            k.Base(slot.Role, Math.Abs(slot.Y) / perPixel, (c.H + 1) / 2.0 - slot.X / perPixel, r);
        }
    }

    /// <summary>
    /// Colossus: the neutral raid boss. A vast spine between two great jaws, long pods, broad
    /// wings, its heart burning in a crown of plate amidships.
    /// </summary>
    static Canvas Colossus(Faction f)
    {
        var c = new Canvas(252, 316);
        var k = KitOf(f, c);
        k.Wing(60, 150, 124, 64);
        k.Pod(86, 72, 288, 19);
        k.Jaw(46, 0, 152, 16);
        k.Spine(14, 300, 44);
        c.Raise(c.Oval(0, 158, 22, 24), M.Plate, 7, 2.5f);
        c.Inset(c.Oval(0, 158, 22, 24), 3f);
        c.Carve(c.Oval(0, 158, 15, 17), 2f);
        c.Glow(c.Oval(0, 158, 13, 15), M.Core, 0);
        c.Glow(c.Oval(0, 157, 8, 10), M.Core, 1);
        c.Glow(c.Oval(-2, 152, 3, 4), M.Core, 2);
        k.Engines(300, 10, 30);
        k.Engines(288, 86);
        return c;
    }

    // The Errants have no pack: their craft and warships are drawn here too, in their own hand,
    // sized as the packs' ships of the same class.

    /// <summary>Scout: a slender lance through a gold crescent collar, horns forward.</summary>
    static Canvas Scout()
    {
        var c = new Canvas(23, 29);
        var k = new ErrantKit(c);
        var collar = c.Stroke((0, 19), (9, 19), (10, 7), 3.5, 1.2);
        c.Raise(collar, M.Trim, 3, 1.2f);
        c.Glow(c.Rect(9.5, 7, 10.5, 8), M.Light, 2);
        var lance = c.Body((0.5, 0), (1.5, 3), (2.5, 8), (3.5, 15), (3.5, 22), (2.5, 27), (1.5, 28));
        c.Raise(lance, M.Hull, 3.5f, 1.5f);
        k.Gild(lance);
        k.Keel(12, 25);
        k.Canopy(7.5, 1.5, 2.5);
        k.Bell(0, 28, 1.5);
        return c;
    }

    /// <summary>Fighter: a broad crescent wing, horns forward with a gun at each tip, a turquoise line along it, round a short hull.</summary>
    static Canvas Fighter()
    {
        var c = new Canvas(29, 25);
        var k = new ErrantKit(c);
        c.Raise(c.Rect(12.5, 0, 13.5, 5), M.Plate, 3, 1);
        c.Glow(c.Rect(12.5, 0, 13.5, 1), M.Light, 2);
        var wing = c.Stroke((0, 16), (12, 16.5), (13.5, 4), 6, 2);
        c.Raise(wing, M.Hull, 3, 1.5f);
        k.Gild(wing);
        c.Glow(c.Stroke((4, 16.5), (11, 16.5), (12, 8), 1, 1), M.Accent, 1);
        var hull = c.Body((0.5, 0), (2, 4), (3.5, 10), (3.5, 19), (2.5, 22));
        c.Raise(hull, M.Hull, 4.5f, 1.5f);
        k.Gild(hull);
        c.Raise(c.Rect(-0.5, 11, 0.5, 19), M.Trim, 5.5f, 1);
        k.Canopy(6.5, 1.5, 2.5);
        k.Bell(5.5, 22, 1.5);
        return c;
    }

    /// <summary>Bomber: a heavy hull crowned with three points at its bow, a bomb pod on each flank, its bomb racks lit.</summary>
    static Canvas Bomber()
    {
        var c = new Canvas(35, 37);
        var k = new ErrantKit(c);
        c.Raise(c.Rect(8, 16, 13, 23), M.Plate, 3, 1);
        var pods = c.Pair((13.5, 8), (16, 14), (16, 30), (14.5, 33), (11.5, 33), (11, 14));
        c.Raise(pods, M.Hull, 3.5f, 1.5f);
        k.Gild(pods);
        c.Glow(c.Rect(13, 9, 14, 10), M.Light, 2);
        c.Glow(c.Rect(14.5, 16, 15.5, 28), M.Accent, 1);
        var hull = c.Body((0.5, 0), (2.5, 6), (4, 9), (6.5, 3), (8.5, 6), (10, 11), (10.5, 26), (8, 33), (3.5, 35));
        c.Raise(hull, M.Hull, 3, 1.5f);
        k.Gild(hull);
        var deck = c.Body((0.5, 5), (3.5, 12), (6, 15), (6, 28), (3, 32));
        c.Raise(deck, M.Hull, 5, 1.2f);
        k.Gild(deck);
        // The bomb racks: two lit slots sunk in the deck.
        c.Carve(c.Rect(1, 18, 4, 28), 1.5f);
        c.Glow(c.Rect(2, 19, 3, 27), M.Accent, 1);
        c.Glow(c.Rect(2, 19, 3, 20), M.Accent, 2);
        k.Canopy(11.5, 2.5, 3);
        k.Bell(5, 35, 2);
        k.Bell(13.5, 33, 1.5);
        return c;
    }

    /// <summary>Support: a gold halo round its bow, turquoise beacons on it, a healing light at its heart.</summary>
    static Canvas Support()
    {
        var c = new Canvas(37, 39);
        var k = new ErrantKit(c);
        var pods = c.Pair((8.5, 20), (11, 24), (11, 34), (6, 34), (6, 24));
        c.Raise(pods, M.Hull, 3, 1.2f);
        k.Gild(pods);
        var hull = c.Body((0.5, 0), (2.5, 4), (4.5, 10), (5.5, 18), (6, 30), (4, 37));
        c.Raise(hull, M.Hull, 4, 1.5f);
        c.Inset(hull, 1.5f, 0.9f);
        k.Gild(hull);
        var halo = Canvas.Minus(c.Oval(0, 13, 15, 10), c.Oval(0, 13, 12, 7));
        c.Raise(halo, M.Trim, 6, 1.2f);
        foreach (var (dx, y) in new[] { (15, 13), (10.5, 5.5), (10.5, 20.5) }) c.Glow(c.Rect(dx - 0.5, y - 0.5, dx + 0.5, y + 0.5), M.Light, 2);
        c.Raise(c.Oval(0, 25, 3.5, 3.5), M.Trim, 5, 1);
        c.Glow(c.Oval(0, 25, 2.5, 2.5), M.Accent, 1);
        c.Glow(c.Oval(0, 24.5, 1, 1), M.Light, 2);
        k.Canopy(9, 1.5, 3);
        k.Bell(8.5, 34, 1.5);
        k.Bell(0, 37, 2);
        return c;
    }

    /// <summary>Torpedo boat: an archer's bow drawn across an arrow of a hull, a torpedo tube at each end, a string of light.</summary>
    static Canvas Torpedo()
    {
        var c = new Canvas(43, 31);
        var k = new ErrantKit(c);
        c.Glow(c.Line(4, 26.5, 16, 26.5), M.Accent, 0);
        var bow = c.Stroke((0, 7), (15, 4), (19, 20), 5, 2.5);
        c.Raise(bow, M.Hull, 3.5f, 1.5f);
        k.Gild(bow);
        c.Glow(c.Stroke((4, 7.5), (13.5, 5.5), (16.5, 16), 1, 1), M.Accent, 1);
        // A tube at each end of the bow, its torpedo's head glowing in the muzzle.
        var tubes = c.Pair((19, 12), (21, 14), (21, 27), (17, 27), (17, 14));
        c.Raise(tubes, M.Hull, 4.5f, 1.2f);
        k.Gild(tubes);
        c.Raise(c.Rect(17.5, 12, 20.5, 15), M.Plate, 5, 1);
        c.Glow(c.Rect(18.5, 13, 19.5, 14), M.Accent, 2);
        var arrow = c.Body((0.5, 0), (2, 4), (3.5, 11), (3.5, 25), (2.5, 29));
        c.Raise(arrow, M.Hull, 4.5f, 1.5f);
        k.Gild(arrow);
        k.Keel(13, 26);
        k.Canopy(7, 1.5, 2.5);
        k.Bell(0, 30, 1.5);
        k.Bell(19, 28, 1.5);
        return c;
    }

    /// <summary>Frigate: a lance between two crescent horns, small crescent wings astern, its violet eye at the bow.</summary>
    static Canvas Frigate()
    {
        var c = new Canvas(45, 53);
        var k = new ErrantKit(c);
        k.Wing(8, 34, 21, 12);
        k.Jaw(12, 2, 28, 3.5);
        k.Spine(1, 51, 10);
        k.Eye(9.5, 2);
        k.Bell(5, 51, 2.5);
        return c;
    }

    /// <summary>Battlecruiser: a lance between horned pods, crescent wings astern, its violet eye at the bow, a crown astern.</summary>
    static Canvas Battlecruiser()
    {
        var c = new Canvas(85, 97);
        var k = new ErrantKit(c);
        k.Wing(24, 56, 41, 22);
        k.Pod(22, 30, 86, 7);
        k.Jaw(19, 0, 40, 5.5);
        k.Spine(3, 94, 13);
        k.Eye(17, 4);
        k.Crown(85, 6.5, 3, 4);
        k.Bell(6, 94, 3.5);
        k.Bell(22, 86, 2.5);
        return c;
    }

    /// <summary>Dreadnought: a great lance between two horns and long pods, crescent wings, its violet eye at the bow, a crown of five points astern.</summary>
    static Canvas Dreadnought()
    {
        var c = new Canvas(105, 127);
        var k = new ErrantKit(c);
        k.Wing(30, 76, 52, 30);
        k.Pod(28, 40, 114, 9);
        k.Jaw(25, 0, 52, 7);
        k.Spine(4, 124, 17);
        k.Eye(16, 4.5);
        k.Crown(113, 10, 5, 6);
        k.Bell(6, 124, 4);
        k.Bell(28, 114, 3);
        return c;
    }

    /// <summary>A forged ship: its hull, its engine flames (a strip), its destruction (a strip), frames as wide as the hull.</summary>
    public sealed record Sprites(Image Hull, Image Engine, int EngineFrames, Image Destruction, int DestructionFrames);

    /// <summary>The classes the packs lack, drawn here for every faction.</summary>
    static readonly ShipClass[] Capitals = [ShipClass.Bulwark, ShipClass.Carrier, ShipClass.Titan, ShipClass.Leviathan, ShipClass.Colossus];

    /// <summary>The classes drawn here for a faction: those the packs lack, and every one for the Errants, who have no pack.</summary>
    public static IReadOnlyList<ShipClass> Classes(Faction f) => f == Faction.Errant ? Ships.All : Capitals;

    /// <summary>
    /// The star lance from above: a round gun deck on four buttresses, an emitter on each
    /// corner of its plate ring, grooves running in to the lens at its heart.
    /// </summary>
    static Canvas Lance(Faction f)
    {
        var c = new Canvas(100, 100);
        var k = KitOf(f, c);
        const double y = 50, r = 40;
        foreach (var sy in new[] { -1.0, 1.0 })
        {
            c.Raise(c.Pair((5, y + sy * 5), (43, y + sy * 39), (38, y + sy * 46), (1, y + sy * 12)), M.Hull, 3, 2);
            c.Raise(c.Pair((12, y + sy * 2), (47, y + sy * 4), (47, y + sy * 10), (10, y + sy * 8)), M.Hull, 3, 2);
        }
        var deck = c.Oval(0, y, r, r);
        c.Raise(deck, M.Hull, 5, 3);
        c.Inset(deck, 3f);
        var ring = c.Oval(0, y, r * 0.66, r * 0.66);
        c.Raise(ring, M.Plate, 7, 2);
        c.Inset(ring, 2f, 1f);
        // Grooves from the rim in to the ring, trim along the straight ones.
        foreach (var sy in new[] { -1.0, 1.0 })
        {
            c.Carve(c.Line(r * 0.5, y + sy * r * 0.5, r * 0.66, y + sy * r * 0.66), 1.2f);
            c.Dye(c.Line(0, y + sy * r * 0.7, 0, y + sy * r * 0.95, 2), M.Trim);
        }
        c.Dye(c.Line(r * 0.7, y, r * 0.95, y, 2), M.Trim);
        foreach (var sy in new[] { -1.0, 1.0 }) k.Mount(r * 0.47, y + sy * r * 0.47, 6);
        c.Glow(c.Oval(0, y, r * 0.3, r * 0.3), M.Glass, 1);
        c.Glow(c.Oval(0, y, r * 0.16, r * 0.16), M.Core, 2);
        c.Glow(c.Oval(-r * 0.05, y - r * 0.07, r * 0.05, r * 0.05), M.Light, 2);
        return c;
    }

    /// <summary>The star lance's sprite in a faction's hand: one image, no engines.</summary>
    public static Image ForgeLance(Faction faction) => Paint(Lance(faction), faction);

    /// <summary>The sprites of a class for a faction; null for the classes the packs have.</summary>
    public static Sprites? Forge(ShipClass cls, Faction faction)
    {
        if (!Classes(faction).Contains(cls)) return null;
        var (canvas, shade) = cls switch
        {
            ShipClass.Scout => (Scout(), 0),
            ShipClass.Fighter => (Fighter(), 0),
            ShipClass.Bomber => (Bomber(), 0),
            ShipClass.Support => (Support(), 0),
            ShipClass.Torpedo => (Torpedo(), 0),
            ShipClass.Frigate => (Frigate(), 0),
            ShipClass.Battlecruiser => (Battlecruiser(), 0),
            ShipClass.Dreadnought => (Dreadnought(), 0),
            ShipClass.Bulwark => (Bulwark(faction), 0),
            ShipClass.Carrier => (Carrier(faction), 0),
            ShipClass.Titan => (Titan(faction), 0),
            ShipClass.Leviathan => (Leviathan(faction), 1),
            ShipClass.Colossus => (Colossus(faction), 1),
            _ => throw new ArgumentOutOfRangeException(nameof(cls)),
        };
        Bases(canvas, KitOf(faction, canvas), cls);
        var hull = Paint(canvas, faction, shade);
        const int engineFrames = 6, deathFrames = 10;
        return new Sprites(hull, Flames(canvas, faction, engineFrames), engineFrames, Wreck(hull, (int)cls * 7 + (int)faction, deathFrames), deathFrames);
    }

    /// <summary>Engine flames under each nozzle, flickering longer and shorter over the frames.</summary>
    static Image Flames(Canvas c, Faction f, int frames)
    {
        var ramp = Colors(f)[M.Engine];
        var (w, h) = (c.W + 2, c.H + 2);
        var img = Image.CreateEmpty(w * frames, h, false, Image.Format.Rgba8);
        for (var i = 0; i < frames; i++)
        {
            var reach = 3 + 2 * Math.Sin(i * Math.Tau / frames) + (i % 2);
            foreach (var (dx, y, size) in c.Nozzles)
            {
                foreach (var x0 in new[] { c.Cx - dx, c.Cx + dx })
                {
                    for (var t = 0.0; t <= reach * size; t += 0.5)
                    {
                        var k = t / (reach * size);
                        var half = size * (1 - k);
                        var color = ramp[k < 0.3 ? 2 : k < 0.65 ? 1 : 0] with { A = (float)(1 - k * 0.6) };
                        for (var u = -half; u <= half; u += 0.5)
                        {
                            var (px, py) = ((int)Math.Round(x0 + u) + 1, (int)(y + t) + 1);
                            if (px >= 0 && py >= 0 && px < w && py < h) img.SetPixel(i * w + px, py, color);
                        }
                    }
                }
            }
        }
        return img;
    }

    /// <summary>
    /// The hull blowing apart: blasts go off one after another, the hull glows and falls away
    /// around them, fireballs bloom from white to dark red and thin out, a few embers last.
    /// </summary>
    static Image Wreck(Image hull, int seed, int frames)
    {
        var (w, h) = (hull.GetWidth(), hull.GetHeight());
        // Read and written as bytes: a call into the engine per pixel and frame costs more than all the rest.
        var src = hull.GetData();
        var outW = w * frames;
        var data = new byte[outW * h * 4];
        Color At(int x, int y)
        {
            var o = (y * w + x) * 4;
            return new Color(src[o] / 255f, src[o + 1] / 255f, src[o + 2] / 255f, src[o + 3] / 255f);
        }
        void Put(int x, int y, Color c)
        {
            var o = (y * outW + x) * 4;
            data[o] = (byte)Math.Clamp(Math.Round(c.R * 255), 0, 255);
            data[o + 1] = (byte)Math.Clamp(Math.Round(c.G * 255), 0, 255);
            data[o + 2] = (byte)Math.Clamp(Math.Round(c.B * 255), 0, 255);
            data[o + 3] = (byte)Math.Clamp(Math.Round(c.A * 255), 0, 255);
        }
        var rand = new Random(seed);
        var fire = new[] { new Color(1, 1, 0.92f), new Color(1, 0.88f, 0.4f), new Color(1, 0.6f, 0.2f), new Color(0.86f, 0.28f, 0.12f), new Color(0.45f, 0.12f, 0.1f) };
        var hot = new Color(1, 0.55f, 0.2f);
        var size = Math.Max(w, h);
        var blasts = Enumerable.Range(0, 6 + w * h / 2500).Select(_ =>
        {
            int x, y;
            do (x, y) = (rand.Next(w), rand.Next(h)); while (src[(y * w + x) * 4 + 3] == 0);
            return (X: x, Y: y, Start: rand.NextDouble() * 0.45, R: size * (0.08 + rand.NextDouble() * 0.12));
        }).ToList();
        // When each hull pixel falls away: soon near a blast, late far from all of them.
        var death = new double[w * h];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var first = double.MaxValue;
                foreach (var b in blasts) first = Math.Min(first, b.Start + Math.Sqrt((x - b.X) * (x - b.X) + (y - b.Y) * (y - b.Y)) / size * 1.1);
                death[y * w + x] = first + rand.NextDouble() * 0.12;
            }
        }
        for (var i = 0; i < frames; i++)
        {
            var t = (double)(i + 1) / frames;
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    if (src[(y * w + x) * 4 + 3] == 0) continue;
                    var c = At(x, y);
                    var d = death[y * w + x];
                    if (t >= d)
                    {
                        // A few embers stay a while where the hull was.
                        if (t - d < 0.2 && rand.NextDouble() < 0.06) Put(i * w + x, y, fire[4]);
                        continue;
                    }
                    Put(i * w + x, y, t > d - 0.14 ? c.Lerp(hot, (float)((t - d + 0.14) / 0.14)) : c);
                }
            }
            foreach (var b in blasts)
            {
                var age = (t - b.Start) / 0.5;
                if (age < 0 || age > 1) continue;
                var r = b.R * (0.45 + 0.75 * Math.Sqrt(age));
                for (var y = (int)(b.Y - r * 1.3); y <= b.Y + r * 1.3; y++)
                {
                    for (var x = (int)(b.X - r * 1.3); x <= b.X + r * 1.3; x++)
                    {
                        if (x < 0 || y < 0 || x >= w || y >= h) continue;
                        // A ragged edge, and a fireball that thins out as it ages.
                        var a = Math.Atan2(y - b.Y, x - b.X);
                        var reach = r * (1 + 0.22 * Math.Sin(a * 5 + b.X) + 0.12 * Math.Sin(a * 9 + b.Y));
                        var d = Math.Sqrt((x - b.X) * (x - b.X) + (y - b.Y) * (y - b.Y)) / reach;
                        if (d > 1 || rand.NextDouble() < age * age * 0.7) continue;
                        Put(i * w + x, y, fire[Math.Clamp((int)(d * 2.2 + age * 2.6), 0, fire.Length - 1)]);
                    }
                }
            }
        }
        return Image.CreateFromData(outW, h, false, Image.Format.Rgba8, data);
    }
}
