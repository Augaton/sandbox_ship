// Mouse and keyboard in battle: selection, orders, control groups, camera, hand flying, and
// the sandbox's spawning. Port of src/input/selection.ts. The keys are the player's
// (Bindings): letter shortcuts follow the keyboard layout (P is P on AZERTY too), camera keys
// are physical positions (ZQSD on AZERTY, WASD on QWERTY); Échap and the groups' digits stay.
using Godot;
using SandboxShip.Core;
using SandboxShip.Sim;
using SandboxShip.Sim.Data;

namespace SandboxShip.Battle;

public abstract record Mode;
public sealed record SelectMode : Mode;
public sealed record PlaceMode(BuildableType Type) : Mode;
public sealed record AttackMoveMode : Mode;
/// <summary>Picking a patrol's points: each Shift + click adds one, a plain click (or letting Shift go) sends it.</summary>
public sealed record PatrolMode(List<Point> Points) : Mode;
/// <summary>Picking the ally the selected ships guard.</summary>
public sealed record GuardMode : Mode;
/// <summary>Picking where (or whom) an ability is aimed at.</summary>
public sealed record AbilityMode(AbilityId Id) : Mode;
/// <summary>Sandbox: `What` is a ship class, a structure, "station" or a hero role, as the web version names them.</summary>
public sealed record SpawnMode(Faction Faction, string What) : Mode;

public sealed record Ghost(double X, double Y, double Radius, bool Ok);

public partial class Selection : Node
{
    public static readonly float[] Zooms = [0.25f, 0.5f, 0.75f, 1, 1.5f, 2, 3];
    const float Edge = 10;
    const float PanSpeed = 900;
    /// <summary>Shift held: the view pans this much faster.</summary>
    const float PanFast = 2.5f;
    const double DoubleTapMs = 350;
    /// <summary>Time constant of the eased zoom, in milliseconds.</summary>
    const double ZoomEaseMs = 70;

    readonly BattleScreen scene;
    public Mode Mode { get; private set; } = new SelectMode();
    /// <summary>Selection rectangle in world coordinates, while dragging.</summary>
    public Rect2? Box { get; private set; }
    /// <summary>Pointer position in world coordinates.</summary>
    public Vector2 Hover;
    /// <summary>Ship flown by hand, if any.</summary>
    public int? PilotId { get; private set; }
    bool pointerInside = true;
    readonly Dictionary<int, List<int>> groups = [];
    (int Group, double Time) lastTap = (-1, 0);
    Vector2? panFrom;
    /// <summary>Zoom level the camera eases towards, and the screen point that stays put meanwhile.</summary>
    (float Zoom, Vector2 At) zoomGoal;
    bool firing;
    (int Id, double Time) lastClick = (-1, 0);
    /// <summary>Which idle ship I selects next.</summary>
    int idleTurn;
    readonly Random random = new();
    /// <summary>The flown ship's controls as last sent: they go again only when they change.</summary>
    Command.PilotInput? sentInput;

    public Selection(BattleScreen scene)
    {
        this.scene = scene;
        zoomGoal = (scene.Zoom, scene.ScreenSize / 2);
    }

    World World => scene.World;
    Side Player => scene.Opts.Player;
    ICommandSink<Command> Sink => scene.Sink;

    /// <summary>The entity under a point; small entities win over the big ones they overlap. Enemies the viewer cannot see are left out.</summary>
    public static Entity? PickAt(World world, double x, double y, double slop, Side? viewer = null)
    {
        Entity? best = null;
        var bestRatio = double.PositiveInfinity;
        var at = new Point(x, y);
        foreach (var e in world.Entities)
        {
            if (viewer is Side v && !Vision.IsVisible(world, v, e)) continue;
            var ratio = Worlds.Dist(e, at) / (e.Radius + slop);
            if (ratio <= 1 && ratio < bestRatio)
            {
                bestRatio = ratio;
                best = e;
            }
        }
        return best;
    }

    /// <summary>What may use an ability among the selection: own ships, and own structures that have one (a star lance).</summary>
    List<Entity> Casters()
    {
        var out_ = OwnShips();
        foreach (var id in scene.Selected)
        {
            if (World.Entities.Get(id) is { Kind: EntityKind.Structure, Build: >= 1 } e && e.Side == Player && e.Abilities.Count > 0) out_.Add(e);
        }
        return out_;
    }

