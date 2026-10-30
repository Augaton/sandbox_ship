// Main menu: a column on the left over a fight that plays behind it, then the new game's modes
// and lobby (Menu.Lobby.cs), the options, the controls and the credits in a wider panel that
// slides out from it. Started as a port of src/ui/menu.ts; the layout and the backdrop fight
// are this version's.
using System.Globalization;
using Godot;
using SandboxShip.Battle;
using SandboxShip.Core;
using SandboxShip.Sim;
using SandboxShip.Sim.Data;

namespace SandboxShip.Ui;

public partial class Menu : Node
{
    /// <summary>The game's version, as in package.json (which the web version reads at build time).</summary>
    public const string Version = "0.4.0";

    enum Screen { Home, Modes, Lobby, Options, Controls, Credits, Multiplayer }

    /// <summary>Width of the panel: the home column, the wider panel of the other screens, a little more for the lobby's six rows.</summary>
    const float HomeWidth = 420, Width = 780, LobbyWidth = 860;
    /// <summary>The panel's padding; on the right, the scroll bar takes the gutter.</summary>
    const float PadX = 48, PadTop = 44, PadBottom = 28, Gutter = 14;
    /// <summary>The veil fades out over this many pixels past the panel.</summary>
    const float Fade = 220;
    const double Slide = 0.24, Appear = 0.18;

    static int NewSeed() => Random.Shared.Next(1, 1_000_000);

    Screen screen;
    Control root = null!, veil = null!, frame = null!;
    VBoxContainer header = null!, body = null!;
    HBoxContainer footer = null!;
    ScrollContainer scroll = null!;
    float width = HomeWidth;
    Tween? sliding, appearing;
    Control? preview;
    /// <summary>The buttons of the screen shown: the first one, and those a click rebuilds the screen from.</summary>
    Button? first;
    readonly Dictionary<string, Button> keyed = [];
    /// <summary>The home entry that opened the screen shown: it has the focus again on the way back.</summary>
    string? opener;
    /// <summary>Whether the last input was a key: only then does the focused button show it.</summary>
    bool viaKeys;

    /// <summary>The fight behind the menu, a new one each time the last one is over.</summary>
    BattleScreen? demo;
    ColorRect curtain = null!;
    HBoxContainer caption = null!;
    bool nextRound;

