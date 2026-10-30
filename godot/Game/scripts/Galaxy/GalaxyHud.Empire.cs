// What the web campaign does not have: a drawer on the left with the player's fleets, an
// overview of the empire (income, production, research, idle planets) and a log of what
// happened; the panel of a fleet on the move; the rally point of a planet.
using Godot;
using SandboxShip.Core;
using SandboxShip.Sim;
using SandboxShip.Sim.Data;
using SandboxShip.Ui;

namespace SandboxShip.Campaign;

public partial class GalaxyHud
{
    public enum Drawer { None, Fleets, Empire, Log, Diplomacy }

    const float DrawerW = 340;
    /// <summary>Entries the log keeps.</summary>
    const int LogSize = 30;

    static readonly (Drawer Id, string Label, string Title, string Key)[] Drawers =
    [
        (Drawer.Fleets, "Flottes", "Tes flottes", "F"), (Drawer.Empire, "Empire", "Vue d'ensemble", "E"), (Drawer.Log, "Journal", "Journal", "J"),
        (Drawer.Diplomacy, "Diplomatie", "Diplomatie", "T"),
    ];

    Drawer drawer;
    PanelContainer drawerPanel = null!;
    Label drawerTitle = null!;
    VBoxContainer drawerBody = null!;
    ScrollContainer drawerScroll = null!;
    readonly List<(Button B, Drawer Id)> drawerButtons = [];
    /// <summary>Newest first: galaxy time, text, the planet it is about, and a number that keys its entry.</summary>
    readonly List<(double Time, string Text, int? Planet, int N)> log = [];
    int logged;
    /// <summary>Parts of the panel updated in place: arrival of a fleet on the move, rally point of the planet.</summary>
    Label? eta, rallyLabel;
    Button? rallySet, rallyClear;

    /// <summary>Right edge of the drawer while it is open, 0 when closed: the map centers what it shows beside it.</summary>
    public float DrawerRight => drawer == Drawer.None ? 0 : 8 + DrawerW;

    /// <summary>The top bar's buttons that open the drawer.</summary>
    HBoxContainer DrawerButtons()
    {
        var row = HudKit.Row(4);
        foreach (var (id, label, title, key) in Drawers)
        {
            var b = Btn(label, () => ToggleDrawer(id));
            b.TooltipText = $"{title} ({key})";
            drawerButtons.Add((b, id));
            row.AddChild(b);
        }
        return row;
    }

    void BuildDrawer()
    {
        drawerPanel = HudKit.Panel(12, 10);
        drawerPanel.Position = new Vector2(8, 64);
        drawerPanel.CustomMinimumSize = new Vector2(DrawerW, 0);
        drawerPanel.Visible = false;
        root.AddChild(drawerPanel);
        var col = HudKit.Column(8);
        drawerPanel.AddChild(col);
        var head = HudKit.Row(8);
        drawerTitle = Text("", size: 18, bold: true);
        drawerTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        head.AddChild(drawerTitle);
        var close = HudKit.Button("×", () => ToggleDrawer(Drawer.None), "Fermer (Échap)", height: 32);
        close.CustomMinimumSize = new Vector2(32, 32);
        head.AddChild(close);
        col.AddChild(head);
        drawerBody = HudKit.Column(6);
        drawerScroll = HudKit.Scroll(drawerBody);
        col.AddChild(drawerScroll);
    }

    /// <summary>Opens the drawer on a list, or closes it if that list is open already.</summary>
    public void ToggleDrawer(Drawer id)
    {
        drawer = drawer == id ? Drawer.None : id;
        drawerPanel.Visible = drawer != Drawer.None;
        drawerTitle.Text = Drawers.FirstOrDefault(d => d.Id == drawer).Title ?? "";
        foreach (var c in drawerBody.GetChildren())
        {
            drawerBody.RemoveChild(c);
            c.QueueFree();
        }
        foreach (var (b, d) in drawerButtons) HudKit.Outline(b, d == drawer ? Palette.Accent : null);
        scene.ApplyView();
        RefreshDrawer();
    }

    /// <summary>Closes the drawer; false if it was closed already.</summary>
    public bool CloseDrawer()
    {
        if (drawer == Drawer.None) return false;
        ToggleDrawer(Drawer.None);
        return true;
    }

