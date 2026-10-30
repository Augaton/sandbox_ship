// What the battle modes show on the battlefield and the minimap (not in the web version): the
// beacons of zone control, a ring in their holder's color and their capture as an arc; the
// flagships of regicide, crowned; and the toasts and alerts of each mode's events.
using Godot;
using SandboxShip.Core;
using SandboxShip.Render;
using SandboxShip.Sim;
using SandboxShip.Sim.Data;
using SandboxShip.Ui;

namespace SandboxShip.Battle;

public partial class BattleScreen
{
    /// <summary>The beacons, above the fog: who holds them is no secret.</summary>
    DrawCanvas? modeMarks;

    void BuildModeView()
    {
        if (World.Mode.Beacons.Count > 0) modeMarks = Layer(Depth.Fog + 1, "modes", DrawBeacons);
    }

    /// <summary>A beacon: a disc and ring in its holder's color, its capture as an arc in the taker's, a red pulse while contested, its letter.</summary>
    void DrawBeacons(CanvasItem g)
    {
        var z = Zoom;
        var pulse = 0.5 + 0.5 * Math.Sin(Now / 200);
        var r = ZoneControl.Radius;
        for (var i = 0; i < World.Mode.Beacons.Count; i++)
        {
            var b = World.Mode.Beacons[i];
            var color = b.Holder is Side h ? Palette.Of(World, h) : Palette.Muted;
            Draw.Disc(g, b.X, b.Y, r, color with { A = b.Holder is null ? 0.03f : 0.06f }, z);
            Draw.Ring(g, b.X, b.Y, r, color with { A = 0.8f }, 2 / z, z);
            Draw.Ring(g, b.X, b.Y, 30, color with { A = 0.9f }, 3 / z, z);
            if (b.Claim is Side c && b.Progress < 1)
            {
                Draw.Ring(g, b.X, b.Y, r + 10 / z, Palette.Line with { A = 0.8f }, 5 / z, z);
                Draw.Arc(g, b.X, b.Y, r + 10 / z, -Math.PI / 2, -Math.PI / 2 + b.Progress * Math.Tau, 64, Palette.Of(World, c), 5 / z);
            }
            if (b.Contested) Draw.Ring(g, b.X, b.Y, r - 8 / z, Palette.Danger with { A = (float)(0.4 + 0.5 * pulse) }, 3 / z, z);
            Draw.Text(g, UiTheme.Mono, Hud.BeaconName(i), b.X, b.Y + 9 / z, 22, Palette.Text, z, Palette.Bg);
        }
    }

    /// <summary>The flagships in sight: a ring in their side's color and a crown above.</summary>
    void DrawFlagships(CanvasItem g, Rect2 rect, double z)
    {
        foreach (var (side, id) in World.Mode.Flagships)
        {
            if (World.Entities.Get(id) is not { } e || !CanSee(e) || !rect.Grow((float)e.Radius + 60 / (float)z).HasPoint(new Vector2((float)e.X, (float)e.Y))) continue;
            var color = Palette.Of(World, side);
            Draw.Ring(g, e.X, e.Y, e.Radius + 16 / z, color with { A = 0.85f }, 2.5 / z, z);
            // Above the hero's name.
            var (x, y, s) = (e.X, e.Y - e.Radius - 34 / z, 1 / z);
            Draw.Rect(g, x - 11 * s, y, 22 * s, 5 * s, color);
            foreach (var dx in new[] { -11.0, 0, 11 })
            {
                var top = dx == 0 ? 14 : 10;
                Draw.Triangle(g, new Vector2((float)(x + (dx - 4) * s), (float)y), new Vector2((float)(x + (dx + 4) * s), (float)y), new Vector2((float)(x + dx * s), (float)(y - top * s)), color);
            }
        }
    }

