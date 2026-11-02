// The galaxy campaign: planets, lanes and fleets on the move, the clock, saving, and the way
// to and from the battle view. Port of src/scenes/GalaxyScene.ts; the panels are in
// GalaxyHud.cs, the campaign over the network in GalaxyScreen.Net.cs. The namespace is not
// "Galaxy", which would hide the simulation's Galaxy type.
using Godot;
using SandboxShip.Battle;
using SandboxShip.Core;
using SandboxShip.Render;
using SandboxShip.Sim;
using SandboxShip.Sim.Data;
using SandboxShip.Ui;

namespace SandboxShip.Campaign;

public partial class GalaxyScreen : Node
{
    /// <summary>Room kept free for the HUD: top bar and the planet panel on the right.</summary>
    const float Top = 64;
    const float PanelW = 340;
    static readonly Color Neutral = Palette.Hex(0x6b7588);
    static readonly Color LaneColor = Palette.Hex(0x2a3446);
    static readonly Color BattleColor = Palette.Hex(0xe8705f);
    const double AutosaveMs = 20000;
    /// <summary>Time the galaxy may take per frame, in milliseconds.</summary>
    const double StepBudgetMs = 12;
    /// <summary>Planet tints, so that the placeholder planets do not all look alike.</summary>
    static readonly uint[] Tints = [0xffffff, 0xffd2a8, 0xc8f0c8, 0xffc0c0, 0xd8ccff, 0xfff0b0];

    /// <summary>
    /// The campaign's saves (SaveSlots): the automatic one and those the player names; "Continuer
    /// la campagne" takes the most recent, "Charger une partie" lists them all.
    /// </summary>
    public static string SaveDir => ProjectSettings.GlobalizePath("user://saves");
    /// <summary>The single save of older versions, moved into SaveDir once (MigrateSaves).</summary>
    static string LegacySavePath => ProjectSettings.GlobalizePath("user://campaign.json");
    /// <summary>Where the host saves a campaign played over the network, apart: what "Reprendre la campagne" hosts again.</summary>
    public static string NetSavePath => Dev.SavePath ?? ProjectSettings.GlobalizePath("user://campaign-net.json");

    public Galaxy Galaxy { get; private set; } = null!;
    public MenuChoice Opts { get; private set; } = null!;
    /// <summary>The local player's side: the save's, or the menu's first seat.</summary>
    public Side Player => Galaxy.Player;
    public GalaxyHud Hud { get; private set; } = null!;
    /// <summary>Where the player's orders go (Sim/Commands.cs): applied at once here, as single player always did.</summary>
    public ICommandSink<GalaxyCommand> Sink { get; private set; } = null!;
    /// <summary>Planet shown in the panel.</summary>
    public int? Selected;
    /// <summary>
    /// Planets selected together, the one shown among them: the player's, or those where a fleet
    /// of theirs waits. Empty when the planet shown is someone else's.
    /// </summary>
    public readonly List<int> Selection = [];
    public double Speed { get; private set; } = 1;
    public bool Paused { get; private set; }

    /// <summary>Pixels the pointer must travel, button held, for a click to become a selection rectangle.</summary>
    const float DragMin = 6;
    /// <summary>From where the left button went down on the map to the pointer, while it is held.</summary>
    Rect2? box;
    int? hover;
    double acc;
    bool ended;
    /// <summary>Milliseconds since the last autosave.</summary>
    double saveTimer;
    bool pointerInside = true;

    readonly List<(Sprite2D Sprite, float Scale)> stars = [];
    double starScroll;
    DrawCanvas under = null!, over = null!;
    readonly List<Node2D> planets = [];
    /// <summary>Owner emblem next to each planet.</summary>
    readonly List<Sprite2D> emblems = [];
    /// <summary>Screen scale and offset of the map, and the screen size they were fitted to.</summary>
    double s = 1, ox, oy;
    Vector2 laidOut;

    public static GalaxyScreen Create(MenuChoice opts) => new() { Opts = opts };

