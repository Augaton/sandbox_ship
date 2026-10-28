// Every pair of factions fights many skirmishes, both AIs on normal difficulty, each side
// taking each start in turn. Prints win rates, game lengths and what got built.
import { it } from 'vitest';
import { FACTION_IDS, type FactionId } from '../../src/data/units';
import { createSkirmish } from '../../src/sim/scenario';
import { step } from '../../src/sim/step';
import { newAI } from '../../src/sim/world';

const SEEDS = 6;
const LIMIT = 30 * 60;
const DT = 1 / 30;

it('measures the factions against each other', () => {
  const wins = new Map<FactionId, number>(FACTION_IDS.map((f) => [f, 0]));
  const games = new Map<FactionId, number>(FACTION_IDS.map((f) => [f, 0]));
  const lengths: number[] = [];
  const built = new Map<string, number>();
  let draws = 0;
  for (const a of FACTION_IDS) {
    for (const b of FACTION_IDS) {
      if (a === b) continue;
      for (let seed = 1; seed <= SEEDS; seed++) {
        const w = createSkirmish({ player: a, enemies: [b], enemyAI: true, seed: seed * 101 });
        w.factions.get(a)!.ai = newAI();
        w.factions.get(a)!.autoAbilities = true;
        const seen = new Set<number>();
        while (!w.winner && w.time < LIMIT) {
          step(w, DT);
          for (const e of w.entities.values()) {
            if (e.kind !== 'ship' || e.launched || seen.has(e.id)) continue;
            seen.add(e.id);
            const k = e.hero ? `héros ${e.hero}` : e.type;
            built.set(k, (built.get(k) ?? 0) + 1);
          }
          w.events.length = 0;
        }
        games.set(a, games.get(a)! + 1);
        games.set(b, games.get(b)! + 1);
        if (w.winner) {
          wins.set(w.winner, wins.get(w.winner)! + 1);
          lengths.push(w.time);
        } else draws++;
        console.log(`${a} contre ${b}, graine ${seed}: ${w.winner ?? 'nul'} en ${Math.round(w.time / 60)} min`);
      }
    }
  }
  console.log('--- victoires ---');
  for (const f of FACTION_IDS) console.log(`${f}: ${wins.get(f)} / ${games.get(f)} (${Math.round((100 * wins.get(f)!) / games.get(f)!)} %)`);
  console.log(`nuls (${LIMIT / 60} min sans vainqueur): ${draws}`);
  const sorted = [...lengths].sort((x, y) => x - y);
  console.log(`durée: médiane ${Math.round(sorted[sorted.length >> 1] / 60)} min, de ${Math.round(sorted[0] / 60)} à ${Math.round(sorted.at(-1)! / 60)} min`);
  console.log('construits:', JSON.stringify(Object.fromEntries([...built].sort((x, y) => y[1] - x[1]))));
});
