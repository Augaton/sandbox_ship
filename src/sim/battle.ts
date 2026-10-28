// A space battle fought over a planet of the galaxy map: built from fleets and planet
// defenses, run by the tactical simulation, and read back as survivors.
import { HEROES, type HeroId } from '../data/heroes';
import type { ResearchLevels } from '../data/research';
import { SHIPS, SHIP_CLASSES, type FactionId, type ShipClass } from '../data/units';
import { formationOffsets, issueMove } from './movement';
import { scatterFields } from './scenario';
import { applyTier } from './structures';
import { isVisible } from './vision';
import {
  addFaction, createWorld, dist, rand, spawnHero, spawnShip, spawnStation, spawnStructure, stationOf,
  type Entity, type World,
} from './world';

export type ShipCounts = Partial<Record<ShipClass, number>>;

export interface PlanetDefense {
  /** Station level, 0 without a station. */
  station: number;
  platforms: number;
  mines: number;
  repairDock: number;
  ionCannon: number;
  /** Level of the platforms, 1 to 3. */
  platformTier: number;
}

export const NO_DEFENSE: PlanetDefense = { station: 0, platforms: 0, mines: 0, repairDock: 0, ionCannon: 0, platformTier: 1 };

export interface BattleSide {
  faction: FactionId;
  ships: ShipCounts;
  heroes?: HeroId[];
  research?: ResearchLevels;
}

/** The attacker took the planet, the defender held it, or the attacker fell back. */
export type BattleResult = 'attacker' | 'defender' | 'retreat';

export const BATTLE_W = 4000;
export const BATTLE_H = 2600;

export function countShips(ships: ShipCounts): number {
  return SHIP_CLASSES.reduce((n, c) => n + (ships[c] ?? 0), 0);
}

/** Total build cost: a rough measure of fighting strength. */
export function fleetValue(ships: ShipCounts, heroes: HeroId[] = []): number {
  return SHIP_CLASSES.reduce((v, c) => v + (ships[c] ?? 0) * SHIPS[c].cost, 0) + heroes.reduce((v, h) => v + HEROES[h].cost, 0);
}

function placeShips(world: World, side: BattleSide, x: number, y: number, angle: number): void {
  const list: (ShipClass | HeroId)[] = [
    ...(side.heroes ?? []),
    ...SHIP_CLASSES.flatMap((c) => Array<ShipClass>(side.ships[c] ?? 0).fill(c)),
  ];
  const radius = (k: ShipClass | HeroId) => SHIPS[k in HEROES ? HEROES[k as HeroId].base : (k as ShipClass)].radius;
  // Heavy ships in the middle, as for a move order.
  list.sort((a, b) => radius(b) - radius(a));
  const offsets = formationOffsets(list.length, 90).sort((a, b) => Math.hypot(a.x, a.y) - Math.hypot(b.x, b.y));
  list.forEach((k, i) => {
    const px = x + offsets[i].x;
    const py = y + offsets[i].y;
    if (k in HEROES) spawnHero(world, side.faction, k as HeroId, px, py, angle);
    else spawnShip(world, side.faction, k as ShipClass, px, py, angle);
  });
}

/**
 * Defender on the left around its station, attacker arriving from the right. Both sides
 * bring their research, and the AI handles their ship abilities unless told otherwise.
 */
export function createPlanetBattle(attacker: BattleSide, defender: BattleSide, defense: PlanetDefense, seed = 1): World {
  const w = createWorld(BATTLE_W, BATTLE_H, seed);
  w.fog = true;
  for (const side of [defender, attacker]) {
    const f = addFaction(w, side.faction, 0);
    f.autoAbilities = true;
    if (side.research) f.research = { ...side.research };
  }
  const home = { x: 800, y: BATTLE_H / 2 };
  scatterFields(w, 5 + Math.floor(rand(w) * 4), [home, { x: BATTLE_W - 500, y: home.y }], 380);
  if (defense.station > 0) spawnStation(w, defender.faction, home.x, home.y, defense.station);
  const fields = [...w.fields].sort((a, b) => dist(a, home) - dist(b, home));
  for (const field of fields.slice(0, defense.mines)) {
    const mine = spawnStructure(w, defender.faction, 'mine', field.x, field.y);
    mine.fieldId = field.id;
    field.mineId = mine.id;
  }
  for (let i = 0; i < defense.platforms; i++) {
    const a = (i - (defense.platforms - 1) / 2) * 0.5;
    const platform = spawnStructure(w, defender.faction, 'platform', home.x + Math.cos(a) * 420, home.y + Math.sin(a) * 420);
    for (let t = 1; t < defense.platformTier; t++) applyTier(w, platform);
  }
  if (defense.repairDock > 0) spawnStructure(w, defender.faction, 'repairDock', home.x - 260, home.y + 260);
  if (defense.ionCannon > 0) spawnStructure(w, defender.faction, 'ionCannon', home.x - 260, home.y - 260);
  placeShips(w, defender, home.x + 550, home.y, 0);
  placeShips(w, attacker, BATTLE_W - 500, home.y, Math.PI);
  return w;
}

