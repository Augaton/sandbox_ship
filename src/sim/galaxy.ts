// Galaxy map simulation: planets, fleets travelling along lanes, production, and the
// battles that start when two factions meet over a planet. Pure TypeScript, no Phaser.
import { GALAXY_RULES, type PlanetSpecial } from '../data/galaxy';
import { HEROES, type HeroId } from '../data/heroes';
import {
  MAX_TECH_LEVEL, TECHS, TECH_BONUS, noResearch, type ResearchJob, type ResearchLevels, type TechId,
} from '../data/research';
import { BUILDABLES, MAX_QUEUE, STATION } from '../data/structures';
import { SHIPS, SHIP_CLASSES, orderName, shipCost, type FactionId, type ShipClass } from '../data/units';
import {
  NO_DEFENSE, battleOutcome, countShips, createPlanetBattle, defenseLeft, fleetValue, heroSurvivors, survivors, updateBattleAI,
  type BattleResult, type PlanetDefense, type ShipCounts,
} from './battle';
import { CLASSIC, type GalaxyLayout } from './galaxyGen';
import { step } from './step';
import { DIFFICULTIES, dist, type Difficulty, type Result, type World } from './world';

export type BuildingKind = 'station' | 'mine' | 'platform' | 'platformTier' | 'lab' | 'repairDock' | 'ionCannon';

export type GalaxyBuild = { kind: 'ship'; cls: ShipClass } | { kind: 'hero'; hero: HeroId } | { kind: BuildingKind };

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
  /** A lab lets its owner research; it stays on the ground, out of space battles. */
  lab: number;
  repairDock: number;
  ionCannon: number;
  /** Level of the planet's platforms, 1 to 3. */
  platformTier: number;
  special?: PlanetSpecial;
  /** Two queues run side by side: ships, and everything else. */
  units: GalaxyQueueItem[];
  buildings: GalaxyQueueItem[];
  /** A lone fleet taking the planet over. */
  occupation: { faction: FactionId; remaining: number } | null;
}

export interface Fleet {
  id: number;
  faction: FactionId;
  ships: ShipCounts;
  heroes: HeroId[];
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
  /** Heroes each side brought, to know who fell. */
  heroes: Partial<Record<FactionId, HeroId[]>>;
}

export interface GalaxyFaction {
  id: FactionId;
  credits: number;
  /** `want` is the ship class the AI saves up for. */
  ai: { timer: number; want: ShipClass | null; difficulty: Difficulty } | null;
  eliminated: boolean;
  research: ResearchLevels;
  researching: ResearchJob | null;
  /** Galaxy seconds before each fallen hero can be built again. */
  heroCooldowns: Partial<Record<HeroId, number>>;
}

