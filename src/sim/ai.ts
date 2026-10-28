import { heroesOf } from '../data/heroes';
import { MAX_TECH_LEVEL, type TechId } from '../data/research';
import { STATION, type BuildableType } from '../data/structures';
import { FACTIONS, SHIPS, SHIP_CLASSES, shipCost, unitValue, type ShipClass } from '../data/units';
import { issueMove } from './movement';
import { buildZones, canBuildHero, enqueueHero, enqueueShip, enqueueUpgrade, placeStructure, startResearch } from './structures';
import { isVisible } from './vision';
import {
  DIFFICULTIES, dist, hasHardpoint, rand, stationOf,
  type AIGoal, type Entity, type FactionState, type Intel, type Result, type World,
} from './world';

const MAX_MINES = 4;
const MAX_OUTPOSTS = 2;
/** Biggest wave the AI waits for, in ship value. */
const MAX_WAVE = 9000;
/** Seconds after which the AI attacks with what it has. */
const PRESSURE_TIME = 1200;
/** Refused for want of a ship on the spot: the AI sends one. */
const UNHELD = 'Un vaisseau doit tenir la position';
/** Relative odds of each ship class once unlocked, before adapting to the enemy. */
const SHIP_WEIGHTS: Partial<Record<ShipClass, number>> = {
  scout: 1, bomber: 1, support: 1, frigate: 4, torpedo: 2, battlecruiser: 3, dreadnought: 2, titan: 1,
};
/** Goal failures worth waiting through: the AI keeps the goal instead of picking another. */
const WAIT = new Set(['Crédits insuffisants', 'Escorte en route']);

const fail = (reason: string): Result => ({ ok: false, reason });

function count(world: World, f: FactionState, type: string): number {
  let n = 0;
  for (const e of world.entities.values()) if (e.faction === f.id && e.type === type) n++;
  return n;
}

function nearestEnemyStation(world: World, f: FactionState, from: Entity): Entity | null {
  let best: Entity | null = null;
  for (const other of world.factions.values()) {
    if (other.id === f.id) continue;
    const s = stationOf(world, other.id);
    if (s && (!best || dist(from, s) < dist(from, best))) best = s;
  }
  return best;
}

/** Warships that answer to the AI: built ones, not hangar craft, not those falling back. */
function warships(world: World, f: FactionState): Entity[] {
  const out: Entity[] = [];
  for (const e of world.entities.values()) {
    if (e.faction === f.id && e.kind === 'ship' && !e.launched && !e.retreating && !e.pilot) out.push(e);
  }
  return out;
}

const shipValue = (e: Entity) => unitValue(e.type as ShipClass) * (e.hero ? 4 : 1);

/** What the enemy fields, from what the AI sees now; older sightings fade. */
function updateIntel(world: World, f: FactionState): Intel {
  const seen: Intel = { small: 0, medium: 0, large: 0, structures: 0, army: 0 };
  for (const e of world.entities.values()) {
    if (e.faction === f.id || !isVisible(world, f.id, e)) continue;
    if (e.kind === 'ship' && !e.launched) {
      seen[e.size === 'small' ? 'small' : e.size === 'medium' ? 'medium' : 'large']++;
      seen.army += shipValue(e);
    } else if (e.kind === 'ship') {
      seen.small++;
    } else if (e.kind === 'structure' && e.weapons.length > 0) {
      seen.structures++;
    }
  }
  const old = f.ai!.intel;
  for (const k of Object.keys(seen) as (keyof Intel)[]) old[k] = Math.max(seen[k], old[k] * 0.97);
  return old;
}

