// Battle HUD, in Control nodes above the battle view: top bar, selection, production,
// minimap, control groups, toasts and alerts, the sandbox panel, the piloting banner and the
// tooltip; the selection panel is in Hud.Selection.cs, the dialogs in Hud.Dialogs.cs. It
// reads the simulation and calls its commands; it never changes simulation state on its own.
// Port of src/ui/hud.ts; its look is src/ui/hud.css.
using Godot;
using SandboxShip.Battle;
using SandboxShip.Core;
using SandboxShip.Render;
using SandboxShip.Sim;
using SandboxShip.Sim.Data;
using static SandboxShip.Ui.HudKit;

namespace SandboxShip.Ui;

public partial class Hud : CanvasLayer
{
    enum Tab { Ships, Buildings, Research }

    /// <summary>One element of a production queue: what it is, its place, its progress, and how to cancel it.</summary>
    sealed record QueueEntry(string Label, string Key, double Progress, Action Cancel, double? Left = null);

    const double RefreshMs = 100;
    static readonly double[] Speeds = [0.5, 1, 2, 4];
    static readonly ShipClass[] ShipyardClasses = Ships.All.Where(c => !Ships.Defs[c].FromHangar).ToArray();
    /// <summary>Build buttons are laid out in columns at least this wide (a picture, then room for the name), 6 pixels apart.</summary>
    const int ColumnWidth = 132 + LabelButton.IconRoom, ColumnGap = 6;

    readonly BattleScreen scene;
    readonly bool sandbox;
    /// <summary>The title of a campaign battle (its planet, or its lane): no building, no sandbox, and the attacker may retreat.</summary>
    readonly string? campaign;

    Control root = null!;
    Label credits = null!, level = null!;
    Label? creditsValue, income;
    Button pauseButton = null!;
    readonly Dictionary<double, Button> speedButtons = [];
    Button? sandboxButton;
    HFlowContainer quickbar = null!;
    ScrollContainer selectionScroll = null!;
    VBoxContainer selection = null!;
    readonly Dictionary<Tab, (Button Button, Control Body)> tabs = [];
    readonly List<ScrollContainer> buildScrolls = [];
    readonly List<GridContainer> grids = [];
    HFlowContainer? unitsQueue, buildingsQueue, researchStatus;
    /// <summary>Last rendered signature of each queue.</summary>
    readonly Dictionary<HFlowContainer, string> lists = [];
    readonly Dictionary<ShipClass, LabelButton> shipButtons = [];
    readonly Dictionary<BuildableType, LabelButton> structureButtons = [];
    readonly Dictionary<TechId, LabelButton> techButtons = [];
    readonly Dictionary<HeroId, LabelButton> heroButtons = [];
    Button? upgradeButton;
    /// <summary>The station level the upgrade button pictures, 0 for none.</summary>
    int upgradeLevel;
    Minimap minimap = null!;
    VBoxContainer toasts = null!;
    /// <summary>When each toast fades out and when it goes, in milliseconds.</summary>
    readonly Dictionary<Control, (double Fade, double End)> toastTimes = [];
    PanelContainer? sandboxPanel;
    OptionButton? spawnFaction, spawnWhat;
    Button? spawnButton;
    PanelContainer pilotBanner = null!;
    RichTextLabel pilotText = null!;
    PanelContainer tip = null!;
    VBoxContainer tipLines = null!;
    double timer;
    /// <summary>The selection last shown, by size and first ship: a new one refreshes the panel at once.</summary>
    (int Count, int First) selectionSig;

    public Hud(BattleScreen scene, bool sandbox, string? campaign)
    {
        this.scene = scene;
        this.sandbox = sandbox;
        this.campaign = campaign;
    }

    World World => scene.World;
    Side Player => scene.Opts.Player;
    /// <summary>The player's race: its ships, prices and heroes.</summary>
    Faction Race => Worlds.RaceOf(World, Player);
    Vector2 Screen => scene.ScreenSize;

