import { TECH_BONUS } from '../data/research';
import { FACTIONS, SHIPS, type ShipClass, type ShipDef } from '../data/units';
import { HEROES } from '../data/heroes';
import { inCommandAura, isActive } from './abilities';
import { isVisible } from './vision';
import { dist, researchLevel, type Entity, type World } from './world';

/** Returns the angle in (-PI, PI]. */
export function wrapAngle(a: number): number {
  a = (a + Math.PI) % (Math.PI * 2);
  if (a <= 0) a += Math.PI * 2;
  return a - Math.PI;
}

/** What steering needs of a ship: top speed, acceleration, turn rate and size. */
type Motion = Pick<ShipDef, 'speed' | 'accel' | 'turnRate' | 'size'>;

function turnTowards(e: Entity, def: Motion, angle: number, dt: number): number {
  const diff = wrapAngle(angle - e.angle);
  const step = def.turnRate * dt;
  e.angle = wrapAngle(e.angle + Math.max(-step, Math.min(step, diff)));
  return diff;
}

/**
 * Turns and throttles a ship towards a point, braking so that it stops at the point.
 * Returns true once the ship is within `arrive` of the point.
 */
export function steerTo(e: Entity, def: Motion, x: number, y: number, dt: number, arrive: number): boolean {
  const d = Math.hypot(x - e.x, y - e.y);
  if (d <= arrive) {
    brake(e, def, dt);
    return true;
  }
  const diff = turnTowards(e, def, Math.atan2(y - e.y, x - e.x), dt);
  const align = Math.cos(diff);
  // Fastest speed from which the ship can still stop over the remaining distance.
  let target = Math.min(def.speed, Math.sqrt(2 * def.accel * (d - arrive)));
  target *= align < 0 ? 0.25 : 0.25 + 0.75 * align;
  const dv = def.accel * dt;
  e.speed += Math.max(-dv, Math.min(dv, target - e.speed));
  return false;
}

export function brake(e: Entity, def: Motion, dt: number): void {
  e.speed = Math.max(0, e.speed - def.accel * dt);
}

function engage(world: World, e: Entity, def: Motion, t: Entity, dt: number): void {
  if (def.size === 'small') {
    // Small craft strafe: they keep circling the target instead of parking next to it.
    const orbit = t.radius + e.range * 0.6;
    const a = world.time * ((def.speed * 0.8) / orbit) + e.id;
    steerTo(e, def, t.x + Math.cos(a) * orbit, t.y + Math.sin(a) * orbit, dt, 0);
  } else if (dist(e, t) - t.radius > e.range * 0.85) {
    steerTo(e, def, t.x, t.y, dt, 0);
  } else {
    brake(e, def, dt);
    turnTowards(e, def, Math.atan2(t.y - e.y, t.x - e.x), dt);
  }
}

/**
 * Lost engines slow a ship down and make it turn slower; the faction trait, engine
 * research, the afterburner and an admiral's command change its top speed. A paralysed
 * ship only brakes.
 */
function effectiveDef(world: World, e: Entity): Motion {
  const def = SHIPS[e.type as ShipClass];
  const engines = e.engines == null || e.maxEngines === 0 ? 1 : Math.max(0.15, e.engines / e.maxEngines);
  const boost = FACTIONS[e.faction].trait.speed * (1 + TECH_BONUS.engines * researchLevel(world, e.faction, 'engines'))
    * (isActive(world, e, 'sprint') ? 2 : 1) * (e.hero ? HEROES[e.hero].speed : 1) * (inCommandAura(world, e) ? 1.1 : 1);
  const stunned = e.effects.stunned > 0;
  if (engines === 1 && boost === 1 && !stunned) return def;
  // One object reused ship after ship, read at once: no garbage every step for every ship.
  scratch.speed = stunned ? 0 : def.speed * engines * boost;
  scratch.accel = def.accel * engines * boost;
  scratch.turnRate = stunned ? 0 : def.turnRate * (0.5 + 0.5 * engines);
  scratch.size = def.size;
  return scratch;
}

const scratch: Motion = { speed: 0, accel: 0, turnRate: 0, size: 'small' };

/** Hand flying: the pilot sets thrust and turn; reverse is slower than full ahead. */
function fly(e: Entity, def: Motion, dt: number): void {
  const p = e.pilot!;
  e.angle = wrapAngle(e.angle + p.turn * def.turnRate * dt);
  const target = p.thrust >= 0 ? def.speed * p.thrust : def.speed * 0.35 * p.thrust;
  const dv = def.accel * dt;
  e.speed += Math.max(-dv, Math.min(dv, target - e.speed));
}

function hostileTarget(world: World, e: Entity): Entity | null {
  const t = e.targetId == null ? undefined : world.entities.get(e.targetId);
  return t && t.faction !== e.faction ? t : null;
}

/** Idle ships chase targets only this far from their guard position. */
const LEASH = 700;

function updateShip(world: World, e: Entity, dt: number): void {
  const def = effectiveDef(world, e);
  if (e.pilot) return fly(e, def, dt);
  const o = e.order;
  const arrive = Math.max(6, e.radius * 0.3);

  if (o?.type === 'attack') {
    // A target lost in the fog is lost.
    const t = world.entities.get(o.targetId);
    if (t && isVisible(world, e.faction, t)) return engage(world, e, def, t, dt);
    e.order = null;
    e.guardX = e.x;
    e.guardY = e.y;
  } else if (o) {
    const t = o.type === 'attackMove' ? hostileTarget(world, e) : null;
    if (t) return engage(world, e, def, t, dt);
    if (steerTo(e, def, o.x, o.y, dt, arrive)) {
      e.order = null;
      e.guardX = o.x;
      e.guardY = o.y;
    }
    return;
  }

  const t = hostileTarget(world, e);
  if (t && dist(t, { x: e.guardX, y: e.guardY }) < LEASH) return engage(world, e, def, t, dt);
  if (Math.hypot(e.guardX - e.x, e.guardY - e.y) > 30) steerTo(e, def, e.guardX, e.guardY, dt, arrive);
  else brake(e, def, dt);
}

