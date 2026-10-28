import { describe, expect, it } from 'vitest';
import { createSkirmish } from '../src/sim/scenario';
import { run } from '../src/sim/step';
import { newAI } from '../src/sim/world';

describe('AI', () => {
  it('builds an economy and a fleet', () => {
    const w = createSkirmish({ player: 'klaed', enemy: 'nairan', enemyAI: true, seed: 7 });
    run(w, 180, 1 / 30);
    const own = [...w.entities.values()].filter((e) => e.faction === 'nairan');
    expect(own.filter((e) => e.type === 'mine').length).toBeGreaterThanOrEqual(2);
    expect(w.entities.get(w.factions.get('nairan')!.stationId!)!.level).toBeGreaterThan(1);
    expect(own.filter((e) => e.kind === 'ship' && e.type !== 'fighter' && e.type !== 'bomber').length).toBeGreaterThan(0);
  });

  it('destroys a passive opponent', () => {
    const w = createSkirmish({ player: 'klaed', enemy: 'nairan', enemyAI: true, seed: 1 });
    for (let minute = 0; minute < 25 && !w.winner; minute++) run(w, 60, 1 / 30);
    expect(w.winner).toBe('nairan');
  }, 60000);

  it('two AIs fight without breaking the simulation', () => {
    const w = createSkirmish({ player: 'klaed', enemy: 'nairan', enemyAI: true, seed: 3 });
    w.factions.get('klaed')!.ai = newAI();
    run(w, 600, 1 / 30);
    for (const e of w.entities.values()) {
      expect(Number.isFinite(e.x) && Number.isFinite(e.y) && Number.isFinite(e.hull)).toBe(true);
    }
    expect(w.events.filter((e) => e.type === 'destroyed').length).toBeGreaterThan(10);
    expect(w.entities.size).toBeLessThan(400);
  }, 30000);
});
