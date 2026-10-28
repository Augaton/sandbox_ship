import { describe, expect, it } from 'vitest';
import { STATION } from '../src/data/structures';
import {
  battleOutcome, createPlanetBattle, defenseLeft, fleetValue, survivors, updateBattleAI,
} from '../src/sim/battle';
import { run } from '../src/sim/step';
import { childrenOf, spawnShip, stationOf } from '../src/sim/world';

describe('planet battles', () => {
  it('lay out the defenses of the planet and both fleets', () => {
    const w = createPlanetBattle(
      { faction: 'klaed', ships: { frigate: 3 } },
      { faction: 'nairan', ships: { scout: 2 } },
      { station: 2, platforms: 2, mines: 1 },
    );
    const station = stationOf(w, 'nairan')!;
    expect(station.level).toBe(2);
    expect(childrenOf(w, station.id)).toHaveLength(STATION.levels[0].hardpoints.length + STATION.levels[1].hardpoints.length);
    expect(defenseLeft(w, 'nairan')).toEqual({ station: 2, platforms: 2, mines: 1 });
    expect(survivors(w, 'klaed')).toEqual({ frigate: 3 });
    expect(survivors(w, 'nairan')).toEqual({ scout: 2 });
  });

  it('end when one side has nothing left', () => {
    const w = createPlanetBattle({ faction: 'klaed', ships: { frigate: 1 } }, { faction: 'nairan', ships: {} }, { station: 1, platforms: 0, mines: 0 });
    expect(battleOutcome(w, 'klaed', 'nairan')).toBeNull();
    stationOf(w, 'nairan')!.hull = 0;
    run(w, 0.1);
    expect(battleOutcome(w, 'klaed', 'nairan')).toBe('attacker');
    for (const e of w.entities.values()) if (e.faction === 'klaed') e.hull = 0;
    run(w, 0.1);
    expect(battleOutcome(w, 'klaed', 'nairan')).toBe('defender');
  });

  it('do not bring hangar craft back to the map', () => {
    const w = createPlanetBattle({ faction: 'klaed', ships: { frigate: 1 } }, { faction: 'nairan', ships: {} }, { station: 0, platforms: 0, mines: 0 });
    spawnShip(w, 'klaed', 'fighter', 100, 100);
    expect(survivors(w, 'klaed')).toEqual({ frigate: 1 });
  });

  it('the battle AI sends idle fleets at each other until one side wins', () => {
    const w = createPlanetBattle(
      { faction: 'klaed', ships: { battlecruiser: 2, frigate: 4 } },
      { faction: 'nairan', ships: { scout: 3 } },
      { station: 0, platforms: 0, mines: 0 },
    );
    let result = null;
    for (let t = 0; t < 240 && !result; t += 1 / 30) {
      run(w, 1 / 30, 1 / 30);
      updateBattleAI(w, ['klaed', 'nairan'], 1 / 30);
      result = battleOutcome(w, 'klaed', 'nairan');
    }
    expect(result).toBe('attacker');
    expect(fleetValue(survivors(w, 'klaed'))).toBeGreaterThan(0);
  });
});