export function updateShips(world: World, dt: number): void {
  for (const e of world.entities.values()) {
    if (e.kind !== 'ship') continue;
    updateShip(world, e, dt);
    e.x += Math.cos(e.angle) * e.speed * dt;
    e.y += Math.sin(e.angle) * e.speed * dt;
  }
}

/**
 * Pushes overlapping ships apart (small craft may overlap each other) and out of
 * structures. Heavier ships move less.
 */
export function separate(world: World): void {
  world.grid.rebuild(world.entities.values());
  for (const a of world.entities.values()) {
    if (a.kind !== 'ship') continue;
    world.grid.near(a.x, a.y, a.radius, (b) => {
      if (b.kind === 'structure') return pushOut(a, b);
      // Each pair once; small craft may overlap each other.
      if (b.kind !== 'ship' || b.id <= a.id || (a.size === 'small' && b.size === 'small')) return;
      const min = (a.radius + b.radius) * 0.9;
      let dx = b.x - a.x;
      let dy = b.y - a.y;
      if (Math.abs(dx) >= min || Math.abs(dy) >= min) return;
      let d = Math.hypot(dx, dy);
      if (d >= min) return;
      if (d === 0) {
        dx = Math.cos(a.id);
        dy = Math.sin(a.id);
        d = 1;
      }
      const push = (min - d) / d;
      const ma = a.radius * a.radius;
      const mb = b.radius * b.radius;
      const wa = mb / (ma + mb);
      a.x -= dx * push * wa;
      a.y -= dy * push * wa;
      b.x += dx * push * (1 - wa);
      b.y += dy * push * (1 - wa);
    });
    a.x = Math.max(0, Math.min(world.width, a.x));
    a.y = Math.max(0, Math.min(world.height, a.y));
  }
}

function pushOut(a: Entity, s: Entity): void {
  const dx = a.x - s.x;
  const dy = a.y - s.y;
  const min = a.radius + s.radius;
  const d = Math.hypot(dx, dy);
  if (d >= min || d === 0) return;
  a.x = s.x + (dx / d) * min;
  a.y = s.y + (dy / d) * min;
}

/** Grid of `n` offsets centered on (0, 0). */
export function formationOffsets(n: number, spacing: number): { x: number; y: number }[] {
  const cols = Math.ceil(Math.sqrt(n));
  const rows = Math.ceil(n / cols);
  const out: { x: number; y: number }[] = [];
  for (let i = 0; i < n; i++) {
    const c = i % cols;
    const r = Math.floor(i / cols);
    out.push({ x: (c - (cols - 1) / 2) * spacing, y: (r - (rows - 1) / 2) * spacing });
  }
  return out;
}

function ownShips(world: World, ids: number[]): Entity[] {
  const out: Entity[] = [];
  for (const id of ids) {
    const e = world.entities.get(id);
    if (e?.kind === 'ship') out.push(e);
  }
  return out;
}

export function issueMove(world: World, ids: number[], x: number, y: number, attackMove = false): void {
  const ships = ownShips(world, ids);
  if (ships.length === 0) return;
  const spacing = Math.max(...ships.map((s) => s.radius)) * 2.4;
  // Big ships take the middle of the formation.
  ships.sort((a, b) => b.radius - a.radius);
  const offsets = formationOffsets(ships.length, spacing)
    .sort((a, b) => Math.hypot(a.x, a.y) - Math.hypot(b.x, b.y));
  ships.forEach((s, i) => {
    const p = reachable(world, s, x + offsets[i].x, y + offsets[i].y);
    s.order = { type: attackMove ? 'attackMove' : 'move', x: p.x, y: p.y };
  });
}

/**
 * Clamps a destination to the map and moves it out of structures: ships are pushed
 * out of them, so a point inside one could never be reached.
 */
function reachable(world: World, s: Entity, x: number, y: number): { x: number; y: number } {
  const p = { x: Math.max(0, Math.min(world.width, x)), y: Math.max(0, Math.min(world.height, y)) };
  for (const st of world.entities.values()) {
    if (st.kind !== 'structure') continue;
    const min = st.radius + s.radius + 4;
    let dx = p.x - st.x;
    let dy = p.y - st.y;
    let d = Math.hypot(dx, dy);
    if (d >= min) continue;
    if (d === 0) {
      dx = s.x - st.x;
      dy = s.y - st.y;
      d = Math.hypot(dx, dy) || 1;
    }
    p.x = st.x + (dx / d) * min;
    p.y = st.y + (dy / d) * min;
  }
  return p;
}

export function issueAttack(world: World, ids: number[], targetId: number): void {
  for (const s of ownShips(world, ids)) {
    if (s.faction !== world.entities.get(targetId)?.faction) s.order = { type: 'attack', targetId };
  }
}

export function issueStop(world: World, ids: number[]): void {
  for (const s of ownShips(world, ids)) {
    s.order = null;
    s.guardX = s.x;
    s.guardY = s.y;
  }
}
