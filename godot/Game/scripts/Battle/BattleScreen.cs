// The battle view: runs the simulation at a fixed step and shows it. Port of
// src/scenes/BattleScene.ts; drawing is in BattleScreen.Draw.cs.
using Godot;
using SandboxShip.Core;
using SandboxShip.Render;
using SandboxShip.Sim;
using SandboxShip.Sim.Data;
using SandboxShip.Ui;

namespace SandboxShip.Battle;

public partial class BattleScreen : Node
{
    const int MaxStepsPerFrame = 20;
    /// <summary>Room around the map that the camera can show.</summary>
    public const double Margin = 600;

    /// <summary>Detail levels by zoom: below Simple, flames, turrets and sparks go; below Icons, small craft become chevrons.</summary>
    public const double LodSimple = 0.5, LodIcons = 0.35;

    static readonly HashSet<Sound> QuietAfar = [Sound.Bullet, Sound.Laser, Sound.Hit, Sound.Shield];

    public World World { get; private set; } = null!;
    public BattleOptions Opts { get; private set; } = null!;
    public Hud Hud { get; private set; } = null!;
    public Selection Controls { get; private set; } = null!;
    /// <summary>Where the player's orders go (Sim/Commands.cs): applied at once here, as single player always did.</summary>
    public ICommandSink<Command> Sink { get; private set; } = null!;
    public readonly HashSet<int> Selected = [];
    public double Speed { get; private set; } = 1;
    public bool Paused { get; private set; }
    /// <summary>Attacks and losses, newest last; Backspace goes back through them.</summary>
    public readonly List<Alert> Alerts = [];

    double acc;
    int alertCursor = -1;
    /// <summary>Set once a campaign battle is decided.</summary>
    BattleResult? result;
    /// <summary>The player's station fell while others fight on.</summary>
    bool defeated;
    bool stalemateWarned;

    /// <summary>Camera: the world point at the center of the screen, and the zoom.</summary>
    public Vector2 Center;
    public float Zoom = 1;
    Camera2D camera = null!;
    Backdrop backdrop = null!;
    double shakeUntil, shakeIntensity;
    readonly Random random = new();

    public static BattleScreen Create(BattleOptions opts) => new() { Opts = opts };

    /// <summary>Milliseconds since start, as Phaser's scene clock.</summary>
    public static double Now => Godot.Time.GetTicksMsec();

    public override void _Ready()
    {
        // A drawn map keeps its seed: "same map" restarts it.
        if (Opts.Seed is null && Opts.Campaign is null) Opts = Opts with { Seed = random.Next(1, 1_000_000_000) };
        var o = Opts;
        var sandbox = o.Mode == GameMode.Sandbox;
        if (o.Demo)
        {
            World = Showcase.Create(o.Seed ?? 1);
            Opts = o = o with { Player = World.Factions.Keys.First() };
        }
        else World = o.Campaign?.World ?? (Dev.Start == "stress" ? Stress.Create(o.Seed ?? 7) : Scenario.CreateSkirmish(new SkirmishOptions
        {
            // In the sandbox, nobody plays by itself: the others only do what the player spawns.
            Slots = [.. o.Slots.Select(s => sandbox ? s with { AI = null } : s)],
            Credits = sandbox ? 50000 : Scenario.StartCredits, Seed = o.Seed ?? 1,
            Map = o.Map, Size = o.Size, Fog = o.Fog && !sandbox, Raid = o.Raid,
            Mode = o.Rules, ZoneTarget = o.ZoneTarget, AssaultMinutes = o.AssaultMinutes,
        }));
        // The sandbox's tools are open in a game played alone, outside the campaign.
        World.SandboxTools = o.Campaign is null && !o.Demo;
        Sink = new LocalSink<Command>(c => Commands.Apply(World, Opts.Player, c));
        // A campaign battle's AI plays the side facing the player.
        if (o.Campaign is { } cb) campaignFoe = [cb.Attacker == o.Player ? cb.Defender : cb.Attacker];
        // The player's ships use their abilities by themselves if the player wants so.
        var own = o.Demo ? null : World.Factions.GetValueOrDefault(o.Player);
        if (own is not null) Sink.Send(new Command.SetAutoAbilities(Settings.Current.AutoAbilities));
        if (Dev.Skip > 0 && own is not null && o.Campaign is null)
        {
            // Checking a screen: both sides play ahead on the AI.
            own.AI = Worlds.NewAI(o.Difficulty);
            Steps.Run(World, Dev.Skip);
        }
        if (Dev.Zoom is float dz) Zoom = dz;
        backdrop = new Backdrop();
        AddChild(backdrop);
        BuildWorldView();
        camera = new Camera2D();
        worldRoot.AddChild(camera);
        camera.MakeCurrent();
        var home = (IPoint?)Worlds.StationOf(World, o.Player) ?? FleetCenter(o.Player);
        CenterOn(home.X, home.Y);
        Controls = new Selection(this);
        AddChild(Controls);
        Hud = new Hud(this, sandbox, o.Campaign?.Title);
        AddChild(Hud);
        if (o.Demo)
        {
            Hud.Visible = false;
            Hud.ProcessMode = ProcessModeEnum.Disabled;
            Controls.ProcessMode = ProcessModeEnum.Disabled;
            CenterOn(World.Width / 2, World.Height / 2);
            Watch(0);
            return;
        }
        if (Dev.Start == "gallery") Gallery();
        if (Dev.Start == "raid" && World.Entities.FirstOrDefault(e => e.Type == "colossus") is { } boss)
        {
            World.Fog = false;
            CenterOn(boss.X, boss.Y);
        }
        if (Dev.Start == "stress")
        {
            CenterOn(World.Width / 2, World.Height / 2);
            StressProbe.Attach(this, GetViewport(), fogMask);
        }
        if (Dev.At is Vector2 at) CenterOn(at.X, at.Y);
    }

