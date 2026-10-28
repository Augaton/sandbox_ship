// A space battle fought over a planet of the galaxy map: built from fleets and planet
// defenses, run by the tactical simulation, and read back as survivors.
import { SHIPS, SHIP_CLASSES, type FactionId, type ShipClass } from '../data/units';
import { formationOffsets, issueMove } from './movement';
import {
  addFaction, addField, createWorld, dist, spawnShip, spawnStation, spawnStructure, stationOf,
  type Entity, type World,
} from './world';

export type ShipCounts = Partial<Record<ShipClass, number>>;

export interface PlanetDefense {
  /** Station level, 0 without a station. */
  station: number;
  platforms: number;
  mines: number;
}

/** The attacker took the planet, the defender held it, or the attacker fell back. */
export type BattleResult = 'attacker' | 'defender' | 'retreat';

export const BATTLE_W = 4000;
export const BATTLE_H = 2600;

export function countShips(ships: ShipCounts): number {
  return SHIP_CLASSES.reduce((n, c) => n + (ships[c] ?? 0), 0);
}

/** Total build cost: a rough measure of fighting strength. */
export function fleetValue(ships: ShipCounts): number {
  return SHIP_CLASSES.reduce((v, c) => v + (ships[c] ?? 0) * SHIPS[c].cost, 0);
}

function placeShips(world: World, faction: FactionId, ships: ShipCounts, x: number, y: number, angle: number): void {
  const list = SHIP_CLASSES.flatMap((c) => Array<ShipClass>(ships[c] ?? 0).fill(c));
  // Heavy ships in the middle, as for a move order.
  list.sort((a, b) => SHIPS[b].radius - SHIPS[a].radius);
  const offsets = formationOffsets(list.length, 90).sort((a, b) => Math.hypot(a.x, a.y) - Math.hypot(b.x, b.y));
  list.forEach((cls, i) => spawnShip(world, faction, cls, x + offsets[i].x, y + offsets[i].y, angle));
}

/** Defender on the left around its station, attacker arriving from the right. */
export function createPlanetBattle(
  attacker: { faction: FactionId; ships: ShipCounts },
  defender: { faction: FactionId; ships: ShipCounts },
  defense: PlanetDefense,
  seed = 1,
): World {
  const w = createWorld(BATTLE_W, BATTLE_H, seed);
  addFaction(w, defender.faction, 0);
  addFaction(w, attacker.faction, 0);
  const home = { x: 800, y: BATTLE_H / 2 };
  for (const [x, y, r] of [[600, 550, 150], [650, 2050, 150], [1900, 450, 170], [1900, 2150, 170], [3100, 800, 150], [3100, 1800, 150]]) {
    addField(w, x, y, r);
  }
  if (defense.station > 0) spawnStation(w, defender.faction, home.x, home.y, defense.station);
  const fields = [...w.fields].sort((a, b) => dist(a, home) - dist(b, home));
  for (const field of fields.slice(0, defense.mines)) {
    const mine = spawnStructure(w, defender.faction, 'mine', field.x, field.y);
    mine.fieldId = field.id;
    field.mineId = mine.id;
  }
  for (let i = 0; i < defense.platforms; i++) {
    const a = (i - (defense.platforms - 1) / 2) * 0.5;
    spawnStructure(w, defender.faction, 'platform', home.x + Math.cos(a) * 420, home.y + Math.sin(a) * 420);
  }
  placeShips(w, defender.faction, defender.ships, home.x + 550, home.y, 0);
  placeShips(w, attacker.faction, attacker.ships, BATTLE_W - 500, home.y, Math.PI);
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

/** Ships that can go back to the galaxy map: hangar craft stay behind. */
export function survivors(world: World, faction: FactionId): ShipCounts {
  const out: ShipCounts = {};
  for (const e of world.entities.values()) {
    if (e.kind !== 'ship' || e.faction !== faction) continue;
    const cls = e.type as ShipClass;
    if (SHIPS[cls].source === 'shipyard') out[cls] = (out[cls] ?? 0) + 1;
  }
  return out;
}

export function defenseLeft(world: World, faction: FactionId): PlanetDefense {
  const station = stationOf(world, faction);
  let platforms = 0;
  let mines = 0;
  for (const e of world.entities.values()) {
    if (e.faction !== faction) continue;
    if (e.type === 'platform') platforms++;
    if (e.type === 'mine') mines++;
  }
  return { station: station?.level ?? 0, platforms, mines };
}

/** Hull and shields a faction still has in the battle: what the battle prompt compares. */
export function sideStrength(world: World, faction: FactionId): number {
  let total = 0;
  for (const e of world.entities.values()) if (e.faction === faction) total += e.hull + e.shield;
  return Math.round(total);
}

/** Once per second, idle warships of each faction attack-move towards the nearest enemy. */
export function updateBattleAI(world: World, factions: FactionId[], dt: number): void {
  if (Math.floor(world.time) === Math.floor(world.time - dt)) return;
  for (const faction of factions) {
    const idle: Entity[] = [];
    for (const e of world.entities.values()) {
      if (e.kind === 'ship' && e.faction === faction && !e.order && SHIPS[e.type as ShipClass].source === 'shipyard') idle.push(e);
    }
    if (idle.length === 0) continue;
    const c = { x: idle.reduce((s, e) => s + e.x, 0) / idle.length, y: idle.reduce((s, e) => s + e.y, 0) / idle.length };
    let target: Entity | null = null;
    for (const e of world.entities.values()) {
      if (e.faction !== faction && (!target || dist(e, c) < dist(target, c))) target = e;
    }
    if (target) issueMove(world, idle.map((e) => e.id), target.x, target.y, true);
  }
}
