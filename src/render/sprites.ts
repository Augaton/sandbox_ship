// Maps game entities to textures: a sprite from the imported packs when one matches,
// a generated placeholder otherwise (see placeholders.ts). Sprites are looked up by the
// dash-separated tokens of their manifest key, which come from the pack's file paths.
import type { FactionId, ShipClass, WeaponDef } from '../data/units';
import type { Entity } from '../sim/world';

export interface AssetEntry {
  url: string;
  w: number;
  h: number;
  frameW: number;
  frameH: number;
  frames: number;
}

export type Manifest = Record<string, AssetEntry>;

/**
 * Each manifest's keys split into tokens, and the answers already given: the same few
 * lookups come back with every shot fired.
 */
const indexes = new WeakMap<Manifest, { keys: { key: string; tokens: Set<string> }[]; answers: Map<string, string[]> }>();

function indexOf(m: Manifest) {
  let index = indexes.get(m);
  if (!index) {
    index = { keys: Object.keys(m).map((key) => ({ key, tokens: new Set(key.split('-')) })), answers: new Map() };
    indexes.set(m, index);
  }
  return index;
}

/** To call after adding entries to a manifest that has already been searched. */
export function forgetLookups(m: Manifest): void {
  indexes.delete(m);
}

/** Keys whose tokens include all of `include` and none of `exclude`, shortest first. */
export function matchAssets(m: Manifest, include: string[], exclude: string[] = []): string[] {
  const index = indexOf(m);
  const question = `${include.join('-')}/${exclude.join('-')}`;
  const known = index.answers.get(question);
  if (known) return known;
  const out: string[] = [];
  for (const { key, tokens } of index.keys) {
    if (tokens.has('preview')) continue;
    if (include.every((t) => tokens.has(t)) && !exclude.some((t) => tokens.has(t))) out.push(key);
  }
  out.sort((a, b) => a.length - b.length || (a < b ? -1 : 1));
  index.answers.set(question, out);
  return out;
}

export function findAsset(m: Manifest, include: string[], exclude: string[] = []): string | null {
  return matchAssets(m, include, exclude)[0] ?? null;
}

export interface Layer {
  key: string;
  /** Size of the visible (opaque) part, relative to the entity diameter. */
  size: number;
}

export interface Look {
  /** Drawn bottom to top, centered on the entity. */
  layers: Layer[];
  /** Looping animation shown while the ship moves; same frame geometry as the first layer. */
  engine: string | null;
  /** One-shot animation played where the entity dies; same frame geometry as the first layer. */
  destruction: string | null;
  /** Placeholders are white and take the faction color. */
  tint: boolean;
}

const CLASS_TOKENS: Record<ShipClass, string[][]> = {
  scout: [['scout']],
  fighter: [['fighter']],
  bomber: [['bomber']],
  support: [['support']],
  torpedo: [['torpedo', 'ship']],
  frigate: [['frigate']],
  battlecruiser: [['battlecruiser'], ['battleship']],
  dreadnought: [['dreadnought']],
  // Composed at boot from the faction's dreadnought and battlecruisers (capitals.ts).
  titan: [['capital']],
};

const placeholder = (key: string): Look => ({ layers: [{ key, size: 1 }], engine: null, destruction: null, tint: true });

function shipLook(m: Manifest, faction: FactionId, cls: ShipClass): Look {
  for (const tokens of CLASS_TOKENS[cls]) {
    const base = findAsset(m, [faction, ...tokens, 'base'], ['engine', 'destruction', 'shield', 'weapons']);
    if (!base) continue;
    return {
      layers: [{ key: base, size: 1.25 }],
      engine: findAsset(m, [faction, ...tokens, 'engine']),
      destruction: findAsset(m, [faction, ...tokens, 'destruction']),
      tint: false,
    };
  }
  return placeholder(`ph-ship-${cls}`);
}

/** The pack sprite of a ship class, for pictures outside the game view (the menu). */
export function shipSprite(m: Manifest, faction: FactionId, cls: ShipClass): string | null {
  const look = shipLook(m, faction, cls);
  return look.tint ? null : look.layers[0].key;
}