    public override void _Ready()
    {
        Layer = 1;
        root = Ignore(new Control { Theme = UiTheme.Get() });
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);
        BuildTopbar();
        BuildBottombar();
        if (campaign is null && scene.Opts.Net is null) BuildSandbox(sandbox);
        BuildOverlays();
        BuildModes();
        RefreshModes();
        Refresh();
    }

    void BuildTopbar()
    {
        var f = Factions.Defs[Race];
        var bar = Panel(8, 4);
        bar.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        bar.OffsetLeft = 8;
        bar.OffsetRight = -8;
        bar.OffsetTop = 8;
        root.AddChild(bar);
        var row = Row(16);
        bar.AddChild(row);
        var faction = Row(8);
        faction.MouseFilter = Control.MouseFilterEnum.Stop;
        faction.TooltipText = $"{f.Trait.Name} : {f.Trait.Description}";
        faction.AddChild(Emblem(Player));
        faction.AddChild(Text(f.Name, bold: true));
        row.AddChild(faction);
        var money = Row(9);
        credits = money.Add(Text(""));
        if (campaign is null)
        {
            creditsValue = money.Add(Text("", Palette.Accent, bold: true));
            income = money.Add(Text("", Palette.Muted));
        }
        row.AddChild(money);
        level = row.Add(Text("", Palette.Muted));
        row.AddChild(Ignore(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }));
        var speeds = row.Add(Row(4));
        pauseButton = speeds.Add(Button("Pause", scene.TogglePause, $"Pause ({Bindings.Label(Bindings.Pause)})"));
        // Over the network, everyone plays at ×1.
        if (scene.Opts.Net is null)
        {
            foreach (var s in Speeds) speedButtons[s] = speeds.Add(Button($"×{N(s)}", () => scene.SetSpeed(s), $"Vitesse ×{N(s)}"));
        }
        var group = row.Add(Row(4));
        if (campaign is not null)
        {
            credits.Text = campaign;
            group.AddChild(Button("Aide", () => ShowHelp()));
            group.AddChild(Button("Options", ShowOptions));
            if (scene.Opts.Campaign?.Attacker == Player && scene.Opts.Net is not { Watching: true })
            {
                group.AddChild(Button("Retraite", scene.Retreat, "Les survivants regagnent la planète d'où ils venaient"));
            }
            return;
        }
        credits.Text = "Crédits";
        if (scene.Opts.Net is not null)
        {
            NetButtons(group);
            return;
        }
        sandboxButton = group.Add(Button("Bac à sable", () =>
        {
            sandboxPanel!.Visible = !sandboxPanel.Visible;
            RefreshModes();
        }));
        group.AddChild(Button("Aide", () => ShowHelp()));
        group.AddChild(Button("Options", ShowOptions));
        group.AddChild(Button("Menu", () => Confirm("Quitter la partie ?", "La bataille en cours sera perdue.", "Quitter", scene.ToMenu)));
    }

    /// <summary>The player's star lance in the top bar: building, charging, or ready to fire.</summary>
    string LanceStatus()
    {
        var lance = World.Entities.FirstOrDefault(e => e.Side == Player && e.Type == "starLance");
        if (lance is null) return "";
        if (lance.Build < 1) return $", Lance stellaire en construction {Percent(lance.Build)}";
        var wait = lance.Abilities[0].Cooldown;
        return wait > 0 ? $", Lance stellaire {Percent(1 - wait / Abilities.Defs[AbilityId.StarLance].Cooldown)}" : ", Lance stellaire prête";
    }

    /// <summary>Whose an entity is, to the player: "ennemi", "allié", or nothing for its own.</summary>
    string Relation(Entity e) => e.Side == Player ? "" : Worlds.Foes(World, e.Side, Player) ? "ennemi" : "allié";

    /// <summary>A side's emblem, 24 pixels, crisp: its race's, in its color.</summary>
    TextureRect Emblem(Side side) => Ignore(new TextureRect
    {
        Texture = Logos.Emblem(Worlds.RaceOf(World, side), 2, Palette.Rgb(World, side)),
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        CustomMinimumSize = new Vector2(24, 24),
        TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
    });

    void BuildBottombar()
    {
        var bar = Row(8);
        bar.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        bar.OffsetLeft = 8;
        bar.OffsetRight = -8;
        bar.OffsetBottom = -8;
        bar.OffsetTop = -8;
        bar.GrowVertical = Control.GrowDirection.Begin;
        root.AddChild(bar);

        var left = Column(6);
        left.CustomMinimumSize = new Vector2(260, 0);
        left.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;
        bar.AddChild(left);
        quickbar = Flow(4);
        left.AddChild(quickbar);
        var panel = Panel();
        panel.CustomMinimumSize = new Vector2(0, 120);
        left.AddChild(panel);
        selection = Column(0);
        selectionScroll = Scroll(selection);
        panel.AddChild(selectionScroll);

        if (campaign is null) BuildProduction(bar);
        else bar.AddChild(Ignore(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }));

        var mini = Panel(0, 0);
        mini.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;
        minimap = new Minimap(scene);
        mini.AddChild(minimap);
        bar.AddChild(mini);
    }

    void BuildProduction(HBoxContainer bar)
    {
        var build = Panel();
        build.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        build.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;
        bar.AddChild(build);
        var box = Column(8);
        build.AddChild(box);
        var tabRow = box.Add(Row(4));
        Control AddTab(Tab id, string label)
        {
            var button = tabRow.Add(Button(label, () => ShowTab(id), height: 36));
            var body = box.Add(Column(8));
            tabs[id] = (button, body);
            return body;
        }
        // Buttons scroll inside each tab; tabs and queue stay in view.
        VBoxContainer Scrolled(Control tab)
        {
            var list = Column(6);
            var s = Scroll(list);
            tab.AddChild(s);
            buildScrolls.Add(s);
            return list;
        }
        GridContainer Grid(Control parent)
        {
            var g = Ignore(new GridContainer { Columns = 1 });
            g.AddThemeConstantOverride("h_separation", ColumnGap);
            g.AddThemeConstantOverride("v_separation", ColumnGap);
            parent.AddChild(g);
            grids.Add(g);
            return g;
        }
        HFlowContainer Queue(Control tab)
        {
            var q = Flow(4);
            q.CustomMinimumSize = new Vector2(0, 40);
            tab.AddChild(q);
            return q;
        }

        var ships = AddTab(Tab.Ships, "Vaisseaux");
        var shipList = Scrolled(ships);
        var shipRow = Grid(shipList);
        foreach (var cls in ShipyardClasses)
        {
            var b = BuildButton(Ships.OrderName(cls), Assets.ShipIcon(Race, cls));
            // Shift: five at once, as many as the queue and the credits allow.
            b.Pressed += () => Report(scene.Sink.Send(new Command.EnqueueShip(cls, Input.IsKeyPressed(Key.Shift) ? 5 : 1)));
            // Right click: keep this class in production.
            b.GuiInput += ev =>
            {
                if (ev is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }) return;
                b.AcceptEvent();
                var on = World.Factions[Player].Repeat.Contains(cls);
                scene.Sink.Send(new Command.SetLoop(cls, !on));
                Toast($"{Ships.Defs[cls].Name} : production en boucle {(on ? "arrêtée" : "lancée")}");
                Refresh();
            };
            shipRow.AddChild(b);
            shipButtons[cls] = b;
        }
        var heroRow = Grid(shipList);
        foreach (var h in Heroes.Of(Race))
        {
            var id = h.Id;
            var b = BuildButton($"{h.Name}, {h.Title.ToLowerInvariant()}", Assets.ShipIcon(Race, h.Base));
            var power = Abilities.Defs[h.Ability];
            b.TooltipText = $"{h.Description} {power.Name} : {power.Description}";
            b.Pressed += () => Report(scene.Sink.Send(new Command.EnqueueHero(id)));
            heroRow.AddChild(b);
            heroButtons[id] = b;
        }
        unitsQueue = Queue(ships);

        var buildings = AddTab(Tab.Buildings, "Bâtiments");
        var buildingList = Scrolled(buildings);
        upgradeButton = Button("", () => Report(scene.Sink.Send(new Command.UpgradeStation())));
        upgradeButton.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        upgradeButton.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
        buildingList.AddChild(upgradeButton);
        var structureRow = Grid(buildingList);
        foreach (var type in Buildables.All)
        {
            var b = BuildButton(Buildables.Defs[type].Name, Assets.BuildingIcon(Race, type));
            b.Pressed += () => scene.Controls.SetMode(new PlaceMode(type));
            structureRow.AddChild(b);
            structureButtons[type] = b;
        }
        buildingsQueue = Queue(buildings);

        var research = AddTab(Tab.Research, "Recherche");
        var techRow = Grid(Scrolled(research));
        foreach (var tech in Techs.All)
        {
            var t = tech;
            var b = BuildButton("");
            b.Pressed += () => Report(scene.Sink.Send(new Command.StartResearch(t)));
            techRow.AddChild(b);
            techButtons[tech] = b;
        }
        researchStatus = Queue(research);
        ShowTab(Tab.Ships);
    }

    /// <summary>A production button: what it makes, pictured, then the name, and under it the price or why it cannot be built.</summary>
    static LabelButton BuildButton(string name, Texture2D? icon = null)
    {
        var b = new LabelButton(true, 8, 4)
        {
            MinSize = new Vector2(ColumnWidth, 44),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        b.Add(name, Palette.Text, 14, wrap: true);
        b.Add("", Palette.Accent, 12, wrap: true);
        b.SetIcon(icon);
        return b;
    }

    void ShowTab(Tab id)
    {
        foreach (var (key, t) in tabs)
        {
            Outline(t.Button, key == id ? Palette.Accent : null);
            t.Body.Visible = key == id;
        }
    }

    void BuildSandbox(bool open)
    {
        var p = Panel(12, 12);
        p.AnchorLeft = p.AnchorRight = 1;
        p.OffsetLeft = -268;
        p.OffsetRight = -8;
        p.OffsetTop = 64;
        p.GrowHorizontal = Control.GrowDirection.Begin;
        p.Visible = open;
        sandboxPanel = p;
        root.AddChild(p);
        var box = Column(8);
        p.AddChild(box);
        box.AddChild(Text("Bac à sable", bold: true));

        VBoxContainer Field(string label)
        {
            var c = Column(4);
            c.AddChild(Text(label, Palette.Muted));
            box.AddChild(c);
            return c;
        }
        var faction = new OptionButton { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 40) };
        // The neutral Errants can be spawned too, to set up a raid by hand.
        foreach (var id in Factions.All.Append(Faction.Errant))
        {
            faction.AddItem(Factions.Defs[id].Name);
            faction.SetItemMetadata(faction.ItemCount - 1, (int)id);
            if (scene.Opts.Slots.Count > 1 && id == scene.Opts.Slots[1].Race) faction.Select(faction.ItemCount - 1);
        }
        Field("Faction").AddChild(faction);
        var what = new OptionButton { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 40) };
        void Item(string name, string value)
        {
            what.AddItem(name);
            what.SetItemMetadata(what.ItemCount - 1, value);
            if (value == "frigate") what.Select(what.ItemCount - 1);
        }
        what.AddSeparator("Vaisseaux");
        foreach (var c in Ships.All) Item(Ships.Defs[c].Name, Ids.Of(c));
        what.AddSeparator("Structures");
        foreach (var t in Buildables.All) Item(Buildables.Defs[t].Name, Ids.Of(t));
        Item(Station.Name, "station");
        what.AddSeparator("Héros");
        Item("Amiral", "admiral");
        Item("As", "ace");
        Item("Ingénieur", "engineer");
        Field("Élément").AddChild(what);
        spawnFaction = faction;
        spawnWhat = what;

        spawnButton = box.Add(Button("Placer au clic", () =>
        {
            var on = scene.Controls.Mode is SpawnMode;
            scene.Controls.SetMode(on ? new SelectMode() : PickSpawn());
        }));
        void Resync(long _)
        {
            if (scene.Controls.Mode is SpawnMode) scene.Controls.SetMode(PickSpawn());
        }
        faction.ItemSelected += Resync;
        what.ItemSelected += Resync;

        box.AddChild(Check("Brouillard de guerre", World.Fog, on => scene.Sink.Send(new Command.SetFog(on))));
        box.AddChild(Check("IA ennemie active", !open, on =>
        {
            foreach (var s in scene.Opts.Slots)
            {
                if (s.Side != Player) scene.Sink.Send(new Command.SetAI(s.Side, on ? s.AI ?? scene.Opts.Difficulty : null));
            }
        }));
        box.AddChild(Button("+5 000 crédits", () => scene.Sink.Send(new Command.GrantCredits(5000))));
        box.AddChild(Button("Détruire la sélection", () => scene.Sink.Send(new Command.Destroy([.. scene.Selected]))));
    }

    SpawnMode PickSpawn() =>
        new((Faction)spawnFaction!.GetItemMetadata(spawnFaction.Selected).AsInt32(), spawnWhat!.GetItemMetadata(spawnWhat.Selected).AsString());

    /// <summary>Toasts, the piloting banner and the tooltip: above the panels, centered or by the pointer.</summary>
    void BuildOverlays()
    {
        var toastArea = Column(4);
        toastArea.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        toastArea.OffsetTop = 112;
        root.AddChild(toastArea);
        toasts = toastArea;

        var bannerArea = Column(0);
        bannerArea.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        bannerArea.OffsetTop = 64;
        root.AddChild(bannerArea);
        pilotBanner = Panel(12, 8, Palette.Accent);
        pilotBanner.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        pilotBanner.Visible = false;
        pilotText = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore };
        pilotText.AddThemeFontOverride("bold_font", Bold);
        pilotBanner.AddChild(pilotText);
        bannerArea.AddChild(pilotBanner);

        tip = Ignore(Panel(10, 8));
        tip.Visible = false;
        tipLines = Column(0);
        tip.AddChild(tipLines);
        root.AddChild(tip);
    }

    /// <summary>Shows the outcome of a command when it was refused.</summary>
    void Report(Result r)
    {
        if (!r.Ok) Toast(r.Reason);
    }

    public void Toast(string text) => AddToast(text, null);

    /// <summary>An attack or a loss, with a button to go and see.</summary>
    public void Alert(string text, Action see) => AddToast(text, see);

    /// <summary>Short messages that fade out on their own; an alert stays longer and offers to go and see.</summary>
    void AddToast(string text, Action? see)
    {
        if (root is null) return;
        var t = see is null ? Panel(12, 6) : Panel(12, 6, Palette.Danger);
        t.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        var row = Row(12);
        t.AddChild(row);
        var label = Text(text);
        label.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(label);
        if (see is not null) row.AddChild(Button("Voir", see, "Retour arrière : aller à la dernière alerte", 32));
        toasts.AddChild(t);
        while (toasts.GetChildCount() > 4) RemoveToast(toasts.GetChild<Control>(0));
        var life = see is not null ? 4500 : 2200;
        var now = BattleScreen.Now;
        toastTimes[t] = (now + life, now + life + 400);
    }

    void RemoveToast(Control t)
    {
        toastTimes.Remove(t);
        toasts.RemoveChild(t);
        t.QueueFree();
    }

    void FadeToasts()
    {
        var now = BattleScreen.Now;
        foreach (var (t, (fade, end)) in toastTimes.ToList())
        {
            if (now >= end) RemoveToast(t);
            else if (now >= fade) t.Modulate = new Color(1, 1, 1, (float)Math.Max(0, 1 - (now - fade) / 300));
        }
    }

    /// <summary>Reflects speed, pause, sandbox and mode states in the buttons.</summary>
    public void RefreshModes()
    {
        if (root is null) return;
        var c = scene.Controls;
        var piloted = World.Entities.Get(c?.PilotId);
        pilotBanner.Visible = piloted is not null;
        if (piloted is not null)
        {
            pilotText.Text = $"[b]Pilotage : {Names.EntityName(piloted)}[/b] " +
                $"[color=#949eb0]{Bindings.Movement()} ou flèches : piloter. Clic gauche maintenu : tirer sur la cible marquée. {Bindings.Label(Bindings.Pilot)} ou Échap : rendre la main.[/color]";
        }
        Outline(pauseButton, scene.Paused ? Palette.Accent : null);
        pauseButton.Text = scene.PausedBy is { } who ? $"Pause · {who}" : "Pause";
        foreach (var (s, b) in speedButtons) Outline(b, !scene.Paused && scene.Speed == s ? Palette.Accent : null);
        if (sandboxButton is not null) Outline(sandboxButton, sandboxPanel?.Visible == true ? Palette.Accent : null);
        var mode = c?.Mode;
        if (spawnButton is not null) Outline(spawnButton, mode is SpawnMode ? Palette.Accent : null);
        foreach (var (type, b) in structureButtons)
        {
            b.Marked = mode is PlaceMode m && m.Type == type;
            Outline(b, b.Marked ? Palette.Accent : null, false);
            b.Paint();
        }
    }

    public void Tick(double delta)
    {
        if (root is null) return;
        PlaceTip();
        FadeToasts();
        Layout();
        TickModes(delta);
        timer -= delta;
        // A new selection shows at once, not at the next refresh.
        var sel = scene.Selected;
        var sig = (sel.Count, sel.Count > 0 ? sel.First() : 0);
        if (sig != selectionSig)
        {
            selectionSig = sig;
            timer = 0;
        }
        if (timer > 0) return;
        timer = RefreshMs;
        Refresh();
    }

    /// <summary>What CSS does by itself: scrolled areas as tall as their content up to a share of the screen, build buttons in as many columns as fit.</summary>
    void Layout()
    {
        var h = Screen.Y;
        Fit(selectionScroll, h * 0.42);
        foreach (var s in buildScrolls) Fit(s, Math.Max(104, h * 0.4 - 112));
        foreach (var g in grids)
        {
            var width = g.GetParentControl().Size.X;
            var cols = Math.Max(1, (int)((width + ColumnGap) / (ColumnWidth + ColumnGap)));
            if (g.Columns != cols) g.Columns = cols;
        }
        var banner = Math.Min(720, Screen.X - 32) - 24;
        if (pilotText.CustomMinimumSize.X != banner) pilotText.CustomMinimumSize = new Vector2(banner, 0);
        if (modalScroll is not null) Fit(modalScroll, h - 32 - 50 - (modalActions is null ? 0 : modalActions.GetCombinedMinimumSize().Y + 16));
    }

    /// <summary>The tooltip follows the pointer every frame; its text changes with the refresh.</summary>
    void PlaceTip()
    {
        if (!tip.Visible) return;
        var p = GetViewport().GetMousePosition();
        var size = tip.GetCombinedMinimumSize();
        if (tip.Size != size) tip.Size = size;
        tip.Position = new Vector2(Math.Min(p.X + 16, Screen.X - size.X - 8), Math.Max(8, p.Y - size.Y - 12));
    }

    /// <summary>What is under the pointer on the battlefield: name, hull, shields, what it is doing.</summary>
    void RefreshTip()
    {
        var c = scene.Controls;
        var e = c.PointerOver && c.Box is null && c.PilotId is null ? c.Hovered() : null;
        if (e is null || !scene.CanSee(e))
        {
            // Over the terrain, what it does (Hud.Terrain.cs).
            var patch = c.PointerOver && c.Box is null && c.PilotId is null && World.Terrain.Count > 0 ? Terrains.At(World, c.Hover.X, c.Hover.Y) : null;
            if (patch is not null) Sync(tipLines, TerrainTip(patch));
            tip.Visible = patch is not null;
            return;
        }
        const int size = 14;
        var lines = new List<Part>
        {
            SpansPart(size, (Names.EntityName(e), Palette.Text, true), (Factions.Defs[e.Race].Name + (Relation(e) is { Length: > 0 } r ? $", {r}" : ""), Palette.Muted, false)),
            TextPart($"Coque {N(Math.Ceiling(e.Hull))} / {N(Js.Round(e.MaxHull))}" + (e.MaxShield > 0 ? $", bouclier {N(Math.Ceiling(e.Shield))} / {N(Js.Round(e.MaxShield))}" : ""), size: size, wrap: false),
        };
        if (e.Build < 1) lines.Add(TextPart($"En construction : {Percent(e.Build)}", size: size, wrap: false));
        foreach (var a in e.Abilities)
        {
            var state = a.Active > 0 ? "active" : a.Cooldown > 0 ? $"recharge {N(Math.Ceiling(a.Cooldown))} s" : "prête";
            lines.Add(TextPart($"{Abilities.Defs[a.Id].Name} : {state}", size: size, wrap: false));
        }
        if (e.Side == Player && e.Kind == EntityKind.Ship)
        {
            var doing = e.Order is { } o ? OrderName(o.Type) : e.TargetId is not null ? "Au combat" : "Inactif";
            lines.Add(TextPart(doing, Palette.Muted, size, wrap: false));
        }
        Sync(tipLines, lines);
        tip.Visible = true;
    }

    static string OrderName(OrderType t) => t switch
    {
        OrderType.Move => "En route",
        OrderType.AttackMove => "Attaque-mouvement",
        _ => "Attaque",
    };

    /// <summary>Control groups, idle ships and the last alert, one click away.</summary>
    void RefreshQuickbar()
    {
        var c = scene.Controls;
        var parts = new List<Part>();
        foreach (var (n, count) in c.GroupSizes())
        {
            parts.Add(QuickButton($"group:{n}", n.ToString(), count.ToString(), $"Groupe {n} (touche {n}, deux fois : centrer)", false, () => c.RecallGroup(n)));
        }
        var idle = c.IdleShips().Count;
        parts.Add(QuickButton("idle", "Inactifs", idle.ToString(), $"Vaisseaux inactifs ({Bindings.Label(Bindings.Idle)} : le suivant, Maj + {Bindings.Label(Bindings.Idle)} : tous)", idle == 0, () => c.SelectIdle(Input.IsKeyPressed(Key.Shift))));
        parts.Add(QuickButton("alert", "Alerte", null, $"Aller à la dernière alerte ({Bindings.Label(Bindings.LastAlert)})", scene.Alerts.Count == 0, scene.JumpToAlert));
        Sync(quickbar, parts);
    }

    static Part QuickButton(string key, string label, string? count, string tip, bool disabled, Action act) => new(key, () =>
    {
        var b = new LabelButton(false, 10, 0) { MinSize = new Vector2(0, 32), TooltipText = tip };
        b.Add(label, Palette.Text);
        if (count is not null) b.Add("", Palette.Muted, 12);
        b.Pressed += act;
        return b;
    }, n =>
    {
        var b = (LabelButton)n;
        if (count is not null) b.Line(1).Text = count;
        b.Disabled = disabled;
        b.Paint();
    });

    void Refresh()
    {
        if (!World.Factions.TryGetValue(Player, out var f)) return;
        // The setting may change here or in the options dialog.
        if (f.AutoAbilities != Settings.Current.AutoAbilities) scene.Sink.Send(new Command.SetAutoAbilities(Settings.Current.AutoAbilities));
        var station = Worlds.StationOf(World, Player);
        RefreshTip();
        RefreshQuickbar();
        if (campaign is not null)
        {
            level.Text = station is not null ? $"Station niveau {station.Level}" : "";
            RefreshSelection();
            minimap.QueueRedraw();
            return;
        }
        var gain = station is not null ? Station.Levels[station.Level - 1].Income : 0;
        foreach (var e in World.Entities) if (e.Side == Player && e.Build >= 1) gain += e.Income;
        creditsValue!.Text = N(Math.Floor(f.Credits));
        income!.Text = $"+{N(gain)}/s";
        level.Text = (station is not null ? $"Station niveau {station.Level}" : "Station détruite") + LanceStatus();

        RefreshProduction(f, station);
        RefreshSelection();
        minimap.QueueRedraw();
    }

    /// <summary>What each production button shows: a refresh that would change nothing leaves it alone, ten times a second.</summary>
    readonly Dictionary<LabelButton, (bool Ok, string Reason, string Detail, string? Before, string? After)> shown = [];
    /// <summary>
    /// The lines of the buttons, by what they describe, made once rather than at every refresh.
    /// Kind: 0 a ship's price (Extra: in a loop), 1 a structure's, 2 a tech level's (Extra: the
    /// level), 3 a tech's name (Extra: level and queued), 4 a hero's price, 5 a tech's effect.
    /// </summary>
    readonly Dictionary<(int Kind, int Id, int Extra), string> details = [];

    /// <summary>A button's state and tooltip, `before` and `after` the detail or the reason.</summary>
    void SetButton(LabelButton b, Result r, string detail, string? before = null, string? after = null)
    {
        var state = (r.Ok, r.Reason, detail, before, after);
        if (shown.TryGetValue(b, out var was) && was == state) return;
        shown[b] = state;
        b.Disabled = !r.Ok;
        // Short of credits, the price still shows, in red: how far off it is matters.
        b.Poor = !r.Ok && r.Reason == "Crédits insuffisants";
        b.Line(1).Text = r.Ok || b.Poor ? detail : ShortReason(r.Reason);
        b.TooltipText = string.Concat(before, r.Ok ? detail : r.Reason, after);
        b.Paint();
    }

    readonly Dictionary<ShipClass, string> shipTips = [];

    /// <summary>What a ship button's tooltip says after its price: its weapons, then how to order it.</summary>
    string ShipTip(ShipClass cls)
    {
        if (!shipTips.TryGetValue(cls, out var s)) shipTips[cls] = s = $". Armes : {Names.Armament(cls)}. Maj + clic : 5 d'un coup. Clic droit : production en boucle.";
        return s;
    }

    string Detail(int kind, int id, int extra, double cost, double time, string suffix = "")
    {
        if (!details.TryGetValue((kind, id, extra), out var s)) details[(kind, id, extra)] = s = $"{N(cost)} cr, {N(time)} s{suffix}";
        return s;
    }

    /// <summary>Why a structure is out of reach, by the station level it needs.</summary>
    static readonly string[] Locked = [.. Enumerable.Range(0, 6).Select(l => $"Station niveau {l} requise")];

    /// <summary>Shown only once the station is up to it: locked choices stay out of the way.</summary>
    static void Unlocked(Control b, int level, int needed)
    {
        if (b.Visible != level >= needed) b.Visible = level >= needed;
    }

    void RefreshProduction(FactionState f, Entity? station)
    {
        var w = World;
        // Without a station (an invasion force), what a level 1 station would allow.
        var level = station?.Level ?? 1;
        foreach (var (cls, b) in shipButtons)
        {
            Unlocked(b, level, Ships.Defs[cls].MinStationLevel);
            var loop = f.Repeat.Contains(cls);
            b.SetColor(1, loop ? Palette.Ok : Palette.Accent);
            SetButton(b, Structures.CanBuildShip(w, Player, cls), Detail(0, (int)cls, loop ? 1 : 0, Ships.Cost(cls, Race), Ships.Defs[cls].BuildTime, loop ? ", en boucle" : ""),
                after: ShipTip(cls));
            Outline(b, loop ? Palette.Ok : null, false);
        }
        foreach (var (type, b) in structureButtons)
        {
            var def = Buildables.Defs[type];
            Unlocked(b, level, def.MinStationLevel);
            // Without a station (an invasion force), what a level 1 station would allow.
            var r = (station?.Level ?? 1) < def.MinStationLevel ? Result.Fail(Locked[def.MinStationLevel])
                : f.Credits < def.Cost ? Result.Fail("Crédits insuffisants")
                : Result.OK;
            SetButton(b, r, Detail(1, (int)type, 0, def.Cost, def.BuildTime));
        }
        var next = station is not null && station.Level < Station.Levels.Count ? Station.Levels[station.Level] : null;
        var up = Structures.CanUpgrade(w, Player);
        // The station it would become, pictured; changed only when its level does.
        var toLevel = next is not null ? station!.Level + 1 : 0;
        if (toLevel != upgradeLevel)
        {
            upgradeLevel = toLevel;
            upgradeButton!.Icon = toLevel > 0 ? Assets.StationIcon(Race, toLevel) : null;
        }
        upgradeButton!.Disabled = !up.Ok;
        upgradeButton.Text = next is not null
            ? $"Améliorer la station au niveau {station!.Level + 1} ({N(next.UpgradeCost)} cr){(up.Ok ? "" : $" : {up.Reason}")}"
            : "Station au niveau maximum";
        foreach (var (tech, b) in techButtons)
        {
            // Levels already queued count: the button offers the one after them.
            var def = Techs.Defs[tech];
            var planned = ResearchQueue.PlannedLevel(f, tech);
            var queued = planned - f.Research[tech];
            if (!details.TryGetValue((3, (int)tech, f.Research[tech] * 100 + queued), out var name))
                details[(3, (int)tech, f.Research[tech] * 100 + queued)] = name = $"{def.Name} {f.Research[tech]}/{def.MaxLevel}{(queued > 0 ? $" (+{queued})" : "")}";
            b.Line(0).Text = name;
            var detail = planned < def.MaxLevel ? Detail(2, (int)tech, planned, def.Costs[planned], def.Times[planned]) : "Terminé";
            if (!details.TryGetValue((5, (int)tech, 0), out var effect)) details[(5, (int)tech, 0)] = effect = def.Effect + ". ";
            SetButton(b, Structures.CanResearch(w, Player, tech), detail, before: effect);
        }
        foreach (var (id, b) in heroButtons)
        {
            Unlocked(b, level, Heroes.Defs[id].MinStationLevel);
            SetButton(b, Structures.CanBuildHero(w, Player, id), Detail(4, (int)id.Faction * 16 + (int)id.Role, 0, Heroes.Defs[id].Cost, Heroes.Defs[id].BuildTime));
        }

        RenderList(unitsQueue!, f.Queue.Select((q, i) => new QueueEntry(
            q.Kind == QueueKind.Ship ? Ships.OrderName(q.Cls) : q.Kind == QueueKind.Hero ? Heroes.Defs[q.Hero].Name : "",
            $"index:{i}", i == 0 ? 1 - q.Remaining / q.Total : 0,
            () => scene.Sink.Send(new Command.CancelQueued(i, QueueSide.Units)), i == 0 ? q.Remaining : null)).ToList(),
            "File vide : choisis un vaisseau ci-dessus.");
        string WorkName(QueueItem q)
        {
            if (q.Kind != QueueKind.Tier) return "Amélioration de la station";
            var target = w.Entities.Get(q.TargetId);
            return target is not null ? $"{Names.EntityName(target)} niveau {target.Level + 1}" : "Amélioration";
        }
        var sites = w.Entities.Where(e => e.Side == Player && e.Kind == EntityKind.Structure && e.Build < 1).ToList();
        RenderList(buildingsQueue!, [
            .. f.BuildQueue.Select((q, i) => new QueueEntry(WorkName(q), $"index:{i}", i == 0 ? 1 - q.Remaining / q.Total : 0,
                () => scene.Sink.Send(new Command.CancelQueued(i, QueueSide.Buildings)), i == 0 ? q.Remaining : null)),
            .. sites.Select(e => new QueueEntry(Names.EntityName(e), $"site:{e.Id}", e.Build, () => scene.Sink.Send(new Command.CancelConstruction(e.Id)))),
        ], "Rien en construction.");
        // Each queued level is named by the level it reaches.
        var reached = f.Research.Clone();
        RenderList(researchStatus!, ResearchQueue.Jobs(f).Select((job, i) => new QueueEntry(
            $"{Techs.Defs[job.Tech].Name} {++reached[job.Tech]}", $"research:{i}", i == 0 ? 1 - job.Remaining / job.Total : 0,
            () => scene.Sink.Send(new Command.CancelResearch(i)), i == 0 ? job.Remaining : null)).ToList(),
            "Aucune recherche en file. Chaque niveau demande une station du même niveau (niveau 4 pour les derniers).");
    }

    /// <summary>
    /// Re-renders a queue only when its items change, so that its buttons stay clickable.
    /// Queue buttons cancel on a right click only: a stray left click does not throw away an order.
    /// </summary>
    void RenderList(HFlowContainer target, List<QueueEntry> items, string empty)
    {
        var sig = string.Join("|", items.Select(i => i.Key + i.Label));
        if (lists.GetValueOrDefault(target) != sig)
        {
            lists[target] = sig;
            foreach (var c in target.GetChildren())
            {
                target.RemoveChild(c);
                c.QueueFree();
            }
            if (items.Count == 0)
            {
                var l = Text(empty, Palette.Muted, wrap: true);
                l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                l.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
                target.AddChild(l);
            }
            else
            {
                var l = Text("File (clic droit : annuler)", Palette.Muted);
                l.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
                target.AddChild(l);
                foreach (var item in items) target.AddChild(QueueButton(item));
            }
        }
        // The element under way shows how far it is, and how many seconds are left.
        var k = 0;
        foreach (var c in target.GetChildren())
        {
            if (c is not Button b) continue;
            var item = items[k++];
            Fill(b.GetChild<ColorRect>(0), item.Progress);
            var text = item.Left is double left ? $"{item.Label} ({N(Math.Ceiling(left))} s)" : item.Label;
            if (b.Text != text) b.Text = text;
        }
    }

    static Button QueueButton(QueueEntry item)
    {
        var b = Button(item.Label, null, $"Annuler {item.Label}", 32);
        b.ButtonMask = MouseButtonMask.Right;
        b.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        b.Pressed += item.Cancel;
        b.AddChild(ProgressBar());
        return b;
    }
}
