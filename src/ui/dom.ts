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

/** Toast area: short messages that fade out on their own. An alert stays longer and offers to go see. */
export function toast(area: HTMLElement, text: string, see?: () => void): void {
  const t = area.appendChild(el('div', { class: see ? 'panel alert' : 'panel' }));
  t.append(text);
  if (see) {
    const b = t.appendChild(el('button', { title: 'Retour arrière : aller à la dernière alerte' }, 'Voir'));
    b.onclick = see;
  }
  while (area.children.length > 4) area.firstElementChild!.remove();
  const life = see ? 4500 : 2200;
  setTimeout(() => t.classList.add('out'), life);
  setTimeout(() => t.remove(), life + 400);
}
