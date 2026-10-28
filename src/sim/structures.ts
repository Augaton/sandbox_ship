import { HEROES, type HeroId } from '../data/heroes';
import { MAX_TECH_LEVEL, TECHS, TECH_BONUS, type TechId } from '../data/research';
import { BUILDABLES, MAX_QUEUE, MINE_DANGER, MINE_ESCORT, MINE_ZONE, STATION, type BuildableType } from '../data/structures';
import { SHIPS, maxRange, shipCost, type FactionId, type ShipClass } from '../data/units';
import {
  addHardpoints, armed, childrenOf, dist, hasHardpoint, hullMult, rand, rescaleForResearch, researchLevel,
  DIFFICULTIES, shieldMult, spawnHero, spawnShip, spawnStructure, stationOf,
  type Entity, type FactionState, type Result, type World,
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
    s.launched = true;
    hangar.hangar!.launched.push(s.id);
  }
}

/** Hangar research: two more craft per level, launches 15 % faster per level. */
function hangarLimits(world: World, e: Entity): { cap: number; interval: number } {
  const lvl = researchLevel(world, e.faction, 'hangars');
  const h = e.hangar!.def;
  return { cap: h.cap + 2 * lvl, interval: h.interval * (1 - 0.15 * lvl) };
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
    const limits = hangarLimits(world, e);
    if (h.launched.length + sq.size > limits.cap) continue;
    h.timer = limits.interval;
    h.next++;
    launch(world, e, sq.cls, sq.size);
  }
}

/** The AI's difficulty scales its income. */
function incomeMult(f: FactionState): number {
  return f.ai ? DIFFICULTIES[f.ai.difficulty].income : 1;
}

export function updateEconomy(world: World, dt: number): void {
  for (const f of world.factions.values()) {
    if (f.stationId != null) f.credits += STATION.income * incomeMult(f) * dt;
  }
  for (const e of world.entities.values()) {
    if (e.income > 0 && e.build >= 1) {
      const f = world.factions.get(e.faction);
      if (f) f.credits += e.income * (1 + TECH_BONUS.mining * f.research.mining) * incomeMult(f) * dt;
    }
  }
}

function applyUpgrade(world: World, station: Entity): void {
  const next = STATION.levels[station.level];
  station.level++;
  const hull = next.hull * hullMult(world, station.faction);
  station.hull += hull - station.maxHull;
  station.maxHull = hull;
  station.maxShield = next.shield * shieldMult(world, station.faction);
  station.shieldRegen = next.shieldRegen * shieldMult(world, station.faction);
  addHardpoints(world, station, next.hardpoints);
}

function shipyardPos(world: World, station: Entity): { x: number; y: number } {
  return childrenOf(world, station.id).find((c) => c.type === 'shipyard') ?? station;
}

/**
 * Two queues run side by side: ships (paused while the shipyard is down) and station
 * upgrades. Only the front of each progresses.
 */
