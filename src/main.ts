import Phaser from 'phaser';
import { play, unlockAudio } from './audio/sfx';
import { BackgroundScene } from './scenes/BackgroundScene';
import { BattleScene } from './scenes/BattleScene';
import { BootScene } from './scenes/BootScene';
import { GalaxyScene } from './scenes/GalaxyScene';

// Sound may only start after a gesture; every button gives a soft click.
document.addEventListener('pointerdown', unlockAudio);
document.addEventListener('keydown', unlockAudio);
document.addEventListener('click', (ev) => {
  if ((ev.target as HTMLElement).closest?.('button')) play('click');
});

new Phaser.Game({
  type: Phaser.AUTO,
  parent: 'game',
  backgroundColor: '#0b0d12',
  pixelArt: true,
  // Sounds are synthesised by src/audio/sfx.ts; Phaser's own sound system stays off.
  audio: { noAudio: true },
  scale: { mode: Phaser.Scale.RESIZE, width: window.innerWidth, height: window.innerHeight },
  // Scenes render in this order: the background goes under the battle.
  scene: [BootScene, GalaxyScene, BackgroundScene, BattleScene],
});
