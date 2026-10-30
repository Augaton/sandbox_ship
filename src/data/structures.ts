import { flak, laser, type ShipClass, type WeaponDef } from './units';

export interface HangarDef {
  /** Launched in turn, one squadron per interval. */
  squadrons: { cls: ShipClass; size: number }[];
  /** Maximum number of launched ships alive at once. */
  cap: number;
  interval: number;
}

export type HardpointRole = 'turret' | 'heavyTurret' | 'hangar' | 'shield' | 'shipyard' | 'engine';

export interface HardpointDef {
  role: HardpointRole;
  name: string;
  hull: number;
  radius: number;
  weapons: WeaponDef[];
  hangar?: HangarDef;
}

export const HARDPOINTS: Record<HardpointRole, HardpointDef> = {
  turret: {
    role: 'turret', name: 'Tourelle', hull: 400, radius: 14,
    weapons: [{ ...laser, range: 420, damage: 12, cooldown: 1 }],
  },
  heavyTurret: {
    role: 'heavyTurret', name: 'Tourelle lourde', hull: 700, radius: 18,
    weapons: [{ ...laser, range: 520, damage: 30, cooldown: 2 }],
  },
  hangar: {
    role: 'hangar', name: 'Hangar', hull: 600, radius: 20, weapons: [],
    hangar: { squadrons: [{ cls: 'fighter', size: 3 }, { cls: 'bomber', size: 2 }], cap: 8, interval: 20 },
  },
  /** While one stands, the station or ship it belongs to has shields. */
  shield: { role: 'shield', name: 'Générateur de bouclier', hull: 500, radius: 16, weapons: [] },
  /** While alive, the faction can build ships. */
  shipyard: { role: 'shipyard', name: 'Chantier naval', hull: 800, radius: 22, weapons: [] },
  /** Each engine lost slows its ship down. */
  engine: { role: 'engine', name: 'Réacteur', hull: 300, radius: 9, weapons: [] },
};

export interface HardpointSlot {
  role: HardpointRole;
  /** Offset from the parent's center with the parent at angle 0: x forward, y to starboard. */
  x: number;
  y: number;
  /** Overrides of the role's defaults, for the smaller parts of ships. */
  hull?: number;
  radius?: number;
  weapons?: WeaponDef[];
  hangar?: HangarDef;
}

export interface StationLevel {
  /** Credits per second the station brings in at this level. */
  income: number;
  hull: number;
  shield: number;
  shieldRegen: number;
  /** Cost and time to reach this level (unused for level 1). */
  upgradeCost: number;
  upgradeTime: number;
  /** Hardpoints added when this level is reached. */
  hardpoints: HardpointSlot[];
}

export const STATION = {
  name: 'Station spatiale',
  radius: 90,
  constructionRadius: 650,
  weapons: [flak] as WeaponDef[],
  levels: [
    {
      income: 6, hull: 3000, shield: 1000, shieldRegen: 5, upgradeCost: 0, upgradeTime: 0,
      hardpoints: [
        { role: 'shipyard', x: 0, y: -100 },
        { role: 'hangar', x: 0, y: 100 },
        { role: 'shield', x: -70, y: 0 },
        { role: 'turret', x: 75, y: -70 },
        { role: 'turret', x: 75, y: 70 },
      ],
    },
    {
      income: 8, hull: 4500, shield: 1500, shieldRegen: 7, upgradeCost: 1200, upgradeTime: 30,
      hardpoints: [
        { role: 'turret', x: -75, y: -70 },
        { role: 'turret', x: -75, y: 70 },
        { role: 'hangar', x: 105, y: 0 },
      ],
    },
    {
      income: 10, hull: 6500, shield: 2200, shieldRegen: 10, upgradeCost: 2500, upgradeTime: 45,
      hardpoints: [
        { role: 'heavyTurret', x: 0, y: -150 },
        { role: 'heavyTurret', x: 0, y: 150 },
      ],
    },
  ] as StationLevel[],
};

export type StructureType = 'station' | 'platform' | 'mine' | 'outpost' | 'repairDock' | 'ionCannon';
export type BuildableType = Exclude<StructureType, 'station'>;

