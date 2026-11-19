// The campaign's tutorial: ten steps on a small fixed galaxy (Sim/TutorialGalaxy.cs), each done by
// doing it, on the card of the battle's tutorial (Guide.cs). It lives with its galaxy and stays on
// screen during the galaxy's battles (GalaxyScreen.Sleep), where it teaches the fleet cap; it
// sees the galaxy's commands and the battle's (Saw). Its galaxy is never saved: the player's own
// campaign, automatic save or any other, is never written nor cleared (SaveSlots.CampaignFile,
// GalaxySave.Save).
using Godot;
using SandboxShip.Battle;
using SandboxShip.Campaign;
using SandboxShip.Core;
using SandboxShip.Sim;
using static SandboxShip.Ui.HudKit;

namespace SandboxShip.Ui;

public partial class CampaignTutorial : Guide
{
    readonly GalaxyScreen galaxy;
    /// <summary>The galaxy's battle shown, while the player commands one.</summary>
    BattleScreen? battle;
    /// <summary>The view as the galaxy first laid it out, at the first frame: the first step asks to zoom and move it.</summary>
    (double X, double Y)? startCenter;
    double startZoom;

    public CampaignTutorial(GalaxyScreen galaxy) => this.galaxy = galaxy;

    /// <summary>The keys of an action on the galaxy map, as the list of controls has them.</summary>
    static string Key(string action) =>
        Controls.Galaxy.FirstOrDefault(c => c.Action.StartsWith(action, StringComparison.Ordinal)).Keys ?? "(voir « Aide »)";

    protected override string Ending =>
        "Tu connais l'essentiel de la campagne. Cette galaxie ne se sauvegarde pas : continue-la si tu veux, puis lance la tienne depuis « Nouvelle partie ».";

    protected override void Leave() => Main.Instance.ShowMenu();

    public override void _Ready()
    {
        // It goes on teaching while the galaxy sleeps under one of its battles.
        ProcessMode = ProcessModeEnum.Always;
        var g = galaxy.Galaxy;
        string Name(int planet) => g.Planets[planet].Name;
        var cap = Reserves.CapPoints(g.FleetCap);
        Steps.AddRange(
        [
            new("La carte galactique", $"Zoome ({Key("Zoom")}), puis déplace la vue ({Key("Déplacer la vue")}). {Bindings.Label(Bindings.Overview)} montre de nouveau toute la galaxie.",
                () => startCenter is { } start && galaxy.ViewZoom > startZoom * 1.2 && Math.Abs(galaxy.ViewCenter.X - start.X) + Math.Abs(galaxy.ViewCenter.Y - start.Y) > 60),
            new("Produire", $"Clique sur ta capitale, {Name(TutorialGalaxy.Capital)}, puis sur l'onglet « Vaisseaux » du panneau de droite : commande un vaisseau. "
                + "Chaque planète a deux files, ses bâtiments et ses vaisseaux.",
                () => Did.Contains("build")),
            new("Le temps", $"La galaxie démarre en pause : {Bindings.Label(Bindings.Pause)} lance le temps, ×1, ×2 et ×4 en haut règlent sa vitesse. Le menu et l'aide l'arrêtent.",
                () => !galaxy.Paused),
            new("Le ravitaillement", $"À plus de {N(Galaxies.SupplyLanes)} voies de tes stations, une flotte est hors ravitaillement : elle perd {Percent(Galaxies.SupplyWear * 60)} "
                + $"de son état par minute, jusqu'à {Percent(Galaxies.SupplyFloor)}, et ne se répare plus. Ouvre la liste des flottes ({Bindings.Label(Bindings.Fleets)}) : elle dit l'état de chacune.",
                () => Did.Contains("fleets")),
            new("Envoyer une flotte", $"Ta capitale sélectionnée, l'onglet « Planète » montre ta flotte et combien en envoyer. Clic droit sur {Name(TutorialGalaxy.Target)}, "
                + "la planète indépendante voisine : ta flotte part par la voie.",
                () => Did.Contains("send")),
            new("L'invite de bataille", "Une rencontre arrête la galaxie : « Commander la bataille » ouvre la vue tactique, « Résolution automatique » la règle sans toi. "
                + "En bataille, « Laisser l'IA finir », dans le menu, te rend la carte. Commande celle-ci.",
                () => Did.Contains("battle")),
            new("Plafond de flotte et renforts", $"Sur le terrain, ta flotte tient au plus {cap} points : « Flotte », en tête du panneau de réserve, les compte. "
                + "Le reste attend en réserve. Quand un vaisseau tombe, une place se libère : glisse un vaisseau de la réserve sur la carte.",
                () => Did.Contains("deploy") || Did.Contains("battle") && !galaxy.Galaxy.Battles.Exists(b => b.Attacker == galaxy.Player || b.Defender == galaxy.Player)),
            new("La recherche", "Onglet « Recherche » : lance une technologie. Il faut un laboratoire sur une de tes planètes ; ta capitale en a un.",
                () => Did.Contains("tech")),
            new("Les indépendants", $"Une planète indépendante peut te rejoindre sans combat. Sélectionne {Name(TutorialGalaxy.Courted)}, voisine de ta capitale, "
                + $"puis « Convaincre » dans son panneau : des crédits, et elle est à toi dans {N(Galaxies.CourtTime)} s.",
                () => Did.Contains("court")),
            new("La diplomatie", $"{Bindings.Label(Bindings.Diplomacy)} ouvre la diplomatie : l'humeur de chaque camp et cinq intentions, la paix, le commerce, l'alliance, "
                + "exiger et la guerre. Choisis-en une et propose : l'IA accepte, refuse en disant pourquoi, ou fait une contre-offre.",
                () => Did.Contains("deal")),
        ]);
        BuildCard(480);
        if (Dev.Coach > 1) DevSkip(Dev.Coach);
    }

