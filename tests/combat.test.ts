import { describe, expect, it } from 'vitest';
import {
  addFaction, applyDamage, childrenOf, createWorld, spawnShip, spawnStation, type World,
} from '../src/sim/world';
import { issueAttack } from '../src/sim/movement';
import { run, step } from '../src/sim/step';

function world(): World {
  const w = createWorld(6000, 4000);
  addFaction(w, 'klaed', 0);
  addFaction(w, 'nairan', 0);
  return w;
}

describe('combat', () => {
  it('drains shields before the hull', () => {
    const w = world();
    const s = spawnShip(w, 'klaed', 'frigate', 0, 0);
    const { maxShield, maxHull } = s;
    applyDamage(s, 100);
    expect(s.shield).toBe(maxShield - 100);
    expect(s.hull).toBe(maxHull);
    applyDamage(s, maxShield);
    expect(s.shield).toBe(0);
    expect(s.hull).toBe(maxHull - 100);
  });

  it('fires at enemies in range and destroys them', () => {
    const w = world();
    spawnShip(w, 'klaed', 'battlecruiser', 1000, 1000);
    const victim = spawnShip(w, 'nairan', 'scout', 1300, 1000);
    run(w, 30);
    expect(w.entities.has(victim.id)).toBe(false);
    expect(w.events.some((e) => e.type === 'destroyed' && e.id === victim.id)).toBe(true);
  });

  it('applies damage multipliers by target size', () => {
    // Fighters' guns barely scratch structures.
    const w = world();
    const station = spawnStation(w, 'nairan', 3000, 2000);
    for (const c of childrenOf(w, station.id)) c.hull = -1;
    step(w);
    station.shield = 0;
    station.maxShield = 0;
    const f = spawnShip(w, 'klaed', 'fighter', 3000, 2000 - station.radius - 60);
    issueAttack(w, [f.id], station.id);
    const before = station.hull;
    run(w, 5);
    const dealt = before - station.hull;
    // 3 dmg x 0.2 per shot, ~3.3 shots/s over 5 s: well under 25.
    expect(dealt).toBeGreaterThan(0);
    expect(dealt).toBeLessThan(25);
  });

  it('destroying a station takes its hardpoints with it', () => {
    const w = world();
    const station = spawnStation(w, 'nairan', 3000, 2000);
    expect(childrenOf(w, station.id).length).toBeGreaterThan(0);
    station.hull = 0;
    step(w);
    expect(w.entities.has(station.id)).toBe(false);
    expect(childrenOf(w, station.id)).toHaveLength(0);
    expect(w.factions.get('nairan')!.stationId).toBeNull();
  });

  it('the station loses its shields with its shield generator', () => {
    const w = world();
    const station = spawnStation(w, 'nairan', 3000, 2000);
    childrenOf(w, station.id).find((c) => c.type === 'shield')!.hull = 0;
    step(w);
    step(w);
    expect(station.shield).toBe(0);
  });

  it('support ships repair damaged allies', () => {
    const w = world();
    const hurt = spawnShip(w, 'klaed', 'frigate', 1000, 1000);
    spawnShip(w, 'klaed', 'support', 1100, 1000);
    hurt.hull = 100;
    run(w, 5);
    expect(hurt.hull).toBeGreaterThan(140);
  });

  it('declares the last faction with a station the winner', () => {
    const w = world();
    spawnStation(w, 'klaed', 700, 2000);
    const enemy = spawnStation(w, 'nairan', 5300, 2000);
    step(w);
    expect(w.winner).toBeNull();
    enemy.hull = 0;
    step(w);
    expect(w.winner).toBe('klaed');
  });
});
