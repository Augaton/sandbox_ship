import { describe, expect, it } from 'vitest';
import { CAPITALS, GALAXY_RULES, PLANETS } from '../src/data/galaxy';
import { STATION } from '../src/data/structures';
import { SHIPS } from '../src/data/units';
import {
  addShipsAt, cancelBuild, canBuild, chooseBattle, concludeBattle, createGalaxy, enqueueBuild, findPath,
  fleetsAt, neighbours, sendFleet, waitingForPlayer, type Galaxy,
} from '../src/sim/galaxy';
import { runGalaxy, stepGalaxy } from '../src/sim/galaxyStep';

const [WEST, EAST] = CAPITALS;
const id = (name: string) => PLANETS.findIndex((p) => p.name === name);

/** The player watches from a faction that is not on the map: every battle resolves on its own. */
function spectated(): Galaxy {
  const g = createGalaxy('klaed', 'nairan', false);
  g.player = 'nautolan';
  return g;
}

describe('galaxy map', () => {
  it('connects every planet, with mirrored lanes', () => {
    const g = createGalaxy('klaed', 'nairan');
    const seen = new Set([0]);
    const todo = [0];
    while (todo.length) for (const n of neighbours(g, todo.pop()!)) if (!seen.has(n)) seen.add(n), todo.push(n);
    expect(seen.size).toBe(PLANETS.length);
    for (const i of PLANETS.keys()) expect(neighbours(g, i).length).toBe(neighbours(g, (i + 7) % 14).length);
  });

  it('finds the shortest route between planets', () => {
    const g = createGalaxy('klaed', 'nairan');
    const path = findPath(g, WEST, EAST)!;
    expect(path.at(-1)).toBe(EAST);
    for (let i = 0, from: number = WEST; i < path.length; from = path[i++]) expect(neighbours(g, from)).toContain(path[i]);
    expect(findPath(g, WEST, WEST)).toBeNull();
  });

  it('starts each side with a capital, a station and a fleet', () => {
    const g = createGalaxy('klaed', 'nairan');
    expect(g.planets[WEST]).toMatchObject({ owner: 'klaed', station: 1, mines: 1 });
    expect(g.planets[EAST]).toMatchObject({ owner: 'nairan', station: 1, mines: 1 });
    expect(fleetsAt(g, WEST, 'klaed')[0].ships).toEqual({ frigate: 2, scout: 2 });
  });
});

describe('fleets', () => {
  it('fly to a neutral planet and take it', () => {
    const g = spectated();
    const fleet = fleetsAt(g, WEST, 'klaed')[0];
    expect(sendFleet(g, fleet.id, id('Orin')).ok).toBe(true);
    runGalaxy(g, 15);
    expect(g.planets[id('Orin')].owner).toBe('klaed');
    expect(fleet.at).toBe(id('Orin'));
  });

  it('pass through neutral planets on the way', () => {
    const g = spectated();
    const fleet = fleetsAt(g, WEST, 'klaed')[0];
    sendFleet(g, fleet.id, id('Nadir'));
    runGalaxy(g, 40);
    expect(fleet.at).toBe(id('Nadir'));
    expect(g.planets[id('Nadir')].owner).toBe('klaed');
    expect(g.planets[id('Kaldra')].owner).toBeNull();
  });

  it('split when only part of the fleet is sent', () => {
    const g = spectated();
    const fleet = fleetsAt(g, WEST, 'klaed')[0];
    expect(sendFleet(g, fleet.id, id('Maru'), { scout: 1 }).ok).toBe(true);
    expect(fleet.ships).toEqual({ frigate: 2, scout: 1 });
    expect(sendFleet(g, fleet.id, id('Maru'), { scout: 5 })).toEqual({ ok: false, reason: 'Vaisseaux insuffisants' });
    runGalaxy(g, 15);
    expect(fleetsAt(g, id('Maru'), 'klaed')[0].ships).toEqual({ scout: 1 });
  });

  it('merge when they meet at a planet', () => {
    const g = spectated();
    addShipsAt(g, 'klaed', id('Orin'), { frigate: 1 });
    g.planets[id('Orin')].owner = 'klaed';
    sendFleet(g, fleetsAt(g, WEST, 'klaed')[0].id, id('Orin'));
    runGalaxy(g, 15);
    const here = fleetsAt(g, id('Orin'), 'klaed');
    expect(here).toHaveLength(1);
    expect(here[0].ships).toEqual({ frigate: 3, scout: 2 });
  });
});

