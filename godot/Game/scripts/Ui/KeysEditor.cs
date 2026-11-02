// The keys on the menu's controls screen: each command with a button showing its key. A click on
// it, then the new key (with Ctrl, Maj or Alt if wanted), changes it; Échap or a click elsewhere
// keeps the old one. A key that another command of the same place already has is said beside
// both. A button puts every key back as it was.
using Godot;
using SandboxShip.Core;

namespace SandboxShip.Ui;

public partial class KeysEditor : VBoxContainer
{
    readonly Dictionary<string, (Button Key, Label Warning)> rows = [];
    readonly Label notice;
    /// <summary>The command waiting for its new key, if any.</summary>
    string? capturing;

    public KeysEditor()
    {
        AddThemeConstantOverride("separation", 16);
        foreach (var (where, title) in new[] { (Bindings.Where.Both, "Partout"), (Bindings.Where.Battle, "En bataille"), (Bindings.Where.Galaxy, "Sur la carte galactique") })
        {
            var part = new VBoxContainer();
            part.AddThemeConstantOverride("separation", 6);
            var heading = UiTheme.Text(title, Palette.Text, 15);
            heading.AddThemeFontOverride("font", HudKit.Bold);
            part.AddChild(heading);
            foreach (var b in Bindings.All.Where(b => b.Where == where)) part.AddChild(Row(b));
            AddChild(part);
        }
        var foot = new HBoxContainer();
        foot.AddThemeConstantOverride("separation", 12);
        var reset = new Button
        {
            Text = "Remettre les touches par défaut", CustomMinimumSize = new Vector2(0, 40), FocusMode = FocusModeEnum.All, MouseFilter = MouseFilterEnum.Pass,
        };
        reset.Pressed += () =>
        {
            capturing = null;
            Bindings.Reset();
            notice!.Text = "Touches par défaut rétablies.";
            Refresh();
        };
        foot.AddChild(reset);
        notice = UiTheme.Text("", Palette.Muted, 14);
        notice.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        notice.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        notice.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        foot.AddChild(notice);
        AddChild(foot);
        Refresh();
    }

    /// <summary>A command's name, its key's button, and what else its key does, if anything.</summary>
    Control Row(Bindings.Binding b)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        var name = UiTheme.Text(b.Name);
        name.CustomMinimumSize = new Vector2(390, 0);
        name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        name.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(name);
        // Passing the mouse on lets the wheel scroll the panel over the buttons, as the menu's do.
        var key = new Button { CustomMinimumSize = new Vector2(150, 36), FocusMode = FocusModeEnum.All, MouseFilter = MouseFilterEnum.Pass, AccessibilityName = b.Name };
        key.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        key.Pressed += () => Capture(b.Id);
        row.AddChild(key);
        var warning = UiTheme.Text("", Palette.Danger, 13);
        warning.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        warning.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        warning.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(warning);
        rows[b.Id] = (key, warning);
        return row;
    }

    void Capture(string id)
    {
        capturing = id;
        notice.Text = "Appuie sur la nouvelle touche ; Échap garde l'ancienne.";
        Refresh();
    }

    public override void _Input(InputEvent ev)
    {
        if (capturing is not { } id) return;
        // A click anywhere keeps the key as it was.
        if (ev is InputEventMouseButton { Pressed: true })
        {
            capturing = null;
            notice.Text = "";
            Refresh();
            return;
        }
        if (ev is not InputEventKey { Pressed: true, Echo: false } k) return;
        // The key is the menu's no more: Échap must not leave the screen.
        GetViewport().SetInputAsHandled();
        // A modifier alone waits for the key that goes with it.
        if (k.Keycode is Key.Ctrl or Key.Shift or Key.Alt or Key.Meta) return;
        capturing = null;
        if (k.Keycode == Key.Escape) notice.Text = "";
        else if (k.PhysicalKeycode is >= Key.Key1 and <= Key.Key9) notice.Text = "1 à 9 servent aux groupes : choisis une autre touche.";
        else
        {
            // The view's keys go by their place on the keyboard, as their defaults do; the others by what is printed on them.
            var physical = Bindings.Find(id).Default.Physical || k.Keycode == Key.None;
            Bindings.Set(id, new Bindings.Combo(physical ? k.PhysicalKeycode : k.Keycode, physical, k.CtrlPressed, k.ShiftPressed, k.AltPressed));
            var clash = Bindings.Conflicts(id).Select(o => $"« {o.Name} »").ToList();
            notice.Text = clash.Count == 0 ? $"{Bindings.Find(id).Name} : {Bindings.Label(id)}."
                : $"{Bindings.Label(id)} sert aussi à {string.Join(", ", clash)} : change l'une des deux.";
        }
        Refresh();
    }

    /// <summary>Every button shows its key, or that it waits for one; a key two commands share shows in red, with the other command beside it.</summary>
    void Refresh()
    {
        foreach (var (id, (key, warning)) in rows)
        {
            var clash = Bindings.Conflicts(id).Select(o => o.Name).ToList();
            key.Text = id == capturing ? "Appuie sur une touche…" : Bindings.Label(id);
            warning.Text = clash.Count == 0 ? "" : $"Aussi : {string.Join(", ", clash)}";
            if (clash.Count > 0) key.AddThemeColorOverride("font_color", Palette.Danger);
            else key.RemoveThemeColorOverride("font_color");
        }
    }
}
