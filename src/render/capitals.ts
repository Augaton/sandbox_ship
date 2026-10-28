// Titans have no sprite in the packs: each faction's is composed at boot from its own
// dreadnought, lengthened by repeating a band of its hull, between two of its battlecruisers.
// The engine and destruction animations are composed the same way, so they keep lining up
// with the hull.
import type Phaser from 'phaser';
import { FACTION_IDS } from '../data/units';
import { findAsset, type Manifest } from './sprites';

/** Side of a composed frame, in texture pixels. */
const SIZE = 192;
/** Rows added to the dreadnought's length, roughly. */
const EXTRA = 32;
/** Battlecruisers sit this far to each side of the center line... */
const SIDE = 40;
/** ...with their stern this far ahead of the titan's. */
const SIDE_LIFT = 12;

const EXCLUDE = ['engine', 'destruction', 'shield', 'weapons'];

type Box = { top: number; bottom: number };

function pixels(scene: Phaser.Scene, key: string): ImageData {
  const frame = scene.textures.getFrame(key, 0) ?? scene.textures.getFrame(key);
  const c = document.createElement('canvas');
  c.width = frame.cutWidth;
  c.height = frame.cutHeight;
  const ctx = c.getContext('2d', { willReadFrequently: true })!;
  ctx.drawImage(frame.source.image as CanvasImageSource, frame.cutX, frame.cutY, c.width, c.height, 0, 0, c.width, c.height);
  return ctx.getImageData(0, 0, c.width, c.height);
}

function opaqueRows(img: ImageData): Box {
  let top = img.height;
  let bottom = 0;
  for (let y = 0; y < img.height; y++) {
    for (let x = 0; x < img.width; x++) {
      if (img.data[(y * img.width + x) * 4 + 3] <= 16) continue;
      top = Math.min(top, y);
      bottom = Math.max(bottom, y + 1);
    }
  }
  return { top, bottom };
}

/** How different two rows look: a shape change counts more than a color change. */
function rowDiff(img: ImageData, a: number, b: number): number {
  let n = 0;
  for (let x = 0; x < img.width; x++) {
    const i = (a * img.width + x) * 4;
    const j = (b * img.width + x) * 4;
    const pa = img.data[i + 3] > 16;
    const pb = img.data[j + 3] > 16;
    if (pa !== pb) n += 3;
    else if (pa && Math.abs(img.data[i] - img.data[j]) + Math.abs(img.data[i + 1] - img.data[j + 1]) + Math.abs(img.data[i + 2] - img.data[j + 2]) > 60) n++;
  }
  return n;
}

/**
 * The band of hull rows to repeat: in the middle of the ship, where the row after the band
 * looks most like its first row, so that the copies join without a seam.
 */
function band(img: ImageData, box: Box): { from: number; rows: number } {
  const h = box.bottom - box.top;
  let best = { score: Infinity, from: box.top + Math.round(h * 0.4), rows: 12 };
  for (let rows = 8; rows <= 24; rows++) {
    for (let a = box.top + Math.round(h * 0.3); a < box.top + Math.round(h * 0.7) - rows; a++) {
      const score = rowDiff(img, a, a + rows);
      if (score < best.score) best = { score, from: a, rows };
    }
  }
  return best;
}

interface Layout {
  /** Band of the dreadnought repeated `times` times. */
  from: number;
  rows: number;
  times: number;
  /** Where the dreadnought and the battlecruisers go in a composed frame. */
  dx: number;
  dy: number;
  bx: number;
  by: number;
}

function layout(scene: Phaser.Scene, dKey: string, bKey: string): Layout {
  const d = pixels(scene, dKey);
  const dBox = opaqueRows(d);
  const { from, rows } = band(d, dBox);
  const times = Math.max(1, Math.round(EXTRA / rows));
  const length = dBox.bottom - dBox.top + rows * times;
  const dy = Math.round((SIZE - length) / 2) - dBox.top;
  const bBox = opaqueRows(pixels(scene, bKey));
  return {
    from, rows, times,
    dx: Math.round((SIZE - d.width) / 2), dy,
    bx: Math.round((SIZE - d.width) / 2), by: dy + dBox.bottom + rows * times - bBox.bottom - SIDE_LIFT,
  };
}

