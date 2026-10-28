import type Phaser from 'phaser';
import { showMenu } from '../ui/menu';

const GAME_SCENES = ['battle', 'background', 'galaxy'];

/** Stops whatever is running and shows the start menu, which then starts the chosen mode. */
export function showStartMenu(game: Phaser.Game): void {
  for (const key of GAME_SCENES) {
    if (game.scene.isActive(key) || game.scene.isSleeping(key) || game.scene.isPaused(key)) game.scene.stop(key);
  }
  const manifest = game.cache.json.get('manifest') ?? {};
  showMenu((choice) => {
    if (choice.mode === 'campaign') {
      game.scene.start('galaxy', choice);
    } else {
      game.scene.start('background');
      game.scene.start('battle', choice);
    }
  }, Object.keys(manifest).length);
}
