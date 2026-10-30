// Key bindings, as shown in the help dialogs and on the menu's controls screen. Port of
// src/ui/controls.ts. Movement keys are physical positions: ZQSD on AZERTY, WASD on QWERTY.
namespace SandboxShip.Ui;

public static class Controls
{
    public static readonly IReadOnlyList<(string Keys, string Action)> Battle =
    [
        ("Clic gauche", "Sélectionner (glisser : rectangle, Maj : ajouter)"),
        ("Double clic, Ctrl + clic", "Tous les vaisseaux de la même classe à l'écran"),
        ("Ctrl + A", "Tous tes vaisseaux"),
        ("Clic droit", "Déplacer, ou attaquer la cible visée"),
        ("F puis clic, Ctrl + clic droit", "Attaque-mouvement : engage tout ce qui croise la route"),
        ("X", "Stop"),
        ("E", "Capacité des vaisseaux sélectionnés (puis clic pour viser si elle se vise)"),
        ("P", "Piloter le vaisseau sélectionné : ZQSD ou flèches, clic gauche pour tirer"),
        ("Ctrl ou Maj + 1 à 9", "Créer un groupe"),
        ("1 à 9", "Rappeler un groupe (deux fois : centrer)"),
        ("ZQSD, flèches, bords, clic molette", "Déplacer la vue (Maj : plus vite)"),
        ("Molette, + et -", "Zoom"),
        ("C", "Centrer la vue sur la sélection"),
        ("H", "Centrer la vue sur la station"),
        ("Retour arrière", "Aller à la dernière alerte (encore : la précédente)"),
        ("I, Maj + I", "Vaisseau inactif suivant, tous les inactifs"),
        ("Clic droit sur la minicarte", "Envoyer la sélection (Ctrl : attaque-mouvement)"),
        ("Maj + clic sur un vaisseau à produire", "En commander 5 ; clic droit : production en boucle"),
        ("Clic droit sur la file", "Annuler un élément"),
        ("Maj + clic en posant", "Poser plusieurs bâtiments de suite"),
        ("M", "Couper ou rétablir le son"),
        ("Espace", "Pause"),
        ("Échap", "Annuler le mode en cours, puis la sélection"),
    ];

    public static readonly IReadOnlyList<(string Keys, string Action)> Galaxy =
    [
        ("Clic", "Sélectionner une planète ou ta flotte en route"),
        ("Maj + clic, glisser sur la carte", "Sélectionner plusieurs de tes planètes (rectangle ; Maj : ajouter)"),
        ("Clic droit", "Envoyer la flotte de la planète sélectionnée (les vaisseaux choisis dans l'onglet Planète) ; de plusieurs planètes, toutes leurs flottes"),
        ("Clic droit, X", "Flotte en route : changer sa destination (demi-tour si plus court) ; l'arrêter à la prochaine planète"),
        ("Ctrl + clic droit", "Ralliement des planètes sélectionnées (sur elle-même : le retirer)"),
        ("Clic sur un vaisseau à produire", "Un sur chaque planète sélectionnée ; clic droit : production en boucle"),
        ("Clic droit sur la file", "Annuler un élément"),
        ("Molette, + et -", "Zoom"),
        ("ZQSD, flèches, bords, clic molette", "Déplacer la vue (Maj : plus vite)"),
        ("H, Début", "Revoir toute la galaxie"),
        ("F, E, J, T", "Flottes, vue d'ensemble, journal, diplomatie"),
        ("Espace", "Pause"),
        ("Échap", "Annuler, fermer le volet, désélectionner"),
    ];

    /// <summary>The list as two columns: keys in amber, what they do beside them, wrapped at `actionWidth` pixels or more.</summary>
    public static Godot.GridContainer List(IReadOnlyList<(string Keys, string Action)> rows, float actionWidth = 420)
    {
        var grid = new Godot.GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 16);
        foreach (var (keys, action) in rows)
        {
            grid.AddChild(UiTheme.Text(keys, Core.Palette.Accent));
            var l = UiTheme.Text(action);
            l.AutowrapMode = Godot.TextServer.AutowrapMode.WordSmart;
            l.CustomMinimumSize = new Godot.Vector2(actionWidth, 0);
            grid.AddChild(l);
        }
        return grid;
    }
}
