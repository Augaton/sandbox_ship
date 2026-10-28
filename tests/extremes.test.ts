// Extreme situations: nothing may leave a game stuck with no way to end or to go on.
import { describe, expect, it } from 'vitest';
import { REBUILD } from '../src/data/structures';
import { SHIP_CLASSES, type FactionId } from '../src/data/units';
import { useAbility } from '../src/sim/abilities';
import { NO_DEFENSE, STALEMATE, battleOutcome, createPlanetBattle } from '../src/sim/battle';
import { addShipsAt, chooseBattle, createGalaxy, delegateBattle, sendFleet } from '../src/sim/galaxy';
import { generateLayout } from '../src/sim/galaxyGen';
import { runGalaxy } from '../src/sim/galaxyStep';
import { createSkirmish } from '../src/sim/scenario';
import { canBuildShip } from '../src/sim/structures';
import { run, step } from '../src/sim/step';
import { addFaction, childrenOf, createWorld, rand, spawnShip, spawnStation, type Entity, type World } from '../src/sim/world';
import { MAX_ENTITIES, spawnAt } from '../src/input/selection';

function disarm(w: World, e: Entity): void {
  for (const part of [e, ...childrenOf(w, e.id)]) part.weapons = [];
  e.range = 0;
}

/** Nothing out of bounds, nothing dead still around, no part without its parent. */
function sane(w: World): void {
  for (const e of w.entities.values()) {
    expect(Number.isFinite(e.x) && Number.isFinite(e.y) && Number.isFinite(e.hull) && Number.isFinite(e.shield)).toBe(true);
    expect(e.hull).toBeGreaterThan(0);
    if (e.parentId != null) expect(w.entities.has(e.parentId)).toBe(true);
  }
  for (const f of w.factions.values()) expect(Number.isFinite(f.credits) && f.credits >= 0).toBe(true);
  for (const p of w.projectiles) expect(Number.isFinite(p.x) && Number.isFinite(p.y)).toBe(true);
}

describe('stations', () => {
  it('rebuild a lost shipyard once left alone, so that production can always resume', () => {
    const w = createWorld(6000, 4000);
    addFaction(w, 'klaed', 5000);
    addFaction(w, 'nairan', 0);
    const st = spawnStation(w, 'klaed', 700, 2000);
    childrenOf(w, st.id).find((c) => c.type === 'shipyard')!.hull = 0;
    step(w);
    expect(canBuildShip(w, 'klaed', 'frigate')).toEqual({ ok: false, reason: 'Chantier naval détruit' });
    // Under fire, nothing is rebuilt.
    for (let t = 0; t < REBUILD.time + 10; t += 1) {
      st.hitAt = w.time;
      run(w, 1);
    }
    expect(canBuildShip(w, 'klaed', 'frigate').ok).toBe(false);
    run(w, REBUILD.quiet + REBUILD.time + 1);
    expect(canBuildShip(w, 'klaed', 'frigate').ok).toBe(true);
    expect(w.events.some((e) => e.type === 'rebuilt')).toBe(true);
  });
});

describe('planet battles', () => {
  it('end even when no side can hurt the other: the attacker falls back', () => {
    const w = createPlanetBattle({ faction: 'klaed', ships: { battlecruiser: 1 } }, { faction: 'nairan', ships: {} }, { ...NO_DEFENSE, station: 1 });
    for (const e of w.entities.values()) if (e.kind === 'ship' || e.type === 'station') disarm(w, e);
    run(w, STALEMATE - 5, 1 / 30);
    expect(battleOutcome(w, 'klaed', 'nairan')).toBeNull();
    run(w, 10, 1 / 30);
    expect(battleOutcome(w, 'klaed', 'nairan')).toBe('retreat');
  });

  it('a battle the player leaves plays on by itself to the end', () => {
    const g = createGalaxy('klaed', 'nairan', false);
    const target = g.planets.find((p) => p.owner === 'nairan')!;
    const fleet = [...g.fleets.values()].find((f) => f.faction === 'klaed')!;
    addShipsAt(g, 'klaed', fleet.at!, { frigate: 6 });
    sendFleet(g, fleet.id, target.id);
    for (let i = 0; i < 6000 && g.battles.length === 0; i++) runGalaxy(g, 0.1);
    const b = g.battles[0];
    expect(b).toBeTruthy();
    chooseBattle(g, b.id, 'tactical');
    delegateBattle(g, b.id);
    expect(b.status).toBe('auto');
    runGalaxy(g, 120);
    expect(g.battles.some((x) => x.id === b.id)).toBe(false);
  });
});

describe('sandbox', () => {
  it('stops spawning past the cap', () => {
    const w = createWorld(6000, 4000);
    addFaction(w, 'klaed', 0);
    let placed = 0;
    for (let i = 0; i < MAX_ENTITIES + 50; i++) if (spawnAt(w, 'klaed', 'fighter', 100 + (i % 50) * 30, 100 + Math.floor(i / 50) * 30)) placed++;
    expect(w.entities.size).toBe(MAX_ENTITIES);
    expect(placed).toBe(MAX_ENTITIES);
  });
});

describe('chaos', () => {
  it('three AIs, random spawns, abilities and kills: the simulation stays sound', () => {
    const w = createSkirmish({ player: 'klaed', enemies: ['nairan', 'nautolan'], enemyAI: true, seed: 31, size: 'small' });
    const factions: FactionId[] = ['klaed', 'nairan', 'nautolan'];
    for (const f of factions) w.factions.get(f)!.autoAbilities = true;
    const pick = <T>(list: readonly T[]) => list[Math.floor(rand(w) * list.length)];
    for (let second = 0; second < 240; second++) {
      // Every second, something unexpected: a ship anywhere, an ability fired at random,
      // a random entity destroyed outright, a faction emptied of credits.
      const f = pick(factions);
      spawnShip(w, f, pick(SHIP_CLASSES), rand(w) * w.width, rand(w) * w.height, rand(w) * 6);
      const all = [...w.entities.values()];
      const caster = all.find((e) => e.ability && e.ability.cooldown === 0 && rand(w) < 0.3);
      const victim = pick(all);
      if (caster) useAbility(w, caster, { x: rand(w) * w.width, y: rand(w) * w.height, id: victim?.id });
      if (victim && rand(w) < 0.2) victim.hull = 0;
      if (rand(w) < 0.05) w.factions.get(f)!.credits = 0;
      run(w, 1, 1 / 30);
      sane(w);
      w.events.length = 0;
    }
    expect(w.projectiles.length).toBeLessThan(5000);
  }, 60000);

  it('a campaign of three AIs keeps resolving its battles', () => {
    const g = createGalaxy('klaed', ['nairan', 'nautolan'], true, 55, generateLayout(55, 3));
    g.player = 'nobody' as FactionId;
    g.factions.get('klaed')!.ai = { timer: 0, want: null, difficulty: 'normal' };
    const opened = new Map<number, number>();
    for (let t = 0; t < 900 && !g.winner; t += 1) {
      runGalaxy(g, 1);
      for (const b of g.battles) if (!opened.has(b.id)) opened.set(b.id, g.time);
      // No battle lasts past the auto-resolution limit, plus a little slack.
      for (const b of g.battles) expect(g.time - opened.get(b.id)!).toBeLessThan(80);
      for (const fleet of g.fleets.values()) expect(Number.isFinite(fleet.progress) && (fleet.condition ?? 1) > 0).toBe(true);
    }
    expect(opened.size).toBeGreaterThan(0);
  }, 120000);
});