/** Ship weights tilted towards what counters the enemy the AI has seen. */
export function pickShip(world: World, f: FactionState, station: Entity): ShipClass | null {
  if (!hasHardpoint(world, station.id, 'shipyard')) return null;
  const intel = f.ai!.intel;
  const weights = { ...SHIP_WEIGHTS };
  // Swarms of small craft: flak frigates. Big hulls: torpedoes and bombers. Turrets: bombers.
  if (intel.small >= 6) weights.frigate! += 4;
  if (intel.large >= 2) {
    weights.torpedo! += 4;
    weights.bomber! += 3;
  }
  if (intel.structures >= 3) weights.bomber! += 3;
  // A unique capital ship only with a fleet around it and a quarter of it paid: saving up
  // for it from nothing would leave the AI without ships for minutes.
  const fleet = warships(world, f).length;
  const fielded = (c: ShipClass) => count(world, f, c) > 0 || f.queue.some((q) => q.kind === 'ship' && q.cls === c);
  const options = SHIP_CLASSES.filter((c) => weights[c] && station.level >= SHIPS[c].minStationLevel
    && (!SHIPS[c].unique || (fleet >= 8 && f.credits >= shipCost(c, f.id) / 4 && !fielded(c))));
  const total = options.reduce((t, c) => t + weights[c]!, 0);
  let r = rand(world) * total;
  for (const c of options) {
    r -= weights[c]!;
    if (r < 0) return c;
  }
  return null;
}

/** Fields the AI could mine now: near its station, or with an idle ship to send there. */
function mineableField(world: World, f: FactionState): boolean {
  const free = world.fields.filter((fl) => fl.mineId == null);
  if (free.some((fl) => inZones(world, f, fl))) return true;
  return free.length > 0 && warships(world, f).some((e) => !e.order);
}

function inZones(world: World, f: FactionState, fl: { x: number; y: number; radius: number }): boolean {
  return buildZones(world, f.id, 'mine').some((z) => dist(z, fl) <= z.r + fl.radius);
}

/** Free fields beyond the build areas: where an outpost would open new ground. */
function farFields(world: World, f: FactionState, station: Entity) {
  return world.fields.filter((fl) => fl.mineId == null && !inZones(world, f, fl)).sort((a, b) => dist(a, station) - dist(b, station));
}

/** Sends the nearest idle warship to hold a spot, unless one is on its way already. */
function escortTo(world: World, f: FactionState, at: { x: number; y: number }, near: number): Result {
  const ships = warships(world, f);
  const heading = ships.some((e) => e.order && e.order.type !== 'attack' && Math.hypot(e.order.x - at.x, e.order.y - at.y) < near);
  if (heading) return fail('Escorte en route');
  const escort = ships.filter((e) => !e.order).sort((a, b) => dist(a, at) - dist(b, at))[0];
  if (!escort) return fail('Aucune escorte');
  issueMove(world, [escort.id], at.x, at.y);
  return fail('Escorte en route');
}

function chooseGoal(world: World, f: FactionState, station: Entity, defensive: boolean): AIGoal | null {
  const mines = count(world, f, 'mine');
  if (mines < Math.min(MAX_MINES, station.level + 1) && mineableField(world, f)) return { kind: 'mine' };
  // A first squadron early: it escorts the miners and scouts the map.
  const fleet = warships(world, f).length;
  if (fleet === 0 && hasHardpoint(world, station.id, 'shipyard')) return { kind: 'ship', cls: 'scout' };
  // Outmatched: dig in before anything else.
  if (defensive && count(world, f, 'platform') < station.level * 2) return { kind: 'platform' };
  if (station.level < STATION.levels.length && mines >= station.level + 1 && rand(world) < 0.5) {
    return { kind: 'upgrade' };
  }
  if (count(world, f, 'platform') < station.level && rand(world) < 0.25) return { kind: 'platform' };
  // Mid game, expand: an outpost by a free field beyond reach, once the mines nearby are taken.
  if (station.level >= 2 && mines >= 3 && count(world, f, 'outpost') < MAX_OUTPOSTS && fleet >= 2 && rand(world) < 0.15 && farFields(world, f, station).length > 0) {
    return { kind: 'outpost' };
  }
  if (fleet >= 3 && count(world, f, 'repairDock') === 0 && rand(world) < 0.1) return { kind: 'structure', type: 'repairDock' };
  if (station.level >= 3 && count(world, f, 'ionCannon') === 0 && rand(world) < 0.15) return { kind: 'structure', type: 'ionCannon' };
  const tech = !f.researching && rand(world) < 0.2 ? pickTech(f, station) : null;
  if (tech) return { kind: 'research', tech };
  // Now and then, a hero: whichever is available, saving up for it if needed.
  const hero = heroesOf(f.id).find((h) => {
    const r = canBuildHero(world, f.id, h.id);
    return r.ok || r.reason === 'Crédits insuffisants';
  });
  if (hero && rand(world) < 0.12) return { kind: 'hero', hero: hero.id };
  const cls = pickShip(world, f, station);
  return cls ? { kind: 'ship', cls } : null;
}

