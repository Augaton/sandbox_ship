import { describe, expect, it } from 'vitest';
import { addFaction, createWorld, dist, spawnShip, spawnStructure } from '../src/sim/world';
import { formationOffsets, issueMove, issueStop, wrapAngle } from '../src/sim/movement';
import { run } from '../src/sim/step';

function world() {
  const w = createWorld(4000, 4000);
  addFaction(w, 'klaed', 0);
  addFaction(w, 'nairan', 0);
  return w;
}

describe('movement', () => {
  it('wraps angles into (-PI, PI]', () => {
    expect(wrapAngle(3 * Math.PI)).toBeCloseTo(Math.PI);
    expect(wrapAngle(-Math.PI / 2 - 2 * Math.PI)).toBeCloseTo(-Math.PI / 2);
    expect(wrapAngle(0.5)).toBeCloseTo(0.5);
  });

  it('flies a ship to its destination and stops there', () => {
    const w = world();
    // Facing away from the target: the ship has to turn around first.
    const s = spawnShip(w, 'klaed', 'frigate', 500, 500, Math.PI);
    issueMove(w, [s.id], 1500, 900);
    run(w, 40);
    expect(s.order).toBeNull();
    expect(dist(s, { x: 1500, y: 900 })).toBeLessThan(15);
    expect(s.speed).toBeLessThan(1);
  });

  it('spreads a group over distinct formation slots', () => {
    const offsets = formationOffsets(7, 50);
    expect(offsets).toHaveLength(7);
    const keys = new Set(offsets.map((o) => `${o.x},${o.y}`));
    expect(keys.size).toBe(7);
    const w = world();
    const ids = Array.from({ length: 6 }, (_, i) => spawnShip(w, 'klaed', 'frigate', 300 + i * 70, 300).id);
    issueMove(w, ids, 2000, 2000);
    run(w, 60);
    const ships = ids.map((id) => w.entities.get(id)!);
    for (const a of ships) {
      for (const b of ships) if (a !== b) expect(dist(a, b)).toBeGreaterThan(a.radius);
    }
  });

  it('pushes ships out of structures', () => {
    const w = world();
    const st = spawnStructure(w, 'klaed', 'platform', 1000, 1000);
    const s = spawnShip(w, 'klaed', 'frigate', 1005, 1000);
    issueStop(w, [s.id]);
    run(w, 0.1);
    expect(dist(s, st)).toBeGreaterThanOrEqual(s.radius + st.radius - 0.01);
  });
});
