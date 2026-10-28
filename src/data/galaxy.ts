// The galaxy map: planets and hyperspace lanes. The east half mirrors the west half
// (x -> W - x, y -> H - y) so both capitals start on equal terms.

export const GALAXY_W = 1600;
export const GALAXY_H = 1000;

export interface PlanetDef {
  name: string;
  x: number;
  y: number;
  /** Credits per second while owned. */
  income: number;
  mineSlots: number;
}

const WEST: PlanetDef[] = [
  { name: 'Vesper', x: 150, y: 500, income: 6, mineSlots: 3 },
  { name: 'Orin', x: 330, y: 260, income: 3, mineSlots: 2 },
  { name: 'Maru', x: 330, y: 740, income: 3, mineSlots: 2 },
  { name: 'Kaldra', x: 520, y: 470, income: 4, mineSlots: 2 },
  { name: 'Sorel', x: 600, y: 150, income: 2, mineSlots: 1 },
  { name: 'Thessa', x: 640, y: 820, income: 2, mineSlots: 1 },
  { name: 'Nadir', x: 800, y: 330, income: 5, mineSlots: 3 },
];
const EAST_NAMES = ['Ostra', 'Pélion', 'Yvra', 'Tarsis', 'Brume', 'Ixion', 'Calyx'];

/** Planet i of the west half mirrors planet i + 7. Nadir and Calyx mirror each other. */
export const PLANETS: PlanetDef[] = [
  ...WEST,
  ...WEST.map((p, i) => ({ ...p, name: EAST_NAMES[i], x: GALAXY_W - p.x, y: GALAXY_H - p.y })),
];

const WEST_LANES: [number, number][] = [
  [0, 1], [0, 2], [1, 3], [2, 3], [1, 4], [2, 5], [3, 6], [3, 13], [4, 6], [5, 13], [6, 13], [4, 12],
];
const mirror = (i: number) => (i + 7) % 14;

/** Each west lane and its mirror image, without duplicates. */
export const LANES: [number, number][] = (() => {
  const seen = new Set<string>();
  const out: [number, number][] = [];
  for (const [a, b] of WEST_LANES.flatMap(([a, b]) => [[a, b], [mirror(a), mirror(b)]])) {
    const key = a < b ? `${a}-${b}` : `${b}-${a}`;
    if (!seen.has(key)) {
      seen.add(key);
      out.push([a, b]);
    }
  }
  return out;
})();

/** Index of each side's capital in PLANETS. */
export const CAPITALS = [0, 7] as const;

export const GALAXY_RULES = {
  startCredits: 2000,
  /** Galaxy income of one mine, per second. */
  mineIncome: 3,
  stationCost: 1500,
  stationTime: 40,
  /** Seconds a lone fleet must hold an undefended enemy planet before taking it. */
  captureTime: 15,
  neutralCaptureTime: 5,
  /** Battles resolved automatically run this many times faster than the galaxy clock. */
  autoSpeed: 15,
  /** An automatic battle still undecided after this many seconds ends with the attacker falling back. */
  autoTimeout: 600,
};