    public List<Entity> OwnShips()
    {
        var out_ = new List<Entity>();
        foreach (var id in scene.Selected)
        {
            var e = World.Entities.Get(id);
            if (e is not null && e.Kind == EntityKind.Ship && e.Side == Player) out_.Add(e);
        }
        return out_;
    }

    public void SetMode(Mode mode)
    {
        Mode = mode;
        scene.Hud.RefreshModes();
    }

    double Slop => 6 / scene.Zoom;

    public override void _Notification(int what)
    {
        if (what == NotificationWMMouseEnter) pointerInside = true;
        else if (what == NotificationWMMouseExit) pointerInside = false;
        else if (what == NotificationApplicationFocusOut)
        {
            firing = false;
            Box = null;
            panFrom = null;
        }
    }

    /// <summary>Whether the pointer is over the battlefield, not over the HUD or outside the window.</summary>
    public bool PointerOver => pointerInside && GetViewport().GuiGetHoveredControl() is null;

    public override void _UnhandledInput(InputEvent ev)
    {
        switch (ev)
        {
            case InputEventMouseButton { Pressed: true } mb when mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown:
                ZoomStep(mb.ButtonIndex == MouseButton.WheelUp ? 1 : -1, mb.Position);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { Pressed: true } mb:
                OnDown(mb);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventKey { Pressed: true, Echo: false } k:
                OnKey(k);
                break;
        }
    }

    /// <summary>Moves and releases: seen before the HUD, so that a drag can cross it. Held keys are the InputMap's (Bindings).</summary>
    public override void _Input(InputEvent ev)
    {
        switch (ev)
        {
            case InputEventMouseMotion m:
                OnMove(m);
                break;
            case InputEventMouseButton { Pressed: false } mb:
                OnUp(mb);
                break;
            // Shift let go with patrol points picked: the patrol goes.
            case InputEventKey { Pressed: false, Keycode: Key.Shift } when Mode is PatrolMode { Points.Count: > 0 } pm:
                SendPatrol(pm.Points);
                break;
        }
    }

    void OnDown(InputEventMouseButton p)
    {
        var w = scene.WorldPoint(p.Position);
        if (PilotId is not null)
        {
            if (p.ButtonIndex == MouseButton.Left) firing = true;
            return;
        }
        if (p.ButtonIndex == MouseButton.Middle)
        {
            panFrom = p.Position;
            return;
        }
        if (p.ButtonIndex == MouseButton.Right)
        {
            if (Mode is not SelectMode) SetMode(new SelectMode());
            else if (p.CtrlPressed) AttackMove(w.X, w.Y, p.ShiftPressed);
            else OrderAt(w.X, w.Y, p.ShiftPressed);
            return;
        }
        if (p.ButtonIndex != MouseButton.Left) return;
        var shift = p.ShiftPressed;
        switch (Mode)
        {
            case PatrolMode m:
                m.Points.Add(new Point(w.X, w.Y));
                scene.Marker(w.X, w.Y, Palette.Hex(0xe8a13a));
                if (!shift || m.Points.Count >= Movement.MaxRoute - 1) SendPatrol(m.Points);
                break;
            case GuardMode:
            {
                var ward = PickAt(World, w.X, w.Y, Slop, Player);
                if (ward is null || !Worlds.Allies(World, ward.Side, Player))
                {
                    scene.Hud.Toast("Choisis un allié");
                    break;
                }
                GuardOrder(ward, shift);
                if (!shift) SetMode(new SelectMode());
                break;
            }
            case PlaceMode m:
            {
                var r = Sink.Send(new Command.PlaceStructure(m.Type, w.X, w.Y));
                if (!r.Ok) scene.Hud.Toast(r.Reason);
                else if (!shift) SetMode(new SelectMode());
                break;
            }
            case AttackMoveMode:
                AttackMove(w.X, w.Y);
                if (!shift) SetMode(new SelectMode());
                break;
            case AbilityMode m:
            {
                var t = PickAt(World, w.X, w.Y, Slop, Player);
                var used = AimAbility(m.Id, new Aim(w.X, w.Y, t is not null && Worlds.Foes(World, t.Side, Player) ? t.Id : null));
                if (used && !shift) SetMode(new SelectMode());
                break;
            }
            case SpawnMode m:
            {
                var r = Sink.Send(new Command.Spawn(m.Faction, m.What, w.X, w.Y, random.NextDouble() * Math.Tau));
                if (!r.Ok) scene.Hud.Toast(r.Reason);
                break;
            }
            default:
                Box = new Rect2(w, Vector2.Zero);
                break;
        }
    }