    /// <summary>On the minimap: the beacons and their capture; the flagships, which everyone knows the place of, pulsing.</summary>
    public void DrawModeMarks(CanvasItem g, double sx, double sy)
    {
        var m = World.Mode;
        foreach (var b in m.Beacons)
        {
            var at = new Vector2((float)(b.X * sx), (float)(b.Y * sy));
            var color = b.Holder is Side h ? Palette.Of(World, h) : Palette.Muted;
            g.DrawArc(at, 6, 0, Mathf.Tau, 24, color, 2);
            if (b.Claim is Side c && b.Progress < 1) g.DrawArc(at, 9, -Mathf.Pi / 2, -Mathf.Pi / 2 + (float)b.Progress * Mathf.Tau, 24, Palette.Of(World, c), 2);
            if (b.Contested) g.DrawArc(at, 9, 0, Mathf.Tau, 24, Palette.Danger, 1);
        }
        var pulse = (float)(0.5 + 0.5 * Math.Sin(Now / 300));
        foreach (var (side, id) in m.Flagships)
        {
            if (World.Entities.Get(id) is not { } e) continue;
            var at = new Vector2((float)(e.X * sx), (float)(e.Y * sy));
            g.DrawArc(at, 5 + 2 * pulse, 0, Mathf.Tau, 24, Palette.Of(World, side, 0.5f + 0.4f * pulse), 2);
            g.DrawCircle(at, 2.5f, Palette.Of(World, side));
        }
    }

    /// <summary>Toasts and alerts for the events of the battle mode; a lost flagship may be the player's defeat.</summary>
    void ShowModeEvent(SimEvent ev)
    {
        var w = World;
        string Race(Side s) => Factions.Defs[Worlds.RaceOf(w, s)].Name;
        switch (ev)
        {
            case WaveIncoming wi:
                var boss = wi.Boss is ShipClass b ? $", avec un {Ships.Defs[b].Name}" : "";
                Alert($"Vague {wi.Wave} : les Errants arrivent{boss}", wi.X, wi.Y, true);
                if (wi.Boss is not null) Sfx.Play(Sound.Horn);
                break;
            case WaveCleared wc:
                Hud.Toast(wc.Wave >= Survival.Waves ? "Dernière vague repoussée" : $"Vague {wc.Wave} repoussée");
                break;
            case BeaconTaken bt:
                var taken = Hud.BeaconName(bt.Beacon);
                Hud.Toast(bt.Side == Opts.Player ? $"Balise {taken} prise" : Worlds.Allies(w, bt.Side, Opts.Player) ? $"Balise {taken} prise par un allié" : $"Les {Race(bt.Side)} ont pris la balise {taken}");
                if (bt.Side == Opts.Player) Sfx.Play(Sound.Built);
                break;
            case BeaconLost bl:
                var lost = Hud.BeaconName(bl.Beacon);
                Hud.Toast(Worlds.Allies(w, bl.Side, Opts.Player) ? $"Balise {lost} perdue" : $"Balise {lost} reprise aux {Race(bl.Side)}");
                break;
            case FlagshipLost fl:
                Hud.Toast(fl.Side == Opts.Player ? "Ton vaisseau amiral est détruit"
                    : Worlds.Allies(w, fl.Side, Opts.Player) ? "Le vaisseau amiral d'un allié est détruit : sa flotte s'autodétruit"
                    : $"Vaisseau amiral des {Race(fl.Side)} abattu : leur flotte s'autodétruit");
                Sfx.Play(Sound.Horn);
                if (!defeated && w.Winner is null && Opts.Campaign is null && Modes.Out(w, Opts.Player))
                {
                    defeated = true;
                    Hud.ShowEnd(false);
                }
                break;
            case ClockWarning cw when World.Mode.Kind == BattleMode.Regicide:
                Hud.Toast(cw.Left > 0
                    ? $"Mort subite dans {cw.Left / 60:0} min : les vaisseaux amiraux perdront leurs boucliers et ne se répareront plus"
                    : "Mort subite : les vaisseaux amiraux n'ont plus de bouclier et ne se réparent plus");
                Sfx.Play(cw.Left > 0 ? Sound.Alert : Sound.Horn);
                break;
            case ClockWarning cw:
                Hud.Toast($"Assaut : plus que {cw.Left / 60:0} min");
                Sfx.Play(Sound.Alert);
                break;
        }
    }
}