/**
 * The defender holds while it keeps a station or a ship; the attacker while it keeps a ship.
 * Returns null while both sides still stand.
 */
export function battleOutcome(world: World, attacker: FactionId, defender: FactionId): BattleResult | null {
  let att = false;
  let def = stationOf(world, defender) != null;
  for (const e of world.entities.values()) {
    if (e.kind !== 'ship') continue;
    if (e.faction === attacker) att = true;
    else if (e.faction === defender) def = true;
  }
  if (!att) return 'defender';
  if (!def) return 'attacker';
  return null;
}

/** Ships that can go back to the galaxy map: hangar craft stay behind, heroes are counted apart. */
export function survivors(world: World, faction: FactionId): ShipCounts {
  const out: ShipCounts = {};
  for (const e of world.entities.values()) {
    if (e.kind !== 'ship' || e.faction !== faction || e.hero) continue;
    const cls = e.type as ShipClass;
    if (SHIPS[cls].source === 'shipyard') out[cls] = (out[cls] ?? 0) + 1;
  }
  return out;
}

export function heroSurvivors(world: World, faction: FactionId): HeroId[] {
  const out: HeroId[] = [];
  for (const e of world.entities.values()) if (e.hero && e.faction === faction) out.push(e.hero);
  return out;
}

export function defenseLeft(world: World, faction: FactionId): PlanetDefense {
  const station = stationOf(world, faction);
  const left = { ...NO_DEFENSE, station: station?.level ?? 0 };
  for (const e of world.entities.values()) {
    if (e.faction !== faction || e.kind !== 'structure') continue;
    if (e.type === 'platform') {
      left.platforms++;
      left.platformTier = Math.max(left.platformTier, e.level);
    }
    if (e.type === 'mine') left.mines++;
    if (e.type === 'repairDock') left.repairDock++;
    if (e.type === 'ionCannon') left.ionCannon++;
  }
  return left;
}

/** Hull and shields a faction still has in the battle: what the battle prompt compares. */
export function sideStrength(world: World, faction: FactionId): number {
  let total = 0;
  for (const e of world.entities.values()) if (e.faction === faction) total += e.hull + e.shield;
  return Math.round(total);
}

/**
 * Once per second, idle warships of each faction attack-move towards the nearest enemy
 * they can see; seeing none, towards the enemy station, or else to scout the middle.
 */
export function updateBattleAI(world: World, factions: FactionId[], dt: number): void {
  if (Math.floor(world.time) === Math.floor(world.time - dt)) return;
  for (const faction of factions) {
    const idle: Entity[] = [];
    for (const e of world.entities.values()) {
      if (e.kind === 'ship' && e.faction === faction && !e.order && SHIPS[e.type as ShipClass].source === 'shipyard') idle.push(e);
    }
    if (idle.length === 0) continue;
    const c = { x: idle.reduce((s, e) => s + e.x, 0) / idle.length, y: idle.reduce((s, e) => s + e.y, 0) / idle.length };
    let target: { x: number; y: number } | null = null;
    for (const e of world.entities.values()) {
      if (e.faction !== faction && isVisible(world, faction, e) && (!target || dist(e, c) < dist(target, c))) target = e;
    }
    if (!target) {
      const station = [...world.factions.keys()].filter((f) => f !== faction).map((f) => stationOf(world, f)).find(Boolean);
      const center = { x: world.width / 2, y: world.height / 2 };
      target = station ?? (stationOf(world, faction) || dist(c, center) < 200 ? null : center);
    }
    if (target) issueMove(world, idle.map((e) => e.id), target.x, target.y, true);
  }
}
