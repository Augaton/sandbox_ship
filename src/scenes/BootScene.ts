import Phaser from 'phaser';
import { FACTION_IDS } from '../data/units';
import { composeCapitals } from '../render/capitals';
import { createPlaceholders } from '../render/placeholders';
import { recolorParts } from '../render/recolor';
import { el, pct } from '../ui/dom';
import { emblemCanvas } from '../ui/logos';
import '../ui/hud.css';
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
    // A bar while some 250 sprites load, instead of a black screen.
    const screen = el('div', { class: 'ui loading' },
      '<div class="panel" role="progressbar" aria-label="Chargement" aria-valuemin="0" aria-valuemax="100">' +
      '<p>Chargement des vaisseaux…</p><div class="bar"><i style="width:0;background:var(--accent)"></i></div></div>');
    document.body.appendChild(screen);
    const bar = screen.querySelector<HTMLElement>('.bar i')!;
    const box = screen.querySelector<HTMLElement>('[role=progressbar]')!;
    this.load.on('progress', (v: number) => {
      bar.style.width = pct(v);
      box.setAttribute('aria-valuenow', String(Math.round(v * 100)));
    });
    for (const [key, a] of Object.entries(manifest)) {
      if (a.frames > 1) this.load.spritesheet(key, a.url, { frameWidth: a.frameW, frameHeight: a.frameH });
      else this.load.image(key, a.url);
    }
    this.load.once('complete', () => {
      createPlaceholders(this);
      // The manifest is shared through the cache: the titans' entries reach every scene.
      composeCapitals(this, manifest);
      recolorParts(this, manifest);
      screen.remove();
      for (const f of FACTION_IDS) this.textures.addCanvas(`emblem-${f}`, emblemCanvas(f, 2));
      showStartMenu(this.game);
    });
    this.load.start();
  }
}
