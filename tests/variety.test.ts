import { describe, expect, it } from 'vitest';
import { ABILITIES } from '../src/data/abilities';
import { HEROES } from '../src/data/heroes';
import { TECHS } from '../src/data/research';
import { BUILDABLES, STATION } from '../src/data/structures';
import { SHIPS, shipCost } from '../src/data/units';
import { useAbility } from '../src/sim/abilities';
import { damageMult } from '../src/sim/combat';
import { createSkirmish } from '../src/sim/scenario';
import {
  applyTier, canBuildHero, cancelConstruction, canResearch, checkPlacement, enqueueHero, enqueueShip, enqueueUpgrade,
  placeStructure, startResearch, upgradeStructure,
} from '../src/sim/structures';
import { run, step } from '../src/sim/step';
import { isVisible, wasSeen } from '../src/sim/vision';
import { addFaction, createWorld, spawnHero, spawnShip, spawnStation, spawnStructure, stationOf, type World } from '../src/sim/world';

function world(credits = 100000): World {
  const w = createWorld(6000, 4000);
  addFaction(w, 'klaed', credits);
  addFaction(w, 'nairan', credits);
  addFaction(w, 'nautolan', credits);
  return w;
}

const ships = (w: World, type: string) => [...w.entities.values()].filter((e) => e.kind === 'ship' && e.type === type);

describe('two production queues', () => {
  it('build ships while the station upgrades', () => {
    const w = world();
    spawnStation(w, 'klaed', 700, 2000);
    expect(enqueueUpgrade(w, 'klaed').ok).toBe(true);
    expect(enqueueShip(w, 'klaed', 'scout').ok).toBe(true);
    run(w, SHIPS.scout.buildTime + 0.5);
    expect(ships(w, 'scout')).toHaveLength(1);
    const f = w.factions.get('klaed')!;
    expect(f.buildQueue).toHaveLength(1);
    run(w, STATION.levels[1].upgradeTime);
    expect(f.buildQueue).toHaveLength(0);
  });

  it('a construction site can be called off for a full refund', () => {
    const w = world(1000);
    spawnStation(w, 'klaed', 700, 2000);
    placeStructure(w, 'klaed', 'platform', 1000, 2300);
    const site = [...w.entities.values()].find((e) => e.type === 'platform')!;
    cancelConstruction(w, 'klaed', site.id);
    expect(w.entities.has(site.id)).toBe(false);
    expect(w.factions.get('klaed')!.credits).toBeCloseTo(1000, 0);
  });
});

describe('research', () => {
  it('costs credits, takes time and needs a matching station level', () => {
    const w = world();
    spawnStation(w, 'klaed', 700, 2000);
    const f = w.factions.get('klaed')!;
    expect(startResearch(w, 'klaed', 'weapons').ok).toBe(true);
    expect(f.credits).toBe(100000 - TECHS.weapons.costs[0]);
    expect(canResearch(w, 'klaed', 'armor')).toEqual({ ok: false, reason: 'Recherche déjà en cours' });
    run(w, TECHS.weapons.times[0] + 0.5);
    expect(f.research.weapons).toBe(1);
    expect(canResearch(w, 'klaed', 'weapons')).toEqual({ ok: false, reason: 'Station niveau 2 requise' });
  });

  it('armor research thickens the hulls already in service', () => {
    const w = world();
    spawnStation(w, 'klaed', 700, 2000);
    const s = spawnShip(w, 'klaed', 'frigate', 1500, 2000);
    const before = s.maxHull;
    startResearch(w, 'klaed', 'armor');
    run(w, TECHS.armor.times[0] + 0.5);
    expect(s.maxHull).toBeCloseTo(before * 1.1, 5);
    expect(spawnShip(w, 'klaed', 'frigate', 1500, 2100).maxHull).toBeCloseTo(before * 1.1, 5);
  });

  it('weapons research hits harder', () => {
    const w = world();
    const target = spawnShip(w, 'nairan', 'battlecruiser', 1300, 1000);
    const a = spawnShip(w, 'klaed', 'frigate', 1000, 1000);
    w.factions.get('klaed')!.research.weapons = 2;
    step(w);
    target.pilot = { thrust: 0, turn: 0, firing: false, aimX: 0, aimY: 0 };
    a.pilot = { thrust: 0, turn: 0, firing: true, aimX: target.x, aimY: target.y };
    run(w, 0.4);
    const shot = w.projectiles.find((p) => p.faction === 'klaed')!;
    // Base damage (flak 4, laser 16) x Kla'ed trait x two levels of weapons research.
    expect(shot.damage).toBeCloseTo((shot.kind === 'laser' ? 16 : 4) * 1.15 * 1.2, 5);
  });
});

