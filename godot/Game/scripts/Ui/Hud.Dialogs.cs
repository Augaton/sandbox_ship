// The battle HUD's dialogs: pause menu, options, controls, confirmations and the end of a
// battle with each faction's record. One at a time, over a dimmed battlefield. Port of the
// dialogs of src/ui/hud.ts.
using Godot;
using SandboxShip.Core;
using SandboxShip.Sim;
using SandboxShip.Sim.Data;
using static SandboxShip.Ui.HudKit;

namespace SandboxShip.Ui;

public partial class Hud
{
    Control? modal;
    /// <summary>Run when the open dialog closes: resume the game, or go back to the pause menu.</summary>
    Action? onModalClose;
    /// <summary>The pause menu paused the game, and resumes it when it closes.</summary>
    bool menuPaused;
    /// <summary>The open dialog's scrolled body, kept as tall as the screen allows, less its buttons under it.</summary>
    ScrollContainer? modalScroll;
    Control? modalActions;

    /// <summary>Opens a dialog in place of the open one; `onClose` runs when it is closed.</summary>
    void OpenModal(Control content, Action? onClose = null)
    {
        // From one dialog to the next, the shade stays: only the panel changes.
        var shaded = modal is not null;
        DropModal();
        // The menus' look: their buttons fade as the main menu's do.
        var m = new Control { MouseFilter = Control.MouseFilterEnum.Stop, Theme = Menu.Look };
        m.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var shade = Ignore(new ColorRect { Color = Palette.Bg with { A = 0.7f } });
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        m.AddChild(shade);
        var center = Ignore(new CenterContainer());
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        center.AddChild(content);
        m.AddChild(center);
        root.AddChild(m);
        if (!shaded) FadeIn(shade);
        FadeIn(center);
        modal = m;
        // A dialog's body is scrolled, or (Dialog) scrolled with its buttons pinned under it.
        var first = content.GetChildCount() > 0 ? content.GetChild(0) : null;
        var pinned = first is not null && first.HasMeta(ActionsMeta);
        modalScroll = pinned ? first!.GetChild(0) as ScrollContainer : first as ScrollContainer;
        modalActions = pinned ? first!.GetChild<Control>(1) : null;
        onModalClose = onClose;
    }

    /// <summary>
    /// The dialog panel: a heading, then its parts 16 pixels apart, scrolled if the screen is too
    /// short; its buttons, when they come last, under the scroll, in view however long the parts.
    /// </summary>
    PanelContainer Dialog(string title, params Control[] parts)
    {
        var p = Panel(24, 24);
        var body = Column(16);
        body.AddChild(Text(title, size: 22, bold: true));
        var actions = parts.Length > 0 && parts[^1].HasMeta(ActionsMeta) ? parts[^1] : null;
        foreach (var c in parts) if (c != actions) body.AddChild(c);
        if (actions is null)
        {
            p.AddChild(Scroll(body));
            return p;
        }
        var frame = Column(16);
        frame.SetMeta(ActionsMeta, true);
        frame.AddChild(Scroll(body));
        frame.AddChild(actions);
        p.AddChild(frame);
        return p;
    }

    /// <summary>Marks a row of dialog buttons, and a dialog body that holds one under its scroll.</summary>
    const string ActionsMeta = "actions";

    /// <summary>A row of dialog buttons.</summary>
    static HBoxContainer Actions(params Button[] buttons)
    {
        var row = Row(8);
        row.SetMeta(ActionsMeta, true);
        foreach (var b in buttons) row.AddChild(b);
        return row;
    }

    /// <summary>Takes the open dialog away without running its `onClose`.</summary>
    void DropModal()
    {
        if (modal is null) return;
        // Hidden now, freed at the end of the frame: the button being pressed may be in it.
        modal.Visible = false;
        modal.QueueFree();
        modal = null;
        modalScroll = null;
        modalActions = null;
    }

    void CloseModal()
    {
        DropModal();
        var then = onModalClose;
        onModalClose = null;
        then?.Invoke();
    }

    /// <summary>Escape closes the open dialog; false when there was none.</summary>
    public bool Escape()
    {
        if (modal is null) return false;
        CloseModal();
        return true;
    }

