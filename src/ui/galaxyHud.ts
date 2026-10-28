// HTML overlay of the galaxy map. Like the battle HUD, it reads the simulation and calls
// its commands.
import './hud.css';
import { GALAXY_RULES } from '../data/galaxy';
import { BUILDABLES, STATION } from '../data/structures';
import { FACTIONS, SHIPS, SHIP_CLASSES, type ShipClass } from '../data/units';
import type { GalaxyScene } from '../scenes/GalaxyScene';
import { defenseLeft, sideStrength, survivors, type ShipCounts } from '../sim/battle';
import {
  buildPrice, cancelBuild, canBuild, enqueueBuild, fleetsAt, incomeOf,
  type GalaxyBattle, type GalaxyBuild, type GalaxyEvent, type Planet,
} from '../sim/galaxy';
import { el, hex, pct, shortReason, toast } from './dom';

const REFRESH_MS = 100;
const SPEEDS = [1, 2, 4];
const SHIPYARD_CLASSES = SHIP_CLASSES.filter((c) => SHIPS[c].source === 'shipyard');

function describeShips(ships: ShipCounts): string {
  const parts = SHIPYARD_CLASSES.filter((c) => ships[c]).map((c) => `${ships[c]}× ${SHIPS[c].name}`);
  return parts.length > 0 ? parts.join(', ') : 'aucun vaisseau';
}

function buildLabel(planet: Planet, b: GalaxyBuild): string {
  if (b.kind === 'ship') return SHIPS[b.cls].name;
  if (b.kind === 'station') return planet.station === 0 ? 'Station' : 'Améliorer la station';
  return BUILDABLES[b.kind].name;
}

export class GalaxyHud {
  private root = el('div', { class: 'ui' });
  private credits!: HTMLElement;
  private panel!: HTMLElement;
  private toasts!: HTMLElement;
  private modal: HTMLElement | null = null;
  private timer = 0;
  private lastPanel = '';
  /** Planet and fleet the send quantities were typed for: they are kept only while both stay the same. */
  private lastFleet = '';
  private buttons: { b: HTMLButtonElement; build: GalaxyBuild }[] = [];

  constructor(private scene: GalaxyScene) {
    const f = FACTIONS[scene.opts.player];
    const bar = this.root.appendChild(el('div', { class: 'topbar panel' }));
    bar.append(el('span', { class: 'faction' }, `<span class="swatch" style="background:${hex(f.color)}"></span>${f.name}`));
    this.credits = bar.appendChild(el('span', { class: 'credits' }));
    bar.append(el('span', { class: 'spacer' }));
    const speeds = bar.appendChild(el('div', { class: 'group', role: 'group', 'aria-label': 'Vitesse du jeu' }));
    speeds.appendChild(el('button', { 'data-speed': '0', title: 'Pause (Espace)' }, 'Pause')).onclick = () => scene.togglePause();
    for (const s of SPEEDS) {
      speeds.appendChild(el('button', { 'data-speed': String(s), 'aria-label': `Vitesse ×${s}` }, `×${s}`)).onclick = () => scene.setSpeed(s);
    }
    const group = bar.appendChild(el('div', { class: 'group' }));
    group.appendChild(el('button', {}, 'Aide')).onclick = () => this.showHelp();
    group.appendChild(el('button', {}, 'Menu')).onclick = () => scene.toMenu();

    this.panel = this.root.appendChild(el('aside', { class: 'galaxy-panel panel', 'aria-label': 'Planète' }));
    this.panel.addEventListener('click', (ev) => {
      const b = (ev.target as HTMLElement).closest('button');
      if (!b) return;
      const g = scene.galaxy;
      if (b.dataset.cancel != null && scene.selected != null) cancelBuild(g, scene.selected, Number(b.dataset.cancel));
      const item = this.buttons.find((x) => x.b === b);
      if (item && scene.selected != null) {
        const r = enqueueBuild(g, scene.opts.player, scene.selected, item.build);
        if (!r.ok) this.toast(r.reason);
      }
      this.refresh();
    });
    this.toasts = this.root.appendChild(el('div', { class: 'toasts', role: 'status', 'aria-live': 'polite' }));
    document.body.appendChild(this.root);
    this.root.addEventListener('click', (ev) => {
      if (ev.target instanceof HTMLButtonElement && ev.detail > 0) ev.target.blur();
    });
    this.refreshModes();
    this.refresh();
  }

