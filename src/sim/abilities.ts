import { ABILITIES, COMMAND_RADIUS, EFFECTS, ORBITAL_DELAY, SPINAL_CHARGE, type AbilityDef, type AbilityId } from '../data/abilities';
import { HEROES } from '../data/heroes';
import type { FactionId } from '../data/units';
import { isVisible } from './vision';
import {
  applyDamage, childrenOf, dist, spawnShip, spawnStructure,
  type Entity, type Result, type Strike, type World, type Zone,
} from './world';

const OK: Result = { ok: true };
const fail = (reason: string): Result => ({ ok: false, reason });

const SPINAL_DAMAGE = 900;
/** Half the width of the spinal beam. */
const SPINAL_WIDTH = 40;
const ORBITAL_DAMAGE = 650;
const SWARM_SIZE = 8;
const SWARM_LIFE = 30;
const TURRET_LIFE = 45;
/** Corrosion eats this share of the hull per second, within bounds. */
const CORROSION = { share: 0.03, min: 10, max: 90 };
const GRAFT_REGEN = 0.03;
/** How often the AI weighs using an ability, in seconds. */
const AI_THINK = 0.5;

/** Where an ability is aimed: a spot on the map, and the enemy there if it needs one. */
export interface Aim {
  x: number;
  y: number;
  id?: number;
}

/** The ship or structure an entity is part of; itself if it is not a part. */
export function ownerOf(world: World, e: Entity): Entity {
  return e.kind === 'hardpoint' && e.parentId != null ? world.entities.get(e.parentId) ?? e : e;
}

/** Whether `e` (or the ship it is a part of) has this ability's effect running. */
export function isActive(world: World, e: Entity, id: AbilityId): boolean {
  const owner = ownerOf(world, e);
  return owner.ability?.id === id && owner.ability.active > 0;
}

export function isStunned(world: World, e: Entity): boolean {
  return ownerOf(world, e).effects.stunned > 0;
}

/** Whether one of its faction's zones of this kind covers the entity. */
export function inZone(world: World, e: Entity, kind: Zone['kind']): boolean {
  return world.zones.some((z) => z.kind === kind && z.faction === e.faction && Math.hypot(z.x - e.x, z.y - e.y) <= z.r);
}

export function inCommandAura(world: World, e: Entity): boolean {
  return inZone(world, ownerOf(world, e), 'command');
}

/** Damage taken multiplier: worse when marked, halved under a shield dome. */
export function damageTaken(world: World, t: Entity): number {
  const owner = ownerOf(world, t);
  return (owner.effects.marked > 0 ? 1.5 : 1) * (inZone(world, owner, 'dome') ? 0.5 : 1);
}

/** Deals damage from a faction, through the shields of the ship a part belongs to. */
export function hurt(world: World, t: Entity, amount: number, from: FactionId): void {
  const parent = t.kind === 'hardpoint' && t.parentId != null ? world.entities.get(t.parentId) : undefined;
  const dealt = amount * damageTaken(world, t);
  applyDamage(t, dealt, parent?.kind === 'ship' ? parent : t);
  t.lastHitBy = from;
  t.hitAt = world.time;
  if (parent) {
    parent.lastHitBy = from;
    parent.hitAt = world.time;
  }
  world.lastDamage = world.time;
  const f = world.factions.get(from);
  if (f) f.stats.damage += dealt;
}

/**
 * Uses the ability of `e`. A point ability takes the spot aimed at, brought back within
 * range; an enemy ability takes the enemy aimed at, which must be seen and within range.
 */
export function useAbility(world: World, e: Entity, aim?: Aim): Result {
  const a = e.ability;
  if (!a) return fail('Aucune capacité');
  if (a.cooldown > 0) return fail(`Prête dans ${Math.ceil(a.cooldown)} s`);
  if (e.effects.stunned > 0) return fail('Paralysé');
  const def = ABILITIES[a.id];
  let point = { x: e.x, y: e.y };
  let target: Entity | undefined;
  if (def.target === 'enemy') {
    const t = aim?.id == null ? undefined : world.entities.get(aim.id);
    if (!t || t.faction === e.faction || !isVisible(world, e.faction, t)) return fail('Choisis un ennemi');
    if (dist(e, t) - t.radius > def.range!) return fail('Cible hors de portée');
    target = ownerOf(world, t);
    point = { x: t.x, y: t.y };
  } else if (def.target === 'point') {
    if (!aim) return fail('Choisis un point');
    const d = Math.hypot(aim.x - e.x, aim.y - e.y);
    const k = d > def.range! ? def.range! / d : 1;
    point = { x: e.x + (aim.x - e.x) * k, y: e.y + (aim.y - e.y) * k };
  }
  a.cooldown = def.cooldown;
  a.active = def.duration;
  apply(world, e, def, point, target);
  world.events.push({ type: 'ability', id: a.id, faction: e.faction, entityId: e.id, x: e.x, y: e.y, tx: point.x, ty: point.y });
  return OK;
}

