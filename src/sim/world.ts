import type { AbilityId } from '../data/abilities';
import { HEROES, type HeroId } from '../data/heroes';
import { TECH_BONUS, noResearch, type ResearchJob, type ResearchLevels, type TechId } from '../data/research';
import { FACTIONS, SHIPS, maxRange, type FactionId, type ShipClass, type SizeClass, type WeaponDef } from '../data/units';
import {
  BUILDABLES, HARDPOINTS, STATION,
  type BuildableType, type HangarDef, type HardpointSlot,
} from '../data/structures';

export type EntityKind = 'ship' | 'structure' | 'hardpoint';

export type Order =
  | { type: 'move'; x: number; y: number }
  | { type: 'attackMove'; x: number; y: number }
  | { type: 'attack'; targetId: number };

export interface WeaponState {
  def: WeaponDef;
  cooldown: number;
}

export interface HangarState {
  def: HangarDef;
  timer: number;
  /** Index of the next squadron to launch in def.squadrons. */
  next: number;
  launched: number[];
}

export interface AbilityState {
  id: AbilityId;
  /** Seconds until it can be used again. */
  cooldown: number;
  /** Seconds the effect still lasts. */
  active: number;
}

/** Controls of a ship flown by hand. */
export interface Pilot {
  /** -1 (reverse) to 1 (full ahead). */
  thrust: number;
  /** -1 (port) to 1 (starboard). */
  turn: number;
  firing: boolean;
  aimX: number;
  aimY: number;
}

export interface Entity {
  id: number;
  kind: EntityKind;
  /** ShipClass, StructureType or HardpointRole, depending on kind. */
  type: string;
  faction: FactionId;
  x: number;
  y: number;
  angle: number;
  speed: number;
  radius: number;
  size: SizeClass;
  hull: number;
  maxHull: number;
  shield: number;
  maxShield: number;
  shieldRegen: number;
  weapons: WeaponState[];
  range: number;
  targetId: number | null;
  retarget: number;
  order: Order | null;
  /** Where an idle ship holds position and returns after a chase. */
  guardX: number;
  guardY: number;
  /** 0..1; a structure only works once fully built. */
  build: number;
  buildTime: number;
  level: number;
  parentId: number | null;
  hangar: HangarState | null;
  income: number;
  fieldId: number | null;
  /** For hardpoints: offset from the parent, in the parent's frame (x forward). */
  ox: number;
  oy: number;
  /** Engine hardpoints still working, out of maxEngines; null without engine hardpoints. */
  engines: number | null;
  maxEngines: number;
  /** Shield generators still standing; null when the shields need none. */
  shieldGens: number | null;
  /** Set while the player flies this ship by hand. */
  pilot: Pilot | null;
  ability: AbilityState | null;
  hero: HeroId | null;
}

export interface Projectile {
  id: number;
  faction: FactionId;
  kind: WeaponDef['kind'];
  x: number;
  y: number;
  vx: number;
  vy: number;
  speed: number;
  damage: number;
  mult: WeaponDef['mult'];
  targetId: number;
  homing: boolean;
  ttl: number;
}

export interface AsteroidField {
  id: number;
  x: number;
  y: number;
  radius: number;
  mineId: number | null;
}

export type QueueItem =
  | { kind: 'ship'; cls: ShipClass; remaining: number; total: number; cost: number }
  | { kind: 'upgrade'; remaining: number; total: number; cost: number }
  /** Next level of a platform or hangar, by entity id. */
  | { kind: 'tier'; id: number; remaining: number; total: number; cost: number }
  | { kind: 'hero'; hero: HeroId; remaining: number; total: number; cost: number };

/** What the AI is saving up for. */
export type AIGoal =
  | { kind: 'mine' } | { kind: 'upgrade' } | { kind: 'platform' }
  | { kind: 'structure'; type: 'repairDock' | 'ionCannon' }
  | { kind: 'research'; tech: TechId }
  | { kind: 'hero'; hero: HeroId }
  | { kind: 'ship'; cls: ShipClass };

export interface AIState {
  timer: number;
  goal: AIGoal | null;
  /** Army value (sum of ship costs) needed before the next attack wave. */
  waveValue: number;
}

export interface FactionState {
  id: FactionId;
  credits: number;
  /** Ships being built, one at a time. */
  queue: QueueItem[];
  /** Station upgrades, one at a time, alongside the ships. */
  buildQueue: QueueItem[];
  research: ResearchLevels;
  researching: ResearchJob | null;
  /** The AI uses this faction's ship abilities. */
  autoAbilities: boolean;
  /** Seconds before each fallen hero can be built again. */
  heroCooldowns: Partial<Record<HeroId, number>>;
  stationId: number | null;
  hadStation: boolean;
  rallyX: number;
  rallyY: number;
  ai: AIState | null;
}

