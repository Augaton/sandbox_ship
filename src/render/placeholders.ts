// Generated textures used whenever a pack sprite is missing. Drawn in white and grey so
// that a tint gives them the faction color. Like pack sprites, they face up.
import Phaser from 'phaser';
import { SHIPS, SHIP_CLASSES } from '../data/units';
import { BUILDABLES, HARDPOINTS, STATION } from '../data/structures';

type Ctx = CanvasRenderingContext2D;
type Draw = (ctx: Ctx, r: number) => void;

const FILL = '#8c8c8c';
const LINE = '#ffffff';

function canvas(scene: Phaser.Scene, key: string, w: number, h: number, draw: (ctx: Ctx) => void): void {
  if (scene.textures.exists(key)) return;
  const tex = scene.textures.createCanvas(key, Math.ceil(w), Math.ceil(h));
  if (!tex) return;
  draw(tex.context);
  tex.refresh();
}

/** A centered texture of diameter 2r (plus a margin for the outline). */
function sprite(scene: Phaser.Scene, key: string, r: number, draw: Draw): void {
  const size = r * 2 + 4;
  canvas(scene, key, size, size, (ctx) => {
    ctx.translate(size / 2, size / 2);
    ctx.lineJoin = 'round';
    ctx.fillStyle = FILL;
    ctx.strokeStyle = LINE;
    ctx.lineWidth = Math.max(1.5, r / 14);
    draw(ctx, r);
  });
}

function poly(ctx: Ctx, r: number, pts: number[]): void {
  ctx.beginPath();
  for (let i = 0; i < pts.length; i += 2) ctx.lineTo(pts[i] * r, pts[i + 1] * r);
  ctx.closePath();
  ctx.fill();
  ctx.stroke();
}

function circle(ctx: Ctx, x: number, y: number, r: number, fill = true): void {
  ctx.beginPath();
  ctx.arc(x, y, r, 0, Math.PI * 2);
  if (fill) ctx.fill();
  ctx.stroke();
}

function cockpit(ctx: Ctx, r: number, y = -0.3): void {
  ctx.save();
  ctx.fillStyle = LINE;
  ctx.beginPath();
  ctx.arc(0, y * r, Math.max(1.5, r * 0.14), 0, Math.PI * 2);
  ctx.fill();
  ctx.restore();
}

/** Hull outlines in units of the ship radius, nose at (0, -1). */
const HULLS: Record<string, number[]> = {
  scout: [0, -1, 0.45, 0.9, 0, 0.6, -0.45, 0.9],
  fighter: [0, -1, 0.8, 0.8, 0, 0.4, -0.8, 0.8],
  bomber: [0, -1, 1, 0.6, 0.5, 1, -0.5, 1, -1, 0.6],
  support: [0, -1, 0.85, -0.5, 0.85, 0.5, 0, 1, -0.85, 0.5, -0.85, -0.5],
  torpedo: [0, -1, 0.35, -0.6, 0.4, 1, -0.4, 1, -0.35, -0.6],
  frigate: [0, -1, 0.5, -0.4, 0.55, 0.9, 0.2, 1, -0.2, 1, -0.55, 0.9, -0.5, -0.4],
  battlecruiser: [0, -1, 0.7, 0.2, 0.75, 0.95, 0.3, 1, -0.3, 1, -0.75, 0.95, -0.7, 0.2],
  dreadnought: [0, -1, 0.3, -0.75, 0.85, 0.3, 0.85, 1, -0.85, 1, -0.85, 0.3, -0.3, -0.75],
};

const STRUCTURES: Record<string, Draw> = {
  station: (ctx, r) => {
    const pts: number[] = [];
    for (let i = 0; i < 8; i++) pts.push(Math.cos((i + 0.5) * Math.PI / 4), Math.sin((i + 0.5) * Math.PI / 4));
    poly(ctx, r, pts);
    circle(ctx, 0, 0, r * 0.55, false);
    circle(ctx, 0, 0, r * 0.22);
  },
  platform: (ctx, r) => {
    poly(ctx, r, [-0.8, -0.8, 0.8, -0.8, 0.8, 0.8, -0.8, 0.8]);
    circle(ctx, 0, 0, r * 0.4);
    ctx.fillRect(-r * 0.08, -r, r * 0.16, r * 0.6);
  },
  mine: (ctx, r) => {
    poly(ctx, r, [0, -1, 0.87, -0.5, 0.87, 0.5, 0, 1, -0.87, 0.5, -0.87, -0.5]);
    poly(ctx, r, [0, -0.45, 0.4, 0.3, -0.4, 0.3]);
  },
  hangar: (ctx, r) => {
    poly(ctx, r, [-0.9, -0.7, 0.9, -0.7, 0.9, 0.7, 0.35, 0.7, 0.35, 0.1, -0.35, 0.1, -0.35, 0.7, -0.9, 0.7]);
  },
};

