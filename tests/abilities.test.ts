import { describe, expect, it } from 'vitest';
import { ABILITIES, EFFECTS, ORBITAL_DELAY, SPINAL_CHARGE } from '../src/data/abilities';
import { damageTaken, useAbility } from '../src/sim/abilities';
import { damageMult } from '../src/sim/combat';
import type { FactionId } from '../src/data/units';
import { canBuildShip, enqueueShip } from '../src/sim/structures';
import { run, step } from '../src/sim/step';
import { isVisible } from '../src/sim/vision';
import { addFaction, childrenOf, createWorld, spawnHero, spawnShip, spawnStation, type Entity, type World } from '../src/sim/world';

function world(): World {
  const w = createWorld(6000, 4000);
  addFaction(w, 'klaed', 100000);
  addFaction(w, 'nairan', 100000);
  addFaction(w, 'nautolan', 100000);
  return w;
}

/** No guns on the ship nor on its turrets: what a test sees is the ability's work alone. */
function disarm(w: World, e: Entity): Entity {
  for (const part of [e, ...childrenOf(w, e.id)]) part.weapons = [];
  e.range = 0;
  return e;
}

/** A target that stays put and does not shoot back. */
function dummy(w: World, x: number, y: number, faction: FactionId = 'nairan'): Entity {
  return disarm(w, spawnShip(w, faction, 'support', x, y));
}

describe('aimed abilities', () => {
  it('a point ability is brought back within range; an enemy ability needs a seen enemy in range', () => {
    const w = world();
    const kesh = spawnHero(w, 'klaed', 'klaed-ace', 1000, 1000);
    const near = dummy(w, 1500, 1000);
    const far = dummy(w, 2500, 1000);
    expect(useAbility(w, kesh)).toEqual({ ok: false, reason: 'Choisis un ennemi' });
    expect(useAbility(w, kesh, { x: far.x, y: far.y, id: far.id })).toEqual({ ok: false, reason: 'Cible hors de portée' });
    expect(useAbility(w, kesh, { x: near.x, y: near.y, id: near.id }).ok).toBe(true);

    const vorrak = spawnHero(w, 'klaed', 'klaed-admiral', 1000, 2000);
    expect(useAbility(w, vorrak, { x: 5000, y: 2000 }).ok).toBe(true);
    expect(w.strikes[0].x).toBeCloseTo(1000 + ABILITIES.orbitalStrike.range!, 5);
  });

  it('the orbital strike lands after its delay, on enemies only', () => {
    const w = world();
    const vorrak = disarm(w, spawnHero(w, 'klaed', 'klaed-admiral', 1000, 1000));
    const foe = dummy(w, 1600, 1000);
    const friend = dummy(w, 1650, 1000, 'klaed');
    const outside = dummy(w, 1600, 1400);
    const hull = foe.hull + foe.shield;
    useAbility(w, vorrak, { x: 1600, y: 1000 });
    run(w, ORBITAL_DELAY - 0.2);
    expect(foe.hull + foe.shield).toBeCloseTo(hull, 0);
    run(w, 0.4);
    expect(foe.hull + foe.shield).toBeLessThan(hull - 600);
    expect(friend.hull).toBe(friend.maxHull);
    expect(outside.hull).toBe(outside.maxHull);
  });

  it("the titan's spinal beam strikes everything on its line after charging", () => {
    const w = world();
    const titan = disarm(w, spawnShip(w, 'klaed', 'titan', 1000, 1000));
    const onLine = dummy(w, 1800, 1000);
    const offLine = dummy(w, 1800, 1200);
    useAbility(w, titan, { x: 2000, y: 1000 });
    run(w, SPINAL_CHARGE + 0.1);
    expect(onLine.hull).toBeLessThan(onLine.maxHull);
    expect(offLine.hull).toBe(offLine.maxHull);
  });
});