export type SimEvent =
  | {
      type: 'destroyed'; id: number; kind: EntityKind; entityType: string;
      faction: FactionId; x: number; y: number; radius: number;
    }
  | { type: 'hit'; x: number; y: number; kind: WeaponDef['kind'] }
  | { type: 'built'; id: number }
  | { type: 'researched'; faction: FactionId; tech: TechId }
  | { type: 'victory'; faction: FactionId };

export interface World {
  time: number;
  width: number;
  height: number;
  entities: Map<number, Entity>;
  projectiles: Projectile[];
  factions: Map<FactionId, FactionState>;
  fields: AsteroidField[];
  events: SimEvent[];
  nextId: number;
  seed: number;
  winner: FactionId | null;
  /** Where admirals' attack orders are running this step. */
  auras: { faction: FactionId; x: number; y: number }[];
  /** Fog of war: each faction only sees and targets what its own units see. */
  fog: boolean;
  /** Enemy entities each faction sees right now. */
  visible: Map<FactionId, Set<number>>;
  /** Enemy structures each faction has seen at least once. */
  seen: Map<FactionId, Set<number>>;
  visionTimer: number;
}

export type Result = { ok: true } | { ok: false; reason: string };

export function createWorld(width: number, height: number, seed = 1): World {
  return {
    time: 0, width, height,
    entities: new Map(), projectiles: [], factions: new Map(), fields: [], events: [],
    nextId: 1, seed, winner: null, auras: [],
    fog: false, visible: new Map(), seen: new Map(), visionTimer: 0,
  };
}

/** Deterministic random number in [0, 1) (mulberry32). */
export function rand(world: World): number {
  let t = (world.seed = (world.seed + 0x6d2b79f5) | 0);
  t = Math.imul(t ^ (t >>> 15), t | 1);
  t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
  return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
}

export function newAI(): AIState {
  return { timer: 0, goal: null, waveValue: 3000 };
}

export function addFaction(world: World, id: FactionId, credits: number, ai = false): FactionState {
  const f: FactionState = {
    id, credits, queue: [], buildQueue: [], research: noResearch(), researching: null, autoAbilities: ai, heroCooldowns: {},
    stationId: null, hadStation: false,
    rallyX: world.width / 2, rallyY: world.height / 2,
    ai: ai ? newAI() : null,
  };
  world.factions.set(id, f);
  return f;
}

export function addField(world: World, x: number, y: number, radius: number): AsteroidField {
  const field = { id: world.nextId++, x, y, radius, mineId: null };
  world.fields.push(field);
  return field;
}

function baseEntity(world: World, kind: EntityKind, type: string, faction: FactionId, x: number, y: number): Entity {
  return {
    id: world.nextId++, kind, type, faction, x, y, angle: 0, speed: 0, radius: 10, size: 'structure',
    hull: 1, maxHull: 1, shield: 0, maxShield: 0, shieldRegen: 0, weapons: [], range: 0,
    targetId: null, retarget: 0, order: null, guardX: x, guardY: y,
    build: 1, buildTime: 0, level: 0, parentId: null, hangar: null, income: 0, fieldId: null,
    ox: 0, oy: 0, engines: null, maxEngines: 0, shieldGens: null, pilot: null, ability: null, hero: null,
  };
}

export function researchLevel(world: World, faction: FactionId, tech: TechId): number {
  return world.factions.get(faction)?.research[tech] ?? 0;
}

/** Hull multiplier of a faction: its trait and its armor research. */
export function hullMult(world: World, faction: FactionId): number {
  return FACTIONS[faction].trait.hull * (1 + TECH_BONUS.armor * researchLevel(world, faction, 'armor'));
}

/** Shield multiplier of a faction: its trait and its shield research. */
export function shieldMult(world: World, faction: FactionId): number {
  return FACTIONS[faction].trait.shield * (1 + TECH_BONUS.shields * researchLevel(world, faction, 'shields'));
}

function scaleDefenses(world: World, e: Entity): void {
  const h = hullMult(world, e.faction);
  const s = shieldMult(world, e.faction);
  e.hull *= h;
  e.maxHull *= h;
  e.shield *= s;
  e.maxShield *= s;
  e.shieldRegen *= s;
}

