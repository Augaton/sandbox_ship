// The options, on the main menu's options screen and in the pause menu's dialog: changes
// apply and are saved right away. Port of src/ui/options.ts, grouped by what they touch.
using Godot;
using SandboxShip.Core;

namespace SandboxShip.Ui;

public static class Options
{
    static readonly (string Group, string Label, Func<Settings, bool> Get, Action<Settings, bool> Set)[] Toggles =
    [
        ("Vue", "Déplacer la vue quand le pointeur touche un bord de l'écran", s => s.EdgePan, (s, v) => s.EdgePan = v),
        ("Vue", "Toujours afficher les barres de coque et de bouclier", s => s.AlwaysBars, (s, v) => s.AlwaysBars = v),
        ("Combat", "Mes vaisseaux utilisent leurs capacités seuls quand c'est utile", s => s.AutoAbilities, (s, v) => s.AutoAbilities = v),
        ("Son", "Couper le son (touche M en bataille)", s => s.Muted, (s, v) => s.Muted = v),
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
        foreach (var group in Toggles.Select(t => t.Group).Distinct())
        {
            var part = new VBoxContainer();
            part.AddThemeConstantOverride("separation", 2);
            part.AddChild(UiTheme.Text(group, Palette.Muted, 14));
            foreach (var (_, label, get, set) in Toggles.Where(t => t.Group == group))
            {
                var check = HudKit.Check(label, get(s), on =>
                {
                    set(s, on);
                    s.Save();
                });
                check.FocusMode = focus;
                part.AddChild(check);
            }
            if (group == "Son") part.AddChild(Volume(s, focus));
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

    /// <summary>The effects volume: a slider and its value.</summary>
    static HBoxContainer Volume(Settings s, Control.FocusModeEnum focus)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        row.AddChild(UiTheme.Text("Volume des effets"));
        var value = UiTheme.Text(HudKit.Percent(s.Volume), Palette.Muted);
        value.CustomMinimumSize = new Vector2(52, 0);
        var volume = new HSlider
        {
            MinValue = 0, MaxValue = 100, Step = 5, Value = Math.Round(s.Volume * 100), CustomMinimumSize = new Vector2(220, 0), FocusMode = focus,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, AccessibilityName = "Volume des effets",
        };
        volume.ValueChanged += v =>
        {
            s.Volume = v / 100;
            value.Text = HudKit.Percent(s.Volume);
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
        box.AddChild(Body());
        var close = HudKit.Primary(HudKit.Button("Fermer", onClose));
        close.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        box.AddChild(close);
        return p;
    }
}