describe('hero powers', () => {
  it('a mark makes the target take half again as much damage', () => {
    const w = world();
    const kesh = disarm(w, spawnHero(w, 'klaed', 'klaed-ace', 1000, 1000));
    const prey = dummy(w, 1300, 1000);
    useAbility(w, kesh, { x: prey.x, y: prey.y, id: prey.id });
    expect(damageTaken(w, prey)).toBe(1.5);
    run(w, EFFECTS.marked + 0.1);
    expect(damageTaken(w, prey)).toBe(1);
  });

  it('a shield dome halves the damage its allies take', () => {
    const w = world();
    const selis = spawnHero(w, 'nairan', 'nairan-admiral', 1000, 1000);
    const inside = dummy(w, 1200, 1000, 'nairan');
    const outside = dummy(w, 2000, 1000, 'nairan');
    useAbility(w, selis);
    step(w);
    expect(damageTaken(w, inside)).toBe(0.5);
    expect(damageTaken(w, outside)).toBe(1);
  });

  it('a cloaked ace cannot be seen or targeted, then shows again', () => {
    const w = world();
    const ilo = spawnHero(w, 'nairan', 'nairan-ace', 1000, 1000);
    const hunter = spawnShip(w, 'klaed', 'frigate', 1200, 1000);
    useAbility(w, ilo);
    run(w, 1);
    expect(isVisible(w, 'klaed', ilo)).toBe(false);
    expect(hunter.targetId).not.toBe(ilo.id);
    run(w, ABILITIES.cloak.duration);
    expect(isVisible(w, 'klaed', ilo)).toBe(true);
  });

  it('an EMP strips shields and holds enemy ships still', () => {
    const w = world();
    const tavi = spawnHero(w, 'nairan', 'nairan-engineer', 1000, 1000);
    const a = spawnShip(w, 'klaed', 'frigate', 1200, 1000);
    a.order = { type: 'move', x: 3000, y: 1000 };
    useAbility(w, tavi);
    expect(a.shield).toBe(0);
    run(w, 1);
    expect(a.speed).toBeLessThan(5);
    expect(a.shield).toBe(0);
    run(w, EFFECTS.stunned);
    expect(a.effects.stunned).toBe(0);
  });

  it('spores eat the hull through the shields', () => {
    const w = world();
    const kael = disarm(w, spawnHero(w, 'nautolan', 'nautolan-ace', 1000, 1000));
    const prey = dummy(w, 1300, 1000);
    useAbility(w, kael, { x: prey.x, y: prey.y, id: prey.id });
    run(w, 2);
    expect(prey.shield).toBeGreaterThan(0);
    expect(prey.hull).toBeLessThan(prey.maxHull);
  });

  it('a graft mends allies over time', () => {
    const w = world();
    const om = spawnHero(w, 'nautolan', 'nautolan-engineer', 1000, 1000);
    // Beyond the reach of Om's own repair beam, within the graft.
    const hurt = dummy(w, 1350, 1000, 'nautolan');
    hurt.hull = hurt.maxHull * 0.3;
    useAbility(w, om);
    run(w, 5);
    expect(hurt.hull).toBeGreaterThan(hurt.maxHull * 0.3 + hurt.maxHull * 0.1);
  });

  it('a swarm and a field turret only last a while', () => {
    const w = world();
    const yshra = spawnHero(w, 'nautolan', 'nautolan-admiral', 1000, 1000);
    const ondra = spawnHero(w, 'klaed', 'klaed-engineer', 3000, 3000);
    useAbility(w, yshra);
    useAbility(w, ondra, { x: 3100, y: 3000 });
    // The mother ship's own hangar launches fighters too: count the swarm only.
    const fighters = () => [...w.entities.values()].filter((e) => e.type === 'fighter' && e.expires != null);
    const turrets = () => [...w.entities.values()].filter((e) => e.type === 'platform' && e.faction === 'klaed');
    expect(fighters()).toHaveLength(8);
    expect(turrets()).toHaveLength(1);
    run(w, 46);
    expect(fighters()).toHaveLength(0);
    expect(turrets()).toHaveLength(0);
  });

  it('an admiral commands: allies around deal more damage', () => {
    const w = world();
    const frigate = spawnShip(w, 'klaed', 'frigate', 1100, 1000);
    const before = damageMult(w, frigate);
    spawnHero(w, 'klaed', 'klaed-admiral', 1000, 1000);
    step(w);
    expect(damageMult(w, frigate)).toBeCloseTo(before * 1.15, 5);
  });

  it('an AI faction uses its powers when they are worth it', () => {
    const w = world();
    w.factions.get('nairan')!.autoAbilities = true;
    const tavi = spawnHero(w, 'nairan', 'nairan-engineer', 1000, 1000);
    spawnShip(w, 'klaed', 'frigate', 1200, 1000);
    run(w, 1);
    expect(tavi.ability!.cooldown).toBe(0);
    spawnShip(w, 'klaed', 'frigate', 1200, 1100);
    run(w, 1);
    expect(tavi.ability!.cooldown).toBeGreaterThan(0);
  });
});

describe('titan', () => {
  it('only one at a time, counting the one in the queue', () => {
    const w = world();
    spawnStation(w, 'klaed', 700, 2000, 3);
    expect(enqueueShip(w, 'klaed', 'titan').ok).toBe(true);
    expect(canBuildShip(w, 'klaed', 'titan')).toEqual({ ok: false, reason: 'Un seul Titan à la fois' });
  });
});