/** Nearest free field it may mine; far fields get an escort sent first. */
function placeMine(world: World, f: FactionState, station: Entity): Result {
  const free = world.fields.filter((fl) => fl.mineId == null).sort((a, b) => dist(a, station) - dist(b, station));
  for (const field of free.slice(0, 3)) {
    const r = placeStructure(world, f.id, 'mine', field.x, field.y);
    if (r.ok || r.reason === 'Crédits insuffisants') return r;
    if (r.reason !== UNHELD) continue;
    const sent = escortTo(world, f, field, field.radius);
    if (sent.ok || sent.reason === 'Escorte en route') return sent;
  }
  return fail('Aucun champ accessible');
}

/** An outpost on the station's side of the nearest far field, which then lies in its zone. */
function placeOutpost(world: World, f: FactionState, station: Entity): Result {
  const field = farFields(world, f, station)[0];
  if (!field) return fail('Aucun champ accessible');
  const d = dist(field, station) || 1;
  const back = field.radius + 90;
  for (const turn of [0, 0.5, -0.5]) {
    const a = Math.atan2(station.y - field.y, station.x - field.x) + turn;
    const at = { x: field.x + Math.cos(a) * back, y: field.y + Math.sin(a) * back };
    if (d < back) break;
    const r = placeStructure(world, f.id, 'outpost', at.x, at.y);
    if (r.ok || r.reason === 'Crédits insuffisants') return r;
    if (r.reason === UNHELD) return escortTo(world, f, at, 150);
  }
  return fail('Aucun emplacement');
}

/** Least researched tech the station allows, combat techs first. */
function pickTech(f: FactionState, station: Entity): TechId | null {
  const order: TechId[] = ['weapons', 'armor', 'shields', 'engines', 'mining', 'hangars'];
  const open = order.filter((t) => f.research[t] < MAX_TECH_LEVEL && station.level > f.research[t]);
  return open.sort((a, b) => f.research[a] - f.research[b])[0] ?? null;
}

/** Defensive structures go on the side of the station facing the enemy. */
function placeDefense(world: World, f: FactionState, station: Entity, toward: Entity | null, type: BuildableType): Result {
  const base = toward ? Math.atan2(toward.y - station.y, toward.x - station.x) : 0;
  let r: Result = fail('Aucun emplacement');
  for (let i = 0; i < 6 && !r.ok; i++) {
    const a = base + (rand(world) - 0.5) * Math.PI;
    const d = STATION.radius + 150 + rand(world) * 300;
    r = placeStructure(world, f.id, type, station.x + Math.cos(a) * d, station.y + Math.sin(a) * d);
    if (!r.ok && r.reason === 'Crédits insuffisants') break;
  }
  return r;
}

/** Tries to buy the goal. Returns true when the goal is done or can no longer be reached. */
function pursue(world: World, f: FactionState, station: Entity, goal: AIGoal, enemy: Entity | null): boolean {
  if ((goal.kind === 'ship' || goal.kind === 'hero') && f.queue.length >= 2) return false;
  const r =
    goal.kind === 'mine' ? placeMine(world, f, station)
    : goal.kind === 'outpost' ? placeOutpost(world, f, station)
    : goal.kind === 'upgrade' ? enqueueUpgrade(world, f.id)
    : goal.kind === 'platform' ? placeDefense(world, f, station, enemy, 'platform')
    : goal.kind === 'structure' ? placeDefense(world, f, station, enemy, goal.type)
    : goal.kind === 'research' ? startResearch(world, f.id, goal.tech)
    : goal.kind === 'hero' ? enqueueHero(world, f.id, goal.hero)
    : enqueueShip(world, f.id, goal.cls);
  return r.ok || !WAIT.has(r.reason);
}

