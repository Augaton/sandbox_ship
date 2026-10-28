import { SHIPS, type ShipClass, type ShipDef } from '../data/units';
import { dist, type Entity, type World } from './world';

/** Returns the angle in (-PI, PI]. */
export function wrapAngle(a: number): number {
  a = (a + Math.PI) % (Math.PI * 2);
  if (a <= 0) a += Math.PI * 2;
  return a - Math.PI;
}

function turnTowards(e: Entity, def: ShipDef, angle: number, dt: number): number {
  const diff = wrapAngle(angle - e.angle);
  const step = def.turnRate * dt;
  e.angle = wrapAngle(e.angle + Math.max(-step, Math.min(step, diff)));
  return diff;
}

/**
 * Turns and throttles a ship towards a point, braking so that it stops at the point.
 * Returns true once the ship is within `arrive` of the point.
 */
export function steerTo(e: Entity, def: ShipDef, x: number, y: number, dt: number, arrive: number): boolean {
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

export function brake(e: Entity, def: ShipDef, dt: number): void {
  e.speed = Math.max(0, e.speed - def.accel * dt);
}

function engage(world: World, e: Entity, def: ShipDef, t: Entity, dt: number): void {
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

function hostileTarget(world: World, e: Entity): Entity | null {
  const t = e.targetId == null ? undefined : world.entities.get(e.targetId);
  return t && t.faction !== e.faction ? t : null;
}

/** Idle ships chase targets only this far from their guard position. */
const LEASH = 700;

function updateShip(world: World, e: Entity, dt: number): void {
  const def = SHIPS[e.type as ShipClass];
  const o = e.order;
  const arrive = Math.max(6, e.radius * 0.3);

  if (o?.type === 'attack') {
    const t = world.entities.get(o.targetId);
    if (t) return engage(world, e, def, t, dt);
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
  const ships: Entity[] = [];
  const statics: Entity[] = [];
  for (const e of world.entities.values()) {
    if (e.kind === 'ship') ships.push(e);
    else if (e.kind === 'structure') statics.push(e);
  }
  for (let i = 0; i < ships.length; i++) {
    const a = ships[i];
    for (let j = i + 1; j < ships.length; j++) {
      const b = ships[j];
      if (a.size === 'small' && b.size === 'small') continue;
      const min = (a.radius + b.radius) * 0.9;
      let dx = b.x - a.x;
      let dy = b.y - a.y;
      if (Math.abs(dx) >= min || Math.abs(dy) >= min) continue;
      let d = Math.hypot(dx, dy);
      if (d >= min) continue;
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
    }
    for (const s of statics) {
      const dx = a.x - s.x;
      const dy = a.y - s.y;
      const min = a.radius + s.radius;
      const d = Math.hypot(dx, dy);
      if (d >= min || d === 0) continue;
      a.x = s.x + (dx / d) * min;
      a.y = s.y + (dy / d) * min;
    }
    a.x = Math.max(0, Math.min(world.width, a.x));
    a.y = Math.max(0, Math.min(world.height, a.y));
  }
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
