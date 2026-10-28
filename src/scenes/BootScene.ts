import Phaser from 'phaser';
import { FACTION_IDS } from '../data/units';
import { createPlaceholders } from '../render/placeholders';
import { emblemCanvas } from '../ui/logos';
import type { Manifest } from '../render/sprites';
import { showStartMenu } from './flow';

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
      for (const f of FACTION_IDS) this.textures.addCanvas(`emblem-${f}`, emblemCanvas(f, 2));
      showStartMenu(this.game);
    });
    this.load.start();
  }
}