export interface BuildableDef {
  id: BuildableType;
  name: string;
  hull: number;
  shield: number;
  shieldRegen: number;
  radius: number;
  cost: number;
  buildTime: number;
  minStationLevel: number;
  weapons: WeaponDef[];
  /** Credits per second once built. */
  income?: number;
  hangar?: HangarDef;
  /** Repairs every friendly ship in range. */
  repair?: { range: number; rate: number };
  /** Can also be built next to one of the faction's mines, not only near its station. */
  nearMines?: boolean;
  /** Construction radius around it, like the station's: an outpost opens a new build area. */
  zone?: number;
  /** Levels 2 and 3, bought one after the other. */
  tiers?: TierDef[];
}

/** What a structure becomes at a higher level. */
export interface TierDef {
  cost: number;
  time: number;
  hull: number;
  shield: number;
  weapons?: WeaponDef[];
  hangar?: HangarDef;
  zone?: number;
}

/** Radius around a built mine where platforms and repair docks may go. */
export const MINE_ZONE = 350;
/**
 * Outside every build area, a mine or an outpost needs one of the faction's ships this
 * close to hold the spot...
 */
export const HOLD_RANGE = 400;
/** ...and no enemy ship or gun this close. */
export const DANGER_RANGE = 600;

export const BUILDABLES: Record<BuildableType, BuildableDef> = {
  platform: {
    id: 'platform', name: 'Plateforme de défense',
    hull: 900, shield: 200, shieldRegen: 5, radius: 22,
    cost: 400, buildTime: 15, minStationLevel: 1,
    weapons: [{ ...laser, range: 450, damage: 12 }, flak],
    nearMines: true,
    tiers: [
      { cost: 400, time: 20, hull: 1400, shield: 350, weapons: [{ ...laser, range: 470, damage: 18 }, flak] },
      { cost: 700, time: 30, hull: 2000, shield: 500, weapons: [{ ...laser, range: 500, damage: 22 }, { ...laser, range: 500, damage: 22 }, flak] },
    ],
  },
  mine: {
    id: 'mine', name: "Mine d'astéroïdes",
    hull: 700, shield: 0, shieldRegen: 0, radius: 26,
    cost: 500, buildTime: 20, minStationLevel: 1,
    weapons: [], income: 8,
  },
  outpost: {
    id: 'outpost', name: 'Avant-poste',
    hull: 1000, shield: 300, shieldRegen: 5, radius: 30,
    cost: 700, buildTime: 25, minStationLevel: 1,
    weapons: [flak], zone: 450,
    tiers: [
      { cost: 500, time: 20, hull: 1500, shield: 450, zone: 600, weapons: [flak, { ...laser, range: 450, damage: 14 }] },
      { cost: 800, time: 30, hull: 2200, shield: 650, zone: 750, weapons: [flak, { ...laser, range: 480, damage: 18 }, { ...laser, range: 480, damage: 18 }] },
    ],
  },
  repairDock: {
    id: 'repairDock', name: 'Dock de réparation',
    hull: 900, shield: 200, shieldRegen: 5, radius: 30,
    cost: 600, buildTime: 20, minStationLevel: 1,
    weapons: [], repair: { range: 280, rate: 15 },
    nearMines: true,
  },
  ionCannon: {
    id: 'ionCannon', name: 'Canon ionique',
    hull: 1400, shield: 400, shieldRegen: 8, radius: 26,
    cost: 1500, buildTime: 35, minStationLevel: 3,
    weapons: [{ kind: 'ray', range: 900, damage: 90, cooldown: 5, projectileSpeed: 1200, mult: { small: 0.1, medium: 0.8 } }],
  },
};

export const BUILDABLE_TYPES = Object.keys(BUILDABLES) as BuildableType[];

export const MAX_QUEUE = 8;

/**
 * A station's lost parts come back on their own: none could ever build ships again after
 * losing its shipyard otherwise. Seconds to rebuild one, counted only once the station has
 * gone this long without being hit.
 */
export const REBUILD = { time: 90, quiet: 20 };
