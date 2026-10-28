import { describe, expect, it } from 'vitest';
import { ABILITIES } from '../src/data/abilities';
import { HEROES } from '../src/data/heroes';
import { TECHS } from '../src/data/research';
import { BUILDABLES, STATION } from '../src/data/structures';
import { FACTIONS, SHIPS, shipCost } from '../src/data/units';
import { useAbility } from '../src/sim/abilities';
import { MAP_SIZES, createSkirmish } from '../src/sim/scenario';
import { survivors } from '../src/sim/battle';
import {
  applyTier, canBuildHero, cancelConstruction, canResearch, checkPlacement, enqueueHero, enqueueShip, enqueueUpgrade,
  placeStructure, startResearch, upgradeStructure,
} from '../src/sim/structures';
import { run, step } from '../src/sim/step';
import { isVisible, wasSeen } from '../src/sim/vision';
import { addFaction, addField, createWorld, newAI, spawnShip, spawnStation, spawnStructure, stationOf, type World } from '../src/sim/world';

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
    expect(ships(w, 'scout')).toHaveLength(SHIPS.scout.squad!);
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
    expect(shot.damage).toBeCloseTo((shot.kind === 'laser' ? 16 : 4) * FACTIONS.klaed.trait.damage * 1.2, 5);
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
    expect(checkPlacement(w, 'klaed', 'ionCannon', 3200, 1000).ok).toBe(false);
    mine.build = 0.5;
    expect(checkPlacement(w, 'klaed', 'platform', 3200, 1000)).toMatchObject({ ok: false, reason: 'Trop loin de la station, des avant-postes et des mines' });
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

  it('an outpost goes where a ship holds the spot, away from any fight, and opens a build area', () => {
    const w = world();
    spawnStation(w, 'klaed', 700, 2000);
    expect(checkPlacement(w, 'klaed', 'outpost', 3500, 2000)).toMatchObject({ ok: false, reason: 'Un vaisseau doit tenir la position' });
    spawnShip(w, 'klaed', 'scout', 3600, 2000);
    const raider = spawnShip(w, 'nairan', 'scout', 3900, 2200);
    expect(checkPlacement(w, 'klaed', 'outpost', 3500, 2000)).toMatchObject({ ok: false, reason: 'Combat trop proche' });
    w.entities.delete(raider.id);
    expect(placeStructure(w, 'klaed', 'outpost', 3500, 2000).ok).toBe(true);
    const outpost = [...w.entities.values()].find((e) => e.type === 'outpost')!;
    // Only once built does it open its area.
    expect(checkPlacement(w, 'klaed', 'platform', 3800, 2000).ok).toBe(false);
    run(w, BUILDABLES.outpost.buildTime + 0.5);
    expect(checkPlacement(w, 'klaed', 'platform', 3800, 2000).ok).toBe(true);
    expect(checkPlacement(w, 'klaed', 'platform', 4100, 2000).ok).toBe(false);
    // Its next level widens the area.
    applyTier(w, outpost);
    expect(checkPlacement(w, 'klaed', 'platform', 4050, 2000).ok).toBe(true);
  });

  it('a field inside an outpost area needs no escort', () => {
    const w = world();
    spawnStation(w, 'klaed', 700, 2000);
    spawnStructure(w, 'klaed', 'outpost', 3500, 2000);
    const field = addField(w, 3800, 2000, 120);
    expect(checkPlacement(w, 'klaed', 'mine', field.x, field.y).ok).toBe(true);
  });
});

describe('random skirmish maps', () => {
  it('depend on the seed, and keep fields clear of the stations', () => {
    const a = createSkirmish({ player: 'klaed', enemies: ['nairan'], enemyAI: false, seed: 5 });
    const b = createSkirmish({ player: 'klaed', enemies: ['nairan'], enemyAI: false, seed: 5 });
    const c = createSkirmish({ player: 'klaed', enemies: ['nairan'], enemyAI: false, seed: 6 });
    expect(a.fields).toEqual(b.fields);
    expect(a.fields).not.toEqual(c.fields);
    for (const f of a.fields) {
      for (const st of [stationOf(a, 'klaed')!, stationOf(a, 'nairan')!]) expect(Math.hypot(f.x - st.x, f.y - st.y)).toBeGreaterThan(300);
    }
    // Every station has two fields right behind it.
    for (const st of [stationOf(a, 'klaed')!, stationOf(a, 'nairan')!]) {
      expect(a.fields.filter((f) => Math.hypot(f.x - st.x, f.y - st.y) < 450).length).toBeGreaterThanOrEqual(2);
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

describe('free-for-all and map sizes', () => {
  it('three factions each get a station and two fields behind it, on maps of three sizes', () => {
    for (const size of ['small', 'medium', 'large'] as const) {
      const w = createSkirmish({ player: 'klaed', enemies: ['nairan', 'nautolan'], enemyAI: true, seed: 9, size });
      expect(w.width).toBe(MAP_SIZES[size].w);
      for (const f of ['klaed', 'nairan', 'nautolan'] as const) {
        const st = stationOf(w, f)!;
        expect(st).toBeTruthy();
        expect(w.fields.filter((fl) => Math.hypot(fl.x - st.x, fl.y - st.y) < 450).length).toBeGreaterThanOrEqual(2);
      }
    }
  });

  it('three AIs play on without breaking', () => {
    const w = createSkirmish({ player: 'klaed', enemies: ['nairan', 'nautolan'], enemyAI: true, seed: 4, size: 'small' });
    w.factions.get('klaed')!.ai = newAI();
    run(w, 240, 1 / 30);
    for (const e of w.entities.values()) expect(Number.isFinite(e.x) && Number.isFinite(e.hull)).toBe(true);
  }, 30000);
});

describe('squadrons and retreats', () => {
  it('a bomber order delivers a squadron; hangar craft never leave the battle', () => {
    const w = world();
    spawnStation(w, 'klaed', 700, 2000);
    expect(enqueueShip(w, 'klaed', 'bomber').ok).toBe(true);
    run(w, SHIPS.bomber.buildTime + 0.5);
    const built = ships(w, 'bomber').filter((e) => !e.launched);
    expect(built).toHaveLength(SHIPS.bomber.squad!);
    run(w, 20);
    expect(ships(w, 'fighter').every((e) => e.launched)).toBe(true);
    expect(survivors(w, 'klaed')).toEqual({ bomber: SHIPS.bomber.squad! });
  });

  it('the AI pulls a badly damaged ship back to its repair dock', () => {
    const w = world();
    const f = w.factions.get('nairan')!;
    f.ai = newAI();
    spawnStation(w, 'nairan', 5000, 2000);
    const dock = spawnStructure(w, 'nairan', 'repairDock', 4600, 2000);
    const bc = spawnShip(w, 'nairan', 'battlecruiser', 2000, 2000);
    bc.hull = bc.maxHull * 0.2;
    run(w, 2);
    expect(bc.retreating).toBe(true);
    run(w, 60);
    expect(Math.hypot(bc.x - dock.x, bc.y - dock.y)).toBeLessThan(400);
  });
});
