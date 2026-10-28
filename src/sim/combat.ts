import { SHIPS, type ShipClass, type WeaponDef } from '../data/units';
import { applyDamage, dist, hasHardpoint, rand, type Entity, type Projectile, type World } from './world';

const RETARGET_INTERVAL = 0.5;

function operational(e: Entity): boolean {
  return e.build >= 1;
}

/** Edge-to-center distance: how far `e` must reach to hit `t`. */
function reach(e: Entity, t: Entity): number {
  return dist(e, t) - t.radius;
}

function nearestHostile(world: World, e: Entity, range: number): Entity | null {
  let best: Entity | null = null;
  let bestD = range;
  for (const t of world.entities.values()) {
    if (t.faction === e.faction) continue;
    const d = reach(e, t);
    if (d < bestD) {
      bestD = d;
      best = t;
    }
  }
  return best;
}

/** Ships look further than they shoot, so that they move in to engage. */
function aggroRange(e: Entity): number {
  return e.kind === 'ship' ? Math.max(e.range * 1.6, 350) : e.range;
}

export function updateTargeting(world: World, dt: number): void {
  for (const e of world.entities.values()) {
    if (e.weapons.length === 0 || !operational(e)) continue;
    e.retarget -= dt;
    if (e.retarget > 0) continue;
    e.retarget = RETARGET_INTERVAL;
    e.targetId = nearestHostile(world, e, aggroRange(e))?.id ?? null;
  }
}

function fire(world: World, e: Entity, w: WeaponDef, t: Entity): void {
  // Aim where the target will be when the shot arrives.
  const tvx = Math.cos(t.angle) * t.speed;
  const tvy = Math.sin(t.angle) * t.speed;
  const time = dist(e, t) / w.projectileSpeed;
  const ax = t.x + tvx * time - e.x;
  const ay = t.y + tvy * time - e.y;
  const len = Math.hypot(ax, ay) || 1;
  world.projectiles.push({
    id: world.nextId++, faction: e.faction, kind: w.kind,
    x: e.x, y: e.y,
    vx: (ax / len) * w.projectileSpeed, vy: (ay / len) * w.projectileSpeed,
    speed: w.projectileSpeed, damage: w.damage, mult: w.mult,
    targetId: t.id, homing: !!w.homing, ttl: (w.range * 1.3) / w.projectileSpeed,
  });
}

export function updateWeapons(world: World, dt: number): void {
  for (const e of world.entities.values()) {
    if (e.weapons.length === 0 || !operational(e)) continue;
    const ordered = e.order?.type === 'attack' ? world.entities.get(e.order.targetId) : undefined;
    const auto = e.targetId == null ? undefined : world.entities.get(e.targetId);
    for (const w of e.weapons) {
      w.cooldown -= dt;
      if (w.cooldown > 0) continue;
      const t = [ordered, auto].find((c) => c && reach(e, c) <= w.def.range);
      if (!t) {
        w.cooldown = 0;
        continue;
      }
      fire(world, e, w.def, t);
      // A little jitter keeps identical ships from firing in lockstep.
      w.cooldown = w.def.cooldown * (0.9 + rand(world) * 0.2);
    }
  }
}

export function updateProjectiles(world: World, dt: number): void {
  const kept: Projectile[] = [];
  for (const p of world.projectiles) {
    const t = world.entities.get(p.targetId);
    if (p.homing && t) {
      const len = dist(p, t) || 1;
      p.vx = ((t.x - p.x) / len) * p.speed;
      p.vy = ((t.y - p.y) / len) * p.speed;
    }
    p.x += p.vx * dt;
    p.y += p.vy * dt;
    p.ttl -= dt;
    if (t && dist(p, t) <= t.radius) {
      applyDamage(t, p.damage * (p.mult?.[t.size] ?? 1));
      world.events.push({ type: 'hit', x: p.x, y: p.y, kind: p.kind });
      continue;
    }
    if (p.ttl > 0) kept.push(p);
  }
  world.projectiles = kept;
}

/** A station only has shields while its shield generator hardpoint stands. */
export function updateShields(world: World, dt: number): void {
  for (const e of world.entities.values()) {
    if (e.maxShield <= 0 || !operational(e)) continue;
    if (e.type === 'station' && !hasHardpoint(world, e.id, 'shield')) {
      e.shield = 0;
      continue;
    }
    e.shield = Math.min(e.maxShield, e.shield + e.shieldRegen * dt);
  }
}

/** Support ships repair the most damaged friendly ship or structure in range. */
export function updateRepair(world: World, dt: number): void {
  for (const e of world.entities.values()) {
    if (e.kind !== 'ship') continue;
    const repair = SHIPS[e.type as ShipClass].repair;
    if (!repair) continue;
    let best: Entity | null = null;
    let bestRatio = 1;
    for (const t of world.entities.values()) {
      if (t === e || t.faction !== e.faction || !operational(t)) continue;
      const ratio = t.hull / t.maxHull;
      if (ratio < bestRatio && dist(e, t) - t.radius <= repair.range) {
        bestRatio = ratio;
        best = t;
      }
    }
    if (best) best.hull = Math.min(best.maxHull, best.hull + repair.rate * dt);
  }
}
