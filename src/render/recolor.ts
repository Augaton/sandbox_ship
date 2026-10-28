// The Space Breaker parts (stations, turrets, platforms) are grey with red trim, whatever the
// faction. At boot each faction gets its own copy, the red trim turned to its color; greys and
// the blue solar panels stay as they are.
import type Phaser from 'phaser';
import { FACTIONS, FACTION_IDS, type FactionId } from '../data/units';
import { partKeys, type Manifest } from './sprites';

/** Hues this close to red (in degrees) count as trim. */
const TRIM = 35;

/** Texture of a part in a faction's colors, when one was made; the part itself otherwise. */
export function factionKey(scene: Phaser.Scene, key: string, faction: FactionId): string {
  const k = `${key}~${faction}`;
  return scene.textures.exists(k) ? k : key;
}

function hue(color: number): number {
  const r = (color >> 16) & 255;
  const g = (color >> 8) & 255;
  const b = color & 255;
  return rgbToHsl(r, g, b)[0];
}

function rgbToHsl(r: number, g: number, b: number): [number, number, number] {
  r /= 255;
  g /= 255;
  b /= 255;
  const max = Math.max(r, g, b);
  const min = Math.min(r, g, b);
  const l = (max + min) / 2;
  if (max === min) return [0, 0, l];
  const d = max - min;
  const s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
  const h = max === r ? (g - b) / d + (g < b ? 6 : 0) : max === g ? (b - r) / d + 2 : (r - g) / d + 4;
  return [h * 60, s, l];
}

function hslToRgb(h: number, s: number, l: number): [number, number, number] {
  const c = (1 - Math.abs(2 * l - 1)) * s;
  const x = c * (1 - Math.abs(((h / 60) % 2) - 1));
  const m = l - c / 2;
  const [r, g, b] = h < 60 ? [c, x, 0] : h < 120 ? [x, c, 0] : h < 180 ? [0, c, x] : h < 240 ? [0, x, c] : h < 300 ? [x, 0, c] : [c, 0, x];
  return [Math.round((r + m) * 255), Math.round((g + m) * 255), Math.round((b + m) * 255)];
}

/** Turns red trim pixels to the given hue, in place. */
export function shiftTrim(data: Uint8ClampedArray, toHue: number): void {
  for (let i = 0; i < data.length; i += 4) {
    if (data[i + 3] === 0) continue;
    const [h, s, l] = rgbToHsl(data[i], data[i + 1], data[i + 2]);
    const fromRed = Math.min(h, 360 - h);
    if (s < 0.35 || fromRed > TRIM) continue;
    const [r, g, b] = hslToRgb(toHue, s, l);
    data[i] = r;
    data[i + 1] = g;
    data[i + 2] = b;
  }
}

/** Makes the faction copies of every part; red factions keep the originals. */
export function recolorParts(scene: Phaser.Scene, m: Manifest): void {
  for (const key of partKeys(m)) {
    const frame = scene.textures.getFrame(key);
    if (!frame) continue;
    for (const f of FACTION_IDS) {
      const h = hue(FACTIONS[f].color);
      if (Math.min(h, 360 - h) <= TRIM || scene.textures.exists(`${key}~${f}`)) continue;
      const canvas = document.createElement('canvas');
      canvas.width = frame.cutWidth;
      canvas.height = frame.cutHeight;
      const ctx = canvas.getContext('2d', { willReadFrequently: true })!;
      ctx.drawImage(frame.source.image as CanvasImageSource, frame.cutX, frame.cutY, canvas.width, canvas.height, 0, 0, canvas.width, canvas.height);
      const img = ctx.getImageData(0, 0, canvas.width, canvas.height);
      shiftTrim(img.data, h);
      ctx.putImageData(img, 0, 0);
      scene.textures.addCanvas(`${key}~${f}`, canvas);
    }
  }
}