/** Instant effects; timed ones (sprint, salvo, dome...) only need `active`. */
function apply(world: World, e: Entity, def: AbilityDef, p: { x: number; y: number }, target?: Entity): void {
  switch (def.id) {
    case 'repair':
      for (const t of world.entities.values()) {
        if (t.kind === 'ship' && t.faction === e.faction && t.hull > 0 && dist(t, e) <= def.radius!) t.hull = Math.min(t.maxHull, t.hull + t.maxHull * 0.25);
      }
      break;
    case 'overcharge':
      e.shield = e.maxShield;
      break;
    case 'broadside':
      for (const part of [e, ...childrenOf(world, e.id)]) for (const w of part.weapons) w.cooldown = 0;
      break;
    case 'spinal': {
      const a = Math.atan2(p.y - e.y, p.x - e.x);
      const len = def.range!;
      world.strikes.push({
        kind: 'beam', faction: e.faction, x: e.x, y: e.y, x2: e.x + Math.cos(a) * len, y2: e.y + Math.sin(a) * len,
        radius: SPINAL_WIDTH, damage: SPINAL_DAMAGE, delay: SPINAL_CHARGE, total: SPINAL_CHARGE,
      });
      break;
    }
    case 'orbitalStrike':
      world.strikes.push({
        kind: 'orbital', faction: e.faction, x: p.x, y: p.y, x2: p.x, y2: p.y,
        radius: def.radius!, damage: ORBITAL_DAMAGE, delay: ORBITAL_DELAY, total: ORBITAL_DELAY,
      });
      break;
    case 'huntMark':
      target!.effects.marked = EFFECTS.marked;
      break;
    case 'spores':
      target!.effects.corroded = EFFECTS.corroded;
      break;
    case 'fieldTurret':
      spawnStructure(world, e.faction, 'platform', p.x, p.y).expires = TURRET_LIFE;
      break;
    case 'emp':
      for (const t of world.entities.values()) {
        if (t.kind !== 'ship' || t.faction === e.faction || dist(t, e) > def.radius! + t.radius) continue;
        t.shield = 0;
        t.effects.stunned = EFFECTS.stunned;
      }
      break;
    case 'swarm':
      for (let i = 0; i < SWARM_SIZE; i++) {
        const a = (i / SWARM_SIZE) * Math.PI * 2;
        const s = spawnShip(world, e.faction, 'fighter', e.x + Math.cos(a) * (e.radius + 20), e.y + Math.sin(a) * (e.radius + 20), a);
        s.launched = true;
        s.expires = SWARM_LIFE;
      }
      break;
    default:
      break;
  }
}

function land(world: World, s: Strike): void {
  const reach = s.kind === 'orbital' ? s.radius : Math.hypot(s.x2 - s.x, s.y2 - s.y) / 2 + s.radius;
  const cx = (s.x + s.x2) / 2;
  const cy = (s.y + s.y2) / 2;
  const hit: Entity[] = [];
  world.grid.near(cx, cy, reach, (t) => {
    if (t.faction === s.faction) return;
    const d = s.kind === 'orbital' ? Math.hypot(t.x - s.x, t.y - s.y) : segmentDistance(t, s);
    if (d <= s.radius + t.radius) hit.push(t);
  });
  for (const t of hit) hurt(world, t, s.damage, s.faction);
  world.events.push({ type: 'strike', strike: s });
}

function segmentDistance(p: { x: number; y: number }, s: Strike): number {
  const dx = s.x2 - s.x;
  const dy = s.y2 - s.y;
  const len2 = dx * dx + dy * dy || 1;
  const k = Math.max(0, Math.min(1, ((p.x - s.x) * dx + (p.y - s.y) * dy) / len2));
  return Math.hypot(p.x - (s.x + dx * k), p.y - (s.y + dy * k));
}

function count(world: World, at: { x: number; y: number }, r: number, test: (t: Entity) => boolean): number {
  let n = 0;
  world.grid.near(at.x, at.y, r, (t) => {
    if (test(t) && dist(t, at) <= r + t.radius) n++;
  });
  return n;
}

