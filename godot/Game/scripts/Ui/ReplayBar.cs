// The bar over a replay played (Sim/Replays.cs), in place of the battle's HUD: what it is, where it
// stands, and how to watch it: pause, speeds up to ×8, a jump ahead, whose eyes (or none of the
// fog), from the start again, back to the menu. No order can be given: the battle is the one
// recorded. At the end it says whether it came out as recorded.
using Godot;
using SandboxShip.Battle;
using SandboxShip.Core;
using SandboxShip.Sim.Data;

namespace SandboxShip.Ui;

public partial class ReplayBar : CanvasLayer
{
    static readonly double[] Speeds = [1, 2, 4, 8];

    readonly BattleScreen scene;
    Label clock = null!, note = null!;
    Button pause = null!;
    readonly List<(double Speed, Button Button)> speeds = [];
    bool ended;

    public ReplayBar(BattleScreen scene) => this.scene = scene;

    public override void _Ready()
    {
        Layer = 2;
        var root = new Control { Theme = UiTheme.Get(), MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);
        var panel = HudKit.Panel(12, 8);
        panel.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        panel.OffsetLeft = 8;
        panel.OffsetRight = -8;
        panel.OffsetTop = 8;
        root.AddChild(panel);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        panel.AddChild(column);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        column.AddChild(row);
        var replay = scene.Replay!.Replay;
        var about = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        about.AddThemeConstantOverride("separation", 0);
        // Cut short rather than pushing the buttons off the screen.
        var title = HudKit.Text(ReplayStore.Title(replay), bold: true);
        title.ClipText = true;
        title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        title.TooltipText = ReplayStore.Title(replay);
        title.MouseFilter = Control.MouseFilterEnum.Pass;
        about.AddChild(title);
        clock = HudKit.Text("", Palette.Muted, 13);
        about.AddChild(clock);
        row.AddChild(about);

        pause = HudKit.Button("Pause", scene.TogglePause, "Espace");
        pause.CustomMinimumSize = new Vector2(96, 40);
        row.AddChild(pause);
        foreach (var s in Speeds)
        {
            var b = HudKit.Button($"×{s}", () => scene.SetSpeed(s), $"Vitesse ×{s}");
            speeds.Add((s, b));
            row.AddChild(b);
        }
        row.AddChild(HudKit.Button("+30 s", () => scene.SeekBy(30), "Avancer de 30 secondes, sans les explosions ni les sons"));
        row.AddChild(View(replay));
        row.AddChild(HudKit.Button("Recommencer", () => scene.Restart(), "Revoir depuis le début"));
        row.AddChild(HudKit.Button("Quitter", scene.ToMenu, "Retour au menu (Échap)"));

        note = HudKit.Text("", Palette.Muted, 13, wrap: true);
        note.Visible = replay.Version != Menu.Version;
        note.Text = $"Enregistrée avec la version {replay.Version} du jeu : elle peut ne pas rejouer exactement la même bataille.";
        column.AddChild(note);
        // Checking the screen: --skip jumps ahead, --speed plays faster.
        if (Dev.Skip > 0) scene.SeekBy(Dev.Skip);
        if (Dev.Speed > 0) scene.SetSpeed(Dev.Speed);
    }

    /// <summary>Whose eyes the replay is watched through, each side's fog, or none at all.</summary>
    OptionButton View(Sim.Replay replay)
    {
        var view = new OptionButton { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(150, 40), TooltipText = "Voir la bataille avec le brouillard d'un camp, ou tout voir", AccessibilityName = "Vue" };
        view.AddItem("Tout voir");
        var sides = new List<Side?> { null };
        foreach (var slot in replay.Options.Slots)
        {
            view.AddItem($"Vue {Factions.Defs[slot.Race].Name}");
            sides.Add(slot.Side);
        }
        view.Selected = Math.Max(0, sides.IndexOf(replay.Player));
        view.ItemSelected += i => scene.ViewAs(sides[(int)i]);
        return view;
    }

    public override void _Process(double delta)
    {
        var player = scene.Replay!;
        var total = player.Replay.Seconds;
        clock.Text = $"Rediffusion, {Time(scene.World.Time)} / {Time(total)}{(scene.Seeking ? ", en avance rapide…" : scene.Paused ? ", en pause" : "")}";
        pause.Text = scene.Paused ? "Lecture" : "Pause";
        foreach (var (s, b) in speeds) HudKit.Outline(b, !scene.Paused && scene.Speed == s ? Palette.Accent : null);
        if (ended || !player.Over) return;
        ended = true;
        if (player.Faithful == true) PlayerProfile.Moment(Sim.Profiles.Moment.Replay);
        note.Visible = true;
        note.Text = player.Faithful == true ? "Fin de la rediffusion."
            : "Cette rediffusion ne rejoue plus la bataille enregistrée : elle vient sans doute d'une autre version du jeu.";
        note.AddThemeColorOverride("font_color", player.Faithful == true ? Palette.Ok : Palette.Danger);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!e.IsActionPressed("ui_cancel")) return;
        GetViewport().SetInputAsHandled();
        scene.ToMenu();
    }

    /// <summary>A battle's clock: "3:07", "1:02:45".</summary>
    static string Time(double seconds)
    {
        var s = (int)seconds;
        return s >= 3600 ? $"{s / 3600}:{s % 3600 / 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
    }
}
