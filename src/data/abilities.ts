// Active abilities: one per ship class, used by the player (key E) or by the AI.

export type AbilityId =
  | 'sprint' | 'repair' | 'salvo' | 'overcharge' | 'barrage' | 'broadside'
  // Heroes only.
  | 'command' | 'precision' | 'fleetRepair';

export interface AbilityDef {
  id: AbilityId;
  name: string;
  description: string;
  cooldown: number;
  /** Seconds the effect lasts; 0 for an instant effect. */
  duration: number;
}

export const ABILITIES: Record<AbilityId, AbilityDef> = {
  sprint: { id: 'sprint', name: 'Postcombustion', description: 'Vitesse doublée pendant 5 s', cooldown: 20, duration: 5 },
  repair: { id: 'repair', name: "Réparation d'urgence", description: 'Répare de 25 % les alliés proches', cooldown: 40, duration: 0 },
  salvo: { id: 'salvo', name: 'Salve', description: 'Cadence de tir triplée pendant 6 s', cooldown: 30, duration: 6 },
  overcharge: { id: 'overcharge', name: 'Surcharge des boucliers', description: 'Boucliers pleins et recharge ×4 pendant 8 s', cooldown: 35, duration: 8 },
  barrage: { id: 'barrage', name: 'Tir de barrage', description: 'Dégâts +50 % pendant 8 s', cooldown: 40, duration: 8 },
  broadside: { id: 'broadside', name: 'Bordée', description: 'Toutes les tourelles rechargées, dégâts ×2 pendant 5 s', cooldown: 45, duration: 5 },
  command: { id: 'command', name: "Ordre d'attaque", description: 'Alliés proches : dégâts +30 % et vitesse +20 % pendant 10 s', cooldown: 60, duration: 10 },
  precision: { id: 'precision', name: 'Tir de précision', description: 'Dégâts ×3 pendant 5 s', cooldown: 30, duration: 5 },
  fleetRepair: { id: 'fleetRepair', name: 'Réparation de flotte', description: 'Répare de 40 % et recharge les boucliers des alliés proches', cooldown: 60, duration: 0 },
};

/** Range of the admiral's attack order. */
export const COMMAND_RADIUS = 500;
/** Range of the engineer's fleet repair. */
export const FLEET_REPAIR_RADIUS = 450;

/** Range of the emergency repair, around the support ship. */
export const REPAIR_RADIUS = 260;
