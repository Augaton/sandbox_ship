// The galaxy map's view, and the orders given on it that the web version does not have: zoom
// and pan (wheel, keys, screen edges, middle button) as the battle camera does them; fleets on
// the move, picked on the map, sent elsewhere or stopped; rally points, set and drawn.
using Godot;
using SandboxShip.Core;
using SandboxShip.Render;
using SandboxShip.Sim;

namespace SandboxShip.Campaign;

public partial class GalaxyScreen
{
    /// <summary>Zoom of a wheel notch or a key press.</summary>
    const double ZoomFactor = 1.25;
    /// <summary>The closest view: a map unit is this many pixels at most.</summary>
    const double MaxScale = 2;
    /// <summary>Time constant of the eased zoom, in milliseconds.</summary>
    const double ZoomEaseMs = 70;
    const float Edge = 10, PanSpeed = 900;
    /// <summary>Shift held: the view pans this much faster.</summary>
    const float PanFast = 2.5f;
    /// <summary>How far, in pixels, the galaxy may leave the free part of the screen.</summary>
    const double ViewMargin = 80;
    /// <summary>Pixels from a fleet marker within which a click picks it.</summary>
    const double FleetSlop = 12;

    /// <summary>A fleet of the player's on the move, shown in the panel; no planet is selected meanwhile.</summary>
    public int? SelectedFleet { get; private set; }
    /// <summary>The next planet of the selected fleet: where to look for it once it docks and merges there.</summary>
    int? fleetBound;
    int? hoverFleet;
    /// <summary>Waiting for the click on the planet the selected planets rally to.</summary>
    public bool Rallying { get; private set; }

    /// <summary>Scale of the view fitted to the galaxy, and the zoom over it: 1 shows the whole galaxy.</summary>
    double fitS = 1, zoom = 1;
    /// <summary>Map point at the middle of the map area.</summary>
    double cx, cy;
    /// <summary>Zoom the view eases towards, and the screen point that stays put meanwhile.</summary>
    (double Zoom, Vector2 At) zoomGoal = (1, Vector2.Zero);
    Vector2? panFrom;
    /// <summary>Physical keys held down.</summary>
    readonly HashSet<Key> keys = [];

    /// <summary>Screen space the HUD leaves for the map: under the top bar, left of the planet panel.</summary>
    Rect2 MapArea() => new(24, Top + 16, Math.Max(1, laidOut.X - PanelW - 48), Math.Max(1, laidOut.Y - Top - 32));

    /// <summary>The map area less what the open drawer covers: where a planet is centered.</summary>
    Rect2 FreeArea()
    {
        var a = MapArea();
        var left = Math.Max(a.Position.X, (Hud?.DrawerRight ?? 0) + 16);
        return new Rect2(left, a.Position.Y, Math.Max(1, a.End.X - left), a.Size.Y);
    }

    double MaxZoom => Math.Max(1, MaxScale / fitS);

    bool PointerOverMap => pointerInside && GetViewport().GuiGetHoveredControl() is null;

    /// <summary>The window changed size: the fitted scale follows, the view keeps its center and zoom.</summary>
    void Fit(bool first)
    {
        var area = MapArea();
        fitS = Math.Max(0.2, Math.Min(area.Size.X / Galaxy.W, area.Size.Y / Galaxy.H));
        if (first)
        {
            cx = Galaxy.W / 2;
            cy = Galaxy.H / 2;
        }
        zoom = Math.Min(zoom, MaxZoom);
        zoomGoal = (Math.Min(zoomGoal.Zoom, MaxZoom), zoomGoal.At);
        ApplyView();
    }