const HARDPOINT_DRAW: Record<string, Draw> = {
  turret: (ctx, r) => {
    circle(ctx, 0, 0, r * 0.75);
    ctx.fillStyle = LINE;
    ctx.fillRect(-r * 0.12, -r, r * 0.24, r * 0.8);
  },
  heavyTurret: (ctx, r) => {
    circle(ctx, 0, 0, r * 0.8);
    ctx.fillStyle = LINE;
    ctx.fillRect(-r * 0.35, -r, r * 0.2, r * 0.8);
    ctx.fillRect(r * 0.15, -r, r * 0.2, r * 0.8);
  },
  hangar: (ctx, r) => poly(ctx, r, [-0.9, -0.6, 0.9, -0.6, 0.9, 0.6, -0.9, 0.6]),
  shield: (ctx, r) => {
    circle(ctx, 0, 0, r * 0.9);
    circle(ctx, 0, 0, r * 0.55, false);
    circle(ctx, 0, 0, r * 0.2, false);
  },
  shipyard: (ctx, r) => poly(ctx, r, [-0.9, -0.9, -0.5, -0.9, -0.5, 0.5, 0.5, 0.5, 0.5, -0.9, 0.9, -0.9, 0.9, 0.9, -0.9, 0.9]),
};

function stars(scene: Phaser.Scene, key: string, count: number, maxSize: number, seed: number): void {
  let s = seed;
  const rnd = () => ((s = (s * 16807) % 2147483647) / 2147483647);
  canvas(scene, key, 512, 512, (ctx) => {
    for (let i = 0; i < count; i++) {
      const a = 0.3 + rnd() * 0.7;
      ctx.fillStyle = `rgba(${200 + rnd() * 55 | 0}, ${200 + rnd() * 55 | 0}, 255, ${a})`;
      const size = rnd() < 0.9 ? 1 : maxSize;
      ctx.fillRect(rnd() * 512 | 0, rnd() * 512 | 0, size, size);
    }
  });
}

function asteroid(scene: Phaser.Scene, key: string, r: number, seed: number): void {
  let s = seed;
  const rnd = () => ((s = (s * 16807) % 2147483647) / 2147483647);
  sprite(scene, key, r, (ctx) => {
    ctx.fillStyle = '#5b5550';
    ctx.strokeStyle = '#8a827a';
    const pts: number[] = [];
    for (let i = 0; i < 9; i++) {
      const a = (i / 9) * Math.PI * 2;
      const d = 0.7 + rnd() * 0.3;
      pts.push(Math.cos(a) * d, Math.sin(a) * d);
    }
    poly(ctx, r, pts);
    ctx.fillStyle = '#48433f';
    circle(ctx, r * 0.2, -r * 0.15, r * 0.18);
  });
}

export function createPlaceholders(scene: Phaser.Scene): void {
  for (const cls of SHIP_CLASSES) {
    const r = SHIPS[cls].radius;
    sprite(scene, `ph-ship-${cls}`, r, (ctx) => {
      poly(ctx, r, HULLS[cls]);
      cockpit(ctx, r);
    });
  }
  sprite(scene, 'ph-st-station', STATION.radius, STRUCTURES.station);
  for (const [type, def] of Object.entries(BUILDABLES)) sprite(scene, `ph-st-${type}`, def.radius, STRUCTURES[type]);
  for (const [role, def] of Object.entries(HARDPOINTS)) sprite(scene, `ph-hp-${role}`, def.radius, HARDPOINT_DRAW[role]);

  const shot = (key: string, w: number, h: number) =>
    canvas(scene, key, w, h, (ctx) => {
      ctx.fillStyle = LINE;
      ctx.beginPath();
      ctx.roundRect(0, 0, w, h, Math.min(w, h) / 2);
      ctx.fill();
    });
  shot('ph-shot-bullet', 3, 6);
  shot('ph-shot-laser', 2, 14);
  shot('ph-shot-torpedo', 5, 10);
  shot('ph-shot-ray', 4, 24);

  for (let i = 0; i < 3; i++) asteroid(scene, `ph-asteroid-${i}`, 16 + i * 8, 11 + i * 97);
  stars(scene, 'ph-stars-0', 260, 2, 7);
  stars(scene, 'ph-stars-1', 90, 2, 13);

  canvas(scene, 'ph-planet', 440, 440, (ctx) => {
    const g = ctx.createRadialGradient(150, 140, 20, 220, 220, 220);
    g.addColorStop(0, '#6d8fb3');
    g.addColorStop(0.7, '#2c4566');
    g.addColorStop(1, '#0c1626');
    ctx.fillStyle = g;
    ctx.beginPath();
    ctx.arc(220, 220, 218, 0, Math.PI * 2);
    ctx.fill();
  });
  canvas(scene, 'ph-spark', 8, 8, (ctx) => {
    const g = ctx.createRadialGradient(4, 4, 0, 4, 4, 4);
    g.addColorStop(0, 'rgba(255,255,255,1)');
    g.addColorStop(1, 'rgba(255,255,255,0)');
    ctx.fillStyle = g;
    ctx.fillRect(0, 0, 8, 8);
  });
  canvas(scene, 'ph-boom', 64, 64, (ctx) => {
    const g = ctx.createRadialGradient(32, 32, 0, 32, 32, 32);
    g.addColorStop(0, 'rgba(255,255,230,1)');
    g.addColorStop(0.35, 'rgba(255,190,90,0.9)');
    g.addColorStop(1, 'rgba(255,80,20,0)');
    ctx.fillStyle = g;
    ctx.fillRect(0, 0, 64, 64);
  });
}