/** Where to drop an orbital strike: on the thickest visible enemy group in range, if any. */
function bestCluster(world: World, e: Entity, def: AbilityDef): Aim | null {
  let best: Aim | null = null;
  let most = 1;
  const enemy = (t: Entity) => t.faction !== e.faction && t.kind !== 'hardpoint' && isVisible(world, e.faction, t);
  world.grid.near(e.x, e.y, def.range!, (c) => {
    if (!enemy(c) || dist(c, e) > def.range!) return;
    const n = count(world, c, def.radius!, enemy) + (c.kind === 'structure' ? 1 : 0);
    if (n > most) {
      most = n;
      best = { x: c.x, y: c.y };
    }
  });
  return best;
}

/** When and where an automatic ability goes: null to hold it for now. */
function autoAim(world: World, e: Entity): Aim | null {
  const def = ABILITIES[e.ability!.id];
  const self = { x: e.x, y: e.y };
  const t = e.targetId == null ? undefined : world.entities.get(e.targetId);
  const reach = t ? dist(e, t) - t.radius : Infinity;
  const ally = (min: number) => (a: Entity) => a.kind === 'ship' && a.faction === e.faction && a.hull < a.maxHull * min;
  switch (def.id) {
    case 'repair':
      return count(world, e, def.radius!, ally(0.6)) >= 1 ? self : null;
    case 'graft':
      return count(world, e, def.radius!, ally(0.7)) >= 2 ? self : null;
    case 'overcharge':
      return t && e.shield < e.maxShield * 0.3 ? self : null;
    case 'sprint':
      return t && reach > e.range ? self : null;
    case 'emp':
      return count(world, e, def.radius!, (a) => a.kind === 'ship' && a.faction !== e.faction) >= 2 ? self : null;
    case 'shieldDome':
      return t && count(world, e, def.radius!, ally(1.01)) >= 3 ? self : null;
    case 'orbitalStrike':
      return bestCluster(world, e, def);
    case 'spinal':
      return t && dist(e, t) <= def.range! ? { x: t.x, y: t.y } : null;
    case 'huntMark':
    case 'spores':
      return t && reach <= def.range! ? { x: t.x, y: t.y, id: t.id } : null;
    case 'fieldTurret': {
      if (!t || reach > 600) return null;
      const d = dist(e, t) || 1;
      return { x: e.x + ((t.x - e.x) / d) * 150, y: e.y + ((t.y - e.y) / d) * 150 };
    }
    case 'cloak':
    case 'swarm':
      return t ? self : null;
    default:
      // Salvo, barrage, broadside: once in range.
      return t && reach <= e.range ? self : null;
  }
}

export function updateAbilities(world: World, dt: number): void {
  // Zones follow the units that cast them; admirals command around them all the time.
  world.zones = [];
  for (const e of world.entities.values()) {
    if (e.hero && HEROES[e.hero].role === 'admiral') world.zones.push({ kind: 'command', faction: e.faction, x: e.x, y: e.y, r: COMMAND_RADIUS });
    const a = e.ability;
    if (a && a.active > 0 && (a.id === 'shieldDome' || a.id === 'graft')) {
      world.zones.push({ kind: a.id === 'shieldDome' ? 'dome' : 'graft', faction: e.faction, x: e.x, y: e.y, r: ABILITIES[a.id].radius! });
    }
  }
  const think = Math.floor(world.time / AI_THINK) !== Math.floor((world.time - dt) / AI_THINK);
  for (const e of world.entities.values()) {
    const fx = e.effects;
    if (fx.corroded > 0) {
      e.hull -= Math.min(CORROSION.max, Math.max(CORROSION.min, e.maxHull * CORROSION.share)) * dt;
      world.lastDamage = world.time;
      fx.corroded = Math.max(0, fx.corroded - dt);
    }
    if (fx.marked > 0) fx.marked = Math.max(0, fx.marked - dt);
    if (fx.stunned > 0) fx.stunned = Math.max(0, fx.stunned - dt);
    if (e.expires != null) {
      e.expires -= dt;
      if (e.expires <= 0) e.hull = 0;
    }
    if (e.kind === 'ship' && e.hull > 0 && e.hull < e.maxHull && inZone(world, e, 'graft')) {
      e.hull = Math.min(e.maxHull, e.hull + e.maxHull * GRAFT_REGEN * dt);
    }
    const a = e.ability;
    if (!a) continue;
    a.cooldown = Math.max(0, a.cooldown - dt);
    a.active = Math.max(0, a.active - dt);
    if (!think || a.cooldown > 0 || e.pilot || fx.stunned > 0 || !world.factions.get(e.faction)?.autoAbilities) continue;
    const aim = autoAim(world, e);
    if (aim) useAbility(world, e, aim);
  }
  const waiting: Strike[] = [];
  for (const s of world.strikes) {
    s.delay -= dt;
    if (s.delay <= 0) land(world, s);
    else waiting.push(s);
  }
  world.strikes = waiting;
}
