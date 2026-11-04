// The guided first battle: a small fleet to start with, a foe on the far side that does nothing but
// defend its station, and a card at the top left that teaches one thing at a time. Each step is
// done by doing it, seen in the commands the player sends or in the battle; each can be passed,
// and the tutorial left at any moment. The keys it names come from the list of controls (Controls.cs).
using Godot;
using SandboxShip.Battle;
using SandboxShip.Core;
using SandboxShip.Sim;
using SandboxShip.Sim.Data;

namespace SandboxShip.Ui;

public partial class Tutorial : CanvasLayer
{
    /// <summary>Credits to start with: enough for a mine, a frigate and a few more.</summary>
    public const double Credits = 5000;

    /// <summary>The tutorial's battle: Kla'ed against Nairan on the small mirrored map, no fog, no AI on the other side.</summary>
    public static BattleOptions Options() => new()
    {
        Player = Faction.Klaed, Slots = [new Slot(Faction.Klaed, Faction.Klaed), new Slot(Faction.Nairan, Faction.Nairan)],
        Map = MapKind.Classic, Size = MapSize.Small, Fog = false, Raid = false, Seed = 1, Tutorial = true,
    };

    /// <summary>What a step asks, and how the tutorial knows it is done.</summary>
    sealed record Step(string Title, string Text, Func<bool> Done);

    readonly BattleScreen scene;
    readonly List<Step> steps = [];
    /// <summary>What the player has done, from the commands that went through: move, attack, mine, frigate, ability.</summary>
    readonly HashSet<string> did = [];
    int current;
    Vector2 startCenter;
    float startZoom;
    PanelContainer card = null!;
    Label count = null!, title = null!, text = null!;
    Button pass = null!;

    public Tutorial(BattleScreen scene) => this.scene = scene;

    /// <summary>The keys of an action, as the list of controls has them; that list is the one place they are written.</summary>
    static string Key(string action) =>
        Controls.Battle.FirstOrDefault(c => c.Action.StartsWith(action, StringComparison.Ordinal)).Keys ?? "(voir « Commandes »)";

    public override void _Ready()
    {
        Layer = 2;
        startCenter = scene.Center;
        startZoom = scene.Zoom;
        Fleet();
        steps.AddRange(
        [
            new("La vue", $"Déplace la vue ({Key("Déplacer la vue")}) et zoome ({Key("Zoom")}).",
                () => scene.Center.DistanceTo(startCenter) > 300 && Math.Abs(scene.Zoom - startZoom) > 0.1f),
            new("Sélectionner", $"{Key("Sélectionner")} sur tes vaisseaux, près de ta station : glisser trace un rectangle autour de plusieurs.",
                () => scene.Selected.Count > 0),
            new("Déplacer", $"{Key("Déplacer, ou attaquer")} sur un point libre : les vaisseaux sélectionnés y vont, en formation.",
                () => did.Contains("move")),
            new("Une mine", "Les mines rapportent des crédits. Onglet « Bâtiments » en bas, choisis la mine, puis clique sur un champ d'astéroïdes près de ta station.",
                () => did.Contains("mine")),
            new("Produire", "Onglet « Vaisseaux » : commande une frégate. Elle sort du chantier de ta station dès qu'elle est prête.",
                () => did.Contains("frigate")),
            new("Attaquer", $"Vaisseaux sélectionnés, {Key("Déplacer, ou attaquer")} sur un ennemi l'attaque ; {Key("Attaque-mouvement")} engage tout ce qui croise la route.",
                () => did.Contains("attack")),
            new("Postures et ordres", $"{Key("Posture suivante")} change la posture des vaisseaux sélectionnés : agressive, défensive, tenir la position ou passive. "
                + $"{Key("Patrouille")} les fait patrouiller, {Key("Garder")} garde un allié, {Key("Ordre en file")} met un ordre à la suite des autres. "
                + "Le panneau de sélection a les mêmes boutons.",
                () => did.Contains("orders")),
            new("Une capacité", $"Chaque vaisseau a une capacité : sélectionne une frégate et appuie sur {Key("Capacité")}. Le panneau de sélection dit ce qu'elle fait.",
                () => did.Contains("ability")),
            new("Un groupe", $"{Key("Créer un groupe")} garde la sélection sous un chiffre ; {Key("Rappeler un groupe")} la rappelle.",
                () => scene.Controls.GroupSizes().Count > 0),
            new("La victoire", "Détruis la station ennemie, à l'autre bout de la carte : ses tourelles d'abord, puis sa coque. Produis des renforts au besoin.",
                () => Worlds.Won(scene.World, scene.Opts.Player)),
        ]);

        var root = new Control { Theme = UiTheme.Get(), MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);
        card = HudKit.Panel(16, 12, Palette.Accent with { A = 0.6f });
        card.Position = new Vector2(8, 64);
        card.CustomMinimumSize = new Vector2(400, 0);
        root.AddChild(card);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        card.AddChild(column);
        count = HudKit.Text("", Palette.Muted, 13);
        column.AddChild(count);
        title = HudKit.Text("", Palette.Accent, 17, bold: true);
        column.AddChild(title);
        text = HudKit.Text("", wrap: true);
        text.CustomMinimumSize = new Vector2(368, 0);
        column.AddChild(text);
        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        column.AddChild(buttons);
        pass = HudKit.Button("Passer", Next, "Passer cette étape", 34);
        buttons.AddChild(pass);
        buttons.AddChild(HudKit.Button("Quitter le didacticiel", scene.ToMenu, "Retour au menu", 34));
        ShowStep();
    }