    void AttackMove(double x, double y, bool queued = false)
    {
        var ids = OwnShips().Select(s => s.Id).ToArray();
        if (ids.Length == 0) return;
        Sink.Send(new Command.Move(ids, x, y, true, queued));
        scene.Marker(x, y, Palette.Hex(0xe8a13a));
    }

    /// <summary>The selected ships round the points picked, from where they are; back to selecting.</summary>
    void SendPatrol(List<Point> points)
    {
        var ids = OwnShips().Select(s => s.Id).ToArray();
        if (ids.Length > 0) Report(Sink.Send(new Command.Patrol(ids, [.. points])));
        SetMode(new SelectMode());
    }

    /// <summary>The selected ships, but the ward itself, guard it.</summary>
    void GuardOrder(Entity ward, bool queued)
    {
        var ids = OwnShips().Where(s => s.Id != ward.Id).Select(s => s.Id).ToArray();
        if (ids.Length == 0) return;
        Report(Sink.Send(new Command.Guard(ids, ward.Id, queued)));
        scene.Marker(ward.X, ward.Y, Palette.Hex(0x5aa9e6));
    }

    /// <summary>The order after the first selected ship's current stance, for all of them; its name shown.</summary>
    public void CycleStance()
    {
        var ships = OwnShips();
        if (ships.Count == 0) return;
        var i = Array.IndexOf(StanceOrder, ships[0].Stance);
        SetStance(StanceOrder[(i + 1) % StanceOrder.Length]);
    }

    /// <summary>Stances in the order the key goes through them, and the panel shows them.</summary>
    public static readonly Stance[] StanceOrder = [Stance.Aggressive, Stance.Defensive, Stance.Hold, Stance.Passive];

    public static string StanceName(Stance s) => s switch
    {
        Stance.Aggressive => "Agressive",
        Stance.Hold => "Tenir la position",
        Stance.Passive => "Passive",
        _ => "Défensive",
    };

    public void SetStance(Stance stance)
    {
        var ids = OwnShips().Select(s => s.Id).ToArray();
        if (ids.Length == 0) return;
        if (Report(Sink.Send(new Command.SetStance(ids, stance)))) scene.Hud.Toast($"Posture : {StanceName(stance)}");
    }

    /// <summary>Patrol or guard mode for the selected ships, from the keys or the panel's buttons.</summary>
    public void ArmPatrol()
    {
        if (OwnShips().Count > 0) SetMode(new PatrolMode([]));
    }

    public void ArmGuard()
    {
        if (OwnShips().Count > 0) SetMode(new GuardMode());
    }

    /// <summary>A refusal shows as a toast; whether the command went through.</summary>
    bool Report(Result r)
    {
        if (!r.Ok) scene.Hud.Toast(r.Reason);
        return r.Ok;
    }

    /// <summary>Sends the selected ships to a point, from the minimap: a plain move or an attack-move.</summary>
    public void MoveSelection(double x, double y, bool attack)
    {
        if (attack)
        {
            AttackMove(x, y);
            return;
        }
        var ids = OwnShips().Select(s => s.Id).ToArray();
        if (ids.Length == 0) return;
        Sink.Send(new Command.Move(ids, x, y));
        scene.Marker(x, y, Palette.Hex(0x6fd08c));
    }

    /// <summary>Warships with nothing to do: no order, no target, not flown. Hangar craft do not count.</summary>
    public List<Entity> IdleShips() =>
        World.Entities.Where(e => e.Kind == EntityKind.Ship && e.Side == Player && !e.Launched && e.Order is null && e.TargetId is null && e.Pilot is null).ToList();

