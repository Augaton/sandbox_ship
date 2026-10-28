// Galaxy map simulation: planets, fleets travelling along lanes, production, and the
// battles that start when two factions meet over a planet. Pure TypeScript, no Phaser.
import { CAPITALS, GALAXY_RULES, LANES, PLANETS } from '../data/galaxy';
import { BUILDABLES, MAX_QUEUE, STATION } from '../data/structures';
import { SHIPS, SHIP_CLASSES, type FactionId, type ShipClass } from '../data/units';
import {
  battleOutcome, countShips, createPlanetBattle, defenseLeft, fleetValue, survivors, updateBattleAI,
  type BattleResult, type PlanetDefense, type ShipCounts,
} from './battle';
import { step } from './step';
import { dist, type Result, type World } from './world';

export type GalaxyBuild =
  | { kind: 'ship'; cls: ShipClass }
  | { kind: 'station' }
  | { kind: 'mine' }
  | { kind: 'platform' };

export type GalaxyQueueItem = GalaxyBuild & { remaining: number; total: number; cost: number };

export interface Planet {
  id: number;
  name: string;
  x: number;
  y: number;
  income: number;
  mineSlots: number;
  owner: FactionId | null;
  /** Station level, 0 without a station. */
  station: number;
  mines: number;
  platforms: number;
  queue: GalaxyQueueItem[];
  /** A lone fleet taking the planet over. */
  occupation: { faction: FactionId; remaining: number } | null;
}

export interface Fleet {
  id: number;
  faction: FactionId;
  ships: ShipCounts;
  /** Planet the fleet is docked at, null while travelling. */
  at: number | null;
  /** Planet last left (or docked at). */
  from: number;
  /** Next planet while travelling. */
  to: number | null;
  /** Planets still to visit after `to`. */
  path: number[];
  /** 0..1 along the lane from `from` to `to`. */
  progress: number;
  /** Planet the fleet came from before docking: where it falls back after a lost battle. */
  came: number;
  arrivedAt: number;
  battleId: number | null;
}

/** `pending` waits for the player to choose; `tactical` is being played in the battle view. */
export type BattleStatus = 'pending' | 'auto' | 'tactical';

export interface GalaxyBattle {
  id: number;
  planetId: number;
  attacker: FactionId;
  defender: FactionId;
  /** Where the attacker falls back. */
  from: number;
  world: World;
  status: BattleStatus;
  elapsed: number;
}

export interface GalaxyFaction {
  id: FactionId;
  credits: number;
  /** `want` is the ship class the AI saves up for. */
  ai: { timer: number; want: ShipClass | null } | null;
  eliminated: boolean;
}

export type GalaxyEvent =
  | { type: 'battle'; battleId: number }
  | { type: 'battleEnd'; planetId: number; attacker: FactionId; defender: FactionId; result: BattleResult }
  | { type: 'captured'; planetId: number; faction: FactionId; from: FactionId | null }
  | { type: 'built'; planetId: number; faction: FactionId; what: string }
  | { type: 'victory'; faction: FactionId };

export interface Galaxy {
  time: number;
  player: FactionId;
  planets: Planet[];
  lanes: [number, number][];
  fleets: Map<number, Fleet>;
  factions: Map<FactionId, GalaxyFaction>;
  battles: GalaxyBattle[];
  events: GalaxyEvent[];
  nextId: number;
  seed: number;
  winner: FactionId | null;
}

const fail = (reason: string): Result => ({ ok: false, reason });
const OK: Result = { ok: true };

/** Deterministic random number in [0, 1) (Park-Miller). */
export function galaxyRand(g: Galaxy): number {
  g.seed = (g.seed * 16807) % 2147483647;
  return (g.seed - 1) / 2147483646;
}

/** Both capitals start with a level 1 station, a mine and a small fleet. */
export function createGalaxy(player: FactionId, enemy: FactionId, enemyAI = true, seed = 7): Galaxy {
  const g: Galaxy = {
    time: 0, player,
    planets: PLANETS.map((p, id) => ({ ...p, id, owner: null, station: 0, mines: 0, platforms: 0, queue: [], occupation: null })),
    lanes: LANES, fleets: new Map(), factions: new Map(), battles: [], events: [],
    nextId: 1, seed, winner: null,
  };
  const sides: [FactionId, number, boolean][] = [[player, CAPITALS[0], false], [enemy, CAPITALS[1], enemyAI]];
  for (const [faction, capital, ai] of sides) {
    g.factions.set(faction, { id: faction, credits: GALAXY_RULES.startCredits, ai: ai ? { timer: 0, want: null } : null, eliminated: false });
    Object.assign(g.planets[capital], { owner: faction, station: 1, mines: 1 });
    addShipsAt(g, faction, capital, { frigate: 2, scout: 2 });
  }
  return g;
}

