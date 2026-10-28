// Small DOM helpers shared by the HUDs.

export function el<K extends keyof HTMLElementTagNameMap>(tag: K, attrs: Record<string, string> = {}, html = ''): HTMLElementTagNameMap[K] {
  const e = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs)) e.setAttribute(k, v);
  e.innerHTML = html;
  return e;
}

export const hex = (c: number) => `#${c.toString(16).padStart(6, '0')}`;
export const pct = (v: number) => `${Math.round(Math.max(0, Math.min(1, v)) * 100)}%`;

/** Build buttons are narrow: "Station niveau 2 requise" becomes "Niveau 2". */
export function shortReason(reason: string): string {
  if (reason === 'Crédits insuffisants') return 'Trop cher';
  return reason.replace(/^Station niveau (\d) requise$/, 'Niveau $1');
}

/** Toast area: short messages that fade out on their own. */
export function toast(area: HTMLElement, text: string): void {
  const t = area.appendChild(el('div', { class: 'panel' }));
  t.textContent = text;
  while (area.children.length > 4) area.firstElementChild!.remove();
  setTimeout(() => t.classList.add('out'), 2200);
  setTimeout(() => t.remove(), 2600);
}