    /// <summary>Selects the next idle ship and centers on it; with `all`, selects every idle ship.</summary>
    public void SelectIdle(bool all)
    {
        var idle = IdleShips();
        if (idle.Count == 0)
        {
            scene.Hud.Toast("Aucun vaisseau inactif");
            return;
        }
        var sel = scene.Selected;
        sel.Clear();
        if (all)
        {
            foreach (var e in idle) sel.Add(e.Id);
            return;
        }
        var s = idle[idleTurn++ % idle.Count];
        sel.Add(s.Id);
        scene.CenterOn(s.X, s.Y);
    }

    /// <summary>Control groups that still have ships: number, ships alive.</summary>
    public List<(int Group, int Alive)> GroupSizes() =>
        groups.OrderBy(g => g.Key).Select(g => (g.Key, g.Value.Count(World.Entities.Has))).Where(g => g.Item2 > 0).ToList();

    void OnMove(InputEventMouseMotion p)
    {
        Hover = scene.WorldPoint(p.Position);
        if (panFrom is Vector2 from)
        {
            scene.Center -= (p.Position - from) / scene.Zoom;
            panFrom = p.Position;
        }
        if (Box is Rect2 b) Box = new Rect2(b.Position, Hover - b.Position);
    }

    void OnUp(InputEventMouseButton p)
    {
        if (p.ButtonIndex == MouseButton.Middle) panFrom = null;
        if (p.ButtonIndex == MouseButton.Left) firing = false;
        if (Box is not Rect2 b || p.ButtonIndex != MouseButton.Left) return;
        Box = null;
        var shift = p.ShiftPressed;
        var sel = scene.Selected;
        var zoom = scene.Zoom;
        if (Math.Abs(b.Size.X) * zoom < 6 && Math.Abs(b.Size.Y) * zoom < 6)
        {
            var end = b.End;
            var e = Hovered(end.X, end.Y);
            var now = BattleScreen.Now;
            var dbl = e is not null && e.Id == lastClick.Id && now - lastClick.Time < DoubleTapMs;
            lastClick = (e?.Id ?? -1, now);
            if ((dbl || p.CtrlPressed) && e?.Kind == EntityKind.Ship && e.Side == Player)
            {
                // Double click or Ctrl + click: every ship of that class on screen.
                if (!shift && !dbl) sel.Clear();
                var view = scene.ViewRect();
                foreach (var s in World.Entities)
                {
                    if (s.Kind == EntityKind.Ship && s.Side == Player && s.Type == e.Type && view.HasPoint(new Vector2((float)s.X, (float)s.Y))) sel.Add(s.Id);
                }
                return;
            }
            if (!shift) sel.Clear();
            if (e is not null && shift && sel.Contains(e.Id)) sel.Remove(e.Id);
            else if (e is not null) sel.Add(e.Id);
            return;
        }
        var box = b.Abs();
        if (!shift) sel.Clear();
        // A ship counts as soon as its hull overlaps the rectangle: big ships are easy to catch.
        foreach (var e in World.Entities)
        {
            if (e.Kind != EntityKind.Ship || e.Side != Player) continue;
            if (e.X + e.Radius >= box.Position.X && e.X - e.Radius <= box.End.X && e.Y + e.Radius >= box.Position.Y && e.Y - e.Radius <= box.End.Y) sel.Add(e.Id);
        }
    }

    /// <summary>What a click at this point would pick: a part of one of your ships stands for the ship.</summary>
    public Entity? Hovered(double? x = null, double? y = null)
    {
        var e = PickAt(World, x ?? Hover.X, y ?? Hover.Y, Slop, Player);
        var parent = World.Entities.Get(e?.ParentId);
        return e is not null && parent?.Kind == EntityKind.Ship && e.Side == Player ? parent : e;
    }

