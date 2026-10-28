import type Phaser from 'phaser';

const cache = new Map<string, number>();
let canvas: HTMLCanvasElement | null = null;

/**
 * Longest side of the opaque part of a texture's first frame, in texture pixels. Pack
 * sprites leave a lot of empty room around the ship, so sizes are matched on this.
 */
export function opaqueSize(scene: Phaser.Scene, key: string): number {
  const hit = cache.get(key);
  if (hit) return hit;
  const frame = scene.textures.getFrame(key, 0) ?? scene.textures.getFrame(key);
  const w = frame.cutWidth;
  const h = frame.cutHeight;
  canvas ??= document.createElement('canvas');
  canvas.width = w;
  canvas.height = h;
  const ctx = canvas.getContext('2d', { willReadFrequently: true })!;
  ctx.clearRect(0, 0, w, h);
  ctx.drawImage(frame.source.image as CanvasImageSource, frame.cutX, frame.cutY, w, h, 0, 0, w, h);
  const data = ctx.getImageData(0, 0, w, h).data;
  let minX = w, minY = h, maxX = -1, maxY = -1;
  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) {
      if (data[(y * w + x) * 4 + 3] <= 16) continue;
      if (x < minX) minX = x;
      if (x > maxX) maxX = x;
      if (y < minY) minY = y;
      if (y > maxY) maxY = y;
    }
  }
  const size = maxX < 0 ? Math.max(w, h) : Math.max(maxX - minX + 1, maxY - minY + 1);
  cache.set(key, size);
  return size;
}
