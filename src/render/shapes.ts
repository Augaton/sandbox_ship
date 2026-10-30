// Circles drawn with as many segments as their size on screen needs. Phaser splits every
// circle into 100, which made the rings the main drawing cost of a big battle.
import type Phaser from 'phaser';

type Graphics = Phaser.GameObjects.Graphics;

/** Enough segments to look round at this radius on screen, in pixels. */
function segments(screenRadius: number): number {
  return Math.max(12, Math.min(64, Math.round(screenRadius / 2.5)));
}

function circlePath(g: Graphics, x: number, y: number, r: number, zoom: number): Graphics {
  const n = segments(r * zoom);
  g.beginPath();
  g.moveTo(x + r, y);
  for (let i = 1; i < n; i++) {
    const a = (i / n) * Math.PI * 2;
    g.lineTo(x + Math.cos(a) * r, y + Math.sin(a) * r);
  }
  g.closePath();
  return g;
}

/** Outline of a circle, in the current line style. */
export function ring(g: Graphics, x: number, y: number, r: number, zoom: number): Graphics {
  return circlePath(g, x, y, r, zoom).strokePath();
}

/** Filled circle, in the current fill style. */
export function disc(g: Graphics, x: number, y: number, r: number, zoom: number): Graphics {
  return circlePath(g, x, y, r, zoom).fillPath();
}
