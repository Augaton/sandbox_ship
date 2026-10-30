// Panels over the galaxy map: top bar, planet panel (planet, buildings, ships, research),
// planet summary under the pointer, messages and dialogs. Like the battle HUD, it reads the
// simulation and calls its commands. Port of src/ui/galaxyHud.ts; built with the battle
// HUD's blocks (Ui/HudKit.cs).
using Godot;
using SandboxShip.Core;
using SandboxShip.Sim;
using SandboxShip.Sim.Data;
using SandboxShip.Ui;

namespace SandboxShip.Campaign;

public partial class GalaxyHud : CanvasLayer
{
    const double RefreshMs = 100;
    static readonly int[] Speeds = [1, 2, 4];
    static readonly ShipClass[] ShipyardClasses = [.. Ships.All.Where(c => !Ships.Defs[c].FromHangar)];
    static readonly GalaxyBuildKind[] Buildings =
    [
        GalaxyBuildKind.Station, GalaxyBuildKind.Mine, GalaxyBuildKind.Platform, GalaxyBuildKind.PlatformTier,
        GalaxyBuildKind.Lab, GalaxyBuildKind.RepairDock, GalaxyBuildKind.IonCannon, GalaxyBuildKind.StarLance,
    ];

    enum Tab { Planet, Buildings, Ships, Research }

    static readonly (Tab Id, string Label)[] Tabs = [(Tab.Planet, "Planète"), (Tab.Buildings, "Bâtiments"), (Tab.Ships, "Vaisseaux"), (Tab.Research, "Recherche")];

    /// <summary>Toasts live this long (ms), then fade out.</summary>
    const double ToastLife = 2200, ToastFade = 400;

    readonly GalaxyScreen scene;
    Control root = null!;
    Label credits = null!, income = null!, owned = null!, researching = null!;
    readonly List<(Button B, int Speed)> speedButtons = [];
    VBoxContainer body = null!;
    VBoxContainer toasts = null!;
    readonly List<(Control Toast, double Born)> toastList = [];
    Control? modal;
    /// <summary>The dialog's first button, which the web version focuses: Enter presses it.</summary>
    Button? modalFirst;
    PanelContainer tip = null!;
    VBoxContainer tipBox = null!;
    /// <summary>Over the network, what holds the galaxy for everyone, under the top bar.</summary>
    PanelContainer banner = null!;
    Label bannerText = null!;
    Tab tab = Tab.Planet;
    double timer;
    string lastPanel = "";
    /// <summary>Planet and fleet the send quantities were typed for: they are kept only while both stay the same.</summary>
    string lastFleet = "";
    readonly List<(LabelButton B, GalaxyBuild Build)> buttons = [];
    readonly Dictionary<TechId, LabelButton> techButtons = [];
    readonly List<(ShipClass Cls, SpinBox Box)> sendShips = [];
    readonly List<(HeroId Hero, CheckBox Box)> sendHeroes = [];
    /// <summary>Parts of the panel updated in place: progress of the first build and research, fleet condition, occupation.</summary>
    ColorRect? buildBar, researchBar;
    Label? condition, occupation;

    public GalaxyHud(GalaxyScreen scene) => this.scene = scene;

    Galaxy G => scene.Galaxy;
    Side Player => scene.Player;
    /// <summary>The player's race: its ship prices and heroes.</summary>
    Faction Race => G.Factions[Player].Race;

    /// <summary>A side as the panels name it: by its race; over the network, another player's by its name too, "Bob (Nairan)".</summary>
    string NameOf(Side side)
    {
        var race = Factions.Defs[Galaxies.RaceOf(G, side)].Name;
        return side != Player && G.Humans.TryGetValue(side, out var name) ? $"{name} ({race})" : race;
    }

    static string DescribeShips(ShipCounts ships, IEnumerable<HeroId>? heroes = null)
    {
        var parts = (heroes ?? []).Select(h => $"{Heroes.Defs[h].Name} ({Heroes.Defs[h].Title.ToLowerInvariant()})")
            .Concat(ShipyardClasses.Where(c => ships.Of(c) > 0).Select(c => $"{ships.Of(c)}× {Ships.Defs[c].Name}"))
            .ToList();
        return parts.Count > 0 ? string.Join(", ", parts) : "aucun vaisseau";
    }

    /// <summary>", état 64 %" for a damaged fleet; nothing for a whole one.</summary>
    string Condition(Fleet f) => (f.Condition < 1 ? $", état {HudKit.Percent(f.Condition)}" : "") + Supply(f);

    static string N(double v) => HudKit.N(v);

    /// <summary>A reason inside a sentence: "Station requise" becomes "station requise".</summary>
    public static string Lower(string s) => s.Length > 0 ? char.ToLowerInvariant(s[0]) + s[1..] : s;

    /// <summary>" (crédits insuffisants : 2)": why some planets refused an order, nothing when none did.</summary>
    static string Refusals(IEnumerable<Result> refused)
    {
        var why = refused.GroupBy(r => r.Reason).Select(r => $"{Lower(r.Key)} : {r.Count()}").ToList();
        return why.Count > 0 ? $" ({string.Join(", ", why)})" : "";
    }

    /// <summary>Who holds a planet, as the panel and the summary name it.</summary>
    string OwnerName(Planet p) => p.Owner switch
    {
        null => "Neutre",
        Side o when o == Faction.Errant => $"{Factions.Defs[Faction.Errant].Name}, neutres",
        Side o => NameOf(o),
    };

    /// <summary>Whether the Errants' colossus still guards their planet, fighting there or not.</summary>
    static bool ColossusAt(Galaxy g, Planet p) =>
        g.Fleets.Values.Any(f => f.At == p.Id && f.Side == Faction.Errant && f.Ships.Of(ShipClass.Colossus) > 0);

    /// <summary>What a raid on the Errants pays, as a sentence.</summary>
    static string RaidBounty(bool colossus) =>
        (colossus ? $"Abattre leur Colosse rapporte {N(Raids.Bounty)} crédits et un niveau de recherche ; chaque" : "Leur Colosse est tombé ; chaque")
        + $" vaisseau abattu rapporte {HudKit.Percent(Worlds.KillShare)} de sa valeur.";

    /// <summary>A side's emblem, its race's in its color, at twice its 16 pixels as the web HUD draws it.</summary>
    public static Texture2D EmblemTexture(Galaxy g, Side side) =>
        Logos.Emblem(Galaxies.RaceOf(g, side), 2, g.Factions.TryGetValue(side, out var f) ? f.Color : null);

    // Building blocks.

    /// <summary>A label centered in its row; wrapped labels take the room left in the row.</summary>
    static Label Text(string text, Color? color = null, int size = UiTheme.FontSize, bool bold = false, bool wrap = false)
    {
        var l = HudKit.Text(text, color, size, bold, wrap);
        l.VerticalAlignment = VerticalAlignment.Center;
        if (wrap) l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return l;
    }

