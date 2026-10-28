import { TECH_BONUS } from '../data/research';
import { BUILDABLES, type BuildableType } from '../data/structures';
import { FACTIONS, SHIPS, type ShipClass, type WeaponDef } from '../data/units';
import { HEROES } from '../data/heroes';
import { hurt, inCommandAura, isActive, isStunned, ownerOf } from './abilities';
import { isVisible } from './vision';
import { dist, rand, researchLevel, type Entity, type Pilot, type Projectile, type World } from './world';

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
  world.grid.near(e.x, e.y, range, (t) => {
    if (t.faction === e.faction || !isVisible(world, e.faction, t)) return;
    const d = reach(e, t);
    if (d < bestD) {
      bestD = d;
      best = t;
    }
  });
  return best;
}

/** Ships look further than they shoot, so that they move in to engage. */
function aggroRange(e: Entity): number {
  return e.kind === 'ship' ? Math.max(e.range * 1.6, 350) : e.range;
}

/** Armed entities pick targets; so do ships whose weapons sit on their turrets, to close in. */
export function updateTargeting(world: World, dt: number): void {
  for (const e of world.entities.values()) {
    const seeks = e.weapons.length > 0 || (e.kind === 'ship' && e.range > 0);
    if (!seeks || !operational(e)) continue;
    e.retarget -= dt;
    if (e.retarget > 0) continue;
    e.retarget = RETARGET_INTERVAL;
    e.targetId = nearestHostile(world, e, aggroRange(e))?.id ?? null;
  }
}

/** Faction trait, weapons research and damage abilities. */
export function damageMult(world: World, e: Entity): number {
  let m = FACTIONS[e.faction].trait.damage * (1 + TECH_BONUS.weapons * researchLevel(world, e.faction, 'weapons'));
  if (isActive(world, e, 'barrage')) m *= 1.5;
  if (isActive(world, e, 'broadside')) m *= 2;
  // Out of hiding, the first blows hit hard.
  if (isActive(world, e, 'cloak')) m *= 2;
  const owner = ownerOf(world, e);
  if (owner.hero) m *= HEROES[owner.hero].damage;
  if (inCommandAura(world, owner)) m *= 1.15;
  return m;
}

function reload(world: World, e: Entity, w: WeaponDef): number {
  // A little jitter keeps identical ships from firing in lockstep.
  return w.cooldown * (0.9 + rand(world) * 0.2) * (isActive(world, e, 'salvo') ? 1 / 3 : 1);
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
    speed: w.projectileSpeed, damage: w.damage * damageMult(world, e), mult: w.mult,
    targetId: t.id, homing: !!w.homing, ttl: (w.range * 1.3) / w.projectileSpeed,
  });
}

/** The enemy closest to where a pilot aims, among those within `range`. */
export function aimedTarget(world: World, e: Entity, pilot: Pilot, range: number): Entity | null {
  let best: Entity | null = null;
  let bestD = Infinity;
  for (const t of world.entities.values()) {
    if (t.faction === e.faction || reach(e, t) > range || !isVisible(world, e.faction, t)) continue;
    const d = Math.hypot(t.x - pilot.aimX, t.y - pilot.aimY) - t.radius;
    if (d < bestD) {
      bestD = d;
      best = t;
    }
  }
  return best;
}

/** A hand-flown ship and its turrets only fire while the pilot holds the trigger. */
function pilotOf(world: World, e: Entity): Pilot | null {
  if (e.pilot) return e.pilot;
  return e.parentId == null ? null : world.entities.get(e.parentId)?.pilot ?? null;
}

export function updateWeapons(world: World, dt: number): void {
  for (const e of world.entities.values()) {
    if (e.weapons.length === 0 || !operational(e) || isStunned(world, e)) continue;
    const pilot = pilotOf(world, e);
    if (pilot) {
      for (const w of e.weapons) {
        w.cooldown -= dt;
        if (w.cooldown > 0) continue;
        const t = pilot.firing ? aimedTarget(world, e, pilot, w.def.range) : null;
        if (!t) {
          w.cooldown = 0;
          continue;
        }
        fire(world, e, w.def, t);
        w.cooldown = reload(world, e, w.def);
      }
      continue;
    }
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
      w.cooldown = reload(world, e, w.def);
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
      const owner = ownerOf(world, t);
      const shieldOf = owner.kind === 'ship' ? owner : t;
      const shieldId = shieldOf.shield > 0 ? shieldOf.id : null;
      hurt(world, t, p.damage * (p.mult?.[t.size] ?? 1), p.faction);
      world.events.push({ type: 'hit', x: p.x, y: p.y, kind: p.kind, targetId: t.id, faction: t.faction, shieldId });
      continue;
    }
    if (p.ttl > 0) kept.push(p);
  }
  world.projectiles = kept;
}

/** Stations and ships with shield generators lose their shields with the last generator. */
export function updateShields(world: World, dt: number): void {
  for (const e of world.entities.values()) {
    // Biomechanical hulls mend themselves, but a ship already lost stays lost.
    const regen = FACTIONS[e.faction].trait.hullRegen;
    if (regen > 0 && e.kind === 'ship' && e.hull > 0) e.hull = Math.min(e.maxHull, e.hull + e.maxHull * regen * dt);
    if (e.maxShield <= 0 || !operational(e) || e.effects.stunned > 0) continue;
    if (e.shieldGens === 0) {
      e.shield = 0;
      continue;
    }
    const boost = isActive(world, e, 'overcharge') ? 4 : 1;
    e.shield = Math.min(e.maxShield, e.shield + e.shieldRegen * boost * dt);
  }
}

/** Support ships repair the most damaged friendly ship or structure in range. */
export function updateRepair(world: World, dt: number): void {
  for (const e of world.entities.values()) {
    // Repair docks mend every friendly ship around them.
    const dock = e.kind === 'structure' && operational(e) ? BUILDABLES[e.type as BuildableType]?.repair : undefined;
    if (dock) {
      world.grid.near(e.x, e.y, dock.range, (t) => {
        if (t.kind === 'ship' && t.faction === e.faction && t.hull > 0 && dist(e, t) <= dock.range) {
          t.hull = Math.min(t.maxHull, t.hull + dock.rate * dt);
        }
      });
      continue;
    }
    if (e.kind !== 'ship') continue;
    const repair = SHIPS[e.type as ShipClass].repair;
    if (!repair) continue;
    let best: Entity | null = null;
    let bestRatio = 1;
    world.grid.near(e.x, e.y, repair.range, (t) => {
      // What is already lost this step is not brought back.
      if (t === e || t.faction !== e.faction || !operational(t) || t.hull <= 0) return;
      const ratio = t.hull / t.maxHull;
      if (ratio < bestRatio && dist(e, t) - t.radius <= repair.range) {
        bestRatio = ratio;
        best = t;
      }
    });
    if (best) (best as Entity).hull = Math.min((best as Entity).maxHull, (best as Entity).hull + repair.rate * dt);
  }
}