    /// <summary>
    /// Right click: attack the foe under the cursor, guard the ally under it (one not selected),
    /// or move there; `queued` (Shift), once the orders given before are done. With the station
    /// selected, sets the rally point.
    /// </summary>
    void OrderAt(double x, double y, bool queued = false)
    {
        var ships = OwnShips();
        if (ships.Count > 0)
        {
            var t = PickAt(World, x, y, Slop, Player);
            var ids = ships.Select(s => s.Id).ToArray();
            var ward = t is null ? null : AbilitySystem.OwnerOf(World, t);
            if (t is not null && Worlds.Foes(World, t.Side, Player))
            {
                Sink.Send(new Command.Attack(ids, t.Id, queued));
                scene.Marker(t.X, t.Y, Palette.Hex(0xe05a4a));
            }
            else if (ward is not null && !scene.Selected.Contains(ward.Id) && ward.Kind != EntityKind.Hardpoint)
            {
                GuardOrder(ward, queued);
            }
            else
            {
                Sink.Send(new Command.Move(ids, x, y, Queued: queued));
                scene.Marker(x, y, Palette.Hex(0x6fd08c));
            }
            return;
        }
        var station = Worlds.StationOf(World, Player);
        if (station is not null && scene.Selected.Contains(station.Id))
        {
            Sink.Send(new Command.SetRally(x, y));
            scene.Marker(x, y, Palette.Hex(0x6fd08c));
        }
    }

    /// <summary>Aims for the next zoom level in or out; the world point under `at` stays put.</summary>
    public void ZoomStep(int dir, Vector2 at)
    {
        var i = Array.IndexOf(Zooms, zoomGoal.Zoom);
        var zoom = Zooms[Math.Clamp((i < 0 ? 3 : i) + dir, 0, Zooms.Length - 1)];
        zoomGoal = (zoom, at);
    }

    /// <summary>Eases the camera zoom towards its goal, in log space so that in and out feel alike.</summary>
    void EaseZoom(double delta)
    {
        var (goal, at) = zoomGoal;
        var z = scene.Zoom;
        if (z == goal) return;
        var t = 1 - Math.Exp(-delta / ZoomEaseMs);
        var ratio = Math.Log(goal / z);
        var next = Math.Abs(ratio) < 0.01 ? goal : (float)(z * Math.Exp(ratio * t));
        // Zooming around the center: shift it to keep the anchor point fixed.
        var half = scene.ScreenSize / 2;
        var anchor = scene.Center + (at - half) / z;
        scene.Zoom = next;
        scene.Center = anchor - (at - half) / next;
    }

    void CenterOnSelection()
    {
        var pts = scene.Selected.Select(id => World.Entities.Get(id)).Where(e => e is not null).ToList();
        if (pts.Count > 0) scene.CenterOn(pts.Average(e => e!.X), pts.Average(e => e!.Y));
    }

    /// <summary>The keys of the battle: Échap and the groups' digits as they are, the others as the player set them (Bindings).</summary>
    void OnKey(InputEventKey ev)
    {
        if (Bindings.Pressed(ev, Bindings.Pilot))
        {
            TogglePilot();
            return;
        }
        if (Bindings.Pressed(ev, Bindings.Ability))
        {
            UseAbilities();
            return;
        }
        if (PilotId is not null)
        {
            if (ev.Keycode == Key.Escape) StopPilot();
            else if (Bindings.Pressed(ev, Bindings.Pause)) scene.TogglePause();
            return;
        }
        if (Bindings.Pressed(ev, Bindings.SelectAll))
        {
            foreach (var e in World.Entities) if (e.Kind == EntityKind.Ship && e.Side == Player) scene.Selected.Add(e.Id);
            return;
        }
        var digit = ev.PhysicalKeycode - Key.Key0;
        if (digit is >= 1 and <= 9)
        {
            if (ev.CtrlPressed || ev.ShiftPressed) groups[(int)digit] = OwnShips().Select(s => s.Id).ToList();
            else RecallGroup((int)digit);
            return;
        }
        var center = scene.ScreenSize / 2;
        if (ev.Keycode == Key.Escape)
        {
            // A dialog first, then the current mode, then the selection; with nothing left, the pause menu.
            if (scene.Hud.Escape()) return;
            if (Mode is not SelectMode) SetMode(new SelectMode());
            else if (scene.Selected.Count > 0) scene.Selected.Clear();
            else scene.Hud.PauseMenu();
        }
        else if (Bindings.Pressed(ev, Bindings.Pause)) scene.TogglePause();
        else if (Bindings.Pressed(ev, Bindings.AttackMove))
        {
            if (OwnShips().Count > 0) SetMode(new AttackMoveMode());
        }
        else if (Bindings.Pressed(ev, Bindings.Patrol)) ArmPatrol();
        else if (Bindings.Pressed(ev, Bindings.Guard)) ArmGuard();
        else if (Bindings.Pressed(ev, Bindings.Stance)) CycleStance();
        else if (Bindings.Pressed(ev, Bindings.Stop)) Sink.Send(new Command.Stop([.. OwnShips().Select(s => s.Id)]));
        else if (Bindings.Pressed(ev, Bindings.CenterSelection)) CenterOnSelection();
        else if (Bindings.Pressed(ev, Bindings.LastAlert)) scene.JumpToAlert();
        else if (Bindings.Pressed(ev, Bindings.Idle)) SelectIdle(ev.ShiftPressed);
        else if (Bindings.Pressed(ev, Bindings.Mute))
        {
            Settings.Current.Muted = !Settings.Current.Muted;
            Settings.Current.Save();
            scene.Hud.Toast(Settings.Current.Muted ? "Son coupé" : "Son rétabli");
        }
        else if (Bindings.Pressed(ev, Bindings.ZoomIn)) ZoomStep(1, center);
        else if (Bindings.Pressed(ev, Bindings.ZoomOut)) ZoomStep(-1, center);
        else if (Bindings.Pressed(ev, Bindings.CenterStation))
        {
            if (Worlds.StationOf(World, Player) is { } st) scene.CenterOn(st.X, st.Y);
        }
    }

