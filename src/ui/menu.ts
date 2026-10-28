import './hud.css';
import { FACTIONS, FACTION_IDS, type FactionId } from '../data/units';
import { hex } from './dom';

export interface MenuChoice {
  player: FactionId;
  enemy: FactionId;
  mode: 'campaign' | 'skirmish' | 'sandbox';
}

/** Start screen: pick a faction, an opponent and a mode. */
export function showMenu(onStart: (choice: MenuChoice) => void, assetCount?: number): void {
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
      <p class="note">Conquiers la galaxie planète par planète. Chaque rencontre se joue en bataille spatiale, que tu commandes toi-même ou que tu laisses se résoudre seule.</p>
      <fieldset><legend>Ta faction</legend><div class="choices" role="group">${choices('player')}</div></fieldset>
      <fieldset><legend>Adversaire</legend><div class="choices" role="group">${choices('enemy')}</div></fieldset>
      <div class="actions">
        <button class="primary" data-mode="campaign">Campagne galactique</button>
        <button data-mode="skirmish">Escarmouche</button>
        <button data-mode="sandbox">Bac à sable</button>
      </div>
      <p class="note">Campagne : 14 planètes, une capitale chacun, l'IA en face. Escarmouche : une seule bataille contre l'IA, 1 500 crédits. Bac à sable : IA coupée, 50 000 crédits, placement libre.</p>
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
      onStart({ player, enemy, mode: b.dataset.mode as MenuChoice['mode'] });
      return;
    }
    sync();
  });
  sync();
  root.querySelector<HTMLButtonElement>('[data-mode=campaign]')!.focus();
}