export function updateProduction(world: World, dt: number): void {
  for (const f of world.factions.values()) {
    const station = stationOf(world, f.id);
    if (!station) continue;
    const work = f.buildQueue[0];
    if (work && (work.remaining -= dt) <= 0) {
      f.buildQueue.shift();
      if (work.kind === 'upgrade') applyUpgrade(world, station);
      if (work.kind === 'tier') {
        const target = world.entities.get(work.id);
        if (target) applyTier(world, target);
      }
    }
    for (const id of Object.keys(f.heroCooldowns) as HeroId[]) f.heroCooldowns[id] = Math.max(0, f.heroCooldowns[id]! - dt);
    const item = f.queue[0];
    if (!item || (item.kind !== 'ship' && item.kind !== 'hero') || !hasHardpoint(world, station.id, 'shipyard')) continue;
    item.remaining -= dt;
    if (item.remaining > 0) continue;
    f.queue.shift();
    const p = shipyardPos(world, station);
    const angle = Math.atan2(f.rallyY - p.y, f.rallyX - p.x);
    // Squadrons come out together and fly to the rally point side by side.
    const count = item.kind === 'hero' ? 1 : SHIPS[item.cls].squad ?? 1;
    for (let i = 0; i < count; i++) {
      const s = item.kind === 'hero' ? spawnHero(world, f.id, item.hero, p.x, p.y, angle) : spawnShip(world, f.id, item.cls, p.x + i * 6, p.y, angle);
      s.order = { type: 'move', x: f.rallyX + (rand(world) - 0.5) * 160, y: f.rallyY + (rand(world) - 0.5) * 160 };
    }
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
  if (f.credits < shipCost(cls, faction)) return fail('Crédits insuffisants');
  return OK;
}

export function enqueueShip(world: World, faction: FactionId, cls: ShipClass): Result {
  const r = canBuildShip(world, faction, cls);
  if (!r.ok) return r;
  const def = SHIPS[cls];
  const f = world.factions.get(faction)!;
  const cost = shipCost(cls, faction);
  f.credits -= cost;
  f.queue.push({ kind: 'ship', cls, remaining: def.buildTime, total: def.buildTime, cost });
  return OK;
}

function heroInService(world: World, faction: FactionId, id: HeroId): boolean {
  for (const e of world.entities.values()) if (e.hero === id && e.faction === faction) return true;
  return world.factions.get(faction)?.queue.some((q) => q.kind === 'hero' && q.hero === id) ?? false;
}

/** One of each hero at a time, and a delay before a fallen hero can be built again. */
export function canBuildHero(world: World, faction: FactionId, id: HeroId): Result {
  const def = HEROES[id];
  const f = world.factions.get(faction);
  const station = stationOf(world, faction);
  if (!f || !station) return fail('Aucune station');
  if (def.faction !== faction) return fail("Héros d'une autre faction");
  if (!hasHardpoint(world, station.id, 'shipyard')) return fail('Chantier naval détruit');
  if (station.level < def.minStationLevel) return fail(`Station niveau ${def.minStationLevel} requise`);
  if (heroInService(world, faction, id)) return fail('Déjà en service');
  const wait = f.heroCooldowns[id] ?? 0;
  if (wait > 0) return fail(`Disponible dans ${Math.ceil(wait)} s`);
  if (f.queue.length >= MAX_QUEUE) return fail("File d'attente pleine");
  if (f.credits < def.cost) return fail('Crédits insuffisants');
  return OK;
}

export function enqueueHero(world: World, faction: FactionId, id: HeroId): Result {
  const r = canBuildHero(world, faction, id);
  if (!r.ok) return r;
  const def = HEROES[id];
  world.factions.get(faction)!.credits -= def.cost;
  world.factions.get(faction)!.queue.push({ kind: 'hero', hero: id, remaining: def.buildTime, total: def.buildTime, cost: def.cost });
  return OK;
}

export function canUpgrade(world: World, faction: FactionId): Result {
  const f = world.factions.get(faction);
  const station = stationOf(world, faction);
  if (!f || !station) return fail('Aucune station');
  if (station.level >= STATION.levels.length) return fail('Niveau maximum');
  if (f.buildQueue.some((q) => q.kind === 'upgrade')) return fail('Amélioration déjà en cours');
  if (f.buildQueue.length >= MAX_QUEUE) return fail("File d'attente pleine");
  if (f.credits < STATION.levels[station.level].upgradeCost) return fail('Crédits insuffisants');
  return OK;
}

export function enqueueUpgrade(world: World, faction: FactionId): Result {
  const r = canUpgrade(world, faction);
  if (!r.ok) return r;
  const f = world.factions.get(faction)!;
  const next = STATION.levels[stationOf(world, faction)!.level];
  f.credits -= next.upgradeCost;
  f.buildQueue.push({ kind: 'upgrade', remaining: next.upgradeTime, total: next.upgradeTime, cost: next.upgradeCost });
  return OK;
}

/** Cancels a queued ship (`units`) or station upgrade (`buildings`) and refunds it in full. */
export function cancelQueued(world: World, faction: FactionId, index: number, which: 'units' | 'buildings' = 'units'): void {
  const f = world.factions.get(faction);
  const queue = which === 'units' ? f?.queue : f?.buildQueue;
  const item = queue?.[index];
  if (!f || !queue || !item) return;
  queue.splice(index, 1);
  f.credits += item.cost;
}

/** Next level of a platform or hangar: sturdier, better armed, more craft. */
export function applyTier(world: World, e: Entity): void {
  const tier = BUILDABLES[e.type as BuildableType].tiers?.[e.level - 1];
  if (!tier) return;
  e.level++;
  const hull = tier.hull * hullMult(world, e.faction);
  e.hull += hull - e.maxHull;
  e.maxHull = hull;
  e.maxShield = tier.shield * shieldMult(world, e.faction);
  if (tier.weapons) {
    e.weapons = armed(tier.weapons);
    e.range = maxRange(tier.weapons);
  }
  if (tier.hangar && e.hangar) e.hangar.def = tier.hangar;
}

export function canUpgradeStructure(world: World, faction: FactionId, id: number): Result {
  const e = world.entities.get(id);
  const f = world.factions.get(faction);
  const tiers = e && e.kind === 'structure' ? BUILDABLES[e.type as BuildableType]?.tiers : undefined;
  if (!e || !f || e.faction !== faction || !tiers) return fail('Rien à améliorer');
  if (e.build < 1) return fail('En construction');
  const queued = f.buildQueue.filter((q) => q.kind === 'tier' && q.id === id).length;
  if (e.level + queued > tiers.length) return fail('Niveau maximum');
  if (f.buildQueue.length >= MAX_QUEUE) return fail("File d'attente pleine");
  if (f.credits < tiers[e.level - 1 + queued].cost) return fail('Crédits insuffisants');
  return OK;
}

/** Queues the next level of a platform or hangar in the buildings queue. */
export function upgradeStructure(world: World, faction: FactionId, id: number): Result {
  const r = canUpgradeStructure(world, faction, id);
  if (!r.ok) return r;
  const f = world.factions.get(faction)!;
  const e = world.entities.get(id)!;
  const queued = f.buildQueue.filter((q) => q.kind === 'tier' && q.id === id).length;
  const tier = BUILDABLES[e.type as BuildableType].tiers![e.level - 1 + queued];
  f.credits -= tier.cost;
  f.buildQueue.push({ kind: 'tier', id, remaining: tier.time, total: tier.time, cost: tier.cost });
  return OK;
}

/** Calls off a structure still under construction, for a full refund. */
export function cancelConstruction(world: World, faction: FactionId, id: number): void {
  const e = world.entities.get(id);
  const f = world.factions.get(faction);
  if (!e || !f || e.faction !== faction || e.kind !== 'structure' || e.build >= 1 || e.type === 'station') return;
  world.entities.delete(id);
  f.credits += BUILDABLES[e.type as BuildableType].cost;
  const field = world.fields.find((fl) => fl.mineId === id);
  if (field) field.mineId = null;
}

/** Research levels past the first need a station of that level. */
export function canResearch(world: World, faction: FactionId, tech: TechId): Result {
  const f = world.factions.get(faction);
  const station = stationOf(world, faction);
  if (!f || !station) return fail('Aucune station');
  const level = f.research[tech];
  if (level >= MAX_TECH_LEVEL) return fail('Niveau maximum');
  if (f.researching) return fail('Recherche déjà en cours');
  if (station.level < level + 1) return fail(`Station niveau ${level + 1} requise`);
  if (f.credits < TECHS[tech].costs[level]) return fail('Crédits insuffisants');
  return OK;
}

export function startResearch(world: World, faction: FactionId, tech: TechId): Result {
  const r = canResearch(world, faction, tech);
  if (!r.ok) return r;
  const f = world.factions.get(faction)!;
  const level = f.research[tech];
  const cost = TECHS[tech].costs[level];
  f.credits -= cost;
  f.researching = { tech, remaining: TECHS[tech].times[level], total: TECHS[tech].times[level], cost };
  return OK;
}

export function cancelResearch(world: World, faction: FactionId): void {
  const f = world.factions.get(faction);
  if (!f?.researching) return;
  f.credits += f.researching.cost;
  f.researching = null;
}

export function updateResearch(world: World, dt: number): void {
  for (const f of world.factions.values()) {
    const job = f.researching;
    if (!job || (job.remaining -= dt) > 0) continue;
    const before = job.tech === 'armor' ? hullMult(world, f.id) : shieldMult(world, f.id);
    f.research[job.tech]++;
    f.researching = null;
    rescaleForResearch(world, f.id, job.tech, before);
    world.events.push({ type: 'researched', faction: f.id, tech: job.tech });
  }
}

export type Placement = Result & { x: number; y: number };

/** Centers and radii where a structure of this type may be built. */
export function buildZones(world: World, faction: FactionId, type: BuildableType): { x: number; y: number; r: number }[] {
  const station = stationOf(world, faction);
  const zones = station ? [{ x: station.x, y: station.y, r: STATION.constructionRadius }] : [];
  if (BUILDABLES[type].nearMines) {
    for (const e of world.entities.values()) {
      if (e.faction === faction && e.type === 'mine' && e.build >= 1) zones.push({ x: e.x, y: e.y, r: MINE_ZONE });
    }
  }
  return zones;
}

function inBuildZone(world: World, faction: FactionId, type: BuildableType, x: number, y: number): boolean {
  return buildZones(world, faction, type).some((z) => Math.hypot(z.x - x, z.y - y) <= z.r);
}

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
    // Away from home, a ship must hold the field, and no fight may be going on there.
    const home = dist(station, field) <= STATION.constructionRadius + field.radius;
    let escort = false;
    let fight = false;
    for (const e of world.entities.values()) {
      const d = dist(e, field) - field.radius;
      if (e.faction === faction && e.kind === 'ship' && d <= MINE_ESCORT) escort = true;
      if (e.faction !== faction && e.kind !== 'hardpoint' && (e.kind === 'ship' || e.weapons.length > 0) && d <= MINE_DANGER) fight = true;
    }
    if (fight) return at(fail('Combat près du champ'), field.x, field.y);
    if (!home && !escort) return at(fail('Un vaisseau doit tenir le champ'), field.x, field.y);
    return at(OK, field.x, field.y);
  }
  if (!inBuildZone(world, faction, type, x, y)) {
    return at(fail(def.nearMines ? "Trop loin de la station et des mines" : 'Trop loin de la station'));
  }
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
