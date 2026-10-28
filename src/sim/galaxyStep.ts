import { updateGalaxy, waitingForPlayer, type Galaxy } from './galaxy';
import { updateGalaxyAI } from './galaxyAI';

/** Fixed galaxy time step, in seconds. */
export const GALAXY_STEP = 0.1;

/** Advances the galaxy, unless the game is over or a battle waits for the player. */
export function stepGalaxy(g: Galaxy, dt = GALAXY_STEP): void {
  if (g.winner || waitingForPlayer(g)) return;
  g.time += dt;
  updateGalaxyAI(g, dt);
  updateGalaxy(g, dt);
}

export function runGalaxy(g: Galaxy, seconds: number, dt = GALAXY_STEP): void {
  const steps = Math.round(seconds / dt);
  for (let i = 0; i < steps; i++) stepGalaxy(g, dt);
}
