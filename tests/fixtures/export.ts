// Writes what the C# port must reproduce: data tables, random draws, derived numbers.
// Its tests (godot/Sim.Tests) compare against these files.
import { it } from 'vitest';
import { ABILITIES } from '../../src/data/abilities';
import { HEROES } from '../../src/data/heroes';
import { TECHS } from '../../src/data/research';
import { BUILDABLES, HARDPOINTS, STATION } from '../../src/data/structures';
import {
  asteroidKeys, backdrop, findAsset, forgetLookups, lookFor, partKeys, planetKey, projectileKey, type Manifest,
} from '../../src/render/sprites';
import { FACTIONS, FACTION_IDS, SHIPS, SHIP_CLASSES, orderName, shipCost, unitValue } from '../../src/data/units';
import { generateLayout, seededRandom } from '../../src/sim/galaxyGen';
import { createSkirmish, type SkirmishOptions } from '../../src/sim/scenario';
import { type Entity, type World } from '../../src/sim/world';

const OUT = 'godot/Sim.Tests/Fixtures';

async function write(name: string, data: unknown): Promise<void> {
  // Node's fs, without pulling Node's types into the project.
  const fs: { writeFileSync(path: string, text: string): void } = await import(/* @vite-ignore */ 'node:' + 'fs');
  fs.writeFileSync(`${OUT}/${name}.json`, JSON.stringify(data, null, 1) + '\n');
}

it('data tables', async () => {
  await write('data', {
    factions: FACTIONS, ships: SHIPS, hardpoints: HARDPOINTS, station: STATION, buildables: BUILDABLES,
    abilities: ABILITIES, heroes: HEROES, techs: TECHS,
    derived: SHIP_CLASSES.map((c) => ({
      cls: c, unitValue: unitValue(c), orderName: orderName(c),
      cost: Object.fromEntries(FACTION_IDS.map((f) => [f, shipCost(c, f)])),
    })),
  });
});

it('random draws', async () => {
  const mulberry: Record<string, number[]> = {};
  for (const seed of [1, 7, -123456, 987654321, 2147483647]) {
    const next = seededRandom(seed);
    mulberry[seed] = Array.from({ length: 32 }, () => next());
  }
  // The galaxy's Park-Miller generator, as galaxyRand runs it.
  const parkMiller: Record<string, number[]> = {};
  for (const seed of [7, 12345, 2147483646]) {
    let s = seed;
    parkMiller[seed] = Array.from({ length: 32 }, () => {
      s = (s * 16807) % 2147483647;
      return (s - 1) / 2147483646;
    });
  }
  await write('rng', { mulberry, parkMiller });
});

/** What a world looks like after a while: what the C# port must reproduce, to a tolerance. */
function snapshot(world: World) {
  return {
    time: world.time,
    nextId: world.nextId,
    seed: world.seed,
    winner: world.winner,
    factions: [...world.factions.values()].map((f) => ({
      id: f.id, credits: f.credits, queue: f.queue.length, buildQueue: f.buildQueue.length, research: { ...f.research },
      stats: { ...f.stats },
    })),
    fields: world.fields.map((f) => ({ id: f.id, x: f.x, y: f.y, radius: f.radius, mineId: f.mineId ?? null })),
    entities: [...world.entities.values()].map((e) => ({
      id: e.id, kind: e.kind, type: e.type, faction: e.faction, x: e.x, y: e.y, angle: e.angle, hull: e.hull, shield: e.shield,
      level: e.level, build: e.build,
    })),
  };
}

it('maps', async () => {
  const layouts = Object.fromEntries([[1, 2], [42, 2], [21, 3], [12345, 2]].map(([seed, players]) => [`${seed}-${players}`, generateLayout(seed, players)]));
  const skirmishes: Record<string, unknown> = {};
  const cases: [string, SkirmishOptions][] = [
    ['duel', { player: 'klaed', enemies: ['nairan'], enemyAI: true, seed: 5 }],
    ['classic', { player: 'nautolan', enemies: ['klaed'], enemyAI: true, seed: 9, map: 'classic' }],
    ['ffa', { player: 'nairan', enemies: ['klaed', 'nautolan'], enemyAI: true, seed: 77, size: 'large' }],
  ];
  // Only the start: the native version changed how fleets move, so play-outs differ by design.
  for (const [name, o] of cases) skirmishes[name] = { start: snapshot(createSkirmish(o)) };
  await write('maps', { layouts, skirmishes });
});

const GAME = 'godot/Game/assets';

/**
 * The game's looks, as the web version picks them from the packs, and the sprites they use:
 * the Godot game reads looks.json instead of searching the manifest itself. Titans are
 * composed there at start, from the faction's dreadnought and battlecruiser.
 */