    /// <summary>The view moved or the drawer opened: scale, offset and planet sprites follow.</summary>
    public void ApplyView()
    {
        var g = Galaxy;
        var mid = MapArea().GetCenter();
        var free = FreeArea();
        s = fitS * zoom;
        ox = Clamp(mid.X - cx * s, free.Position.X, free.End.X, g.W * s);
        oy = Clamp(mid.Y - cy * s, free.Position.Y, free.End.Y, g.H * s);
        cx = (mid.X - ox) / s;
        cy = (mid.Y - oy) / s;
        var opaque = Assets.OpaqueSize(Assets.Planet);
        foreach (var p in g.Planets)
        {
            var r = Radius(p);
            var scale = (float)(r * 2 / opaque);
            planets[p.Id].Position = new Vector2((float)Sx(p.X), (float)Sy(p.Y));
            planets[p.Id].Scale = new Vector2(scale, scale);
            emblems[p.Id].Position = new Vector2((float)(Sx(p.X) - r - 6), (float)(Sy(p.Y) - r - 6));
        }
    }

    /// <summary>
    /// An offset along one axis: a galaxy `extent` pixels long that fits in the free span
    /// [lo, hi] stays inside it; a bigger one leaves no more of it empty than it overflows,
    /// ViewMargin at most, so that the fitted view never slides under the panels.
    /// </summary>
    static double Clamp(double o, double lo, double hi, double extent)
    {
        var m = Math.Clamp(extent - (hi - lo), 0, ViewMargin);
        return Math.Clamp(o, Math.Min(hi - m - extent, lo - m), Math.Max(lo + m, hi + m - extent));
    }

    /// <summary>Zooms to `z`, the map point under `at` staying put.</summary>
    void ZoomAt(double z, Vector2 at)
    {
        var mx = (at.X - ox) / s;
        var my = (at.Y - oy) / s;
        zoom = z;
        var ns = fitS * z;
        var mid = MapArea().GetCenter();
        cx = mx + (mid.X - at.X) / ns;
        cy = my + (mid.Y - at.Y) / ns;
    }

    /// <summary>Aims a zoom step in or out; the map point under `at` stays put.</summary>
    void ZoomStep(int dir, Vector2 at) => zoomGoal = (Math.Clamp(zoomGoal.Zoom * Math.Pow(ZoomFactor, dir), 1, MaxZoom), at);

    /// <summary>Back to the whole galaxy.</summary>
    void FitView()
    {
        zoom = 1;
        zoomGoal = (1, zoomGoal.At);
        cx = Galaxy.W / 2;
        cy = Galaxy.H / 2;
        ApplyView();
    }

    /// <summary>Brings a map point to the middle of the free part of the screen, as far as the view goes.</summary>
    void CenterOn(double x, double y)
    {
        var mid = MapArea().GetCenter();
        var at = FreeArea().GetCenter();
        cx = x + (mid.X - at.X) / s;
        cy = y + (mid.Y - at.Y) / s;
        ApplyView();
    }

    bool Held(params Key[] codes) => codes.Any(keys.Contains);

    /// <summary>Per frame: eased zoom, then panning with the keys and the screen edges.</summary>
    void TickView(double delta)
    {
        var moved = false;
        var (goal, at) = zoomGoal;
        if (zoom != goal)
        {
            // In log space, so that in and out feel alike.
            var ratio = Math.Log(goal / zoom);
            ZoomAt(Math.Abs(ratio) < 0.01 ? goal : zoom * Math.Exp(ratio * (1 - Math.Exp(-delta * 1000 / ZoomEaseMs))), at);
            moved = true;
        }
        var p = GetViewport().GetMousePosition();
        var size = laidOut;
        var step = PanSpeed * (Held(Key.Shift) ? PanFast : 1) * delta / s;
        // A capture run (--shot) does not follow the pointer, which may rest anywhere.
        var edge = Settings.Current.EdgePan && PointerOverMap && panFrom is null && Dev.Shot is null;
        // Keys typed in a quantity box are not for the map; nor are letters with Ctrl down.
        var arrows = GetViewport().GuiGetFocusOwner() is not LineEdit;
        var letters = arrows && !Held(Key.Ctrl);
        double dx = 0, dy = 0;
        if ((arrows && Held(Key.Left)) || (letters && Held(Key.A)) || (edge && p.X < Edge)) dx -= step;
        if ((arrows && Held(Key.Right)) || (letters && Held(Key.D)) || (edge && p.X > size.X - Edge)) dx += step;
        if ((arrows && Held(Key.Up)) || (letters && Held(Key.W)) || (edge && p.Y < Edge)) dy -= step;
        if ((arrows && Held(Key.Down)) || (letters && Held(Key.S)) || (edge && p.Y > size.Y - Edge)) dy += step;
        if (dx != 0 || dy != 0)
        {
            cx += dx;
            cy += dy;
            moved = true;
        }
        if (moved) ApplyView();
        // Fleets move under a still pointer: the one it points at is found again each frame.
        hoverFleet = PointerOverMap ? PickFleet(p) : null;
    }

