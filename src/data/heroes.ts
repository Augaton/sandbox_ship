// Heroes: unique, expensive, much stronger than the ship they are built on. Each faction
// has three. A fallen hero can be built again after a delay.
import type { AbilityId } from './abilities';
import type { FactionId, ShipClass } from './units';

export type HeroRole = 'admiral' | 'ace' | 'engineer';
export type HeroId = `${FactionId}-${HeroRole}`;

export interface HeroDef {
  id: HeroId;
  faction: FactionId;
  role: HeroRole;
  name: string;
  title: string;
  description: string;
  /** Ship class the hero flies: sprite, size, weapons and hardpoints. */
  base: ShipClass;
  /** Multipliers over the base class. */
  hull: number;
  shield: number;
  damage: number;
  speed: number;
  ability: AbilityId;
  cost: number;
  buildTime: number;
  minStationLevel: number;
  /** Seconds before a fallen hero can be built again. */
  respawn: number;
}

type RoleStats = Omit<HeroDef, 'id' | 'faction' | 'name' | 'title' | 'description'>;

const ROLES: Record<HeroRole, RoleStats> = {
  admiral: {
    role: 'admiral', base: 'dreadnought', hull: 1.6, shield: 1.5, damage: 1.4, speed: 1.1, ability: 'command',
    cost: 5000, buildTime: 60, minStationLevel: 3, respawn: 180,
  },
  ace: {
    role: 'ace', base: 'scout', hull: 12, shield: 15, damage: 5, speed: 1.3, ability: 'precision',
    cost: 1500, buildTime: 25, minStationLevel: 1, respawn: 90,
  },
  engineer: {
    role: 'engineer', base: 'support', hull: 3.5, shield: 4, damage: 2, speed: 1.2, ability: 'fleetRepair',
    cost: 1800, buildTime: 30, minStationLevel: 2, respawn: 120,
  },
};

function hero(faction: FactionId, role: HeroRole, name: string, title: string, description: string, tweak: Partial<RoleStats> = {}): HeroDef {
  return { ...ROLES[role], ...tweak, id: `${faction}-${role}`, faction, name, title, description };
}

export const HEROES: Record<HeroId, HeroDef> = {
  'klaed-admiral': hero('klaed', 'admiral', 'Vorrak', 'Amiral', 'Cuirassé de commandement aux canons renforcés.', { damage: 1.7 }),
  'klaed-ace': hero('klaed', 'ace', 'Kesh', 'As', "Éclaireur d'élite, redoutable contre les petits vaisseaux."),
  'klaed-engineer': hero('klaed', 'engineer', 'Ondra', 'Ingénieure', 'Remet une flotte entière en état en pleine bataille.'),
  'nairan-admiral': hero('nairan', 'admiral', 'Selis Vaun', 'Amirale', 'Cuirassé aux boucliers presque impénétrables.', { shield: 2.2 }),
  'nairan-ace': hero('nairan', 'ace', 'Ilo Fenn', 'As', 'Pilote de chasse, plus rapide que tout ce qui vole.', { speed: 1.5 }),
  'nairan-engineer': hero('nairan', 'engineer', 'Tavi Qor', 'Ingénieur', 'Spécialiste des boucliers et des réparations de flotte.'),
  'nautolan-admiral': hero('nautolan', 'admiral', "Ysh'ra", 'Mère-nef', 'Cuirassé vivant, à la coque immense.', { hull: 2.2 }),
  'nautolan-ace': hero('nautolan', 'ace', 'Kael', 'Rôdeur', 'Chasseur organique, endurant et tenace.', { hull: 16 }),
  'nautolan-engineer': hero('nautolan', 'engineer', 'Om', 'Greffeur', 'Fait repousser les coques endommagées.'),
};

export const HERO_IDS = Object.keys(HEROES) as HeroId[];

export function heroesOf(faction: FactionId): HeroDef[] {
  return HERO_IDS.filter((id) => HEROES[id].faction === faction).map((id) => HEROES[id]);
}
