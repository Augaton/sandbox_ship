// The guided first battle: a small fleet to start with, a foe on the far side that does nothing but
// defend its station, and a card at the top left that teaches one thing at a time (Guide.cs). Each
// step is done by doing it, seen in the commands the player sends or in the battle. The keys it
// names come from the list of controls (Controls.cs).
using SandboxShip.Battle;
using SandboxShip.Core;
using SandboxShip.Sim;
using SandboxShip.Sim.Data;

namespace SandboxShip.Ui;

public partial class Tutorial : Guide
{
    /// <summary>Credits to start with: enough for a mine, a frigate and a few more.</summary>
    public const double Credits = 5000;

    /// <summary>The tutorial's battle: Kla'ed against Nairan on the small mirrored map, no fog, no AI on the other side.</summary>
    public static BattleOptions Options() => new()
    {
        Player = Faction.Klaed, Slots = [new Slot(Faction.Klaed, Faction.Klaed), new Slot(Faction.Nairan, Faction.Nairan)],
        Map = MapKind.Classic, Size = MapSize.Small, Fog = false, Raid = false, Seed = 1, Tutorial = true,
    };

    readonly BattleScreen scene;
    Godot.Vector2 startCenter;
    float startZoom;

    public Tutorial(BattleScreen scene) => this.scene = scene;

    /// <summary>The keys of an action, as the list of controls has them; that list is the one place they are written.</summary>
    static string Key(string action) =>
        Controls.Battle.FirstOrDefault(c => c.Action.StartsWith(action, StringComparison.Ordinal)).Keys ?? "(voir « Commandes »)";

    protected override string Ending =>
        "Tu connais l'essentiel. Toutes les commandes sont dans le menu, « Commandes » ; la suite se joue en escarmouche ou en campagne.";

    protected override void Leave() => scene.ToMenu();

    protected override void Finished() => PlayerProfile.Moment(Profiles.Moment.Tutorial);

    public override void _Ready()
    {
        startCenter = scene.Center;
        startZoom = scene.Zoom;
        Fleet();
        Steps.AddRange(
        [
            new("La vue", $"Déplace la vue ({Key("Déplacer la vue")}) et zoome ({Key("Zoom")}).",
                () => scene.Center.DistanceTo(startCenter) > 300 && Math.Abs(scene.Zoom - startZoom) > 0.1f),
            new("Sélectionner", $"{Key("Sélectionner")} sur tes vaisseaux, près de ta station : glisser trace un rectangle autour de plusieurs.",
                () => scene.Selected.Count > 0),
            new("Déplacer", $"{Key("Déplacer, ou attaquer")} sur un point libre : les vaisseaux sélectionnés y vont, en formation.",
                () => Did.Contains("move")),
            new("Une mine", "Les mines rapportent des crédits. Onglet « Bâtiments » en bas, choisis la mine, puis clique sur un champ d'astéroïdes près de ta station.",
                () => Did.Contains("mine")),
            new("Produire", "Onglet « Vaisseaux » : commande une frégate. Une fois prête, elle attend en réserve, dans le panneau au-dessus des groupes.",
                () => Did.Contains("frigate")),
            new("La réserve", "Glisse la frégate de la réserve sur la carte, près de ta station ou de tes vaisseaux : elle arrive par l'hyperespace.",
                () => Did.Contains("deploy")),
            new("Attaquer", $"Vaisseaux sélectionnés, {Key("Déplacer, ou attaquer")} sur un ennemi l'attaque ; {Key("Attaque-mouvement")} engage tout ce qui croise la route.",
                () => Did.Contains("attack")),
            new("Postures et ordres", $"{Key("Posture suivante")} change la posture des vaisseaux sélectionnés : agressive, défensive, tenir la position ou passive. "
                + $"{Key("Patrouille")} les fait patrouiller, {Key("Garder")} garde un allié, {Key("Ordre en file")} met un ordre à la suite des autres. "
                + "Le panneau de sélection a les mêmes boutons.",
                () => Did.Contains("orders")),
            new("Une capacité", $"Chaque vaisseau a une capacité : sélectionne une frégate et appuie sur {Key("Capacité")}. Le panneau de sélection dit ce qu'elle fait.",
                () => Did.Contains("ability")),
            new("Un groupe", $"{Key("Créer un groupe")} garde la sélection sous un chiffre ; {Key("Rappeler un groupe")} la rappelle.",
                () => scene.Controls.GroupSizes().Count > 0),
            new("La victoire", "Détruis la station ennemie, à l'autre bout de la carte : ses tourelles d'abord, puis sa coque. Produis des renforts au besoin.",
                () => Worlds.Won(scene.World, scene.Opts.Player)),
        ]);
        BuildCard();
    }

    /// <summary>A few ships in front of the station, between it and the middle of the map.</summary>
    void Fleet()
    {
        var w = scene.World;
        var side = scene.Opts.Player;
        if (Worlds.StationOf(w, side) is not { } station) return;
        // Without the platforms every station starts with: its few ships are to win this battle.
        foreach (var p in w.Entities.Where(e => e.Type == "platform").ToList()) w.Entities.Remove(p.Id);
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
            case Command.SetStance or Command.Patrol or Command.Guard or Command.Move { Queued: true } or Command.Attack { Queued: true }: Did.Add("orders"); break;
            case Command.Move m: Did.Add(m.AttackMove ? "attack" : "move"); break;
            case Command.Attack: Did.Add("attack"); break;
            case Command.PlaceStructure { Type: BuildableType.Mine }: Did.Add("mine"); break;
            case Command.EnqueueShip { Cls: ShipClass.Frigate }: Did.Add("frigate"); break;
            case Command.Deploy: Did.Add("deploy"); break;
            case Command.UseAbility: Did.Add("ability"); break;
        }
    }
}