export function neighbours(g: Galaxy, planetId: number): number[] {
  const out: number[] = [];
  for (const [a, b] of g.lanes) {
    if (a === planetId) out.push(b);
    else if (b === planetId) out.push(a);
  }
  return out;
}

/** Shortest route by lane length (Dijkstra). Returns the planets to visit after `from`, or null. */
export function findPath(g: Galaxy, from: number, to: number): number[] | null {
  const best = new Map<number, number>([[from, 0]]);
  const prev = new Map<number, number>();
  const open = new Set([from]);
  while (open.size > 0) {
    let cur = -1;
    for (const id of open) if (cur < 0 || best.get(id)! < best.get(cur)!) cur = id;
    open.delete(cur);
    if (cur === to) break;
    for (const n of neighbours(g, cur)) {
      const d = best.get(cur)! + dist(g.planets[cur], g.planets[n]);
      if (d < (best.get(n) ?? Infinity)) {
        best.set(n, d);
        prev.set(n, cur);
        open.add(n);
      }
    }
  }
  if (from === to || !prev.has(to)) return null;
  const path = [to];
  while (prev.get(path[0]) !== from) path.unshift(prev.get(path[0])!);
  return path;
}

/** Map speed of a fleet: as fast as its slowest ship. */
export function fleetSpeed(ships: ShipCounts): number {
  const slowest = Math.min(...SHIP_CLASSES.filter((c) => ships[c]).map((c) => SHIPS[c].speed));
  return 30 + slowest * 0.5;
}

export function fleetsAt(g: Galaxy, planetId: number, faction?: FactionId): Fleet[] {
  return [...g.fleets.values()].filter((f) => f.at === planetId && f.battleId == null && (!faction || f.faction === faction));
}

export function addShipsAt(g: Galaxy, faction: FactionId, planetId: number, ships: ShipCounts): Fleet | null {
  if (countShips(ships) === 0) return null;
  const fleet = fleetsAt(g, planetId, faction)[0];
  if (fleet) {
    for (const c of SHIP_CLASSES) if (ships[c]) fleet.ships[c] = (fleet.ships[c] ?? 0) + ships[c]!;
    return fleet;
  }
  const f: Fleet = {
    id: g.nextId++, faction, ships: { ...ships }, at: planetId, from: planetId, to: null, path: [],
    progress: 0, came: planetId, arrivedAt: g.time, battleId: null,
  };
  g.fleets.set(f.id, f);
  return f;
}

/** Sends `ships` (all of the fleet by default) towards `dest`; a partial order splits the fleet. */
export function sendFleet(g: Galaxy, fleetId: number, dest: number, ships?: ShipCounts): Result {
  const fleet = g.fleets.get(fleetId);
  if (!fleet || fleet.at == null || fleet.battleId != null) return fail('Flotte indisponible');
  const path = findPath(g, fleet.at, dest);
  if (!path) return fail('Aucune route');
  let moving = fleet;
  if (ships) {
    for (const c of SHIP_CLASSES) if ((ships[c] ?? 0) > (fleet.ships[c] ?? 0)) return fail('Vaisseaux insuffisants');
    if (countShips(ships) === 0) return fail('Aucun vaisseau choisi');
    const rest: ShipCounts = {};
    for (const c of SHIP_CLASSES) {
      const left = (fleet.ships[c] ?? 0) - (ships[c] ?? 0);
      if (left > 0) rest[c] = left;
    }
    if (countShips(rest) > 0) {
      fleet.ships = rest;
      moving = { ...fleet, id: g.nextId++, ships: { ...ships }, path: [] };
      g.fleets.set(moving.id, moving);
    }
  }
  moving.from = moving.at!;
  moving.at = null;
  moving.to = path[0];
  moving.path = path.slice(1);
  moving.progress = 0;
  return OK;
}

function hostileTo(g: Galaxy, planet: Planet, faction: FactionId): boolean {
  const owned = planet.owner != null && planet.owner !== faction;
  return owned || fleetsAt(g, planet.id).some((f) => f.faction !== faction);
}