describe('production', () => {
  it('builds mines, stations and ships, and refunds cancelled work', () => {
    const g = spectated();
    const f = g.factions.get('klaed')!;
    f.credits = 100000;
    const orin = id('Orin');
    g.planets[orin].owner = 'klaed';
    expect(canBuild(g, 'klaed', orin, { kind: 'ship', cls: 'frigate' })).toEqual({ ok: false, reason: 'Station requise' });
    expect(enqueueBuild(g, 'klaed', orin, { kind: 'mine' }).ok).toBe(true);
    expect(enqueueBuild(g, 'klaed', orin, { kind: 'station' }).ok).toBe(true);
    expect(enqueueBuild(g, 'klaed', orin, { kind: 'station' }).ok).toBe(true);
    // The second station order is an upgrade, priced at level 2.
    expect(g.planets[orin].queue[2].cost).toBe(STATION.levels[1].upgradeCost);
    const before = f.credits;
    cancelBuild(g, orin, 2);
    expect(f.credits).toBe(before + STATION.levels[1].upgradeCost);
    runGalaxy(g, 20 + GALAXY_RULES.stationTime + 1);
    expect(g.planets[orin]).toMatchObject({ mines: 1, station: 1 });
    expect(enqueueBuild(g, 'klaed', orin, { kind: 'ship', cls: 'frigate' }).ok).toBe(true);
    runGalaxy(g, SHIPS.frigate.buildTime + 1);
    expect(fleetsAt(g, orin, 'klaed')[0].ships).toEqual({ frigate: 1 });
  });

  it('owned planets and mines bring income', () => {
    const g = spectated();
    const f = g.factions.get('klaed')!;
    f.credits = 0;
    runGalaxy(g, 10);
    const capital = PLANETS[WEST];
    expect(f.credits).toBeCloseTo((capital.income + GALAXY_RULES.mineIncome) * 10, 0);
  });
});

describe('battles', () => {
  it('a strong fleet takes a weakly held planet', () => {
    const g = spectated();
    const kaldra = id('Kaldra');
    Object.assign(g.planets[kaldra], { owner: 'nairan', station: 1 });
    addShipsAt(g, 'klaed', id('Orin'), { battlecruiser: 4, frigate: 6 });
    sendFleet(g, fleetsAt(g, id('Orin'), 'klaed')[0].id, kaldra);
    runGalaxy(g, 120);
    expect(g.battles).toHaveLength(0);
    expect(g.planets[kaldra]).toMatchObject({ owner: 'klaed', station: 0 });
    const winners = fleetsAt(g, kaldra, 'klaed')[0];
    expect(winners.ships.battlecruiser! + (winners.ships.frigate ?? 0)).toBeGreaterThan(0);
    expect(g.events.some((e) => e.type === 'battleEnd' && e.result === 'attacker')).toBe(true);
  });

  it('a fleet that loses everything leaves the planet to its owner', () => {
    const g = spectated();
    const kaldra = id('Kaldra');
    Object.assign(g.planets[kaldra], { owner: 'nairan', station: 3, platforms: 4 });
    addShipsAt(g, 'klaed', id('Orin'), { scout: 2 });
    sendFleet(g, fleetsAt(g, id('Orin'), 'klaed')[0].id, kaldra);
    runGalaxy(g, 120);
    expect(g.planets[kaldra].owner).toBe('nairan');
    expect([...g.fleets.values()].filter((f) => f.faction === 'klaed' && f.at === kaldra)).toHaveLength(0);
  });

  it('wait for the player to choose, then resolve', () => {
    const g = createGalaxy('klaed', 'nairan', false);
    const kaldra = id('Kaldra');
    Object.assign(g.planets[kaldra], { owner: 'nairan', station: 1 });
    sendFleet(g, fleetsAt(g, WEST, 'klaed')[0].id, kaldra);
    runGalaxy(g, 30);
    expect(waitingForPlayer(g)).toBe(true);
    const t = g.time;
    stepGalaxy(g);
    expect(g.time).toBe(t);
    chooseBattle(g, g.battles[0].id, 'auto');
    runGalaxy(g, 120);
    expect(g.battles).toHaveLength(0);
  });

  it('a retreat sends the attacker back where it came from', () => {
    const g = createGalaxy('klaed', 'nairan', false);
    const kaldra = id('Kaldra');
    Object.assign(g.planets[kaldra], { owner: 'nairan', station: 1 });
    const fleet = fleetsAt(g, WEST, 'klaed')[0];
    sendFleet(g, fleet.id, id('Orin'));
    runGalaxy(g, 15);
    sendFleet(g, fleet.id, kaldra);
    runGalaxy(g, 15);
    const b = g.battles[0];
    expect(b.from).toBe(id('Orin'));
    concludeBattle(g, b.id, 'retreat');
    runGalaxy(g, 15);
    expect(fleetsAt(g, id('Orin'), 'klaed')[0].ships).toEqual({ frigate: 2, scout: 2 });
    expect(g.planets[kaldra].owner).toBe('nairan');
  });
});

describe('galaxy AI', () => {
  it('expands, builds up and fights another AI', () => {
    const g = createGalaxy('klaed', 'nairan', true, 3);
    g.factions.get('klaed')!.ai = { timer: 0, want: null };
    g.player = 'nautolan';
    runGalaxy(g, 90);
    // Early skirmishes may favour one side: count the expansion of both together.
    expect(g.planets.filter((p) => p.owner != null).length).toBeGreaterThanOrEqual(6);
    runGalaxy(g, 1200);
    expect(g.planets.some((p) => p.station > 1)).toBe(true);
    expect(g.events.filter((e) => e.type === 'battleEnd').length).toBeGreaterThanOrEqual(5);
  }, 30000);

  it('beats a passive opponent', () => {
    const g = createGalaxy('klaed', 'nairan', true, 5);
    g.player = 'nautolan';
    for (let minute = 0; minute < 40 && !g.winner; minute++) runGalaxy(g, 60);
    expect(g.winner).toBe('nairan');
    expect(g.factions.get('klaed')!.eliminated).toBe(true);
    expect([...g.fleets.values()].some((f) => f.faction === 'klaed')).toBe(false);
  }, 30000);
});