    static Button Btn(string text, Action act, bool primary = false)
    {
        var b = HudKit.Button(text, act);
        return primary ? HudKit.Primary(b) : b;
    }

    TextureRect Emblem(Side side) => new()
    {
        Texture = EmblemTexture(G, side), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        CustomMinimumSize = new Vector2(24, 24), MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    /// <summary>A heading, with the side's emblem before it.</summary>
    HBoxContainer Heading(string text, Side? side, int size)
    {
        var h = HudKit.Row(8);
        if (side is Side s) h.AddChild(Emblem(s));
        h.AddChild(Text(text, size: size, bold: true));
        return h;
    }

    /// <summary>A subheading of the panel, muted, a little apart from what comes before.</summary>
    static MarginContainer H3(string text, Control? after = null)
    {
        var m = HudKit.Ignore(new MarginContainer());
        m.AddThemeConstantOverride("margin_top", 6);
        var row = HudKit.Row(8);
        row.AddChild(Text(text, Palette.Muted, bold: true));
        if (after is not null) row.AddChild(after);
        m.AddChild(row);
        return m;
    }

    /// <summary>A note of the panel: a colored bar, then a bold title over its text.</summary>
    static HBoxContainer Notice(string title, string text, Color color)
    {
        var box = HudKit.Row(8);
        box.AddChild(HudKit.Ignore(new ColorRect { Color = color, CustomMinimumSize = new Vector2(3, 0) }));
        var lines = HudKit.Column(0);
        lines.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        lines.AddChild(Text(title, bold: true, wrap: true));
        lines.AddChild(Text(text, wrap: true));
        box.AddChild(lines);
        return box;
    }

    /// <summary>A grid button of the panel: its name, then its price or why not, smaller.</summary>
    static LabelButton GridButton(string name, Action act)
    {
        var b = new LabelButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MinSize = new Vector2(0, 40) };
        b.Add(name, Palette.Text, 14, wrap: true);
        b.Add("", Palette.Accent, 12, wrap: true);
        HudKit.OnPress(b, act);
        return b;
    }

    static GridContainer Grid()
    {
        var g = HudKit.Ignore(new GridContainer { Columns = 2 });
        g.AddThemeConstantOverride("h_separation", 6);
        g.AddThemeConstantOverride("v_separation", 6);
        return g;
    }