    public void RecallGroup(int n)
    {
        var ids = groups.GetValueOrDefault(n)?.Where(World.Entities.Has).ToList() ?? [];
        if (ids.Count == 0) return;
        var sel = scene.Selected;
        sel.Clear();
        foreach (var id in ids) sel.Add(id);
        var now = BattleScreen.Now;
        if (lastTap.Group == n && now - lastTap.Time < DoubleTapMs)
        {
            var e = World.Entities.Get(ids[0])!;
            scene.CenterOn(e.X, e.Y);
        }
        lastTap = (n, now);
    }

    /// <summary>
    /// Fires the abilities of the selected ships (or of the piloted one), all of them or only
    /// those of one kind, when they are ready. Those that need a target wait for the next click;
    /// a pilot aims them with the crosshair.
    /// </summary>
    public void UseAbilities(AbilityId? id = null)
    {
        var piloted = World.Entities.Get(PilotId);
        var ships = piloted is not null ? [piloted] : Casters();
        // Every ready ability of every ship: big ships have several.
        var ready = ships.SelectMany(s => s.Abilities.Where(a => (id is null || a.Id == id) && a.Cooldown == 0).Select(a => (Ship: s, a.Id))).ToList();
        if (ready.Count == 0)
        {
            if (ships.Exists(s => s.Abilities.Count > 0)) scene.Hud.Toast("Capacité en recharge");
            return;
        }
        var aimed = ready.FindIndex(r => Abilities.Defs[r.Id].Target != AbilityTarget.Self);
        foreach (var (s, a) in ready) if (Abilities.Defs[a].Target == AbilityTarget.Self) Sink.Send(new Command.UseAbility(s.Id, a));
        if (aimed < 0) return;
        var aid = ready[aimed].Id;
        var def = Abilities.Defs[aid];
        if (piloted?.Pilot is Pilot p)
        {
            var t = Combat.AimedTarget(World, piloted, p, def.Range ?? 0);
            AimAbility(aid, new Aim(p.AimX, p.AimY, t?.Id));
            return;
        }
        SetMode(new AbilityMode(aid));
        scene.Hud.Toast($"{def.Name} : {(def.Target == AbilityTarget.Enemy ? "clique sur un ennemi" : "clique où viser")}");
    }

    /// <summary>Uses this ability of every ready selected ship at the aim; false if none could.</summary>
    bool AimAbility(AbilityId id, Aim aim)
    {
        var piloted = World.Entities.Get(PilotId);
        var ships = (piloted is not null ? [piloted] : Casters()).Where(s => s.Abilities.Exists(a => a.Id == id && a.Cooldown == 0));
        var reason = "Capacité en recharge";
        var used = false;
        foreach (var s in ships)
        {
            var r = Sink.Send(new Command.UseAbility(s.Id, id, aim));
            if (r.Ok) used = true;
            else reason = r.Reason;
        }
        if (!used) scene.Hud.Toast(reason);
        return used;
    }