describe('factions', () => {
  it('have their own strengths', () => {
    const w = world();
    addFaction(w, 'nautolan', 0);
    const k = spawnShip(w, 'klaed', 'frigate', 0, 0);
    const n = spawnShip(w, 'nairan', 'frigate', 0, 200);
    const t = spawnShip(w, 'nautolan', 'frigate', 0, 400);
    expect(n.maxShield).toBeGreaterThan(k.maxShield);
    expect(n.maxHull).toBeLessThan(k.maxHull);
    expect(shipCost('frigate', 'nautolan')).toBeLessThan(shipCost('frigate', 'klaed'));
    t.hull = t.maxHull / 2;
    run(w, 10);
    expect(t.hull).toBeCloseTo(t.maxHull * 0.6, 0);
  });
});

describe('abilities', () => {
  it('go on cooldown once used', () => {
    const w = world();
    const s = spawnShip(w, 'klaed', 'frigate', 1000, 1000);
    expect(useAbility(w, s).ok).toBe(true);
    expect(useAbility(w, s).ok).toBe(false);
    run(w, ABILITIES.overcharge.cooldown + 0.1);
    expect(useAbility(w, s).ok).toBe(true);
  });

  it('emergency repair mends nearby allies', () => {
    const w = world();
    const support = spawnShip(w, 'klaed', 'support', 1000, 1000);
    const hurt = spawnShip(w, 'klaed', 'frigate', 1100, 1000);
    hurt.hull = hurt.maxHull * 0.3;
    useAbility(w, support);
    expect(hurt.hull).toBeCloseTo(hurt.maxHull * 0.55, 5);
  });

  it('afterburner doubles speed', () => {
    const w = world();
    const a = spawnShip(w, 'klaed', 'scout', 1000, 1000);
    const b = spawnShip(w, 'klaed', 'scout', 1000, 2000);
    for (const s of [a, b]) s.pilot = { thrust: 1, turn: 0, firing: false, aimX: 0, aimY: 0 };
    useAbility(w, b);
    run(w, 3);
    expect(b.x - 1000).toBeGreaterThan((a.x - 1000) * 1.6);
  });

  it('the AI uses them in combat', () => {
    const w = world();
    w.factions.get('nairan')!.autoAbilities = true;
    const torpedo = spawnShip(w, 'nairan', 'torpedo', 1000, 1000);
    spawnShip(w, 'klaed', 'frigate', 1300, 1000);
    run(w, 2);
    expect(torpedo.ability!.cooldown).toBeGreaterThan(0);
  });
});

describe('new structures and capital ships', () => {
  it('a repair dock mends ships around it', () => {
    const w = world();
    spawnStructure(w, 'klaed', 'repairDock', 1000, 1000);
    const s = spawnShip(w, 'klaed', 'frigate', 1150, 1000);
    s.hull = 100;
    run(w, 5);
    expect(s.hull).toBeCloseTo(100 + BUILDABLES.repairDock.repair!.rate * 5, 0);
  });

  it('the ion cannon needs a level 3 station', () => {
    const w = world();
    spawnStation(w, 'klaed', 700, 2000);
    expect(checkPlacement(w, 'klaed', 'ionCannon', 1000, 2300)).toMatchObject({ ok: false, reason: 'Station niveau 3 requise' });
  });

  it('capital ships close in on enemies even though their guns sit on turrets', () => {
    const w = world();
    const bc = spawnShip(w, 'klaed', 'battlecruiser', 1000, 1000);
    spawnShip(w, 'nairan', 'frigate', 1600, 1000);
    run(w, 1);
    expect(bc.targetId).not.toBeNull();
    expect(bc.speed).toBeGreaterThan(0);
  });
});

describe('build zones and structure levels', () => {
  it('platforms and docks may go near a built mine, far from the station', () => {
    const w = world();
    spawnStation(w, 'klaed', 700, 2000);
    const mine = spawnStructure(w, 'klaed', 'mine', 3000, 1000);
    expect(checkPlacement(w, 'klaed', 'platform', 3200, 1000).ok).toBe(true);
    expect(checkPlacement(w, 'klaed', 'repairDock', 3200, 1100).ok).toBe(true);
    expect(checkPlacement(w, 'klaed', 'hangar', 3200, 1000).ok).toBe(false);
    mine.build = 0.5;
    expect(checkPlacement(w, 'klaed', 'platform', 3200, 1000)).toMatchObject({ ok: false, reason: 'Trop loin de la station et des mines' });
  });

  it('a platform goes up two levels through the buildings queue', () => {
    const w = world();
    spawnStation(w, 'klaed', 700, 2000);
    const p = spawnStructure(w, 'klaed', 'platform', 1000, 2300);
    const tiers = BUILDABLES.platform.tiers!;
    expect(upgradeStructure(w, 'klaed', p.id).ok).toBe(true);
    expect(upgradeStructure(w, 'klaed', p.id).ok).toBe(true);
    expect(upgradeStructure(w, 'klaed', p.id)).toEqual({ ok: false, reason: 'Niveau maximum' });
    run(w, tiers[0].time + tiers[1].time + 1);
    expect(p.level).toBe(3);
    expect(p.maxHull).toBeCloseTo(tiers[1].hull * 1, 5);
    expect(p.weapons).toHaveLength(3);
  });

  it('a hangar at level 2 launches bombers too', () => {
    const w = world();
    spawnStation(w, 'klaed', 700, 2000);
    const h = spawnStructure(w, 'klaed', 'hangar', 1000, 2300);
    applyTier(w, h);
    run(w, 60);
    expect(ships(w, 'bomber').length).toBeGreaterThan(0);
  });
});

