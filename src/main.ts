import Phaser from 'phaser';
import { BackgroundScene } from './scenes/BackgroundScene';
import { BattleScene } from './scenes/BattleScene';
import { BootScene } from './scenes/BootScene';

new Phaser.Game({
  type: Phaser.AUTO,
  parent: 'game',
  backgroundColor: '#0b0d12',
  pixelArt: true,
  scale: { mode: Phaser.Scale.RESIZE, width: window.innerWidth, height: window.innerHeight },
  // Scenes render in this order: the background goes under the battle.
  scene: [BootScene, BackgroundScene, BattleScene],
});