    /// <summary>
    /// --coach=N: a dev run starts at step N, for its captures. At the sixth and seventh, the
    /// capital's fleet leaves for the planet to take, time going, and its battle is prompted (with
    /// --choose=command, commanded); at the last, the diplomacy screen opens on the rival.
    /// </summary>
    void DevSkip(int step)
    {
        SkipTo(step - 1);
        var g = galaxy.Galaxy;
        if (step is 6 or 7 && Galaxies.FleetsAt(g, TutorialGalaxy.Capital, galaxy.Player).FirstOrDefault() is { } fleet)
        {
            galaxy.Sink.Send(new GalaxyCommand.SendFleet(fleet.Id, TutorialGalaxy.Target));
            if (galaxy.Paused) galaxy.TogglePause();
        }
        if (step == Steps.Count) galaxy.Hud.OpenDiplomacy(TutorialGalaxy.Rival);
    }

    public override void _Process(double delta)
    {
        // The galaxy, its parent, laid its view out earlier in this frame.
        if (startCenter is null)
        {
            startCenter = galaxy.ViewCenter;
            startZoom = galaxy.ViewZoom;
        }
        if (galaxy.Hud.ShownDrawer == GalaxyHud.Drawer.Fleets) Did.Add("fleets");
        // In battle, under the top bar as the battle's tutorial; on the map, at the bottom left, clear
        // of the drawers, the galaxy fitted above it.
        var screen = GetViewport().GetVisibleRect().Size;
        Card.Position = battle is not null ? new Vector2(8, 64) : new Vector2(8, screen.Y - Card.Size.Y - 8);
        if (battle is null) galaxy.MakeRoom(Card.Size.Y + 8);
        base._Process(delta);
    }

    /// <summary>A galaxy command the player sent and the galaxy took: the steps it may complete.</summary>
    public void Saw(GalaxyCommand c, Result r)
    {
        if (!r.Ok) return;
        switch (c)
        {
            case GalaxyCommand.EnqueueBuild: Did.Add("build"); break;
            case GalaxyCommand.SendFleet or GalaxyCommand.RedirectFleet: Did.Add("send"); break;
            case GalaxyCommand.ChooseBattle: Did.Add("battle"); break;
            case GalaxyCommand.StartTech: Did.Add("tech"); break;
            case GalaxyCommand.Court: Did.Add("court"); break;
            case GalaxyCommand.ProposeDeal or GalaxyCommand.ProposeAccord or GalaxyCommand.OfferTruce or GalaxyCommand.AnswerDeal: Did.Add("deal"); break;
        }
    }

    /// <summary>A battle command of the player's, in one of the galaxy's battles.</summary>
    public void Saw(Command c, Result r)
    {
        if (r.Ok && c is Command.Deploy) Did.Add("deploy");
    }

    /// <summary>One of the galaxy's battles is shown, or no longer: the card moves with it.</summary>
    public void Attach(BattleScreen b) => battle = b;

    public void Detach(BattleScreen b)
    {
        if (battle == b) battle = null;
    }
}
