// The battle's terrain on screen (Sim/Terrain.cs): nebulae as drifts of cloud, belts as packed
// rocks, minefields as blinking mines, gravity wells as a dark heart and a swirl. What never
// moves is drawn once, on two canvases (the clouds smoothed, the rocks left crisp), again only if
// the terrain changes (a replica's arrives with its first whole snapshot); the mines and the
// swirl, on a small layer drawn every frame, only where the view is. All of it lies under the fog
// of war, dimmed there; thin outlines over the fog keep each piece's shape readable everywhere,
// as the minimap shows it, at the same width on screen whatever the zoom. Not in the web version.
using Godot;
using SandboxShip.Core;
using SandboxShip.Render;
using SandboxShip.Sim;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Battle;

public partial class BattleScreen
{
    public static readonly Color NebulaTint = Palette.Hex(0x9a78e6), NebulaEdge = Palette.Hex(0x4fb8cc);
    public static readonly Color RockTint = Palette.Hex(0x8a7f70), WellTint = Palette.Hex(0x6fa8ff);

    DrawCanvas clouds = null!, rocks = null!, terrainLive = null!, terrainMarks = null!;
    /// <summary>The terrain the static canvases show: drawn again when the world's changes.</summary>
    int terrainDrawn = -1;

    void BuildTerrainView()
    {
        clouds = new DrawCanvas { ZIndex = Depth.Z(Depth.Field) - 2, TextureFilter = CanvasItem.TextureFilterEnum.Linear, OnDraw = PaintClouds };
        rocks = new DrawCanvas { ZIndex = Depth.Z(Depth.Field) - 1, OnDraw = PaintRocks };
        worldRoot.AddChild(clouds);
        worldRoot.AddChild(rocks);
        terrainLive = Layer(Depth.Field + 0.5, "terrain", PaintTerrainLive);
        terrainMarks = Layer(Depth.Fog + 0.5, "terrain", PaintTerrainMarks);
    }

    void RedrawTerrain()
    {
        if (World.Terrain.Count > 0)
        {
            terrainLive.QueueRedraw();
            terrainMarks.QueueRedraw();
        }
        var signature = World.Terrain.Count == 0 ? 0 : World.Terrain.Count * 31 + World.Terrain[^1].Id;
        if (signature == terrainDrawn) return;
        terrainDrawn = signature;
        clouds.QueueRedraw();
        rocks.QueueRedraw();
    }

    /// <summary>The draws of a piece's look: its own, from its id, the same at every drawing.</summary>
    static Func<double> Draws(TerrainPatch p)
    {
        long seed = 7919L * p.Id + 17;
        return () => (seed = seed * 16807 % 2147483647) / 2147483647.0;
    }

    void PaintClouds(DrawCanvas g)
    {
        var cloud = Assets.Frame("ph-cloud");
        var size = cloud.GetWidth();
        foreach (var p in World.Terrain)
        {
            var rnd = Draws(p);
            switch (p.Kind)
            {
                case TerrainKind.Nebula:
                    // Overlapping drifts, denser toward the middle, violet shading to teal; few and
                    // broad, each a screenful of blending for the graphics card.
                    var n = (int)Math.Clamp(p.Radius / 28, 10, 22);
                    for (var i = 0; i < n; i++)
                    {
                        var a = rnd() * Math.Tau;
                        var d = Math.Sqrt(rnd()) * p.Radius * 0.65;
                        var r = p.Radius * (0.34 + rnd() * 0.3);
                        var tint = NebulaTint.Lerp(NebulaEdge, (float)rnd()) with { A = (float)(0.1 + rnd() * 0.08) };
                        Draw.Sprite(g, cloud, p.X + Math.Cos(a) * d, p.Y + Math.Sin(a) * d, rnd() * Math.Tau, r * 2 / size, tint);
                    }
                    break;
                case TerrainKind.Belt:
                    // A haze of dust along the band, under its rocks.
                    var (len, ux, uy) = Spine(p);
                    for (var t = 0.0; t <= len; t += p.Radius)
                    {
                        Draw.Sprite(g, cloud, p.X + ux * t, p.Y + uy * t, 0, p.Radius * 2.6 / size, RockTint with { A = 0.13f });
                    }
                    break;
                case TerrainKind.Well:
                    Draw.Sprite(g, cloud, p.X, p.Y, 0, p.Radius * 2.3 / size, WellTint with { A = 0.22f });
                    Draw.Sprite(g, cloud, p.X, p.Y, 0, p.Radius * 0.9 / size, Palette.Hex(0x05070d, 0.95f));
                    break;
            }
        }
    }