/** Structures and hardpoints are assembled from Space Breaker parts: [tokens, size] per layer. */
const PARTS: Record<string, [string[], number][]> = {
  station: [[['stations', 'station'], 1.15]],
  platform: [[['towers'], 1], [['medium', 'laser', 'turret', 'mk2'], 0.6]],
  mine: [[['asteroid', '01', 'base'], 1.1], [['weapon', 'hardpoint', 'medium'], 0.55]],
  outpost: [[['stations', 'station'], 1.2], [['small', 'laser', 'turret', 'mk1'], 0.45]],
  repairDock: [[['others', 'tanks', '01'], 1]],
  ionCannon: [[['towers'], 1], [['medium', 'plasma', 'turret', 'mk3'], 0.75]],
  'hp-turret': [[['small', 'laser', 'turret', 'mk1'], 1.1]],
  'hp-heavyTurret': [[['medium', 'cannon', 'turret', 'mk3'], 1.1]],
  'hp-hangar': [[['weapon', 'base', 'small'], 1]],
  'hp-shield': [[['weapon', 'hardpoint', 'medium'], 1]],
  'hp-shipyard': [[['towers'], 1]],
  'hp-engine': [[['engines', 'pngs', 'base', 'engine'], 1.3]],
};

/** Every pack sprite the parts are made of. */
export function partKeys(m: Manifest): string[] {
  const keys = new Set<string>();
  for (const spec of Object.values(PARTS)) {
    for (const [tokens] of spec) {
      const key = findAsset(m, tokens);
      if (key) keys.add(key);
    }
  }
  return [...keys];
}

function partsLook(m: Manifest, part: string, ph: string): Look {
  const spec = PARTS[part];
  const layers = spec?.map(([tokens, size]) => ({ key: findAsset(m, tokens), size }));
  if (!layers || layers.some((l) => !l.key)) return placeholder(ph);
  return { layers: layers as Layer[], engine: null, destruction: null, tint: false };
}

export function lookFor(m: Manifest, e: Entity): Look {
  if (e.kind === 'ship') return shipLook(m, e.faction, e.type as ShipClass);
  if (e.kind === 'hardpoint') return partsLook(m, `hp-${e.type}`, `ph-hp-${e.type}`);
  return partsLook(m, e.type, `ph-st-${e.type}`);
}

/** Projectile sprites to try, in order, for each weapon kind. */
const PROJECTILES: Record<WeaponDef['kind'], { include: string[]; exclude?: string[] }[]> = {
  bullet: [{ include: ['bullet'], exclude: ['big', 'spinning'] }, { include: ['bolt'] }],
  laser: [{ include: ['big', 'bullet'] }, { include: ['spinning', 'bullet'] }, { include: ['bolt'] }],
  torpedo: [{ include: ['torpedo'], exclude: ['ship'] }, { include: ['rocket'], exclude: ['main'] }],
  ray: [{ include: ['ray'] }],
};

/** The faction's own projectile when its pack has one, else any pack's, else a placeholder. */
export function projectileKey(m: Manifest, faction: FactionId, kind: WeaponDef['kind']): { key: string; tint: boolean } {
  const options = PROJECTILES[kind];
  for (const o of options) {
    const key = findAsset(m, [faction, 'projectiles', ...o.include], o.exclude);
    if (key) return { key, tint: false };
  }
  for (const o of options) {
    const key = findAsset(m, ['projectiles', ...o.include], o.exclude);
    if (key) return { key, tint: false };
  }
  return { key: `ph-shot-${kind}`, tint: true };
}

export function asteroidKeys(m: Manifest): string[] {
  const keys = matchAssets(m, ['asteroid', 'base']).filter((k) => m[k].frames === 1);
  return keys.length > 0 ? keys : ['ph-asteroid-0', 'ph-asteroid-1', 'ph-asteroid-2'];
}

/** Planet sprite; the pack's planet is an animated, rotating one. */
export function planetKey(m: Manifest): string {
  return findAsset(m, ['planet'], ['without']) ?? 'ph-planet';
}

export interface Backdrop {
  /** Tiled layers, farthest first, with how much each follows the camera. */
  tiles: { key: string; frame: number; factor: number }[];
  /** A big decorative picture (a galaxy) drifting slowly behind the battle. */
  decor: string | null;
}

/** The pack's star texture alone is dense enough; the placeholders need two layers. */
export function backdrop(m: Manifest): Backdrop {
  const stars = findAsset(m, ['background', 'stars', 'texture']);
  return {
    tiles: stars
      ? [{ key: stars, frame: 0, factor: 0.03 }]
      : [{ key: 'ph-stars-0', frame: 0, factor: 0.03 }, { key: 'ph-stars-1', frame: 0, factor: 0.08 }],
    decor: findAsset(m, ['background', 'galaxy']),
  };
}
