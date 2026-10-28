import { SHIPS, type FactionId, type ShipClass } from '../data/units';
import { BUILDABLES, MAX_QUEUE, STATION, type BuildableType } from '../data/structures';
import {
  addHardpoints, childrenOf, dist, hasHardpoint, rand, spawnShip, spawnStructure, stationOf,
  type Entity, type Result, type World,
} from './world';

const fail = (reason: string): Result => ({ ok: false, reason });
const OK: Result = { ok: true };

export function updateConstruction(world: World, dt: number): void {
  for (const e of world.entities.values()) {
    if (e.build >= 1) continue;
    const step = dt / e.buildTime;
    e.build = Math.min(1, e.build + step);
    e.hull = Math.min(e.maxHull, e.hull + e.maxHull * 0.9 * step);
    if (e.build >= 1) world.events.push({ type: 'built', id: e.id });
  }
}

function launch(world: World, hangar: Entity, cls: ShipClass, count: number): void {
  // Launched craft hold position away from the station center.
  const parent = hangar.parentId == null ? null : world.entities.get(hangar.parentId);
  const from = parent ?? hangar;
  const a = parent ? Math.atan2(hangar.y - parent.y, hangar.x - parent.x) : rand(world) * Math.PI * 2;
  const gx = hangar.x + Math.cos(a) * 140;
  const gy = hangar.y + Math.sin(a) * 140;
  for (let i = 0; i < count; i++) {
    const s = spawnShip(world, hangar.faction, cls, hangar.x, hangar.y, Math.atan2(hangar.y - from.y, hangar.x - from.x));
    s.guardX = gx + (i - (count - 1) / 2) * 24;
    s.guardY = gy;
    hangar.hangar!.launched.push(s.id);
  }
}

/** Hangars launch squadrons in turn, up to their cap of living craft. */
export function updateHangars(world: World, dt: number): void {
  for (const e of world.entities.values()) {
    const h = e.hangar;
    if (!h || e.build < 1) continue;
    h.launched = h.launched.filter((id) => world.entities.has(id));
    h.timer -= dt;
    if (h.timer > 0) continue;
    const sq = h.def.squadrons[h.next % h.def.squadrons.length];
    if (h.launched.length + sq.size > h.def.cap) continue;
    h.timer = h.def.interval;
    h.next++;
    launch(world, e, sq.cls, sq.size);
  }
}

export function updateEconomy(world: World, dt: number): void {
  for (const f of world.factions.values()) {
    if (f.stationId != null) f.credits += STATION.income * dt;
  }
  for (const e of world.entities.values()) {
    if (e.income > 0 && e.build >= 1) {
      const f = world.factions.get(e.faction);
      if (f) f.credits += e.income * dt;
    }
  }
}

function applyUpgrade(world: World, station: Entity): void {
  const next = STATION.levels[station.level];
  station.level++;
  station.hull += next.hull - station.maxHull;
  station.maxHull = next.hull;
  station.maxShield = next.shield;
  station.shieldRegen = next.shieldRegen;
  addHardpoints(world, station, next.hardpoints);
}

function shipyardPos(world: World, station: Entity): { x: number; y: number } {
  return childrenOf(world, station.id).find((c) => c.type === 'shipyard') ?? station;
}

/** Only the front of the queue progresses. Ship production pauses while the shipyard is down. */
export function updateProduction(world: World, dt: number): void {
  for (const f of world.factions.values()) {
    const item = f.queue[0];
    const station = stationOf(world, f.id);
    if (!item || !station) continue;
    if (item.kind === 'ship' && !hasHardpoint(world, station.id, 'shipyard')) continue;
    item.remaining -= dt;
    if (item.remaining > 0) continue;
    f.queue.shift();
    if (item.kind === 'upgrade') {
      applyUpgrade(world, station);
      continue;
    }
    const p = shipyardPos(world, station);
    const s = spawnShip(world, f.id, item.cls, p.x, p.y, Math.atan2(f.rallyY - p.y, f.rallyX - p.x));
    s.order = { type: 'move', x: f.rallyX + (rand(world) - 0.5) * 120, y: f.rallyY + (rand(world) - 0.5) * 120 };
  }
}