/** Fleets pass through friendly and neutral planets, and stop at enemy ones. */
function moveFleets(g: Galaxy, dt: number): void {
  for (const f of g.fleets.values()) {
    if (f.to == null) continue;
    const a = g.planets[f.from];
    const b = g.planets[f.to];
    f.progress += (fleetSpeed(f.ships) * dt) / dist(a, b);
    if (f.progress < 1) continue;
    const arrived = f.to;
    f.came = f.from;
    f.from = arrived;
    f.progress = 0;
    if (f.path.length > 0 && !hostileTo(g, b, f.faction)) {
      f.to = f.path.shift()!;
      continue;
    }
    f.to = null;
    f.path = [];
    f.at = arrived;
    f.arrivedAt = g.time;
  }
}

function mergeFleets(g: Galaxy): void {
  const seen = new Map<string, Fleet>();
  for (const f of [...g.fleets.values()].sort((a, b) => a.arrivedAt - b.arrivedAt)) {
    if (f.at == null || f.battleId != null) continue;
    const key = `${f.at}:${f.faction}`;
    const into = seen.get(key);
    if (!into) {
      seen.set(key, f);
      continue;
    }
    for (const c of SHIP_CLASSES) if (f.ships[c]) into.ships[c] = (into.ships[c] ?? 0) + f.ships[c]!;
    g.fleets.delete(f.id);
  }
}

function capture(g: Galaxy, planet: Planet, faction: FactionId): void {
  const from = planet.owner;
  planet.owner = faction;
  planet.station = 0;
  planet.platforms = 0;
  planet.queue = [];
  planet.occupation = null;
  g.events.push({ type: 'captured', planetId: planet.id, faction, from });
}

function defenseOf(planet: Planet): PlanetDefense {
  return { station: planet.station, platforms: planet.platforms, mines: planet.mines };
}

/** Strength a planet opposes to an attacker: its defenses plus the fleets docked there. */
export function defenseValue(g: Galaxy, planet: Planet): number {
  const owner = planet.owner;
  const fleets = owner ? fleetsAt(g, planet.id, owner) : [];
  const levels = STATION.levels.slice(0, planet.station).reduce((v, l) => v + l.hull + l.shield, 0);
  return levels / 2 + planet.platforms * BUILDABLES.platform.cost + fleets.reduce((v, f) => v + fleetValue(f.ships), 0);
}

function startBattle(g: Galaxy, planet: Planet, attacker: FactionId, defender: FactionId, from: number): void {
  const id = g.nextId++;
  const side = (faction: FactionId): ShipCounts => {
    const ships: ShipCounts = {};
    for (const f of fleetsAt(g, planet.id, faction)) {
      f.battleId = id;
      for (const c of SHIP_CLASSES) if (f.ships[c]) ships[c] = (ships[c] ?? 0) + f.ships[c]!;
    }
    return ships;
  };
  const defense = planet.owner === defender ? defenseOf(planet) : { station: 0, platforms: 0, mines: 0 };
  const world = createPlanetBattle(
    { faction: attacker, ships: side(attacker) },
    { faction: defender, ships: side(defender) },
    defense,
    Math.floor(galaxyRand(g) * 2147483646) + 1,
  );
  const status: BattleStatus = attacker === g.player || defender === g.player ? 'pending' : 'auto';
  g.battles.push({ id, planetId: planet.id, attacker, defender, from, world, status, elapsed: 0 });
  g.events.push({ type: 'battle', battleId: id });
}

/**
 * A lone fleet occupies an undefended planet, and takes it once the occupation runs out.
 * Two factions present (fleets, or an owner with a station or platforms) fight: the last
 * fleet to arrive attacks.
 */
function detectConflicts(g: Galaxy): void {
  for (const planet of g.planets) {
    if (g.battles.some((b) => b.planetId === planet.id)) continue;
    const here = fleetsAt(g, planet.id).sort((a, b) => a.arrivedAt - b.arrivedAt);
    const present = new Set(here.map((f) => f.faction));
    const owner = planet.owner;
    const defended = owner != null && (planet.station > 0 || planet.platforms > 0 || present.has(owner));
    if (!defended && present.size === 1) {
      const f = here[0].faction;
      if (owner === f) planet.occupation = null;
      else if (planet.occupation?.faction !== f) {
        const time = owner ? GALAXY_RULES.captureTime : GALAXY_RULES.neutralCaptureTime;
        planet.occupation = { faction: f, remaining: time };
      }
      continue;
    }
    planet.occupation = null;
    const sides = new Set(present);
    if (defended) sides.add(owner!);
    if (sides.size < 2) continue;
    const defender = defended ? owner! : here[0].faction;
    const last = here.filter((f) => f.faction !== defender).at(-1)!;
    startBattle(g, planet, last.faction, defender, last.came);
  }
}