    /// <summary>Takes the controls of the one selected ship, or gives them back.</summary>
    public void TogglePilot()
    {
        if (PilotId is not null)
        {
            StopPilot();
            return;
        }
        var ships = OwnShips();
        if (ships.Count != 1)
        {
            scene.Hud.Toast("Sélectionne un seul de tes vaisseaux pour le piloter");
            return;
        }
        var s = ships[0];
        Sink.Send(new Command.StartPilot(s.Id));
        sentInput = null;
        PilotId = s.Id;
        Box = null;
        SetMode(new SelectMode());
    }

    public void StopPilot()
    {
        if (PilotId is int id) Sink.Send(new Command.StopPilot(id));
        PilotId = null;
        firing = false;
        scene.Hud.RefreshModes();
    }

    /// <summary>The controls, sent whenever they change; the world may not show them flown yet (a host answers later).</summary>
    void UpdatePilot()
    {
        var s = World.Entities.Get(PilotId);
        if (s is null)
        {
            StopPilot();
            scene.Hud.Toast("Vaisseau perdu");
            return;
        }
        var thrust = (Bindings.Held(Bindings.PanUp) ? 1 : 0) - (Bindings.Held(Bindings.PanDown) ? 1 : 0);
        var turn = (Bindings.Held(Bindings.PanRight) ? 1 : 0) - (Bindings.Held(Bindings.PanLeft) ? 1 : 0);
        var aim = scene.WorldPoint(GetViewport().GetMousePosition());
        var input = new Command.PilotInput(s.Id, thrust, turn, aim.X, aim.Y, firing);
        if (input != sentInput)
        {
            Sink.Send(input);
            sentInput = input;
        }
        // The camera follows the ship, a little behind.
        scene.Center += (new Vector2((float)s.X, (float)s.Y) - scene.Center) * 0.15f;
    }

    /// <summary>Per frame: eased zoom, then hand flying or camera panning (keys, edges).</summary>
    public void Tick(double delta)
    {
        EaseZoom(delta);
        // Its casters gone (destroyed, deselected), an ability has nothing left to aim with.
        if (Mode is AbilityMode m && !OwnShips().Exists(s => s.Abilities.Exists(a => a.Id == m.Id))) SetMode(new SelectMode());
        if (PilotId is not null)
        {
            UpdatePilot();
            return;
        }
        var p = GetViewport().GetMousePosition();
        var size = scene.ScreenSize;
        var fast = Input.IsKeyPressed(Key.Shift) ? PanFast : 1;
        var step = (float)(PanSpeed * fast * delta / 1000) / scene.Zoom;
        // A capture window (--shot) is not being played: wherever the pointer is, the view stays.
        var edge = Settings.Current.EdgePan && PointerOver && panFrom is null && Dev.Shot is null;
        // Ctrl + a key gives an order (Ctrl + A selects everything): the view keys rest while Ctrl is down.
        var keys = !Input.IsKeyPressed(Key.Ctrl);
        var c = scene.Center;
        if ((keys && Bindings.Held(Bindings.PanLeft)) || (edge && p.X < Edge)) c.X -= step;
        if ((keys && Bindings.Held(Bindings.PanRight)) || (edge && p.X > size.X - Edge)) c.X += step;
        if ((keys && Bindings.Held(Bindings.PanUp)) || (edge && p.Y < Edge)) c.Y -= step;
        if ((keys && Bindings.Held(Bindings.PanDown)) || (edge && p.Y > size.Y - Edge)) c.Y += step;
        scene.Center = c;
    }

    /// <summary>Ghost of the structure being placed: where it would go and whether it can.</summary>
    public Ghost? PlacementGhost()
    {
        if (Mode is not PlaceMode m) return null;
        var p = Structures.CheckPlacement(World, Player, m.Type, Hover.X, Hover.Y);
        return new Ghost(p.X, p.Y, Buildables.Defs[m.Type].Radius, p.Ok);
    }
}
