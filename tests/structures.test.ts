import { describe, expect, it } from 'vitest';
import { BUILDABLES, REBUILD, STATION } from '../src/data/structures';
import { SHIPS } from '../src/data/units';
import {
  addFaction, addField, childrenOf, createWorld, spawnShip, spawnStation, spawnStructure, type World,
} from '../src/sim/world';
import {
  cancelQueued, checkPlacement, enqueueShip, enqueueUpgrade, placeStructure,
} from '../src/sim/structures';
import { run, step } from '../src/sim/step';

function world(credits = 0): World {
  const w = createWorld(6000, 4000);
  addFaction(w, 'klaed', credits);
  addFaction(w, 'nairan', 0);
  return w;
}

function ships(w: World, faction: string, type?: string) {
  return [...w.entities.values()].filter((e) => e.kind === 'ship' && e.faction === faction && (!type || e.type === type));
}

describe('production', () => {
  it('debits credits, refuses when short, and delivers the ship', () => {
    const w = world(700);
    spawnStation(w, 'klaed', 700, 2000);
    expect(enqueueShip(w, 'klaed', 'frigate').ok).toBe(true);
    expect(w.factions.get('klaed')!.credits).toBe(50);
    const r = enqueueShip(w, 'klaed', 'frigate');
    expect(r).toEqual({ ok: false, reason: 'Crédits insuffisants' });
    run(w, SHIPS.frigate.buildTime + 0.5);
    expect(ships(w, 'klaed', 'frigate')).toHaveLength(1);
  });

  it('refunds a cancelled item', () => {
    const w = world(1000);
    spawnStation(w, 'klaed', 700, 2000);
    enqueueShip(w, 'klaed', 'frigate');
    cancelQueued(w, 'klaed', 0);
    expect(w.factions.get('klaed')!.credits).toBe(1000);
    expect(w.factions.get('klaed')!.queue).toHaveLength(0);
  });

  it('locks ship classes behind station levels', () => {
    const w = world(100000);
    spawnStation(w, 'klaed', 700, 2000);
    expect(enqueueShip(w, 'klaed', 'battlecruiser').ok).toBe(false);
    expect(enqueueShip(w, 'klaed', 'fighter').ok).toBe(false);
    expect(enqueueUpgrade(w, 'klaed').ok).toBe(true);
    expect(enqueueUpgrade(w, 'klaed').ok).toBe(false);
    run(w, STATION.levels[1].upgradeTime + 0.5);
    expect(enqueueShip(w, 'klaed', 'battlecruiser').ok).toBe(true);
  });

  it('an upgrade raises the hull and adds hardpoints', () => {
    const w = world(100000);
    const st = spawnStation(w, 'klaed', 700, 2000);
    const before = childrenOf(w, st.id).length;
    enqueueUpgrade(w, 'klaed');
    run(w, STATION.levels[1].upgradeTime + 0.5);
    expect(st.level).toBe(2);
    expect(st.maxHull).toBe(STATION.levels[1].hull);
    expect(childrenOf(w, st.id).length).toBe(before + STATION.levels[1].hardpoints.length);
  });

  it('a destroyed shipyard stops ship production', () => {
    const w = world(5000);
    const st = spawnStation(w, 'klaed', 700, 2000);
    enqueueShip(w, 'klaed', 'scout');
    childrenOf(w, st.id).find((c) => c.type === 'shipyard')!.hull = 0;
    run(w, SHIPS.scout.buildTime + 1);
    expect(ships(w, 'klaed', 'scout')).toHaveLength(0);
    expect(enqueueShip(w, 'klaed', 'scout')).toEqual({ ok: false, reason: 'Chantier naval détruit' });
  });
});

