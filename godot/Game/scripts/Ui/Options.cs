// The options, on the main menu's options screen and in the pause menu's dialog: changes
// apply and are saved right away. Port of src/ui/options.ts, grouped by what they touch.
using Godot;
using SandboxShip.Core;

namespace SandboxShip.Ui;

public static class Options
{
    /// <summary>Made when the options are shown: a label may name a key the player has changed.</summary>
    static (string Group, string Label, Func<Settings, bool> Get, Action<Settings, bool> Set)[] Toggles =>
    [
        ("Vue", "Déplacer la vue quand le pointeur touche un bord de l'écran", s => s.EdgePan, (s, v) => s.EdgePan = v),
        ("Vue", "Toujours afficher les barres de coque et de bouclier", s => s.AlwaysBars, (s, v) => s.AlwaysBars = v),
        ("Combat", "Mes vaisseaux utilisent leurs capacités seuls quand c'est utile", s => s.AutoAbilities, (s, v) => s.AutoAbilities = v),
        ("Son", $"Couper le son (touche {Bindings.Label(Bindings.Mute)} en bataille)", s => s.Muted, (s, v) => s.Muted = v),
        ("Accessibilité", "Réduire les animations : ouverture, menus, secousses de la vue", s => s.ReduceMotion, (s, v) => s.ReduceMotion = v),
    ];

