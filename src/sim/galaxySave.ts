// Campaign save games. Battles are not saved: the fleets that were fighting stay face to
// face on the map, and the battle starts again when the game is loaded.
import type { FactionId } from '../data/units';
import type { Fleet, Galaxy, GalaxyFaction, Planet } from './galaxy';

/** Bumped whenever the saved shape changes: older saves are then ignored. */
const VERSION = 4;
const KEY = 'sandbox-ship:campaign';

interface SavedGalaxy {
  version: number;
  savedAt: number;
  time: number;
  player: FactionId;
  planets: Planet[];
  lanes: [number, number][];
  fleets: Fleet[];
  factions: GalaxyFaction[];
  nextId: number;
  seed: number;
  winner: FactionId | null;
}

export function serializeGalaxy(g: Galaxy, savedAt = Date.now()): string {
  const save: SavedGalaxy = {
    version: VERSION, savedAt, time: g.time, player: g.player,
    planets: g.planets,
    lanes: g.lanes,
    fleets: [...g.fleets.values()].map((f) => ({ ...f, battleId: null })),
    factions: [...g.factions.values()],
    nextId: g.nextId, seed: g.seed, winner: g.winner,
  };
  return JSON.stringify(save);
}

/** Null when the text is not a save of this version. */
export function deserializeGalaxy(text: string): Galaxy | null {
  let s: SavedGalaxy;
  try {
    s = JSON.parse(text);
  } catch {
    return null;
  }
  if (s?.version !== VERSION || !Array.isArray(s.planets) || !Array.isArray(s.fleets) || !Array.isArray(s.lanes)) return null;
  return {
    time: s.time, player: s.player, planets: s.planets, lanes: s.lanes,
    fleets: new Map(s.fleets.map((f) => [f.id, f])),
    factions: new Map(s.factions.map((f) => [f.id, f])),
    battles: [], events: [], nextId: s.nextId, seed: s.seed, winner: s.winner,
  };
}

export interface SaveInfo {
  savedAt: number;
  player: FactionId;
  planets: number;
}

function storage(): Storage | null {
  try {
    return globalThis.localStorage ?? null;
  } catch {
    return null;
  }
}

/** Returns false when the browser refuses to store it (private browsing, quota). */
export function saveCampaign(g: Galaxy): boolean {
  try {
    storage()?.setItem(KEY, serializeGalaxy(g));
    return storage() != null;
  } catch {
    return false;
  }
}

export function loadCampaign(): Galaxy | null {
  const text = storage()?.getItem(KEY);
  return text ? deserializeGalaxy(text) : null;
}

export function campaignInfo(): SaveInfo | null {
  const text = storage()?.getItem(KEY);
  if (!text) return null;
  try {
    const s: SavedGalaxy = JSON.parse(text);
    if (s.version !== VERSION) return null;
    return { savedAt: s.savedAt, player: s.player, planets: s.planets.filter((p) => p.owner === s.player).length };
  } catch {
    return null;
  }
}

export function clearCampaign(): void {
  storage()?.removeItem(KEY);
}
