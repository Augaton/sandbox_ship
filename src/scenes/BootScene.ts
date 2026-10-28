import Phaser from 'phaser';
import { createPlaceholders } from '../render/placeholders';
import type { Manifest } from '../render/sprites';
import { showMenu } from '../ui/menu';

/** Loads the asset manifest, then every sprite it lists, then shows the start menu. */
export class BootScene extends Phaser.Scene {
  constructor() {
    super('boot');
  }

  preload(): void {
    this.load.json('manifest', 'assets/manifest.json');
  }

  create(): void {
    const manifest: Manifest = this.cache.json.get('manifest') ?? {};
    for (const [key, a] of Object.entries(manifest)) {
      if (a.frames > 1) this.load.spritesheet(key, a.url, { frameWidth: a.frameW, frameHeight: a.frameH });
      else this.load.image(key, a.url);
    }
    this.load.once('complete', () => {
      createPlaceholders(this);
      showMenu((opts) => {
        this.scene.launch('background');
        this.scene.start('battle', opts);
      }, Object.keys(manifest).length);
    });
    this.load.start();
  }
}
