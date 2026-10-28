import { ABILITIES, COMMAND_RADIUS, FLEET_REPAIR_RADIUS, REPAIR_RADIUS, type AbilityId } from '../data/abilities';
import { childrenOf, dist, type Entity, type Result, type World } from './world';

const fail = (reason: string): Result => ({ ok: false, reason });

/** Whether `e` (or the ship it is a part of) has this ability's effect running. */
export function isActive(world: World, e: Entity, id: AbilityId): boolean {
  const owner = e.kind === 'hardpoint' && e.parentId != null ? world.entities.get(e.parentId) ?? e : e;
  return owner.ability?.id === id && owner.ability.active > 0;
}

export function useAbility(world: World, e: Entity): Result {
  const a = e.ability;
  if (!a) return fail('Aucune capacité');
  if (a.cooldown > 0) return fail(`Prête dans ${Math.ceil(a.cooldown)} s`);
  const def = ABILITIES[a.id];
  a.cooldown = def.cooldown;
  a.active = def.duration;
  if (a.id === 'repair') {
    for (const t of world.entities.values()) {
      if (t.kind === 'ship' && t.faction === e.faction && dist(t, e) <= REPAIR_RADIUS) {
        t.hull = Math.min(t.maxHull, t.hull + t.maxHull * 0.25);
      }
    }
  } else if (a.id === 'fleetRepair') {
    for (const t of world.entities.values()) {
      if (t.kind === 'ship' && t.faction === e.faction && dist(t, e) <= FLEET_REPAIR_RADIUS) {
        t.hull = Math.min(t.maxHull, t.hull + t.maxHull * 0.4);
        t.shield = t.maxShield;
      }
    }
  } else if (a.id === 'overcharge') {
    e.shield = e.maxShield;
  } else if (a.id === 'broadside') {
    for (const part of [e, ...childrenOf(world, e.id)]) for (const w of part.weapons) w.cooldown = 0;
  }
  return { ok: true };
}

/** When an AI faction's ship should use its ability. */
function worthUsing(world: World, e: Entity): boolean {
  const id = e.ability!.id;
  if (id === 'repair' || id === 'fleetRepair') {
    const radius = id === 'repair' ? REPAIR_RADIUS : FLEET_REPAIR_RADIUS;
    for (const t of world.entities.values()) {
      if (t.kind === 'ship' && t.faction === e.faction && t.hull < t.maxHull * 0.6 && dist(t, e) <= radius) return true;
    }
    return false;
  }
  const target = e.targetId == null ? undefined : world.entities.get(e.targetId);
  if (!target) return false;
  if (id === 'overcharge') return e.shield < e.maxShield * 0.3;
  if (id === 'sprint') return dist(e, target) - target.radius > e.range;
  return dist(e, target) - target.radius <= e.range;
}

/** Whether an admiral's attack order covers this entity. */
export function inCommandAura(world: World, e: Entity): boolean {
  return world.auras.some((a) => a.faction === e.faction && Math.hypot(a.x - e.x, a.y - e.y) <= COMMAND_RADIUS);
}

export function updateAbilities(world: World, dt: number): void {
  world.auras = [];
  for (const e of world.entities.values()) {
    if (e.ability?.id === 'command' && e.ability.active > 0) world.auras.push({ faction: e.faction, x: e.x, y: e.y });
  }
  for (const e of world.entities.values()) {
    const a = e.ability;
    if (!a) continue;
    a.cooldown = Math.max(0, a.cooldown - dt);
    a.active = Math.max(0, a.active - dt);
    if (a.cooldown === 0 && !e.pilot && world.factions.get(e.faction)?.autoAbilities && worthUsing(world, e)) {
      useAbility(world, e);
    }
  }
}