  destroy(): void {
    this.root.remove();
  }

  setVisible(visible: boolean): void {
    this.root.hidden = !visible;
  }

  toast(text: string): void {
    toast(this.toasts, text);
  }

  refreshModes(): void {
    for (const b of this.root.querySelectorAll<HTMLButtonElement>('[data-speed]')) {
      const s = Number(b.dataset.speed);
      b.setAttribute('aria-pressed', String(s === 0 ? this.scene.paused : !this.scene.paused && this.scene.speed === s));
    }
  }

  update(delta: number): void {
    this.timer -= delta;
    if (this.timer > 0) return;
    this.timer = REFRESH_MS;
    this.refresh();
  }

  /** Ships to send, as chosen in the fleet section of the panel (everything by default). */
  sendCounts(): ShipCounts {
    const out: ShipCounts = {};
    for (const input of this.panel.querySelectorAll<HTMLInputElement>('input[data-cls]')) {
      const n = Math.max(0, Math.min(Number(input.max), Math.floor(Number(input.value) || 0)));
      if (n > 0) out[input.dataset.cls as ShipClass] = n;
    }
    return out;
  }

  refresh(): void {
    const g = this.scene.galaxy;
    const player = this.scene.opts.player;
    const f = g.factions.get(player)!;
    const owned = g.planets.filter((p) => p.owner === player).length;
    this.credits.innerHTML = `Crédits <strong>${Math.floor(f.credits)}</strong> <span class="muted">+${incomeOf(g, player)}/s</span>` +
      ` <span class="muted">${owned} planète${owned > 1 ? 's' : ''}</span>`;
    const id = this.scene.selected;
    const p = id == null ? null : g.planets[id];
    const sig = p ? this.signature(p) : '';
    if (sig !== this.lastPanel) {
      this.lastPanel = sig;
      this.renderPanel(p);
    }
    if (p) this.updatePanel(p);
  }

  /** Changes that need the panel rebuilt; everything else is updated in place. */
  private signature(p: Planet): string {
    const g = this.scene.galaxy;
    const fleets = fleetsAt(g, p.id).map((f) => `${f.faction}:${describeShips(f.ships)}`).join(';');
    const battle = g.battles.some((b) => b.planetId === p.id);
    return [p.id, p.owner, p.station, p.mines, p.platforms, fleets, p.queue.map((q) => q.kind).join(','), battle, p.occupation?.faction].join('|');
  }