    public override void _ExitTree() => StressProbe.Detach();

    /// <summary>Checking the looks: one ship of each class per faction, the neutral Errants last, platforms at each level, a lab, a star lance charging; paused.</summary>
    void Gallery()
    {
        var y = 600.0;
        foreach (var f in Factions.All.Append(Faction.Errant))
        {
            if (!World.Factions.ContainsKey(f)) Worlds.AddFaction(World, f, 0);
            var x = 400.0;
            foreach (var c in Ships.All)
            {
                var r = Ships.Defs[c].Radius;
                x += r + 40;
                Worlds.SpawnShip(World, f, c, x, y, -Math.PI / 2);
                x += r + 40;
            }
            y += 380;
        }
        for (var level = 1; level <= 3; level++)
        {
            var p = Worlds.SpawnStructure(World, Faction.Klaed, BuildableType.Platform, 500 + level * 160, y);
            for (var t = 1; t < level; t++) Structures.ApplyTier(World, p);
        }
        Worlds.SpawnStructure(World, Faction.Klaed, BuildableType.Lab, 1250, y);
        Worlds.SpawnStructure(World, Faction.Klaed, BuildableType.StarLance, 1500, y).Abilities[0].Cooldown *= 0.4;
        World.Fog = false;
        Paused = true;
        CenterOn(1700, 1150);
    }

    Point FleetCenter(Side side)
    {
        var ships = World.Entities.Where(e => e.Side == side).ToList();
        if (ships.Count == 0) return new Point(World.Width / 2, World.Height / 2);
        return new Point(ships.Average(e => e.X), ships.Average(e => e.Y));
    }

    public override void _Process(double delta)
    {
        var probe = StressProbe.Current;
        probe?.Frame();
        var whole = StressProbe.Start();
        var t = StressProbe.Start();
        if (!Paused)
        {
            // Measuring: one step a frame whatever the frame rate, so that every run shows the same battle.
            acc += probe is not null ? Steps.Step : Math.Min(delta, 0.1) * Speed;
            var n = 0;
            while (acc >= Steps.Step && n < MaxStepsPerFrame)
            {
                Steps.StepWorld(World);
                if (Opts.Campaign is not null) Sim.Battle.UpdateBattleAI(World, campaignFoe, Steps.Step);
                if (Opts.Demo && World.Time >= nextSteer) Steer();
                acc -= Steps.Step;
                n++;
            }
            // Too slow to keep up: drop the backlog rather than spiral.
            if (n == MaxStepsPerFrame) acc = 0;
        }
        StressProbe.Stop("sim", t);
        CheckCampaignEnd();
        if (Opts.Demo) Watch(delta);
        else Controls.Tick(delta * 1000);
        probe?.Hold();
        ClampCamera();
        t = StressProbe.Start();
        ConsumeEvents();
        StressProbe.Stop("events", t);
        t = StressProbe.Start();
        SyncViews(delta);
        StressProbe.Stop("views", t);
        t = StressProbe.Start();
        SyncShots();
        StressProbe.Stop("shots", t);
        RedrawLayers();
        backdrop.Sync(Center - ScreenSize / (2 * 1f));
        t = StressProbe.Start();
        if (!Opts.Demo) Hud.Tick(delta * 1000);
        StressProbe.Stop("hud", t);
        StressProbe.Stop("battle process", whole);
    }

