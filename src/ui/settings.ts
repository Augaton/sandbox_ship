// Player preferences, kept in the browser between sessions.

export interface Settings {
  /** Pan the battle view when the pointer touches a screen edge. */
  edgePan: boolean;
  /** Show hull and shield bars on everything, not only on damaged or selected units. */
  alwaysBars: boolean;
}

const KEY = 'sandbox-ship:settings';
const DEFAULTS: Settings = { edgePan: true, alwaysBars: false };

function load(): Settings {
  try {
    return { ...DEFAULTS, ...JSON.parse(localStorage.getItem(KEY) ?? '{}') };
  } catch {
    return { ...DEFAULTS };
  }
}

export const settings: Settings = load();

export function saveSettings(): void {
  try {
    localStorage.setItem(KEY, JSON.stringify(settings));
  } catch {
    // Private browsing or storage disabled: the settings last for this session only.
  }
}
