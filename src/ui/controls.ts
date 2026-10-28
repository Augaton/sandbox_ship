// Key bindings, as shown in the help dialogs and on the menu's controls screen.
// Movement keys are physical positions: ZQSD on AZERTY, WASD on QWERTY.

export const BATTLE_CONTROLS: [string, string][] = [
  ['Clic gauche', 'Sélectionner (glisser : rectangle, Maj : ajouter)'],
  ['Double clic, Ctrl + clic', 'Tous les vaisseaux de la même classe à l\'écran'],
  ['Ctrl + A', 'Tous tes vaisseaux'],
  ['Clic droit', 'Déplacer, ou attaquer la cible visée'],
  ['F puis clic, Ctrl + clic droit', 'Attaque-mouvement : engage tout ce qui croise la route'],
  ['X', 'Stop'],
  ['E', 'Capacité des vaisseaux sélectionnés'],
  ['P', 'Piloter le vaisseau sélectionné : ZQSD ou flèches, clic gauche pour tirer'],
  ['Ctrl ou Maj + 1 à 9', 'Créer un groupe'],
  ['1 à 9', 'Rappeler un groupe (deux fois : centrer)'],
  ['ZQSD, flèches, bords, clic molette', 'Déplacer la vue (Maj : plus vite)'],
  ['Molette, + et -', 'Zoom'],
  ['C', 'Centrer la vue sur la sélection'],
  ['H', 'Centrer la vue sur la station'],
  ['Espace', 'Pause'],
  ['Échap', 'Annuler le mode en cours, puis la sélection'],
];

export const GALAXY_CONTROLS: [string, string][] = [
  ['Clic', 'Sélectionner une planète'],
  ['Clic droit', 'Envoyer la flotte de la planète sélectionnée (les vaisseaux choisis dans l\'onglet Planète)'],
  ['Espace', 'Pause'],
  ['Échap', 'Désélectionner'],
];

export function controlsList(rows: [string, string][]): string {
  return '<dl>' + rows.map(([k, v]) => `<dt>${k}</dt><dd>${v}</dd>`).join('') + '</dl>';
}
