import { updateAI } from './ai';
import { updateProjectiles, updateRepair, updateShields, updateTargeting, updateWeapons } from './combat';
import { separate, updateShips } from './movement';
import { updateConstruction, updateEconomy, updateHangars, updateProduction } from './structures';
import { checkVictory, removeDead, type World } from './world';

/** Fixed simulation time step, in seconds. */
export const STEP = 1 / 60;

export function step(world: World, dt = STEP): void {
  world.time += dt;
  updateAI(world, dt);
  updateEconomy(world, dt);
  updateProduction(world, dt);
  updateConstruction(world, dt);
  updateHangars(world, dt);
  updateTargeting(world, dt);
  updateShips(world, dt);
  separate(world);
  updateWeapons(world, dt);
  updateProjectiles(world, dt);
  updateRepair(world, dt);
  updateShields(world, dt);
  removeDead(world);
  checkVictory(world);
}

/** Runs the simulation for `seconds` of game time. */
export function run(world: World, seconds: number, dt = STEP): void {
  const steps = Math.round(seconds / dt);
  for (let i = 0; i < steps; i++) step(world, dt);
}
