// Gameplay definitions only: no Phaser, no sprite paths (see src/render/sprites.ts).

export type FactionId = 'klaed' | 'nairan' | 'nautolan';

export const FACTIONS: Record<FactionId, { name: string; color: number }> = {
  klaed: { name: "Kla'ed", color: 0xe0563b },
  nairan: { name: 'Nairan', color: 0x4fa3e0 },
  nautolan: { name: 'Nautolan', color: 0x7bd36a },
};

export const FACTION_IDS = Object.keys(FACTIONS) as FactionId[];

/** Damage multipliers are keyed by the size class of the target. */
export type SizeClass = 'small' | 'medium' | 'large' | 'structure';

export interface WeaponDef {
  kind: 'bullet' | 'laser' | 'torpedo' | 'ray';
  range: number;
  damage: number;
  cooldown: number;
  projectileSpeed: number;
  homing?: boolean;
  mult?: Partial<Record<SizeClass, number>>;
}

export type ShipClass =
  | 'scout'
  | 'fighter'
  | 'bomber'
  | 'support'
  | 'torpedo'
  | 'frigate'
  | 'battlecruiser'
  | 'dreadnought';

export interface ShipDef {
  id: ShipClass;
  name: string;
  size: SizeClass;
  hull: number;
  shield: number;
  shieldRegen: number;
  speed: number;
  accel: number;
  turnRate: number;
  radius: number;
  /** Ships launched by hangars are free and have no cost or build time. */
  source: 'shipyard' | 'hangar';
  cost: number;
  buildTime: number;
  minStationLevel: number;
  weapons: WeaponDef[];
  repair?: { range: number; rate: number };
}

export const flak: WeaponDef = {
  kind: 'bullet', range: 260, damage: 4, cooldown: 0.35, projectileSpeed: 700,
  mult: { small: 2, medium: 0.6, large: 0.3, structure: 0.3 },
};
export const laser: WeaponDef = {
  kind: 'laser', range: 400, damage: 16, cooldown: 1.2, projectileSpeed: 900,
  mult: { small: 0.4 },
};
export const torpedo: WeaponDef = {
  kind: 'torpedo', range: 420, damage: 45, cooldown: 4, projectileSpeed: 320, homing: true,
  mult: { small: 0.15, large: 1.6, structure: 1.6 },
};

export const SHIPS: Record<ShipClass, ShipDef> = {
  scout: {
    id: 'scout', name: 'Éclaireur', size: 'small',
    hull: 60, shield: 20, shieldRegen: 2, speed: 230, accel: 300, turnRate: 4, radius: 10,
    source: 'shipyard', cost: 150, buildTime: 6, minStationLevel: 1,
    weapons: [{ kind: 'bullet', range: 220, damage: 4, cooldown: 0.4, projectileSpeed: 650 }],
  },
  fighter: {
    id: 'fighter', name: 'Chasseur', size: 'small',
    hull: 40, shield: 0, shieldRegen: 0, speed: 260, accel: 360, turnRate: 5, radius: 9,
    source: 'hangar', cost: 0, buildTime: 0, minStationLevel: 1,
    weapons: [{
      kind: 'bullet', range: 200, damage: 3, cooldown: 0.3, projectileSpeed: 700,
      mult: { small: 1.5, medium: 0.5, large: 0.3, structure: 0.2 },
    }],
  },
  bomber: {
    id: 'bomber', name: 'Bombardier', size: 'small',
    hull: 70, shield: 0, shieldRegen: 0, speed: 190, accel: 260, turnRate: 3.5, radius: 12,
    source: 'hangar', cost: 0, buildTime: 0, minStationLevel: 1,
    weapons: [{ ...torpedo, range: 260, damage: 30, cooldown: 3, mult: { small: 0.2, large: 1.5, structure: 1.8 } }],
  },
  support: {
    id: 'support', name: 'Soutien', size: 'medium',
    hull: 220, shield: 80, shieldRegen: 4, speed: 120, accel: 120, turnRate: 2, radius: 18,
    source: 'shipyard', cost: 400, buildTime: 12, minStationLevel: 1,
    weapons: [{ ...flak, damage: 3 }],
    repair: { range: 220, rate: 10 },
  },
  torpedo: {
    id: 'torpedo', name: 'Torpilleur', size: 'medium',
    hull: 300, shield: 100, shieldRegen: 4, speed: 110, accel: 110, turnRate: 1.8, radius: 18,
    source: 'shipyard', cost: 500, buildTime: 14, minStationLevel: 2,
    weapons: [torpedo],
  },
  frigate: {
    id: 'frigate', name: 'Frégate', size: 'medium',
    hull: 600, shield: 250, shieldRegen: 3, speed: 90, accel: 80, turnRate: 1.2, radius: 26,
    source: 'shipyard', cost: 650, buildTime: 16, minStationLevel: 1,
    weapons: [laser, flak],
  },
  battlecruiser: {
    id: 'battlecruiser', name: 'Croiseur', size: 'large',
    hull: 1600, shield: 600, shieldRegen: 5, speed: 65, accel: 50, turnRate: 0.7, radius: 40,
    source: 'shipyard', cost: 1400, buildTime: 28, minStationLevel: 2,
    weapons: [{ ...laser, range: 450, damage: 22 }, { ...laser, range: 450, damage: 22 }, flak],
  },
  dreadnought: {
    id: 'dreadnought', name: 'Cuirassé', size: 'large',
    hull: 4000, shield: 1500, shieldRegen: 8, speed: 45, accel: 35, turnRate: 0.45, radius: 56,
    source: 'shipyard', cost: 3000, buildTime: 45, minStationLevel: 3,
    weapons: [
      { kind: 'ray', range: 560, damage: 60, cooldown: 2.5, projectileSpeed: 1100, mult: { small: 0.2 } },
      { ...laser, range: 450, damage: 22 },
      flak,
      flak,
    ],
  },
};

export const SHIP_CLASSES = Object.keys(SHIPS) as ShipClass[];

export function maxRange(weapons: WeaponDef[]): number {
  return weapons.reduce((m, w) => Math.max(m, w.range), 0);
}