    // The menu's backdrop.

    /// <summary>Screen width on the left that the menu covers: the fight is framed in the rest.</summary>
    public float DemoInset;
    /// <summary>Set when the showcase fight is over, a few seconds after one side is left (or after too long).</summary>
    public bool DemoOver { get; private set; }
    double nextSteer, lastStanding = double.MaxValue;
    /// <summary>In a campaign battle, the side the AI plays.</summary>
    IReadOnlyList<Side> campaignFoe = [];

    /// <summary>Every two seconds: the idle ships back into the fight, and is it over?</summary>
    void Steer()
    {
        nextSteer = World.Time + 2;
        Showcase.Steer(World);
        if (Showcase.Standing(World) > 1) lastStanding = World.Time;
        else if (lastStanding == double.MaxValue) lastStanding = World.Time;
        DemoOver = World.Time - lastStanding > 5 || World.Time > 200;
    }

    /// <summary>The camera drifts to where the ships are and zooms to take them in, beside the menu.</summary>
    void Watch(double delta)
    {
        double sx = 0, sy = 0;
        var n = 0;
        foreach (var e in World.Entities)
        {
            if (e.Kind != EntityKind.Ship) continue;
            sx += e.X;
            sy += e.Y;
            n++;
        }
        if (n == 0) return;
        var mid = new Vector2((float)(sx / n), (float)(sy / n));
        // The spread of the ships near the middle: stragglers far off do not widen the view.
        float x0 = mid.X, x1 = mid.X, y0 = mid.Y, y1 = mid.Y;
        foreach (var e in World.Entities)
        {
            if (e.Kind != EntityKind.Ship || Math.Abs(e.X - mid.X) > 1400 || Math.Abs(e.Y - mid.Y) > 1000) continue;
            x0 = Math.Min(x0, (float)e.X);
            x1 = Math.Max(x1, (float)e.X);
            y0 = Math.Min(y0, (float)e.Y);
            y1 = Math.Max(y1, (float)e.Y);
        }
        var free = new Vector2(Math.Max(320, ScreenSize.X - DemoInset), ScreenSize.Y);
        var want = Math.Clamp(Math.Min(free.X / (x1 - x0 + 500), free.Y / (y1 - y0 + 400)), 0.6f, 1.1f);
        var mid2 = new Vector2((x0 + x1) / 2, (y0 + y1) / 2);
        // Slow, as a camera that follows the action rather than jumps to it; at once the first time.
        var k = delta <= 0 ? 1f : 1 - (float)Math.Exp(-delta * 0.5);
        Zoom += (want - Zoom) * k;
        var target = mid2 - new Vector2(DemoInset / 2 / Zoom, 0);
        Center += (target - Center) * (delta <= 0 ? 1f : 1 - (float)Math.Exp(-delta * 0.8));
    }

    // Camera.

    public Vector2 ScreenSize => GetViewport().GetVisibleRect().Size;

    public void CenterOn(double x, double y) => Center = new Vector2((float)x, (float)y);

    /// <summary>Keeps the view over the map and its margin, as Phaser's camera bounds do.</summary>
    void ClampCamera()
    {
        var size = ScreenSize / Zoom;
        float Clamp(float c, double from, double length, float view)
        {
            var lo = (float)from + view / 2;
            var hi = Math.Max(lo, (float)(from + length) - view / 2);
            return Math.Clamp(c, lo, hi);
        }
        Center = new Vector2(
            Clamp(Center.X, -Margin, World.Width + 2 * Margin, size.X),
            Clamp(Center.Y, -Margin, World.Height + 2 * Margin, size.Y));
        camera.Position = Center;
        camera.Zoom = new Vector2(Zoom, Zoom);
        camera.Offset = Now < shakeUntil
            ? new Vector2((float)((random.NextDouble() - 0.5) * 2 * shakeIntensity * ScreenSize.X), (float)((random.NextDouble() - 0.5) * 2 * shakeIntensity * ScreenSize.Y)) / Zoom
            : Vector2.Zero;
    }

    void Shake(double ms, double intensity)
    {
        shakeUntil = Now + ms;
        shakeIntensity = intensity;
    }

    /// <summary>The world point under a screen point.</summary>
    public Vector2 WorldPoint(Vector2 screen) => Center + (screen - ScreenSize / 2) / Zoom;

    /// <summary>The world rectangle on screen this frame.</summary>
    public Rect2 ViewRect()
    {
        var size = ScreenSize / Zoom;
        return new Rect2(Center - size / 2, size);
    }

