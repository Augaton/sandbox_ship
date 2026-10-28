// Fog of war in battles: each faction only sees what its ships and structures see, and
// only targets what it sees. The AI is bound by the same rule.
import type { FactionId, ShipClass } from '../data/units';
import type { Entity, World } from './world';

const SHIP_VISION: Record<ShipClass, number> = {
  scout: 1100, fighter: 550, bomber: 550, support: 700, torpedo: 700, frigate: 750, battlecruiser: 850, dreadnought: 900,
};
const STRUCTURE_VISION: Record<string, number> = {
  station: 1100, platform: 850, mine: 500, hangar: 600, repairDock: 550, ionCannon: 1000,
};
/** Seconds between two vision updates. */
const VISION_INTERVAL = 0.2;
/** At least the longest sight (hero scouts) plus a large target's radius. */
const EYE_CELL = 1400;
const eyeKey = (cx: number, cy: number) => ((cx + 64) << 10) | (cy + 64);

/** How far an entity sees; hardpoints see through their parent. */
export function visionOf(e: Entity): number {
  if (e.kind === 'ship') return SHIP_VISION[e.type as ShipClass] + (e.hero ? 200 : 0);
  if (e.kind === 'structure') return e.build < 1 ? 300 : STRUCTURE_VISION[e.type] ?? 500;
  return 0;
}

export function isVisible(world: World, faction: FactionId, e: Entity): boolean {
  return !world.fog || e.faction === faction || (world.visible.get(faction)?.has(e.id) ?? false);
}

/** An enemy structure seen once stays on the map, greyed out, even in the fog. */
export function wasSeen(world: World, faction: FactionId, e: Entity): boolean {
  return world.seen.get(faction)?.has(e.id) ?? false;
}

export function updateVision(world: World, dt: number): void {
  if (!world.fog) return;
  world.visionTimer -= dt;
  if (world.visionTimer > 0) return;
  world.visionTimer = VISION_INTERVAL;
  const all = [...world.entities.values()];
  for (const faction of world.factions.keys()) {
    const visible = new Set<number>();
    const seen = world.seen.get(faction) ?? new Set<number>();
    // Eyes binned in cells as wide as the longest sight: a target only checks the 3x3
    // cells around it, and stops at the first eye that sees it.
    const eyes = new Map<number, Entity[]>();
    for (const e of all) {
      if (e.faction !== faction || visionOf(e) <= 0) continue;
      const k = eyeKey(Math.floor(e.x / EYE_CELL), Math.floor(e.y / EYE_CELL));
      let list = eyes.get(k);
      if (!list) eyes.set(k, (list = []));
      list.push(e);
    }
    for (const t of all) {
      if (t.faction === faction || t.kind === 'hardpoint') continue;
      const cx = Math.floor(t.x / EYE_CELL);
      const cy = Math.floor(t.y / EYE_CELL);
      search: for (let dx = -1; dx <= 1; dx++) {
        for (let dy = -1; dy <= 1; dy++) {
          for (const e of eyes.get(eyeKey(cx + dx, cy + dy)) ?? []) {
            if (Math.hypot(e.x - t.x, e.y - t.y) <= visionOf(e) + t.radius) {
              visible.add(t.id);
              break search;
            }
          }
        }
      }
    }
    // Parts are seen along with what they are attached to.
    for (const t of all) {
      if (t.kind === 'hardpoint' && t.faction !== faction && t.parentId != null && visible.has(t.parentId)) visible.add(t.id);
    }
    for (const id of visible) {
      const t = world.entities.get(id)!;
      if (t.kind !== 'ship') seen.add(id);
    }
    world.visible.set(faction, visible);
    world.seen.set(faction, seen);
  }
}