/** A finished armor or shield research upgrades what the faction already fields. */
export function rescaleForResearch(world: World, faction: FactionId, tech: TechId, before: number): void {
  if (tech !== 'armor' && tech !== 'shields') return;
  const after = tech === 'armor' ? hullMult(world, faction) : shieldMult(world, faction);
  const ratio = after / before;
  for (const e of world.entities.values()) {
    if (e.faction !== faction) continue;
    if (tech === 'armor') {
      e.hull *= ratio;
      e.maxHull *= ratio;
    } else {
      e.shield *= ratio;
      e.maxShield *= ratio;
      e.shieldRegen *= ratio;
    }
  }
}

export function armed(defs: WeaponDef[]): WeaponState[] {
  return defs.map((def) => ({ def, cooldown: def.cooldown }));
}

function hangarState(def: HangarDef | undefined): HangarState | null {
  return def ? { def, timer: 2, next: 0, launched: [] } : null;
}

export function spawnShip(world: World, faction: FactionId, cls: ShipClass, x: number, y: number, angle = 0): Entity {
  const def = SHIPS[cls];
  const e = baseEntity(world, 'ship', cls, faction, x, y);
  Object.assign(e, {
    angle, radius: def.radius, size: def.size,
    hull: def.hull, maxHull: def.hull, shield: def.shield, maxShield: def.shield, shieldRegen: def.shieldRegen,
    weapons: armed(def.weapons), range: maxRange(def.weapons),
    retarget: rand(world) * 0.5,
    ability: def.ability ? { id: def.ability, cooldown: 0, active: 0 } : null,
  });
  scaleDefenses(world, e);
  world.entities.set(e.id, e);
  if (def.hardpoints) addHardpoints(world, e, def.hardpoints);
  return e;
}

/** A hero: its base ship, much tougher, with the hero's own ability. */
export function spawnHero(world: World, faction: FactionId, id: HeroId, x: number, y: number, angle = 0): Entity {
  const def = HEROES[id];
  const e = spawnShip(world, faction, def.base, x, y, angle);
  e.hero = id;
  for (const part of [e, ...childrenOf(world, e.id)]) {
    part.hull *= def.hull;
    part.maxHull *= def.hull;
  }
  e.shield *= def.shield;
  e.maxShield *= def.shield;
  e.shieldRegen *= def.shield;
  e.ability = { id: def.ability, cooldown: 0, active: 0 };
  return e;
}

/** Spawns a station already upgraded to `level`, with the hardpoints of every level up to it. */
export function spawnStation(world: World, faction: FactionId, x: number, y: number, level = 1): Entity {
  const lvl = STATION.levels[level - 1];
  const e = baseEntity(world, 'structure', 'station', faction, x, y);
  Object.assign(e, {
    radius: STATION.radius, level,
    hull: lvl.hull, maxHull: lvl.hull, shield: lvl.shield, maxShield: lvl.shield, shieldRegen: lvl.shieldRegen,
    weapons: armed(STATION.weapons), range: maxRange(STATION.weapons),
  });
  scaleDefenses(world, e);
  world.entities.set(e.id, e);
  for (let i = 0; i < level; i++) addHardpoints(world, e, STATION.levels[i].hardpoints);
  const f = world.factions.get(faction);
  if (f) {
    f.stationId = e.id;
    f.hadStation = true;
    // Default rally point: in front of the station, towards the map center.
    const a = Math.atan2(world.height / 2 - y, world.width / 2 - x);
    f.rallyX = x + Math.cos(a) * 320;
    f.rallyY = y + Math.sin(a) * 320;
  }
  return e;
}

/** Attaches destructible parts to a station or a ship. Ship parts take the ship's size class. */
export function addHardpoints(world: World, parent: Entity, slots: HardpointSlot[]): void {
  for (const slot of slots) {
    const def = HARDPOINTS[slot.role];
    const weapons = slot.weapons ?? def.weapons;
    const hull = slot.hull ?? def.hull;
    const e = baseEntity(world, 'hardpoint', slot.role, parent.faction, parent.x, parent.y);
    Object.assign(e, {
      radius: slot.radius ?? def.radius, hull, maxHull: hull,
      size: parent.kind === 'ship' ? parent.size : 'structure',
      weapons: armed(weapons), range: maxRange(weapons),
      parentId: parent.id, ox: slot.x, oy: slot.y,
      hangar: hangarState(slot.hangar ?? def.hangar),
      retarget: rand(world) * 0.5,
    });
    placeOnParent(e, parent);
    scaleDefenses(world, e);
    world.entities.set(e.id, e);
    if (slot.role === 'engine') {
      parent.engines = (parent.engines ?? 0) + 1;
      parent.maxEngines++;
    }
    if (slot.role === 'shield') parent.shieldGens = (parent.shieldGens ?? 0) + 1;
    // A ship engages from as far as its longest-reaching turret.
    if (parent.kind === 'ship') parent.range = Math.max(parent.range, e.range);
  }
}