describe('hangars', () => {
  it('never exceed their cap of living craft', () => {
    const w = world();
    const st = spawnStation(w, 'klaed', 700, 2000);
    const hangar = childrenOf(w, st.id).find((c) => c.type === 'hangar')!;
    run(w, 300);
    expect(hangar.hangar!.launched.length).toBeLessThanOrEqual(hangar.hangar!.def.cap);
    expect(ships(w, 'klaed', 'fighter').length).toBeGreaterThan(0);
    expect(ships(w, 'klaed', 'bomber').length).toBeGreaterThan(0);
  });

  it('relaunch once craft are lost, stop when destroyed, and start again once rebuilt', () => {
    const w = world();
    const st = spawnStation(w, 'klaed', 700, 2000);
    const hangar = childrenOf(w, st.id).find((c) => c.type === 'hangar')!;
    run(w, 100);
    for (const s of ships(w, 'klaed')) s.hull = 0;
    run(w, 25);
    expect(ships(w, 'klaed').length).toBeGreaterThan(0);
    hangar.hull = 0;
    step(w);
    for (const s of ships(w, 'klaed')) s.hull = 0;
    run(w, REBUILD.time - 10);
    expect(ships(w, 'klaed')).toHaveLength(0);
    run(w, 60);
    expect(childrenOf(w, st.id).some((c) => c.type === 'hangar')).toBe(true);
    expect(ships(w, 'klaed').length).toBeGreaterThan(0);
  });
});

describe('economy and placement', () => {
  it('a station and its built mines earn credits', () => {
    const w = world();
    spawnStation(w, 'klaed', 700, 2000);
    spawnStructure(w, 'klaed', 'mine', 1500, 1500);
    run(w, 10);
    const expected = (STATION.income + BUILDABLES.mine.income!) * 10;
    expect(w.factions.get('klaed')!.credits).toBeCloseTo(expected, 0);
  });

  it('a mine under construction earns nothing', () => {
    const w = world(BUILDABLES.mine.cost);
    spawnStation(w, 'klaed', 700, 2000);
    addField(w, 1000, 1700, 150);
    expect(placeStructure(w, 'klaed', 'mine', 1020, 1690).ok).toBe(true);
    run(w, 5);
    expect(w.factions.get('klaed')!.credits).toBeCloseTo(STATION.income * 5, 0);
  });

  it('mines need a free asteroid field and snap onto it', () => {
    const w = world(10000);
    spawnStation(w, 'klaed', 700, 2000);
    const field = addField(w, 3000, 1000, 150);
    expect(checkPlacement(w, 'klaed', 'mine', 2000, 2000).ok).toBe(false);
    // Far from the station, a ship has to hold the field first.
    expect(checkPlacement(w, 'klaed', 'mine', 3000, 1000)).toMatchObject({ ok: false, reason: 'Un vaisseau doit tenir la position' });
    spawnShip(w, 'klaed', 'scout', 3100, 1100);
    // And no fight may be going on there.
    const raider = spawnShip(w, 'nairan', 'scout', 3300, 1000);
    expect(checkPlacement(w, 'klaed', 'mine', 3000, 1000)).toMatchObject({ ok: false, reason: 'Combat trop proche' });
    w.entities.delete(raider.id);
    const p = checkPlacement(w, 'klaed', 'mine', 3050, 1020);
    expect(p).toMatchObject({ ok: true, x: field.x, y: field.y });
    placeStructure(w, 'klaed', 'mine', 3050, 1020);
    expect(checkPlacement(w, 'klaed', 'mine', 3000, 1000)).toMatchObject({ ok: false, reason: 'Champ déjà exploité' });
  });

  it('other structures stay inside the construction radius and off occupied spots', () => {
    const w = world(10000);
    spawnStation(w, 'klaed', 700, 2000);
    expect(checkPlacement(w, 'klaed', 'platform', 700 + STATION.constructionRadius + 50, 2000).ok).toBe(false);
    expect(checkPlacement(w, 'klaed', 'platform', 700, 2000).ok).toBe(false);
    expect(checkPlacement(w, 'klaed', 'platform', 1000, 2300).ok).toBe(true);
    expect(checkPlacement(w, 'klaed', 'ionCannon', 1000, 2300)).toMatchObject({ ok: false, reason: 'Station niveau 3 requise' });
  });

  it('a structure only works once built', () => {
    const w = world(BUILDABLES.platform.cost);
    spawnStation(w, 'klaed', 700, 2000);
    placeStructure(w, 'klaed', 'platform', 1000, 2300);
    const site = [...w.entities.values()].find((e) => e.type === 'platform')!;
    expect(site.build).toBe(0);
    run(w, BUILDABLES.platform.buildTime + 0.5);
    expect(site.build).toBe(1);
    expect(site.hull).toBeCloseTo(site.maxHull, 0);
    expect(w.events.some((e) => e.type === 'built' && e.id === site.id)).toBe(true);
  });
});