    public override void _Ready()
    {
        var opts = Opts;
        if (opts.Net is { } net)
        {
            // Over the network, the galaxy is the session's: the host's own, or a replica.
            Galaxy = net.Galaxy;
            Sink = net.Sink;
            net.Screen = this;
            NetReady();
        }
        else Begin(opts);
        var home = Galaxy.Planets.FindIndex(p => p.Owner == Galaxy.Player);
        Click(home >= 0 ? home : null, false);
        Build();
    }

    /// <summary>A galaxy of its own: new, loaded from a save (the most recent one to continue), or played ahead (--skip).</summary>
    void Begin(MenuChoice opts)
    {
        var file = opts.Load ?? (opts.Resume ? SaveSlots.Latest(SaveDir)?.Path : null);
        var saved = file is not null ? GalaxySave.Load(file) : null;
        // Another campaign is about to take the automatic save: the one there keeps a file of its own.
        if (SaveFile is not null && file != SaveSlots.PathOf(SaveDir, SaveSlots.Auto)) SaveSlots.KeepAuto(SaveDir);
        var slots = opts.Slots;
        var layout = opts.Map == MapKind.Classic && slots.Count == 2
            ? GalaxyGen.Classic
            : GalaxyGen.GenerateLayout(opts.Seed, slots.Count, opts.Size);
        Galaxy = saved ?? Galaxies.CreateGalaxy(slots, opts.Seed, layout);
        // The player may sit at any seat of the lobby, not only the first.
        if (saved is null && opts.Me is Side me) Galaxy.Player = me;
        Sink = new LocalSink<GalaxyCommand>(c => Commands.Apply(Galaxy, Player, c));
        if (Dev.Skip > 0 && saved is null) PlayAhead(Dev.Skip);
    }

    /// <summary>The map's layers: the stars behind, the planets, what is drawn under and over them; then the HUD.</summary>
    void Build()
    {
        var back = new CanvasLayer { Layer = -1 };
        AddChild(back);
        foreach (var (key, _) in Assets.BackdropTiles)
        {
            var tex = Assets.Frame(key);
            // Small pixel-art layers are doubled so that their stars do not look like dust.
            var scale = tex.GetWidth() < 512 ? 2f : 1f;
            var sprite = new Sprite2D
            {
                Texture = tex, Centered = false, RegionEnabled = true, TextureRepeat = CanvasItem.TextureRepeatEnum.Enabled,
                Scale = new Vector2(scale, scale),
            };
            back.AddChild(sprite);
            stars.Add((sprite, scale));
        }

        var map = new CanvasLayer { Layer = 0 };
        AddChild(map);
        under = new DrawCanvas { OnDraw = DrawUnder };
        map.AddChild(under);
        var key2 = Assets.Planet;
        var frames = Assets.Frames(key2);
        foreach (var p in Galaxy.Planets)
        {
            var tint = Palette.Hex(Tints[p.Id % Tints.Length]);
            Node2D sprite;
            if (frames > 1)
            {
                var a = new AnimatedSprite2D { SpriteFrames = Assets.Anim(key2, true, 8), Modulate = tint };
                a.Play();
                // Each planet starts its rotation at a different frame.
                a.Frame = p.Id * 11 % frames;
                sprite = a;
            }
            else sprite = new Sprite2D { Texture = Assets.Frame(key2), Modulate = tint };
            map.AddChild(sprite);
            planets.Add(sprite);
            var emblem = new Sprite2D { Visible = false, Scale = new Vector2(0.6f, 0.6f) };
            map.AddChild(emblem);
            emblems.Add(emblem);
        }
        over = new DrawCanvas { OnDraw = DrawOver };
        map.AddChild(over);

        Hud = new GalaxyHud(this);
        AddChild(Hud);
    }

    /// <summary>Checking the screen (--skip): the galaxy plays ahead with the player on the AI, its battles resolved on their own.</summary>
    void PlayAhead(double seconds)
    {
        var own = Galaxy.Factions[Galaxy.Player];
        own.AI = new GalaxyAIState { Difficulty = Opts.Difficulty };
        for (var t = 0.0; t < seconds && Galaxy.Winner is null; t += Galaxies.Step)
        {
            foreach (var b in Galaxy.Battles) Galaxies.ChooseBattle(Galaxy, b.Id, BattleStatus.Auto);
            Galaxies.StepGalaxy(Galaxy);
        }
        own.AI = null;
        Galaxy.Events.Clear();
    }

