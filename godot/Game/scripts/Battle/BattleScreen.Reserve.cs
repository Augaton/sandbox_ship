// The reserve on the battlefield: the jumps under way, marked for all (the ship's outline, a
// tunnel of light along its course, its hologram flickering into being), the ghost of a ship
// being called in, and its coming out of hyperspace: racing in from behind its point,
// stretched, braking hard, streaks of light left behind it, a flash and a shockwave. A capital
// comes another way: a storm gathers at its point, lightning running through it, and the ship
// slides slowly out of it, bow first, lightning crawling on its hull, to a horn and thunder.
using Godot;
using SandboxShip.Core;
using SandboxShip.Render;
using SandboxShip.Sim;
using SandboxShip.Sim.Data;
using SandboxShip.Ui;

namespace SandboxShip.Battle;

public partial class BattleScreen
{
    /// <summary>How long a ship takes to come in from hyperspace to its point; a capital, out of its storm.</summary>
    const double JumpArrivalMs = 520, CapitalEmergeMs = 4500;
    /// <summary>How long a capital's storm takes to clear once the ship is out of it.</summary>
    const double StormClearMs = 6000;
    static readonly Color HyperLight = Palette.Hex(0x9fd4ff), HyperWhite = Palette.Hex(0xe8f6ff);
    static readonly Color StormDark = Palette.Hex(0x2b3354), StormViolet = Palette.Hex(0x4a3d78), StormLit = Palette.Hex(0x9fb4ff), StormShade = Palette.Hex(0x05060c);

    /// <summary>A capital's storm, from the moment its ship came out: where, which way, how big, and the jump it was (the shape of its clouds).</summary>
    sealed record Storm(int Seed, double X, double Y, double Angle, double Half, double Girth, double Out);
    readonly List<Storm> storms = [];
    /// <summary>The capitals' jumps seen at the last frame, for their storm to go on where the ship comes out; and those whose thunder was heard.</summary>
    readonly List<Jump> stormsGathering = [];
    readonly HashSet<int> stormsHeard = [];

    static double GirthOf(ShipDef def) => def.Beam > 0 ? def.Beam : def.Radius;

    /// <summary>
    /// How a ship just out of hyperspace shows: still racing in from behind its point, stretched
    /// along its course, fading in; null once there (its arrival is then forgotten).
    /// </summary>
    (Vector2 Offset, float Stretch, float Alpha)? Arriving(Entity ship)
    {
        if (!arrivals.TryGetValue(ship.Id, out var came)) return null;
        var capital = Reserves.Capital(ship.Cls);
        var k = (Now - came) / (capital ? CapitalEmergeMs : JumpArrivalMs);
        if (k >= 1)
        {
            arrivals.Remove(ship.Id);
            return null;
        }
        if (capital)
        {
            // Out of its storm, slowly: bow first, the last of the way the slowest, no stretch.
            var out_ = Math.Sin(Math.Max(0, k) * Math.PI / 2);
            var behind = (1 - out_) * Ships.Defs[ship.Cls].Radius * 1.1;
            return (new Vector2((float)(-Math.Cos(ship.Angle) * behind), (float)(-Math.Sin(ship.Angle) * behind)), 1, (float)Math.Min(1, 0.1 + 1.4 * k));
        }
        // Fast, then braking hard: most of the way in the first moments.
        var ease = 1 - Math.Pow(1 - Math.Max(0, k), 4);
        var back = (1 - ease) * (capital ? 1100 : ship.Size == SizeClass.Small ? 300 : 700);
        var offset = new Vector2((float)(-Math.Cos(ship.Angle) * back), (float)(-Math.Sin(ship.Angle) * back));
        return (offset, (float)(1 + 2.5 * (1 - ease)), (float)Math.Min(1, 0.3 + 1.5 * k));
    }

