import { MAX_TECH_LEVEL, type TechId } from '../data/research';
import { SHIPS, SHIP_CLASSES, type ShipClass } from '../data/units';
import { STATION, type BuildableType } from '../data/structures';
import { issueMove } from './movement';
import { isVisible } from './vision';
import { heroesOf } from '../data/heroes';
import { canBuildHero, enqueueHero, enqueueShip, enqueueUpgrade, placeStructure, startResearch } from './structures';
import {
  dist, hasHardpoint, rand, stationOf,
  type AIGoal, type Entity, type FactionState, type Result, type World,
} from './world';

const THINK_INTERVAL = 1.5;
const MAX_MINES = 4;
/** Relative odds of picking each ship class once unlocked. */
const SHIP_WEIGHTS: Partial<Record<ShipClass, number>> = {
  scout: 1, support: 1, frigate: 4, torpedo: 2, battlecruiser: 3, dreadnought: 2,
};

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

function pickShip(world: World, station: Entity): ShipClass | null {
  if (!hasHardpoint(world, station.id, 'shipyard')) return null;
  const options = SHIP_CLASSES.filter((c) => SHIP_WEIGHTS[c] && station.level >= SHIPS[c].minStationLevel);
  const total = options.reduce((t, c) => t + SHIP_WEIGHTS[c]!, 0);
  let r = rand(world) * total;
  for (const c of options) {
    r -= SHIP_WEIGHTS[c]!;
    if (r < 0) return c;
  }
  return null;
}

function chooseGoal(world: World, f: FactionState, station: Entity): AIGoal | null {
  const mines = count(world, f, 'mine');
  const freeField = world.fields.some((fl) => fl.mineId == null);
  if (freeField && mines < Math.min(MAX_MINES, station.level + 1)) return { kind: 'mine' };
  if (station.level < STATION.levels.length && mines >= station.level + 1 && rand(world) < 0.5) {
    return { kind: 'upgrade' };
  }
  if (count(world, f, 'platform') < station.level && rand(world) < 0.25) return { kind: 'platform' };
  if (count(world, f, 'repairDock') === 0 && rand(world) < 0.1) return { kind: 'structure', type: 'repairDock' };
  if (station.level >= 3 && count(world, f, 'ionCannon') === 0 && rand(world) < 0.15) return { kind: 'structure', type: 'ionCannon' };
  const tech = !f.researching && rand(world) < 0.2 ? pickTech(f, station) : null;
  if (tech) return { kind: 'research', tech };
  // Now and then, a hero: whichever is available, saving up for it if needed.
  const hero = heroesOf(f.id).find((h) => {
    const r = canBuildHero(world, f.id, h.id);
    return r.ok || r.reason === 'Crédits insuffisants';
  });
  if (hero && rand(world) < 0.12) return { kind: 'hero', hero: hero.id };
  const cls = pickShip(world, station);
  return cls ? { kind: 'ship', cls } : null;
}

function placeMine(world: World, f: FactionState, station: Entity): Result {
  const free = world.fields
    .filter((fl) => fl.mineId == null)
    .sort((a, b) => dist(a, station) - dist(b, station))[0];
  return free ? placeStructure(world, f.id, 'mine', free.x, free.y) : { ok: false, reason: 'Aucun champ libre' };
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
  let r: Result = { ok: false, reason: 'Aucun emplacement' };
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
    : goal.kind === 'upgrade' ? enqueueUpgrade(world, f.id)
    : goal.kind === 'platform' ? placeDefense(world, f, station, enemy, 'platform')
    : goal.kind === 'structure' ? placeDefense(world, f, station, enemy, goal.type)
    : goal.kind === 'research' ? startResearch(world, f.id, goal.tech)
    : goal.kind === 'hero' ? enqueueHero(world, f.id, goal.hero)
    : enqueueShip(world, f.id, goal.cls);
  // Short on credits: keep saving for it.
  return r.ok || r.reason !== 'Crédits insuffisants';
}

function command(world: World, f: FactionState, station: Entity, enemy: Entity | null): void {
  const idle: Entity[] = [];
  let threatened = false;
  for (const e of world.entities.values()) {
    if (e.faction === f.id) {
      if (e.kind === 'ship' && !e.order && SHIPS[e.type as ShipClass].source === 'shipyard') idle.push(e);
    } else if (e.kind === 'ship' && dist(e, station) < STATION.constructionRadius && isVisible(world, f.id, e)) {
      threatened = true;
    }
  }
  if (threatened) {
    // Ships already out on a wave keep going; only the home fleet defends.
    issueMove(world, idle.map((e) => e.id), station.x, station.y, true);
    return;
  }
  const value = idle.reduce((v, e) => v + SHIPS[e.type as ShipClass].cost, 0);
  if (enemy && value >= f.ai!.waveValue) {
    issueMove(world, idle.map((e) => e.id), enemy.x, enemy.y, true);
    f.ai!.waveValue = Math.min(f.ai!.waveValue * 1.25, 15000);
  }
}

function think(world: World, f: FactionState): void {
  const ai = f.ai!;
  const station = stationOf(world, f.id);
  if (!station) return;
  const enemy = nearestEnemyStation(world, f, station);
  command(world, f, station, enemy);
  ai.goal ??= chooseGoal(world, f, station);
  if (ai.goal && pursue(world, f, station, ai.goal, enemy)) ai.goal = null;
}

export function updateAI(world: World, dt: number): void {
  for (const f of world.factions.values()) {
    if (!f.ai) continue;
    f.ai.timer -= dt;
    if (f.ai.timer > 0) continue;
    f.ai.timer = THINK_INTERVAL;
    think(world, f);
  }
}
