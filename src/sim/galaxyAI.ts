import { GALAXY_RULES } from '../data/galaxy';
import { STATION } from '../data/structures';
import { SHIPS, SHIP_CLASSES, type FactionId, type ShipClass } from '../data/units';
import { countShips, fleetValue } from './battle';
import {
  canBuild, defenseValue, enqueueBuild, fleetsAt, galaxyRand, neighbours, sendFleet,
  type Fleet, type Galaxy, type GalaxyBuild, type Planet,
} from './galaxy';

const THINK_INTERVAL = 2;
/** Relative odds of each ship class once unlocked. */
const SHIP_WEIGHTS: Partial<Record<ShipClass, number>> = {
  scout: 1, support: 1, frigate: 4, torpedo: 2, battlecruiser: 3, dreadnought: 2,
};

/** Hops from each planet to the nearest planet held by another faction or with an enemy fleet on it. */
function hopsToEnemy(g: Galaxy, faction: FactionId): Map<number, number> {
  const hops = new Map<number, number>();
  let frontier = g.planets
    .filter((p) => (p.owner && p.owner !== faction) || fleetsAt(g, p.id).some((f) => f.faction !== faction))
    .map((p) => p.id);
  for (const id of frontier) hops.set(id, 0);
  for (let d = 1; frontier.length > 0; d++) {
    const next: number[] = [];
    for (const id of frontier) {
      for (const n of neighbours(g, id)) {
        if (hops.has(n)) continue;
        hops.set(n, d);
        next.push(n);
      }
    }
    frontier = next;
  }
  return hops;
}

/** Strength an attacker would meet at a planet. */
function threat(g: Galaxy, planet: Planet, faction: FactionId): number {
  const owned = planet.owner && planet.owner !== faction ? defenseValue(g, planet) : 0;
  const fleets = fleetsAt(g, planet.id).filter((f) => f.faction !== faction && f.faction !== planet.owner);
  return owned + fleets.reduce((v, f) => v + fleetValue(f.ships), 0);
}

function pickShip(g: Galaxy, planet: Planet): ShipClass | null {
  const options = SHIP_CLASSES.filter((c) => SHIP_WEIGHTS[c] && planet.station >= SHIPS[c].minStationLevel);
  const total = options.reduce((t, c) => t + SHIP_WEIGHTS[c]!, 0);
  let r = galaxyRand(g) * total;
  for (const c of options) {
    r -= SHIP_WEIGHTS[c]!;
    if (r < 0) return c;
  }
  return options[0] ?? null;
}

/** Candidate purchases, most wanted first. */
function wishes(g: Galaxy, faction: FactionId, hops: Map<number, number>): [number, GalaxyBuild][] {
  const own = g.planets.filter((p) => p.owner === faction);
  const byFront = [...own].sort((a, b) => (hops.get(a.id) ?? 99) - (hops.get(b.id) ?? 99));
  const out: [number, GalaxyBuild][] = [];
  for (const p of [...own].sort((a, b) => b.income - a.income)) out.push([p.id, { kind: 'mine' }]);

  const infra: [number, GalaxyBuild][] = [];
  const capital = [...own].sort((a, b) => b.station - a.station)[0];
  if (capital && capital.station < STATION.levels.length) infra.push([capital.id, { kind: 'station' }]);
  const stations = own.filter((p) => p.station > 0).length;
  const staging = byFront.find((p) => p.station === 0 && p.income >= 3);
  if (staging && stations < 1 + own.length / 3) infra.push([staging.id, { kind: 'station' }]);
  for (const p of byFront) if (p.station > 0 && (hops.get(p.id) ?? 99) <= 1) infra.push([p.id, { kind: 'platform' }]);

  const ships: [number, GalaxyBuild][] = [];
  const yard = byFront.find((p) => p.station > 0 && p.queue.length < 2);
  const ai = g.factions.get(faction)!.ai!;
  if (yard) ai.want ??= pickShip(g, yard);
  if (yard && ai.want) ships.push([yard.id, { kind: 'ship', cls: ai.want }]);

  // Alternate between growing the fleet and the infrastructure.
  return Math.floor(g.time / 20) % 2 === 0 ? [...out, ...ships, ...infra] : [...out, ...infra, ...ships];
}

/** Buys the first wish it can; stops to save up when the wish is only too expensive. */
function spend(g: Galaxy, faction: FactionId, hops: Map<number, number>): void {
  for (const [planetId, build] of wishes(g, faction, hops)) {
    const r = canBuild(g, faction, planetId, build);
    if (r.ok) {
      enqueueBuild(g, faction, planetId, build);
      if (build.kind === 'ship') g.factions.get(faction)!.ai!.want = null;
      return;
    }
    if (r.reason === 'Crédits insuffisants') return;
  }
}

function heading(g: Galaxy, faction: FactionId, planetId: number): boolean {
  return [...g.fleets.values()].some((f) => f.faction === faction && (f.to === planetId || f.path.includes(planetId)));
}

function cheapest(f: Fleet): ShipClass {
  return SHIP_CLASSES.filter((c) => f.ships[c]).sort((a, b) => SHIPS[a].cost - SHIPS[b].cost)[0];
}

/** What a planet brings to its owner. */
function worth(p: Planet): number {
  return p.income + p.mines * GALAXY_RULES.mineIncome + p.station * 5;
}

function command(g: Galaxy, faction: FactionId, hops: Map<number, number>): void {
  for (const fleet of [...g.fleets.values()]) {
    if (fleet.faction !== faction || fleet.at == null || fleet.battleId != null) continue;
    const near = neighbours(g, fleet.at).map((id) => g.planets[id]);
    const value = fleetValue(fleet.ships);

    // Defend first: an own planet with enemies on it.
    const rescue = near.find((p) => p.owner === faction && threat(g, p, faction) > 0 && value >= threat(g, p, faction) * 1.2);
    if (rescue) {
      sendFleet(g, fleet.id, rescue.id);
      continue;
    }

    // Then the most valuable enemy planet the fleet can clearly beat.
    const target = near
      .filter((p) => ((p.owner && p.owner !== faction) || threat(g, p, faction) > 0) && value >= threat(g, p, faction) * 1.4 + 300)
      .sort((a, b) => worth(b) - worth(a))[0];
    if (target) {
      sendFleet(g, fleet.id, target.id);
      continue;
    }

    const free = near.find((p) => p.owner == null && !heading(g, faction, p.id) && threat(g, p, faction) === 0);
    if (free && countShips(fleet.ships) >= 2) {
      sendFleet(g, fleet.id, free.id, { [cheapest(fleet)]: 1 });
      continue;
    }

    // Move up towards the front through planets the faction can cross.
    const here = hops.get(fleet.at) ?? 99;
    const closer = near
      .filter((p) => (p.owner == null || p.owner === faction) && (hops.get(p.id) ?? 99) < here)
      .sort((a, b) => (hops.get(a.id) ?? 99) - (hops.get(b.id) ?? 99))[0];
    if (closer && here > 1) sendFleet(g, fleet.id, closer.id);
  }
}

export function updateGalaxyAI(g: Galaxy, dt: number): void {
  for (const f of g.factions.values()) {
    if (!f.ai || f.eliminated) continue;
    f.ai.timer -= dt;
    if (f.ai.timer > 0) continue;
    f.ai.timer = THINK_INTERVAL;
    const hops = hopsToEnemy(g, f.id);
    spend(g, f.id, hops);
    command(g, f.id, hops);
  }
}
