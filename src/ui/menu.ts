import './hud.css';
import { FACTIONS, FACTION_IDS, type FactionId } from '../data/units';
import { backdrop, shipSprite, type Manifest } from '../render/sprites';
import { campaignInfo } from '../sim/galaxySave';
import { el } from './dom';
import { emblemUrl, logoUrl } from './logos';
import { optionsPanel } from './options';

export interface MenuChoice {
  player: FactionId;
  enemy: FactionId;
  mode: 'campaign' | 'skirmish' | 'sandbox';
  /** Continue the saved campaign instead of starting a new one. */
  resume?: boolean;
  /** Map of a new game: generated from the seed, or the hand-made classic one. */
  map: 'random' | 'classic';
  seed: number;
}

const newSeed = () => 1 + Math.floor(Math.random() * 999999);

/** Start screen: continue the saved campaign, or pick a faction, an opponent and a mode. */
export function showMenu(onStart: (choice: MenuChoice) => void, manifest: Manifest): void {
  let player: FactionId = 'klaed';
  let enemy: FactionId = 'nairan';
  let map: MenuChoice['map'] = 'random';
  const save = campaignInfo();
  const root = document.createElement('div');
  root.className = 'ui menu';
  const emblem = (id: FactionId) => `<img class="emblem" src="${emblemUrl(id, 3)}" alt="">`;
  const cards = FACTION_IDS.map((id) => {
    const f = FACTIONS[id];
    const sprite = shipSprite(manifest, id, 'frigate');
    const picture = sprite ? `<img class="thumb" src="assets/${sprite}.png" alt="">` : '';
    return `<button data-player="${id}"><span class="name">${emblem(id)}${f.name}${picture}</span>` +
      `<strong>${f.trait.name}</strong><small>${f.trait.description}</small></button>`;
  }).join('');
  const opponents = FACTION_IDS.map((id) => `<button data-enemy="${id}">${emblem(id)}${FACTIONS[id].name}</button>`).join('');
  const resume = save
    ? `<section><div class="actions resume"><button class="primary" data-resume>Continuer la campagne</button>` +
      `<span class="muted">${FACTIONS[save.player].name}, ${save.planets} planète${save.planets > 1 ? 's' : ''}, ` +
      `sauvegardée le ${new Date(save.savedAt).toLocaleString('fr-FR', { dateStyle: 'short', timeStyle: 'short' })}</span></div></section>`
    : '';
  const assets = Object.keys(manifest).length > 0
    ? ''
    : '<p class="note">Aucun pack importé : les vaisseaux sont des formes provisoires. Dépose les .zip dans assets/raw puis lance npm run import-assets.</p>';
  root.innerHTML = `
    <div class="modal"><div class="panel">
      <img class="logo" src="${logoUrl(5)}" alt="Sandbox Ship">
      <p class="note">Conquiers la galaxie planète par planète. Chaque rencontre se joue en bataille spatiale, que tu commandes toi-même ou que tu laisses se résoudre seule.</p>
      ${resume}
      <section><h3>Ta faction</h3><div class="cards" role="group" aria-label="Ta faction">${cards}</div></section>
      <section><h3>Adversaire</h3><div class="choices" role="group" aria-label="Adversaire">${opponents}</div></section>
      <section><h3>Carte</h3>
        <div class="choices" role="group" aria-label="Carte">
          <button data-map="random">Aléatoire</button>
          <button data-map="classic">Classique</button>
        </div>
        <div class="seed">
          <label>Graine<input type="number" name="seed" min="1" max="999999" value="${newSeed()}"></label>
          <button data-act="reroll">Nouvelle graine</button>
        </div>
        <p class="note">La même graine redonne la même carte, en campagne comme en escarmouche.</p>
      </section>
      <section><h3>Nouvelle partie</h3>
        <div class="actions">
          <button class="${save ? '' : 'primary'}" data-mode="campaign">Campagne galactique</button>
          <button data-mode="skirmish">Escarmouche</button>
          <button data-mode="sandbox">Bac à sable</button>
          <button data-act="options">Options</button>
        </div>
        <p class="note">Campagne : 14 planètes, une capitale chacun, l'IA en face. Escarmouche : une seule bataille contre l'IA, 1 500 crédits. Bac à sable : IA coupée, 50 000 crédits, placement libre.</p>
      </section>
      ${assets}
    </div></div>`;
  const b = backdrop(manifest);
  const layers = ['linear-gradient(rgba(11, 13, 18, 0.55), rgba(11, 13, 18, 0.55))'];
  if (b.decor) layers.push(`url(assets/${b.decor}.png) center / min(900px, 90vw) no-repeat`);
  if (Object.keys(manifest).length > 0) layers.push(`url(assets/${b.tiles[0].key}.png) 0 0 / 1024px repeat`);
  root.querySelector<HTMLElement>('.modal')!.style.background = layers.join(', ');
  document.body.appendChild(root);

  const seedInput = root.querySelector<HTMLInputElement>('[name=seed]')!;
  const seed = () => Math.max(1, Math.floor(Number(seedInput.value)) || 1);
  const sync = () => {
    if (enemy === player) enemy = FACTION_IDS.find((id) => id !== player)!;
    for (const b of root.querySelectorAll<HTMLButtonElement>('[data-map]')) b.setAttribute('aria-pressed', String(b.dataset.map === map));
    seedInput.disabled = map === 'classic';
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
    if (b.dataset.map) map = b.dataset.map as MenuChoice['map'];
    if (b.dataset.act === 'reroll') {
      seedInput.value = String(newSeed());
      map = 'random';
    }
    if (b.dataset.act === 'options') {
      const modal = root.appendChild(el('div', { class: 'modal', role: 'dialog', 'aria-modal': 'true' }));
      modal.append(optionsPanel(() => modal.remove()));
      modal.querySelector<HTMLElement>('input')?.focus();
      return;
    }
    if (b.dataset.resume != null) {
      root.remove();
      onStart({ player, enemy, mode: 'campaign', resume: true, map, seed: seed() });
      return;
    }
    if (b.dataset.mode) {
      root.remove();
      onStart({ player, enemy, mode: b.dataset.mode as MenuChoice['mode'], map, seed: seed() });
      return;
    }
    sync();
  });
  sync();
  root.querySelector<HTMLButtonElement>(save ? '[data-resume]' : '[data-mode=campaign]')!.focus();
}