    /// <summary>The game pauses behind a menu: resume, options, controls, restart or leave.</summary>
    public void PauseMenu()
    {
        if (scene.Opts.Net is not null)
        {
            NetMenu();
            return;
        }
        if (!scene.Paused)
        {
            scene.TogglePause();
            menuPaused = true;
        }
        var inCampaign = campaign is not null;
        var retreat = inCampaign && scene.Opts.Campaign?.Attacker == Player;
        var list = Column(8);
        // Wide enough for "Bataille en cours depuis 12 min 05 s." on one line.
        list.CustomMinimumSize = new Vector2(320, 0);
        void Item(string label, Action act, string tip = "", bool primary = false)
        {
            var b = Button(label, act, tip, 44);
            list.AddChild(primary ? Primary(b) : b);
        }
        Action back = PauseMenu;
        Item("Reprendre", CloseModal, primary: true);
        Item("Options", () => OpenModal(Options.Panel(CloseModal), back));
        Item("Commandes", () => ShowHelp(back));
        if (!inCampaign)
        {
            Item("Recommencer", () => Confirm("Recommencer ?", "La bataille repart du début, sur la même carte.", "Recommencer", () => scene.Restart(), back));
            Item("Menu principal", () => Confirm("Quitter la partie ?", "La bataille en cours sera perdue.", "Quitter", scene.ToMenu, back));
        }
        if (retreat) Item("Retraite", scene.Retreat);
        if (inCampaign) Item("Laisser l'IA finir", scene.Delegate, "La bataille continue seule ; tu retrouves la carte galactique");
        OpenModal(Dialog("Pause", Para($"Bataille en cours depuis {Names.Duration(World.Time)}.", Palette.Muted), list), () =>
        {
            if (!menuPaused) return;
            menuPaused = false;
            if (scene.Paused) scene.TogglePause();
        });
    }

    /// <summary>Each faction's record: built, lost, destroyed, damage dealt, credits earned.</summary>
    Control StatsTable()
    {
        var box = Column(16);
        box.AddChild(Para($"Durée : {Names.Duration(World.Time)}", Palette.Muted));
        // The neutral guardians of a raid are not a player.
        // What a side spent comes from the battle's history, its last sample.
        var history = World.History is { Times.Count: > 0 } h ? h : null;
        var rows = World.Factions.Values.Where(f => f.Race != Faction.Errant).Select(f =>
        {
            var s = f.Stats;
            var color = f.Id == Player ? Palette.Accent : Palette.Text;
            var values = new[] { s.Built, s.Lost, s.Kills, Js.Round(s.Damage), Js.Round(s.Earned) }.Select(v => N(v));
            if (history is not null) values = values.Append(history.Sides.TryGetValue(f.Id, out var points) ? N(Js.Round(points[^1].Spent)) : "-");
            return (Who(f.Id, color), color, values);
        });
        string[] heads = ["Faction", "Construits", "Perdus", "Détruits", "Dégâts", "Crédits gagnés"];
        box.AddChild(Table(history is not null ? [.. heads, "Dépensés"] : heads, rows));
        return box;
    }

    /// <summary>A side as a table names it: its emblem, then its race in `color`.</summary>
    Control Who(Side side, Color color)
    {
        var who = Row(8);
        who.AddChild(Emblem(side));
        who.AddChild(Text(Factions.Defs[Worlds.RaceOf(World, side)].Name, color, 14));
        return who;
    }

