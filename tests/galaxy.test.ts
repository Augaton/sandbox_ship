import { describe, expect, it } from 'vitest';
import { CAPITALS, GALAXY_RULES, PLANETS } from '../src/data/galaxy';
import { STATION } from '../src/data/structures';
import { SHIPS } from '../src/data/units';
import { HEROES } from '../src/data/heroes';
import { TECHS } from '../src/data/research';
import {
  FLEET_REPAIR, addShipsAt, buildTime, cancelBuild, canBuild, canResearchTech, chooseBattle, concludeBattle, createGalaxy,
  enqueueBuild, findPath, fleetVisible, fleetsAt, incomeOf, neighbours, platformCap, researchSpeed, sendFleet, startTech,
  visiblePlanets, waitingForPlayer, type Galaxy,
} from '../src/sim/galaxy';
import { NO_DEFENSE, createPlanetBattle, survivorCondition } from '../src/sim/battle';
import { runGalaxy, stepGalaxy } from '../src/sim/galaxyStep';
import { crosses, generateLayout } from '../src/sim/galaxyGen';
import { deserializeGalaxy, serializeGalaxy } from '../src/sim/galaxySave';

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
    expect(g.planets[orin].buildings[2].cost).toBe(STATION.levels[1].upgradeCost);
    const before = f.credits;
    cancelBuild(g, orin, 2);
    expect(f.credits).toBe(before + STATION.levels[1].upgradeCost);
    runGalaxy(g, 20 + GALAXY_RULES.stationTime + 1);
    expect(g.planets[orin]).toMatchObject({ mines: 1, station: 1 });
    expect(enqueueBuild(g, 'klaed', orin, { kind: 'ship', cls: 'frigate' }).ok).toBe(true);
    runGalaxy(g, SHIPS.frigate.buildTime + 1);
    expect(fleetsAt(g, orin, 'klaed')[0].ships).toEqual({ frigate: 1 });
  });

  it('a station at the top level shows no upgrade price and refuses one', () => {
    const g = spectated();
    const capital = g.planets.find((p) => p.owner === 'klaed')!;
    capital.station = STATION.levels.length;
    expect(() => buildTime(capital, { kind: 'station' })).not.toThrow();
    expect(canBuild(g, 'klaed', capital.id, { kind: 'station' })).toEqual({ ok: false, reason: 'Niveau maximum' });
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
    g.factions.get('klaed')!.ai = { timer: 0, want: null, difficulty: 'normal' };
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

describe('campaign saves', () => {
  it('restore the map, fleets and treasury', () => {
    const g = spectated();
    sendFleet(g, fleetsAt(g, WEST, 'klaed')[0].id, id('Nadir'));
    runGalaxy(g, 25);
    const back = deserializeGalaxy(serializeGalaxy(g))!;
    expect(back.time).toBe(g.time);
    expect(back.planets).toEqual(g.planets);
    expect([...back.fleets.values()]).toEqual([...g.fleets.values()]);
    expect(back.factions.get('klaed')!.credits).toBe(g.factions.get('klaed')!.credits);
    // The game goes on from there.
    runGalaxy(back, 30);
    expect(back.planets[id('Nadir')].owner).toBe('klaed');
  });

  it('drop battles: the fleets meet again and fight once loaded', () => {
    const g = createGalaxy('klaed', 'nairan', false);
    Object.assign(g.planets[id('Kaldra')], { owner: 'nairan', station: 1 });
    sendFleet(g, fleetsAt(g, WEST, 'klaed')[0].id, id('Kaldra'));
    runGalaxy(g, 30);
    expect(g.battles).toHaveLength(1);
    const back = deserializeGalaxy(serializeGalaxy(g))!;
    expect(back.battles).toHaveLength(0);
    stepGalaxy(back);
    expect(back.battles).toHaveLength(1);
  });

  it('reject what is not a save', () => {
    expect(deserializeGalaxy('not json')).toBeNull();
    expect(deserializeGalaxy('{"version":0}')).toBeNull();
  });
});

describe('planet production and research', () => {
  it('builds buildings and ships side by side', () => {
    const g = spectated();
    g.factions.get('klaed')!.credits = 100000;
    expect(enqueueBuild(g, 'klaed', WEST, { kind: 'mine' }).ok).toBe(true);
    expect(enqueueBuild(g, 'klaed', WEST, { kind: 'ship', cls: 'scout' }).ok).toBe(true);
    runGalaxy(g, SHIPS.scout.buildTime + 0.5);
    expect(fleetsAt(g, WEST, 'klaed')[0].ships.scout).toBe(2 + SHIPS.scout.squad!);
    expect(g.planets[WEST].buildings).toHaveLength(1);
  });

  it('research needs a lab, then raises the level', () => {
    const g = spectated();
    const f = g.factions.get('klaed')!;
    f.credits = 100000;
    expect(canResearchTech(g, 'klaed', 'weapons')).toEqual({ ok: false, reason: 'Laboratoire requis' });
    g.planets[WEST].lab = 1;
    expect(startTech(g, 'klaed', 'weapons').ok).toBe(true);
    runGalaxy(g, TECHS.weapons.times[0] + 0.5);
    expect(f.research.weapons).toBe(1);
  });

  it('a battle fought at a planet uses both sides research', () => {
    const g = spectated();
    g.factions.get('klaed')!.research.armor = 2;
    const kaldra = id('Kaldra');
    Object.assign(g.planets[kaldra], { owner: 'nairan', station: 1 });
    sendFleet(g, fleetsAt(g, WEST, 'klaed')[0].id, kaldra);
    runGalaxy(g, 12);
    expect(g.battles[0].world.factions.get('klaed')!.research.armor).toBe(2);
  });

  it('special planets', () => {
    const g = spectated();
    const f = g.factions.get('klaed')!;
    // Rich deposit: double mine income.
    const nadir = g.planets[id('Nadir')];
    Object.assign(nadir, { owner: 'klaed', mines: 1 });
    const capital = g.planets[WEST];
    expect(incomeOf(g, 'klaed')).toBe(capital.income + nadir.income + GALAXY_RULES.mineIncome * (capital.mines + 2));
    // Orbital shipyard: ships 30 % faster.
    const kaldra = g.planets[id('Kaldra')];
    Object.assign(kaldra, { owner: 'klaed', station: 1 });
    expect(buildTime(kaldra, { kind: 'ship', cls: 'frigate' })).toBeCloseTo(SHIPS.frigate.buildTime * 0.7, 5);
    // Fortress: two more platforms.
    const sorel = g.planets[id('Sorel')];
    Object.assign(sorel, { owner: 'klaed', station: 1 });
    expect(platformCap(sorel)).toBe(4);
    // Archives: research a quarter faster.
    expect(researchSpeed(g, 'klaed')).toBe(1);
    g.planets[id('Orin')].owner = 'klaed';
    expect(researchSpeed(g, 'klaed')).toBeCloseTo(4 / 3, 5);
    f.credits = 0;
  });

  it('taking a planet keeps its mines and lab but not what is in orbit', () => {
    const g = spectated();
    const kaldra = id('Kaldra');
    Object.assign(g.planets[kaldra], { owner: 'nairan', mines: 2, lab: 1 });
    sendFleet(g, fleetsAt(g, WEST, 'klaed')[0].id, kaldra);
    runGalaxy(g, 40);
    expect(g.planets[kaldra]).toMatchObject({ owner: 'klaed', mines: 2, lab: 1, station: 0 });
  });
});

describe('generated galaxies', () => {
  it('are the same for the same seed and differ between seeds', () => {
    expect(generateLayout(42)).toEqual(generateLayout(42));
    expect(generateLayout(42).planets).not.toEqual(generateLayout(43).planets);
  });

  it('are connected, spread out, without crossing lanes, with distant capitals', () => {
    for (const seed of [1, 2, 3, 99, 12345]) {
      const layout = generateLayout(seed);
      const { planets, lanes, capitals } = layout;
      expect(planets.length).toBeGreaterThanOrEqual(12);
      const g = createGalaxy('klaed', 'nairan', false, seed, layout);
      const seen = new Set([0]);
      const todo = [0];
      while (todo.length) for (const n of neighbours(g, todo.pop()!)) if (!seen.has(n)) seen.add(n), todo.push(n);
      expect(seen.size).toBe(planets.length);
      for (let i = 0; i < lanes.length; i++) {
        for (let j = i + 1; j < lanes.length; j++) {
          const [a, b] = lanes[i];
          const [c, d] = lanes[j];
          expect(crosses(planets[a], planets[b], planets[c], planets[d])).toBe(false);
        }
      }
      expect(findPath(g, capitals[0], capitals[1])!.length).toBeGreaterThanOrEqual(3);
      expect(g.planets[capitals[0]].owner).toBe('klaed');
    }
  });

  it('saves keep the generated lanes', () => {
    const layout = generateLayout(7);
    const g = createGalaxy('klaed', 'nairan', true, 7, layout);
    expect(deserializeGalaxy(serializeGalaxy(g))!.lanes).toEqual(layout.lanes);
  });
});

describe('heroes on the galaxy map', () => {
  it('are built at a planet, travel with fleets or stay behind, and fall in battle', () => {
    const g = spectated();
    const f = g.factions.get('klaed')!;
    f.credits = 100000;
    expect(enqueueBuild(g, 'klaed', WEST, { kind: 'hero', hero: 'klaed-ace' }).ok).toBe(true);
    expect(canBuild(g, 'klaed', WEST, { kind: 'hero', hero: 'klaed-ace' })).toEqual({ ok: false, reason: 'Déjà en service' });
    runGalaxy(g, 26);
    const fleet = fleetsAt(g, WEST, 'klaed')[0];
    expect(fleet.heroes).toEqual(['klaed-ace']);
    // A partial order leaves the hero home unless it is picked.
    sendFleet(g, fleet.id, id('Orin'), { scout: 1 });
    expect(fleet.heroes).toEqual(['klaed-ace']);
    // Sent alone against a strong planet, the hero falls and must wait to be rebuilt.
    const kaldra = id('Kaldra');
    Object.assign(g.planets[kaldra], { owner: 'nairan', station: 3, platforms: 6 });
    sendFleet(g, fleet.id, kaldra, {}, ['klaed-ace']);
    runGalaxy(g, 40);
    expect([...g.fleets.values()].some((fl) => fl.heroes.includes('klaed-ace'))).toBe(false);
    expect(canBuild(g, 'klaed', WEST, { kind: 'hero', hero: 'klaed-ace' }).ok).toBe(false);
    runGalaxy(g, HEROES['klaed-ace'].respawn);
    expect(canBuild(g, 'klaed', WEST, { kind: 'hero', hero: 'klaed-ace' }).ok).toBe(true);
  }, 30000);
});

describe('galaxy fog of war', () => {
  it('shows nearby planets and fleets only', () => {
    const g = createGalaxy('klaed', 'nairan', false);
    const seen = visiblePlanets(g, 'klaed');
    expect(seen.has(WEST)).toBe(true);
    for (const n of neighbours(g, WEST)) expect(seen.has(n)).toBe(true);
    expect(seen.has(EAST)).toBe(false);
    const enemy = fleetsAt(g, EAST, 'nairan')[0];
    expect(fleetVisible(g, 'klaed', enemy)).toBe(false);
    expect(fleetVisible(g, 'nairan', enemy)).toBe(true);
  });
});

describe('galaxies for three', () => {
  it('give each faction a capital far from the others, and run with three AIs', () => {
    const layout = generateLayout(21, 3);
    expect(new Set(layout.capitals).size).toBe(3);
    const g = createGalaxy('klaed', ['nairan', 'nautolan'], true, 21, layout);
    g.factions.get('klaed')!.ai = { timer: 0, want: null, difficulty: 'normal' };
    g.player = 'nautolan';
    for (const [i, f] of (['klaed', 'nairan', 'nautolan'] as const).entries()) expect(g.planets[layout.capitals[i]].owner).toBe(f);
    for (let i = 0; i < 3; i++) for (let j = i + 1; j < 3; j++) expect(findPath(g, layout.capitals[i], layout.capitals[j])!.length).toBeGreaterThanOrEqual(2);
    g.player = 'klaed';
    g.factions.get('klaed')!.ai = null;
    runGalaxy(g, 300);
    expect(g.planets.filter((p) => p.owner === 'nautolan').length).toBeGreaterThan(1);
  }, 30000);
});

describe('fleet condition', () => {
  it('ships enter a battle as worn as their fleet, and come out with what they kept', () => {
    const w = createPlanetBattle({ faction: 'klaed', ships: { frigate: 2 }, condition: 0.5 }, { faction: 'nairan', ships: {} }, NO_DEFENSE);
    const frigates = [...w.entities.values()].filter((e) => e.type === 'frigate');
    for (const f of frigates) expect(f.hull).toBeCloseTo(f.maxHull * 0.5, 5);
    frigates[0].hull = frigates[0].maxHull * 0.2;
    expect(survivorCondition(w, 'klaed')).toBeCloseTo(0.35, 5);
  });

  it('mend at their own planets, faster with a repair dock, and average out when merged', () => {
    const g = spectated();
    const capital = g.planets.find((p) => p.owner === 'klaed')!;
    const fleet = fleetsAt(g, capital.id, 'klaed')[0];
    fleet.condition = 0.5;
    runGalaxy(g, 10);
    const slow = fleet.condition! - 0.5;
    expect(slow).toBeCloseTo(10 * FLEET_REPAIR.station, 2);
    capital.repairDock = 1;
    fleet.condition = 0.5;
    runGalaxy(g, 10);
    expect(fleet.condition! - 0.5).toBeGreaterThan(slow * 2);
    // Fresh ships of the same value bring the average halfway up.
    fleet.condition = 0.4;
    capital.repairDock = 0;
    const ships = { ...fleet.ships };
    addShipsAt(g, 'klaed', capital.id, ships);
    expect(fleet.condition).toBeCloseTo(0.7, 1);
  });
});