/** Ends a battle: survivors go back on the map as fleets, and the planet may change hands. */
export function concludeBattle(g: Galaxy, battleId: number, result: BattleResult): void {
  const b = g.battles.find((x) => x.id === battleId);
  if (!b) return;
  g.battles = g.battles.filter((x) => x !== b);
  for (const f of [...g.fleets.values()]) if (f.battleId === b.id) g.fleets.delete(f.id);
  const planet = g.planets[b.planetId];
  const w = b.world;
  if (planet.owner === b.defender) Object.assign(planet, defenseLeft(w, b.defender));
  const att = survivors(w, b.attacker);
  const def = survivors(w, b.defender);
  if (result === 'attacker') {
    capture(g, planet, b.attacker);
    addShipsAt(g, b.attacker, planet.id, att);
  } else {
    addShipsAt(g, b.defender, planet.id, def);
    if (result === 'retreat') {
      const back = addShipsAt(g, b.attacker, planet.id, att);
      if (back) sendFleet(g, back.id, b.from);
    }
  }
  g.events.push({ type: 'battleEnd', planetId: planet.id, attacker: b.attacker, defender: b.defender, result });
}

export function chooseBattle(g: Galaxy, battleId: number, mode: 'auto' | 'tactical'): void {
  const b = g.battles.find((x) => x.id === battleId);
  if (b?.status === 'pending') b.status = mode;
}

const AUTO_DT = 1 / 30;

function advanceAutoBattles(g: Galaxy, dt: number): void {
  for (const b of [...g.battles]) {
    if (b.status !== 'auto') continue;
    let result: BattleResult | null = null;
    for (let t = 0; t < GALAXY_RULES.autoSpeed * dt && !result; t += AUTO_DT) {
      step(b.world, AUTO_DT);
      updateBattleAI(b.world, [b.attacker, b.defender], AUTO_DT);
      b.elapsed += AUTO_DT;
      result = battleOutcome(b.world, b.attacker, b.defender);
      if (!result && b.elapsed >= GALAXY_RULES.autoTimeout) result = 'retreat';
    }
    b.world.events.length = 0;
    if (result) concludeBattle(g, b.id, result);
  }
}

function updateOccupations(g: Galaxy, dt: number): void {
  for (const planet of g.planets) {
    const o = planet.occupation;
    if (!o) continue;
    o.remaining -= dt;
    if (o.remaining <= 0) capture(g, planet, o.faction);
  }
}

/** Galaxy income per second for a faction. */
export function incomeOf(g: Galaxy, faction: FactionId): number {
  return g.planets
    .filter((p) => p.owner === faction)
    .reduce((s, p) => s + p.income + p.mines * GALAXY_RULES.mineIncome, 0);
}

function buildCost(planet: Planet, build: GalaxyBuild): { cost: number; time: number } {
  if (build.kind === 'ship') return { cost: SHIPS[build.cls].cost, time: SHIPS[build.cls].buildTime };
  if (build.kind === 'station') {
    if (planet.station === 0) return { cost: GALAXY_RULES.stationCost, time: GALAXY_RULES.stationTime };
    const next = STATION.levels[planet.station];
    return { cost: next.upgradeCost, time: next.upgradeTime };
  }
  return { cost: BUILDABLES[build.kind].cost, time: BUILDABLES[build.kind].buildTime };
}

export function buildPrice(planet: Planet, build: GalaxyBuild): number {
  return buildCost(planet, build).cost;
}

