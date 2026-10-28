import { updateAbilities } from './abilities';
import { updateAI } from './ai';
import { updateProjectiles, updateRepair, updateShields, updateTargeting, updateWeapons } from './combat';
import { separate, updateShips } from './movement';
import { updateVision } from './vision';
import { updateConstruction, updateEconomy, updateHangars, updateProduction, updateRebuild, updateResearch } from './structures';
import { attachHardpoints, checkVictory, removeDead, type World } from './world';

/** Fixed simulation time step, in seconds. */
export const STEP = 1 / 60;

export function step(world: World, dt = STEP): void {
  world.time += dt;
  world.grid.rebuild(world.entities.values());
  updateAI(world, dt);
  updateEconomy(world, dt);
  updateProduction(world, dt);
  updateResearch(world, dt);
  updateConstruction(world, dt);
  updateRebuild(world, dt);
  updateHangars(world, dt);
  updateVision(world, dt);
  updateTargeting(world, dt);
  updateAbilities(world, dt);
  updateShips(world, dt);
  separate(world);
  attachHardpoints(world);
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