  private renderPanel(p: Planet | null): void {
    const previous = new Map([...this.panel.querySelectorAll<HTMLInputElement>('input[data-cls]')].map((i) => [i.dataset.cls, i.value]));
    this.buttons = [];
    if (!p) {
      this.panel.innerHTML = '<h2>Carte galactique</h2>' +
        '<p class="muted">Clic sur une planète : la sélectionner.<br>Clic droit sur une autre : y envoyer ta flotte.</p>' +
        '<p class="muted">Une flotte seule sur une planète sans défense la prend après quelques secondes.</p>';
      return;
    }
    const g = this.scene.galaxy;
    const player = this.scene.opts.player;
    const owner = p.owner ? FACTIONS[p.owner].name : 'Neutre';
    const lines: string[] = [`<h2>${p.name}</h2>`, `<div class="muted">${owner}</div>`];
    lines.push(`<div>Revenu +${p.income}/s, plus ${GALAXY_RULES.mineIncome}/s par mine</div>`);
    lines.push(`<div>Mines ${p.mines} / ${p.mineSlots}</div>`);
    lines.push(`<div>${p.station > 0 ? `Station niveau ${p.station}` : 'Pas de station'}, ${p.platforms} plateforme${p.platforms > 1 ? 's' : ''}</div>`);
    if (p.occupation) lines.push(`<div class="note" data-occupation></div>`);
    if (g.battles.some((b) => b.planetId === p.id)) lines.push('<div><strong>Combat en cours</strong></div>');

    const own = fleetsAt(g, p.id, player)[0];
    const fleetSig = own ? `${p.id}:${describeShips(own.ships)}` : '';
    if (fleetSig !== this.lastFleet) previous.clear();
    this.lastFleet = fleetSig;
    if (own) {
      lines.push('<h3>Ta flotte</h3>');
      for (const c of SHIPYARD_CLASSES) {
        const n = own.ships[c];
        if (!n) continue;
        const value = Math.min(n, Number(previous.get(c) ?? n));
        lines.push(`<label class="fleet-row">${SHIPS[c].name} (${n})<input type="number" data-cls="${c}" min="0" max="${n}" value="${value}" aria-label="${SHIPS[c].name} à envoyer"></label>`);
      }
      lines.push('<p class="muted">Clic droit sur une planète : envoyer ces vaisseaux.</p>');
    }
    for (const f of fleetsAt(g, p.id).filter((x) => x.faction !== player)) {
      lines.push(`<h3>Flotte ${FACTIONS[f.faction].name}</h3><div>${describeShips(f.ships)}</div>`);
    }
    if (p.owner === player) {
      lines.push('<h3>Construire</h3><div class="grid" data-builds></div><div class="queue" data-queue></div>');
    }
    this.panel.innerHTML = lines.join('');

    const grid = this.panel.querySelector('[data-builds]');
    if (grid) {
      const builds: GalaxyBuild[] = [{ kind: 'station' }, { kind: 'mine' }, { kind: 'platform' }, ...SHIPYARD_CLASSES.map((cls) => ({ kind: 'ship' as const, cls }))];
      for (const build of builds) {
        const b = grid.appendChild(el('button', {}, `<span>${buildLabel(p, build)}</span><small></small>`));
        this.buttons.push({ b, build });
      }
      const queue = this.panel.querySelector('[data-queue]')!;
      queue.innerHTML = p.queue.length === 0
        ? '<span class="muted">File vide</span>'
        : '<span class="muted">File (clic : annuler)</span>' + p.queue.map((q, i) => {
          const name = q.kind === 'ship' ? SHIPS[q.cls].name : q.kind === 'station' ? STATION.name : BUILDABLES[q.kind].name;
          return `<button data-cancel="${i}" aria-label="Annuler ${name}">${name}<span class="progress"></span></button>`;
        }).join('');
    }
  }

  private updatePanel(p: Planet): void {
    const g = this.scene.galaxy;
    const player = this.scene.opts.player;
    for (const { b, build } of this.buttons) {
      const r = canBuild(g, player, p.id, build);
      b.disabled = !r.ok;
      const detail = build.kind === 'ship'
        ? `${buildPrice(p, build)} cr, ${SHIPS[build.cls].buildTime} s`
        : `${buildPrice(p, build)} cr`;
      b.querySelector('small')!.textContent = r.ok ? detail : shortReason(r.reason);
      b.title = r.ok ? detail : r.reason;
    }
    const bar = this.panel.querySelector<HTMLElement>('.progress');
    if (bar && p.queue[0]) bar.style.width = pct(1 - p.queue[0].remaining / p.queue[0].total);
    const occ = this.panel.querySelector('[data-occupation]');
    if (occ && p.occupation) {
      occ.textContent = `Prise par ${FACTIONS[p.occupation.faction].name} dans ${Math.ceil(p.occupation.remaining)} s`;
    }
  }

  onEvent(ev: GalaxyEvent): void {
    const g = this.scene.galaxy;
    const player = this.scene.opts.player;
    if (ev.type === 'captured') {
      const p = g.planets[ev.planetId];
      if (ev.faction === player) this.toast(`${p.name} est à toi`);
      else if (ev.from === player) this.toast(`${p.name} perdue`);
    } else if (ev.type === 'built' && ev.faction === player) {
      this.toast(`${g.planets[ev.planetId].name} : ${ev.what} terminé`);
    } else if (ev.type === 'battleEnd' && (ev.attacker === player || ev.defender === player)) {
      const winner = ev.result === 'attacker' ? ev.attacker : ev.defender;
      const what = ev.result === 'retreat'
        ? (ev.attacker === player ? 'retraite' : "l'ennemi se replie")
        : winner === player ? 'victoire' : 'défaite';
      this.toast(`Bataille de ${g.planets[ev.planetId].name} : ${what}`);
    }
  }

