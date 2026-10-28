import { describe, expect, it } from 'vitest';
import { SHIPS } from '../src/data/units';
import { issueMove } from '../src/sim/movement';
import { run, step } from '../src/sim/step';
import { addFaction, childrenOf, createWorld, dist, spawnShip, type World } from '../src/sim/world';

function world(): World {
  const w = createWorld(6000, 4000);
  addFaction(w, 'klaed', 0);
  addFaction(w, 'nairan', 0);
  return w;
}

const part = (w: World, shipId: number, role: string) => childrenOf(w, shipId).find((c) => c.type === role)!;

describe('ship hardpoints', () => {
  it('capital ships carry their parts, and their weapons on their turrets', () => {
    const w = world();
    const bc = spawnShip(w, 'klaed', 'battlecruiser', 1000, 1000);
    const parts = childrenOf(w, bc.id);
    expect(parts.map((p) => p.type).sort()).toEqual(['engine', 'engine', 'shield', 'turret', 'turret', 'turret']);
    expect(bc.weapons).toHaveLength(0);
    expect(bc.range).toBe(450);
    expect(bc).toMatchObject({ engines: 2, maxEngines: 2, shieldGens: 1 });
    expect(spawnShip(w, 'klaed', 'support', 0, 0)).toMatchObject({ engines: null, shieldGens: null });
    expect(spawnShip(w, 'klaed', 'frigate', 200, 0)).toMatchObject({ engines: null, shieldGens: 1 });
  });

  it('parts follow their ship as it moves and turns', () => {
    const w = world();
    const bc = spawnShip(w, 'klaed', 'battlecruiser', 1000, 1000);
    const engine = part(w, bc.id, 'engine');
    issueMove(w, [bc.id], 1000, 2000);
    run(w, 20);
    // The ship now points down (+y): its engines trail behind it, above.
    expect(engine.y).toBeLessThan(bc.y);
    expect(dist(engine, bc)).toBeCloseTo(Math.hypot(engine.ox, engine.oy), 5);
  });

  it('lost engines slow the ship down', () => {
    const w = world();
    const intact = spawnShip(w, 'klaed', 'battlecruiser', 1000, 1000);
    const crippled = spawnShip(w, 'klaed', 'battlecruiser', 1000, 2000);
    for (const c of childrenOf(w, crippled.id)) if (c.type === 'engine') c.hull = 0;
    step(w);
    expect(crippled.engines).toBe(0);
    issueMove(w, [intact.id], 3000, 1000);
    issueMove(w, [crippled.id], 3000, 2000);
    run(w, 10);
    expect(intact.x - 1000).toBeGreaterThan((crippled.x - 1000) * 3);
  });

  it('the shields go down with the shield generator, and cover the parts until then', () => {
    const w = world();
    const bc = spawnShip(w, 'klaed', 'battlecruiser', 1000, 1000);
    const turret = part(w, bc.id, 'turret');
    // A torpedo bomber's shot lands on a turret: the ship's shields take it.
    const bomber = spawnShip(w, 'nairan', 'torpedo', 1300, 1000, Math.PI);
    bomber.weapons[0].cooldown = 0;
    bomber.targetId = turret.id;
    bomber.order = { type: 'attack', targetId: turret.id };
    run(w, 2);
    expect(turret.hull).toBe(turret.maxHull);
    expect(bc.shield).toBeLessThan(bc.maxShield);
    part(w, bc.id, 'shield').hull = 0;
    run(w, 0.1);
    expect(bc.shield).toBe(0);
  });

  it('a ship whose turrets are gone cannot fire', () => {
    const w = world();
    const bc = spawnShip(w, 'klaed', 'battlecruiser', 1000, 1000);
    for (const c of childrenOf(w, bc.id)) if (c.type === 'turret') c.hull = 0;
    const scout = spawnShip(w, 'nairan', 'scout', 1250, 1000);
    run(w, 10);
    expect(w.entities.has(scout.id)).toBe(true);
    expect(w.projectiles.filter((p) => p.faction === 'klaed')).toHaveLength(0);
  });

  it('parts die with their ship', () => {
    const w = world();
    const dn = spawnShip(w, 'klaed', 'dreadnought', 1000, 1000);
    expect(childrenOf(w, dn.id)).toHaveLength(SHIPS.dreadnought.hardpoints!.length);
    dn.hull = 0;
    step(w);
    expect(childrenOf(w, dn.id)).toHaveLength(0);
  });
});

describe('piloting', () => {
  it('flies where the pilot steers', () => {
    const w = world();
    const s = spawnShip(w, 'klaed', 'frigate', 1000, 1000);
    s.pilot = { thrust: 1, turn: 0, firing: false, aimX: 0, aimY: 0 };
    run(w, 3);
    expect(s.x).toBeGreaterThan(1100);
    expect(Math.abs(s.y - 1000)).toBeLessThan(1);
    s.pilot.turn = 1;
    run(w, 1);
    expect(s.angle).toBeGreaterThan(0.5);
  });

  it('fires only on the trigger, at the enemy nearest the aim', () => {
    const w = world();
    const s = spawnShip(w, 'klaed', 'frigate', 1000, 1000);
    const near = spawnShip(w, 'nairan', 'scout', 1200, 950);
    const aimed = spawnShip(w, 'nairan', 'scout', 1200, 1100);
    for (const e of [near, aimed]) e.pilot = { thrust: 0, turn: 0, firing: false, aimX: 0, aimY: 0 };
    s.pilot = { thrust: 0, turn: 0, firing: false, aimX: 1200, aimY: 1100 };
    run(w, 2);
    expect(w.projectiles.filter((p) => p.faction === 'klaed')).toHaveLength(0);
    s.pilot.firing = true;
    run(w, 0.5);
    const shots = w.projectiles.filter((p) => p.faction === 'klaed');
    expect(shots.length).toBeGreaterThan(0);
    expect(shots.every((p) => p.targetId === aimed.id)).toBe(true);
  });
});