export type GalaxyEvent =
  | { type: 'battle'; battleId: number }
  | { type: 'battleEnd'; planetId: number; attacker: FactionId; defender: FactionId; result: BattleResult }
  | { type: 'captured'; planetId: number; faction: FactionId; from: FactionId | null }
  | { type: 'built'; planetId: number; faction: FactionId; what: string }
  | { type: 'researched'; faction: FactionId; tech: TechId }
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
export function createGalaxy(
  player: FactionId, enemies: FactionId | FactionId[], enemyAI = true, seed = 7, layout: GalaxyLayout = CLASSIC,
  difficulty: Difficulty = 'normal',
): Galaxy {
  const foes = Array.isArray(enemies) ? enemies : [enemies];
  const g: Galaxy = {
    time: 0, player,
    planets: layout.planets.map((p, id) => ({
      ...p, id, owner: null, station: 0, mines: 0, platforms: 0, lab: 0, repairDock: 0, ionCannon: 0, platformTier: 1,
      units: [], buildings: [], occupation: null,
    })),
    lanes: layout.lanes, fleets: new Map(), factions: new Map(), battles: [], events: [],
    nextId: 1, seed, winner: null,
  };
  const sides: [FactionId, number, boolean][] = [[player, layout.capitals[0], false], ...foes.map((f, i) => [f, layout.capitals[i + 1], enemyAI] as [FactionId, number, boolean])];
  for (const [faction, capital, ai] of sides) {
    g.factions.set(faction, {
      id: faction, credits: GALAXY_RULES.startCredits, ai: ai ? { timer: 0, want: null, difficulty } : null, eliminated: false,
      research: noResearch(), researching: null, heroCooldowns: {},
    });
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

/** Map speed of a fleet: as fast as its slowest ship, faster with engine research. */
export function fleetSpeed(ships: ShipCounts, engines = 0, heroes: HeroId[] = []): number {
  const slowest = Math.min(
    ...SHIP_CLASSES.filter((c) => ships[c]).map((c) => SHIPS[c].speed),
    ...heroes.map((h) => SHIPS[HEROES[h].base].speed * HEROES[h].speed),
  );
  return (30 + slowest * 0.5) * (1 + TECH_BONUS.engines * engines);
}

/**
 * Planets a faction has eyes on: its own, their neighbours, and those its fleets sit at or
 * fly between. Ownership is known everywhere; defenses and fleets only there.
 */
export function visiblePlanets(g: Galaxy, faction: FactionId): Set<number> {
  const out = new Set<number>();
  for (const p of g.planets) {
    if (p.owner !== faction) continue;
    out.add(p.id);
    for (const n of neighbours(g, p.id)) out.add(n);
  }
  for (const f of g.fleets.values()) {
    if (f.faction !== faction) continue;
    if (f.at != null) out.add(f.at);
    out.add(f.from);
    if (f.to != null) out.add(f.to);
  }
  return out;
}

export function fleetVisible(g: Galaxy, faction: FactionId, fleet: Fleet, seen = visiblePlanets(g, faction)): boolean {
  if (fleet.faction === faction) return true;
  return fleet.at != null ? seen.has(fleet.at) : seen.has(fleet.from) || (fleet.to != null && seen.has(fleet.to));
}

export function fleetsAt(g: Galaxy, planetId: number, faction?: FactionId): Fleet[] {
  return [...g.fleets.values()].filter((f) => f.at === planetId && f.battleId == null && (!faction || f.faction === faction));
}

export function addShipsAt(g: Galaxy, faction: FactionId, planetId: number, ships: ShipCounts, heroes: HeroId[] = []): Fleet | null {
  if (countShips(ships) === 0 && heroes.length === 0) return null;
  const fleet = fleetsAt(g, planetId, faction)[0];
  if (fleet) {
    for (const c of SHIP_CLASSES) if (ships[c]) fleet.ships[c] = (fleet.ships[c] ?? 0) + ships[c]!;
    fleet.heroes.push(...heroes);
    return fleet;
  }
  const f: Fleet = {
    id: g.nextId++, faction, ships: { ...ships }, heroes: [...heroes], at: planetId, from: planetId, to: null, path: [],
    progress: 0, came: planetId, arrivedAt: g.time, battleId: null,
  };
  g.fleets.set(f.id, f);
  return f;
}

/**
 * Sends `ships` and `heroes` (the whole fleet by default) towards `dest`; a partial order
 * splits the fleet, and heroes left out of it stay behind.
 */
export function sendFleet(g: Galaxy, fleetId: number, dest: number, ships?: ShipCounts, heroes: HeroId[] = []): Result {
  const fleet = g.fleets.get(fleetId);
  if (!fleet || fleet.at == null || fleet.battleId != null) return fail('Flotte indisponible');
  const path = findPath(g, fleet.at, dest);
  if (!path) return fail('Aucune route');
  let moving = fleet;
  if (ships) {
    for (const c of SHIP_CLASSES) if ((ships[c] ?? 0) > (fleet.ships[c] ?? 0)) return fail('Vaisseaux insuffisants');
    if (heroes.some((h) => !fleet.heroes.includes(h))) return fail('Héros absent');
    if (countShips(ships) === 0 && heroes.length === 0) return fail('Aucun vaisseau choisi');
    const rest: ShipCounts = {};
    for (const c of SHIP_CLASSES) {
      const left = (fleet.ships[c] ?? 0) - (ships[c] ?? 0);
      if (left > 0) rest[c] = left;
    }
    const restHeroes = fleet.heroes.filter((h) => !heroes.includes(h));
    if (countShips(rest) > 0 || restHeroes.length > 0) {
      fleet.ships = rest;
      fleet.heroes = restHeroes;
      moving = { ...fleet, id: g.nextId++, ships: { ...ships }, heroes: [...heroes], path: [] };
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
    const engines = g.factions.get(f.faction)?.research.engines ?? 0;
    f.progress += (fleetSpeed(f.ships, engines, f.heroes) * dt) / dist(a, b);
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
    into.heroes.push(...f.heroes);
    g.fleets.delete(f.id);
  }
}

function capture(g: Galaxy, planet: Planet, faction: FactionId): void {
  const from = planet.owner;
  planet.owner = faction;
  // Space structures are lost; mines and labs on the ground change hands.
  Object.assign(planet, {
    station: 0, platforms: 0, platformTier: 1, repairDock: 0, ionCannon: 0, units: [], buildings: [], occupation: null,
  });
  g.events.push({ type: 'captured', planetId: planet.id, faction, from });
}

function defenseOf(planet: Planet): PlanetDefense {
  const { station, platforms, mines, repairDock, ionCannon, platformTier } = planet;
  return { station, platforms, mines, repairDock, ionCannon, platformTier };
}

/** Whether the planet has anything in orbit that fights back. */
function fortified(planet: Planet): boolean {
  return planet.station > 0 || planet.platforms > 0 || planet.ionCannon > 0 || planet.repairDock > 0;
}

/** Strength a planet opposes to an attacker: its defenses plus the fleets docked there. */
export function defenseValue(g: Galaxy, planet: Planet): number {
  const owner = planet.owner;
  const fleets = owner ? fleetsAt(g, planet.id, owner) : [];
  const levels = STATION.levels.slice(0, planet.station).reduce((v, l) => v + l.hull + l.shield, 0);
  return levels / 2 + planet.platforms * BUILDABLES.platform.cost + planet.ionCannon * BUILDABLES.ionCannon.cost
    + fleets.reduce((v, f) => v + fleetValue(f.ships, f.heroes), 0);
}

function startBattle(g: Galaxy, planet: Planet, attacker: FactionId, defender: FactionId, from: number): void {
  const id = g.nextId++;
  const side = (faction: FactionId) => {
    const ships: ShipCounts = {};
    const heroes: HeroId[] = [];
    for (const f of fleetsAt(g, planet.id, faction)) {
      f.battleId = id;
      for (const c of SHIP_CLASSES) if (f.ships[c]) ships[c] = (ships[c] ?? 0) + f.ships[c]!;
      heroes.push(...f.heroes);
    }
    return { faction, ships, heroes, research: g.factions.get(faction)?.research };
  };
  const defense = planet.owner === defender ? defenseOf(planet) : NO_DEFENSE;
  const att = side(attacker);
  const def = side(defender);
  const world = createPlanetBattle(
    att,
    def,
    defense,
    Math.floor(galaxyRand(g) * 2147483646) + 1,
  );
  const status: BattleStatus = attacker === g.player || defender === g.player ? 'pending' : 'auto';
  g.battles.push({
    id, planetId: planet.id, attacker, defender, from, world, status, elapsed: 0,
    heroes: { [attacker]: att.heroes, [defender]: def.heroes },
  });
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
    const defended = owner != null && (fortified(planet) || present.has(owner));
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
  const attHeroes = heroSurvivors(w, b.attacker);
  const defHeroes = heroSurvivors(w, b.defender);
  // Fallen heroes can be built again after a while.
  for (const [faction, alive] of [[b.attacker, attHeroes], [b.defender, defHeroes]] as const) {
    const f = g.factions.get(faction);
    for (const h of b.heroes[faction] ?? []) if (f && !alive.includes(h)) f.heroCooldowns[h] = HEROES[h].respawn;
  }
  if (result === 'attacker') {
    capture(g, planet, b.attacker);
    addShipsAt(g, b.attacker, planet.id, att, attHeroes);
  } else {
    addShipsAt(g, b.defender, planet.id, def, defHeroes);
    if (result === 'retreat') {
      const back = addShipsAt(g, b.attacker, planet.id, att, attHeroes);
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

/** Galaxy income per second for a faction: planets, plus mines (more on rich deposits, more with research). */
export function incomeOf(g: Galaxy, faction: FactionId): number {
  const mining = 1 + TECH_BONUS.mining * (g.factions.get(faction)?.research.mining ?? 0);
  return g.planets
    .filter((p) => p.owner === faction)
    .reduce((s, p) => s + p.income + p.mines * GALAXY_RULES.mineIncome * mining * (p.special === 'rich' ? 2 : 1), 0);
}

const BUILDING_NAMES: Record<BuildingKind, string> = {
  station: STATION.name, mine: BUILDABLES.mine.name, platform: BUILDABLES.platform.name, platformTier: 'Renforcer les plateformes',
  lab: 'Laboratoire', repairDock: BUILDABLES.repairDock.name, ionCannon: BUILDABLES.ionCannon.name,
};

export function buildName(planet: Planet, build: GalaxyBuild): string {
  if (build.kind === 'ship') return orderName(build.cls);
  if (build.kind === 'hero') return `${HEROES[build.hero].name} (${HEROES[build.hero].title})`;
  if (build.kind === 'station' && planet.station > 0) return 'Améliorer la station';
  if (build.kind === 'platformTier') return `Plateformes niveau ${Math.min(3, planet.platformTier + 1)}`;
  return BUILDING_NAMES[build.kind];
}

function buildCost(planet: Planet, build: GalaxyBuild): { cost: number; time: number } {
  if (build.kind === 'hero') {
    const def = HEROES[build.hero];
    return { cost: def.cost, time: def.buildTime * (planet.special === 'shipyard' ? 0.7 : 1) };
  }
  if (build.kind === 'ship') {
    const time = SHIPS[build.cls].buildTime * (planet.special === 'shipyard' ? 0.7 : 1);
    return { cost: shipCost(build.cls, planet.owner ?? 'klaed'), time };
  }
  if (build.kind === 'station') {
    if (planet.station === 0) return { cost: GALAXY_RULES.stationCost, time: GALAXY_RULES.stationTime };
    const next = STATION.levels[planet.station];
    return { cost: next.upgradeCost, time: next.upgradeTime };
  }
  if (build.kind === 'lab') return { cost: GALAXY_RULES.labCost, time: GALAXY_RULES.labTime };
  if (build.kind === 'platformTier') {
    const tier = BUILDABLES.platform.tiers![Math.min(planet.platformTier, 2) - 1];
    return { cost: tier.cost, time: tier.time };
  }
  return { cost: BUILDABLES[build.kind].cost, time: BUILDABLES[build.kind].buildTime };
}

export function buildPrice(planet: Planet, build: GalaxyBuild): number {
  return buildCost(planet, build).cost;
}

export function buildTime(planet: Planet, build: GalaxyBuild): number {
  return buildCost(planet, build).time;
}

function queueFor(planet: Planet, build: GalaxyBuild): GalaxyQueueItem[] {
  return build.kind === 'ship' || build.kind === 'hero' ? planet.units : planet.buildings;
}

/** Whether a hero is out there already: in a fleet, in a battle, or being built. */
function heroInService(g: Galaxy, hero: HeroId): boolean {
  return [...g.fleets.values()].some((f) => f.heroes.includes(hero))
    || g.battles.some((b) => Object.values(b.heroes).some((list) => list?.includes(hero)))
    || g.planets.some((p) => p.units.some((q) => q.kind === 'hero' && q.hero === hero));
}

export function platformCap(planet: Planet): number {
  return planet.station * 2 + (planet.special === 'fortress' ? 2 : 0);
}

/** Whether the faction can queue this at the planet right now, and if not, why. */
export function canBuild(g: Galaxy, faction: FactionId, planetId: number, build: GalaxyBuild): Result {
  const planet = g.planets[planetId];
  const f = g.factions.get(faction);
  if (!f || planet.owner !== faction) return fail('Planète non contrôlée');
  if (g.battles.some((b) => b.planetId === planetId)) return fail('Combat en cours');
  if (queueFor(planet, build).length >= MAX_QUEUE) return fail("File d'attente pleine");
  const queued = (kind: string) => planet.buildings.filter((q) => q.kind === kind).length;
  // Queued station work counts: a station being built or upgraded unlocks what comes after it.
  const have = (kind: Exclude<BuildingKind, 'mine' | 'platform' | 'platformTier'> | 'mines' | 'platforms') =>
    (kind === 'mines' ? planet.mines : kind === 'platforms' ? planet.platforms : planet[kind])
    + queued(kind === 'mines' ? 'mine' : kind === 'platforms' ? 'platform' : kind);
  switch (build.kind) {
    case 'station':
      if (have('station') >= STATION.levels.length) return fail('Niveau maximum');
      break;
    case 'mine':
      if (have('mines') >= planet.mineSlots) return fail('Aucun emplacement');
      break;
    case 'lab':
      if (have('lab') >= 1) return fail('Déjà construit');
      break;
    case 'platformTier':
      if (planet.platformTier >= 3) return fail('Niveau maximum');
      if (queued('platformTier') > 0) return fail('Déjà en cours');
      if (planet.platforms === 0) return fail('Plateforme requise');
      if (planet.station < planet.platformTier + 1) return fail(`Station niveau ${planet.platformTier + 1} requise`);
      break;
    case 'platform':
      if (planet.station === 0) return fail('Station requise');
      if (have('platforms') >= platformCap(planet)) return fail(`Maximum ${platformCap(planet)}`);
      break;
    case 'repairDock':
    case 'ionCannon': {
      const need = build.kind === 'ionCannon' ? 2 : 1;
      if (planet.station < need) return fail(need === 1 ? 'Station requise' : `Station niveau ${need} requise`);
      if (have(build.kind) >= 1) return fail('Déjà construit');
      break;
    }
    case 'hero': {
      const def = HEROES[build.hero];
      if (def.faction !== faction) return fail("Héros d'une autre faction");
      if (planet.station < def.minStationLevel) return fail(`Station niveau ${def.minStationLevel} requise`);
      if (heroInService(g, build.hero)) return fail('Déjà en service');
      const wait = f.heroCooldowns[build.hero] ?? 0;
      if (wait > 0) return fail(`Disponible dans ${Math.ceil(wait)} s`);
      break;
    }
    case 'ship': {
      const need = SHIPS[build.cls].minStationLevel;
      if (SHIPS[build.cls].source !== 'shipyard') return fail('Lancé par les hangars');
      if (planet.station === 0) return fail('Station requise');
      if (planet.station < need) return fail(`Station niveau ${need} requise`);
    }
  }
  if (f.credits < buildCost(planet, build).cost) return fail('Crédits insuffisants');
  return OK;
}

export function enqueueBuild(g: Galaxy, faction: FactionId, planetId: number, build: GalaxyBuild): Result {
  const r = canBuild(g, faction, planetId, build);
  if (!r.ok) return r;
  const planet = g.planets[planetId];
  // A station upgrade queued behind another one is priced at the level it will reach.
  const ahead = { ...planet, station: planet.station + planet.buildings.filter((q) => q.kind === 'station').length };
  const { cost, time } = buildCost(ahead, build);
  if (g.factions.get(faction)!.credits < cost) return fail('Crédits insuffisants');
  g.factions.get(faction)!.credits -= cost;
  queueFor(planet, build).push({ ...build, remaining: time, total: time, cost });
  return OK;
}

export function cancelBuild(g: Galaxy, planetId: number, index: number, which: 'units' | 'buildings' = 'buildings'): void {
  const planet = g.planets[planetId];
  const queue = planet[which];
  const item = queue[index];
  if (!item || !planet.owner) return;
  queue.splice(index, 1);
  g.factions.get(planet.owner)!.credits += item.cost;
}

function finish(g: Galaxy, planet: Planet, item: GalaxyQueueItem): void {
  const owner = planet.owner!;
  let what: string;
  if (item.kind === 'ship') {
    addShipsAt(g, owner, planet.id, { [item.cls]: SHIPS[item.cls].squad ?? 1 });
    what = orderName(item.cls);
  } else if (item.kind === 'hero') {
    addShipsAt(g, owner, planet.id, {}, [item.hero]);
    what = HEROES[item.hero].name;
  } else if (item.kind === 'station') {
    planet.station++;
    what = planet.station === 1 ? STATION.name : `${STATION.name} niveau ${planet.station}`;
  } else {
    if (item.kind === 'mine') planet.mines++;
    else if (item.kind === 'platform') planet.platforms++;
    else if (item.kind === 'platformTier') planet.platformTier++;
    else planet[item.kind]++;
    what = BUILDING_NAMES[item.kind];
  }
  g.events.push({ type: 'built', planetId: planet.id, faction: owner, what });
}

/** The front of each queue progresses; nothing is built on a planet under attack. */
function updateProduction(g: Galaxy, dt: number): void {
  for (const planet of g.planets) {
    if (!planet.owner || g.battles.some((b) => b.planetId === planet.id)) continue;
    for (const queue of [planet.buildings, planet.units]) {
      const item = queue[0];
      if (!item || (item.remaining -= dt) > 0) continue;
      queue.shift();
      finish(g, planet, item);
    }
  }
}

/** Research needs a lab, and a station of the level about to be reached. */
export function canResearchTech(g: Galaxy, faction: FactionId, tech: TechId): Result {
  const f = g.factions.get(faction);
  if (!f) return fail('Faction inconnue');
  const own = g.planets.filter((p) => p.owner === faction);
  const level = f.research[tech];
  if (!own.some((p) => p.lab > 0)) return fail('Laboratoire requis');
  if (level >= MAX_TECH_LEVEL) return fail('Niveau maximum');
  if (f.researching) return fail('Recherche déjà en cours');
  if (Math.max(0, ...own.map((p) => p.station)) < level + 1) return fail(`Station niveau ${level + 1} requise`);
  if (f.credits < TECHS[tech].costs[level]) return fail('Crédits insuffisants');
  return OK;
}

export function startTech(g: Galaxy, faction: FactionId, tech: TechId): Result {
  const r = canResearchTech(g, faction, tech);
  if (!r.ok) return r;
  const f = g.factions.get(faction)!;
  const level = f.research[tech];
  const cost = TECHS[tech].costs[level];
  f.credits -= cost;
  f.researching = { tech, remaining: TECHS[tech].times[level], total: TECHS[tech].times[level], cost };
  return OK;
}

export function cancelTech(g: Galaxy, faction: FactionId): void {
  const f = g.factions.get(faction);
  if (!f?.researching) return;
  f.credits += f.researching.cost;
  f.researching = null;
}

/** Owning ancient archives speeds research up. */
export function researchSpeed(g: Galaxy, faction: FactionId): number {
  return g.planets.some((p) => p.owner === faction && p.special === 'archive') ? 1 / 0.75 : 1;
}

function updateResearch(g: Galaxy, dt: number): void {
  for (const f of g.factions.values()) {
    const job = f.researching;
    if (!job || (job.remaining -= dt * researchSpeed(g, f.id)) > 0) continue;
    f.research[job.tech]++;
    f.researching = null;
    g.events.push({ type: 'researched', faction: f.id, tech: job.tech });
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
    if (!f.eliminated) f.credits += incomeOf(g, f.id) * (f.ai ? DIFFICULTIES[f.ai.difficulty].income : 1) * dt;
    for (const h of Object.keys(f.heroCooldowns) as HeroId[]) f.heroCooldowns[h] = Math.max(0, f.heroCooldowns[h]! - dt);
  }
  updateProduction(g, dt);
  updateResearch(g, dt);
  moveFleets(g, dt);
  mergeFleets(g);
  detectConflicts(g);
  updateOccupations(g, dt);
  advanceAutoBattles(g, dt);
  checkElimination(g);
}
