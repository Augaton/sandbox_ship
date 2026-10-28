#!/usr/bin/env node
// Extracts the asset packs dropped in assets/raw/ (archives or plain folders), copies
// their PNG files to public/assets/ and writes public/assets/manifest.json.
// Usage: npm run import-assets
import { execFileSync } from 'node:child_process';
import {
  copyFileSync, existsSync, mkdirSync, readdirSync, readFileSync, rmSync, statSync, writeFileSync,
} from 'node:fs';
import { basename, dirname, extname, join, relative, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const RAW = join(ROOT, 'assets/raw');
const EXTRACTED = join(ROOT, 'assets/extracted');
const OUT = join(ROOT, 'public/assets');
const OVERRIDES = join(ROOT, 'assets/overrides.json');
const ARCHIVES = new Set(['.zip', '.rar', '.7z']);

/** "Kla'ed - Fighter - Base" -> "klaed-fighter-base". Path separators become dashes too. */
export function slug(s) {
  return s.toLowerCase().replace(/['’]/g, '').replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '');
}

/** A horizontal strip of square frames is an animation; anything else is a single frame. */
export function frameInfo(w, h) {
  if (w > h && w % h === 0) return { frameW: h, frameH: h, frames: w / h };
  return { frameW: w, frameH: h, frames: 1 };
}

/** Reads width and height from the IHDR chunk of a PNG file. */
export function pngSize(buf) {
  const sig = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
  if (buf.length < 24 || sig.some((b, i) => buf[i] !== b)) return null;
  const v = new DataView(buf.buffer, buf.byteOffset, buf.byteLength);
  return { w: v.getUint32(16), h: v.getUint32(20) };
}

function walk(dir) {
  const out = [];
  for (const name of readdirSync(dir)) {
    const p = join(dir, name);
    if (statSync(p).isDirectory()) {
      if (name !== '__MACOSX') out.push(...walk(p));
    } else {
      out.push(p);
    }
  }
  return out;
}

function packSources() {
  const sources = [];
  for (const name of readdirSync(RAW)) {
    const p = join(RAW, name);
    const ext = extname(name).toLowerCase();
    if (statSync(p).isDirectory()) {
      sources.push({ pack: slug(name), dir: p });
    } else if (ARCHIVES.has(ext)) {
      const pack = slug(basename(name, extname(name)));
      const dest = join(EXTRACTED, pack);
      rmSync(dest, { recursive: true, force: true });
      mkdirSync(dest, { recursive: true });
      execFileSync('7z', ['x', '-y', `-o${dest}`, p], { stdio: 'ignore' });
      sources.push({ pack, dir: dest });
    }
  }
  return sources;
}

function main() {
  mkdirSync(RAW, { recursive: true });
  const overrides = existsSync(OVERRIDES) ? JSON.parse(readFileSync(OVERRIDES, 'utf8')) : {};
  rmSync(OUT, { recursive: true, force: true });
  mkdirSync(OUT, { recursive: true });
  const manifest = {};
  for (const { pack, dir } of packSources()) {
    let n = 0;
    for (const file of walk(dir)) {
      if (extname(file).toLowerCase() !== '.png') continue;
      const size = pngSize(readFileSync(file));
      if (!size) continue;
      const rel = relative(dir, file).slice(0, -extname(file).length);
      let key = slug(`${pack}/${rel}`);
      // Pack previews are big showcase pictures, not game sprites.
      if (/(^|-)previews?(-|$)/.test(key)) continue;
      for (let i = 2; manifest[key]; i++) key = slug(`${pack}/${rel}-${i}`);
      copyFileSync(file, join(OUT, `${key}.png`));
      const o = overrides[key];
      const frames = o
        ? { frameW: o.frameW, frameH: o.frameH, frames: Math.floor(size.w / o.frameW) * Math.floor(size.h / o.frameH) }
        : frameInfo(size.w, size.h);
      manifest[key] = { url: `assets/${key}.png`, w: size.w, h: size.h, ...frames };
      n++;
    }
    console.log(`${pack}: ${n} PNG`);
  }
  writeFileSync(join(OUT, 'manifest.json'), JSON.stringify(manifest, null, 1) + '\n');
  console.log(`${Object.keys(manifest).length} assets -> public/assets/manifest.json`);
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) main();
