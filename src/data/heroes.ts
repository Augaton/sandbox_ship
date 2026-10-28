// Heroes: unique, expensive, much stronger than the ship they are built on, each with a power
// of their own. Each faction has three. A fallen hero can be built again after a delay.
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

type RoleStats = Omit<HeroDef, 'id' | 'faction' | 'name' | 'title' | 'description' | 'ability'>;

const ROLES: Record<HeroRole, RoleStats> = {
  admiral: {
    role: 'admiral', base: 'dreadnought', hull: 1.6, shield: 1.5, damage: 1.4, speed: 1.1,
    cost: 5000, buildTime: 60, minStationLevel: 3, respawn: 180,
  },
  ace: {
    role: 'ace', base: 'scout', hull: 12, shield: 15, damage: 5, speed: 1.3,
    cost: 1500, buildTime: 25, minStationLevel: 1, respawn: 90,
  },
  engineer: {
    role: 'engineer', base: 'support', hull: 3.5, shield: 4, damage: 2, speed: 1.2,
    cost: 1800, buildTime: 30, minStationLevel: 2, respawn: 120,
  },
};

function hero(
  faction: FactionId, role: HeroRole, ability: AbilityId, name: string, title: string, description: string, tweak: Partial<RoleStats> = {},
): HeroDef {
  return { ...ROLES[role], ...tweak, id: `${faction}-${role}`, faction, ability, name, title, description };
}

// Admirals also command: allies around them deal more damage and fly faster.
export const HEROES: Record<HeroId, HeroDef> = {
  'klaed-admiral': hero('klaed', 'admiral', 'orbitalStrike', 'Vorrak', 'Amiral', 'Cuirassé de commandement aux canons renforcés. Appelle un bombardement orbital.', { damage: 1.7 }),
  'klaed-ace': hero('klaed', 'ace', 'huntMark', 'Kesh', 'As', "Éclaireur d'élite. Marque une proie que toute la flotte achève."),
  'klaed-engineer': hero('klaed', 'engineer', 'fieldTurret', 'Ondra', 'Ingénieure', 'Répare en continu et déploie des tourelles en pleine bataille.'),
  'nairan-admiral': hero('nairan', 'admiral', 'shieldDome', 'Selis Vaun', 'Amirale', 'Cuirassé aux boucliers presque impénétrables. Couvre sa flotte d\'un dôme.', { shield: 2.2 }),
  'nairan-ace': hero('nairan', 'ace', 'cloak', 'Ilo Fenn', 'As', 'Pilote de chasse plus rapide que tout. Disparaît pour frapper par surprise.', { speed: 1.5 }),
  'nairan-engineer': hero('nairan', 'engineer', 'emp', 'Tavi Qor', 'Ingénieur', 'Spécialiste des boucliers : son impulsion paralyse les vaisseaux ennemis.'),
  'nautolan-admiral': hero('nautolan', 'admiral', 'swarm', "Ysh'ra", 'Mère-nef', 'Cuirassé vivant, à la coque immense, qui enfante des essaims.', { hull: 2.2 }),
  'nautolan-ace': hero('nautolan', 'ace', 'spores', 'Kael', 'Rôdeur', 'Chasseur organique et tenace. Ses spores rongent les coques.', { hull: 16 }),
  'nautolan-engineer': hero('nautolan', 'engineer', 'graft', 'Om', 'Greffeur', 'Fait repousser les coques de toute une flotte.'),
};

export const HERO_IDS = Object.keys(HEROES) as HeroId[];

export function heroesOf(faction: FactionId): HeroDef[] {
  return HERO_IDS.filter((id) => HEROES[id].faction === faction).map((id) => HEROES[id]);
}