/** Whether the faction can queue this ship class right now, and if not, why. */
export function canBuildShip(world: World, faction: FactionId, cls: ShipClass): Result {
  const def = SHIPS[cls];
  const f = world.factions.get(faction);
  const station = stationOf(world, faction);
  if (!f || !station) return fail('Aucune station');
  if (def.source !== 'shipyard') return fail('Lancé par les hangars');
  if (!hasHardpoint(world, station.id, 'shipyard')) return fail('Chantier naval détruit');
  if (station.level < def.minStationLevel) return fail(`Station niveau ${def.minStationLevel} requise`);
  if (f.queue.length >= MAX_QUEUE) return fail("File d'attente pleine");
  if (f.credits < def.cost) return fail('Crédits insuffisants');
  return OK;
}

export function enqueueShip(world: World, faction: FactionId, cls: ShipClass): Result {
  const r = canBuildShip(world, faction, cls);
  if (!r.ok) return r;
  const def = SHIPS[cls];
  const f = world.factions.get(faction)!;
  f.credits -= def.cost;
  f.queue.push({ kind: 'ship', cls, remaining: def.buildTime, total: def.buildTime, cost: def.cost });
  return OK;
}

export function canUpgrade(world: World, faction: FactionId): Result {
  const f = world.factions.get(faction);
  const station = stationOf(world, faction);
  if (!f || !station) return fail('Aucune station');
  if (station.level >= STATION.levels.length) return fail('Niveau maximum');
  if (f.queue.some((q) => q.kind === 'upgrade')) return fail('Amélioration déjà en cours');
  if (f.queue.length >= MAX_QUEUE) return fail("File d'attente pleine");
  if (f.credits < STATION.levels[station.level].upgradeCost) return fail('Crédits insuffisants');
  return OK;
}

export function enqueueUpgrade(world: World, faction: FactionId): Result {
  const r = canUpgrade(world, faction);
  if (!r.ok) return r;
  const f = world.factions.get(faction)!;
  const next = STATION.levels[stationOf(world, faction)!.level];
  f.credits -= next.upgradeCost;
  f.queue.push({ kind: 'upgrade', remaining: next.upgradeTime, total: next.upgradeTime, cost: next.upgradeCost });
  return OK;
}

/** Cancels a queued item and refunds it in full. */
export function cancelQueued(world: World, faction: FactionId, index: number): void {
  const f = world.factions.get(faction);
  const item = f?.queue[index];
  if (!f || !item) return;
  f.queue.splice(index, 1);
  f.credits += item.cost;
}

export type Placement = Result & { x: number; y: number };

/**
 * Mines snap onto a free asteroid field anywhere on the map. Other structures go
 * inside the station's construction radius, clear of everything else.
 */
export function checkPlacement(world: World, faction: FactionId, type: BuildableType, x: number, y: number): Placement {
  const def = BUILDABLES[type];
  const f = world.factions.get(faction);
  const station = stationOf(world, faction);
  const at = (r: Result, px = x, py = y): Placement => ({ ...r, x: px, y: py });
  if (!f || !station) return at(fail('Aucune station'));
  if (station.level < def.minStationLevel) return at(fail(`Station niveau ${def.minStationLevel} requise`));
  if (f.credits < def.cost) return at(fail('Crédits insuffisants'));
  if (type === 'mine') {
    const field = world.fields.find((fl) => dist(fl, { x, y }) <= fl.radius);
    if (!field) return at(fail("Placer sur un champ d'astéroïdes"));
    if (field.mineId != null) return at(fail('Champ déjà exploité'), field.x, field.y);
    return at(OK, field.x, field.y);
  }
  if (dist(station, { x, y }) > STATION.constructionRadius) return at(fail('Trop loin de la station'));
  if (x < 0 || y < 0 || x > world.width || y > world.height) return at(fail('Hors de la carte'));
  for (const e of world.entities.values()) {
    if (e.kind !== 'ship' && dist(e, { x, y }) < e.radius + def.radius + 10) return at(fail('Emplacement occupé'));
  }
  return at(OK);
}

export function placeStructure(world: World, faction: FactionId, type: BuildableType, x: number, y: number): Result {
  const p = checkPlacement(world, faction, type, x, y);
  if (!p.ok) return { ok: false, reason: p.reason };
  world.factions.get(faction)!.credits -= BUILDABLES[type].cost;
  const e = spawnStructure(world, faction, type, p.x, p.y, false);
  if (type === 'mine') {
    const field = world.fields.find((fl) => fl.x === p.x && fl.y === p.y)!;
    field.mineId = e.id;
    e.fieldId = field.id;
  }
  return OK;
}
