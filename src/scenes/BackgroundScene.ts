import Phaser from 'phaser';
import { backgroundKeys, planetKey, type Manifest } from '../render/sprites';

/** Parallax layers behind the battle, drawn by their own camera so that zoom leaves them alone. */
export class BackgroundScene extends Phaser.Scene {
  private layers: { sprite: Phaser.GameObjects.TileSprite; factor: number }[] = [];
  private planet!: Phaser.GameObjects.Image;

  constructor() {
    super('background');
  }

  create(): void {
    const manifest: Manifest = this.cache.json.get('manifest') ?? {};
    const keys = backgroundKeys(manifest);
    this.layers = keys.map((key, i) => ({
      sprite: this.add.tileSprite(0, 0, this.scale.width, this.scale.height, key).setOrigin(0),
      factor: 0.02 + i * 0.04,
    }));
    this.planet = this.add.image(0, 0, planetKey(manifest)).setScale(0.6).setAlpha(0.8);
    this.scale.on('resize', this.fit, this);
    this.events.once('shutdown', () => this.scale.off('resize', this.fit, this));
    this.fit();
  }

  private fit(): void {
    const { width, height } = this.scale;
    for (const l of this.layers) {
      l.sprite.setSize(width, height);
      // Pack backgrounds are pictures, not tiles: scale them to cover the screen.
      const src = l.sprite.texture.getSourceImage();
      const placeholder = l.sprite.texture.key.startsWith('ph-');
      l.sprite.setTileScale(placeholder ? 1 : Math.max(width / src.width, height / src.height));
    }
  }

  /** Follows the battle camera, each layer moving less than the one in front of it. */
  sync(scrollX: number, scrollY: number): void {
    for (const l of this.layers) {
      l.sprite.tilePositionX = (scrollX * l.factor) / l.sprite.tileScaleX;
      l.sprite.tilePositionY = (scrollY * l.factor) / l.sprite.tileScaleY;
    }
    this.planet.setPosition(this.scale.width * 0.7 - scrollX * 0.08, this.scale.height * 0.3 - scrollY * 0.08);
  }
}
