// The star lance on the galaxy map: aiming it, the planets within its reach while it is aimed,
// its charge around its planet, and its blow crossing the map.
using Godot;
using SandboxShip.Core;
using SandboxShip.Render;
using SandboxShip.Sim;

namespace SandboxShip.Campaign;

public partial class GalaxyScreen
{
    /// <summary>How long a blow shows on the map.</summary>
    const double ShotMs = 1600;
    static readonly Color Star = Palette.Hex(0xfff2d0);

    /// <summary>The planet whose lance is being aimed: the next click on a planet fires it.</summary>
    public int? LanceFrom { get; private set; }
    readonly List<(int From, int To, double At)> shots = [];

    public void BeginLance(int from)
    {
        Rallying = false;
        LanceFrom = from;
        Hud.Toast("Clique sur une planète ennemie à portée (Échap : annuler)");
    }

    /// <summary>Gives up aiming; false when nothing was being aimed.</summary>
    bool CancelLance()
    {
        if (LanceFrom is null) return false;
        LanceFrom = null;
        return true;
    }

    void AimLance(int from, int? target)
    {
        LanceFrom = null;
        if (target is not int t)
        {
            Hud.Toast("Tir annulé");
            return;
        }
        var r = Sink.Send(new GalaxyCommand.FireLance(from, t));
        if (!r.Ok) Hud.Toast(r.Reason);
        Hud.Refresh();
    }

    /// <summary>A lance fired, by anyone: its beam crosses the map for a moment.</summary>
    public void LanceShot(int from, int to) => shots.Add((from, to, Godot.Time.GetTicksMsec()));

    void DrawLances(DrawCanvas u)
    {
        var g = Galaxy;
        var now = (double)Godot.Time.GetTicksMsec();
        var pulse = (float)(0.5 + 0.5 * Math.Sin(now / 200));
        // Each known lance: a ring around its planet, filling as it charges, burning once ready.
        foreach (var p in g.Planets)
        {
            if (p.StarLance == 0 || !Galaxies.Known(g, Player, p)) continue;
            var (x, y, r) = (Sx(p.X), Sy(p.Y), Radius(p) + 20);
            var charge = 1 - p.LanceCharge / Galaxies.LanceRecharge;
            Draw.Ring(u, x, y, r, Palette.Line, 3, 1);
            if (charge >= 1) Draw.Ring(u, x, y, r, Star with { A = 0.5f + 0.5f * pulse }, 3, 1);
            else Draw.Arc(u, x, y, r, -Math.PI / 2, -Math.PI / 2 + charge * Math.Tau, 48, Star with { A = 0.85f }, 3);
        }
        // Aiming: the planets it can strike, ringed in red.
        if (LanceFrom is int from)
        {
            foreach (var p in g.Planets)
            {
                if (Galaxies.CanFireLance(g, Player, from, p.Id).Ok)
                    Draw.Ring(u, Sx(p.X), Sy(p.Y), Radius(p) + 12, Palette.Danger with { A = 0.5f + 0.4f * pulse }, 3, 1);
            }
        }
        shots.RemoveAll(s => now - s.At > ShotMs);
        foreach (var (a, b, at) in shots)
        {
            var k = (now - at) / ShotMs;
            var (pa, pb) = (g.Planets[a], g.Planets[b]);
            var fade = (float)(1 - k);
            Draw.Line(u, Sx(pa.X), Sy(pa.Y), Sx(pb.X), Sy(pb.Y), Star with { A = 0.9f * fade }, 6 * fade + 1);
            Draw.Ring(u, Sx(pb.X), Sy(pb.Y), Radius(pb) + 10 + 60 * k, Star with { A = fade }, 4, 1);
        }
    }
}
