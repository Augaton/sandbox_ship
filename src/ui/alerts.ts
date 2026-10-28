// What happened to the player out of sight: attacks and losses, with where they happened.

export interface Alert {
  text: string;
  x: number;
  y: number;
  /** Scene time, in ms. */
  time: number;
}

/** Within this long and this close to a reported spot, more of the same is old news. */
export const ALERT_GAP = 15000;
export const ALERT_RADIUS = 900;

/** Whether an event here is worth an alert: nothing was reported near it lately. */
export function isNews(alerts: Alert[], x: number, y: number, now: number): boolean {
  return !alerts.some((a) => now - a.time < ALERT_GAP && Math.hypot(a.x - x, a.y - y) < ALERT_RADIUS);
}
