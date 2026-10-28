import type { FactionId } from '../data/units';
import { addFaction, addField, createWorld, dist, rand, spawnStation, type World } from './world';

export const MAP_W = 6000;
export const MAP_H = 4000;
export const START_CREDITS = 1500;

export interface SkirmishOptions {
  player: FactionId;
  enemy: FactionId;
  /** Off in sandbox mode: the enemy then only does what the player spawns. */
  enemyAI: boolean;
  credits?: number;
  seed?: number;
  /** `classic`: the hand-made, mirrored map. `random` (default): stations and fields drawn from the seed. */
  map?: 'classic' | 'random';
  /** Fog of war, on by default. */
  fog?: boolean;
}

/**
 * Scatters asteroid fields at random, clear of the given spots and of each other.
 * Used for random skirmishes and for the battles of the galaxy campaign.
 */
export function scatterFields(world: World, count: number, keepClear: { x: number; y: number }[], clearance: number): void {
  for (let tries = 0, placed = 0; placed < count && tries < 500; tries++) {
    const r = 120 + rand(world) * 100;
    const p = { x: 250 + rand(world) * (world.width - 500), y: 250 + rand(world) * (world.height - 500) };
    if (keepClear.some((c) => dist(c, p) < clearance + r)) continue;
    if (world.fields.some((f) => dist(f, p) < f.radius + r + 120)) continue;
    addField(world, p.x, p.y, r);
    placed++;
  }
}

function classicFields(world: World): void {
  const half: [number, number, number][] = [
    [1100, 1200, 140],
    [1200, 2900, 140],
    [2200, 700, 170],
    [2400, 2300, 160],
  ];
  for (const [x, y, r] of half) {
    addField(world, x, y, r);
    addField(world, MAP_W - x, MAP_H - y, r);
  }
  addField(world, MAP_W / 2, MAP_H / 2 - 900, 200);
  addField(world, MAP_W / 2, MAP_H / 2 + 900, 200);
}

/** Two stations on opposite sides; fields mirrored (classic map) or scattered from the seed. */
export function createSkirmish(o: SkirmishOptions): World {
  const world = createWorld(MAP_W, MAP_H, o.seed ?? 1);
  world.fog = o.fog ?? true;
  const credits = o.credits ?? START_CREDITS;
  addFaction(world, o.player, credits, false);
  addFaction(world, o.enemy, credits, o.enemyAI);
  if (o.map === 'classic') {
    spawnStation(world, o.player, 700, MAP_H / 2);
    spawnStation(world, o.enemy, MAP_W - 700, MAP_H / 2);
    classicFields(world);
    return world;
  }
  const home = { x: 500 + rand(world) * 500, y: 600 + rand(world) * (MAP_H - 1200) };
  const away = { x: MAP_W - 500 - rand(world) * 500, y: 600 + rand(world) * (MAP_H - 1200) };
  spawnStation(world, o.player, home.x, home.y);
  spawnStation(world, o.enemy, away.x, away.y);
  scatterFields(world, 8 + Math.floor(rand(world) * 6), [home, away], 450);
  return world;
}