    public bool OnScreen(double x, double y) => ViewRect().HasPoint(new Vector2((float)x, (float)y));

    // Game flow.

    public void TogglePause()
    {
        Paused = !Paused;
        Hud.RefreshModes();
    }

    public void SetSpeed(double speed)
    {
        Speed = speed;
        Paused = false;
        Hud.RefreshModes();
    }

    /// <summary>Starts the battle again, on the same map or on a new one.</summary>
    public void Restart(bool newMap = false) =>
        Main.Instance.StartBattle(newMap ? Opts with { Seed = random.Next(1, 1_000_000_000) } : Opts);

    public void ToMenu() => Main.Instance.ShowMenu();

    void CheckCampaignEnd()
    {
        var c = Opts.Campaign;
        if (c is null || result is not null) return;
        // Warn before a battle where nobody hits anything ends on its own.
        var quiet = World.Time - World.LastDamage;
        if (quiet > Sim.Battle.Stalemate - 30 && !stalemateWarned)
        {
            stalemateWarned = true;
            Hud.Toast("Aucun tir depuis longtemps : dans 30 s, l'attaquant se replie");
        }
        else if (quiet < Sim.Battle.Stalemate - 30) stalemateWarned = false;
        if (Sim.Battle.Outcome(World, c.Attacker, c.Defender) is not BattleResult r) return;
        result = r;
        var winner = r == BattleResult.Attacker ? c.Attacker : c.Defender;
        Hud.ShowBattleEnd(winner == Opts.Player, ReturnToGalaxy);
    }

    /// <summary>Leaves a campaign battle to play on by itself, back on the galaxy map.</summary>
    public void Delegate()
    {
        if (Opts.Campaign is not CampaignBattle c || result is not null) return;
        Main.Instance.DelegateCampaignBattle(c.BattleId);
    }

    /// <summary>The attacker can fall back: its surviving ships return to the planet they came from.</summary>
    public void Retreat()
    {
        if (Opts.Campaign is not CampaignBattle c || c.Attacker != Opts.Player || result is not null) return;
        result = BattleResult.Retreat;
        Main.Instance.RetreatCampaignBattle(c.BattleId);
    }

    void ReturnToGalaxy() => Main.Instance.FinishCampaignBattle(Opts.Campaign!.BattleId, result!.Value);

    /// <summary>Whether the player sees this entity right now (always true for their own).</summary>
    public bool CanSee(Entity e) => Vision.IsVisible(World, Opts.Player, e);

    // Events of the step: explosions, hits, alerts, sounds.