    /// <summary>
    /// The settings, under a heading per group. `focusable`: reachable with the keyboard, as on
    /// the menu; in battle, Space and Enter stay the game's.
    /// </summary>
    public static VBoxContainer Body(bool focusable = false)
    {
        var s = Settings.Current;
        var focus = focusable ? Control.FocusModeEnum.All : Control.FocusModeEnum.None;
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 18);
        box.AddChild(DisplayPart(s, focus));
        var toggles = Toggles;
        foreach (var group in toggles.Select(t => t.Group).Distinct())
        {
            var part = new VBoxContainer();
            part.AddThemeConstantOverride("separation", 2);
            part.AddChild(UiTheme.Text(group, Palette.Muted, 14));
            foreach (var (_, label, get, set) in toggles.Where(t => t.Group == group))
            {
                var check = HudKit.Check(label, get(s), on =>
                {
                    set(s, on);
                    s.Save();
                });
                check.FocusMode = focus;
                part.AddChild(check);
            }
            if (group == "Son")
            {
                part.AddChild(Volume(s, focus));
                part.AddChild(Volume(s, focus, "Volume de la musique", x => x.MusicVolume, (x, v) => x.MusicVolume = v));
            }
            box.AddChild(part);
        }
        // Only on the menu: in battle, a name could not change any more.
        if (focusable) box.AddChild(Multiplayer(s));
        return box;
    }

    /// <summary>The name other players see over the network, and the online server that lists the games and relays those hosted online.</summary>
    static VBoxContainer Multiplayer(Settings s)
    {
        var part = new VBoxContainer();
        part.AddThemeConstantOverride("separation", 6);
        part.AddChild(UiTheme.Text("Multijoueur", Palette.Muted, 14));
        part.AddChild(Field("Nom du joueur", s.PlayerName, "", Sim.Net.LobbyRoom.MaxName, text => s.PlayerName = text));
        // Where the online server is, only in a developer build: a player's always uses the default.
        if (OS.IsDebugBuild()) part.AddChild(Field("Serveur en ligne", s.MasterAddress, Settings.DefaultMaster, 100, text => s.MasterAddress = text.Trim()));
        return part;
    }

    /// <summary>The window, the scale of what is drawn, the sync and the frame cap, each applied as it is chosen.</summary>
    static VBoxContainer DisplayPart(Settings s, Control.FocusModeEnum focus)
    {
        var part = new VBoxContainer();
        part.AddThemeConstantOverride("separation", 6);
        part.AddChild(UiTheme.Text("Affichage", Palette.Muted, 14));
        var sizes = Display.Sizes().ToList();
        var size = sizes.FindIndex(z => z.X == s.WindowWidth && z.Y == s.WindowHeight);
        // A size set by hand in the file, or too big for this screen, is still shown as it is.
        if (size < 0)
        {
            sizes.Insert(0, new Vector2I(s.WindowWidth, s.WindowHeight));
            size = 0;
        }
        var sizeRow = Choice("Taille de la fenêtre", sizes.Select(z => $"{z.X} × {z.Y}"), size, focus, i =>
        {
            (s.WindowWidth, s.WindowHeight) = (sizes[i].X, sizes[i].Y);
            Display.Window();
        });
        var sizeChoice = sizeRow.GetChild<OptionButton>(1);
        sizeChoice.Disabled = s.Display != DisplayMode.Windowed;
        part.AddChild(Choice("Mode", ["Fenêtre", "Plein écran", "Plein écran sans bordure"], (int)s.Display, focus, i =>
        {
            s.Display = (DisplayMode)i;
            sizeChoice.Disabled = s.Display != DisplayMode.Windowed;
            Display.Window();
        }));
        part.AddChild(sizeRow);
        var scale = Array.FindIndex(Display.Scales, x => Math.Abs(x - s.UiScale) < 0.001);
        part.AddChild(Choice("Échelle de l'interface", Display.Scales.Select(HudKit.Percent), scale < 0 ? Array.IndexOf(Display.Scales, 1.0) : scale, focus, i =>
        {
            s.UiScale = Display.Scales[i];
            Display.Scale();
        }));
        var fps = Array.IndexOf(Display.FpsLimits, s.MaxFps);
        part.AddChild(Choice("Images par seconde", Display.FpsLimits.Select(n => n == 0 ? "Sans limite" : n.ToString()), fps < 0 ? Display.FpsLimits.Length - 1 : fps, focus, i =>
        {
            s.MaxFps = Display.FpsLimits[i];
            Display.Fps();
        }));
        var sync = HudKit.Check("Synchronisation verticale (évite les déchirures de l'image)", s.VSync, on =>
        {
            s.VSync = on;
            Display.Sync();
            s.Save();
        });
        sync.FocusMode = focus;
        part.AddChild(sync);
        return part;
    }

    /// <summary>A choice among `items` with its name before it, saved and applied as it is picked.</summary>
    static HBoxContainer Choice(string name, IEnumerable<string> items, int selected, Control.FocusModeEnum focus, Action<int> pick)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        var label = UiTheme.Text(name);
        label.CustomMinimumSize = new Vector2(220, 0);
        label.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(label);
        var choice = new OptionButton { CustomMinimumSize = new Vector2(300, 36), FocusMode = focus, AccessibilityName = name, FitToLongestItem = false };
        foreach (var item in items) choice.AddItem(item);
        choice.Selected = selected;
        choice.ItemSelected += i =>
        {
            pick((int)i);
            Settings.Current.Save();
        };
        row.AddChild(choice);
        return row;
    }

    /// <summary>A line of text with its name before it, saved as it is typed.</summary>
    static HBoxContainer Field(string name, string text, string placeholder, int max, Action<string> set)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        var label = UiTheme.Text(name);
        label.CustomMinimumSize = new Vector2(170, 0);
        label.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(label);
        var input = new LineEdit { Text = text, PlaceholderText = placeholder, MaxLength = max, CustomMinimumSize = new Vector2(260, 40), AccessibilityName = name };
        input.TextChanged += typed =>
        {
            set(typed);
            Settings.Current.Save();
        };
        row.AddChild(input);
        return row;
    }

    /// <summary>A volume, the effects' unless told otherwise: a slider and its value.</summary>
    static HBoxContainer Volume(Settings s, Control.FocusModeEnum focus, string name = "Volume des effets", Func<Settings, double>? get = null, Action<Settings, double>? set = null)
    {
        get ??= x => x.Volume;
        set ??= (x, v) => x.Volume = v;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        var label = UiTheme.Text(name);
        // The two volumes' sliders line up.
        label.CustomMinimumSize = new Vector2(200, 0);
        row.AddChild(label);
        var value = UiTheme.Text(HudKit.Percent(get(s)), Palette.Muted);
        value.CustomMinimumSize = new Vector2(52, 0);
        var volume = new HSlider
        {
            MinValue = 0, MaxValue = 100, Step = 5, Value = Math.Round(get(s) * 100), CustomMinimumSize = new Vector2(220, 0), FocusMode = focus,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, AccessibilityName = name,
        };
        volume.ValueChanged += v =>
        {
            set(s, v / 100);
            value.Text = HudKit.Percent(get(s));
            s.Save();
        };
        row.AddChild(volume);
        row.AddChild(value);
        return row;
    }

    /// <summary>The options dialog of the pause menu; `onClose` runs when it is closed.</summary>
    public static PanelContainer Panel(Action onClose)
    {
        var p = HudKit.Panel(24, 24);
        p.Theme = UiTheme.Get();
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 16);
        p.AddChild(box);
        var title = HudKit.Text("Options", size: 22, bold: true);
        box.AddChild(title);
        // Taller than the screen leaves room for, the settings scroll.
        var screen = ((SceneTree)Engine.GetMainLoop()).Root.GetVisibleRect().Size;
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            CustomMinimumSize = new Vector2(620, Math.Max(240, Math.Min(620, screen.Y - 200))),
        };
        var body = Body();
        body.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(body);
        box.AddChild(scroll);
        var close = HudKit.Primary(HudKit.Button("Fermer", onClose));
        close.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        box.AddChild(close);
        return p;
    }
}
