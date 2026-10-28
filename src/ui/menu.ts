// Main menu: a title screen, then the setup of a new game, the controls and the credits.
import './hud.css';
import { version } from '../../package.json';
import { FACTIONS, FACTION_IDS, type FactionId } from '../data/units';
import { backdrop, shipSprite, type Manifest } from '../render/sprites';
import { GALAXY_H, GALAXY_W } from '../data/galaxy';
import { CLASSIC, generateLayout } from '../sim/galaxyGen';
import { campaignInfo } from '../sim/galaxySave';
import { MAP_SIZES, createSkirmish, type MapSize } from '../sim/scenario';
import { DIFFICULTIES, type Difficulty } from '../sim/world';
import { BATTLE_CONTROLS, GALAXY_CONTROLS, controlsList } from './controls';
import { el, hex } from './dom';
import { emblemUrl, logoUrl } from './logos';
import { optionsPanel } from './options';

export interface MenuChoice {
  player: FactionId;
  /** One or two opponents, every faction against all others. */
  enemies: FactionId[];
  mode: 'campaign' | 'skirmish' | 'sandbox';
  /** Continue the saved campaign instead of starting a new one. */
  resume?: boolean;
  /** Map of a new game: generated from the seed, or the hand-made classic one (two players). */
  map: 'random' | 'classic';
  seed: number;
  /** Battle map size, for skirmishes and the sandbox. */
  size: MapSize;
  difficulty: Difficulty;
}

type Screen = 'home' | 'new' | 'controls' | 'credits';

const MODES: [MenuChoice['mode'], string, string][] = [
  ['campaign', 'Campagne galactique', 'Une galaxie de planètes à conquérir. Chaque rencontre se joue en bataille, commandée ou résolue seule.'],
  ['skirmish', 'Escarmouche', 'Une seule bataille : chacun sa station, le dernier à en garder une gagne.'],
  ['sandbox', 'Bac à sable', 'IA coupée, 50 000 crédits, placement libre de n\'importe quelle unité.'],
];

const newSeed = () => 1 + Math.floor(Math.random() * 999999);

/**
 * The map the choices give, before launching: the galaxy (planets, lanes, capitals in their
 * faction's color) or the battlefield (stations and asteroid fields).
 */
function drawPreview(canvas: HTMLCanvasElement, c: MenuChoice): void {
  const ctx = canvas.getContext('2d')!;
  const { width: w, height: h } = canvas;
  ctx.fillStyle = '#0b0d12';
  ctx.fillRect(0, 0, w, h);
  const factions = [c.player, ...c.enemies];
  if (c.mode === 'campaign') {
    const layout = c.map === 'classic' && c.enemies.length === 1 ? CLASSIC : generateLayout(c.seed, factions.length);
    const sx = w / GALAXY_W;
    const sy = h / GALAXY_H;
    ctx.strokeStyle = '#2a3446';
    ctx.lineWidth = 1;
    for (const [a, b] of layout.lanes) {
      ctx.beginPath();
      ctx.moveTo(layout.planets[a].x * sx, layout.planets[a].y * sy);
      ctx.lineTo(layout.planets[b].x * sx, layout.planets[b].y * sy);
      ctx.stroke();
    }
    layout.planets.forEach((p, i) => {
      const owner = layout.capitals.indexOf(i);
      ctx.fillStyle = owner >= 0 ? hex(FACTIONS[factions[owner]].color) : '#949eb0';
      ctx.beginPath();
      ctx.arc(p.x * sx, p.y * sy, owner >= 0 ? 6 : 3.5, 0, Math.PI * 2);
      ctx.fill();
    });
    return;
  }
  const world = createSkirmish({ player: c.player, enemies: c.enemies, enemyAI: false, seed: c.seed, map: c.map, size: c.size });
  const sx = w / world.width;
  const sy = h / world.height;
  ctx.fillStyle = '#2a3446';
  for (const f of world.fields) {
    ctx.beginPath();
    ctx.arc(f.x * sx, f.y * sy, Math.max(2, f.radius * sx), 0, Math.PI * 2);
    ctx.fill();
  }
  for (const e of world.entities.values()) {
    if (e.type !== 'station') continue;
    ctx.fillStyle = hex(FACTIONS[e.faction].color);
    ctx.fillRect(e.x * sx - 5, e.y * sy - 5, 10, 10);
  }
}