    public override void _Ready()
    {
        NewRound();
        var under = new CanvasLayer { Layer = 1 };
        AddChild(under);
        curtain = new ColorRect { Color = Palette.Bg, MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = Colors.Transparent };
        curtain.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        under.AddChild(curtain);

        var ui = new CanvasLayer { Layer = 2 };
        AddChild(ui);
        root = new Control { Theme = MenuTheme() };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        ui.AddChild(root);
        veil = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        veil.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        veil.Draw += DrawVeil;
        root.AddChild(veil);

        caption = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        caption.AddThemeConstantOverride("separation", 8);
        caption.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        caption.GrowHorizontal = Control.GrowDirection.Begin;
        caption.GrowVertical = Control.GrowDirection.Begin;
        caption.OffsetLeft = caption.OffsetRight = -24;
        caption.OffsetTop = caption.OffsetBottom = -20;
        root.AddChild(caption);
        Caption();

        // Anchored to the screen's height: whatever its content asks for a moment, it comes back to it.
        frame = new MarginContainer();
        frame.SetAnchorsPreset(Control.LayoutPreset.LeftWide);
        frame.AddThemeConstantOverride("margin_left", (int)PadX);
        frame.AddThemeConstantOverride("margin_top", (int)PadTop);
        frame.AddThemeConstantOverride("margin_right", (int)(PadX - Gutter));
        frame.AddThemeConstantOverride("margin_bottom", (int)PadBottom);
        root.AddChild(frame);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 20);
        frame.AddChild(column);
        header = new VBoxContainer();
        header.AddThemeConstantOverride("separation", 8);
        column.AddChild(header);
        scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        column.AddChild(scroll);
        var content = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("margin_right", (int)Gutter);
        scroll.AddChild(content);
        body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 22);
        content.AddChild(body);
        // The footer ends where the content does, short of the scroll bar's gutter.
        var foot = new MarginContainer();
        foot.AddThemeConstantOverride("margin_right", (int)Gutter);
        column.AddChild(foot);
        footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 8);
        foot.AddChild(footer);
        root.Resized += Fit;

        foreach (var l in Remembered()) lobbies[l.Mode] = l;
        var start = Dev.Menu switch
        {
            "new" => Screen.Modes,
            "controls" => Screen.Controls,
            "credits" => Screen.Credits,
            "options" => Screen.Options,
            "multiplayer" or "join" => Screen.Multiplayer,
            "skirmish" => Screen.Lobby,
            var m when m.StartsWith("lobby-", StringComparison.Ordinal) => Screen.Lobby,
            _ => Screen.Home,
        };
        if (start is Screen.Modes or Screen.Lobby) opener = "new";
        if (start == Screen.Lobby) DevLobby(Dev.Menu == "skirmish" ? LobbyMode.Skirmish : Enum.Parse<LobbyMode>(Dev.Menu["lobby-".Length..], true));
        start = NetStart(start);
        width = WidthOf(start);
        Show(start, false);
    }

    public override void _Input(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true }) viaKeys = true;
        else if (e is InputEventMouseButton { Pressed: true }) viaKeys = false;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!e.IsActionPressed("ui_cancel") || screen == Screen.Home) return;
        GetViewport().SetInputAsHandled();
        Back();
    }

    public override void _Process(double delta)
    {
        NetTick();
        if (demo is null) return;
        demo.DemoInset = width;
        if (demo.DemoOver && !nextRound) CrossRound();
    }

    // The fight behind.

    void NewRound()
    {
        var next = BattleScreen.Create(new BattleOptions { Demo = true, Seed = Random.Shared.Next(1, 1_000_000_000) });
        next.DemoInset = width;
        if (demo is not null)
        {
            demo.ProcessMode = ProcessModeEnum.Disabled;
            demo.QueueFree();
        }
        demo = next;
        AddChild(demo);
        // Under the menu's layers, whatever the order of the children.
        MoveChild(demo, 0);
    }

    /// <summary>The fight is over: the view goes dark, a new one starts, the view comes back.</summary>
    async void CrossRound()
    {
        nextRound = true;
        var dark = CreateTween();
        dark.TweenProperty(curtain, "modulate:a", 1.0, 0.8);
        dark.Parallel().TweenProperty(caption, "modulate:a", 0.0, 0.8);
        await ToSignal(dark, Tween.SignalName.Finished);
        if (!IsInsideTree()) return;
        NewRound();
        Caption();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var light = CreateTween();
        light.TweenProperty(curtain, "modulate:a", 0.0, 1.0);
        light.Parallel().TweenProperty(caption, "modulate:a", 1.0, 1.0);
        nextRound = false;
    }

    /// <summary>Who fights behind the menu, at the bottom right.</summary>
    void Caption()
    {
        foreach (var child in caption.GetChildren()) child.QueueFree();
        if (demo is null) return;
        var sides = demo.Opts.Demo && demo.World is { } w ? w.Factions.Values.ToList() : [];
        for (var i = 0; i < sides.Count; i++)
        {
            if (i > 0 && i == sides.Count - 1) caption.AddChild(Shadowed("contre"));
            caption.AddChild(Picture(Logos.Emblem(sides[i].Race, 1, sides[i].Color), new Vector2(16, 16)));
            caption.AddChild(Shadowed(Factions.Defs[sides[i].Race].Name + (i < sides.Count - 2 ? "," : "")));
        }
    }

    /// <summary>Small text that stays readable over the ships passing under it.</summary>
    static Label Shadowed(string text)
    {
        var l = UiTheme.Text(text, Palette.Muted, 13);
        l.AddThemeConstantOverride("outline_size", 4);
        l.AddThemeColorOverride("font_outline_color", Palette.Bg);
        return l;
    }

    /// <summary>The veil under the panel: dark enough to read on, then fading into the fight.</summary>
    void DrawVeil()
    {
        var h = veil.Size.Y;
        var solid = Palette.Bg with { A = 0.9f };
        var clear = Palette.Bg with { A = 0 };
        veil.DrawRect(new Rect2(0, 0, width, h), solid);
        veil.DrawPolygon([new(width, 0), new(width + Fade, 0), new(width + Fade, h), new(width, h)], [solid, clear, clear, solid]);
        veil.DrawLine(new Vector2(width, 0), new Vector2(width, h), Palette.Line with { A = 0.35f }, 1);
    }

    // Screens.

    /// <summary>The panel follows its width; the veil, and the fight's framing, with it.</summary>
    void Fit()
    {
        frame.OffsetRight = width;
        veil.QueueRedraw();
    }

    void SetWidth(float w)
    {
        width = w;
        Fit();
    }

    static float WidthOf(Screen s) => s switch { Screen.Home => HomeWidth, Screen.Lobby or Screen.Multiplayer => LobbyWidth, _ => Width };

    /// <summary>Esc, or "Retour": from the lobby to the modes, from anywhere else home; the entry that led there has the focus again.</summary>
    void Back()
    {
        if (NetBack()) return;
        if (screen == Screen.Lobby)
        {
            Remember();
            Show(Screen.Modes);
            Settle(keyed.GetValueOrDefault($"mode:{mode}"), 0);
            return;
        }
        var from = opener;
        Show(Screen.Home);
        if (from is not null) Settle(keyed.GetValueOrDefault(from), 0);
    }

    /// <summary>Shows a screen: the panel slides to its width, the content fades in.</summary>
    void Show(Screen next, bool animate = true)
    {
        screen = next;
        Build();
        var to = WidthOf(next);
        sliding?.Kill();
        appearing?.Kill();
        if (animate)
        {
            sliding = CreateTween().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
            sliding.TweenMethod(Callable.From<float>(SetWidth), width, to, Slide);
            frame.Modulate = Colors.Transparent;
            appearing = CreateTween();
            appearing.TweenProperty(frame, "modulate:a", 1.0, Appear).SetDelay(Slide * 0.4);
        }
        else
        {
            SetWidth(to);
            frame.Modulate = Colors.White;
        }
        scroll.ScrollVertical = 0;
        Settle(first, 0);
    }

    /// <summary>Fills the header, the body and the footer for the screen shown.</summary>
    void Build()
    {
        foreach (var part in new Control[] { header, body, footer })
        {
            foreach (var child in part.GetChildren())
            {
                part.RemoveChild(child);
                child.QueueFree();
            }
        }
        first = null;
        keyed.Clear();
        preview = null;
        switch (screen)
        {
            case Screen.Home: Home(); break;
            case Screen.Modes: ModeCards(); break;
            case Screen.Lobby: LobbyScreen(); break;
            case Screen.Multiplayer: MultiplayerScreen(); break;
            case Screen.Options:
                Titled("Options", "Les changements s'appliquent tout de suite.");
                body.AddChild(Options.Body(true));
                footer.AddChild(Btn("Retour", Back));
                break;
            case Screen.Controls:
                Titled("Commandes");
                body.AddChild(Section("Bataille", Controls.List(Controls.Battle, 330)));
                body.AddChild(Section("Carte galactique", Controls.List(Controls.Galaxy, 330)));
                footer.AddChild(Btn("Retour", Back));
                break;
            case Screen.Credits:
                Titled("Crédits");
                body.AddChild(Paragraph("Sprites : packs Void (flottes, environnement, vaisseau principal) de Foozle, et Space Breaker de zintoki, tous en CC0. Merci à eux."));
                body.AddChild(Paragraph("Vaisseaux des Errants, Rempart, Transporteur, Titan, Léviathan et Colosse : dessinés par le jeu dans la manière de ces packs."));
                body.AddChild(Paragraph("Emblèmes, logo et jeu : Sandbox Ship."));
                footer.AddChild(Btn("Retour", Back));
                break;
        }
    }

    void Titled(string title, string? note = null)
    {
        header.AddChild(Title(title));
        var l = Note(note ?? "Échap : retour");
        l.CustomMinimumSize = new Vector2(280, 0);
        header.AddChild(l);
    }

    void Home()
    {
        opener = null;
        var save = GalaxySave.Info(Campaign.GalaxyScreen.SavePath);
        var logo = Picture(Logos.Logo(5), new Vector2(345, 45));
        logo.AccessibilityName = "Sandbox Ship";
        header.AddChild(logo);
        header.AddChild(Note("Stratégie spatiale en temps réel"));

        body.AddChild(Spacer());
        var nav = new VBoxContainer();
        nav.AddThemeConstantOverride("separation", 2);
        if (save is not null)
        {
            nav.AddChild(Nav("Continuer la campagne", () => Main.Instance.StartCampaign(new MenuChoice { Resume = true }), true));
            var when = DateTimeOffset.FromUnixTimeMilliseconds(save.SavedAt).ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
            var info = Paragraph($"{Factions.Defs[save.Race].Name}, {save.Planets} planète{(save.Planets > 1 ? "s" : "")}, le {when}", Palette.Muted, 13);
            var indent = new MarginContainer();
            indent.AddThemeConstantOverride("margin_left", 17);
            indent.AddThemeConstantOverride("margin_bottom", 10);
            indent.AddChild(info);
            nav.AddChild(indent);
        }
        nav.AddChild(Nav("Nouvelle partie", () => OpenModes(false), save is null, "new"));
        nav.AddChild(Nav("Multijoueur", () => Open(Screen.Multiplayer, "multi"), key: "multi"));
        nav.AddChild(Nav("Options", () => Open(Screen.Options, "options"), key: "options"));
        nav.AddChild(Nav("Commandes", () => Open(Screen.Controls, "controls"), key: "controls"));
        nav.AddChild(Nav("Crédits", () => Open(Screen.Credits, "credits"), key: "credits"));
        nav.AddChild(Nav("Quitter", Main.Instance.QuitGame));
        body.AddChild(nav);
        body.AddChild(Spacer(1.6f));
        footer.AddChild(UiTheme.Text($"Version {Version}", Palette.Muted, 13));
    }

    void Open(Screen next, string from)
    {
        Show(next);
        opener = from;
    }

    /// <summary>Applies a choice, then rebuilds the screen with the focus on the button just pressed.</summary>
    void Pick(string key, Action change)
    {
        change();
        Rebuild(key);
    }

    /// <summary>The key of the button last pressed: a rebuild the network asks for puts the focus back there.</summary>
    string lastKey = "";

    /// <summary>Rebuilds the screen where it was scrolled to, with the focus on the button of that key.</summary>
    void Rebuild(string key)
    {
        lastKey = key;
        var at = scroll.ScrollVertical;
        Build();
        Settle(keyed.GetValueOrDefault(key), at);
    }

    /// <summary>
    /// Once the new layout is done, a frame later: puts the focus on a button (the first one of
    /// a screen, or the one just pressed) and the scroll where it goes. The focus shows only
    /// when the keyboard is in use.
    /// </summary>
    async void Settle(Button? focus, int scrollTo)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!IsInstanceValid(this)) return;
        if (focus is not null && IsInstanceValid(focus) && focus.IsInsideTree()) focus.GrabFocus(!viaKeys);
        scroll.ScrollVertical = scrollTo;
    }

    /// <summary>A choice drawn as a card: a button under, its content on top; `on` outlines it in amber.</summary>
    Control Tile(string key, bool on, Action act, string name, Action<VBoxContainer> fill)
    {
        var tile = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var button = Btn("", act, on ? On : null, key);
        button.AccessibilityName = name;
        tile.AddChild(button);
        var pad = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        foreach (var side in new[] { "left", "top", "right", "bottom" }) pad.AddThemeConstantOverride($"margin_{side}", 12);
        tile.AddChild(pad);
        var col = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 6);
        pad.AddChild(col);
        fill(col);
        return tile;
    }

    // Building blocks, after the menu classes of src/ui/hud.css.

    /// <summary>Theme variations: the amber call to action, a choice that is on (aria-pressed), and the home entries.</summary>
    const string Primary = "MenuPrimary", On = "MenuOn", NavItem = "MenuNav", NavMain = "MenuNavMain";

    static readonly Font Bold = new SystemFont
    {
        FontNames = ((SystemFont)UiTheme.Mono).FontNames, FontWeight = 700, Antialiasing = TextServer.FontAntialiasing.Gray,
    };

    static Theme? theme;

    static StyleBoxFlat Style(Color bg, Color? border = null)
    {
        var s = UiTheme.Box(bg, border, 12);
        s.ContentMarginTop = s.ContentMarginBottom = 6;
        return s;
    }

    /// <summary>A home entry's look: bare text; under the pointer or the keys, an amber bar on its left and the text a step aside.</summary>
    static StyleBoxFlat NavStyle(bool lit)
    {
        var s = new StyleBoxFlat
        {
            BgColor = lit ? Palette.Accent with { A = 0.08f } : Colors.Transparent, BorderColor = lit ? Palette.Accent : Colors.Transparent,
            BorderWidthLeft = 3, ContentMarginLeft = lit ? 22 : 14, ContentMarginRight = 12, ContentMarginTop = 4, ContentMarginBottom = 4,
        };
        return s;
    }

    /// <summary>The shared theme, in 16 pixels as the web menu, with its variations.</summary>
    static Theme MenuTheme()
    {
        if (theme is not null) return theme;
        var t = new Theme();
        t.MergeWith(UiTheme.Get());
        t.DefaultFont = UiTheme.Mono;
        t.DefaultFontSize = 16;
        foreach (var state in new[] { "normal", "hover", "pressed", "disabled" })
        {
            var s = (StyleBox)t.GetStylebox(state, "Button").Duplicate();
            s.ContentMarginLeft = s.ContentMarginRight = 12;
            t.SetStylebox(state, "Button", s);
        }
        t.SetConstant("h_separation", "Button", 8);

        t.SetTypeVariation(Primary, "Button");
        t.SetStylebox("normal", Primary, Style(Palette.Accent, Palette.Accent));
        t.SetStylebox("hover", Primary, Style(Palette.Accent.Lightened(0.12f), Palette.Text));
        t.SetStylebox("pressed", Primary, Style(Palette.Accent.Darkened(0.15f), Palette.Accent));
        t.SetStylebox("focus", Primary, Style(new Color(0, 0, 0, 0), Palette.Text));
        foreach (var c in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "font_hover_pressed_color" }) t.SetColor(c, Primary, Palette.Bg);
        t.SetFont("font", Primary, Bold);

        t.SetTypeVariation(On, "Button");
        t.SetStylebox("normal", On, Style(Palette.Raised, Palette.Accent));
        t.SetStylebox("hover", On, Style(Palette.Raised.Lightened(0.08f), Palette.Accent));
        foreach (var c in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "font_hover_pressed_color" }) t.SetColor(c, On, Palette.Accent);

        foreach (var nav in new[] { NavItem, NavMain })
        {
            t.SetTypeVariation(nav, "Button");
            t.SetStylebox("normal", nav, NavStyle(false));
            t.SetStylebox("disabled", nav, NavStyle(false));
            t.SetStylebox("hover", nav, NavStyle(true));
            t.SetStylebox("pressed", nav, NavStyle(true));
            t.SetStylebox("hover_pressed", nav, NavStyle(true));
            t.SetStylebox("focus", nav, NavStyle(true));
            t.SetColor("font_color", nav, nav == NavMain ? Palette.Accent : Palette.Text);
            foreach (var c in new[] { "font_hover_color", "font_pressed_color", "font_focus_color", "font_hover_pressed_color" }) t.SetColor(c, nav, Palette.Accent);
            t.SetFontSize("font_size", nav, nav == NavMain ? 21 : 18);
        }
        t.SetFont("font", NavMain, Bold);

        var input = UiTheme.Box(Palette.Raised, pad: 6);
        t.SetStylebox("normal", "LineEdit", input);
        t.SetStylebox("read_only", "LineEdit", UiTheme.Box(Palette.Raised with { A = 0.5f }, pad: 6));
        t.SetStylebox("focus", "LineEdit", UiTheme.Box(new Color(0, 0, 0, 0), Palette.Accent, 6));
        t.SetColor("font_uneditable_color", "LineEdit", Palette.Muted);
        t.SetColor("caret_color", "LineEdit", Palette.Text);
        t.SetColor("selection_color", "LineEdit", Palette.Accent with { A = 0.35f });

        t.SetStylebox("disabled", "OptionButton", UiTheme.Box(Palette.Raised with { A = 0.5f }));
        t.SetColor("font_disabled_color", "OptionButton", Palette.Muted with { A = 0.6f });

        // A check box, focused from the keyboard: an amber frame around it.
        t.SetStylebox("focus", "CheckBox", UiTheme.Box(new Color(0, 0, 0, 0), Palette.Accent, 4));
        t.SetStylebox("focus", "HSlider", UiTheme.Box(new Color(0, 0, 0, 0), Palette.Accent, 2));

        t.SetStylebox("scroll", "VScrollBar", UiTheme.Box(Palette.Bg with { A = 0 }, Palette.Bg with { A = 0 }, 4));
        t.SetStylebox("grabber", "VScrollBar", UiTheme.Box(Palette.Line, Palette.Line, 4));
        t.SetStylebox("grabber_highlight", "VScrollBar", UiTheme.Box(Palette.Muted, Palette.Muted, 4));
        t.SetStylebox("grabber_pressed", "VScrollBar", UiTheme.Box(Palette.Accent, Palette.Accent, 4));
        theme = t;
        return t;
    }

    /// <summary>A button of the menu. Its action runs once the click is over, as it may rebuild the screen under it.</summary>
    Button Btn(string text, Action act, string? variation = null, string? key = null)
    {
        // Passing the mouse on lets the wheel scroll the panel over a button.
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(0, 40), FocusMode = Control.FocusModeEnum.All, MouseFilter = Control.MouseFilterEnum.Pass };
        if (variation is not null) b.ThemeTypeVariation = variation;
        b.Pressed += () => Callable.From(act).CallDeferred();
        first ??= b;
        if (key is not null) keyed[key] = b;
        return b;
    }

    /// <summary>An entry of the home column; `main`: the one to start with, in amber.</summary>
    Button Nav(string text, Action act, bool main = false, string? key = null)
    {
        var b = Btn(text, act, main ? NavMain : NavItem, key);
        b.CustomMinimumSize = new Vector2(0, main ? 48 : 42);
        b.Alignment = HorizontalAlignment.Left;
        return b;
    }

    /// <summary>Buttons side by side, one of which is on.</summary>
    HFlowContainer Group<T>(string name, IEnumerable<(T Value, string Label)> items, T current, Action<T> set, Func<T, bool>? disabled = null) where T : struct
    {
        var flow = Flow();
        foreach (var (value, label) in items)
        {
            var key = $"{name}:{value}";
            var b = Btn(label, () => Pick(key, () => set(value)), value.Equals(current) ? On : null, key);
            b.CustomMinimumSize = new Vector2(0, 44);
            b.Disabled = disabled?.Invoke(value) ?? false;
            flow.AddChild(b);
        }
        return flow;
    }

    static HFlowContainer Flow()
    {
        var flow = new HFlowContainer();
        flow.AddThemeConstantOverride("h_separation", 8);
        flow.AddThemeConstantOverride("v_separation", 8);
        return flow;
    }

    static VBoxContainer Section(string title, params Control[] items)
    {
        var s = new VBoxContainer();
        s.AddThemeConstantOverride("separation", 8);
        s.AddChild(Heading(title));
        foreach (var i in items) s.AddChild(i);
        return s;
    }

    /// <summary>A setting with its name above it.</summary>
    static VBoxContainer Labelled(string label, Control item)
    {
        var s = new VBoxContainer();
        s.AddThemeConstantOverride("separation", 4);
        s.AddChild(UiTheme.Text(label, Palette.Muted, 13));
        s.AddChild(item);
        return s;
    }

    /// <summary>Room that pushes the home entries toward the middle of the column.</summary>
    static Control Spacer(float ratio = 1) => new() { SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsStretchRatio = ratio, MouseFilter = Control.MouseFilterEnum.Ignore };

    static Label Title(string text)
    {
        var l = UiTheme.Text(text, size: 26);
        l.AddThemeFontOverride("font", Bold);
        return l;
    }

    static Label Heading(string text)
    {
        var l = UiTheme.Text(text, Palette.Text, 15);
        l.AddThemeFontOverride("font", Bold);
        return l;
    }

    static Label Strong(string text, bool on)
    {
        var l = UiTheme.Text(text, on ? Palette.Accent : Palette.Text);
        l.AddThemeFontOverride("font", Bold);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return l;
    }

    static Label Small(string text)
    {
        var l = UiTheme.Text(text, Palette.Muted, 13);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return l;
    }

    static Label Paragraph(string text, Color? color = null, int size = UiTheme.FontSize)
    {
        var l = UiTheme.Text(text, color, size);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return l;
    }

    static Label Note(string text) => Paragraph(text, Palette.Muted, 14);

    static TextureRect Picture(Texture2D tex, Vector2 size) => new()
    {
        Texture = tex, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        CustomMinimumSize = size, MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
    };
}