  private openModal(content: HTMLElement): void {
    this.modal?.remove();
    this.modal = this.root.appendChild(el('div', { class: 'modal', role: 'dialog', 'aria-modal': 'true' }));
    this.modal.append(content);
    content.querySelector<HTMLElement>('button')?.focus();
  }

  private closeModal(): void {
    this.modal?.remove();
    this.modal = null;
  }

  /** The galaxy is on hold until the player picks how to fight. */
  promptBattle(b: GalaxyBattle): void {
    const g = this.scene.galaxy;
    const player = this.scene.opts.player;
    const enemy = b.attacker === player ? b.defender : b.attacker;
    const planet = g.planets[b.planetId];
    const def = defenseLeft(b.world, enemy);
    const theirs = [describeShips(survivors(b.world, enemy))];
    if (def.station > 0) theirs.unshift(`station niveau ${def.station}`);
    if (def.platforms > 0) theirs.unshift(`${def.platforms} plateforme${def.platforms > 1 ? 's' : ''}`);
    const p = el('div', { class: 'panel' },
      `<h2>Bataille de ${planet.name}</h2>` +
      `<p>${b.attacker === player ? 'Tu attaques' : 'Tu es attaqué par'} ${FACTIONS[enemy].name}.</p>` +
      '<dl>' +
      `<dt>Tes forces</dt><dd>${describeShips(survivors(b.world, player))}</dd>` +
      `<dt>En face</dt><dd>${theirs.join(', ')}</dd>` +
      `<dt>Coque et boucliers</dt><dd>${sideStrength(b.world, player)} contre ${sideStrength(b.world, enemy)}</dd>` +
      '</dl>' +
      '<div class="actions"><button class="primary" data-act="fight">Commander la bataille</button><button data-act="auto">Résolution automatique</button></div>');
    p.querySelector<HTMLButtonElement>('[data-act=fight]')!.onclick = () => {
      this.closeModal();
      this.scene.fight(b.id);
    };
    p.querySelector<HTMLButtonElement>('[data-act=auto]')!.onclick = () => {
      this.closeModal();
      this.scene.autoResolve(b.id);
    };
    this.openModal(p);
  }

  showEnd(won: boolean): void {
    const p = el('div', { class: 'panel' },
      `<h2>${won ? 'Victoire' : 'Défaite'}</h2>` +
      `<p>${won ? "L'adversaire n'a plus aucune planète." : "Tu n'as plus aucune planète."}</p>` +
      '<div class="actions"><button class="primary">Menu</button></div>');
    p.querySelector('button')!.onclick = () => this.scene.toMenu();
    this.openModal(p);
  }

  private showHelp(): void {
    const rows: [string, string][] = [
      ['Clic', 'Sélectionner une planète'],
      ['Clic droit', 'Envoyer la flotte de la planète sélectionnée (les vaisseaux choisis dans le panneau)'],
      ['Espace', 'Pause'],
      ['Échap', 'Désélectionner'],
    ];
    const p = el('div', { class: 'panel' },
      '<h2>Carte galactique</h2><dl>' + rows.map(([k, v]) => `<dt>${k}</dt><dd>${v}</dd>`).join('') + '</dl>' +
      `<p class="muted">Une flotte seule prend une planète sans défense en ${GALAXY_RULES.captureTime} s (${GALAXY_RULES.neutralCaptureTime} s si elle est neutre). ` +
      'Quand deux camps se rencontrent, la bataille se commande dans la vue tactique ou se résout seule. ' +
      "Les flottes traversent les planètes neutres ou alliées et s'arrêtent aux planètes ennemies. Tu perds quand tu n'as plus de planète.</p>" +
      '<div class="actions"><button class="primary">Fermer</button></div>');
    p.querySelector('button')!.onclick = () => this.closeModal();
    this.openModal(p);
  }
}