    void ConsumeEvents()
    {
        var w = World;
        foreach (var ev in w.Events)
        {
            switch (ev)
            {
                case Destroyed d:
                    Explode(d.Id, d.X, d.Y, d.Radius);
                    Play(d.Radius >= 50 ? Sound.BigExplosion : Sound.Explosion, d.X, d.Y, Math.Min(1, 0.4 + d.Radius / 60));
                    if (d.Side == Opts.Player && d.Kind != EntityKind.Ship) Alert($"Perte : {Names.TypeName(d.EntityType)}", d.X, d.Y, true);
                    // Out of a free-for-all before it ends, with no ally left holding a station (or in
                    // regicide, a flagship): say so now, the others may fight on for long.
                    if (d.EntityType == "station" && d.Side == Opts.Player && Opts.Campaign is null && w.Winner is null && !defeated && Modes.Out(w, Opts.Player))
                    {
                        defeated = true;
                        Hud.ShowEnd(false);
                    }
                    else if (d.Side == Opts.Player && !OnScreen(d.X, d.Y)) Alert($"Perte : {Names.TypeName(d.EntityType)}", d.X, d.Y);
                    break;
                case Hit h:
                    Spark(h.X, h.Y);
                    if (h.ShieldId is int sid) shieldFlash[sid] = Now;
                    Play(h.ShieldId is not null ? Sound.Shield : Sound.Hit, h.X, h.Y, 0.6);
                    if (h.Side == Opts.Player) UnderFire(h.TargetId, h.X, h.Y);
                    break;
                case RaidWon rw:
                    var tech = rw.Tech is TechId t ? $", {Techs.Defs[t].Name} niveau {World.Factions[rw.Side].Research[t]}" : "";
                    if (rw.Side == Opts.Player)
                    {
                        Hud.Toast($"Colosse abattu : +{rw.Credits:0} crédits{tech}");
                        Sfx.Play(Sound.Horn);
                    }
                    else Hud.Toast($"Les {Factions.Defs[Worlds.RaceOf(w, rw.Side)].Name} ont abattu le Colosse");
                    break;
                case Delivered dl:
                    if (w.Entities.Get(dl.Id) is { Size: SizeClass.Large } big && (CanSee(big) || big.Side == Opts.Player)) Portal(big);
                    break;
                case Built b:
                    var built = w.Entities.Get(b.Id);
                    if (built?.Side == Opts.Player)
                    {
                        Hud.Toast(built.Type == "starLance" ? "Lance stellaire en service : elle charge" : "Construction terminée");
                        Sfx.Play(Sound.Built);
                    }
                    // Everyone hears of a star lance, wherever it stands.
                    else if (built?.Type == "starLance" && !Opts.Demo)
                    {
                        Hud.Toast($"Les {Factions.Defs[built.Race].Name} ont mis en service une Lance stellaire");
                        Sfx.Play(Sound.Alert);
                    }
                    break;
                case Victory v:
                    if (!defeated) Hud.ShowEnd(Worlds.Allies(w, v.Side, Opts.Player));
                    break;
                case Rebuilt r:
                    if (r.Side == Opts.Player) Hud.Toast($"{Names.TypeName(r.Role)} reconstruit");
                    break;
                case AbilityUsed { Id: AbilityId.StarLance } lance when !Opts.Demo:
                    Hud.Toast(lance.Side == Opts.Player ? "Frappe stellaire lancée" : "Frappe stellaire en approche : fuyez la zone !");
                    Sfx.Play(Sound.Horn);
                    if (lance.Side != Opts.Player) Alert("Frappe stellaire", lance.Tx, lance.Ty, true);
                    break;
                case AbilityUsed a:
                    if (w.Entities.Get(a.EntityId) is { } e && CanSee(e))
                    {
                        AbilityShown(e, a.Id);
                        Play(Sound.Ability, e.X, e.Y);
                    }
                    break;
                case StrikeLanded s:
                    StrikeShown(s.Strike);
                    Play(s.Strike.Kind == StrikeKind.Orbital ? Sound.BigExplosion : Sound.Beam, s.Strike.X2, s.Strike.Y2);
                    break;
                default:
                    if (!Opts.Demo) ShowModeEvent(ev);
                    break;
            }
        }
        w.Events.Clear();
    }

    /// <summary>
    /// Plays a sound from (x, y): panned to its side of the screen, and only if in view. Seen
    /// from afar, single shots and hits stay silent; explosions and abilities still carry.
    /// </summary>
    void Play(Sound s, double x, double y, double gain = 1)
    {
        if (Opts.Demo || (Zoom < LodSimple && QuietAfar.Contains(s))) return;
        var v = ViewRect();
        var w = v.Size.X;
        var h = v.Size.Y;
        if (x < v.Position.X - w * 0.1 || x > v.End.X + w * 0.1 || y < v.Position.Y - h * 0.1 || y > v.End.Y + h * 0.1) return;
        // Seen from afar, a battle is quieter.
        Sfx.Play(s, ((x - v.Position.X) / w * 2 - 1) * 0.8, gain * Math.Min(1, 0.45 + Zoom * 0.55));
    }

    /// <summary>One of the player's things took a hit: structures always raise an alert, ships only out of sight.</summary>
    void UnderFire(int id, double x, double y)
    {
        if (World.Entities.Get(id) is not { } t) return;
        var owner = AbilitySystem.OwnerOf(World, t);
        if (owner.Kind == EntityKind.Structure) Alert($"Attaque : {Names.TypeName(owner.Type)}", x, y, true);
        else if (!OnScreen(x, y)) Alert("Attaque : tes vaisseaux", x, y);
    }

    /// <summary>Reports an attack or a loss, unless one was reported near there lately.</summary>
    void Alert(string text, double x, double y, bool always = false)
    {
        if (Opts.Demo) return;
        var now = Now;
        if (!Ui.Alerts.IsNews(Alerts, x, y, now) && !(always && text.StartsWith("Perte"))) return;
        Alerts.Add(new Alert(text, x, y, now));
        if (Alerts.Count > 12) Alerts.RemoveAt(0);
        alertCursor = -1;
        Sfx.Play(Sound.Alert);
        Hud.Alert(text, () => CenterOn(x, y));
    }

    /// <summary>Centers the view on the latest alert; again, on the one before, and so on.</summary>
    public void JumpToAlert()
    {
        if (Alerts.Count == 0)
        {
            Hud.Toast("Aucune alerte");
            return;
        }
        alertCursor = alertCursor <= 0 ? Alerts.Count - 1 : alertCursor - 1;
        var a = Alerts[alertCursor];
        CenterOn(a.X, a.Y);
    }
}