/** Badly damaged warships fall back to a repair dock, and rejoin once mended. */
function retreatDamaged(world: World, f: FactionState): void {
  const docks = [...world.entities.values()].filter((e) => e.faction === f.id && e.type === 'repairDock' && e.build >= 1);
  const mends = docks.length > 0 || FACTIONS[f.id].trait.hullRegen > 0;
  // Ships on a wave hold on longer, and late in the game not at all: a wave that keeps
  // breaking off never takes a station.
  const waveLimit = world.time > PRESSURE_TIME / 2 ? 0 : 0.15;
  for (const e of world.entities.values()) {
    if (e.faction !== f.id || e.kind !== 'ship' || e.launched || e.size === 'small' || e.pilot) continue;
    const limit = e.order?.type === 'attackMove' ? waveLimit : 0.3;
    if (e.retreating && e.hull >= e.maxHull * 0.85) {
      e.retreating = false;
      e.order = null;
    } else if (!e.retreating && mends && e.hull < e.maxHull * limit) {
      e.retreating = true;
      const to = docks.sort((a, b) => dist(a, e) - dist(b, e))[0] ?? stationOf(world, f.id);
      if (to) issueMove(world, [e.id], to.x, to.y);
    }
  }
}

/**
 * Defends home when attacked; otherwise sends idle warships at the nearest enemy station
 * once the wave is big enough, or right away when clearly stronger than what it has seen.
 * Returns whether it is outmatched.
 */
function command(world: World, f: FactionState, station: Entity, enemy: Entity | null): boolean {
  const ships = warships(world, f);
  const idle = ships.filter((e) => !e.order);
  let threatened = false;
  for (const e of world.entities.values()) {
    if (e.faction !== f.id && e.kind === 'ship' && dist(e, station) < STATION.constructionRadius && isVisible(world, f.id, e)) {
      threatened = true;
      break;
    }
  }
  const intel = updateIntel(world, f);
  const army = ships.reduce((v, e) => v + shipValue(e), 0);
  const outmatched = intel.army > 0 && army < intel.army * 0.7;
  if (threatened) {
    // Ships already out on a wave keep going; only the home fleet defends.
    issueMove(world, idle.map((e) => e.id), station.x, station.y, true);
    return outmatched;
  }
  const value = idle.reduce((v, e) => v + shipValue(e), 0);
  // As the game drags on, the AI settles for smaller waves and a thinner edge: no endless standoff.
  const pressure = Math.min(1, world.time / PRESSURE_TIME);
  const stronger = intel.army > 0 && army > intel.army * (1.4 - 0.3 * pressure) && value >= 1500;
  if (enemy && !outmatched && (value >= f.ai!.waveValue * (1 - 0.4 * pressure) || stronger)) {
    issueMove(world, idle.map((e) => e.id), enemy.x, enemy.y, true);
    f.ai!.waveValue = Math.min(f.ai!.waveValue * 1.2, MAX_WAVE);
  }
  return outmatched;
}

function think(world: World, f: FactionState): void {
  const ai = f.ai!;
  const station = stationOf(world, f.id);
  if (!station) return;
  const enemy = nearestEnemyStation(world, f, station);
  retreatDamaged(world, f);
  const defensive = command(world, f, station, enemy);
  ai.goal ??= chooseGoal(world, f, station, defensive);
  if (ai.goal && pursue(world, f, station, ai.goal, enemy)) ai.goal = null;
}

export function updateAI(world: World, dt: number): void {
  for (const f of world.factions.values()) {
    if (!f.ai) continue;
    f.ai.timer -= dt;
    if (f.ai.timer > 0) continue;
    f.ai.timer = DIFFICULTIES[f.ai.difficulty].think;
    think(world, f);
  }
}
