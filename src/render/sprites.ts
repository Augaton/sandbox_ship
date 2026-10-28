// Maps game entities to textures: a sprite from the imported packs when one matches,
// a generated placeholder otherwise (see placeholders.ts).
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

/** Keys whose dash-separated tokens include all of `include` and none of `exclude`, shortest first. */
export function matchAssets(m: Manifest, include: string[], exclude: string[] = []): string[] {
  const out: string[] = [];
  for (const key of Object.keys(m)) {
    const tokens = new Set(key.split('-'));
    if (tokens.has('preview')) continue;
    if (include.every((t) => tokens.has(t)) && !exclude.some((t) => tokens.has(t))) out.push(key);
  }
  return out.sort((a, b) => a.length - b.length || (a < b ? -1 : 1));
}

export function findAsset(m: Manifest, include: string[], exclude: string[] = []): string | null {
  return matchAssets(m, include, exclude)[0] ?? null;
}

/** The match with the biggest frames. */
function largest(m: Manifest, include: string[]): string | null {
  const keys = matchAssets(m, include);
  return keys.sort((a, b) => m[b].frameW * m[b].frameH - m[a].frameW * m[a].frameH)[0] ?? null;
}

export interface Look {
  /** Drawn bottom to top, centered on the entity. */
  layers: string[];
  /** Looping animation shown while the ship moves. */
  engine: string | null;
  /** One-shot animation played where the entity dies. */
  destruction: string | null;
  /** Layer size relative to the entity diameter: pack sprites have padding around the hull. */
  fit: number;
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
};

const placeholder = (key: string): Look => ({ layers: [key], engine: null, destruction: null, fit: 1, tint: true });

function shipLook(m: Manifest, faction: FactionId, cls: ShipClass): Look {
  for (const tokens of CLASS_TOKENS[cls]) {
    const base = findAsset(m, [faction, ...tokens, 'base']);
    if (!base) continue;
    return {
      layers: [base],
      engine: findAsset(m, [faction, ...tokens, 'engine']),
      destruction: findAsset(m, [faction, ...tokens, 'destruction']),
      fit: 1.6,
      tint: false,
    };
  }
  return placeholder(`ph-ship-${cls}`);
}

function structureLook(m: Manifest, e: Entity): Look {
  const tower = largest(m, ['tower']);
  const turret = findAsset(m, ['turret']);
  const sprite = (layers: (string | null)[], ph: string): Look =>
    layers.every(Boolean) ? { layers: layers as string[], engine: null, destruction: null, fit: 1, tint: false } : placeholder(ph);
  if (e.kind === 'hardpoint') {
    if (e.type === 'turret') return sprite([turret], 'ph-hp-turret');
    if (e.type === 'heavyTurret') return sprite([largest(m, ['turret'])], 'ph-hp-heavyTurret');
    return placeholder(`ph-hp-${e.type}`);
  }
  if (e.type === 'station') return sprite([tower], 'ph-st-station');
  if (e.type === 'platform') return sprite([tower, turret], 'ph-st-platform');
  return placeholder(`ph-st-${e.type}`);
}

export function lookFor(m: Manifest, e: Entity): Look {
  return e.kind === 'ship' ? shipLook(m, e.faction, e.type as ShipClass) : structureLook(m, e);
}

const PROJECTILE_TOKENS: Record<WeaponDef['kind'], { include: string[]; exclude: string[] }> = {
  bullet: { include: ['bullet'], exclude: ['big'] },
  laser: { include: ['big', 'bullet'], exclude: [] },
  torpedo: { include: ['torpedo'], exclude: ['ship'] },
  ray: { include: ['ray'], exclude: [] },
};

/** Faction-specific projectile if the faction's pack has one, else any pack's. */
export function projectileKey(m: Manifest, faction: FactionId, kind: WeaponDef['kind']): { key: string; tint: boolean } {
  const t = PROJECTILE_TOKENS[kind];
  const key = findAsset(m, [faction, ...t.include], t.exclude) ?? findAsset(m, t.include, t.exclude);
  return key ? { key, tint: false } : { key: `ph-shot-${kind}`, tint: true };
}

export function asteroidKeys(m: Manifest): string[] {
  const keys = matchAssets(m, ['asteroid']).filter((k) => m[k].frames === 1);
  return keys.length > 0 ? keys : ['ph-asteroid-0', 'ph-asteroid-1', 'ph-asteroid-2'];
}

export function planetKey(m: Manifest): string {
  return findAsset(m, ['planet']) ?? 'ph-planet';
}

/** Parallax background layers, farthest first. */
export function backgroundKeys(m: Manifest): string[] {
  const keys = matchAssets(m, ['background']).filter((k) => m[k].frames === 1);
  return keys.length > 0 ? keys.slice(0, 3) : ['ph-stars-0', 'ph-stars-1'];
}
