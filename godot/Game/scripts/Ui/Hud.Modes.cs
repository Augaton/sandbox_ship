// The battle HUD's mode panel (not in the web version), at the top center: the wave and its
// countdown in survival, each camp's score and the beacons in zone control, the clock and the
// player's role in an assault, the flagships still flying in regicide. Also each mode's line
// on the end screen.
using Godot;
using SandboxShip.Core;
using SandboxShip.Sim;
using SandboxShip.Sim.Data;
using static SandboxShip.Ui.HudKit;

namespace SandboxShip.Ui;

public partial class Hud
{
    /// <summary>Where the panel starts, under the top bar.</summary>
    const int ModeTop = 60;

    PanelContainer? modePanel;
    VBoxContainer modeLines = null!;
    double modeTimer;

    /// <summary>Beacons are named by letter, in the order of the mode state.</summary>
    public static string BeaconName(int i) => ((char)('A' + i)).ToString();

    /// <summary>"12:05"</summary>
    static string Clock(double seconds)
    {
        var s = (int)Math.Ceiling(Math.Max(0, seconds));
        return $"{s / 60}:{s % 60:00}";
    }

    void BuildModes()
    {
        if (World.Mode.Kind == BattleMode.Standard) return;
        modePanel = Ignore(Panel(14, 6));
        modePanel.AnchorLeft = modePanel.AnchorRight = 0.5f;
        modePanel.GrowHorizontal = Control.GrowDirection.Both;
        modePanel.OffsetTop = ModeTop;
        modeLines = Column(4);
        modePanel.AddChild(modeLines);
        root.AddChild(modePanel);
    }

    void TickModes(double delta)
    {
        if (modePanel is null) return;
        // The piloting banner and the toasts make room below the panel.
        var below = ModeTop + modePanel.Size.Y + 6;
        var banner = pilotBanner.GetParent<Control>();
        if (banner.OffsetTop != below)
        {
            banner.OffsetTop = below;
            toasts.OffsetTop = below + 48;
        }
        modeTimer -= delta;
        if (modeTimer > 0) return;
        modeTimer = RefreshMs;
        Sync(modeLines, ModeParts());
        // Back to the width of its content, centered, when a line gets shorter.
        modePanel.OffsetLeft = modePanel.OffsetRight = 0;
    }

    /// <summary>The camps of the battle, the Errants aside: a team, or a side alone, its sides in the order they joined.</summary>
    List<List<Side>> Camps()
    {
        var camps = new List<List<Side>>();
        foreach (var f in World.Factions.Values)
        {
            if (f.Race == Faction.Errant) continue;
            if (camps.Find(c => Worlds.Allies(World, c[0], f.Id)) is { } camp) camp.Add(f.Id);
            else camps.Add([f.Id]);
        }
        return camps;
    }

    static Part Heading(string mode, string detail) => SpansPart(14, (mode, Palette.Accent, true), (detail, Palette.Muted, false));

    List<Part> ModeParts()
    {
        var m = World.Mode;
        var now = World.Time;
        var parts = new List<Part>();
        switch (m.Kind)
        {
            case BattleMode.Survival:
                parts.AddRange(SurvivalParts(now));
                break;
            case BattleMode.Zones:
                parts.Add(Heading("Contrôle de zone", $"premier à {m.ZoneTarget} points"));
                foreach (var camp in Camps())
                {
                    var score = ZoneControl.Score(World, camp[0]);
                    parts.Add(CampBar(camp, score / m.ZoneTarget, $"{Math.Floor(score)} / {m.ZoneTarget}"));
                }
                parts.Add(BeaconsPart());
                break;
            case BattleMode.Assault:
            {
                var defending = Worlds.TeamOf(World, Player) == Assault.Defenders;
                parts.Add(Heading("Assaut", defending ? "tu défends la forteresse" : "tu attaques la forteresse"));
                var left = m.Deadline - now;
                parts.Add(SpansPart(22, (Clock(left), left < 60 ? Palette.Danger : Palette.Text, true),
                    (defending ? "à tenir" : "pour la détruire", Palette.Muted, false)));
                break;
            }
            case BattleMode.Regicide:
                parts.Add(Heading("Régicide", "vaisseaux amiraux"));
                foreach (var f in World.Factions.Values)
                {
                    if (f.Race == Faction.Errant) continue;
                    var flag = m.Flagships.TryGetValue(f.Id, out var id) ? World.Entities.Get(id) : null;
                    var state = flag is null ? "perdu" : $"coque {Percent(flag.Hull / flag.MaxHull)}";
                    parts.Add(FlagshipLine(f.Id, state, flag is not null));
                }
                break;
        }
        return parts;
    }

