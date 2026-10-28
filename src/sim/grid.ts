// Uniform grid over the battle map: neighbour queries without testing every pair.
import type { Entity } from './world';

const CELL = 200;
/** Cell coordinates fit in 10 bits each: maps up to 200 km wide. */
const key = (cx: number, cy: number) => (Math.max(0, cx) << 10) | Math.max(0, cy);

export class SpatialGrid {
  private cells = new Map<number, Entity[]>();
  /** Largest entity radius inserted: queries widen by it to catch big entities near the edge. */
  maxRadius = 0;

  rebuild(entities: Iterable<Entity>): void {
    for (const list of this.cells.values()) list.length = 0;
    this.maxRadius = 0;
    for (const e of entities) {
      const k = key(Math.floor(e.x / CELL), Math.floor(e.y / CELL));
      let list = this.cells.get(k);
      if (!list) this.cells.set(k, (list = []));
      list.push(e);
      if (e.radius > this.maxRadius) this.maxRadius = e.radius;
    }
  }

  /** Calls `fn` for every entity whose center may lie within `r` of (x, y), plus the largest radius. */
  near(x: number, y: number, r: number, fn: (e: Entity) => void): void {
    const reach = r + this.maxRadius;
    const x0 = Math.floor((x - reach) / CELL);
    const x1 = Math.floor((x + reach) / CELL);
    const y0 = Math.floor((y - reach) / CELL);
    const y1 = Math.floor((y + reach) / CELL);
    for (let cx = x0; cx <= x1; cx++) {
      for (let cy = y0; cy <= y1; cy++) {
        if (cx < 0 || cy < 0) continue;
        const list = this.cells.get(key(cx, cy));
        if (list) for (const e of list) fn(e);
      }
    }
  }
}
