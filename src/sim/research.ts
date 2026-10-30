// Research queue, shared by battles and the galaxy: levels are queued and paid for up front,
// then researched one after the other.
import { MAX_TECH_LEVEL, TECHS, type ResearchJob, type ResearchLevels, type TechId } from '../data/research';

/** Whoever researches: a battle faction or a galaxy faction. */
export interface Researcher {
  credits: number;
  research: ResearchLevels;
  /** The level being researched now. */
  researching: ResearchJob | null;
  /** Levels waiting their turn, already paid for. */
  researchQueue: ResearchJob[];
}

/** Every planned job, the running one first. */
export function researchJobs(r: Researcher): ResearchJob[] {
  return r.researching ? [r.researching, ...r.researchQueue] : [...r.researchQueue];
}

/** Level a tech reaches once what is planned for it is done. */
export function plannedLevel(r: Researcher, tech: TechId): number {
  return r.research[tech] + researchJobs(r).filter((j) => j.tech === tech).length;
}

/** Queues the next level of a tech; the caller checks that it is allowed. */
export function queueResearch(r: Researcher, tech: TechId): void {
  const level = plannedLevel(r, tech);
  const cost = TECHS[tech].costs[level];
  const time = TECHS[tech].times[level];
  r.credits -= cost;
  const job = { tech, remaining: time, total: time, cost };
  if (r.researching) r.researchQueue.push(job);
  else r.researching = job;
}

/**
 * Cancels the job at `index` (0 is the running one) and refunds it, with the later levels of
 * the same tech: they were priced for a level that will now come earlier.
 */
export function cancelResearchAt(r: Researcher, index: number): void {
  const jobs = researchJobs(r);
  const job = jobs[index];
  if (!job) return;
  const kept = jobs.filter((j, i) => i < index || j.tech !== job.tech);
  for (const j of jobs) if (!kept.includes(j)) r.credits += j.cost;
  r.researching = kept[0] ?? null;
  r.researchQueue = kept.slice(1);
}

/** Advances the running job; returns the tech that just gained a level, if any. */
export function advanceResearch(r: Researcher, dt: number): TechId | null {
  const job = r.researching;
  if (!job || (job.remaining -= dt) > 0) return null;
  r.research[job.tech] = Math.min(MAX_TECH_LEVEL, r.research[job.tech] + 1);
  r.researching = r.researchQueue.shift() ?? null;
  return job.tech;
}
