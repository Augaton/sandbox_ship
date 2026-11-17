// The reserve on the battlefield: the jumps under way, marked for all (the ship's outline, a
// tunnel of light along its course, its hologram flickering into being), the ghost of a ship
// being called in with where it may come, and its coming out of hyperspace: racing in from
// behind its point, stretched, braking hard, streaks of light left behind it, a flash and a
// shockwave; a capital with a horn and the screen shaking.
using Godot;
using SandboxShip.Core;
using SandboxShip.Render;
using SandboxShip.Sim;
using SandboxShip.Sim.Data;
using SandboxShip.Ui;

namespace SandboxShip.Battle;

public partial class BattleScreen
{
    /// <summary>How long a ship takes to come in from hyperspace to its point; a capital, longer.</summary>
    const double JumpArrivalMs = 520, CapitalArrivalMs = 1100;
    static readonly Color HyperLight = Palette.Hex(0x9fd4ff), HyperWhite = Palette.Hex(0xe8f6ff);

    static double GirthOf(ShipDef def) => def.Beam > 0 ? def.Beam : def.Radius;

    /// <summary>
    /// How a ship just out of hyperspace shows: still racing in from behind its point, stretched
    /// along its course, fading in; null once there (its arrival is then forgotten).
    /// </summary>
    (Vector2 Offset, float Stretch, float Alpha)? Arriving(Entity ship)
    {
        if (!arrivals.TryGetValue(ship.Id, out var came)) return null;
        var capital = Reserves.Capital(ship.Cls);
        var k = (Now - came) / (capital ? CapitalArrivalMs : JumpArrivalMs);
        if (k >= 1)
        {
            arrivals.Remove(ship.Id);
            return null;
        }
        // Fast, then braking hard: most of the way in the first moments.
        var ease = 1 - Math.Pow(1 - Math.Max(0, k), 4);
        var back = (1 - ease) * (capital ? 1100 : 700);
        var offset = new Vector2((float)(-Math.Cos(ship.Angle) * back), (float)(-Math.Sin(ship.Angle) * back));
        return (offset, (float)(1 + 2.5 * (1 - ease)), (float)Math.Min(1, 0.3 + 1.5 * k));
    }

