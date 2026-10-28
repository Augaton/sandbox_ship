// Research: permanent upgrades, one level at a time, bought with credits.

export type TechId = 'weapons' | 'shields' | 'armor' | 'engines' | 'mining' | 'hangars';

export interface TechDef {
  id: TechId;
  name: string;
  effect: string;
  /** Cost and time of each level, from level 1 to the last. */
  costs: number[];
  times: number[];
}

export const TECHS: Record<TechId, TechDef> = {
  weapons: { id: 'weapons', name: 'Armement', effect: '+10 % de dégâts par niveau', costs: [600, 1200, 2000], times: [30, 45, 60] },
  shields: { id: 'shields', name: 'Boucliers', effect: '+15 % de bouclier et de recharge par niveau', costs: [600, 1200, 2000], times: [30, 45, 60] },
  armor: { id: 'armor', name: 'Blindage', effect: '+10 % de coque par niveau', costs: [600, 1200, 2000], times: [30, 45, 60] },
  engines: { id: 'engines', name: 'Propulsion', effect: '+10 % de vitesse par niveau', costs: [400, 800, 1400], times: [25, 35, 50] },
  mining: { id: 'mining', name: 'Extraction', effect: '+20 % de revenu des mines par niveau', costs: [500, 1000, 1600], times: [25, 35, 50] },
  hangars: { id: 'hangars', name: 'Hangars', effect: '+2 appareils par hangar et lancements 15 % plus rapides, par niveau', costs: [500, 1000, 1600], times: [25, 35, 50] },
};

export const TECH_IDS = Object.keys(TECHS) as TechId[];
export const MAX_TECH_LEVEL = 3;

/** Bonus per level of the techs that scale a stat. */
export const TECH_BONUS = { weapons: 0.1, shields: 0.15, armor: 0.1, engines: 0.1, mining: 0.2 } as const;

export type ResearchLevels = Record<TechId, number>;

export function noResearch(): ResearchLevels {
  return { weapons: 0, shields: 0, armor: 0, engines: 0, mining: 0, hangars: 0 };
}

/** Research in progress. */
export interface ResearchJob {
  tech: TechId;
  remaining: number;
  total: number;
  cost: number;
}
