import type { FactionId } from '../data/units';
import { addFaction, addField, createWorld, spawnStation, type World } from './world';

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
}

/** Two stations on opposite sides, asteroid fields mirrored around the center. */
export function createSkirmish(o: SkirmishOptions): World {
  const world = createWorld(MAP_W, MAP_H, o.seed ?? 1);
  const credits = o.credits ?? START_CREDITS;
  addFaction(world, o.player, credits, false);
  addFaction(world, o.enemy, credits, o.enemyAI);
  spawnStation(world, o.player, 700, MAP_H / 2);
  spawnStation(world, o.enemy, MAP_W - 700, MAP_H / 2);
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
  return world;
}