    /// <summary>A ship of the reserve out of hyperspace: its race in, streaks of light, a flash, the shockwave; a capital's horn.</summary>
    void Hyperspace(Entity e)
    {
        arrivals[e.Id] = Now;
        var capital = Reserves.Capital(e.Cls);
        var (x, y, z) = (e.X, e.Y, Zoom);
        var (c, s) = (Math.Cos(e.Angle), Math.Sin(e.Angle));
        var girth = e.Girth;
        var color = Palette.Of(World, e.Side);
        // Streaks of light left along its course, drawing in after it and fading.
        var length = capital ? 1700 : 1000;
        Add(fxList, capital ? 1000 : 650, (g, k) =>
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
        Boom(x, y, girth * 2.6 / 64, 1.7, capital ? 1100 : 700, 0.8f);
        Ripple(x, y, girth + Reserves.ShockReach, HyperLight);
        if (capital) Ripple(x, y, (girth + Reserves.ShockReach) * 1.7, color);
        var mine = e.Side == Opts.Player;
        var heard = mine || OnScreen(x, y);
        if (heard) Sfx.Play(Sound.Warp, 0, capital ? 1 : 0.8);
        if (!capital) return;
        Shake(700, e.Cls == ShipClass.Titan ? 0.005 : 0.009);
        // The horn carries: one's own is always heard, an enemy's when in sight.
        if (heard) Sfx.Play(Sound.Horn, 0, 1);
        if (mine) Hud.Toast($"{Ships.Defs[e.Cls].Name} en service");
        else if (CanSee(e)) Hud.Toast($"Un {Ships.Defs[e.Cls].Name} ennemi sort de l'hyperespace");
    }

    /// <summary>
    /// The jumps under way, whoever is coming and wherever: the ship's outline breathing faster
    /// as it comes, a tunnel of light along its course, its hologram flickering into being; a
    /// capital's rings turning and a light scanning it from bow to stern. One's own shows the
    /// seconds left.
    /// </summary>
    void DrawJumps(CanvasItem g)
    {
        if (World.Jumps.Count == 0) return;
        var z = Zoom;
        var now = Now / 1000;
        var rect = ViewRect().Grow(600);
        foreach (var j in World.Jumps)
        {
            if (!rect.HasPoint(new Vector2((float)j.X, (float)j.Y))) continue;
            var def = Ships.Defs[j.Cls];
            var girth = GirthOf(def);
            var t = Math.Clamp(1 - j.Left / j.Total, 0, 1);
            var capital = Reserves.Capital(j.Cls);
            var color = Palette.Of(World, j.Side);
            var pulse = 0.5 + 0.5 * Math.Sin(now * (4 + 10 * t) + j.Id);
            var (c, s) = (Math.Cos(j.Angle), Math.Sin(j.Angle));
            // The tunnel: light along its course, closing in on the point.
            var reach = (capital ? 1500 : 900) * (1 - 0.6 * t);
            for (var i = -1; i <= 1; i++)
            {
                var off = i * girth * 0.6;
                var (px, py) = (-s * off, c * off);
                Draw.Line(g, j.X + px - c * reach, j.Y + py - s * reach, j.X + px - c * girth, j.Y + py - s * girth,
                    HyperLight with { A = (float)((0.06 + 0.3 * t) * (i == 0 ? 1 : 0.6)) }, Math.Max(1 / z, girth * 0.05 * (1 + t)));
            }
            // The outline of what comes, in its side's color.
            var outline = color with { A = (float)(0.3 + 0.45 * pulse * (0.4 + 0.6 * t)) };
            if (def.Beam > 0) Draw.Capsule(g, j.X, j.Y, j.Angle, def.Radius - def.Beam, def.Beam + 8 / z, outline, 2 / z, z);
            else Draw.Ring(g, j.X, j.Y, girth * 1.15, outline, 2 / z, z);
            if (capital)
            {
                for (var i = 0; i < 3; i++)
                {
                    var a0 = now * 1.2 + i * Math.Tau / 3;
                    Draw.Arc(g, j.X, j.Y, def.Radius * 1.25, a0, a0 + 1.2, 24, HyperLight with { A = (float)(0.25 + 0.5 * t) }, 3 / z);
                }
            }
            // Its hologram over a glowing field, fainter far from coming, flickering.
            var look = Assets.ShipLook(Worlds.RaceOf(World, j.Side), j.Cls);
            var layer = look.Layers[0];
            var scale = def.Radius * 2 * layer.Size / Assets.OpaqueSize(layer.Key);
            var flicker = 0.75 + 0.25 * Math.Sin(now * 23 + j.Id * 3.1);
            var field = HyperLight with { A = (float)((0.06 + 0.16 * t) * flicker) };
            if (def.Beam > 0) Draw.CapsuleDisc(g, j.X, j.Y, j.Angle, def.Radius - def.Beam, def.Beam, field, z);
            else Draw.Disc(g, j.X, j.Y, girth, field, z);
            Draw.Sprite(g, Assets.Frame(layer.Key), j.X, j.Y, j.Angle + Facing, scale, new Color(0.8f, 0.95f, 1, (float)((0.15 + 0.5 * t) * flicker)));
            if (capital)
            {
                // A light scanning it from bow to stern, again and again.
                var along = def.Radius * (1 - 2 * (now * 0.7 % 1));
                var (sx, sy) = (j.X + c * along, j.Y + s * along);
                Draw.Line(g, sx - s * girth * 1.3, sy + c * girth * 1.3, sx + s * girth * 1.3, sy - c * girth * 1.3, HyperWhite with { A = (float)(0.3 + 0.4 * t) }, 2 / z);
            }
            if (j.Side == Opts.Player) Draw.Text(g, UiTheme.Mono, $"{Math.Ceiling(j.Left)}", j.X, j.Y - girth - 22 / z, 13, HyperWhite, z);
        }
    }

    /// <summary>
    /// The ship being called in: where its side may call it (round its structures and warships,
    /// in blue), the foes' structures it must keep away from (in red, those in view), and its
    /// outline under the pointer, facing the way it would come, green or red with the reason.
    /// </summary>
    void DrawDeployGhost(CanvasItem g, double z)
    {
        if (Controls.DeployGhost() is not { } ghost) return;
        var rect = ViewRect();
        foreach (var zone in Reserves.Zones(World, Opts.Player))
        {
            if (!rect.Grow((float)zone.R).HasPoint(new Vector2((float)zone.X, (float)zone.Y))) continue;
            Draw.Ring(g, zone.X, zone.Y, zone.R, HyperLight with { A = 0.28f }, 1.5 / z, z);
        }
        foreach (var e in World.Entities)
        {
            if (e.Kind != EntityKind.Structure || !Worlds.Foes(World, e.Side, Opts.Player) || !CanSee(e) && !Vision.WasSeen(World, Opts.Player, e)) continue;
            var r = e.Radius + Reserves.EnemyClearance;
            if (!rect.Grow((float)r).HasPoint(new Vector2((float)e.X, (float)e.Y))) continue;
            Draw.Ring(g, e.X, e.Y, r, Palette.Hex(0xe05a4a, 0.3f), 1.5 / z, z);
        }
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