it('game assets', async () => {
  const fs: {
    readFileSync(path: string, enc: string): string;
    copyFileSync(from: string, to: string): void;
    mkdirSync(path: string, o: { recursive: boolean }): void;
  } = await import(/* @vite-ignore */ 'node:' + 'fs');
  const m: Manifest = JSON.parse(fs.readFileSync('public/assets/manifest.json', 'utf8'));
  const capitals: Record<string, { base: string; dreadnought: Record<string, string | null>; battlecruiser: Record<string, string | null> }> = {};
  const exclude = ['engine', 'destruction', 'shield', 'weapons'];
  for (const f of FACTION_IDS) {
    const d = findAsset(m, [f, 'dreadnought', 'base'], exclude);
    const b = findAsset(m, [f, 'battlecruiser', 'base'], exclude);
    if (!d || !b) continue;
    const parts = { dreadnought: { base: d } as Record<string, string | null>, battlecruiser: { base: b } as Record<string, string | null> };
    m[`gen-${f}-capital-base`] = { url: '', w: 192, h: 192, frameW: 192, frameH: 192, frames: 1 };
    for (const part of ['engine', 'destruction']) {
      const dp = findAsset(m, [f, 'dreadnought', part]);
      const bp = findAsset(m, [f, 'battlecruiser', part]);
      parts.dreadnought[part] = dp;
      parts.battlecruiser[part] = bp;
      if (!dp && !bp) continue;
      const frames = Math.max(dp ? m[dp].frames : 1, bp ? m[bp].frames : 1);
      m[`gen-${f}-capital-${part}`] = { url: '', w: 192 * frames, h: 192, frameW: 192, frameH: 192, frames };
    }
    capitals[f] = { base: `gen-${f}-capital-base`, ...parts };
  }
  forgetLookups(m);
  const fake = (kind: string, type: string, faction: string) => ({ kind, type, faction }) as unknown as Entity;
  const ships = Object.fromEntries(FACTION_IDS.map((f) => [f, Object.fromEntries(SHIP_CLASSES.map((c) => [c, lookFor(m, fake('ship', c, f))]))]));
  const structures = Object.fromEntries(['station', ...Object.keys(BUILDABLES)].map((t) => [t, lookFor(m, fake('structure', t, 'klaed'))]));
  const hardpoints = Object.fromEntries(Object.keys(HARDPOINTS).map((r) => [r, lookFor(m, fake('hardpoint', r, 'klaed'))]));
  const kinds = ['bullet', 'laser', 'torpedo', 'ray'] as const;
  const projectiles = Object.fromEntries(FACTION_IDS.map((f) => [f, Object.fromEntries(kinds.map((k) => [k, projectileKey(m, f, k)]))]));
  // Parts the native version assembles into looks of its own: platforms by level, the lab.
  const extra = Object.fromEntries(Object.entries({
    tower: ['towers'], laserSmall1: ['small', 'laser', 'turret', 'mk1'], laserMedium2: ['medium', 'laser', 'turret', 'mk2'],
    laserMedium3: ['medium', 'laser', 'turret', 'mk3'], cannonMedium3: ['medium', 'cannon', 'turret', 'mk3'],
    missileMedium3: ['medium', 'missile', 'turret', 'mk3'], plasmaMedium1: ['medium', 'plasma', 'turret', 'mk1'],
    plasmaSmall2: ['small', 'plasma', 'turret', 'mk2'], baseMedium: ['weapon', 'base', 'medium'], labBody: ['bonus', 'body', '01b'],
  }).map(([name, tokens]) => [name, findAsset(m, tokens)]));
  const looks = {
    ships, structures, hardpoints, projectiles, capitals, extra,
    asteroids: asteroidKeys(m), planet: planetKey(m), backdrop: backdrop(m),
    parts: [...new Set([...partKeys(m), ...Object.values(extra).filter((k): k is string => k != null)])],
  };
  // Every pack sprite named anywhere above, with its frame layout.
  const used = new Set<string>();
  JSON.stringify(looks, (_k, v) => {
    if (typeof v === 'string' && m[v]?.url) used.add(v);
    return v;
  });
  fs.mkdirSync(`${GAME}/sprites`, { recursive: true });
  const sprites: Record<string, { file: string; frameW: number; frameH: number; frames: number }> = {};
  for (const key of [...used].sort()) {
    const e = m[key];
    fs.copyFileSync(`public/${e.url}`, `${GAME}/sprites/${key}.png`);
    sprites[key] = { file: `sprites/${key}.png`, frameW: e.frameW, frameH: e.frameH, frames: e.frames };
  }
  for (const key of Object.keys(m)) if (key.startsWith('gen-')) sprites[key] = { file: '', ...m[key] };
  const out: { writeFileSync(path: string, text: string): void } = await import(/* @vite-ignore */ 'node:' + 'fs');
  out.writeFileSync(`${GAME}/looks.json`, JSON.stringify({ ...looks, sprites }, null, 1) + '\n');
});
