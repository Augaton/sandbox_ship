import { describe, expect, it } from 'vitest';
import { frameInfo, pngSize, slug } from '../scripts/import-assets.mjs';
import { findAsset, lookFor, matchAssets, projectileKey, type Manifest } from '../src/render/sprites';
import { addFaction, createWorld, spawnShip } from '../src/sim/world';

const entry = (frameW = 64, frames = 1) => ({ url: '', w: frameW * frames, h: frameW, frameW, frameH: frameW, frames });

describe('asset import', () => {
  it('slugs pack paths into dash-separated tokens', () => {
    expect(slug("Kla'ed - Fighter - Base")).toBe('klaed-fighter-base');
    expect(slug('void-fleet-1/Kla’ed/Engine Effects/PNGs/Battlecruiser')).toBe('void-fleet-1-klaed-engine-effects-pngs-battlecruiser');
  });

  it('splits horizontal strips of square frames only', () => {
    expect(frameInfo(640, 64)).toEqual({ frameW: 64, frameH: 64, frames: 10 });
    expect(frameInfo(64, 64)).toEqual({ frameW: 64, frameH: 64, frames: 1 });
    expect(frameInfo(32, 64)).toEqual({ frameW: 32, frameH: 64, frames: 1 });
    expect(frameInfo(100, 64)).toEqual({ frameW: 100, frameH: 64, frames: 1 });
  });

  it('reads PNG dimensions and rejects other files', () => {
    const buf = new Uint8Array(24);
    buf.set([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
    const v = new DataView(buf.buffer);
    v.setUint32(16, 320);
    v.setUint32(20, 64);
    expect(pngSize(buf)).toEqual({ w: 320, h: 64 });
    expect(pngSize(new TextEncoder().encode('GIF89a..................'))).toBeNull();
  });
});

describe('sprite lookup', () => {
  const m: Manifest = {
    'void-klaed-fighter-base': entry(),
    'void-klaed-fighter-engine': entry(64, 8),
    'void-klaed-fighter-base-preview': entry(),
    'void-klaed-torpedo-ship-base': entry(),
    'void-klaed-torpedo': entry(16, 4),
    'void-nairan-bullet': entry(8),
    'void-nairan-big-bullet': entry(8),
  };

  it('matches whole tokens, honours exclusions and skips previews', () => {
    expect(matchAssets(m, ['klaed', 'fighter', 'base'])).toEqual(['void-klaed-fighter-base']);
    expect(findAsset(m, ['torpedo'], ['ship'])).toBe('void-klaed-torpedo');
    expect(findAsset(m, ['bullet'], ['big'])).toBe('void-nairan-bullet');
    expect(findAsset(m, ['fight'])).toBeNull();
  });

  it('uses pack sprites when they exist and placeholders otherwise', () => {
    const w = createWorld(1000, 1000);
    addFaction(w, 'klaed', 0);
    const fighter = lookFor(m, spawnShip(w, 'klaed', 'fighter', 0, 0));
    expect(fighter).toMatchObject({ layers: ['void-klaed-fighter-base'], engine: 'void-klaed-fighter-engine', tint: false });
    expect(lookFor(m, spawnShip(w, 'klaed', 'frigate', 0, 0))).toMatchObject({ layers: ['ph-ship-frigate'], tint: true });
  });

  it('falls back to another faction projectile, then to a placeholder', () => {
    expect(projectileKey(m, 'klaed', 'bullet')).toEqual({ key: 'void-nairan-bullet', tint: false });
    expect(projectileKey(m, 'klaed', 'ray')).toEqual({ key: 'ph-shot-ray', tint: true });
  });
});