    /// <summary>One of its battles is shown: the galaxy is hidden and does not play, as a sleeping Phaser scene.</summary>
    public void Sleep()
    {
        box = null;
        panFrom = null;
        ProcessMode = ProcessModeEnum.Disabled;
        foreach (var layer in GetChildren().OfType<CanvasLayer>()) layer.Visible = false;
    }

    /// <summary>Back from the battle view.</summary>
    public void Wake()
    {
        ProcessMode = ProcessModeEnum.Inherit;
        foreach (var layer in GetChildren().OfType<CanvasLayer>()) layer.Visible = true;
        Hud.Tooltip(null, Vector2.Zero);
    }

    /// <summary>Fits the galaxy into the screen space the HUD leaves free; the view zooms and pans from there (GalaxyScreen.View.cs).</summary>
    void Layout(Vector2 size)
    {
        var first = laidOut == Vector2.Zero;
        laidOut = size;
        Fit(first);
    }

    double Sx(double x) => ox + x * s;

    double Sy(double y) => oy + y * s;

    /// <summary>Screen radius: richer planets look bigger.</summary>
    double Radius(Planet p) => Math.Max(10, (14 + p.Income * 3) * s);

    int? Pick(Vector2 at)
    {
        foreach (var p in Galaxy.Planets)
        {
            if (Worlds.Hypot(Sx(p.X) - at.X, Sy(p.Y) - at.Y) <= Radius(p) + 10) return p.Id;
        }
        return null;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMMouseEnter) pointerInside = true;
        else if (what == NotificationWMMouseExit)
        {
            pointerInside = false;
            Hud?.Tooltip(null, Vector2.Zero);
        }
        else if (what == NotificationApplicationFocusOut) panFrom = null;
    }

    /// <summary>Clicks and moves that no panel took: they are on the map.</summary>
    public override void _UnhandledInput(InputEvent ev)
    {
        switch (ev)
        {
            case InputEventMouseMotion m:
                hover = Pick(m.Position);
                Hud.Tooltip(hover, m.Position);
                break;
            case InputEventMouseButton { Pressed: true } mb when mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown or MouseButton.Middle:
                ViewButton(mb);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { Pressed: true } mb when mb.ButtonIndex is MouseButton.Left or MouseButton.Right:
                // As a click on the web canvas, it takes the focus off a fleet quantity being typed.
                GetViewport().GuiReleaseFocus();
                if (mb.ButtonIndex == MouseButton.Right)
                {
                    // Picking a rally point or a lance's target: a right click gives up, as in battle.
                    if (CancelLance()) Hud.Toast("Tir annulé");
                    else if (Rallying) Rallying = false;
                    else if (Pick(mb.Position) is int dest)
                    {
                        if (mb.CtrlPressed) RallyTo(dest);
                        else Order(dest);
                    }
                }
                // A click, or a selection rectangle if the pointer moves: told apart on release.
                else box = new Rect2(mb.Position, Vector2.Zero);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventKey { Pressed: true, Echo: false } k:
                OnKey(k);
                break;
        }
    }

    /// <summary>Moves and the release while the left button is held on the map: seen before the HUD, so that a drag can cross it.</summary>
    public override void _Input(InputEvent ev)
    {
        ViewInput(ev);
        if (box is not Rect2 b) return;
        switch (ev)
        {
            case InputEventMouseMotion m:
                box = new Rect2(b.Position, m.Position - b.Position);
                break;
            case InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left } mb:
                box = null;
                var rect = new Rect2(b.Position, mb.Position - b.Position);
                if (Dragged(rect)) BoxSelect(rect.Abs(), mb.ShiftPressed);
                else if (!MapClick(b.Position, mb.ShiftPressed)) Click(Pick(b.Position), mb.ShiftPressed);
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    static bool Dragged(Rect2 b) => Math.Abs(b.Size.X) >= DragMin || Math.Abs(b.Size.Y) >= DragMin;

    /// <summary>Whether a planet can join a selection of several: the player's, or one where a fleet of theirs waits.</summary>
    bool Selectable(int id) => Galaxy.Planets[id].Owner == Player || Galaxies.FleetsAt(Galaxy, id, Player).Count > 0;

    /// <summary>Planets the orders go to: the whole selection when it holds several, else the planet shown.</summary>
    public IReadOnlyList<int> Targets => Selection.Count > 1 ? Selection : Selected is int id ? [id] : [];

    /// <summary>A click on the map: one planet shown alone, or with Shift, one more (or one less) in the selection.</summary>
    void Click(int? id, bool shift)
    {
        if (!shift || id is not null) SelectedFleet = null;
        if (!shift)
        {
            Selected = id;
            Selection.Clear();
            if (id is int only && Selectable(only)) Selection.Add(only);
            return;
        }
        if (id is not int p) return;
        if (!Selectable(p))
        {
            Hud.Toast("Seules tes planètes, et celles où attend ta flotte, s'ajoutent à la sélection");
            return;
        }
        if (Selection.Remove(p))
        {
            if (Selected == p) Selected = Selection.Count > 0 ? Selection[^1] : null;
            return;
        }
        Selection.Add(p);
        Selected = p;
    }

    /// <summary>
    /// Selects the player's planets the rectangle touches (with Shift, adds them). The panel
    /// keeps its planet if it is among them, else shows one of the player's.
    /// </summary>
    void BoxSelect(Rect2 rect, bool shift)
    {
        SelectedFleet = null;
        Rallying = false;
        if (!shift) Selection.Clear();
        foreach (var p in Galaxy.Planets)
        {
            var x = Sx(p.X);
            var y = Sy(p.Y);
            var r = Radius(p);
            var touches = x + r >= rect.Position.X && x - r <= rect.End.X && y + r >= rect.Position.Y && y - r <= rect.End.Y;
            if (touches && Selectable(p.Id) && !Selection.Contains(p.Id)) Selection.Add(p.Id);
        }
        if (Selected is int shown && Selection.Contains(shown)) return;
        if (Selection.Count > 0)
        {
            var owned = Selection.FindIndex(id => Galaxy.Planets[id].Owner == Player);
            Selected = Selection[Math.Max(0, owned)];
        }
        else if (!shift) Selected = null;
    }

    void OnKey(InputEventKey ev)
    {
        if (ViewKey(ev))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        if (Bindings.Pressed(ev, Bindings.Pause))
        {
            TogglePause();
            GetViewport().SetInputAsHandled();
            return;
        }
        switch (ev.Keycode)
        {
            case Key.Escape:
                // A lance's target or the rally point being picked first, then the drawer, then the selection.
                if (CancelLance()) Hud.Toast("Tir annulé");
                else if (Rallying) Rallying = false;
                else if (!Hud.CloseDrawer())
                {
                    Selected = null;
                    Selection.Clear();
                    SelectedFleet = null;
                }
                GetViewport().SetInputAsHandled();
                break;
            // The web dialogs focus their first button: Enter presses it.
            case Key.Enter or Key.KpEnter:
                if (Hud.Accept()) GetViewport().SetInputAsHandled();
                break;
        }
    }

    /// <summary>Sends the ships picked in the panel from the selected planet to `dest`; from several planets, every fleet there.</summary>
    void Order(int dest)
    {
        if (SelectedFleet is int moving)
        {
            Redirect(moving, dest);
            return;
        }
        if (Selection.Count > 1)
        {
            SendAll(dest);
            return;
        }
        if (Selected is not int from || from == dest) return;
        var fleet = Galaxies.FleetsAt(Galaxy, from, Player).FirstOrDefault();
        if (fleet is null)
        {
            Hud.Toast("Aucune flotte à toi sur cette planète");
            return;
        }
        var (ships, heroes) = Hud.SendCounts();
        var r = Sink.Send(new GalaxyCommand.SendFleet(fleet.Id, dest, ships, [.. heroes]));
        if (!r.Ok) Hud.Toast(r.Reason);
        // The fleet left: show what stays, with fresh quantities, before the next order.
        else Hud.Refresh();
    }

    /// <summary>Sends every fleet of the player waiting at the selected planets to `dest`, each one whole.</summary>
    void SendAll(int dest)
    {
        var g = Galaxy;
        var fleets = Selection.Where(id => id != dest).SelectMany(id => Galaxies.FleetsAt(g, id, Player)).ToList();
        if (fleets.Count == 0)
        {
            Hud.Toast("Aucune flotte à envoyer depuis ces planètes");
            return;
        }
        var sent = 0;
        var stayed = new List<string>();
        foreach (var f in fleets)
        {
            var at = g.Planets[f.At!.Value].Name;
            var r = Sink.Send(new GalaxyCommand.SendFleet(f.Id, dest));
            if (r.Ok) sent++;
            else stayed.Add($"{at} ({GalaxyHud.Lower(r.Reason)})");
        }
        var to = g.Planets[dest].Name;
        var left = sent switch { 0 => "Aucune flotte n'est partie", 1 => $"1 flotte partie vers {to}", _ => $"{sent} flottes parties vers {to}" };
        Hud.Toast(stayed.Count > 0 ? $"{left} ; restée{(stayed.Count > 1 ? "s" : "")} : {string.Join(", ", stayed)}" : left);
        if (sent > 0) Hud.Refresh();
    }

    public void TogglePause()
    {
        // Over the network, for everyone: the host decides, then tells this screen (NetPaused).
        if (Opts.Net is { } net)
        {
            net.Session.AskPause(!Paused);
            return;
        }
        Paused = !Paused;
        Hud.RefreshModes();
    }

    public void SetSpeed(double speed)
    {
        // Over the network, the host sets it for everyone.
        if (Opts.Net is { } net)
        {
            net.Session.SetGalaxySpeed(speed);
            return;
        }
        Speed = speed;
        Paused = false;
        Hud.RefreshModes();
    }

    /// <summary>
    /// Where this campaign is saved as it goes, if anywhere: alone, the automatic save; over the
    /// network, the host's own (a player keeps none). A capture run (--shot) leaves the player's
    /// saves as they were, unless --save names a file of its own.
    /// </summary>
    string? SaveFile => Opts.Net is { Replica: true } ? null
        : Opts.Net is not null ? Dev.Shot is null || Dev.SavePath is not null ? NetSavePath : null
        : Dev.Shot is null ? SaveSlots.PathOf(SaveDir, SaveSlots.Auto) : null;

    /// <summary>Saves the campaign, unless it is over.</summary>
    public void Save()
    {
        if (SaveFile is string file && Galaxy.Winner is null && !(Opts.Net is null ? SaveSlots.SaveAuto(Galaxy, SaveDir) : GalaxySave.Save(Galaxy, file)))
            Hud.Toast("Sauvegarde impossible");
        saveTimer = 0;
    }

    /// <summary>What a save of this campaign is called unless the player names it: race, planets, now.</summary>
    public string DefaultSaveName() => SaveSlots.DefaultName(Galaxy.Factions[Player].Race, Galaxy.Planets.Count(p => p.Owner == Player), DateTimeOffset.Now);

    /// <summary>
    /// Saves the campaign under the player's name, beside the automatic save. Not over the network,
    /// once it is over, nor in a capture run: false then, as when it cannot be written.
    /// </summary>
    public bool SaveAs(string name)
    {
        if (Opts.Net is not null || Dev.Shot is not null || Galaxy.Winner is not null) return false;
        return SaveSlots.SaveAs(Galaxy, SaveDir, name.Trim().Length > 0 ? name : DefaultSaveName()) is not null;
    }

    /// <summary>At the start: the single save of older versions moves into SaveDir, the old file kept aside. Never in a capture run.</summary>
    public static void MigrateSaves()
    {
        if (Dev.Shot is null && !Dev.Service) SaveSlots.Migrate(LegacySavePath, SaveDir);
    }

    /// <summary>Back to the menu; over the network, out of the game (a player's side goes to the AI, a host closes it for all).</summary>
    public void ToMenu()
    {
        Save();
        Opts.Net?.Session.Leave();
        Main.Instance.ShowMenu();
    }

    /// <summary>
    /// Plays a battle in the tactical view; the galaxy sleeps until it ends. Over the network, the
    /// choice goes to the host, which opens the battle view once every human side in it has chosen.
    /// </summary>
    public void Fight(int battleId)
    {
        var b = Galaxy.Battles.Find(x => x.Id == battleId);
        if (b is null) return;
        Sink.Send(new GalaxyCommand.ChooseBattle(battleId, BattleStatus.Tactical));
        Answered(battleId);
        if (Opts.Net is not null) return;
        var player = Player;
        var opts = new BattleOptions
        {
            Player = player,
            Mode = GameMode.Campaign,
            Campaign = new CampaignBattle(b.World, battleId, Galaxies.BattleName(Galaxy, b.PlanetId, b.Lane), b.Attacker, b.Defender),
        };
        Save();
        Main.Instance.StartBattle(opts);
    }

    public void AutoResolve(int battleId)
    {
        Sink.Send(new GalaxyCommand.ChooseBattle(battleId, BattleStatus.Auto));
        Answered(battleId);
    }

    /// <summary>The player left a battle they were commanding: it plays on by itself.</summary>
    public void DelegateBattle(int battleId)
    {
        Sink.Send(new GalaxyCommand.DelegateBattle(battleId));
        Save();
    }

    /// <summary>The player, attacking, fell back from a battle they were commanding.</summary>
    public void Retreat(int battleId)
    {
        Sink.Send(new GalaxyCommand.Retreat(battleId));
        Save();
    }

    /// <summary>Called when a tactical battle ends in the battle view; over the network, the host concluded it already.</summary>
    public void FinishBattle(int battleId, BattleResult result)
    {
        if (Opts.Net is null) Galaxies.ConcludeBattle(Galaxy, battleId, result);
        Save();
    }

    public override void _Process(double delta)
    {
        var size = GetViewport().GetVisibleRect().Size;
        if (size != laidOut) Layout(size);
        TickView(delta);
        var g = Galaxy;
        // Over the network, the session steps the host's galaxy, and a player's replica follows snapshots.
        if (Opts.Net is not null) NetFrame(delta);
        else if (!Paused)
        {
            acc += Math.Min(delta, 0.25) * Speed;
            // A big battle resolving on its own can make a step slow: past the budget, the rest
            // carries over to the next frames rather than freezing this one.
            var deadline = Godot.Time.GetTicksUsec() + (ulong)(StepBudgetMs * 1000);
            while (acc >= Galaxies.Step && Godot.Time.GetTicksUsec() < deadline)
            {
                Galaxies.StepGalaxy(g);
                acc -= Galaxies.Step;
            }
            acc = Math.Min(acc, 1);
        }
        foreach (var ev in g.Events) Hud.OnEvent(ev);
        g.Events.Clear();
        // A planet lost, or left by the fleet that waited there, drops out of the selection; a
        // planet shown alone stays shown, as it always did.
        if (Selection.RemoveAll(id => !Selectable(id)) > 0 && Selection.Count > 0 && Selected is int shown && !Selection.Contains(shown))
        {
            Selected = Selection[0];
        }
        FollowFleet();
        // A battle commanded from a view that is no longer running has nobody to finish it (alone:
        // over the network, it runs in the session).
        if (Opts.Net is null) foreach (var b in g.Battles) if (b.Status == BattleStatus.Tactical) Galaxies.Reopen(b);
        // A battle waiting for the player is asked about whenever no other dialog is open: a
        // prompt pushed aside (by the help, or by a second battle at once) comes back, rather
        // than leaving the galaxy waiting forever. Over the network, until the player has chosen.
        var pending = g.Battles.Find(b => b.Status == BattleStatus.Pending && Galaxies.HumansIn(g, b).Contains(Player) && !b.Choices.ContainsKey(Player)
            && !answered.Contains(b.Id));
        if (pending is not null && g.Winner is null && !Hud.DialogOpen) Prompt(pending);
        if (g.Winner is not null && !ended)
        {
            ended = true;
            Hud.ShowEnd(Galaxies.Won(g, Player));
            if (SaveFile is string file) GalaxySave.Clear(file);
        }
        saveTimer += delta * 1000;
        if (saveTimer >= AutosaveMs) Save();
        // The pointer left the map for a panel, or the window: no planet summary.
        if (!pointerInside || GetViewport().GuiGetHoveredControl() is not null) Hud.Tooltip(null, Vector2.Zero);
        starScroll += delta * 1000;
        for (var i = 0; i < stars.Count; i++)
        {
            var (sprite, scale) = stars[i];
            sprite.RegionRect = new Rect2(new Vector2((float)(starScroll * 0.002 * (i + 1)), 0), size / scale);
        }
        SyncSprites();
        under.QueueRedraw();
        over.QueueRedraw();
        Hud.Tick(delta * 1000);
    }

    /// <summary>
    /// Fog of war on the planet sprites: ownership is known everywhere, the rest only where the
    /// player has eyes, and at the Errants' site, known to all.
    /// </summary>
    void SyncSprites()
    {
        var seen = Galaxies.VisiblePlanets(Galaxy, Player);
        foreach (var p in Galaxy.Planets)
        {
            var sprite = planets[p.Id];
            sprite.Modulate = sprite.Modulate with { A = Galaxies.Known(Galaxy, Player, p, seen) ? 1 : 0.45f };
            var badge = emblems[p.Id];
            badge.Visible = p.Owner is not null;
            if (p.Owner is Side owner) badge.Texture = GalaxyHud.EmblemTexture(Galaxy, owner);
        }
    }

    void DrawUnder(DrawCanvas u)
    {
        var g = Galaxy;
        foreach (var (a, b) in g.Lanes)
        {
            var pa = g.Planets[a];
            var pb = g.Planets[b];
            Draw.Line(u, Sx(pa.X), Sy(pa.Y), Sx(pb.X), Sy(pb.Y), LaneColor, 2);
        }
        DrawMarksUnder(u);
        DrawRoutes(u);
        DrawLances(u);
        // Route previews from the selected planets to the hovered one, where the player has a fleet.
        if (hover is not int to) return;
        foreach (var from in Targets)
        {
            if (from == to || Galaxies.FleetsAt(g, from, Player).Count == 0) continue;
            var prev = g.Planets[from];
            foreach (var id in Galaxies.FindPath(g, from, to) ?? [])
            {
                var next = g.Planets[id];
                Draw.Line(u, Sx(prev.X), Sy(prev.Y), Sx(next.X), Sy(next.Y), Palette.Accent with { A = 0.8f }, 3);
                prev = next;
            }
        }
    }

    void DrawOver(DrawCanvas o)
    {
        var g = Galaxy;
        var t = Godot.Time.GetTicksMsec() / 1000.0;
        var player = Player;
        var font = UiTheme.Mono;
        var nameHeight = font.GetHeight(14);

        var seen = Galaxies.VisiblePlanets(g, player);
        foreach (var p in g.Planets)
        {
            var x = Sx(p.X);
            var y = Sy(p.Y);
            var r = Radius(p);
            Draw.Text(o, font, p.Name, x, y + r + 10 + nameHeight, 14, Palette.Text, 1);
        }
        foreach (var p in g.Planets)
        {
            var x = Sx(p.X);
            var y = Sy(p.Y);
            var r = Radius(p);
            var visible = Galaxies.Known(g, player, p, seen);
            var ring = p.Owner is Side owner ? Palette.Of(g, owner) : Neutral;
            Draw.Ring(o, x, y, r + 3, ring with { A = visible ? 1 : 0.5f }, 3, 1);
            // Every selected planet has its ring; the one shown in the panel, a thicker one.
            if (p.Id == Selected || Selection.Contains(p.Id)) Draw.Ring(o, x, y, r + 9, Palette.Accent, p.Id == Selected && Selection.Count > 1 ? 3 : 2, 1);
            else if (p.Id == hover) Draw.Ring(o, x, y, r + 9, Palette.Accent with { A = 0.6f }, 1, 1);
            if (!visible) continue;
            if (p.Occupation is { } occ)
            {
                var total = p.Owner is not null ? GalaxyRules.CaptureTime : GalaxyRules.NeutralCaptureTime;
                var done = 1 - occ.Remaining / total;
                if (done > 0)
                {
                    o.DrawArc(new Vector2((float)x, (float)y), (float)(r + 7), -Mathf.Pi / 2, (float)(-Math.PI / 2 + done * Math.Tau),
                        Math.Max(4, (int)(done * 48)), Palette.Of(g, occ.Side), 4);
                }
            }
            if (Galaxies.BattleAt(g, p.Id))
            {
                Draw.Ring(o, x, y, r + 14, BattleColor with { A = (float)(0.5 + 0.5 * Math.Sin(t * 6)) }, 3, 1);
            }
            // One pip per station level, above the planet.
            for (var i = 0; i < p.Station; i++) Draw.Rect(o, x - p.Station * 4 + i * 8, y - r - 12, 6, 6, Palette.Text);
        }
        DrawMarksOver(o);

        // Fleets that met on a lane fight where they met.
        foreach (var b in g.Battles)
        {
            if (b.Lane is not { } l) continue;
            var (pa, pb) = (g.Planets[l.A], g.Planets[l.B]);
            Draw.Ring(o, Sx(pa.X + (pb.X - pa.X) * l.T), Sy(pa.Y + (pb.Y - pa.Y) * l.T), 16, BattleColor with { A = (float)(0.5 + 0.5 * Math.Sin(t * 6)) }, 3, 1);
        }
        foreach (var f in g.Fleets.Values)
        {
            // The Errants' guard never leaves their site, known to all.
            if (!Galaxies.FleetVisible(g, player, f, seen) && f.Side != Faction.Errant) continue;
            var count = Sim.Battle.CountShips(f.Ships) + f.Heroes.Count;
            var color = Palette.Of(g, f.Side);
            if (f.At is int at)
            {
                // The owner's fleets and its allies' on its left, the others on its right.
                var p = g.Planets[at];
                var side = p.Owner is Side owner ? Galaxies.Allies(g, f.Side, owner) : f.Side == player;
                FleetMarker(o, Sx(p.X) + (side ? -1 : 1) * (Radius(p) + 18), Sy(p.Y), side ? 0 : Math.PI, color, count);
            }
            else if (f.To is int to)
            {
                var a = g.Planets[f.From];
                var b = g.Planets[to];
                var x = Sx(a.X + (b.X - a.X) * f.Progress);
                var y = Sy(a.Y + (b.Y - a.Y) * f.Progress);
                FleetMarker(o, x, y, Math.Atan2(b.Y - a.Y, b.X - a.X), color, count);
            }
        }
        DrawFleetMarks(o);
        if (box is Rect2 drag && Dragged(drag))
        {
            var rect = drag.Abs();
            o.DrawRect(rect, Palette.Accent with { A = 0.08f });
            o.DrawRect(rect, Palette.Accent with { A = 0.8f }, false, 1);
        }
    }

    /// <summary>Fleet marker: a triangle in its side's color pointing along `angle`, with the ship count above it.</summary>
    static void FleetMarker(DrawCanvas o, double x, double y, double angle, Color c, int count)
    {
        const double r = 8;
        Vector2 Pt(double a) => new((float)(x + Math.Cos(angle + a) * r), (float)(y + Math.Sin(angle + a) * r));
        Vector2[] tri = [Pt(0), Pt(2.5), Pt(-2.5)];
        o.DrawColoredPolygon(tri, c);
        o.DrawPolyline([.. tri, tri[0]], Palette.Bg, 1);
        var font = HudKit.Bold;
        // Centered on y - 16, as the web label's origin.
        Draw.Text(o, font, count.ToString(), x, y - 16 + font.GetHeight(13) / 2, 13, c, 1);
    }
}