describe('random skirmish maps', () => {
  it('depend on the seed, and keep fields clear of the stations', () => {
    const a = createSkirmish({ player: 'klaed', enemy: 'nairan', enemyAI: false, seed: 5 });
    const b = createSkirmish({ player: 'klaed', enemy: 'nairan', enemyAI: false, seed: 5 });
    const c = createSkirmish({ player: 'klaed', enemy: 'nairan', enemyAI: false, seed: 6 });
    expect(a.fields).toEqual(b.fields);
    expect(a.fields).not.toEqual(c.fields);
    for (const f of a.fields) {
      for (const st of [stationOf(a, 'klaed')!, stationOf(a, 'nairan')!]) expect(Math.hypot(f.x - st.x, f.y - st.y)).toBeGreaterThan(450);
    }
  });
});

describe('heroes', () => {
  it('are unique, tied to their faction and station level, and come back after a delay', () => {
    const w = world();
    const station = spawnStation(w, 'klaed', 700, 2000);
    expect(canBuildHero(w, 'klaed', 'nairan-ace')).toEqual({ ok: false, reason: "Héros d'une autre faction" });
    expect(canBuildHero(w, 'klaed', 'klaed-admiral')).toEqual({ ok: false, reason: 'Station niveau 3 requise' });
    expect(enqueueHero(w, 'klaed', 'klaed-ace').ok).toBe(true);
    expect(canBuildHero(w, 'klaed', 'klaed-ace')).toEqual({ ok: false, reason: 'Déjà en service' });
    run(w, HEROES['klaed-ace'].buildTime + 0.5);
    const kesh = [...w.entities.values()].find((e) => e.hero === 'klaed-ace')!;
    expect(kesh.maxHull).toBeCloseTo(SHIPS.scout.hull * HEROES['klaed-ace'].hull, 5);
    kesh.hull = 0;
    step(w);
    expect(canBuildHero(w, 'klaed', 'klaed-ace').ok).toBe(false);
    run(w, HEROES['klaed-ace'].respawn + 1);
    expect(canBuildHero(w, 'klaed', 'klaed-ace').ok).toBe(true);
    expect(station.level).toBe(1);
  });

  it("the admiral's order makes nearby allies hit harder", () => {
    const w = world();
    const admiral = spawnHero(w, 'klaed', 'klaed-admiral', 1000, 1000);
    const frigate = spawnShip(w, 'klaed', 'frigate', 1100, 1000);
    const before = damageMult(w, frigate);
    useAbility(w, admiral);
    step(w);
    expect(damageMult(w, frigate)).toBeCloseTo(before * 1.3, 5);
  });

  it("the engineer's fleet repair mends and recharges allies", () => {
    const w = world();
    const om = spawnHero(w, 'nautolan', 'nautolan-engineer', 1000, 1000);
    const s = spawnShip(w, 'nautolan', 'frigate', 1200, 1000);
    s.hull = s.maxHull * 0.2;
    s.shield = 0;
    useAbility(w, om);
    expect(s.hull).toBeCloseTo(s.maxHull * 0.6, 5);
    expect(s.shield).toBe(s.maxShield);
  });
});

describe('fog of war', () => {
  it('nothing out of sight is targeted, and structures once seen are remembered', () => {
    const w = world();
    w.fog = true;
    // Long guns, short eyes: the ion cannon outranges what a frigate sees.
    const watcher = spawnShip(w, 'klaed', 'frigate', 1000, 1000);
    const far = spawnStructure(w, 'nairan', 'mine', 2600, 1000);
    run(w, 1);
    expect(isVisible(w, 'klaed', far)).toBe(false);
    expect(watcher.targetId).toBeNull();
    watcher.x = 2000;
    run(w, 0.5);
    expect(isVisible(w, 'klaed', far)).toBe(true);
    watcher.x = 1000;
    watcher.order = null;
    watcher.guardX = 1000;
    run(w, 0.5);
    expect(isVisible(w, 'klaed', far)).toBe(false);
    expect(wasSeen(w, 'klaed', far)).toBe(true);
  });
});