    void PaintRocks(DrawCanvas g)
    {
        var keys = Assets.Asteroids;
        // The rocks of every band, then drawn texture by texture: the renderer sends each run in one call.
        var laid = new List<(int Key, double X, double Y, double Rotation, double Size, float Shade)>();
        foreach (var p in World.Terrain)
        {
            switch (p.Kind)
            {
                case TerrainKind.Belt when keys.Count > 0:
                    var rnd = Draws(p);
                    var (len, ux, uy) = Spine(p);
                    var count = (int)Math.Round((len + 2 * p.Radius) * 2 * p.Radius / 1000);
                    for (var i = 0; i < count; i++)
                    {
                        // Along the band and across it, kept inside its rounded ends.
                        var t = -p.Radius + rnd() * (len + 2 * p.Radius);
                        var s = (rnd() * 2 - 1) * p.Radius * 0.95;
                        var (x, y) = (p.X + ux * t - uy * s, p.Y + uy * t + ux * s);
                        var big = rnd() < 0.12;
                        var shade = (float)(0.6 + rnd() * 0.4);
                        if (p.Distance(x, y) > p.Radius * 0.95) continue;
                        laid.Add(((int)(rnd() * keys.Count) % keys.Count, x, y, rnd() * Math.Tau, big ? 48 + rnd() * 26 : 16 + rnd() * 26, shade));
                    }
                    break;
            }
        }
        foreach (var rock in laid.OrderBy(r => r.Key))
        {
            var key = keys[rock.Key];
            Draw.Sprite(g, Assets.Frame(key), rock.X, rock.Y, rock.Rotation, rock.Size / Assets.OpaqueSize(key), new Color(rock.Shade, rock.Shade * 0.97f, rock.Shade * 0.94f));
        }
    }

    /// <summary>A band's length and the way it runs, from its first end.</summary>
    static (double Length, double Ux, double Uy) Spine(TerrainPatch p)
    {
        var len = Hypot(p.X2 - p.X, p.Y2 - p.Y);
        return len > 0 ? (len, (p.X2 - p.X) / len, (p.Y2 - p.Y) / len) : (0, 1, 0);
    }

    /// <summary>
    /// Where the i-th mine of a field lies, spread evenly from the middle out: those still armed
    /// are the first ones, so a field is cleared from its rim in.
    /// </summary>
    public static (double X, double Y) MineAt(TerrainPatch p, int i)
    {
        var r = p.Radius * 0.88 * Math.Sqrt((i + 0.5) / Math.Max(1, p.MaxMines));
        var a = i * 2.39996 + p.Id;
        return (p.X + Math.Cos(a) * r, p.Y + Math.Sin(a) * r);
    }

    void PaintTerrainLive(DrawCanvas g)
    {
        if (World.Terrain.Count == 0) return;
        var view = ViewRect().Grow(80);
        var now = Now / 1000;
        var z = Zoom;
        foreach (var p in World.Terrain)
        {
            if (p.Kind is not (TerrainKind.Minefield or TerrainKind.Well)) continue;
            if (!view.Intersects(new Rect2((float)(p.X - p.Radius), (float)(p.Y - p.Radius), (float)(2 * p.Radius), (float)(2 * p.Radius)))) continue;
            if (p.Kind == TerrainKind.Minefield)
            {
                // A red wash while it is armed.
                if (p.Armed) Draw.Disc(g, p.X, p.Y, p.Radius, Palette.Danger with { A = 0.05f }, z);
                for (var i = 0; i < p.Mines; i++)
                {
                    var (x, y) = MineAt(p, i);
                    // Two pixels on screen at least: a field zoomed out still shows its mines.
                    var r = Math.Max(11, 2 / z);
                    // Each blinks on its own beat, a red glow around it while lit.
                    var blink = 0.5 + 0.5 * Math.Sin(now * 3.2 + i * 1.7);
                    if (blink > 0.55) Draw.Disc(g, x, y, r * 2.2, Palette.Danger with { A = (float)(0.22 * blink) }, z);
                    Draw.Disc(g, x, y, r, Palette.Hex(0x262b36), z);
                    Draw.Ring(g, x, y, r, Palette.Hex(0x6b7486), 2 / Math.Min(1, z), z);
                    if (blink > 0.55) Draw.Disc(g, x, y, r * 0.45, Palette.Danger with { A = (float)blink }, z);
                }
            }
            else if (p.Kind == TerrainKind.Well)
            {
                // Arms of the swirl, faster toward the heart.
                for (var k = 0; k < 6; k++)
                {
                    var r = p.Radius * (0.24 + 0.13 * k);
                    var from = -now * (1.5 / (0.5 + 0.35 * k)) + k * 1.1;
                    var color = WellTint with { A = 0.55f - 0.07f * k };
                    Draw.Arc(g, p.X, p.Y, r, from, from + 1.3, 14, color, 2.5 / z);
                    Draw.Arc(g, p.X, p.Y, r, from + Math.PI, from + Math.PI + 1.3, 14, color, 2.5 / z);
                }
            }
        }
    }