    /// <summary>
    /// A queue item: a right click cancels it, a stray left click keeps it (and, as a right
    /// click on the web, makes no click sound). The first one shows its progress.
    /// </summary>
    static Button QueueButton(string name, Action cancel, out ColorRect bar)
    {
        var b = new Button
        {
            Text = name, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 32), ButtonMask = MouseButtonMask.Right,
        };
        b.Pressed += cancel;
        b.SetMeta("label", name);
        bar = HudKit.ProgressBar();
        b.AddChild(bar);
        return b;
    }

    /// <summary>
    /// Tabs are narrow, four abreast: 13 pixel text and 2 pixels of padding, as in the web panel.
    /// The chosen one has an amber border and text.
    /// </summary>
    static void TabStyle(Button b, bool chosen)
    {
        var border = chosen ? Palette.Accent : Palette.Line;
        StyleBoxFlat Box(Color bg, Color line) => UiTheme.Box(bg, line, pad: 2);
        b.AddThemeStyleboxOverride("normal", Box(Palette.Raised, border));
        b.AddThemeStyleboxOverride("hover", Box(Palette.Raised.Lightened(0.08f), chosen ? border : Palette.Accent with { A = 0.6f }));
        b.AddThemeStyleboxOverride("pressed", Box(Palette.Raised.Darkened(0.15f), Palette.Accent));
        b.AddThemeStyleboxOverride("disabled", Box(Palette.Raised with { A = 0.5f }, Palette.Line));
        b.AddThemeFontSizeOverride("font_size", 13);
        if (!chosen) return;
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color" }) b.AddThemeColorOverride(state, Palette.Accent);
    }

    /// <summary>The element under way: how far it is, and how many seconds are left on its button.</summary>
    static void Progress(ColorRect? bar, double done, double left)
    {
        if (bar is null || !IsInstanceValid(bar)) return;
        HudKit.Fill(bar, done);
        if (bar.GetParent() is not Button b) return;
        var text = $"{b.GetMeta("label").AsString()} ({N(Math.Ceiling(left))} s)";
        if (b.Text != text) b.Text = text;
    }

    public override void _Ready()
    {
        Layer = 1;
        root = new Control { Theme = UiTheme.Get(), MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);
        var f = Factions.Defs[Race];

        var bar = HudKit.Panel(8, 4);
        bar.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        bar.OffsetLeft = 8;
        bar.OffsetRight = -8;
        bar.OffsetTop = 8;
        root.AddChild(bar);
        var row = HudKit.Row(16);
        bar.AddChild(row);
        var faction = Heading(f.Name, Player, UiTheme.FontSize);
        faction.MouseFilter = Control.MouseFilterEnum.Pass;
        faction.TooltipText = $"{f.Trait.Name} : {f.Trait.Description}";
        row.AddChild(faction);
        var money = HudKit.Row(8);
        money.AddChild(Text("Crédits"));
        credits = Text("", Palette.Accent, bold: true);
        money.AddChild(credits);
        income = Text("", Palette.Muted);
        money.AddChild(income);
        owned = Text("", Palette.Muted);
        money.AddChild(owned);
        row.AddChild(money);
        researching = Text("", Palette.Muted);
        // It takes the room left in the bar, and is cut short rather than push the buttons out.
        researching.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        researching.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        researching.SizeFlagsStretchRatio = 20;
        row.AddChild(researching);
        row.AddChild(HudKit.Ignore(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }));
        row.AddChild(DrawerButtons());
        var speeds = HudKit.Row(4);
        row.AddChild(speeds);
        var pause = Btn("Pause", scene.TogglePause);
        pause.TooltipText = "Pause (Espace)";
        speedButtons.Add((pause, 0));
        foreach (var s in Speeds) speedButtons.Add((Btn($"×{s}", () => scene.SetSpeed(s)), s));
        foreach (var (b, _) in speedButtons) speeds.AddChild(b);
        var group = HudKit.Row(4);
        row.AddChild(group);
        group.AddChild(Btn("Aide", ShowHelp));
        group.AddChild(Btn("Menu", ConfirmMenu));

        var panel = HudKit.Panel(12, 12);
        panel.AnchorLeft = 1;
        panel.AnchorRight = 1;
        panel.AnchorBottom = 1;
        panel.OffsetLeft = -8 - 340;
        panel.OffsetRight = -8;
        panel.OffsetTop = 64;
        panel.OffsetBottom = -8;
        root.AddChild(panel);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = false };
        panel.AddChild(scroll);
        body = HudKit.Column(8);
        body.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(body);
        BuildDrawer();

        banner = HudKit.Ignore(HudKit.Panel(12, 6));
        banner.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        banner.GrowHorizontal = Control.GrowDirection.Both;
        banner.OffsetTop = 64;
        bannerText = Text("", Palette.Accent, bold: true);
        banner.AddChild(bannerText);
        banner.Visible = false;
        root.AddChild(banner);

        toasts = HudKit.Column(4);
        toasts.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        toasts.GrowHorizontal = Control.GrowDirection.Both;
        toasts.OffsetTop = 112;
        root.AddChild(toasts);

        tip = HudKit.Ignore(HudKit.Panel(10, 8));
        tip.Visible = false;
        tipBox = HudKit.Column(0);
        tip.AddChild(tipBox);
        root.AddChild(tip);

        RefreshModes();
        Refresh();
    }

    public void Toast(string text)
    {
        var t = HudKit.Ignore(HudKit.Panel(12, 6));
        t.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        // A long message (an order to several planets) wraps rather than run under the planet panel.
        t.AddChild(HudKit.Para(text, max: 520));
        toasts.AddChild(t);
        toastList.Add((t, Godot.Time.GetTicksMsec()));
        while (toastList.Count > 4)
        {
            toastList[0].Toast.QueueFree();
            toastList.RemoveAt(0);
        }
    }

    public void RefreshModes()
    {
        // Over the network, the host sets the speed for everyone; anyone pauses.
        var host = scene.Opts.Net is not { Replica: true };
        foreach (var (b, s) in speedButtons)
        {
            HudKit.Outline(b, (s == 0 ? scene.Paused : !scene.Paused && scene.Speed == s) ? Palette.Accent : null);
            if (s != 0) b.Disabled = !host;
            b.TooltipText = s == 0 ? scene.PausedBy is { } who ? $"En pause, demandée par {who} (Espace)" : "Pause (Espace)"
                : host ? $"Vitesse ×{s}" : "L'hôte règle la vitesse pour tous";
        }
    }

    /// <summary>Over the network, a line under the top bar while something holds the galaxy for everyone; none hides it.</summary>
    public void Banner(string? text)
    {
        banner.Visible = text is not null;
        if (text is not null && bannerText.Text != text) bannerText.Text = text;
    }

    public void Tick(double delta)
    {
        var now = Godot.Time.GetTicksMsec();
        for (var i = toastList.Count - 1; i >= 0; i--)
        {
            var (t, born) = toastList[i];
            var age = now - born;
            if (age >= ToastLife + ToastFade)
            {
                t.QueueFree();
                toastList.RemoveAt(i);
            }
            else if (age > ToastLife) t.Modulate = new Color(1, 1, 1, (float)Math.Max(0, 1 - (age - ToastLife) / 300));
        }
        timer -= delta;
        if (timer > 0) return;
        timer = RefreshMs;
        Refresh();
    }

    /// <summary>A click in the panel acts, then the panel follows, once the button's signal is over.</summary>
    void RefreshSoon() => Callable.From(() =>
    {
        if (IsInstanceValid(this) && IsInsideTree()) Refresh();
    }).CallDeferred();

    /// <summary>The player's planets among those the orders go to.</summary>
    List<int> OwnTargets() => [.. scene.Targets.Where(id => G.Planets[id].Owner == Player)];

    /// <summary>A ship goes to every selected planet, one each; heroes and buildings, to the planet shown.</summary>
    void Enqueue(GalaxyBuild build)
    {
        List<int> targets = build.Kind == GalaxyBuildKind.Ship ? OwnTargets() : scene.Selected is int shown ? [shown] : [];
        if (targets.Count == 1)
        {
            var r = scene.Sink.Send(new GalaxyCommand.EnqueueBuild(targets[0], build));
            if (!r.Ok) Toast(r.Reason);
        }
        else if (targets.Count > 1)
        {
            var refused = targets.Select(id => scene.Sink.Send(new GalaxyCommand.EnqueueBuild(id, build))).Where(r => !r.Ok).ToList();
            var took = targets.Count - refused.Count;
            var name = Ships.Defs[build.Cls].Name;
            Toast(took switch
            {
                0 => $"{name} : aucune planète n'a pris l'ordre{Refusals(refused)}",
                1 => $"{name} : 1 planète sur {targets.Count} a pris l'ordre{Refusals(refused)}",
                _ => $"{name} : {took} planètes sur {targets.Count} ont pris l'ordre{Refusals(refused)}",
            });
        }
        RefreshSoon();
    }

    /// <summary>
    /// Right click on a ship: keeps it in production at every selected planet that can build it,
    /// or, if they all do already, stops it there.
    /// </summary>
    void ToggleLoop(ShipClass cls)
    {
        var g = G;
        var targets = OwnTargets();
        if (targets.Count == 0) return;
        var name = Ships.Defs[cls].Name;
        var looping = targets.Where(id => g.Planets[id].Repeat.Contains(cls)).ToList();
        var able = targets.Where(id => Galaxies.CanLoop(g, Player, id, cls).Ok).ToList();
        if (looping.Count > 0 && able.All(looping.Contains))
        {
            foreach (var id in looping) scene.Sink.Send(new GalaxyCommand.SetLoop(id, cls, false));
            Toast($"{name} : production en boucle arrêtée{(targets.Count > 1 ? $" sur {looping.Count} planète{(looping.Count > 1 ? "s" : "")}" : "")}");
        }
        else if (targets.Count == 1)
        {
            var r = scene.Sink.Send(new GalaxyCommand.SetLoop(targets[0], cls, true));
            Toast(r.Ok ? $"{name} : production en boucle lancée" : r.Reason);
        }
        else
        {
            var refused = targets.Select(id => scene.Sink.Send(new GalaxyCommand.SetLoop(id, cls, true))).Where(r => !r.Ok).ToList();
            var took = targets.Count - refused.Count;
            Toast(took == 0
                ? $"{name} : production en boucle impossible{Refusals(refused)}"
                : $"{name} : production en boucle lancée sur {took} planète{(took > 1 ? "s" : "")} sur {targets.Count}{Refusals(refused)}");
        }
        RefreshSoon();
    }

    void Cancel(int index, QueueSide which)
    {
        if (scene.Selected is int planet) scene.Sink.Send(new GalaxyCommand.CancelBuild(planet, index, which));
        RefreshSoon();
    }

    void Research(TechId tech)
    {
        var r = scene.Sink.Send(new GalaxyCommand.StartTech(tech));
        if (!r.Ok) Toast(r.Reason);
        RefreshSoon();
    }

    /// <summary>Ships and heroes to send, as chosen in the fleet section of the panel (everything by default).</summary>
    public (ShipCounts Ships, List<HeroId> Heroes) SendCounts()
    {
        var ships = new ShipCounts();
        foreach (var (cls, box) in sendShips)
        {
            var n = (int)Math.Max(0, Math.Min(box.MaxValue, Math.Floor(box.Value)));
            if (n > 0) ships[cls] = n;
        }
        return (ships, [.. sendHeroes.Where(h => h.Box.ButtonPressed).Select(h => h.Hero)]);
    }

    public void Refresh()
    {
        var g = G;
        var player = Player;
        var f = g.Factions[player];
        var n = g.Planets.Count(p => p.Owner == player);
        credits.Text = N(Math.Floor(f.Credits));
        income.Text = $"+{N(Js.Round(Galaxies.IncomeOf(g, player)))}/s";
        owned.Text = $"{n} planète{(n > 1 ? "s" : "")}";
        var job = f.Researching;
        var waiting = f.ResearchQueue.Count;
        researching.Text = job is not null
            ? $"Recherche : {Techs.Defs[job.Tech].Name} {HudKit.Percent(1 - job.Remaining / job.Total)}{(waiting > 0 ? $", {waiting} en file" : "")}"
            : "";
        var planet = scene.Selected is int id ? g.Planets[id] : null;
        var sig = Signature(planet);
        if (sig != lastPanel)
        {
            lastPanel = sig;
            RenderPanel(planet);
        }
        UpdatePanel(planet);
        UpdateExtras(planet);
        UpdateLance(planet);
    }

    /// <summary>Changes that need the panel rebuilt; everything else is updated in place.</summary>
    string Signature(Planet? p)
    {
        var g = G;
        var f = g.Factions[Player];
        var research = $"{string.Concat(Techs.All.Select(t => f.Research[t]))}:{string.Join(",", ResearchQueue.Jobs(f).Select(j => j.Tech))}";
        if (p is null) return $"{tab}|none|{research}|{TransitSignature(Transit())}";
        var fleets = string.Join(";", Galaxies.FleetsAt(g, p.Id).Select(fl => $"{fl.Side}:{DescribeShips(fl.Ships, fl.Heroes)}"));
        var battle = Galaxies.BattleAt(g, p.Id);
        var visible = Galaxies.Known(g, Player, p);
        // Several planets selected: which, how many of them are the player's, and their fleets.
        var selection = scene.Selection.Count > 1
            ? $"{string.Join(",", scene.Selection)}/{OwnTargets().Count}/"
                + string.Join(";", scene.Selection.SelectMany(id => Galaxies.FleetsAt(g, id, Player)).Select(fl => DescribeShips(fl.Ships, fl.Heroes)))
            : "";
        return string.Join("|", tab, p.Id, visible, p.Owner, p.Station, p.Mines, p.Platforms, p.PlatformTier, p.Lab, p.RepairDock, p.IonCannon, fleets,
            string.Join(",", p.Units.Select(q => q.Kind)), string.Join(",", p.Buildings.Select(q => q.Kind)), battle, p.Occupation?.Side, research, selection, DepthSignature(p));
    }

    void RenderPanel(Planet? p)
    {
        var own = p?.Owner == Player;
        // Quantities typed in the fleet section, kept if the same fleet is shown again.
        var previous = sendShips.ToDictionary(x => x.Cls, x => x.Box.Value);
        var checkedHeroes = sendHeroes.ToDictionary(x => x.Hero, x => x.Box.ButtonPressed);
        foreach (var c in body.GetChildren())
        {
            body.RemoveChild(c);
            c.QueueFree();
        }
        buttons.Clear();
        techButtons.Clear();
        sendShips.Clear();
        sendHeroes.Clear();
        buildBar = researchBar = null;
        condition = occupation = null;
        eta = rallyLabel = null;
        rallySet = rallyClear = null;
        if (tab is Tab.Buildings or Tab.Ships && !own) tab = Tab.Planet;

        var many = scene.Selection.Count;
        if (p is not null)
        {
            body.AddChild(Heading(p.Name, p.Owner, 20));
            body.AddChild(Text(OwnerName(p), Palette.Muted));
            if (many > 1) body.AddChild(Text($"Sélection : {many} planètes", Palette.Accent, bold: true));
        }
        else if (Transit() is not null) body.AddChild(Heading("Flotte en route", Player, 20));
        else body.AddChild(Heading("Carte galactique", null, 20));
        var tabs = HudKit.Ignore(new GridContainer { Columns = 4 });
        tabs.AddThemeConstantOverride("h_separation", 4);
        foreach (var (id, label) in Tabs)
        {
            var b = HudKit.Button(label, () =>
            {
                tab = id;
                RefreshSoon();
            }, height: 36);
            b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            b.ClipText = true;
            b.Disabled = id is Tab.Buildings or Tab.Ships && !own;
            TabStyle(b, tab == id);
            tabs.AddChild(b);
        }
        body.AddChild(tabs);

        if (tab == Tab.Research) RenderResearch();
        else if (p is null && Transit() is { } moving) RenderTransit(moving);
        else if (p is null)
        {
            body.AddChild(Text("Clic sur une planète : la sélectionner. Maj + clic, ou glisser sur la carte : en sélectionner plusieurs.\n"
                + "Clic droit sur une autre : y envoyer ta flotte.", Palette.Muted, wrap: true));
            body.AddChild(Text("Une flotte seule sur une planète sans défense la prend après quelques secondes.", Palette.Muted, wrap: true));
        }
        else if (tab == Tab.Planet) RenderPlanet(p, previous, checkedHeroes);
        else if (tab == Tab.Ships)
        {
            var yards = OwnTargets().Count;
            body.AddChild(Text(yards > 1
                ? $"Clic : un vaisseau sur chacune des {yards} planètes. Clic droit : production en boucle sur toutes. Les héros, sur {p.Name} seulement."
                : "Clic droit sur un vaisseau : production en boucle, qui attend les crédits.", Palette.Muted, wrap: true));
            var builds = ShipyardClasses.Select(GalaxyBuild.Ship).Concat(Heroes.Of(Race).Select(h => GalaxyBuild.HeroOf(h.Id)));
            RenderBuilds(p, [.. builds]);
        }
        else
        {
            if (many > 1) body.AddChild(Text($"Sur {p.Name} seulement.", Palette.Muted, wrap: true));
            RenderBuilds(p, [.. Buildings.Select(GalaxyBuild.Of)]);
        }
    }

    void RenderPlanet(Planet p, Dictionary<ShipClass, double> previous, Dictionary<HeroId, bool> checkedHeroes)
    {
        var g = G;
        var player = Player;
        if (p.Owner == Faction.Errant)
        {
            body.AddChild(Notice("Site de raid neutre",
                "Les Errants gardent cette planète sans jamais en bouger, ni construire ; ils ne comptent pas pour la victoire. "
                + $"{RaidBounty(ColossusAt(g, p))} Vaincus, ils laissent la planète au vainqueur.", Palette.Of(Faction.Errant)));
        }
        if (p.Special is PlanetSpecial special)
        {
            var def = GalaxyMap.Specials[special];
            body.AddChild(Notice(def.Name, def.Description, Palette.Accent));
        }
        foreach (var (title, text, color) in DepthNotes(p)) body.AddChild(Notice(title, text, color));
        if (!Galaxies.Known(g, player, p))
        {
            body.AddChild(Text("Hors de portée de tes capteurs : défenses et flottes inconnues. Approche une flotte ou prends une planète voisine pour voir.", Palette.Muted, wrap: true));
            return;
        }
        var fromStation = p.Station * GalaxyRules.StationIncome;
        var perMine = GalaxyRules.MineIncome * (p.Special == PlanetSpecial.Rich ? 2 : 1);
        body.AddChild(Text($"Revenu +{N(p.Income)}/s{(fromStation > 0 ? $", +{N(fromStation)}/s de la station" : "")}, plus {N(perMine)}/s par mine", wrap: true));
        body.AddChild(Text($"Mines {p.Mines} / {p.MineSlots}", wrap: true));
        body.AddChild(Text($"{(p.Station > 0 ? $"Station niveau {p.Station}" : "Pas de station")}, plateformes {p.Platforms} / {Galaxies.PlatformCap(p)}", wrap: true));
        var extra = new List<string>();
        if (p.Lab > 0) extra.Add("laboratoire");
        if (p.RepairDock > 0) extra.Add("dock de réparation");
        if (p.IonCannon > 0) extra.Add("canon ionique");
        if (extra.Count > 0) body.AddChild(Text($"Aussi : {string.Join(", ", extra)}", wrap: true));
        if (p.Owner == player && p.Station > 0) RenderRally();
        if (p.Owner == player && p.StarLance > 0) RenderLance(p);
        if (p.Occupation is not null)
        {
            occupation = Text("", wrap: true);
            body.AddChild(occupation);
        }
        if (Galaxies.BattleAt(g, p.Id)) body.AddChild(Text("Combat en cours", bold: true));

        if (scene.Selection.Count > 1) RenderSelectionFleets();
        else RenderFleet(p, previous, checkedHeroes);
        // Fleets hidden in a nebula do not show.
        foreach (var f in Galaxies.FleetsAt(g, p.Id).Where(x => x.Side != player && Galaxies.FleetVisible(g, player, x)))
        {
            body.AddChild(H3($"Flotte {NameOf(f.Side)}"));
            body.AddChild(Text($"{DescribeShips(f.Ships, f.Heroes)}{Condition(f)}", wrap: true));
        }
    }

    /// <summary>The player's fleets at the selected planets: a right click sends them all, whole.</summary>
    void RenderSelectionFleets()
    {
        var g = G;
        body.AddChild(H3("Flottes de la sélection"));
        var any = false;
        foreach (var id in scene.Selection)
        {
            foreach (var f in Galaxies.FleetsAt(g, id, Player))
            {
                body.AddChild(Text($"{g.Planets[id].Name} : {DescribeShips(f.Ships, f.Heroes)}", wrap: true));
                any = true;
            }
        }
        body.AddChild(Text(any ? "Clic droit sur une planète : y envoyer toutes ces flottes, entières." : "Aucune flotte à toi sur ces planètes.", Palette.Muted, wrap: true));
    }

    /// <summary>The player's fleet at the planet shown, with how many of each ship to send.</summary>
    void RenderFleet(Planet p, Dictionary<ShipClass, double> previous, Dictionary<HeroId, bool> checkedHeroes)
    {
        var g = G;
        var player = Player;
        var own = Galaxies.FleetsAt(g, p.Id, player).FirstOrDefault();
        var fleetSig = own is not null ? $"{p.Id}:{DescribeShips(own.Ships, own.Heroes)}" : "";
        if (fleetSig != lastFleet)
        {
            previous.Clear();
            checkedHeroes.Clear();
        }
        lastFleet = fleetSig;
        if (own is not null)
        {
            condition = Text("", Palette.Muted, 12);
            body.AddChild(H3("Ta flotte", condition));
            foreach (var c in ShipyardClasses)
            {
                var n = own.Ships.Of(c);
                if (n == 0) continue;
                var row = HudKit.Row(8);
                row.AddChild(Text($"{Ships.Defs[c].Name} ({n})", wrap: true));
                var box = new SpinBox
                {
                    MinValue = 0, MaxValue = n, Step = 1, Rounded = true, UpdateOnTextChanged = true,
                    Value = Math.Min(n, previous.TryGetValue(c, out var v) ? v : n),
                    CustomMinimumSize = new Vector2(72, 36), TooltipText = $"{Ships.Defs[c].Name} à envoyer",
                };
                var edit = box.GetLineEdit();
                // Enter confirms the number and gives the keys back to the map (Space: pause).
                edit.TextSubmitted += _ => edit.ReleaseFocus();
                row.AddChild(box);
                body.AddChild(row);
                sendShips.Add((c, box));
            }
            foreach (var h in own.Heroes)
            {
                var row = HudKit.Row(8);
                row.AddChild(Text($"{Heroes.Defs[h].Name} ({Heroes.Defs[h].Title.ToLowerInvariant()})", wrap: true));
                var box = new CheckBox { ButtonPressed = checkedHeroes.GetValueOrDefault(h, true), FocusMode = Control.FocusModeEnum.None };
                row.AddChild(box);
                body.AddChild(row);
                sendHeroes.Add((h, box));
            }
            body.AddChild(Text("Clic droit sur une planète : envoyer ces vaisseaux.", Palette.Muted, wrap: true));
        }
    }

    void RenderBuilds(Planet p, List<GalaxyBuild> builds)
    {
        var which = builds[0].Kind == GalaxyBuildKind.Ship ? QueueSide.Units : QueueSide.Buildings;
        var grid = Grid();
        foreach (var build in builds)
        {
            var b = GridButton(Galaxies.BuildName(p, build), () => Enqueue(build));
            if (build.Kind == GalaxyBuildKind.Hero)
            {
                var hero = Heroes.Defs[build.Hero];
                var power = Abilities.Defs[hero.Ability];
                b.TooltipText = $"{hero.Description} {power.Name} : {power.Description}";
            }
            if (build.Kind == GalaxyBuildKind.Ship)
            {
                // Right click: keep this class in production, as in battle; it works on a greyed button too.
                b.GuiInput += ev =>
                {
                    if (ev is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }) return;
                    b.AcceptEvent();
                    ToggleLoop(build.Cls);
                };
            }
            grid.AddChild(b);
            buttons.Add((b, build));
        }
        body.AddChild(grid);
        var queue = which == QueueSide.Units ? p.Units : p.Buildings;
        var flow = HudKit.Flow(4);
        if (queue.Count == 0) flow.AddChild(Text("File vide", Palette.Muted));
        else
        {
            flow.AddChild(Text(scene.Selection.Count > 1 ? $"File de {p.Name} (clic droit : annuler)" : "File (clic droit : annuler)", Palette.Muted));
            // Queued buildings are named as they were ordered: "Station", not "Améliorer la station".
            var bare = new Planet { PlatformTier = p.PlatformTier };
            for (var i = 0; i < queue.Count; i++)
            {
                var q = queue[i];
                var name = q.Kind == GalaxyBuildKind.Ship ? Ships.OrderName(q.Cls) : Galaxies.BuildName(bare, q.Build);
                var index = i;
                flow.AddChild(QueueButton(name, () => Cancel(index, which), out var bar));
                if (i == 0) buildBar = bar;
            }
        }
        body.AddChild(flow);
    }

    void RenderResearch()
    {
        var g = G;
        var player = Player;
        var f = g.Factions[player];
        var speed = Galaxies.ResearchSpeed(g, player);
        body.AddChild(Text("Recherche commune à toute ta faction. Il faut un laboratoire, et une station du niveau visé (niveau 4 pour les derniers niveaux)."
            + (speed > 1 ? " Les archives anciennes accélèrent la recherche." : ""), Palette.Muted, wrap: true));
        var grid = Grid();
        foreach (var tech in Techs.All)
        {
            var b = GridButton($"{Techs.Defs[tech].Name} {f.Research[tech]}/{Techs.Defs[tech].MaxLevel}", () => Research(tech));
            b.TooltipText = Techs.Defs[tech].Effect;
            grid.AddChild(b);
            techButtons[tech] = b;
        }
        body.AddChild(grid);
        // Each queued level is named by the level it reaches.
        var reached = Techs.All.ToDictionary(t => t, t => f.Research[t]);
        var jobs = ResearchQueue.Jobs(f);
        var flow = HudKit.Flow(4);
        if (jobs.Count == 0) flow.AddChild(Text("Aucune recherche en file", Palette.Muted));
        else
        {
            flow.AddChild(Text("File (clic droit : annuler)", Palette.Muted));
            for (var i = 0; i < jobs.Count; i++)
            {
                var name = $"{Techs.Defs[jobs[i].Tech].Name} {++reached[jobs[i].Tech]}";
                var index = i;
                flow.AddChild(QueueButton(name, () =>
                {
                    scene.Sink.Send(new GalaxyCommand.CancelTech(index));
                    RefreshSoon();
                }, out var bar));
                if (i == 0) researchBar = bar;
            }
        }
        body.AddChild(flow);
    }

    void UpdatePanel(Planet? p)
    {
        var g = G;
        var player = Player;
        var f = g.Factions[player];
        if (p is not null)
        {
            foreach (var (b, build) in buttons)
            {
                if (build.Kind == GalaxyBuildKind.Ship)
                {
                    ShipState(b, p, build.Cls);
                    continue;
                }
                var r = Galaxies.CanBuild(g, player, p.Id, build);
                var detail = $"{N(Galaxies.BuildPrice(g, p, build))} cr, {N(Js.Round(Galaxies.BuildTime(g, p, build)))} s";
                ShowState(b, r, detail, r.Ok ? detail : r.Reason);
            }
            var which = tab == Tab.Ships ? p.Units : p.Buildings;
            if (which.Count > 0) Progress(buildBar, 1 - which[0].Remaining / which[0].Total, which[0].Remaining);
            var own = Galaxies.FleetsAt(g, p.Id, player).FirstOrDefault();
            if (condition is not null && own is not null)
            {
                var c = own.Condition;
                var home = p.Owner == player;
                condition.Text = (c >= 1 ? "intacte" : $"état {HudKit.Percent(c)}{(home ? ", en réparation" : "")}") + Supply(own);
                HudKit.Paint(condition, c < 0.5 ? Palette.Danger : Palette.Muted);
            }
            if (occupation is not null && p.Occupation is { } occ)
            {
                occupation.Text = $"Prise par {NameOf(occ.Side)} dans {N(Math.Ceiling(occ.Remaining))} s";
            }
        }
        foreach (var (tech, b) in techButtons)
        {
            var level = ResearchQueue.PlannedLevel(f, tech);
            var queued = level - f.Research[tech];
            var def = Techs.Defs[tech];
            b.Line(0).Text = $"{def.Name} {f.Research[tech]}/{def.MaxLevel}{(queued > 0 ? $" (+{queued})" : "")}";
            var r = Galaxies.CanResearchTech(g, player, tech);
            var detail = level < def.MaxLevel ? $"{N(def.Costs[level])} cr, {N(def.Times[level])} s" : "Terminé";
            ShowState(b, r, detail, $"{def.Effect}. {(r.Ok ? detail : r.Reason)}");
        }
        if (f.Researching is { } job) Progress(researchBar, 1 - job.Remaining / job.Total, job.Remaining);
    }

    /// <summary>
    /// A ship button: enabled while one of the selected planets can build it, with how many can;
    /// green while it is in a production loop, with how many planets keep it. A loop waiting for
    /// credits keeps its button lit, so that it shows.
    /// </summary>
    void ShipState(LabelButton b, Planet p, ShipClass cls)
    {
        var g = G;
        var build = GalaxyBuild.Ship(cls);
        var targets = OwnTargets();
        var many = targets.Count > 1;
        var ready = targets.Count(id => Galaxies.CanBuild(g, Player, id, build).Ok);
        var loops = targets.Count(id => g.Planets[id].Repeat.Contains(cls));
        // The planet shown speaks for the others when none of them can.
        var r = ready > 0 ? Result.OK : Galaxies.CanBuild(g, Player, p.Id, build);
        var price = N(Galaxies.BuildPrice(g, p, build));
        var detail = many ? $"{price} cr, {ready}/{targets.Count} planètes" : $"{price} cr, {N(Js.Round(Galaxies.BuildTime(g, p, build)))} s";
        var line = r.Ok ? detail : HudKit.ShortReason(r.Reason);
        if (loops > 0) line += loops == targets.Count ? ", en boucle" : $", boucle {loops}/{targets.Count}";
        var hint = many ? "Clic : un sur chaque planète sélectionnée. Clic droit : production en boucle sur toutes." : "Clic droit : production en boucle.";
        b.Disabled = !r.Ok && loops == 0;
        b.Line(1).Text = line;
        b.TooltipText = $"{(r.Ok ? detail : r.Reason)}. {hint}";
        b.SetColor(1, loops > 0 ? Palette.Ok : Palette.Accent);
        b.Paint();
        HudKit.Outline(b, loops > 0 ? Palette.Ok : null, false);
    }

    /// <summary>A grid button's state: enabled with its price, or grey with the short reason why not.</summary>
    static void ShowState(LabelButton b, Result r, string detail, string tooltip)
    {
        b.Disabled = !r.Ok;
        b.Line(1).Text = r.Ok ? detail : HudKit.ShortReason(r.Reason);
        b.TooltipText = tooltip;
        b.Paint();
    }

    /// <summary>Summary of the planet under the pointer, so that it can be read without selecting it.</summary>
    public void Tooltip(int? planetId, Vector2 at)
    {
        if (planetId is not int id || modal is not null)
        {
            tip.Visible = false;
            return;
        }
        var g = G;
        var p = g.Planets[id];
        var lines = new List<string> { p.Name, OwnerName(p) };
        if (p.Owner == Faction.Errant) lines.Add(ColossusAt(g, p) ? $"Site de raid : le Colosse vaut {N(Raids.Bounty)} crédits" : "Site de raid : le Colosse est tombé");
        if (p.Special is PlanetSpecial sp) lines.Add(GalaxyMap.Specials[sp].Name);
        foreach (var (title, _, _) in DepthNotes(p)) lines.Add(title);
        if (!Galaxies.Known(g, Player, p)) lines.Add("Hors de portée de tes capteurs");
        else
        {
            if (p.Station > 0) lines.Add($"Station niveau {p.Station}");
            if (p.Platforms > 0) lines.Add($"{p.Platforms} plateforme{(p.Platforms > 1 ? "s" : "")}");
            if (p.IonCannon > 0) lines.Add("Canon ionique");
            lines.Add($"Revenu +{N(Math.Round(Galaxies.PlanetIncome(g, p), 1))}/s");
            foreach (var f in Galaxies.FleetsAt(g, p.Id).Where(f => Galaxies.FleetVisible(g, Player, f)))
                lines.Add($"{NameOf(f.Side)} : {DescribeShips(f.Ships, f.Heroes)}{Condition(f)}");
            if (Galaxies.BattleAt(g, p.Id)) lines.Add("Combat en cours");
            else if (p.Occupation is { } occ) lines.Add($"Prise par {NameOf(occ.Side)} dans {N(Math.Ceiling(occ.Remaining))} s");
        }
        while (tipBox.GetChildCount() < lines.Count) tipBox.AddChild(Text("", size: 14, bold: tipBox.GetChildCount() == 0));
        for (var i = 0; i < tipBox.GetChildCount(); i++)
        {
            var l = tipBox.GetChild<Label>(i);
            l.Visible = i < lines.Count;
            if (!l.Visible) continue;
            l.Text = lines[i];
            // At most 380 pixels wide with its margins, as the web tooltip: longer lines wrap.
            var wide = UiTheme.Mono.GetStringSize(lines[i], HorizontalAlignment.Left, -1, 14).X > 360;
            l.AutowrapMode = wide ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off;
            l.CustomMinimumSize = new Vector2(wide ? 360 : 0, 0);
        }
        tip.Visible = true;
        tip.ResetSize();
        var size = tip.GetCombinedMinimumSize();
        var screen = root.Size;
        tip.Position = new Vector2(Math.Min(at.X + 16, screen.X - size.X - 8), Math.Min(at.Y + 16, screen.Y - size.Y - 8));
    }

    void ConfirmMenu()
    {
        var (p, box) = Dialog();
        var net = scene.Opts.Net;
        box.AddChild(Heading(net is null ? "Retour au menu ?" : "Quitter la partie ?", null, 22));
        box.AddChild(HudKit.Para(net is null ? "La campagne est sauvegardée : « Continuer la campagne » la reprend où tu l'as laissée."
            : !net.Replica ? "La partie s'arrête pour tous les joueurs. Elle est sauvegardée : « Multijoueur », héberger, puis « Reprendre la campagne », "
                + "la rouvre, chacun reprenant sa place."
            : "L'IA prend ta place jusqu'à la fin."));
        box.AddChild(Actions(Btn("Annuler", CloseModal), Btn(net is null ? "Menu" : "Quitter", scene.ToMenu, primary: true)));
        OpenModal(p);
    }

    public void OnEvent(GalaxyEvent ev)
    {
        var g = G;
        var player = Player;
        OnDepthEvent(ev);
        switch (ev)
        {
            case LanceFired lf:
                LanceFiredShown(lf);
                break;
            case Captured c:
                var planet = g.Planets[c.PlanetId];
                if (c.Side == player)
                {
                    Notify($"{planet.Name} est à toi", planet.Id);
                    Sfx.Play(Sound.Built);
                }
                else if (c.From == player)
                {
                    Notify($"{planet.Name} perdue", planet.Id);
                    Sfx.Play(Sound.Alert);
                }
                else if (c.From == Faction.Errant) Notify($"{planet.Name} : les Errants tombent devant {NameOf(c.Side)}", planet.Id);
                break;
            case PlanetBuilt b when b.Side == player:
                Notify($"{g.Planets[b.PlanetId].Name} : {b.What} terminé", b.PlanetId);
                Sfx.Play(Sound.Built);
                break;
            case TechResearched t when t.Side == player:
                Notify($"Recherche terminée : {Techs.Defs[t.Tech].Name} niveau {g.Factions[player].Research[t.Tech]}");
                Sfx.Play(Sound.Built);
                break;
            case BountyPaid bp when bp.Side == player:
                var taught = bp.Tech is TechId tech ? $", {Techs.Defs[tech].Name} niveau {g.Factions[player].Research[tech]}" : "";
                var of = bp.Lane is { } l ? $"de l'interception entre {g.Planets[l.A].Name} et {g.Planets[l.B].Name}" : $"de la bataille de {g.Planets[bp.PlanetId].Name}";
                Notify($"Primes {of} : +{N(Js.Round(bp.Credits))} crédits{taught}", bp.PlanetId);
                break;
            case BattleEnded e when e.Attacker == player || e.Defender == player:
                var winner = e.Result == BattleResult.Attacker ? e.Attacker : e.Defender;
                var what = e.Result == BattleResult.Retreat
                    ? (e.Attacker == player ? "retraite" : "l'ennemi se replie")
                    : winner == player ? "victoire" : "défaite";
                Notify($"{Galaxies.BattleName(g, e.PlanetId, e.Lane)} : {what}", e.PlanetId);
                Sfx.Play(winner == player && e.Result != BattleResult.Retreat ? Sound.Built : Sound.Alert);
                break;
        }
    }

    public bool DialogOpen => modal is not null;

    /// <summary>Presses the open dialog's first button; false without a dialog.</summary>
    public bool Accept()
    {
        if (modalFirst is null || !IsInstanceValid(modalFirst)) return false;
        modalFirst.EmitSignal(BaseButton.SignalName.Pressed);
        return true;
    }

    /// <summary>A dialog panel and the column its content goes in.</summary>
    static (PanelContainer Panel, VBoxContainer Box) Dialog()
    {
        var p = HudKit.Panel(24, 24);
        var box = HudKit.Column(16);
        p.AddChild(box);
        return (p, box);
    }

    static HBoxContainer Actions(params Button[] buttons)
    {
        var row = HudKit.Row(8);
        foreach (var b in buttons) row.AddChild(b);
        return row;
    }

    void OpenModal(PanelContainer content)
    {
        // From one dialog to the next, the shade stays: only the panel changes.
        var shaded = modal is not null;
        modal?.QueueFree();
        var m = HudKit.Ignore(new Control());
        m.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var shade = new ColorRect { Color = Palette.Bg with { A = 0.7f }, MouseFilter = Control.MouseFilterEnum.Stop };
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        m.AddChild(shade);
        var center = HudKit.Ignore(new CenterContainer());
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        center.AddChild(content);
        m.AddChild(center);
        root.AddChild(m);
        if (!shaded) HudKit.FadeIn(shade);
        HudKit.FadeIn(center);
        modal = m;
        modalFirst = FirstButton(content);
        tip.Visible = false;
    }

    static Button? FirstButton(Node n)
    {
        foreach (var c in n.GetChildren())
        {
            if (c is Button b) return b;
            if (FirstButton(c) is { } inner) return inner;
        }
        return null;
    }

    void CloseModal()
    {
        modal?.QueueFree();
        modal = null;
        modalFirst = null;
    }

    /// <summary>Closes the dialog open, if any: a dev run answering it.</summary>
    public void CloseDialog() => CloseModal();

    /// <summary>Over the network, a battle others command in the battle view: watch it, or stay on the map.</summary>
    public void ShowFight(string title, string text, Action watch)
    {
        Sfx.Play(Sound.Alert);
        var (p, box) = Dialog();
        box.AddChild(Heading(title, null, 22));
        box.AddChild(HudKit.Para(text));
        box.AddChild(Actions(Btn("Regarder", () =>
        {
            CloseModal();
            watch();
        }, primary: true), Btn("Rester sur la carte", CloseModal)));
        OpenModal(p);
    }

    /// <summary>The galaxy is on hold until the player picks how to fight.</summary>
    public void PromptBattle(GalaxyBattle b)
    {
        Sfx.Play(Sound.Alert);
        var g = G;
        var player = Player;
        var enemy = b.Attacker == player ? b.Defender : b.Attacker;
        // Over the network, a replica's battle has no world: what each side brings came with the snapshot.
        var (mine, foe) = (Galaxies.ForceOf(b, player), Galaxies.ForceOf(b, enemy));
        var def = foe.Defense;
        var theirs = new List<string> { DescribeShips(foe.Ships, foe.Heroes) };
        if (def.IonCannon > 0) theirs.Insert(0, "canon ionique");
        if (def.Station > 0) theirs.Insert(0, $"station niveau {def.Station}");
        if (def.Platforms > 0) theirs.Insert(0, $"{def.Platforms} plateforme{(def.Platforms > 1 ? "s" : "")}");
        var (p, box) = Dialog();
        box.AddChild(Heading(Galaxies.BattleName(g, b.PlanetId, b.Lane), enemy, 22));
        var them = enemy == Faction.Errant ? "les Errants" : NameOf(enemy);
        box.AddChild(HudKit.Para(b.Lane is not null ? $"Ta flotte croise une flotte {NameOf(enemy)} en chemin : impossible de passer sans combattre."
            : $"{(b.Attacker == player ? "Tu attaques" : "Tu es attaqué par")} {them}."));
        if (enemy == Faction.Errant) box.AddChild(HudKit.Para(RaidBounty(foe.Ships.Of(ShipClass.Colossus) > 0), Palette.Muted));
        // Over the network, the other side may be a player too: the battle starts once both have chosen.
        if (enemy != player && g.Humans.TryGetValue(enemy, out var rival))
        {
            box.AddChild(HudKit.Para($"{rival} joue en face : la bataille commence quand vous avez choisi tous les deux. Si l'un de vous la commande, "
                + "l'IA prend le camp de celui qui la laisse se résoudre seule.", Palette.Muted));
        }
        var dl = HudKit.Ignore(new GridContainer { Columns = 2 });
        dl.AddThemeConstantOverride("h_separation", 16);
        dl.AddThemeConstantOverride("v_separation", 4);
        void Item(string term, string value)
        {
            dl.AddChild(Text(term, Palette.Accent));
            dl.AddChild(HudKit.Para(value, max: 340));
        }
        Item("Tes forces", DescribeShips(mine.Ships, mine.Heroes));
        Item("En face", string.Join(", ", theirs));
        Item("Coque et boucliers", $"{N(mine.Strength)} contre {N(foe.Strength)}");
        box.AddChild(dl);
        box.AddChild(Actions(
            Btn("Commander la bataille", () =>
            {
                CloseModal();
                scene.Fight(b.Id);
            }, primary: true),
            Btn("Résolution automatique", () =>
            {
                CloseModal();
                scene.AutoResolve(b.Id);
            })));
        OpenModal(p);
    }

    public void ShowEnd(bool won)
    {
        var (p, box) = Dialog();
        box.AddChild(Heading(won ? "Victoire" : "Défaite", Player, 22));
        box.AddChild(HudKit.Para(won ? "Plus aucune faction rivale n'a de planète." : "Tu n'as plus aucune planète."));
        if (scene.Opts.Net is { Replica: false }) box.AddChild(HudKit.Para("Retourner au menu termine la partie pour tous les joueurs.", Palette.Muted));
        box.AddChild(Actions(Btn("Menu", scene.ToMenu, primary: true)));
        OpenModal(p);
    }

    void ShowHelp()
    {
        var (p, box) = Dialog();
        box.AddChild(Heading("Carte galactique", null, 22));
        // The keys and the rules scroll on a small window, so that the dialog and its button stay on screen.
        var content = HudKit.Column(16);
        var pad = HudKit.Ignore(new MarginContainer());
        // Room for the scroll bar, which would otherwise cover the end of the lines.
        pad.AddThemeConstantOverride("margin_right", 14);
        pad.AddChild(content);
        var scroll = HudKit.Scroll(pad);
        box.AddChild(scroll);
        content.AddChild(Controls.List(Controls.Galaxy));
        content.AddChild(HudKit.Para($"Une flotte seule prend une planète sans défense en {N(GalaxyRules.CaptureTime)} s ({N(GalaxyRules.NeutralCaptureTime)} s si elle est neutre). "
            + "Quand deux camps se rencontrent, la bataille se commande dans la vue tactique ou se résout seule. "
            + "Les flottes traversent les planètes neutres ou alliées et s'arrêtent aux planètes ennemies. Tu perds quand tu n'as plus de planète. "
            + "Chaque planète a deux files : bâtiments et vaisseaux. Certaines planètes ont un bonus. "
            + $"À plus d'une voie de tes planètes, une flotte est hors ravitaillement : elle s'use de {HudKit.Percent(Galaxies.SupplyWear * 60)} par minute. "
            + "La liste Diplomatie (T) propose et rompt les trêves. Toutes les quelques minutes, un événement survient : épave à récupérer, bande errante, tempête ionique, essor commercial. "
            + "Les Errants, neutres, gardent la planète la plus isolée avec leur Colosse : ils n'en bougent jamais et ne comptent pas pour la victoire. "
            + $"Chaque vaisseau abattu rapporte {HudKit.Percent(Worlds.KillShare)} de sa valeur ; le Colosse, {N(Raids.Bounty)} crédits et un niveau de recherche. "
            + "La campagne se sauvegarde toute seule ; « Continuer la campagne » la reprend depuis le menu.", Palette.Muted));
        box.AddChild(Actions(Btn("Fermer", CloseModal, primary: true)));
        OpenModal(p);
        // Wrapped lines know their height once laid out: the scroll takes it then, up to what the window leaves.
        Callable.From(() =>
        {
            if (IsInstanceValid(scroll)) HudKit.Fit(scroll, root.Size.Y - 200);
        }).CallDeferred();
    }
}