/** Draws frame `i` of a strip, lengthened like the dreadnought. */
function drawLong(ctx: CanvasRenderingContext2D, frame: Phaser.Textures.Frame, l: Layout, x: number, y: number): void {
  const img = frame.source.image as CanvasImageSource;
  const w = frame.cutWidth;
  const h = frame.cutHeight;
  const cut = l.from + l.rows;
  const piece = (sy: number, sh: number, ty: number) => ctx.drawImage(img, frame.cutX, frame.cutY + sy, w, sh, x, y + ty, w, sh);
  piece(0, cut, 0);
  for (let k = 0; k < l.times; k++) piece(l.from, l.rows, cut + k * l.rows);
  piece(cut, h - cut, cut + l.times * l.rows);
}

/**
 * A strip of `frames` composed frames. `hold` keeps a shorter animation on its last frame
 * (a destruction) instead of looping it (an engine).
 */
function compose(scene: Phaser.Scene, dKey: string | null, bKey: string | null, l: Layout, frames: number, hold: boolean): HTMLCanvasElement {
  const canvas = document.createElement('canvas');
  canvas.width = SIZE * frames;
  canvas.height = SIZE;
  const ctx = canvas.getContext('2d')!;
  ctx.imageSmoothingEnabled = false;
  const pick = (key: string, i: number) => {
    const n = scene.textures.get(key).frameTotal - 1;
    return scene.textures.getFrame(key, n > 1 ? (hold ? Math.min(i, n - 1) : i % n) : undefined);
  };
  for (let i = 0; i < frames; i++) {
    const ox = i * SIZE;
    if (bKey) {
      const f = pick(bKey, i);
      for (const side of [-1, 1]) ctx.drawImage(f.source.image as CanvasImageSource, f.cutX, f.cutY, f.cutWidth, f.cutHeight, ox + l.bx + side * SIDE, l.by, f.cutWidth, f.cutHeight);
    }
    if (dKey) drawLong(ctx, pick(dKey, i), l, ox + l.dx, l.dy);
  }
  return canvas;
}

function register(scene: Phaser.Scene, m: Manifest, key: string, canvas: HTMLCanvasElement, frames: number): void {
  const tex = scene.textures.addCanvas(key, canvas);
  if (!tex) return;
  if (frames > 1) for (let i = 0; i < frames; i++) tex.add(i, 0, i * SIZE, 0, SIZE, SIZE);
  m[key] = { url: '', w: canvas.width, h: canvas.height, frameW: SIZE, frameH: SIZE, frames };
}

/** Adds each faction's titan textures, and their manifest entries, when its pack is there. */
export function composeCapitals(scene: Phaser.Scene, m: Manifest): void {
  for (const f of FACTION_IDS) {
    const dBase = findAsset(m, [f, 'dreadnought', 'base'], EXCLUDE);
    const bBase = findAsset(m, [f, 'battlecruiser', 'base'], EXCLUDE);
    if (!dBase || !bBase || scene.textures.exists(`gen-${f}-capital-base`)) continue;
    const l = layout(scene, dBase, bBase);
    register(scene, m, `gen-${f}-capital-base`, compose(scene, dBase, bBase, l, 1, false), 1);
    for (const [part, hold] of [['engine', false], ['destruction', true]] as const) {
      const d = findAsset(m, [f, 'dreadnought', part]);
      const b = findAsset(m, [f, 'battlecruiser', part]);
      if (!d && !b) continue;
      const frames = Math.max(d ? m[d].frames : 1, b ? m[b].frames : 1);
      register(scene, m, `gen-${f}-capital-${part}`, compose(scene, d, b, l, frames, hold), frames);
    }
  }
}