    /// <summary>A camp's emblems, its score as a bar in its color, and the score.</summary>
    Part CampBar(List<Side> camp, double ratio, string label)
    {
        var mine = camp.Exists(s => s == Player);
        return new($"camp:{string.Join(",", camp)}", () =>
        {
            var row = Row(8);
            foreach (var s in camp) row.AddChild(Emblem(s));
            var color = Palette.Of(World, camp[0]);
            var bar = new ProgressBar
            {
                ShowPercentage = false, MaxValue = 1, Step = 0, CustomMinimumSize = new Vector2(180, 10),
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            var fill = UiTheme.Box(color, color, pad: 0);
            fill.SetContentMarginAll(0);
            var back = UiTheme.Box(Palette.Bg, Palette.Line, pad: 0);
            back.SetContentMarginAll(0);
            bar.AddThemeStyleboxOverride("fill", fill);
            bar.AddThemeStyleboxOverride("background", back);
            row.AddChild(bar);
            row.AddChild(Text("", mine ? Palette.Text : Palette.Muted, 14, bold: mine));
            return row;
        }, n =>
        {
            n.GetChild<ProgressBar>(camp.Count).Value = Math.Clamp(ratio, 0, 1);
            n.GetChild<Label>(camp.Count + 1).Text = label;
        });
    }

    /// <summary>Each beacon's letter in its holder's color, marked while it is contested.</summary>
    Part BeaconsPart()
    {
        var beacons = World.Mode.Beacons;
        return new($"beacons:{beacons.Count}", () =>
        {
            var row = Row(14);
            row.AddChild(Text("Balises", Palette.Muted, 14));
            for (var i = 0; i < beacons.Count; i++)
            {
                var cell = Row(4);
                cell.AddChild(Ignore(new ColorRect { CustomMinimumSize = new Vector2(10, 10), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter }));
                cell.AddChild(Text(BeaconName(i), Palette.Text, 14, bold: true));
                row.AddChild(cell);
            }
            return row;
        }, n =>
        {
            for (var i = 0; i < beacons.Count; i++)
            {
                var b = beacons[i];
                var cell = n.GetChild(i + 1);
                cell.GetChild<ColorRect>(0).Color = b.Holder is Side h ? Palette.Of(World, h) : Palette.Line;
                var label = cell.GetChild<Label>(1);
                label.Text = BeaconName(i) + (b.Contested ? " !" : b.Holder is null && b.Claim is not null ? $" {Percent(b.Progress)}" : "");
                Paint(label, b.Contested ? Palette.Danger : Palette.Text);
            }
        });
    }

    /// <summary>A side's emblem and name, and how its flagship fares.</summary>
    Part FlagshipLine(Side side, string state, bool flying) => new($"flag:{side}", () =>
    {
        var row = Row(8);
        row.AddChild(Emblem(side));
        row.AddChild(Text(Factions.Defs[Worlds.RaceOf(World, side)].Name, side == Player ? Palette.Text : Palette.Muted, 14, bold: side == Player));
        row.AddChild(Text("", Palette.Text, 14));
        return row;
    }, n =>
    {
        var label = n.GetChild<Label>(2);
        label.Text = state;
        Paint(label, flying ? Palette.Ok : Palette.Danger);
    });

    /// <summary>What the end screen says happened, in the battle's mode; the winner may not be known yet after a defeat.</summary>
    string EndText(bool won)
    {
        var m = World.Mode;
        var winner = World.Winner;
        string Who(Side s) => $"Les {Factions.Defs[Worlds.RaceOf(World, s)].Name}";
        switch (m.Kind)
        {
            case BattleMode.Survival:
                return $"Les Errants ont détruit les stations à la vague {m.Wave} : {m.Cleared} repoussée{(m.Cleared > 1 ? "s" : "")}, tenu {Names.Duration(m.Held > 0 ? m.Held : World.Time)}.";
            case BattleMode.Zones when winner is Side w && ZoneControl.Score(World, w) >= m.ZoneTarget:
                return won ? $"{m.ZoneTarget} points atteints : les balises sont à toi." : $"{Who(w)} atteignent {m.ZoneTarget} points.";
            case BattleMode.Assault:
            {
                var defending = Worlds.TeamOf(World, Player) == Assault.Defenders;
                var held = World.Time >= m.Deadline;
                if (defending) return won ? held ? "La forteresse a tenu jusqu'au bout." : "La tête de pont ennemie est détruite." : "La forteresse est tombée.";
                return won ? "La forteresse est tombée." : held ? "Le temps est écoulé : la forteresse a tenu." : "Ta tête de pont est détruite.";
            }
            case BattleMode.Regicide:
                return won ? "Le dernier vaisseau amiral ennemi est abattu." : "Ton vaisseau amiral est détruit.";
        }
        return won ? "La station ennemie est détruite." : "Ta station est détruite.";
    }
}