    /// <summary>Wheel and middle button pressed on the map.</summary>
    void ViewButton(InputEventMouseButton mb)
    {
        if (mb.ButtonIndex == MouseButton.Middle) panFrom = mb.Position;
        else ZoomStep(mb.ButtonIndex == MouseButton.WheelUp ? 1 : -1, mb.Position);
    }

    /// <summary>Middle drag and held keys: seen before the HUD, so that a drag can cross it.</summary>
    void ViewInput(InputEvent ev)
    {
        switch (ev)
        {
            case InputEventMouseMotion m when panFrom is Vector2 from:
                cx -= (m.Position.X - from.X) / s;
                cy -= (m.Position.Y - from.Y) / s;
                panFrom = m.Position;
                ApplyView();
                break;
            case InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Middle }:
                panFrom = null;
                break;
            case InputEventKey { Pressed: true } k:
                keys.Add(k.PhysicalKeycode);
                break;
            case InputEventKey { Pressed: false } k:
                keys.Remove(k.PhysicalKeycode);
                break;
        }
    }

    /// <summary>Keys of the view, the fleets and the drawer; false for any other key.</summary>
    bool ViewKey(InputEventKey ev)
    {
        var mid = FreeArea().GetCenter();
        switch (ev.Keycode)
        {
            case Key.Plus or Key.Equal or Key.KpAdd:
                ZoomStep(1, mid);
                return true;
            case Key.Minus or Key.KpSubtract:
                ZoomStep(-1, mid);
                return true;
            case Key.H or Key.Home:
                FitView();
                return true;
            case Key.X when SelectedFleet is not null:
                StopFleet();
                return true;
            case Key.F:
                Hud.ToggleDrawer(GalaxyHud.Drawer.Fleets);
                return true;
            case Key.E:
                Hud.ToggleDrawer(GalaxyHud.Drawer.Empire);
                return true;
            case Key.J:
                Hud.ToggleDrawer(GalaxyHud.Drawer.Log);
                return true;
            case Key.T:
                Hud.ToggleDrawer(GalaxyHud.Drawer.Diplomacy);
                return true;
        }
        return false;
    }

    /// <summary>Where a fleet on the move is, in map coordinates.</summary>
    (double X, double Y) FleetPoint(Fleet f)
    {
        var a = Galaxy.Planets[f.From];
        var b = Galaxy.Planets[f.To ?? f.From];
        return (a.X + (b.X - a.X) * f.Progress, a.Y + (b.Y - a.Y) * f.Progress);
    }

    /// <summary>The player's fleet on the move whose marker is under a screen point, the nearest one.</summary>
    int? PickFleet(Vector2 at)
    {
        int? best = null;
        var bestD = FleetSlop;
        foreach (var f in Galaxy.Fleets.Values)
        {
            if (f.Side != Player || f.At is not null || f.To is null) continue;
            var (x, y) = FleetPoint(f);
            var d = Worlds.Hypot(Sx(x) - at.X, Sy(y) - at.Y);
            if (d > bestD) continue;
            bestD = d;
            best = f.Id;
        }
        return best;
    }

    /// <summary>A click on the map that picks a rally point or a fleet on the move; false when it is for the planets.</summary>
    bool MapClick(Vector2 at, bool shift)
    {
        if (LanceFrom is int from)
        {
            AimLance(from, Pick(at));
            return true;
        }
        if (Rallying)
        {
            if (Pick(at) is int dest) RallyTo(dest);
            else
            {
                Rallying = false;
                Hud.Toast("Ralliement annulé");
            }
            return true;
        }
        if (shift || PickFleet(at) is not int id) return false;
        SelectFleet(id);
        return true;
    }

    void SelectFleet(int id)
    {
        SelectedFleet = id;
        fleetBound = null;
        Selected = null;
        Selection.Clear();
        Rallying = false;
    }

    /// <summary>A planet chosen from a list: selected alone, and brought to the middle of the view.</summary>
    public void ShowPlanet(int id)
    {
        Click(id, false);
        var p = Galaxy.Planets[id];
        CenterOn(p.X, p.Y);
    }

    /// <summary>A fleet chosen from the list: selected (a docked one, through its planet), and brought to the middle of the view.</summary>
    public void ShowFleet(int id)
    {
        if (!Galaxy.Fleets.TryGetValue(id, out var f)) return;
        if (f.At is int at)
        {
            ShowPlanet(at);
            return;
        }
        SelectFleet(id);
        var (x, y) = FleetPoint(f);
        CenterOn(x, y);
    }

    /// <summary>Once the selected fleet docks (or merges with the fleet waiting where it arrived), its planet is shown instead.</summary>
    void FollowFleet()
    {
        if (SelectedFleet is not int id) return;
        var g = Galaxy;
        var f = g.Fleets.TryGetValue(id, out var found) ? found : null;
        if (f is not null && f.At is null)
        {
            fleetBound = f.To;
            return;
        }
        SelectedFleet = null;
        var at = f?.At ?? fleetBound;
        if (at is int p && (f is not null || Galaxies.FleetsAt(g, p, Player).Count > 0)) Click(p, false);
    }

    /// <summary>Sends the selected fleet on the move to `dest`, the shorter way, turning back if need be.</summary>
    void Redirect(int fleetId, int dest)
    {
        var r = Galaxies.RedirectFleet(Galaxy, fleetId, dest);
        if (!r.Ok) Hud.Toast(r.Reason);
        else Hud.Refresh();
    }

    /// <summary>The selected fleet on the move stops at its next planet.</summary>
    public void StopFleet()
    {
        if (SelectedFleet is not int id) return;
        var r = Galaxies.StopFleet(Galaxy, id);
        if (!r.Ok)
        {
            Hud.Toast(r.Reason);
            return;
        }
        Hud.Toast($"La flotte s'arrêtera à {Galaxy.Planets[Galaxy.Fleets[id].To!.Value].Name}");
        Hud.Refresh();
    }

    List<int> OwnTargets() => [.. Targets.Where(id => Galaxy.Planets[id].Owner == Player)];

    /// <summary>The next click on a planet sets the rally point of the selected planets.</summary>
    public void BeginRally()
    {
        if (OwnTargets().Count == 0) return;
        Rallying = true;
        Hud.Toast("Clique sur la planète de ralliement (Échap : annuler)");
    }

    public void ClearRally() => RallyTo(null);

    /// <summary>Sets (or with null, clears) the rally point of the player's selected planets; a planet rallying to itself clears its own.</summary>
    void RallyTo(int? dest)
    {
        Rallying = false;
        var g = Galaxy;
        var own = OwnTargets();
        if (own.Count == 0)
        {
            Hud.Toast("Sélectionne d'abord une de tes planètes");
            return;
        }
        var refused = own.Select(id => Galaxies.SetRally(g, Player, id, dest)).Where(r => !r.Ok).ToList();
        if (refused.Count == own.Count)
        {
            Hud.Toast(refused[0].Reason);
            return;
        }
        var who = own.Count == 1 ? g.Planets[own[0]].Name : $"{own.Count - refused.Count} planètes";
        Hud.Toast(dest is int d && !(own.Count == 1 && own[0] == d) ? $"Ralliement de {who} : {g.Planets[d].Name}" : $"Ralliement de {who} retiré");
        Hud.Refresh();
    }

    /// <summary>Rally points, as dashed green routes with an arrowhead, and the selected fleet's way ahead.</summary>
    void DrawRoutes(DrawCanvas u)
    {
        var g = Galaxy;
        foreach (var p in g.Planets)
        {
            if (p.Owner != Player || p.Rally is not int rally) continue;
            DashedRoute(u, p.Id, rally, Palette.Ok with { A = Targets.Contains(p.Id) ? 0.9f : 0.4f });
        }
        // Picking a rally point: where it would lead.
        if (Rallying && hover is int to)
        {
            foreach (var id in OwnTargets()) if (id != to) DashedRoute(u, id, to, Palette.Ok with { A = 0.6f });
        }
        if (SelectedFleet is not int fid || !g.Fleets.TryGetValue(fid, out var f) || f.To is not int next) return;
        // Its way ahead, and the way it would take to the planet under the pointer, fainter.
        if (hover is int dest && dest != f.Path.LastOrDefault(next) && Galaxies.PlanRedirect(g, f, dest) is { } plan)
        {
            FleetRoute(u, f, plan.Path.Prepend(plan.Back ? f.From : next), Palette.Accent with { A = 0.4f });
        }
        FleetRoute(u, f, f.Path.Prepend(next), Palette.Accent with { A = 0.8f });
    }

    void FleetRoute(DrawCanvas u, Fleet f, IEnumerable<int> stops, Color c)
    {
        var (x, y) = FleetPoint(f);
        foreach (var id in stops)
        {
            var b = Galaxy.Planets[id];
            Draw.Line(u, Sx(x), Sy(y), Sx(b.X), Sy(b.Y), c, 3);
            (x, y) = (b.X, b.Y);
        }
    }

    void DashedRoute(DrawCanvas u, int from, int to, Color c)
    {
        var g = Galaxy;
        var path = Galaxies.FindPath(g, from, to);
        if (path is null) return;
        var prev = g.Planets[from];
        Vector2 At(Planet p) => new((float)Sx(p.X), (float)Sy(p.Y));
        foreach (var id in path)
        {
            u.DrawDashedLine(At(prev), At(g.Planets[id]), c, 2, 8);
            if (id != to) prev = g.Planets[id];
        }
        // An arrowhead short of the rally planet tells which way the ships go.
        var end = g.Planets[to];
        var dir = (At(end) - At(prev)).Normalized();
        var tip = At(end) - dir * (float)(Radius(end) + 5);
        var side = new Vector2(-dir.Y, dir.X) * 5;
        Draw.Triangle(u, tip, tip - dir * 10 + side, tip - dir * 10 - side, c);
    }

    /// <summary>Rings around the selected fleet on the move and the one under the pointer.</summary>
    void DrawFleetMarks(DrawCanvas o)
    {
        var g = Galaxy;
        if (hoverFleet is int h && h != SelectedFleet && g.Fleets.TryGetValue(h, out var hovered) && hovered.At is null)
        {
            var (x, y) = FleetPoint(hovered);
            Draw.Ring(o, Sx(x), Sy(y), 14, Palette.Accent with { A = 0.6f }, 1, 1);
        }
        if (SelectedFleet is int id && g.Fleets.TryGetValue(id, out var f) && f.At is null)
        {
            var (x, y) = FleetPoint(f);
            Draw.Ring(o, Sx(x), Sy(y), 14, Palette.Accent, 2, 1);
        }
    }
}
