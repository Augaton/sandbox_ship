// The end screens' curves (not in the web version): each side's army and earnings over the battle,
// from its history (Sim/BattleHistory.cs), and the player's ships that destroyed the most. A line
// per side in its color, its mark at the end of it (and along it, with colors for colour blindness).
using Godot;
using SandboxShip.Core;
using SandboxShip.Render;
using SandboxShip.Sim;
using SandboxShip.Sim.Data;
using static SandboxShip.Ui.HudKit;

namespace SandboxShip.Ui;

public partial class Hud
{
    /// <summary>The end's table, with the battle over time under it, when there is one to show.</summary>
    Control WithCharts(Control table)
    {
        var box = Column(18);
        box.AddChild(table);
        if (Charts() is { } charts) box.AddChild(charts);
        if (Aces() is { } aces) box.AddChild(aces);
        return box;
    }

    /// <summary>The sides drawn, the player's first: the players, and the Errants only when they are the game (a survival).</summary>
    IEnumerable<Side> Charted(BattleHistory h) => h.Sides.Keys.Where(s =>
        World.Mode.Kind == BattleMode.Survival || !World.Factions.TryGetValue(s, out var f) || f.Race != Faction.Errant).OrderBy(s => s != Player);

    /// <summary>Army and earnings over time, side by side, and which line is whose; none for a battle too short to draw.</summary>
    Control? Charts()
    {
        if (World.History is not { Times.Count: >= 2 } h) return null;
        var sides = Charted(h).ToList();
        if (sides.Count == 0) return null;
        var box = Column(8);
        var row = Row(16);
        row.AddChild(Curve("Armée en vol", h, sides, p => p.Army));
        row.AddChild(Curve("Crédits gagnés", h, sides, p => p.Earned));
        box.AddChild(row);
        var legend = Row(14);
        foreach (var side in sides)
        {
            var item = Row(6);
            item.AddChild(Mark(side));
            item.AddChild(Text(Factions.Defs[Worlds.RaceOf(World, side)].Name, side == Player ? Palette.Accent : Palette.Muted, 13));
            legend.AddChild(item);
        }
        box.AddChild(legend);
        return box;
    }

    Control Curve(string title, BattleHistory h, List<Side> sides, Func<HistoryPoint, float> pick)
    {
        var box = Column(4);
        box.AddChild(Text(title, Palette.Muted, 13));
        box.AddChild(new Chart(h.Times, [.. sides.Select(s => (s, Palette.Of(World, s), h.Sides[s].Select(pick).ToArray()))], Palette.Marked));
        return box;
    }

    /// <summary>A side's mark alone, for the legend.</summary>
    Control Mark(Side side)
    {
        var mark = Ignore(new Control { CustomMinimumSize = new Vector2(14, 14), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        var color = Palette.Of(World, side);
        mark.Draw += () => Render.Draw.Mark(mark, side, 7, 7, 5.5, color, 1);
        return mark;
    }

    /// <summary>The player's ships that destroyed the most, the best first; none if none of them did.</summary>
    Control? Aces()
    {
        if (World.History?.Aces.GetValueOrDefault(Player) is not { Count: > 0 } aces) return null;
        var box = Column(6);
        box.AddChild(Text("Tes meilleurs vaisseaux", Palette.Muted, 13));
        var race = Worlds.RaceOf(World, Player);
        foreach (var a in aces.Take(3))
        {
            var line = Row(10);
            if (Assets.ShipIcon(race, a.Cls) is { } icon)
            {
                line.AddChild(Ignore(new TextureRect
                {
                    Texture = icon, StretchMode = TextureRect.StretchModeEnum.KeepCentered, CustomMinimumSize = new Vector2(34, 34),
                    TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                }));
            }
            var name = a.Hero is HeroId hero ? Heroes.Defs[hero].Name : Ships.Defs[a.Cls].Name;
            line.AddChild(Text(name, Palette.Text, 14, bold: true));
            line.AddChild(Text($"{N(a.Kills)} destructions pour {N(Js.Round(a.Value))} crédits", Palette.Accent, 14));
            line.AddChild(Text(a.Alive ? "en service" : "détruit", Palette.Muted, 13));
            box.AddChild(line);
        }
        return box;
    }
}

/// <summary>
/// Curves over a battle: time across, values up from nothing to the highest, a line per side in
/// its color with its mark at its end, the first side's over the others; with the adapted colors,
/// marks along it too.
/// </summary>
public partial class Chart : Control
{
    const float Left = 4, Top = 18, Bottom = 18, Right = 12;
    /// <summary>Marks along a line, with the adapted colors.</summary>
    const int MarksAlong = 5;

    readonly IReadOnlyList<double> times;
    readonly (Side Side, Color Color, float[] Values)[] series;
    readonly bool marked;

    public Chart() : this([], [], false) { }

    public Chart(IReadOnlyList<double> times, (Side Side, Color Color, float[] Values)[] series, bool marked)
    {
        (this.times, this.series, this.marked) = (times, series, marked);
        CustomMinimumSize = new Vector2(330, 150);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        if (times.Count < 2 || series.Length == 0) return;
        var (w, h) = (Size.X, Size.Y);
        DrawRect(new Rect2(0, 0, w, h), Palette.Bg with { A = 0.55f });
        var max = 0f;
        foreach (var s in series) foreach (var v in s.Values) max = Math.Max(max, v);
        if (max <= 0) max = 1;
        var (t0, span) = (times[0], Math.Max(1, times[^1] - times[0]));
        var plot = new Rect2(Left, Top, w - Left - Right, h - Top - Bottom);
        for (var k = 0; k <= 2; k++)
        {
            var y = plot.Position.Y + plot.Size.Y * k / 2;
            DrawLine(new Vector2(plot.Position.X, y), new Vector2(plot.End.X, y), Palette.Line with { A = k == 2 ? 0.9f : 0.45f }, 1);
        }
        var font = UiTheme.Mono;
        DrawString(font, new Vector2(Left + 2, 13), HudKit.N(Js.Round(max)), HorizontalAlignment.Left, -1, 11, Palette.Muted);
        DrawString(font, new Vector2(Left + 2, h - 4), "0", HorizontalAlignment.Left, -1, 11, Palette.Muted);
        DrawString(font, new Vector2(0, h - 4), Names.Duration(span), HorizontalAlignment.Right, w - Right, 11, Palette.Muted);
        Vector2 At(int i, float v) => new(
            plot.Position.X + (float)((times[i] - t0) / span) * plot.Size.X,
            plot.Position.Y + (1 - Math.Clamp(v / max, 0, 1)) * plot.Size.Y);
        // The first series last, over the others: the player's line shows even where another runs along it.
        for (var k = series.Length - 1; k >= 0; k--)
        {
            var (side, color, values) = series[k];
            var points = new Vector2[times.Count];
            for (var i = 0; i < points.Length; i++) points[i] = At(i, values[i]);
            DrawPolyline(points, color, 2, true);
            if (marked)
            {
                var every = Math.Max(1, points.Length / MarksAlong);
                for (var i = every; i < points.Length - 1; i += every) Render.Draw.Mark(this, side, points[i].X, points[i].Y, 3.5, color, 1);
            }
            Render.Draw.Mark(this, side, points[^1].X, points[^1].Y, 5, color, 1);
        }
    }
}