    /// <summary>A table of the end screens: a heading row, then a line per row, its first cell as given and its numbers to the right, in its color.</summary>
    static GridContainer Table(string[] heads, IEnumerable<(Control First, Color Color, IEnumerable<string> Values)> rows)
    {
        var grid = Ignore(new GridContainer { Columns = heads.Length });
        grid.AddThemeConstantOverride("h_separation", 0);
        grid.AddThemeConstantOverride("v_separation", 0);
        var line = new StyleBoxFlat { BgColor = Colors.Transparent, BorderColor = Palette.Line, BorderWidthBottom = 1 };
        line.ContentMarginLeft = line.ContentMarginRight = 10;
        line.ContentMarginTop = line.ContentMarginBottom = 4;
        void Cell(Control content)
        {
            var c = Ignore(new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
            c.AddThemeStyleboxOverride("panel", line);
            c.AddChild(content);
            grid.AddChild(c);
        }
        static Label Number(string text, Color color)
        {
            var l = Text(text, color, 14);
            l.HorizontalAlignment = HorizontalAlignment.Right;
            return l;
        }
        for (var i = 0; i < heads.Length; i++) Cell(i == 0 ? Text(heads[i], Palette.Muted, 14) : Number(heads[i], Palette.Muted));
        foreach (var (first, color, values) in rows)
        {
            Cell(first);
            foreach (var v in values) Cell(Number(v, color));
        }
        return grid;
    }

    /// <summary>The end screen's title: a survival game comes to its end, there is no winning it.</summary>
    string EndTitle(bool won) => World.Mode.Kind == BattleMode.Survival ? "Fin de la survie" : won ? "Victoire" : "Défaite";

    /// <summary>The end screen's table: a survival game's scoreboard, else each faction's record; the battle over time under it.</summary>
    Control EndTable() => WithCharts(World.Mode.Kind == BattleMode.Survival ? SurvivalEnd() : StatsTable());

    public void ShowEnd(bool won)
    {
        // A battle played here takes its last sample now; a player's comes from the host (Message.Record).
        if (scene.Opts.Net is null) World.History?.Close(World);
        PlayerProfile.BattleEnded(scene, won);
        Music.Sting(won);
        if (scene.Opts.Net is not null)
        {
            NetEnd(won);
            return;
        }
        var again = Primary(Button("Rejouer la même carte", () => scene.Restart()));
        var reroll = Button("Nouvelle carte", () => scene.Restart(true));
        var menu = Button("Menu", scene.ToMenu);
        var close = Button("Continuer à regarder", CloseModal);
        OpenModal(Dialog(EndTitle(won), Para(EndText(won), max: 672), EndTable(), Actions(again, reroll, menu, close)));
    }

    /// <summary>End of a campaign battle: the only way on is back to the galaxy map. `watched`: who won, for one who only watched it.</summary>
    public void ShowBattleEnd(bool won, Action onBack, string? watched = null)
    {
        if (watched is null) PlayerProfile.BattleEnded(scene, won);
        if (scene.Opts.Net is null) World.History?.Close(World);
        var back = Primary(Button("Retour à la carte", onBack));
        OpenModal(Dialog(watched is not null ? "Bataille terminée" : won ? "Victoire" : "Défaite",
            Para($"{watched ?? (won ? "La bataille est gagnée." : "La bataille est perdue.")} Les survivants regagnent la carte galactique.", max: 672),
            WithCharts(StatsTable()), Actions(back)));
    }

    /// <summary>Asks before an action that loses progress; `back` runs if the player changes their mind.</summary>
    void Confirm(string title, string text, string action, Action onYes, Action? back = null)
    {
        var no = Button("Annuler", CloseModal);
        var yes = Primary(Button(action, () =>
        {
            onModalClose = null;
            CloseModal();
            onYes();
        }));
        OpenModal(Dialog(title, Para(text), Actions(no, yes)), back);
    }

    void ShowOptions() => OpenModal(Options.Panel(CloseModal));

    static readonly string[] HelpNotes =
    [
        "[b]Capacités.[/b] Chaque classe de vaisseau et chaque héros en a une. Le panneau de sélection dit ce qu'elle fait et si elle est prête, active ou en recharge. " +
        "E (ou son bouton) la déclenche ; certaines se visent d'un clic. Case « Automatique » : tes vaisseaux s'en servent seuls quand c'est utile. Sur le champ de bataille, un anneau coloré montre une capacité active, et son nom s'affiche quand elle part.",
        "[b]Construction.[/b] Autour de la station et des avant-postes. Un avant-poste se pose loin de la base, là où un de tes vaisseaux tient la position, hors combat ; il ouvre une nouvelle zone de construction. " +
        "Plateformes et docks se construisent aussi près de tes mines. Sélectionne une plateforme ou un avant-poste pour l'améliorer.",
        "Détruire les points d'attache d'une station la désarme : chantier naval, hangar, générateur de bouclier, tourelles. Le brouillard de guerre cache ce que tes unités ne voient pas, l'IA comprise. " +
        "Les héros et le Titan sont uniques ; un héros détruit se reconstruit après un délai.",
    ];

    void ShowHelp(Action? back = null)
    {
        var parts = new List<Control> { Controls.List(Controls.Battle) };
        foreach (var note in HelpNotes)
        {
            var p = new RichTextLabel { BbcodeEnabled = true, Text = note, FitContent = true, ScrollActive = false, AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore };
            p.AddThemeColorOverride("default_color", Palette.Muted);
            p.AddThemeFontOverride("bold_font", Bold);
            parts.Add(p);
        }
        parts.Add(Actions(Primary(Button("Fermer", CloseModal))));
        OpenModal(Dialog("Commandes", [.. parts]), back);
    }
}