    /// <summary>
    /// Over the fog: each piece's outline, faint, a pixel and a half wide on screen. A belt's
    /// band, a field's dashed ring (fainter once cleared), a well's rim, a nebula's edge.
    /// </summary>
    void PaintTerrainMarks(DrawCanvas g)
    {
        if (World.Terrain.Count == 0) return;
        var view = ViewRect().Grow(80);
        var z = Zoom;
        var width = 1.5 / z;
        foreach (var p in World.Terrain)
        {
            var (x0, y0) = (Math.Min(p.X, p.X2) - p.Radius, Math.Min(p.Y, p.Y2) - p.Radius);
            var box = new Rect2((float)x0, (float)y0, (float)(Math.Max(p.X, p.X2) + p.Radius - x0), (float)(Math.Max(p.Y, p.Y2) + p.Radius - y0));
            if (!view.Intersects(box)) continue;
            switch (p.Kind)
            {
                case TerrainKind.Belt:
                    var (_, ux, uy) = Spine(p);
                    var (nx, ny) = (-uy * p.Radius, ux * p.Radius);
                    var color = RockTint with { A = 0.35f };
                    Draw.Line(g, p.X + nx, p.Y + ny, p.X2 + nx, p.Y2 + ny, color, width);
                    Draw.Line(g, p.X - nx, p.Y - ny, p.X2 - nx, p.Y2 - ny, color, width);
                    var a = Math.Atan2(uy, ux);
                    Draw.Arc(g, p.X, p.Y, p.Radius, a + Math.PI / 2, a + Math.PI * 1.5, 12, color, width);
                    Draw.Arc(g, p.X2, p.Y2, p.Radius, a - Math.PI / 2, a + Math.PI / 2, 12, color, width);
                    break;
                case TerrainKind.Minefield:
                    const int dashes = 28;
                    var ring = Palette.Danger with { A = p.Armed ? 0.55f : 0.2f };
                    for (var i = 0; i < dashes; i++)
                    {
                        var from = i * Math.Tau / dashes;
                        Draw.Arc(g, p.X, p.Y, p.Radius, from, from + Math.Tau / dashes * 0.55, 4, ring, width);
                    }
                    break;
                case TerrainKind.Well:
                    Draw.Ring(g, p.X, p.Y, p.Radius, WellTint with { A = 0.35f }, width, z);
                    break;
                default:
                    Draw.Ring(g, p.X, p.Y, p.Radius * 0.85, NebulaTint with { A = 0.18f }, width, z);
                    break;
            }
        }
    }

    /// <summary>
    /// The terrain on a small map (the minimap, the lobby's preview), `sx` and `sy` pixels to the
    /// world's unit: clouds, bands, rings.
    /// </summary>
    public static void DrawTerrainMap(CanvasItem g, IReadOnlyList<TerrainPatch> terrain, double sx, double sy)
    {
        foreach (var p in terrain)
        {
            var at = new Vector2((float)(p.X * sx), (float)(p.Y * sy));
            var r = (float)Math.Max(2, p.Radius * sx);
            switch (p.Kind)
            {
                case TerrainKind.Nebula:
                    g.DrawCircle(at, r, NebulaTint with { A = 0.22f }, true, -1, true);
                    break;
                case TerrainKind.Belt:
                    var to = new Vector2((float)(p.X2 * sx), (float)(p.Y2 * sy));
                    var width = Math.Max(2, r * 1.6f);
                    g.DrawLine(at, to, RockTint with { A = 0.85f }, width, true);
                    g.DrawCircle(at, width / 2, RockTint with { A = 0.85f }, true, -1, true);
                    g.DrawCircle(to, width / 2, RockTint with { A = 0.85f }, true, -1, true);
                    break;
                case TerrainKind.Minefield:
                    var mines = p.Armed ? 0.8f : 0.3f;
                    g.DrawArc(at, r, 0, Mathf.Tau, 24, Palette.Danger with { A = mines }, 1, true);
                    g.DrawCircle(at, 1.5f, Palette.Danger with { A = mines }, true, -1, true);
                    break;
                case TerrainKind.Well:
                    g.DrawArc(at, r, 0, Mathf.Tau, 24, WellTint with { A = 0.6f }, 1, true);
                    g.DrawArc(at, r * 0.5f, 0, Mathf.Tau, 16, WellTint with { A = 0.8f }, 1, true);
                    break;
            }
        }
    }
}
