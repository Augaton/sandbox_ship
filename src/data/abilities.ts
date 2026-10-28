// Active abilities: one per ship class and one per hero, used by the player (key E or the
// HUD buttons) or, when automatic, by the faction itself.

export type AbilityId =
  | 'sprint' | 'repair' | 'salvo' | 'overcharge' | 'barrage' | 'broadside' | 'spinal'
  // Heroes only: one each.
  | 'orbitalStrike' | 'huntMark' | 'fieldTurret'
  | 'shieldDome' | 'cloak' | 'emp'
  | 'swarm' | 'spores' | 'graft';

/** What the player picks to use it: nothing, a spot on the map, or an enemy. */
export type AbilityTarget = 'self' | 'point' | 'enemy';

export interface AbilityDef {
  id: AbilityId;
  name: string;
  description: string;
  cooldown: number;
  /** Seconds the effect lasts; 0 for an instant effect. */
  duration: number;
  target: AbilityTarget;
  /** How far from the ship a point or an enemy may be picked. */
  range?: number;
  /** Radius of the area it covers. */
  radius?: number;
  /** Color of its marks on the battlefield. */
  color: number;
}

const GOLD = 0xe8a13a;
const RED = 0xe05a4a;
const GREEN = 0x6fd08c;
const BLUE = 0x5aa9e6;

export const ABILITIES: Record<AbilityId, AbilityDef> = {
  sprint: {
    id: 'sprint', name: 'Postcombustion', description: 'Vitesse doublée pendant 5 s.',
    cooldown: 20, duration: 5, target: 'self', color: BLUE,
  },
  repair: {
    id: 'repair', name: "Réparation d'urgence", description: 'Répare de 25 % les alliés proches.',
    cooldown: 40, duration: 0, target: 'self', radius: 260, color: GREEN,
  },
  salvo: {
    id: 'salvo', name: 'Salve', description: 'Cadence de tir triplée pendant 6 s.',
    cooldown: 30, duration: 6, target: 'self', color: GOLD,
  },
  overcharge: {
    id: 'overcharge', name: 'Surcharge des boucliers', description: 'Boucliers pleins, recharge ×4 pendant 8 s.',
    cooldown: 35, duration: 8, target: 'self', color: BLUE,
  },
  barrage: {
    id: 'barrage', name: 'Tir de barrage', description: 'Dégâts +50 % pendant 8 s.',
    cooldown: 40, duration: 8, target: 'self', color: RED,
  },
  broadside: {
    id: 'broadside', name: 'Bordée', description: 'Toutes les tourelles rechargées, dégâts ×2 pendant 5 s.',
    cooldown: 45, duration: 5, target: 'self', color: RED,
  },
  spinal: {
    id: 'spinal', name: 'Rayon spinal',
    description: 'Charge 2 s, puis un rayon de 1200 de long frappe tout ce qui est sur sa ligne (900 dégâts).',
    cooldown: 50, duration: 0, target: 'point', range: 1200, color: GOLD,
  },
  orbitalStrike: {
    id: 'orbitalStrike', name: 'Bombardement orbital',
    description: 'Après 2,5 s, une zone explose : 650 dégâts à tous les ennemis dedans. Le délai laisse le temps de fuir.',
    cooldown: 45, duration: 0, target: 'point', range: 1000, radius: 220, color: RED,
  },
  huntMark: {
    id: 'huntMark', name: 'Marque du chasseur', description: 'La cible subit +50 % de dégâts de toutes parts pendant 12 s.',
    cooldown: 25, duration: 0, target: 'enemy', range: 700, color: RED,
  },
  fieldTurret: {
    id: 'fieldTurret', name: 'Tourelle de campagne', description: 'Déploie une plateforme de défense armée pendant 45 s.',
    cooldown: 50, duration: 0, target: 'point', range: 300, color: GOLD,
  },
  shieldDome: {
    id: 'shieldDome', name: 'Dôme de bouclier', description: 'Les alliés sous le dôme subissent 50 % de dégâts en moins pendant 10 s.',
    cooldown: 50, duration: 10, target: 'self', radius: 380, color: BLUE,
  },
  cloak: {
    id: 'cloak', name: 'Camouflage', description: 'Invisible et intouchable pendant 8 s, dégâts ×2 en embuscade.',
    cooldown: 30, duration: 8, target: 'self', color: BLUE,
  },
  emp: {
    id: 'emp', name: 'Impulsion IEM', description: 'Les ennemis proches perdent leurs boucliers et restent paralysés 4 s.',
    cooldown: 45, duration: 0, target: 'self', radius: 350, color: BLUE,
  },
  swarm: {
    id: 'swarm', name: 'Essaim', description: 'Lâche 8 chasseurs organiques qui vivent 30 s.',
    cooldown: 50, duration: 0, target: 'self', color: GREEN,
  },
  spores: {
    id: 'spores', name: 'Spores corrosives', description: 'Ronge la coque de la cible, à travers les boucliers, pendant 8 s.',
    cooldown: 30, duration: 0, target: 'enemy', range: 600, color: GREEN,
  },
  graft: {
    id: 'graft', name: 'Greffe', description: 'Les alliés proches régénèrent 3 % de leur coque par seconde pendant 10 s.',
    cooldown: 50, duration: 10, target: 'self', radius: 420, color: GREEN,
  },
};

/** Admirals' passive command: allies this close deal more damage and fly faster. */
export const COMMAND_RADIUS = 500;

/** Status effects that abilities leave on their targets, in seconds. */
export const EFFECTS = { marked: 12, stunned: 4, corroded: 8 };
/** Time between using the spinal beam and its shot, and between the strike call and the blast. */
export const SPINAL_CHARGE = 2;
export const ORBITAL_DELAY = 2.5;