function placeOnParent(e: Entity, p: Entity): void {
  const c = Math.cos(p.angle);
  const s = Math.sin(p.angle);
  e.x = p.x + e.ox * c - e.oy * s;
  e.y = p.y + e.ox * s + e.oy * c;
  e.angle = p.angle;
}

/** Keeps ship parts in place on their moving, turning ship. */
export function attachHardpoints(world: World): void {
  for (const e of world.entities.values()) {
    if (e.kind !== 'hardpoint' || e.parentId == null) continue;
    const p = world.entities.get(e.parentId);
    if (p?.kind === 'ship') placeOnParent(e, p);
  }
}

/** Spawns a buildable structure; `built: false` starts it as a construction site. */
export function spawnStructure(
  world: World, faction: FactionId, type: BuildableType, x: number, y: number, built = true,
): Entity {
  const def = BUILDABLES[type];
  const e = baseEntity(world, 'structure', type, faction, x, y);
  Object.assign(e, {
    radius: def.radius,
    hull: built ? def.hull : def.hull * 0.1, maxHull: def.hull,
    shield: built ? def.shield : 0, maxShield: def.shield, shieldRegen: def.shieldRegen,
    weapons: armed(def.weapons), range: maxRange(def.weapons),
    build: built ? 1 : 0, buildTime: def.buildTime, level: 1,
    hangar: hangarState(def.hangar), income: def.income ?? 0,
    retarget: rand(world) * 0.5,
  });
  scaleDefenses(world, e);
  world.entities.set(e.id, e);
  return e;
}

export function dist(a: { x: number; y: number }, b: { x: number; y: number }): number {
  return Math.hypot(a.x - b.x, a.y - b.y);
}

export function childrenOf(world: World, parentId: number): Entity[] {
  const out: Entity[] = [];
  for (const e of world.entities.values()) if (e.parentId === parentId) out.push(e);
  return out;
}

export function hasHardpoint(world: World, stationId: number, role: string): boolean {
  for (const e of world.entities.values()) if (e.parentId === stationId && e.type === role) return true;
  return false;
}

export function stationOf(world: World, faction: FactionId): Entity | null {
  const id = world.factions.get(faction)?.stationId;
  return id == null ? null : world.entities.get(id) ?? null;
}

/** Shields absorb damage first; the rest goes to the hull. A ship's shields cover its parts too. */
export function applyDamage(target: Entity, amount: number, shieldOf: Entity = target): void {
  const absorbed = Math.min(shieldOf.shield, amount);
  shieldOf.shield -= absorbed;
  target.hull -= amount - absorbed;
}

/**
 * Removes destroyed entities. Hardpoints die with what they are attached to, and a lost
 * engine or shield generator weakens its parent.
 */
export function removeDead(world: World): void {
  const dead: Entity[] = [];
  for (const e of world.entities.values()) if (e.hull <= 0) dead.push(e);
  for (let i = 0; i < dead.length; i++) {
    const e = dead[i];
    if (!world.entities.delete(e.id)) continue;
    world.events.push({
      type: 'destroyed', id: e.id, kind: e.kind, entityType: e.type,
      faction: e.faction, x: e.x, y: e.y, radius: e.radius,
    });
    if (e.hero) {
      const f = world.factions.get(e.faction);
      if (f) f.heroCooldowns[e.hero] = HEROES[e.hero].respawn;
    }
    const parent = e.parentId == null ? undefined : world.entities.get(e.parentId);
    if (parent && e.type === 'engine' && parent.engines) parent.engines--;
    if (parent && e.type === 'shield' && parent.shieldGens) parent.shieldGens--;
    dead.push(...childrenOf(world, e.id));
    if (e.type === 'station') {
      const f = world.factions.get(e.faction);
      if (f && f.stationId === e.id) {
        f.stationId = null;
        f.queue = [];
        f.buildQueue = [];
      }
    }
    if (e.fieldId != null) {
      const field = world.fields.find((fl) => fl.id === e.fieldId);
      if (field) field.mineId = null;
    }
  }
}

/** The last faction holding a station wins, once at least two factions had one. */
export function checkVictory(world: World): void {
  if (world.winner) return;
  const contenders = [...world.factions.values()].filter((f) => f.hadStation);
  if (contenders.length < 2) return;
  const alive = contenders.filter((f) => f.stationId != null);
  if (alive.length === 1) {
    world.winner = alive[0].id;
    world.events.push({ type: 'victory', faction: world.winner });
  }
}
