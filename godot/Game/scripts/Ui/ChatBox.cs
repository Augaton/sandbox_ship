// The chat of a game played over the network (Session.Chat, Sim/Net/Chat.cs). In the lobby, a
// panel with its line to write always there; in battle and on the galaxy, the last lines over
// the view, fading after a while, and a line to write that Entrée opens, Entrée sends and Échap
// closes. Plain text only: a Label shows what it is given, nothing in it is read as markup.
using Godot;
using SandboxShip.Core;
using SandboxShip.Network;
using SandboxShip.Sim.Net;

namespace SandboxShip.Ui;

public partial class ChatBox : CanvasLayer
{
    /// <summary>Seconds a line stays over the view, then the time it takes to fade out.</summary>
    const double Shown = 8, Fading = 1.5;
    /// <summary>Lines shown: over the view, and with the line to write open (or in the lobby).</summary>
    const int Few = 5, Many = 12;
    const float Wide = 440, LobbyWide = 340;

    /// <summary>The lobby's panel; otherwise the overlay of a battle or of the galaxy.</summary>
    readonly bool lobby;
    /// <summary>Overlay: how far above the bottom of the screen its last line sits, clear of the HUD.</summary>
    readonly float bottom;
    VBoxContainer lines = null!;
    LineEdit input = null!;
    Control frame = null!;
    int version = -1;
    bool open;

    public ChatBox(bool lobby, float bottom = 24)
    {
        (this.lobby, this.bottom) = (lobby, bottom);
        Layer = 8;
    }

    public override void _Ready()
    {
        var root = new Control { Theme = UiTheme.Get(), MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 6);
        lines = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        lines.AddThemeConstantOverride("separation", 2);
        input = new LineEdit
        {
            PlaceholderText = lobby ? "Écrire aux autres joueurs" : "Message (Entrée : envoyer, Échap : fermer)",
            MaxLength = Chat.MaxText, AccessibilityName = "Message aux autres joueurs", Visible = lobby,
            CustomMinimumSize = new Vector2(0, 36),
        };
        input.TextSubmitted += Submit;
        input.GuiInput += e =>
        {
            if (e is not InputEventKey { Pressed: true, Keycode: Key.Escape }) return;
            input.AcceptEvent();
            if (lobby) input.ReleaseFocus();
            else Close();
        };
        if (lobby)
        {
            var panel = HudKit.Panel(12, 10);
            panel.MouseFilter = Control.MouseFilterEnum.Stop;
            var inside = new VBoxContainer();
            inside.AddThemeConstantOverride("separation", 8);
            inside.AddChild(UiTheme.Text("Discussion", Palette.Muted, 14));
            inside.AddChild(lines);
            inside.AddChild(input);
            panel.AddChild(inside);
            column.AddChild(panel);
            frame = panel;
        }
        else
        {
            column.AddChild(lines);
            column.AddChild(input);
            frame = column;
        }
        root.AddChild(column);
        void Place()
        {
            var size = root.Size;
            var w = lobby ? LobbyWide : Math.Min(Wide, size.X - 32);
            var h = column.GetCombinedMinimumSize().Y;
            column.Size = new Vector2(w, h);
            column.Position = lobby ? new Vector2(size.X - w - 24, size.Y - h - 64) : new Vector2(16, size.Y - h - bottom);
        }
        root.Resized += Place;
        column.Resized += Place;
        column.MinimumSizeChanged += Place;
        Place();
    }

    public override void _Process(double delta)
    {
        if (Session.Current is not { } s)
        {
            frame.Visible = false;
            return;
        }
        frame.Visible = true;
        if (s.Chat.Version != version)
        {
            version = s.Chat.Version;
            Fill(s.Chat);
        }
        if (!lobby) Fade(s);
    }

    /// <summary>Entrée, when nothing else took it: opens the line to write over the view.</summary>
    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (lobby || open || e is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Enter or Key.KpEnter } || Session.Current is null) return;
        GetViewport().SetInputAsHandled();
        open = true;
        input.Visible = true;
        input.GrabFocus();
        Fill(Session.Current.Chat);
    }

    void Submit(string text)
    {
        Session.Current?.Say(text);
        input.Clear();
        if (!lobby) Close();
    }

    void Close()
    {
        open = false;
        input.ReleaseFocus();
        input.Visible = false;
        if (Session.Current is { } s) Fill(s.Chat);
    }

    /// <summary>The newest lines, each its author's name then its text; the game's own lines muted.</summary>
    void Fill(ChatLog log)
    {
        foreach (var child in lines.GetChildren()) child.QueueFree();
        var all = log.Lines;
        var n = lobby || open ? Many : Few;
        for (var i = Math.Max(0, all.Count - n); i < all.Count; i++)
        {
            var line = all[i];
            var label = UiTheme.Text(line.Who.Length > 0 ? $"{line.Who} : {line.Text}" : line.Text, line.Who.Length > 0 ? Palette.Text : Palette.Muted, 14);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.MouseFilter = Control.MouseFilterEnum.Ignore;
            label.SetMeta("at", line.At);
            if (!lobby)
            {
                // Readable over the ships passing under it.
                label.AddThemeConstantOverride("outline_size", 4);
                label.AddThemeColorOverride("font_outline_color", Palette.Bg);
            }
            lines.AddChild(label);
        }
        if (lobby && all.Count == 0) lines.AddChild(UiTheme.Text("Personne n'a encore rien écrit.", Palette.Muted, 13));
    }

    /// <summary>Over the view, a line fades out some seconds after it came, unless the line to write is open.</summary>
    void Fade(Session s)
    {
        foreach (var child in lines.GetChildren())
        {
            if (child is not Label label) continue;
            var age = s.Clock - (double)label.GetMeta("at");
            var a = open ? 1 : age < Shown ? 1 : Math.Max(0, 1 - (age - Shown) / Fading);
            label.Modulate = new Color(1, 1, 1, (float)a);
        }
    }
}
