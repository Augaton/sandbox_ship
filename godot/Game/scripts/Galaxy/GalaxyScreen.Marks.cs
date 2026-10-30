// Map marks for what this version adds to the campaign: the haze of nebulae, lanes an ion storm
// crosses, a war band's way to its target, derelicts waiting to be claimed, booming planets.
// Random events are announced to all: they show whatever the fog.
using Godot;
using SandboxShip.Core;
using SandboxShip.Render;
using SandboxShip.Sim;
using SandboxShip.Sim.Data;
using SandboxShip.Ui;

namespace SandboxShip.Campaign;

public partial class GalaxyScreen
{
    static readonly Color NebulaColor = Palette.Hex(0x8a6cd8), StormColor = Palette.Hex(0x9b8cff), DerelictColor = Palette.Hex(0xb8c0cc);

    /// <summary>Under the lanes' fleets and planets: nebulae, storms, war bands on their way.</summary>
    void DrawMarksUnder(DrawCanvas u)
    {
        var g = Galaxy;
        var t = Godot.Time.GetTicksMsec() / 1000.0;
        foreach (var p in g.Planets)
        {
            if (p.Special != PlanetSpecial.Nebula) continue;
            Draw.Disc(u, Sx(p.X), Sy(p.Y), Radius(p) * 2.4, NebulaColor with { A = 0.10f }, 1);
            Draw.Disc(u, Sx(p.X), Sy(p.Y), Radius(p) * 1.7, NebulaColor with { A = 0.12f }, 1);
        }
        foreach (var i in g.Ongoing)
        {
            if (i.Kind is not (IncidentKind.IonStorm or IncidentKind.WarBand)) continue;
            var (a, b) = (g.Planets[i.Planet], g.Planets[i.Other]);
            var (x0, y0, x1, y1) = (Sx(a.X), Sy(a.Y), Sx(b.X), Sy(b.Y));
            if (i.Kind == IncidentKind.IonStorm)
            {
                Draw.Line(u, x0, y0, x1, y1, StormColor with { A = (float)(0.45 + 0.25 * Math.Sin(t * 4)) }, 6);
                continue;
            }
            // A war band's way: dashes that run towards the planet it strikes.
            var len = Math.Max(1, Worlds.Hypot(x1 - x0, y1 - y0));
            var errant = Palette.Of(Faction.Errant);
            for (var d = (t * 40) % 18; d + 9 < len; d += 18)
            {
                var (s0, s1) = (d / len, (d + 9) / len);
                Draw.Line(u, x0 + (x1 - x0) * s0, y0 + (y1 - y0) * s0, x0 + (x1 - x0) * s1, y0 + (y1 - y0) * s1, errant, 3);
            }
        }
    }

    /// <summary>Over the planets: a derelict's wreck beside its planet, a gold ring round a booming one, a pulse on a war band's target.</summary>
    void DrawMarksOver(DrawCanvas o)
    {
        var g = Galaxy;
        var t = Godot.Time.GetTicksMsec() / 1000.0;
        foreach (var i in g.Ongoing)
        {
            var p = g.Planets[i.Planet];
            var (x, y, r) = (Sx(p.X), Sy(p.Y), Radius(p));
            switch (i.Kind)
            {
                case IncidentKind.Derelict:
                {
                    // A small grey diamond up and to the left, where no fleet marker goes.
                    var (cx, cy) = (x - r - 10, y - r - 6);
                    Vector2[] wreck = [new((float)cx, (float)(cy - 7)), new((float)(cx + 6), (float)cy), new((float)cx, (float)(cy + 7)), new((float)(cx - 6), (float)cy)];
                    Draw.Flush(o);
                    o.DrawColoredPolygon(wreck, DerelictColor with { A = (float)(0.7 + 0.3 * Math.Sin(t * 3)) });
                    o.DrawPolyline([.. wreck, wreck[0]], Palette.Bg, 1);
                    break;
                }
                case IncidentKind.TradeBoom:
                    Draw.Ring(o, x, y, r + 5, Palette.Accent with { A = 0.85f }, 2, 1);
                    break;
                case IncidentKind.WarBand:
                {
                    var target = g.Planets[i.Other];
                    Draw.Ring(o, Sx(target.X), Sy(target.Y), Radius(target) + 12, Palette.Of(Faction.Errant) with { A = (float)(0.5 + 0.5 * Math.Sin(t * 5)) }, 2, 1);
                    break;
                }
            }
        }
    }
}
