import type { FactionId } from '../data/units';
import { addFaction, addField, createWorld, dist, rand, spawnStation, type Difficulty, type World } from './world';

export const MAP_SIZES = {
  small: { w: 4500, h: 3000, name: 'Petite' },
  medium: { w: 6000, h: 4000, name: 'Moyenne' },
  large: { w: 8000, h: 5400, name: 'Grande' },
} as const;
export type MapSize = keyof typeof MAP_SIZES;

export const START_CREDITS = 1500;

export interface SkirmishOptions {
  player: FactionId;
  /** One or two opponents: every faction fights every other one. */
  enemies: FactionId[];
  /** Off in sandbox mode: the enemies then only do what the player spawns. */
  enemyAI: boolean;
  credits?: number;
  seed?: number;
  /** `classic`: the hand-made, mirrored map for two. `random` (default): drawn from the seed. */
  map?: 'classic' | 'random';
  size?: MapSize;
  /** Fog of war, on by default. */
  fog?: boolean;
  difficulty?: Difficulty;
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

/** Two fields tucked behind a station, on the side away from the map center: every player gets them. */
function backFields(world: World, s: { x: number; y: number }): void {
  const out = Math.atan2(s.y - world.height / 2, s.x - world.width / 2);
  for (const side of [-0.6, 0.6]) {
    const a = out + side;
    const x = Math.max(220, Math.min(world.width - 220, s.x + Math.cos(a) * 430));
    const y = Math.max(220, Math.min(world.height - 220, s.y + Math.sin(a) * 430));
    addField(world, x, y, 130);
  }
}

function classicFields(world: World): void {
  const sx = world.width / 6000;
  const sy = world.height / 4000;
  const half: [number, number, number][] = [
    [1100, 1200, 140],
    [1200, 2900, 140],
    [2200, 700, 170],
    [2400, 2300, 160],
  ];
  for (const [x, y, r] of half) {
    addField(world, x * sx, y * sy, r);
    addField(world, world.width - x * sx, world.height - y * sy, r);
  }
  addField(world, world.width / 2, world.height / 2 - 900 * sy, 200);
  addField(world, world.width / 2, world.height / 2 + 900 * sy, 200);
}

/** Where each station goes: left and right for two, a rough triangle for three. */
function startPositions(world: World, count: number): { x: number; y: number }[] {
  const { width: w, height: h } = world;
  if (count === 2) {
    return [
      { x: 500 + rand(world) * 500, y: 600 + rand(world) * (h - 1200) },
      { x: w - 500 - rand(world) * 500, y: 600 + rand(world) * (h - 1200) },
    ];
  }
  const start = rand(world) * Math.PI * 2;
  return Array.from({ length: count }, (_, i) => {
    const a = start + (i * Math.PI * 2) / count;
    return {
      x: Math.max(600, Math.min(w - 600, w / 2 + Math.cos(a) * w * 0.38)),
      y: Math.max(600, Math.min(h - 600, h / 2 + Math.sin(a) * h * 0.36)),
    };
  });
}

/** Every faction gets a station and two fields behind it; the rest of the fields are scattered. */
export function createSkirmish(o: SkirmishOptions): World {
  const size = MAP_SIZES[o.size ?? 'medium'];
  const world = createWorld(size.w, size.h, o.seed ?? 1);
  world.fog = o.fog ?? true;
  const credits = o.credits ?? START_CREDITS;
  addFaction(world, o.player, credits, false);
  for (const e of o.enemies) addFaction(world, e, credits, o.enemyAI, o.difficulty);
  const factions = [o.player, ...o.enemies];
  if (o.map === 'classic' && factions.length === 2) {
    spawnStation(world, o.player, 700 * (size.w / 6000), size.h / 2);
    spawnStation(world, o.enemies[0], size.w - 700 * (size.w / 6000), size.h / 2);
    classicFields(world);
    return world;
  }
  const spots = startPositions(world, factions.length);
  factions.forEach((f, i) => spawnStation(world, f, spots[i].x, spots[i].y));
  for (const s of spots) backFields(world, s);
  const area = (size.w * size.h) / (6000 * 4000);
  scatterFields(world, Math.round((6 + Math.floor(rand(world) * 5)) * area), spots, 550);
  return world;
}
