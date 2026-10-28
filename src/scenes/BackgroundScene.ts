import Phaser from 'phaser';
import { opaqueSize } from '../render/opaque';
import { backdrop, planetKey, type Manifest } from '../render/sprites';

/** On-screen size of the backdrop planet, in pixels. */
const PLANET_SIZE = 190;

/** Parallax layers behind the battle, drawn by their own camera so that zoom leaves them alone. */
export class BackgroundScene extends Phaser.Scene {
  private layers: { sprite: Phaser.GameObjects.TileSprite; factor: number }[] = [];
  private decor: Phaser.GameObjects.Image | null = null;
  private planet!: Phaser.GameObjects.Sprite;

  constructor() {
    super('background');
  }

  create(): void {
    const manifest: Manifest = this.cache.json.get('manifest') ?? {};
    const b = backdrop(manifest);
    this.layers = b.tiles.map((t) => {
      const sprite = this.add.tileSprite(0, 0, this.scale.width, this.scale.height, t.key, t.frame).setOrigin(0);
      // Small pixel-art layers are doubled so that their stars do not look like dust.
      if (sprite.frame.cutWidth < 512) sprite.setTileScale(2);
      return { sprite, factor: t.factor };
    });
    this.decor = b.decor ? this.add.image(0, 0, b.decor).setScale(0.6).setAlpha(0.45) : null;
    const key = planetKey(manifest);
    this.planet = this.add.sprite(0, 0, key).setScale(PLANET_SIZE / opaqueSize(this, key));
    if (manifest[key]?.frames > 1) {
      if (!this.anims.exists(key)) this.anims.create({ key, frames: this.anims.generateFrameNumbers(key), frameRate: 8, repeat: -1 });
      this.planet.play(key);
    }
    this.scale.on('resize', this.fit, this);
    this.events.once('shutdown', () => this.scale.off('resize', this.fit, this));
    this.fit();
  }

  private fit(): void {
    for (const l of this.layers) l.sprite.setSize(this.scale.width, this.scale.height);
  }

  /** Follows the battle camera, each layer moving less than the one in front of it. */
  sync(scrollX: number, scrollY: number): void {
    for (const l of this.layers) {
      l.sprite.tilePositionX = (scrollX * l.factor) / l.sprite.tileScaleX;
      l.sprite.tilePositionY = (scrollY * l.factor) / l.sprite.tileScaleY;
    }
    const { width, height } = this.scale;
    this.decor?.setPosition(width * 0.25 - scrollX * 0.04, height * 0.6 - scrollY * 0.04);
    this.planet.setPosition(width * 0.7 - scrollX * 0.08, height * 0.3 - scrollY * 0.08);
  }
}
