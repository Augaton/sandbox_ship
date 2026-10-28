import './hud.css';
import { FACTIONS, FACTION_IDS, type FactionId } from '../data/units';
import type { BattleOptions } from '../scenes/BattleScene';

const hex = (c: number) => `#${c.toString(16).padStart(6, '0')}`;

/** Start screen: pick a faction, an opponent and a mode. */
export function showMenu(onStart: (opts: BattleOptions) => void, assetCount?: number): void {
  let player: FactionId = 'klaed';
  let enemy: FactionId = 'nairan';
  const root = document.createElement('div');
  root.className = 'ui menu';
  const choices = (name: string) =>
    FACTION_IDS.map((id) =>
      `<button data-${name}="${id}"><span class="swatch" style="background:${hex(FACTIONS[id].color)}"></span>${FACTIONS[id].name}</button>`,
    ).join('');
  const assets = assetCount == null ? '' : assetCount > 0
    ? `<p class="note">${assetCount} sprites importés.</p>`
    : '<p class="note">Aucun pack importé : les vaisseaux sont des formes provisoires. Dépose les .zip dans assets/raw puis lance npm run import-assets.</p>';
  root.innerHTML = `
    <div class="modal"><div class="panel">
      <h1>Sandbox Ship</h1>
      <p class="note">Chaque camp a une station. Construis des mines sur les astéroïdes, produis une flotte et détruis la station adverse. Ses points d'attache (chantier, hangars, bouclier, tourelles) tombent un par un.</p>
      <fieldset><legend>Ta faction</legend><div class="choices" role="group">${choices('player')}</div></fieldset>
      <fieldset><legend>Adversaire</legend><div class="choices" role="group">${choices('enemy')}</div></fieldset>
      <div class="actions">
        <button class="primary" data-mode="skirmish">Escarmouche</button>
        <button data-mode="sandbox">Bac à sable</button>
      </div>
      <p class="note">Escarmouche : contre l'IA, 1 500 crédits. Bac à sable : IA coupée, 50 000 crédits, placement libre de n'importe quelle unité.</p>
      ${assets}
    </div></div>`;
  document.body.appendChild(root);

  const sync = () => {
    if (enemy === player) enemy = FACTION_IDS.find((id) => id !== player)!;
    for (const b of root.querySelectorAll<HTMLButtonElement>('[data-player]')) b.setAttribute('aria-pressed', String(b.dataset.player === player));
    for (const b of root.querySelectorAll<HTMLButtonElement>('[data-enemy]')) {
      b.setAttribute('aria-pressed', String(b.dataset.enemy === enemy));
      b.disabled = b.dataset.enemy === player;
    }
  };
  root.addEventListener('click', (ev) => {
    const b = (ev.target as HTMLElement).closest('button');
    if (!b) return;
    if (b.dataset.player) player = b.dataset.player as FactionId;
    if (b.dataset.enemy) enemy = b.dataset.enemy as FactionId;
    if (b.dataset.mode) {
      root.remove();
      onStart({ player, enemy, mode: b.dataset.mode as BattleOptions['mode'] });
      return;
    }
    sync();
  });
  sync();
  root.querySelector<HTMLButtonElement>('[data-mode=skirmish]')!.focus();
}
