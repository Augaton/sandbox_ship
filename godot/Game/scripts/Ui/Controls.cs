// Key bindings, as shown in the help dialogs and on the menu's controls screen. Port of
// src/ui/controls.ts. The keys the player may change are named as they are now (Bindings);
// the view's keys are physical positions: ZQSD on AZERTY, WASD on QWERTY.
using SandboxShip.Core;
using static SandboxShip.Core.Bindings;

namespace SandboxShip.Ui;

public static class Controls
{
    /// <summary>
    /// A row of the lists: its keys and what they do, and what of them stays whatever the player
    /// sets (the mouse, the groups, Échap), null when all of its keys can be changed.
    /// </summary>
    readonly record struct Row(string Keys, string Action, string? Fixed);

    static Row Same(string keys, string action) => new(keys, action, keys);

    static IEnumerable<Row> BattleRows() =>
    [
        Same("Clic gauche", "Sélectionner (glisser : rectangle, Maj : ajouter)"),
        Same("Double clic, Ctrl + clic", "Tous les vaisseaux de la même classe à l'écran"),
        new(Label(SelectAll), "Tous tes vaisseaux", null),
        Same("Clic droit", "Déplacer, ou attaquer la cible visée"),
        new($"{Label(AttackMove)} puis clic, Ctrl + clic droit", "Attaque-mouvement : engage tout ce qui croise la route", "Ctrl + clic droit"),
        new(Label(Stop), "Stop", null),
        new(Label(Ability), "Capacité des vaisseaux sélectionnés (puis clic pour viser si elle se vise)", null),
        new(Label(Pilot), $"Piloter le vaisseau sélectionné : {Movement()} ou flèches, clic gauche pour tirer", null),
        Same("Ctrl ou Maj + 1 à 9", "Créer un groupe"),
        Same("1 à 9", "Rappeler un groupe (deux fois : centrer)"),
        new($"{Movement()}, flèches, bords, clic molette", "Déplacer la vue (Maj : plus vite)", "Flèches, bords, clic molette"),
        new($"Molette, {Label(ZoomIn)} et {Label(ZoomOut)}", "Zoom", "Molette"),
        new(Label(CenterSelection), "Centrer la vue sur la sélection", null),
        new(Label(CenterStation), "Centrer la vue sur la station", null),
        new(Label(LastAlert), "Aller à la dernière alerte (encore : la précédente)", null),
        new($"{Label(Idle)}, Maj + {Label(Idle)}", "Vaisseau inactif suivant, tous les inactifs", null),
        Same("Clic droit sur la minicarte", "Envoyer la sélection (Ctrl : attaque-mouvement)"),
        Same("Maj + clic sur un vaisseau à produire", "En commander 5 ; clic droit : production en boucle"),
        Same("Clic droit sur la file", "Annuler un élément"),
        Same("Maj + clic en posant", "Poser plusieurs bâtiments de suite"),
        new(Label(Mute), "Couper ou rétablir le son", null),
        new(Label(Pause), "Pause", null),
        Same("Échap", "Annuler le mode en cours, puis la sélection"),
    ];

    static IEnumerable<Row> GalaxyRows() =>
    [
        Same("Clic", "Sélectionner une planète ou ta flotte en route"),
        Same("Maj + clic, glisser sur la carte", "Sélectionner plusieurs de tes planètes (rectangle ; Maj : ajouter)"),
        Same("Clic droit", "Envoyer la flotte de la planète sélectionnée (les vaisseaux choisis dans l'onglet Planète) ; de plusieurs planètes, toutes leurs flottes"),
        new($"Clic droit, {Label(Stop)}", "Flotte en route : changer sa destination (demi-tour si plus court) ; l'arrêter à la prochaine planète", "Clic droit"),
        Same("Ctrl + clic droit", "Ralliement des planètes sélectionnées (sur elle-même : le retirer)"),
        Same("Clic sur un vaisseau à produire", "Un sur chaque planète sélectionnée ; clic droit : production en boucle"),
        Same("Clic droit sur la file", "Annuler un élément"),
        new($"Molette, {Label(ZoomIn)} et {Label(ZoomOut)}", "Zoom", "Molette"),
        new($"{Movement()}, flèches, bords, clic molette", "Déplacer la vue (Maj : plus vite)", "Flèches, bords, clic molette"),
        new($"{Label(Overview)}, Début", "Revoir toute la galaxie", "Début"),
        new($"{Label(Fleets)}, {Label(Empire)}, {Label(Log)}, {Label(Diplomacy)}", "Flottes, vue d'ensemble, journal, diplomatie", null),
        new(Label(Pause), "Pause", null),
        Same("Échap", "Annuler, fermer le volet, désélectionner"),
    ];

    /// <summary>The battle's commands, with the keys they have now: for the help and the menu.</summary>
    public static IReadOnlyList<(string Keys, string Action)> Battle => [.. BattleRows().Select(r => (r.Keys, r.Action))];

    /// <summary>The galaxy's commands, with the keys they have now.</summary>
    public static IReadOnlyList<(string Keys, string Action)> Galaxy => [.. GalaxyRows().Select(r => (r.Keys, r.Action))];

    /// <summary>What of the battle's commands no setting changes, under the keys to change on the controls screen.</summary>
    public static IReadOnlyList<(string Keys, string Action)> BattleFixed => [.. BattleRows().Where(r => r.Fixed is not null).Select(r => (r.Fixed!, r.Action))];

    /// <summary>What of the galaxy's commands no setting changes.</summary>
    public static IReadOnlyList<(string Keys, string Action)> GalaxyFixed => [.. GalaxyRows().Where(r => r.Fixed is not null).Select(r => (r.Fixed!, r.Action))];

    /// <summary>The list as two columns: keys in amber, what they do beside them, wrapped at `actionWidth` pixels or more.</summary>
    public static Godot.GridContainer List(IReadOnlyList<(string Keys, string Action)> rows, float actionWidth = 420)
    {
        var grid = new Godot.GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 16);
        foreach (var (keys, action) in rows)
        {
            grid.AddChild(UiTheme.Text(keys, Palette.Accent));
            var l = UiTheme.Text(action);
            l.AutowrapMode = Godot.TextServer.AutowrapMode.WordSmart;
            l.CustomMinimumSize = new Godot.Vector2(actionWidth, 0);
            grid.AddChild(l);
        }
        return grid;
    }
}
