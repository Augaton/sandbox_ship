import { GALAXY_RULES } from '../data/galaxy';
import { STATION } from '../data/structures';
import { SHIPS, SHIP_CLASSES, type FactionId, type ShipClass } from '../data/units';
import { countShips } from './battle';
import { heroesOf } from '../data/heroes';
import { DIFFICULTIES } from './world';
import type { TechId } from '../data/research';
import {
  canBuild, canResearchTech, defenseValue, enqueueBuild, fleetsAt, galaxyRand, neighbours, sendFleet, startTech,
  fleetStrength, fleetVisible, visiblePlanets,
  type Fleet, type Galaxy, type GalaxyBuild, type Planet,
} from './galaxy';

const THINK_INTERVAL = 2;
/** Relative odds of each ship class once unlocked. */
const SHIP_WEIGHTS: Partial<Record<ShipClass, number>> = {
  scout: 1, bomber: 1, support: 1, frigate: 4, torpedo: 2, battlecruiser: 3, dreadnought: 2, titan: 1,
};

/** Hops from each planet to the nearest planet held by another faction or with an enemy fleet on it. */
function hopsToEnemy(g: Galaxy, faction: FactionId): Map<number, number> {
  const hops = new Map<number, number>();
  const seen = visiblePlanets(g, faction);
  let frontier = g.planets
    .filter((p) => (p.owner && p.owner !== faction) || (seen.has(p.id) && fleetsAt(g, p.id).some((f) => f.faction !== faction)))
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

/** Strength an attacker would meet at a planet, as far as the faction can see. */
function threat(g: Galaxy, planet: Planet, faction: FactionId): number {
  if (!visiblePlanets(g, faction).has(planet.id)) return 0;
  const owned = planet.owner && planet.owner !== faction ? defenseValue(g, planet) : 0;
  const fleets = fleetsAt(g, planet.id).filter((f) => f.faction !== faction && f.faction !== planet.owner);
  return owned + fleets.reduce((v, f) => v + fleetStrength(f), 0);
}

/** Ship weights tilted towards what counters the enemy fleets the faction can see. */
function pickShip(g: Galaxy, faction: FactionId, planet: Planet): ShipClass | null {
  const weights = { ...SHIP_WEIGHTS };
  const seen = visiblePlanets(g, faction);
  let small = 0;
  let large = 0;
  for (const f of g.fleets.values()) {
    if (f.faction === faction || !fleetVisible(g, faction, f, seen)) continue;
    for (const c of SHIP_CLASSES) {
      if (SHIPS[c].size === 'small') small += f.ships[c] ?? 0;
      if (SHIPS[c].size === 'large') large += f.ships[c] ?? 0;
    }
  }
  if (small >= 6) weights.frigate! += 4;
  if (large >= 2) {
    weights.torpedo! += 4;
    weights.bomber! += 3;
  }
  const options = SHIP_CLASSES.filter((c) => weights[c] && planet.station >= SHIPS[c].minStationLevel);
  const total = options.reduce((t, c) => t + weights[c]!, 0);
  let r = galaxyRand(g) * total;
  for (const c of options) {
    r -= weights[c]!;
    if (r < 0) return c;
  }
  return options[0] ?? null;
}

type Wish = { planet: number; build: GalaxyBuild } | { tech: TechId };

/** Least researched tech, combat techs first. */
function pickTech(g: Galaxy, faction: FactionId): TechId {
  const r = g.factions.get(faction)!.research;
  const order: TechId[] = ['weapons', 'armor', 'shields', 'engines', 'mining', 'hangars'];
  return [...order].sort((a, b) => r[a] - r[b])[0];
}

/** Candidate purchases, most wanted first. */
function wishes(g: Galaxy, faction: FactionId, hops: Map<number, number>): Wish[] {
  const own = g.planets.filter((p) => p.owner === faction);
  const byFront = [...own].sort((a, b) => (hops.get(a.id) ?? 99) - (hops.get(b.id) ?? 99));
  const at = (p: Planet, build: GalaxyBuild): Wish => ({ planet: p.id, build });
  const out: Wish[] = [...own].sort((a, b) => b.income - a.income).map((p) => at(p, { kind: 'mine' }));

  const infra: Wish[] = [];
  const capital = [...own].sort((a, b) => b.station - a.station)[0];
  if (capital && capital.station < STATION.levels.length) infra.push(at(capital, { kind: 'station' }));
  const stations = own.filter((p) => p.station > 0).length;
  const staging = byFront.find((p) => p.station === 0 && p.income >= 3);
  if (staging && stations < 1 + own.length / 3) infra.push(at(staging, { kind: 'station' }));
  // Research: a lab in the rear once the empire grows, then one tech after another.
  const rear = byFront.at(-1);
  if (rear && own.length >= 3 && !own.some((p) => p.lab > 0)) infra.push(at(rear, { kind: 'lab' }));
  if (own.some((p) => p.lab > 0)) infra.push({ tech: pickTech(g, faction) });
  for (const p of byFront) {
    if (p.station === 0 || (hops.get(p.id) ?? 99) > 1) continue;
    infra.push(at(p, { kind: 'platform' }), at(p, { kind: 'repairDock' }), at(p, { kind: 'ionCannon' }));
  }

  const ships: Wish[] = [];
  const yard = byFront.find((p) => p.station > 0 && p.units.length < 2);
  const ai = g.factions.get(faction)!.ai!;
  if (yard) ai.want ??= pickShip(g, faction, yard);
  if (yard && ai.want) ships.push(at(yard, { kind: 'ship', cls: ai.want }));
  // Heroes now and then, at the front shipyard.
  const hero = yard && heroesOf(faction).find((h) => {
    const r = canBuild(g, faction, yard.id, { kind: 'hero', hero: h.id });
    return r.ok || r.reason === 'Crédits insuffisants';
  });
  if (yard && hero && galaxyRand(g) < 0.15) ships.unshift(at(yard, { kind: 'hero', hero: hero.id }));

  // A planet facing a stronger enemy fleet next door gets its defenses first.
  const seen = visiblePlanets(g, faction);
  const danger: Wish[] = [];
  for (const p of own) {
    const near = neighbours(g, p.id).flatMap((n) => fleetsAt(g, n)).filter((f) => f.faction !== faction && fleetVisible(g, faction, f, seen));
    const menace = near.reduce((v, f) => v + fleetStrength(f), 0);
    if (menace > 0 && menace > defenseValue(g, p)) {
      danger.push(at(p, p.station === 0 ? { kind: 'station' } : { kind: 'platform' }), at(p, { kind: 'ionCannon' }));
    }
  }
  if (danger.length > 0) return [...danger, ...out, ...ships, ...infra];
  // Otherwise alternate between growing the fleet and the infrastructure.
  return Math.floor(g.time / 20) % 2 === 0 ? [...out, ...ships, ...infra] : [...out, ...infra, ...ships];
}

/** Buys the first wish it can; stops to save up when the wish is only too expensive. */
function spend(g: Galaxy, faction: FactionId, hops: Map<number, number>): void {
  for (const wish of wishes(g, faction, hops)) {
    const r = 'tech' in wish ? canResearchTech(g, faction, wish.tech) : canBuild(g, faction, wish.planet, wish.build);
    if (r.ok) {
      if ('tech' in wish) startTech(g, faction, wish.tech);
      else enqueueBuild(g, faction, wish.planet, wish.build);
      if ('build' in wish && wish.build.kind === 'ship') g.factions.get(faction)!.ai!.want = null;
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
    const value = fleetStrength(fleet);
    // A worn fleet stays home to mend, unless its own planet needs it.
    const worn = (fleet.condition ?? 1) < 0.5;

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
    if (target && !worn) {
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
    f.ai.timer = THINK_INTERVAL * (DIFFICULTIES[f.ai.difficulty].think / DIFFICULTIES.normal.think);
    const hops = hopsToEnemy(g, f.id);
    spend(g, f.id, hops);
    command(g, f.id, hops);
  }
}