    void RefreshDrawer()
    {
        if (drawer == Drawer.None) return;
        HudKit.Sync(drawerBody, drawer switch { Drawer.Fleets => FleetParts(), Drawer.Empire => EmpireParts(), Drawer.Diplomacy => DiplomacyParts(), _ => LogParts() });
        // Its title, margins and gaps take about 70 pixels.
        HudKit.Fit(drawerScroll, root.Size.Y - 64 - 8 - 70);
        drawerPanel.ResetSize();
    }

    /// <summary>A line of the drawer: a button with its text, then up to `lines - 1` smaller lines; a click acts.</summary>
    static LabelButton Entry(int lines, Action act)
    {
        var b = new LabelButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        b.Add("", Palette.Text, 14, wrap: true);
        for (var i = 1; i < lines; i++) b.Add("", Palette.Muted, 13, wrap: true);
        HudKit.OnPress(b, act);
        return b;
    }

    /// <summary>An entry's lines, empty ones hidden; a chosen entry is outlined, its first line amber.</summary>
    static void SetEntry(LabelButton b, bool chosen, params (string Text, Color Color)[] lines)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            var l = b.Line(i);
            if (l.Text != lines[i].Text) l.Text = lines[i].Text;
            l.Visible = lines[i].Text.Length > 0;
            b.SetColor(i, lines[i].Color);
        }
        b.Marked = chosen;
        b.Paint();
        HudKit.Outline(b, chosen ? Palette.Accent : null, false);
    }

    static string ShipCount(int n) => $"{n} vaisseau{(n > 1 ? "x" : "")}";

    /// <summary>Where a fleet of the player's is, then its size, condition and arrival.</summary>
    (string Where, string What) FleetLines(Fleet f)
    {
        var g = G;
        string Name(int id) => g.Planets[id].Name;
        var what = $"{ShipCount(Sim.Battle.CountShips(f.Ships) + f.Heroes.Count)}{Condition(f)}";
        if (f.At is int at) return (f.BattleId is not null ? $"Combat à {Name(at)}" : $"À {Name(at)}", what);
        if (f.BattleId is not null) return ($"Interception entre {Name(f.From)} et {Name(f.To!.Value)}", what);
        var end = f.Path.Count > 0 ? f.Path[^1] : f.To!.Value;
        return ($"{Name(f.From)} → {Name(end)}", $"{what}, arrivée dans {N(Math.Ceiling(Galaxies.TravelTime(g, f)))} s");
    }

    List<Part> FleetParts()
    {
        var own = G.Fleets.Values.Where(f => f.Side == Player).ToList();
        if (own.Count == 0) return [HudKit.TextPart("Aucune flotte.", Palette.Muted)];
        var parts = new List<Part> { HudKit.TextPart("Clic : sélectionner la flotte et centrer la vue sur elle.", Palette.Muted, 13) };
        foreach (var f in own)
        {
            var id = f.Id;
            var (where, what) = FleetLines(f);
            var chosen = scene.SelectedFleet == id || (f.At is int at && scene.Selected == at);
            parts.Add(new Part($"fleet:{id}", () => Entry(2, () => scene.ShowFleet(id)), n => SetEntry((LabelButton)n, chosen, (where, Palette.Text), (what, Palette.Muted))));
        }
        return parts;
    }

    /// <summary>A planet of the player's with a station and nothing to build, nor any loop waiting for credits.</summary>
    bool Idle(Planet p) => p.Station > 0 && p.Units.Count == 0 && p.Buildings.Count == 0 && p.Repeat.Count == 0 && !Galaxies.BattleAt(G, p.Id);

    /// <summary>The element under way in a queue, how far it is, and how many wait behind it.</summary>
    static string QueueHead(Planet p, List<GalaxyQueueItem> queue)
    {
        var q = queue[0];
        // Named as they were ordered: "Station", not "Améliorer la station".
        var name = q.Kind == GalaxyBuildKind.Ship ? Ships.OrderName(q.Cls) : Galaxies.BuildName(new Planet { PlatformTier = p.PlatformTier }, q.Build);
        return $"{name} {HudKit.Percent(1 - q.Remaining / q.Total)}{(queue.Count > 1 ? $" (+{queue.Count - 1})" : "")}";
    }

    List<Part> EmpireParts()
    {
        var g = G;
        var player = Player;
        var f = g.Factions[player];
        var own = g.Planets.Where(p => p.Owner == player).ToList();
        var parts = new List<Part>
        {
            HudKit.SpansPart(UiTheme.FontSize, ("Revenu total", Palette.Text, false), ($"+{N(Js.Round(Galaxies.IncomeOf(g, player)))}/s", Palette.Accent, true),
                ($"{own.Count} planète{(own.Count > 1 ? "s" : "")}", Palette.Muted, false)),
        };
        if (f.Researching is { } job)
        {
            var waiting = f.ResearchQueue.Count;
            parts.Add(HudKit.TextPart($"Recherche : {Techs.Defs[job.Tech].Name} niveau {f.Research[job.Tech] + 1}, {HudKit.Percent(1 - job.Remaining / job.Total)}"
                + (waiting > 0 ? $" ({waiting} en file)" : "")));
        }
        else parts.Add(own.Exists(p => p.Lab > 0) ? HudKit.TextPart("Recherche à l'arrêt", Palette.Accent) : HudKit.TextPart("Recherche : laboratoire requis", Palette.Muted));
        var idle = own.Where(Idle).Select(p => p.Name).ToList();
        if (idle.Count > 0) parts.Add(HudKit.TextPart($"Inactive{(idle.Count > 1 ? "s" : "")} : {string.Join(", ", idle)}", Palette.Accent));
        parts.Add(HudKit.TextPart("Clic : sélectionner la planète et centrer la vue sur elle.", Palette.Muted, 13));
        foreach (var p in own)
        {
            var id = p.Id;
            var head = $"{p.Name}  +{N(Math.Round(Galaxies.PlanetIncome(g, p), 1))}/s{(p.Rally is int r ? $"  → {g.Planets[r].Name}" : "")}";
            var units = p.Units.Count > 0 ? $"Vaisseaux : {QueueHead(p, p.Units)}" : p.Repeat.Count > 0 ? "Vaisseaux : boucle en attente" : "";
            var buildings = p.Buildings.Count > 0 ? $"Bâtiments : {QueueHead(p, p.Buildings)}" : "";
            var (note, color) = Galaxies.BattleAt(g, id) ? ("Combat en cours", Palette.Danger)
                : Idle(p) ? ("Inactive : rien en production", Palette.Accent)
                : p.Station == 0 && units.Length + buildings.Length == 0 ? ("Pas de station", Palette.Muted)
                : ("", Palette.Muted);
            var chosen = scene.Targets.Contains(id);
            parts.Add(new Part($"planet:{id}", () => Entry(4, () => scene.ShowPlanet(id)),
                n => SetEntry((LabelButton)n, chosen, (head, Palette.Text), (note, color), (units, Palette.Muted), (buildings, Palette.Muted))));
        }
        return parts;
    }

    /// <summary>Galaxy time as minutes and seconds.</summary>
    static string Clock(double t) => $"{(int)(t / 60)}:{(int)(t % 60):00}";

    List<Part> LogParts()
    {
        if (log.Count == 0) return [HudKit.TextPart("Rien à signaler pour l'instant.", Palette.Muted)];
        var parts = new List<Part> { HudKit.TextPart("Clic : centrer la vue sur la planète.", Palette.Muted, 13) };
        foreach (var (time, text, planet, n) in log)
        {
            var where = planet is int p ? $"{Clock(time)}, {G.Planets[p].Name}" : Clock(time);
            parts.Add(new Part($"log:{n}", () => Entry(2, () =>
            {
                if (planet is int id) scene.ShowPlanet(id);
            }), b => SetEntry((LabelButton)b, false, (text, Palette.Text), (where, Palette.Muted))));
        }
        return parts;
    }

    /// <summary>A message worth keeping: shown, and written in the log with the galaxy time.</summary>
    void Notify(string text, int? planet = null)
    {
        Toast(text);
        log.Insert(0, (G.Time, text, planet, ++logged));
        if (log.Count > LogSize) log.RemoveAt(log.Count - 1);
    }

    /// <summary>The player's fleet on the move shown in the panel, if any.</summary>
    Fleet? Transit() => scene.SelectedFleet is int id && G.Fleets.TryGetValue(id, out var f) && f.At is null ? f : null;

    static string TransitSignature(Fleet? f) =>
        f is null ? "" : $"{f.Id}:{f.From}:{f.To}:{string.Join(",", f.Path)}:{f.BattleId}:{DescribeShips(f.Ships, f.Heroes)}";

    /// <summary>A fleet of the player's on the move: its ships, its route and when it arrives, and the order to stop.</summary>
    void RenderTransit(Fleet f)
    {
        var g = G;
        condition = Text("", Palette.Muted, 12);
        body.AddChild(H3("Vaisseaux", condition));
        body.AddChild(Text(DescribeShips(f.Ships, f.Heroes), wrap: true));
        body.AddChild(H3("Route"));
        body.AddChild(Text(string.Join(" → ", new[] { f.From, f.To!.Value }.Concat(f.Path).Select(id => g.Planets[id].Name)), wrap: true));
        eta = Text("", wrap: true);
        body.AddChild(eta);
        if (f.BattleId is not null)
        {
            body.AddChild(Text("En combat : aucun ordre possible", Palette.Danger, bold: true, wrap: true));
            return;
        }
        var stop = Btn($"S'arrêter à {g.Planets[f.To.Value].Name}", scene.StopFleet);
        stop.TooltipText = f.Path.Count > 0 ? "La flotte s'arrête à la prochaine planète de sa route (X)" : "La flotte s'arrête déjà là";
        stop.Disabled = f.Path.Count == 0;
        body.AddChild(stop);
        body.AddChild(Text("Clic droit sur une planète : changer de destination ; la flotte fait demi-tour si c'est plus court.", Palette.Muted, wrap: true));
    }

    /// <summary>The rally point of the planet shown: the ships built there leave for it on their own.</summary>
    void RenderRally()
    {
        var row = HudKit.Row(6);
        rallyLabel = Text("", wrap: true);
        row.AddChild(rallyLabel);
        rallySet = Btn("Rallier", scene.BeginRally);
        rallySet.CustomMinimumSize = new Vector2(0, 32);
        rallySet.TooltipText = "Les vaisseaux construits ici partent seuls vers une planète : clic ici puis sur elle, ou Ctrl + clic droit sur elle. "
            + "Vaut pour toutes les planètes sélectionnées.";
        row.AddChild(rallySet);
        rallyClear = Btn("×", scene.ClearRally);
        rallyClear.CustomMinimumSize = new Vector2(32, 32);
        rallyClear.TooltipText = "Retirer le point de ralliement";
        row.AddChild(rallyClear);
        body.AddChild(row);
    }

    /// <summary>What the panel and the drawer show that changes all the time.</summary>
    void UpdateExtras(Planet? p)
    {
        var g = G;
        if (Transit() is { } f)
        {
            if (condition is not null && IsInstanceValid(condition))
            {
                condition.Text = (f.Condition >= 1 ? "intacte" : $"état {HudKit.Percent(f.Condition)}") + Supply(f);
                HudKit.Paint(condition, f.Condition < 0.5 ? Palette.Danger : Palette.Muted);
            }
            if (eta is not null && IsInstanceValid(eta))
            {
                var end = g.Planets[f.Path.Count > 0 ? f.Path[^1] : f.To!.Value].Name;
                eta.Text = f.BattleId is not null ? "Arrêtée par le combat" : $"Arrivée à {end} dans {N(Math.Ceiling(Galaxies.TravelTime(g, f)))} s";
            }
        }
        if (p is not null && rallyLabel is not null && IsInstanceValid(rallyLabel))
        {
            rallyLabel.Text = p.Rally is int r ? $"Ralliement : {g.Planets[r].Name}" : "Pas de ralliement";
            rallyClear!.Disabled = p.Rally is null;
            HudKit.Outline(rallySet!, scene.Rallying ? Palette.Ok : null);
        }
        RefreshDrawer();
    }
}
