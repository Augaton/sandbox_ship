// Pixel-art logos, drawn from character grids so that they stay easy to edit:
// '.' transparent, 'k' outline, 'a' faction color, 'b' lighter tone, 'w' white.
import { FACTIONS, type FactionId } from '../data/units';

export const EMBLEMS: Record<FactionId, string[]> = {
  // Kla'ed, the assault faction: a double arrowhead.
  klaed: [
    '................',
    '.......kk.......',
    '......kbbk......',
    '.....kbaabk.....',
    '....kbaaaabk....',
    '...kbaakkaabk...',
    '..kbaak..kaabk..',
    '.kbaak....kaabk.',
    '.kaak.kbbk.kaak.',
    '.kak.kbaabk.kak.',
    '.kk.kbaaaabk.kk.',
    '...kbaakkaabk...',
    '..kbaak..kaabk..',
    '..kaak....kaak..',
    '..kkk......kkk..',
    '................',
  ],
  // Nairan, the technology faction: a ring around a bright core.
  nairan: [
    '................',
    '.....kkkkkk.....',
    '...kkaaaaaakk...',
    '..kaabbbbbbaak..',
    '.kabk......kbak.',
    '.kab..kkkk..bak.',
    'kab..kbwwbk..bak',
    'kab.kbwwwwbk.bak',
    'kab.kbwwwwbk.bak',
    'kab..kbwwbk..bak',
    '.kab..kkkk..bak.',
    '.kabk......kbak.',
    '..kaabbbbbbaak..',
    '...kkaaaaaakk...',
    '.....kkkkkk.....',
    '................',
  ],
  // Nautolan, the biomechanical faction: three claws on one body.
  nautolan: [
    '................',
    '.kk....kk....kk.',
    '.kbk..kbbk..kbk.',
    '.kbak.kbbk.kabk.',
    '..kbakbaabkabk..',
    '..kbaabaabaabk..',
    '...kbaaaaaabk...',
    '...kbaaaaaabk...',
    '....kbaaaabk....',
    '....kbawwabk....',
    '....kbaaaabk....',
    '.....kbaabk.....',
    '.....kbaabk.....',
    '......kaak......',
    '.......kk.......',
    '................',
  ],
};

/** 5x7 pixel font, only the letters of the title. */
export const GLYPHS: Record<string, string[]> = {
  S: ['.###.', '#...#', '#....', '.###.', '....#', '#...#', '.###.'],
  A: ['.###.', '#...#', '#...#', '#####', '#...#', '#...#', '#...#'],
  N: ['#...#', '##..#', '#.#.#', '#..##', '#...#', '#...#', '#...#'],
  D: ['####.', '#...#', '#...#', '#...#', '#...#', '#...#', '####.'],
  B: ['####.', '#...#', '#...#', '####.', '#...#', '#...#', '####.'],
  O: ['.###.', '#...#', '#...#', '#...#', '#...#', '#...#', '.###.'],
  X: ['#...#', '#...#', '.#.#.', '..#..', '.#.#.', '#...#', '#...#'],
  H: ['#...#', '#...#', '#...#', '#####', '#...#', '#...#', '#...#'],
  I: ['#####', '..#..', '..#..', '..#..', '..#..', '..#..', '#####'],
  P: ['####.', '#...#', '#...#', '####.', '#....', '#....', '#....'],
};

const OUTLINE = '#0b0d12';

function hex(c: number): string {
  return `#${c.toString(16).padStart(6, '0')}`;
}

function lighten(c: number, t: number): string {
  const ch = (shift: number) => Math.round(((c >> shift) & 255) + (255 - ((c >> shift) & 255)) * t);
  return `rgb(${ch(16)}, ${ch(8)}, ${ch(0)})`;
}

function paint(grid: string[], color: (ch: string, x: number, y: number) => string | null, scale: number): HTMLCanvasElement {
  const c = document.createElement('canvas');
  c.width = grid[0].length * scale;
  c.height = grid.length * scale;
  const ctx = c.getContext('2d')!;
  grid.forEach((row, y) => {
    [...row].forEach((ch, x) => {
      const fill = color(ch, x, y);
      if (!fill) return;
      ctx.fillStyle = fill;
      ctx.fillRect(x * scale, y * scale, scale, scale);
    });
  });
  return c;
}

export function emblemCanvas(faction: FactionId, scale = 4): HTMLCanvasElement {
  const base = FACTIONS[faction].color;
  const palette: Record<string, string> = { k: OUTLINE, a: hex(base), b: lighten(base, 0.45), w: '#ffffff' };
  return paint(EMBLEMS[faction], (ch) => palette[ch] ?? null, scale);
}

/** "SANDBOX SHIP": light letters fading to amber, with a dark outline. */
export function logoCanvas(scale = 4): HTMLCanvasElement {
  const words = ['SANDBOX', 'SHIP'];
  const rows = Array.from({ length: 9 }, () => '');
  // One empty row and column all around leaves room for the outline.
  const text = words.map((w) => [...w].map((l) => GLYPHS[l]));
  for (let y = 0; y < 9; y++) {
    const line = text.map((word) => word.map((g) => (y === 0 || y === 8 ? '.....' : g[y - 1])).join('.')).join('...');
    rows[y] = `.${line}.`;
  }
  // Outline: every empty pixel next to a letter pixel.
  const lit = (x: number, y: number) => rows[y]?.[x] === '#';
  const grid = rows.map((row, y) =>
    [...row].map((ch, x) => {
      if (ch === '#') return '#';
      for (let dy = -1; dy <= 1; dy++) for (let dx = -1; dx <= 1; dx++) if (lit(x + dx, y + dy)) return 'k';
      return '.';
    }).join(''),
  );
  return paint(grid, (ch, _x, y) => (ch === 'k' ? OUTLINE : ch === '#' ? (y <= 4 ? '#e9edf3' : '#e8a13a') : null), scale);
}

const urls = new Map<string, string>();

export function emblemUrl(faction: FactionId, scale = 4): string {
  const key = `${faction}:${scale}`;
  if (!urls.has(key)) urls.set(key, emblemCanvas(faction, scale).toDataURL());
  return urls.get(key)!;
}

export function logoUrl(scale = 4): string {
  const key = `logo:${scale}`;
  if (!urls.has(key)) urls.set(key, logoCanvas(scale).toDataURL());
  return urls.get(key)!;
}