/** Whether the faction can queue this at the planet right now, and if not, why. */
export function canBuild(g: Galaxy, faction: FactionId, planetId: number, build: GalaxyBuild): Result {
  const planet = g.planets[planetId];
  const f = g.factions.get(faction);
  if (!f || planet.owner !== faction) return fail('Planète non contrôlée');
  if (g.battles.some((b) => b.planetId === planetId)) return fail('Combat en cours');
  if (planet.queue.length >= MAX_QUEUE) return fail("File d'attente pleine");
  const queued = (kind: string) => planet.queue.filter((q) => q.kind === kind).length;
  // Queued station work counts: a station being built or upgraded unlocks what comes after it.
  const level = planet.station + queued('station');
  if (build.kind === 'station' && level >= STATION.levels.length) return fail('Niveau maximum');
  if (build.kind === 'mine' && planet.mines + queued('mine') >= planet.mineSlots) return fail('Aucun emplacement');
  if (build.kind === 'platform') {
    if (planet.station === 0) return fail('Station requise');
    if (planet.platforms + queued('platform') >= planet.station * 2) return fail(`Maximum ${planet.station * 2}`);
  }
  if (build.kind === 'ship') {
    const need = SHIPS[build.cls].minStationLevel;
    if (SHIPS[build.cls].source !== 'shipyard') return fail('Lancé par les hangars');
    if (planet.station === 0) return fail('Station requise');
    if (planet.station < need) return fail(`Station niveau ${need} requise`);
  }
  if (f.credits < buildCost(planet, build).cost) return fail('Crédits insuffisants');
  return OK;
}

export function enqueueBuild(g: Galaxy, faction: FactionId, planetId: number, build: GalaxyBuild): Result {
  const r = canBuild(g, faction, planetId, build);
  if (!r.ok) return r;
  const planet = g.planets[planetId];
  // A station upgrade queued behind another one is priced at the level it will reach.
  const ahead = { ...planet, station: planet.station + planet.queue.filter((q) => q.kind === 'station').length };
  const { cost, time } = buildCost(ahead, build);
  if (g.factions.get(faction)!.credits < cost) return fail('Crédits insuffisants');
  g.factions.get(faction)!.credits -= cost;
  planet.queue.push({ ...build, remaining: time, total: time, cost });
  return OK;
}

export function cancelBuild(g: Galaxy, planetId: number, index: number): void {
  const planet = g.planets[planetId];
  const item = planet.queue[index];
  if (!item || !planet.owner) return;
  planet.queue.splice(index, 1);
  g.factions.get(planet.owner)!.credits += item.cost;
}

function updateProduction(g: Galaxy, dt: number): void {
  for (const planet of g.planets) {
    const item = planet.queue[0];
    if (!item || !planet.owner || g.battles.some((b) => b.planetId === planet.id)) continue;
    item.remaining -= dt;
    if (item.remaining > 0) continue;
    planet.queue.shift();
    let what: string;
    if (item.kind === 'ship') {
      addShipsAt(g, planet.owner, planet.id, { [item.cls]: 1 });
      what = SHIPS[item.cls].name;
    } else if (item.kind === 'station') {
      planet.station++;
      what = planet.station === 1 ? STATION.name : `${STATION.name} niveau ${planet.station}`;
    } else {
      planet[item.kind === 'mine' ? 'mines' : 'platforms']++;
      what = BUILDABLES[item.kind].name;
    }
    g.events.push({ type: 'built', planetId: planet.id, faction: planet.owner, what });
  }
}

/**
 * A faction without a planet is out, unless it is taking one over or fighting for one:
 * its fleets have nowhere left to go. The last faction standing wins.
 */
function checkElimination(g: Galaxy): void {
  for (const f of g.factions.values()) {
    if (f.eliminated) continue;
    const planets = g.planets.some((p) => p.owner === f.id || p.occupation?.faction === f.id);
    const fighting = g.battles.some((b) => b.attacker === f.id || b.defender === f.id);
    if (planets || fighting) continue;
    f.eliminated = true;
    for (const fl of [...g.fleets.values()]) if (fl.faction === f.id) g.fleets.delete(fl.id);
  }
  const alive = [...g.factions.values()].filter((f) => !f.eliminated);
  if (!g.winner && alive.length === 1) {
    g.winner = alive[0].id;
    g.events.push({ type: 'victory', faction: g.winner });
  }
}

/** The galaxy waits while the player has a battle to choose or to fight. */
export function waitingForPlayer(g: Galaxy): boolean {
  return g.battles.some((b) => b.status !== 'auto');
}

export function updateGalaxy(g: Galaxy, dt: number): void {
  for (const f of g.factions.values()) {
    if (!f.eliminated) f.credits += incomeOf(g, f.id) * dt;
  }
  updateProduction(g, dt);
  moveFleets(g, dt);
  mergeFleets(g);
  detectConflicts(g);
  updateOccupations(g, dt);
  advanceAutoBattles(g, dt);
  checkElimination(g);
}