    /// <summary>A ship of the reserve out of hyperspace: its race in, streaks of light, a flash, a large hull's shockwave; a capital out of its storm.</summary>
    void Hyperspace(Entity e)
    {
        arrivals[e.Id] = Now;
        if (Reserves.Capital(e.Cls))
        {
            OutOfStorm(e);
            return;
        }
        // Small craft come by squadrons and often: a lighter flash, no shockwave, a softer sound.
        var small = e.Size == SizeClass.Small;
        var (x, y, z) = (e.X, e.Y, Zoom);
        var (c, s) = (Math.Cos(e.Angle), Math.Sin(e.Angle));
        var girth = e.Girth;
        // Streaks of light left along its course, drawing in after it and fading.
        var length = small ? 400 : 1000;
        Add(fxList, 650, (g, k) =>
        {
            var a = (float)(1 - k);
            for (var i = -2; i <= 2; i++)
            {
                var off = i * girth * 0.4;
                var (px, py) = (-s * off, c * off);
                var l = length * (1 - EaseOutCubic(k)) * (1 - Math.Abs(i) * 0.18);
                var width = Math.Max(1.5 / z, girth * (i == 0 ? 0.16 : 0.07)) * (0.4 + 0.6 * a);
                Draw.Line(g, x + px - c * (girth + l), y + py - s * (girth + l), x + px - c * girth * 0.4, y + py - s * girth * 0.4, (i == 0 ? HyperWhite : HyperLight) with { A = 0.75f * a }, width);
            }
        });
        // The flash where it breaks out, and the shockwave that throws the craft aside.
        Add(fxList, 260, (g, k) => Draw.Disc(g, x, y, girth * (1.2 + 1.5 * k), HyperWhite with { A = 0.55f * (float)(1 - k) }, z));
        if (!small)
        {
            Boom(x, y, girth * 2.6 / 64, 1.7, 700, 0.8f);
            if (e.Size == SizeClass.Large) Ripple(x, y, girth + Reserves.ShockReach, HyperLight);
        }
        var mine = e.Side == Opts.Player;
        var heard = mine || OnScreen(x, y);
        if (heard) Sfx.Play(Sound.Warp, 0, small ? 0.35 : 0.8);
    }

    /// <summary>
    /// A capital out of its storm: a flash through the clouds, thunder and the horn, the screen
    /// shaking long and low, the craft about it thrown aside, the storm left to clear.
    /// </summary>
    void OutOfStorm(Entity e)
    {
        var def = Ships.Defs[e.Cls];
        var (x, y) = (e.X, e.Y);
        // The storm of its jump goes on, cloud for cloud; one not seen gathering takes the ship's own.
        var jump = stormsGathering.Where(j => j.Side == e.Side && j.Cls == e.Cls).MinBy(j => Math.Abs(j.X - x) + Math.Abs(j.Y - y));
        var (seed, sx, sy, angle) = jump is not null && Math.Abs(jump.X - x) + Math.Abs(jump.Y - y) < 120 ? (jump.Id, jump.X, jump.Y, jump.Angle) : (e.Id, x, y, e.Angle);
        storms.Add(new Storm(seed, sx, sy, angle, def.Radius, GirthOf(def), Now));
        var big = Assets.Frame("ph-cloud");
        var reach = def.Radius * 2.6;
        Add(fxList, 500, (g, k) => Draw.Sprite(g, big, sx, sy, 0, reach * 2 / 96, HyperWhite with { A = 0.55f * (float)(1 - k) }));
        Ripple(x, y, e.Girth + Reserves.ShockReach, HyperLight);
        Ripple(x, y, (e.Girth + Reserves.ShockReach) * 1.7, Palette.Of(World, e.Side));
        var mine = e.Side == Opts.Player;
        var heard = mine || OnScreen(x, y);
        Shake(1800, e.Cls == ShipClass.Titan ? 0.004 : 0.007);
        if (heard)
        {
            Sfx.Play(Sound.BigExplosion, 0, 0.7, 0.45);
            Sfx.Play(Sound.Horn, 0, 1);
        }
        if (mine) Hud.Toast($"{def.Name} en service");
        else if (CanSee(e)) Hud.Toast($"Un {def.Name} ennemi sort de la tempête");
    }

    /// <summary>A number in [0, 1) from a few integers: the same every frame for the same ones.</summary>
    static double Hash(int a, int b, int c = 0)
    {
        unchecked
        {
            var h = (uint)a * 0x9E3779B1u ^ (uint)b * 0x85EBCA77u ^ (uint)c * 0xC2B2AE3Du;
            h ^= h >> 15;
            h *= 0x2C1B3C6Du;
            h ^= h >> 12;
            return (h & 0xFFFFFF) / (double)0x1000000;
        }
    }