    /// <summary>Where the card ends, down the screen: the battle's toasts come under it.</summary>
    public float Bottom => card.Position.Y + card.Size.Y;

    /// <summary>A few ships in front of the station, between it and the middle of the map.</summary>
    void Fleet()
    {
        var w = scene.World;
        var side = scene.Opts.Player;
        if (Worlds.StationOf(w, side) is not { } station) return;
        var toward = Math.Atan2(w.Height / 2 - station.Y, w.Width / 2 - station.X);
        (ShipClass Cls, double Ahead, double Across)[] fleet =
        [
            (ShipClass.Battlecruiser, 320, 0), (ShipClass.Frigate, 260, -120), (ShipClass.Frigate, 260, 120),
            (ShipClass.Frigate, 400, -120), (ShipClass.Frigate, 400, 120), (ShipClass.Scout, 470, 0),
        ];
        foreach (var (cls, ahead, across) in fleet)
        {
            var x = station.X + Math.Cos(toward) * ahead - Math.Sin(toward) * across;
            var y = station.Y + Math.Sin(toward) * ahead + Math.Cos(toward) * across;
            Worlds.SpawnShip(w, side, cls, x, y, toward);
        }
    }

    /// <summary>A command the player sent and the simulation took: the steps it may complete.</summary>
    public void Saw(Command c, Result r)
    {
        if (!r.Ok) return;
        switch (c)
        {
            case Command.SetStance or Command.Patrol or Command.Guard or Command.Move { Queued: true } or Command.Attack { Queued: true }: did.Add("orders"); break;
            case Command.Move m: did.Add(m.AttackMove ? "attack" : "move"); break;
            case Command.Attack: did.Add("attack"); break;
            case Command.PlaceStructure { Type: BuildableType.Mine }: did.Add("mine"); break;
            case Command.EnqueueShip { Cls: ShipClass.Frigate }: did.Add("frigate"); break;
            case Command.UseAbility: did.Add("ability"); break;
        }
    }

    public override void _Process(double delta)
    {
        if (current < steps.Count && steps[current].Done()) Next();
    }

    void Next()
    {
        if (current >= steps.Count) return;
        current++;
        Sfx.Play(Sound.Built, 0, 0.5);
        ShowStep();
    }

    void ShowStep()
    {
        if (current >= steps.Count)
        {
            count.Text = "Didacticiel terminé";
            PlayerProfile.Moment(Profiles.Moment.Tutorial);
            title.Text = "Bravo";
            text.Text = "Tu connais l'essentiel. Toutes les commandes sont dans le menu, « Commandes » ; la suite se joue en escarmouche ou en campagne.";
            pass.Visible = false;
        }
        else
        {
            count.Text = $"Didacticiel, étape {current + 1} sur {steps.Count}";
            title.Text = steps[current].Title;
            text.Text = steps[current].Text;
        }
        HudKit.FadeIn(card, 0.2);
    }
}