export function showMenu(onStart: (choice: MenuChoice) => void, manifest: Manifest): void {
  const choice: MenuChoice = {
    player: 'klaed', enemies: ['nairan'], mode: 'campaign', map: 'random', seed: newSeed(), size: 'medium', difficulty: 'normal',
  };
  const root = el('div', { class: 'ui menu' });
  const modal = root.appendChild(el('div', { class: 'modal' }));
  const panel = modal.appendChild(el('div', { class: 'panel', role: 'dialog', 'aria-modal': 'true' }));
  const b = backdrop(manifest);
  const layers = ['linear-gradient(rgba(11, 13, 18, 0.55), rgba(11, 13, 18, 0.55))'];
  if (b.decor) layers.push(`url(assets/${b.decor}.png) center / min(900px, 90vw) no-repeat`);
  if (Object.keys(manifest).length > 0) layers.push(`url(assets/${b.tiles[0].key}.png) 0 0 / 1024px repeat`);
  modal.style.background = layers.join(', ');
  document.body.appendChild(root);
  const emblem = (id: FactionId, scale = 3) => `<img class="emblem" src="${emblemUrl(id, scale)}" alt="">`;
  let screen: Screen = 'home';

  const start = (c: MenuChoice) => {
    document.removeEventListener('keydown', onKey);
    root.remove();
    onStart(c);
  };

  const show = (next: Screen) => {
    screen = next;
    if (next === 'home') home();
    else if (next === 'new') setup();
    else if (next === 'controls') {
      panel.innerHTML = '<h2>Commandes</h2><h3>Bataille</h3>' + controlsList(BATTLE_CONTROLS) +
        '<h3>Carte galactique</h3>' + controlsList(GALAXY_CONTROLS) +
        '<div class="actions"><button data-go="home">Retour</button></div>';
    } else {
      panel.innerHTML = '<h2>Crédits</h2>' +
        '<p>Sprites : packs Void (flottes, environnement, vaisseau principal) de Foozle, et Space Breaker de zintoki, tous en CC0. Merci à eux.</p>' +
        '<p>Emblèmes, logo et jeu : Sandbox Ship.</p>' +
        '<div class="actions"><button data-go="home">Retour</button></div>';
    }
    panel.querySelector<HTMLElement>('button')?.focus();
  };

  function home(): void {
    const save = campaignInfo();
    const when = save ? new Date(save.savedAt).toLocaleString('fr-FR', { dateStyle: 'short', timeStyle: 'short' }) : '';
    panel.innerHTML = `
      <img class="logo" src="${logoUrl(5)}" alt="Sandbox Ship">
      <p class="note">Stratégie spatiale en temps réel : conquiers la galaxie, bataille après bataille.</p>
      <div class="home-actions">
        ${save ? `<button class="primary" data-act="resume">Continuer la campagne</button>
          <span class="muted">${FACTIONS[save.player].name}, ${save.planets} planète${save.planets > 1 ? 's' : ''}, sauvegardée le ${when}</span>` : ''}
        <button class="${save ? '' : 'primary'}" data-go="new">Nouvelle partie</button>
        <button data-act="options">Options</button>
        <button data-go="controls">Commandes</button>
        <button data-go="credits">Crédits</button>
      </div>
      <p class="muted">Version ${version}</p>
      ${Object.keys(manifest).length > 0 ? '' : '<p class="note">Aucun pack importé : les vaisseaux sont des formes provisoires. Dépose les .zip dans assets/raw puis lance npm run import-assets.</p>'}`;
  }

  function setup(): void {
    const c = choice;
    const battle = c.mode !== 'campaign';
    const cards = FACTION_IDS.map((id) => {
      const f = FACTIONS[id];
      const sprite = shipSprite(manifest, id, 'frigate');
      const picture = sprite ? `<img class="thumb" src="assets/${sprite}.png" alt="">` : '';
      return `<button data-player="${id}" aria-pressed="${c.player === id}"><span class="name">${emblem(id)}${f.name}${picture}</span>` +
        `<strong>${f.trait.name}</strong><small>${f.trait.description}</small></button>`;
    }).join('');
    const foes = FACTION_IDS.filter((id) => id !== c.player)
      .map((id) => `<button data-enemy="${id}" aria-pressed="${c.enemies.includes(id)}">${emblem(id, 2)}${FACTIONS[id].name}</button>`).join('');
    const group = (name: string, items: [string, string][], current: string, disabled: (v: string) => boolean = () => false) =>
      `<div class="choices" role="group">${items.map(([v, label]) =>
        `<button data-${name}="${v}" aria-pressed="${current === v}"${disabled(v) ? ' disabled' : ''}>${label}</button>`).join('')}</div>`;
    panel.innerHTML = `
      <h2>Nouvelle partie</h2>
      <section><h3>Mode</h3>${group('mode', MODES.map(([v, l]) => [v, l]), c.mode)}
        <p class="note">${MODES.find(([v]) => v === c.mode)![2]}</p></section>
      <section><h3>Ta faction</h3><div class="cards" role="group" aria-label="Ta faction">${cards}</div></section>
      <section><h3>Adversaires</h3><div class="choices" role="group" aria-label="Adversaires">${foes}</div>
        <p class="note">Un ou deux. À trois, chacun se bat contre tous.</p></section>
      <section><h3>Carte</h3>
        ${group('map', [['random', 'Aléatoire'], ['classic', 'Classique']], c.map, (v) => v === 'classic' && c.enemies.length > 1)}
        ${battle ? group('size', Object.entries(MAP_SIZES).map(([v, s]) => [v, s.name]), c.size) : ''}
        <div class="seed">
          <label>Graine<input type="number" name="seed" min="1" max="999999" value="${c.seed}"${c.map === 'classic' ? ' disabled' : ''}></label>
          <button data-act="reroll">Nouvelle graine</button>
        </div>
        <canvas class="preview" width="320" height="${battle ? Math.round(320 * MAP_SIZES[c.size].h / MAP_SIZES[c.size].w) : 200}" role="img" aria-label="Aperçu de la carte : les carrés et les gros points sont les bases de départ"></canvas>
        <p class="note">La même graine redonne la même carte.${c.enemies.length > 1 ? ' La carte classique est faite pour deux.' : ''}</p>
      </section>
      ${c.mode === 'sandbox' ? '' : `<section><h3>Difficulté</h3>${group('difficulty', Object.entries(DIFFICULTIES).map(([v, d]) => [v, d.name]), c.difficulty)}
        <p class="note">Plus difficile : l'IA gagne plus de crédits et réagit plus vite.</p></section>`}
      <div class="actions"><button data-go="home">Retour</button><button class="primary" data-act="start">Lancer la partie</button></div>`;
    const preview = panel.querySelector<HTMLCanvasElement>('.preview')!;
    drawPreview(preview, c);
    panel.querySelector<HTMLInputElement>('[name=seed]')!.oninput = (ev) => {
      c.seed = Math.max(1, Math.floor(Number((ev.target as HTMLInputElement).value)) || 1);
      drawPreview(preview, c);
    };
  }

  panel.addEventListener('click', (ev) => {
    const t = (ev.target as HTMLElement).closest('button');
    if (!t) return;
    const d = t.dataset;
    const c = choice;
    if (d.go) return show(d.go as Screen);
    if (d.act === 'options') {
      const over = root.appendChild(el('div', { class: 'modal', role: 'dialog', 'aria-modal': 'true' }));
      over.append(optionsPanel(() => over.remove()));
      over.querySelector<HTMLElement>('input')?.focus();
      return;
    }
    if (d.act === 'resume') return start({ ...c, mode: 'campaign', resume: true });
    if (d.act === 'start') {
      const seed = panel.querySelector<HTMLInputElement>('[name=seed]');
      if (seed) c.seed = Math.max(1, Math.floor(Number(seed.value)) || 1);
      return start({ ...c });
    }
    if (d.mode) c.mode = d.mode as MenuChoice['mode'];
    if (d.player) {
      c.player = d.player as FactionId;
      c.enemies = c.enemies.filter((e) => e !== c.player);
      if (c.enemies.length === 0) c.enemies = [FACTION_IDS.find((id) => id !== c.player)!];
    }
    if (d.enemy) {
      const id = d.enemy as FactionId;
      const on = c.enemies.includes(id);
      // Keep at least one opponent.
      if (on && c.enemies.length > 1) c.enemies = c.enemies.filter((e) => e !== id);
      if (!on) c.enemies = [...c.enemies, id];
      if (c.enemies.length > 1) c.map = 'random';
    }
    if (d.map) c.map = d.map as MenuChoice['map'];
    if (d.size) c.size = d.size as MapSize;
    if (d.difficulty) c.difficulty = d.difficulty as Difficulty;
    if (d.act === 'reroll') {
      c.seed = newSeed();
      c.map = 'random';
    }
    // Rebuild the setup screen and keep the focus on the button just pressed.
    const [attr, value] = Object.entries(d)[0] ?? [];
    setup();
    if (attr) panel.querySelector<HTMLElement>(`[data-${attr}="${value}"]`)?.focus();
  });

  const onKey = (ev: KeyboardEvent) => {
    const overlay = root.querySelectorAll('.modal').length > 1;
    if (ev.key === 'Escape' && !overlay && screen !== 'home') show('home');
  };
  document.addEventListener('keydown', onKey);
  show('home');
}