    /// <summary>
    /// A capital's storm at (x, y), its ship heading `angle`: dark clouds turning slowly about a
    /// point a little behind it, `gather` of them come together (0 to 1), `fade` gone (0 to 1);
    /// lightning through them at `rate` (bolts lit at once, about), lighting the clouds round it,
    /// and, while `crawl` is on, lightning crawling on the ship's hull.
    /// </summary>
    void DrawStorm(CanvasItem g, int seed, double x, double y, double angle, double half, double girth, double gather, double fade, double rate, double crawl)
    {
        var z = Zoom;
        var now = Now / 1000;
        var cloud = Assets.Frame("ph-cloud");
        var (c, s) = (Math.Cos(angle), Math.Sin(angle));
        // Its middle a little behind the ship's: it comes out forward.
        var (mx, my) = (x - c * half * 0.35, y - s * half * 0.35);
        var (along, across) = (half * 1.3, girth * 1.8 + 60);
        var puff = Math.Max(girth * 1.3, 90);
        var alpha = (float)(1 - fade);
        // A soft shadow under it all: the stars behind go dark.
        var shade = StormShade with { A = (float)(0.4 * Math.Min(1, gather * 1.5)) * alpha };
        for (var u = -0.8; u <= 0.81; u += 0.4)
        {
            foreach (var v in new[] { -0.35, 0.35 })
                Draw.Sprite(g, cloud, mx + c * u * along - s * v * across, my + s * u * along + c * v * across, 0, puff * 2.6 * 2 / 96, shade);
        }
        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 0; i < 34; i++)
            {
                // Each cloud comes in its turn, and turns slowly round the middle.
                var come = Math.Clamp((gather - Hash(seed, i, 1) * 0.5) / 0.5, 0, 1);
                if (come <= 0) continue;
                var (u, v) = (Hash(seed, i, 2) * 2 - 1, Hash(seed, i, 3) * 2 - 1);
                var turn = now * (0.12 + 0.1 * Hash(seed, i, 4)) * (i % 2 == 0 ? 1 : -1) + Hash(seed, i, 5) * Math.Tau;
                var (du, dv) = (u * along + Math.Cos(turn) * puff * 0.25, v * across * Math.Sqrt(1 - u * u * 0.6) + Math.Sin(turn) * puff * 0.25);
                var (px, py) = (mx + c * du - s * dv, my + s * du + c * dv);
                var size = puff * (0.55 + 0.55 * Hash(seed, i, 6)) * (0.6 + 0.4 * come) * (1 + 0.5 * fade);
                if (pass == 0)
                {
                    var tint = StormDark.Lerp(StormViolet, (float)Hash(seed, i, 7));
                    Draw.Sprite(g, cloud, px, py, turn, size * 2 / 96, tint with { A = (float)(0.7 * come) * alpha });
                }
                else
                {
                    // Its crest lit from the top left, the way the light falls on the ships.
                    Draw.Sprite(g, cloud, px - size * 0.2, py - size * 0.2, turn, size * 1.2 / 96, StormLit with { A = (float)(0.12 * come) * alpha });
                }
            }
        }
        // Lightning: a few bolts at a time, each lit a short while, then elsewhere.
        for (var j = 0; j < 4; j++)
        {
            var span = 0.11 + 0.04 * j;
            var tick = (int)Math.Floor(now / span);
            if (Hash(seed, j * 977 + tick, 8) > rate * 0.4) continue;
            var (u0, v0, u1, v1) = (Hash(seed, tick, 9 + j) * 2 - 1, Hash(seed, tick, 13 + j) * 2 - 1, Hash(seed, tick, 17 + j) * 2 - 1, Hash(seed, tick, 21 + j) * 2 - 1);
            var a0 = (mx + c * u0 * along - s * v0 * across * 0.7, my + s * u0 * along + c * v0 * across * 0.7);
            var a1 = (mx + c * u1 * along * 0.6 - s * v1 * across * 0.7, my + s * u1 * along * 0.6 + c * v1 * across * 0.7);
            Bolt(g, a0, a1, seed + tick * 31 + j, alpha, z);
            var (fx, fy) = ((a0.Item1 + a1.Item1) / 2, (a0.Item2 + a1.Item2) / 2);
            Draw.Sprite(g, cloud, fx, fy, 0, puff * 4 / 96, StormLit with { A = 0.28f * alpha });
        }
        if (crawl <= 0) return;
        // On the hull: short arcs from one point of it to the next.
        for (var j = 0; j < 3; j++)
        {
            var tick = (int)Math.Floor(now / (0.08 + 0.03 * j));
            if (Hash(seed, j * 131 + tick, 30) > crawl) continue;
            var u = Hash(seed, tick, 31 + j) * 2 - 1;
            var v = Hash(seed, tick, 34 + j) * 2 - 1;
            var du = (Hash(seed, tick, 37 + j) - 0.5) * half * 0.5;
            var p0 = (x + c * u * half - s * v * girth * 0.8, y + s * u * half + c * v * girth * 0.8);
            var p1 = (p0.Item1 + c * du + s * girth * 0.5 * v, p0.Item2 + s * du - c * girth * 0.5 * v);
            Bolt(g, p0, p1, seed * 7 + tick * 13 + j, (float)crawl, z);
        }
    }

    /// <summary>A bolt of lightning from one point to another, jagged, a glow about its white core, with a fork.</summary>
    static void Bolt(CanvasItem g, (double X, double Y) from, (double X, double Y) to, int seed, float alpha, double z)
    {
        var (dx, dy) = (to.X - from.X, to.Y - from.Y);
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) return;
        var (nx, ny) = (-dy / len, dx / len);
        const int Steps = 8;
        Span<(double X, double Y)> pts = stackalloc (double, double)[Steps + 1];
        for (var i = 0; i <= Steps; i++)
        {
            var t = i / (double)Steps;
            var jag = i == 0 || i == Steps ? 0 : (Hash(seed, i, 50) - 0.5) * len * 0.22;
            pts[i] = (from.X + dx * t + nx * jag, from.Y + dy * t + ny * jag);
        }
        for (var i = 0; i < Steps; i++)
        {
            Draw.Line(g, pts[i].X, pts[i].Y, pts[i + 1].X, pts[i + 1].Y, HyperLight with { A = 0.3f * alpha }, 7 / z);
            Draw.Line(g, pts[i].X, pts[i].Y, pts[i + 1].X, pts[i + 1].Y, HyperWhite with { A = 0.95f * alpha }, 2 / z);
        }
        // A fork from the middle, shorter, thinner.
        var mid = pts[Steps / 2];
        var side = Hash(seed, 0, 51) < 0.5 ? -1 : 1;
        var fork = (mid.X + dx * 0.25 + nx * side * len * 0.2, mid.Y + dy * 0.25 + ny * side * len * 0.2);
        Draw.Line(g, mid.X, mid.Y, fork.Item1, fork.Item2, HyperWhite with { A = 0.7f * alpha }, 1.5 / z);
    }

    /// <summary>The storms of capitals already out, clearing; their lightning dies down, the last crawling on the hull.</summary>
    void DrawStorms(CanvasItem g)
    {
        var rect = ViewRect().Grow(1200);
        for (var i = 0; i < storms.Count; i++)
        {
            var st = storms[i];
            var since = Now - st.Out;
            if (since > StormClearMs)
            {
                storms.RemoveAt(i--);
                continue;
            }
            if (!rect.HasPoint(new Vector2((float)st.X, (float)st.Y))) continue;
            var k = since / StormClearMs;
            var emerging = Math.Clamp(1 - since / CapitalEmergeMs, 0, 1);
            DrawStorm(g, st.Seed, st.X, st.Y, st.Angle, st.Half, st.Girth, 1, k, 1.4 * emerging, emerging);
        }
    }

    /// <summary>
    /// The jumps under way, whoever is coming and wherever: the ship's outline breathing faster
    /// as it comes, a tunnel of light along its course, its hologram flickering into being. A
    /// capital's point gathers a storm instead, lightning running through it faster as it comes,
    /// its outline faint under the clouds, a roll of thunder as it begins. One's own shows the
    /// seconds left. Then the storms of the capitals already out.
    /// </summary>
    void DrawJumps(CanvasItem g)
    {
        stormsGathering.Clear();
        if (World.Jumps.Count > 0) DrawJumpPoints(g);
        if (storms.Count > 0) DrawStorms(g);
    }

    void DrawJumpPoints(CanvasItem g)
    {
        var z = Zoom;
        var now = Now / 1000;
        var rect = ViewRect().Grow(600);
        foreach (var j in World.Jumps)
        {
            var capital = Reserves.Capital(j.Cls);
            if (capital) stormsGathering.Add(j);
            if (!rect.Grow(capital ? 600 : 0).HasPoint(new Vector2((float)j.X, (float)j.Y))) continue;
            var def = Ships.Defs[j.Cls];
            var girth = GirthOf(def);
            var t = Math.Clamp(1 - j.Left / j.Total, 0, 1);
            var color = Palette.Of(World, j.Side);
            var pulse = 0.5 + 0.5 * Math.Sin(now * (4 + 10 * t) + j.Id);
            var (c, s) = (Math.Cos(j.Angle), Math.Sin(j.Angle));
            var outline = color with { A = (float)(0.3 + 0.45 * pulse * (0.4 + 0.6 * t)) };
            if (capital)
            {
                if (stormsHeard.Add(j.Id) && (j.Side == Opts.Player || OnScreen(j.X, j.Y))) Sfx.Play(Sound.BigExplosion, 0, 0.45, 0.35);
                DrawStorm(g, j.Id, j.X, j.Y, j.Angle, def.Radius, girth, Math.Min(1, t * 1.6), 0, 0.3 + 1.2 * t, 0);
                Draw.Capsule(g, j.X, j.Y, j.Angle, Math.Max(0, def.Radius - def.Beam), girth + 8 / z, outline with { A = outline.A * 0.5f }, 2 / z, z);
                if (j.Side == Opts.Player) Draw.Text(g, UiTheme.Mono, $"{Math.Ceiling(j.Left)}", j.X, j.Y - girth - 60 / z, 13, HyperWhite, z);
                continue;
            }
            // The tunnel: light along its course, closing in on the point.
            var reach = (def.Size == SizeClass.Small ? 400 : 900) * (1 - 0.6 * t);
            for (var i = -1; i <= 1; i++)
            {
                var off = i * girth * 0.6;
                var (px, py) = (-s * off, c * off);
                Draw.Line(g, j.X + px - c * reach, j.Y + py - s * reach, j.X + px - c * girth, j.Y + py - s * girth,
                    HyperLight with { A = (float)((0.06 + 0.3 * t) * (i == 0 ? 1 : 0.6)) }, Math.Max(1 / z, girth * 0.05 * (1 + t)));
            }
            // The outline of what comes, in its side's color.
            if (def.Beam > 0) Draw.Capsule(g, j.X, j.Y, j.Angle, def.Radius - def.Beam, def.Beam + 8 / z, outline, 2 / z, z);
            else Draw.Ring(g, j.X, j.Y, girth * 1.15, outline, 2 / z, z);
            // Its hologram over a glowing field, fainter far from coming, flickering.
            var look = Assets.ShipLook(Worlds.RaceOf(World, j.Side), j.Cls);
            var layer = look.Layers[0];
            var scale = def.Radius * 2 * layer.Size / Assets.OpaqueSize(layer.Key);
            var flicker = 0.75 + 0.25 * Math.Sin(now * 23 + j.Id * 3.1);
            var field = HyperLight with { A = (float)((0.06 + 0.16 * t) * flicker) };
            if (def.Beam > 0) Draw.CapsuleDisc(g, j.X, j.Y, j.Angle, def.Radius - def.Beam, def.Beam, field, z);
            else Draw.Disc(g, j.X, j.Y, girth, field, z);
            Draw.Sprite(g, Assets.Frame(layer.Key), j.X, j.Y, j.Angle + Facing, scale, new Color(0.8f, 0.95f, 1, (float)((0.15 + 0.5 * t) * flicker)));
            if (j.Side == Opts.Player) Draw.Text(g, UiTheme.Mono, $"{Math.Ceiling(j.Left)}", j.X, j.Y - girth - 22 / z, 13, HyperWhite, z);
        }
    }

    /// <summary>
    /// The ship being called in: its outline under the pointer, facing the way it would come,
    /// green or red with the reason. No circles for where it may come: the outline says it.
    /// </summary>
    void DrawDeployGhost(CanvasItem g, double z)
    {
        if (Controls.DeployGhost() is not { } ghost) return;
        var def = Ships.Defs[ghost.Ship.Cls];
        var (x, y, angle) = (ghost.X, ghost.Y, ghost.Angle);
        var ok = ghost.Check.Ok;
        var tint = ok ? Palette.Hex(0x6fd08c) : Palette.Hex(0xe05a4a);
        var look = Assets.ShipLook(Worlds.RaceOf(World, Opts.Player), ghost.Ship.Cls);
        var layer = look.Layers[0];
        Draw.Sprite(g, Assets.Frame(layer.Key), x, y, angle + Facing, def.Radius * 2 * layer.Size / Assets.OpaqueSize(layer.Key), tint with { A = 0.45f });
        if (def.Beam > 0) Draw.Capsule(g, x, y, angle, def.Radius - def.Beam, def.Beam + 6 / z, tint with { A = 0.9f }, 2 / z, z);
        else Draw.Ring(g, x, y, def.Radius * 1.1, tint with { A = 0.9f }, 2 / z, z);
        // Which way it will face, coming from home.
        var (c, s) = (Math.Cos(angle), Math.Sin(angle));
        var tip = def.Radius * 1.25 + 30 / z;
        Draw.Line(g, x + c * def.Radius * 1.1, y + s * def.Radius * 1.1, x + c * tip, y + s * tip, tint with { A = 0.9f }, 2 / z);
        if (!ok) Draw.Text(g, UiTheme.Mono, ghost.Check.Reason, x, y + GirthOf(def) + 26 / z, 13, tint, z);
    }
}
