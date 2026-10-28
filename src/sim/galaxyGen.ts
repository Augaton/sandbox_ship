// Galaxy layouts: the hand-made classic map, or a random one generated from a seed.
import { CAPITALS, GALAXY_H, GALAXY_W, LANES, PLANETS, type PlanetDef, type PlanetSpecial } from '../data/galaxy';

export interface GalaxyLayout {
  planets: PlanetDef[];
  lanes: [number, number][];
  /** Index of each side's capital in `planets`. */
  capitals: [number, number];
}

export const CLASSIC: GalaxyLayout = { planets: PLANETS, lanes: LANES, capitals: [CAPITALS[0], CAPITALS[1]] };

const NAMES = [
  'Vesper', 'Orin', 'Maru', 'Kaldra', 'Sorel', 'Thessa', 'Nadir', 'Ostra', 'Pélion', 'Yvra', 'Tarsis', 'Brume',
  'Ixion', 'Calyx', 'Aldane', 'Bérénice', 'Corvée', 'Dravon', 'Elyne', 'Fargo', 'Gélise', 'Hesper', 'Isaure',
  'Joran', 'Kestel', 'Lunaé', 'Mirth', 'Nérée', 'Opale', 'Pyros', 'Quarre', 'Rhéa', 'Solenne', 'Tyval', 'Ulme',
];
const SPECIAL_KINDS: PlanetSpecial[] = ['rich', 'shipyard', 'fortress', 'archive'];
const MARGIN = 90;
const MIN_GAP = 165;

/** Deterministic random numbers in [0, 1) (mulberry32). */
export function seededRandom(seed: number): () => number {
  let s = seed | 0;
  return () => {
    let t = (s = (s + 0x6d2b79f5) | 0);
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

type Point = { x: number; y: number };

/** Whether segments ab and cd cross, not counting a shared end. */
export function crosses(a: Point, b: Point, c: Point, d: Point): boolean {
  const same = (p: Point, q: Point) => p.x === q.x && p.y === q.y;
  if (same(a, c) || same(a, d) || same(b, c) || same(b, d)) return false;
  const orient = (p: Point, q: Point, r: Point) => Math.sign((q.x - p.x) * (r.y - p.y) - (q.y - p.y) * (r.x - p.x));
  return orient(a, b, c) !== orient(a, b, d) && orient(c, d, a) !== orient(c, d, b);
}

function hops(lanes: [number, number][], n: number, from: number): number[] {
  const dist = Array<number>(n).fill(Infinity);
  dist[from] = 0;
  const todo = [from];
  while (todo.length > 0) {
    const cur = todo.shift()!;
    for (const [a, b] of lanes) {
      const next = a === cur ? b : b === cur ? a : -1;
      if (next >= 0 && dist[next] === Infinity) {
        dist[next] = dist[cur] + 1;
        todo.push(next);
      }
    }
  }
  return dist;
}

/**
 * A random galaxy: 12 to 18 planets spread out, lanes between near neighbours that never
 * cross, everything connected, and the two capitals as far apart as the lanes allow.
 */
export function generateLayout(seed: number): GalaxyLayout {
  const rnd = seededRandom(seed);
  const count = 12 + Math.floor(rnd() * 7);
  const points: Point[] = [];
  for (let tries = 0; points.length < count && tries < 5000; tries++) {
    const p = { x: MARGIN + rnd() * (GALAXY_W - 2 * MARGIN), y: MARGIN + rnd() * (GALAXY_H - 2 * MARGIN) };
    if (points.every((q) => Math.hypot(p.x - q.x, p.y - q.y) >= MIN_GAP)) points.push({ x: Math.round(p.x), y: Math.round(p.y) });
  }
  const names = [...NAMES];
  for (let i = names.length - 1; i > 0; i--) {
    const j = Math.floor(rnd() * (i + 1));
    [names[i], names[j]] = [names[j], names[i]];
  }
  const planets: PlanetDef[] = points.map((p, i) => {
    const income = 2 + Math.floor(rnd() * 5);
    const special = rnd() < 0.3 ? SPECIAL_KINDS[Math.floor(rnd() * SPECIAL_KINDS.length)] : undefined;
    return { name: names[i], ...p, income, mineSlots: Math.min(3, 1 + Math.floor(income / 2)), special };
  });

  // Shortest lanes first, each to one of a planet's nearest neighbours, as long as none cross.
  const lanes: [number, number][] = [];
  const degree = Array<number>(points.length).fill(0);
  const candidates: [number, number, number][] = [];
  points.forEach((p, i) => {
    const near = points.map((q, j) => [j, Math.hypot(p.x - q.x, p.y - q.y)] as const)
      .filter(([j]) => j !== i).sort((a, b) => a[1] - b[1]).slice(0, 4);
    for (const [j, d] of near) candidates.push([Math.min(i, j), Math.max(i, j), d]);
  });
  candidates.sort((a, b) => a[2] - b[2]);
  const has = (a: number, b: number) => lanes.some(([x, y]) => (x === a && y === b) || (x === b && y === a));
  const free = (a: number, b: number) => lanes.every(([x, y]) => !crosses(points[a], points[b], points[x], points[y]));
  for (const [a, b] of candidates) {
    if (has(a, b) || degree[a] >= 4 || degree[b] >= 4 || !free(a, b)) continue;
    lanes.push([a, b]);
    degree[a]++;
    degree[b]++;
  }
  // Join separate groups with their shortest crossing-free link (any link as a last resort).
  for (;;) {
    const reach = hops(lanes, points.length, 0);
    const out = points.map((_, i) => i).filter((i) => reach[i] === Infinity);
    if (out.length === 0) break;
    const links: [number, number, number][] = [];
    for (const a of points.keys()) {
      if (reach[a] === Infinity) continue;
      for (const b of out) links.push([a, b, Math.hypot(points[a].x - points[b].x, points[a].y - points[b].y)]);
    }
    links.sort((x, y) => x[2] - y[2]);
    const [a, b] = links.find(([x, y]) => free(x, y)) ?? links[0];
    lanes.push([a, b]);
  }

  // Capitals: the pair furthest apart in lanes, then in distance.
  let capitals: [number, number] = [0, 1];
  let best = -1;
  for (const a of points.keys()) {
    const h = hops(lanes, points.length, a);
    for (const b of points.keys()) {
      const score = h[b] * 10000 + Math.hypot(points[a].x - points[b].x, points[a].y - points[b].y);
      if (b !== a && score > best) {
        best = score;
        capitals = points[a].x <= points[b].x ? [a, b] : [b, a];
      }
    }
  }
  for (const c of capitals) Object.assign(planets[c], { income: 6, mineSlots: 3, special: undefined });
  return { planets, lanes, capitals };
}
