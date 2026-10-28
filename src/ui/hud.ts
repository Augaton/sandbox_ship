// HTML overlay above the game canvas. It reads the simulation and calls its commands;
// it never changes simulation state on its own.
import './hud.css';
import { FACTIONS, FACTION_IDS, SHIPS, SHIP_CLASSES, type FactionId, type ShipClass } from '../data/units';
import { BUILDABLES, BUILDABLE_TYPES, HARDPOINTS, STATION, type BuildableType } from '../data/structures';
import type { SpawnWhat } from '../input/selection';
import type { BattleScene } from '../scenes/BattleScene';
import { canBuildShip, canUpgrade, cancelQueued, enqueueShip, enqueueUpgrade } from '../sim/structures';
import { childrenOf, newAI, stationOf, type Entity, type Result } from '../sim/world';
import { el, hex, pct, shortReason, toast } from './dom';

const REFRESH_MS = 100;
const SHIPYARD_CLASSES = SHIP_CLASSES.filter((c) => SHIPS[c].source === 'shipyard');
const SPEEDS = [0.5, 1, 2, 4];

export function entityName(e: Entity): string {
  if (e.kind === 'ship') return SHIPS[e.type as ShipClass].name;
  if (e.kind === 'hardpoint') return HARDPOINTS[e.type as keyof typeof HARDPOINTS].name;
  return e.type === 'station' ? STATION.name : BUILDABLES[e.type as BuildableType].name;
}

export class Hud {
  private root = el('div', { class: 'ui' });
  private credits!: HTMLElement;
  private level!: HTMLElement;
  private selection!: HTMLElement;
  private queue: HTMLElement | null = null;
  private minimap!: HTMLCanvasElement;
  private toasts!: HTMLElement;
  private sandboxPanel: HTMLElement | null = null;
  private shipButtons = new Map<ShipClass, HTMLButtonElement>();
  private structureButtons = new Map<BuildableType, HTMLButtonElement>();
  private upgradeButton: HTMLButtonElement | null = null;
  private modal: HTMLElement | null = null;
  private timer = 0;
  private lastSelection = '';
  /** Signature of the rendered queue; null forces the first render. */
  private lastQueue: string | null = null;

  /**
   * `campaign` is the planet fought over in a campaign battle: no building, no sandbox,
   * and the attacker may retreat.
   */
  constructor(private scene: BattleScene, private mode: { sandbox: boolean; campaign: string | null }) {
    this.buildTopbar();
    this.buildBottombar();
    if (!mode.campaign) this.buildSandbox(mode.sandbox);
    this.toasts = this.root.appendChild(el('div', { class: 'toasts', role: 'status', 'aria-live': 'polite' }));
    document.body.appendChild(this.root);
    // A clicked button keeps focus, and Space or Enter would press it again: give focus back.
    this.root.addEventListener('click', (ev) => {
      if (ev.target instanceof HTMLButtonElement && ev.detail > 0) ev.target.blur();
    });
    this.refreshModes();
    this.refresh();
  }

  destroy(): void {
    this.root.remove();
  }

  private get world() {
    return this.scene.world;
  }

  private get player(): FactionId {
    return this.scene.opts.player;
  }

  private buildTopbar(): void {
    const f = FACTIONS[this.player];
    const bar = this.root.appendChild(el('div', { class: 'topbar panel' }));
    bar.append(el('span', { class: 'faction' }, `<span class="swatch" style="background:${hex(f.color)}"></span>${f.name}`));
    this.credits = bar.appendChild(el('span', { class: 'credits' }));
    this.level = bar.appendChild(el('span', { class: 'muted' }));
    bar.append(el('span', { class: 'spacer' }));
    const speeds = bar.appendChild(el('div', { class: 'group', role: 'group', 'aria-label': 'Vitesse du jeu' }));
    const pause = speeds.appendChild(el('button', { 'data-speed': '0', title: 'Pause (Espace)' }, 'Pause'));
    pause.onclick = () => this.scene.togglePause();
    for (const s of SPEEDS) {
      const b = speeds.appendChild(el('button', { 'data-speed': String(s), 'aria-label': `Vitesse ×${s}` }, `×${s}`));
      b.onclick = () => this.scene.setSpeed(s);
    }
    const group = bar.appendChild(el('div', { class: 'group' }));
    if (this.mode.campaign) {
      this.credits.textContent = `Bataille de ${this.mode.campaign}`;
      group.appendChild(el('button', {}, 'Aide')).onclick = () => this.showHelp();
      if (this.scene.opts.campaign?.attacker === this.player) {
        group.appendChild(el('button', { title: 'Les survivants regagnent la planète d\'où ils venaient' }, 'Retraite')).onclick = () => this.scene.retreat();
      }
      return;
    }
    const sb = group.appendChild(el('button', { 'data-act': 'sandbox' }, 'Bac à sable'));
    sb.onclick = () => {
      this.sandboxPanel!.hidden = !this.sandboxPanel!.hidden;
      this.refreshModes();
    };
    group.appendChild(el('button', {}, 'Aide')).onclick = () => this.showHelp();
    group.appendChild(el('button', {}, 'Menu')).onclick = () => this.scene.toMenu();
  }

  private buildBottombar(): void {
    const bar = this.root.appendChild(el('div', { class: 'bottombar' }));
    this.selection = bar.appendChild(el('section', { class: 'selection panel', 'aria-label': 'Sélection' }));
    this.selection.addEventListener('click', (ev) => {
      const type = (ev.target as HTMLElement).closest('button')?.dataset.type;
      if (!type) return;
      for (const id of [...this.scene.selected]) {
        if (this.world.entities.get(id)?.type !== type) this.scene.selected.delete(id);
      }
    });

    if (!this.mode.campaign) this.buildProduction(bar);

    const mini = bar.appendChild(el('section', { class: 'minimap panel', 'aria-label': 'Minicarte' }));
    this.minimap = mini.appendChild(el('canvas', { width: '240', height: '160' }));
    const jump = (ev: PointerEvent) => {
      const r = this.minimap.getBoundingClientRect();
      const x = ((ev.clientX - r.left) / r.width) * this.world.width;
      const y = ((ev.clientY - r.top) / r.height) * this.world.height;
      this.scene.cameras.main.centerOn(x, y);
    };
    this.minimap.addEventListener('pointerdown', (ev) => {
      this.minimap.setPointerCapture(ev.pointerId);
      jump(ev);
    });
    this.minimap.addEventListener('pointermove', (ev) => {
      if (ev.buttons & 1) jump(ev);
    });
  }

  private buildProduction(bar: HTMLElement): void {
    const build = bar.appendChild(el('section', { class: 'build panel', 'aria-label': 'Construction' }));
    const head = build.appendChild(el('div', { class: 'head' }));
    head.append(el('h3', {}, 'Chantier naval'));
    this.upgradeButton = head.appendChild(el('button'));
    this.upgradeButton.onclick = () => this.report(enqueueUpgrade(this.world, this.player));
    const ships = build.appendChild(el('div', { class: 'row' }));
    for (const cls of SHIPYARD_CLASSES) {
      const b = ships.appendChild(el('button', {}, `<span>${SHIPS[cls].name}</span><small></small>`));
      b.onclick = () => this.report(enqueueShip(this.world, this.player, cls));
      this.shipButtons.set(cls, b);
    }
    const structures = build.appendChild(el('div', { class: 'row', 'aria-label': 'Structures' }));
    for (const type of BUILDABLE_TYPES) {
      const b = structures.appendChild(el('button', {}, `<span>${BUILDABLES[type].name}</span><small></small>`));
      b.onclick = () => this.scene.controls.setMode({ kind: 'place', type });
      this.structureButtons.set(type, b);
    }
    this.queue = build.appendChild(el('div', { class: 'queue', 'aria-label': "File d'attente" }));
    this.queue.addEventListener('click', (ev) => {
      const i = (ev.target as HTMLElement).closest('button')?.dataset.index;
      if (i != null) cancelQueued(this.world, this.player, Number(i));
    });
  }

  private buildSandbox(open: boolean): void {
    const p = this.root.appendChild(el('section', { class: 'sandbox panel', 'aria-label': 'Bac à sable' }));
    p.hidden = !open;
    this.sandboxPanel = p;
    p.append(el('h3', {}, 'Bac à sable'));
    const factions = FACTION_IDS.map((id) => `<option value="${id}">${FACTIONS[id].name}</option>`).join('');
    const ships = SHIP_CLASSES.map((c) => `<option value="${c}">${SHIPS[c].name}</option>`).join('');
    const structures = BUILDABLE_TYPES.map((t) => `<option value="${t}">${BUILDABLES[t].name}</option>`).join('');
    p.append(el('label', {}, `Faction<select name="faction">${factions}</select>`));
    p.append(el('label', {}, `Élément<select name="what"><optgroup label="Vaisseaux">${ships}</optgroup>` +
      `<optgroup label="Structures">${structures}<option value="station">${STATION.name}</option></optgroup></select>`));
    const faction = p.querySelector<HTMLSelectElement>('[name=faction]')!;
    const what = p.querySelector<HTMLSelectElement>('[name=what]')!;
    faction.value = this.scene.opts.enemy;
    what.value = 'frigate';
    const spawn = p.appendChild(el('button', { 'data-act': 'spawn' }, 'Placer au clic'));
    const pick = () => ({ kind: 'spawn' as const, faction: faction.value as FactionId, what: what.value as SpawnWhat });
    spawn.onclick = () => {
      const on = this.scene.controls.mode.kind === 'spawn';
      this.scene.controls.setMode(on ? { kind: 'select' } : pick());
    };
    const sync = () => {
      if (this.scene.controls.mode.kind === 'spawn') this.scene.controls.setMode(pick());
    };
    faction.onchange = sync;
    what.onchange = sync;

    const ai = p.appendChild(el('label', { class: 'check' }, '<input type="checkbox" name="ai"> IA ennemie active'));
    const box = ai.querySelector('input')!;
    box.checked = !open;
    box.onchange = () => {
      const enemy = this.world.factions.get(this.scene.opts.enemy);
      if (enemy) enemy.ai = box.checked ? newAI() : null;
    };
    p.appendChild(el('button', {}, '+5 000 crédits')).onclick = () => {
      this.world.factions.get(this.player)!.credits += 5000;
    };
    p.appendChild(el('button', {}, 'Détruire la sélection')).onclick = () => {
      for (const id of this.scene.selected) {
        const e = this.world.entities.get(id);
        if (e) e.hull = 0;
      }
    };
  }

  /** Shows the outcome of a command when it was refused. */
  private report(r: Result): void {
    if (!r.ok) this.toast(r.reason);
  }

  toast(text: string): void {
    toast(this.toasts, text);
  }

  /** The player lost a structure or hardpoint. */
  lost(type: string): void {
    const name =
      type === 'station' ? STATION.name
      : type in HARDPOINTS ? HARDPOINTS[type as keyof typeof HARDPOINTS].name
      : BUILDABLES[type as BuildableType]?.name ?? type;
    this.toast(`${name} détruit`);
  }

  /** Reflects speed, pause, sandbox and mode states in the buttons. */
  refreshModes(): void {
    for (const b of this.root.querySelectorAll<HTMLButtonElement>('[data-speed]')) {
      const s = Number(b.dataset.speed);
      b.setAttribute('aria-pressed', String(s === 0 ? this.scene.paused : !this.scene.paused && this.scene.speed === s));
    }
    this.root.querySelector('[data-act=sandbox]')?.setAttribute('aria-pressed', String(this.sandboxPanel?.hidden === false));
    const mode = this.scene.controls?.mode.kind ?? 'select';
    this.root.querySelector('[data-act=spawn]')?.setAttribute('aria-pressed', String(mode === 'spawn'));
    for (const [type, b] of this.structureButtons) {
      const m = this.scene.controls?.mode;
      b.setAttribute('aria-pressed', String(m?.kind === 'place' && m.type === type));
    }
  }

  update(delta: number): void {
    this.timer -= delta;
    if (this.timer > 0) return;
    this.timer = REFRESH_MS;
    this.refresh();
  }

  private refresh(): void {
    const w = this.world;
    const f = w.factions.get(this.player)!;
    const station = stationOf(w, this.player);
    if (this.mode.campaign) {
      this.level.textContent = station ? `Station niveau ${station.level}` : '';
      this.refreshSelection();
      this.drawMinimap();
      return;
    }
    let income = station ? STATION.income : 0;
    for (const e of w.entities.values()) if (e.faction === this.player && e.build >= 1) income += e.income;
    this.credits.innerHTML = `Crédits <strong>${Math.floor(f.credits)}</strong> <span class="muted">+${income}/s</span>`;
    this.level.textContent = station ? `Station niveau ${station.level}` : 'Station détruite';

    for (const [cls, b] of this.shipButtons) {
      const def = SHIPS[cls];
      this.setButton(b, canBuildShip(w, this.player, cls), `${def.cost} cr, ${def.buildTime} s`);
    }
    for (const [type, b] of this.structureButtons) {
      const def = BUILDABLES[type];
      const r: Result =
        !station ? { ok: false, reason: 'Aucune station' }
        : station.level < def.minStationLevel ? { ok: false, reason: `Station niveau ${def.minStationLevel} requise` }
        : f.credits < def.cost ? { ok: false, reason: 'Crédits insuffisants' }
        : { ok: true };
      this.setButton(b, r, `${def.cost} cr, ${def.buildTime} s`);
    }
    const next = station && STATION.levels[station.level];
    const up = canUpgrade(w, this.player);
    this.upgradeButton!.disabled = !up.ok;
    this.upgradeButton!.textContent = next
      ? `Améliorer la station (${next.upgradeCost} cr)${up.ok ? '' : ` : ${up.reason}`}`
      : 'Station au niveau maximum';

    this.refreshQueue();
    this.refreshSelection();
    this.drawMinimap();
  }

  private setButton(b: HTMLButtonElement, r: Result, detail: string): void {
    b.disabled = !r.ok;
    b.querySelector('small')!.textContent = r.ok ? detail : shortReason(r.reason);
    b.title = r.ok ? detail : r.reason;
  }

  private refreshQueue(): void {
    if (!this.queue) return;
    const q = this.world.factions.get(this.player)!.queue;
    const sig = q.map((i) => (i.kind === 'ship' ? i.cls : 'upgrade')).join(',');
    if (sig !== this.lastQueue) {
      this.lastQueue = sig;
      this.queue.innerHTML = q.length === 0
        ? '<span class="muted">File vide : choisis un vaisseau ci-dessus.</span>'
        : '<span class="muted">File (clic : annuler)</span>' + q.map((item, i) => {
          const name = item.kind === 'ship' ? SHIPS[item.cls].name : 'Amélioration';
          return `<button data-index="${i}" aria-label="Annuler ${name}">${name}<span class="progress"></span></button>`;
        }).join('');
    }
    const bar = this.queue.querySelector<HTMLElement>('.progress');
    if (bar && q[0]) bar.style.width = pct(1 - q[0].remaining / q[0].total);
  }

  private refreshSelection(): void {
    const ents = [...this.scene.selected].map((id) => this.world.entities.get(id)).filter((e): e is Entity => !!e);
    for (const id of this.scene.selected) if (!this.world.entities.has(id)) this.scene.selected.delete(id);
    let html: string;
    if (ents.length === 0) {
      html = '<div class="title">Aucune sélection</div><div class="muted">Clic gauche ou rectangle : sélectionner.<br>Clic droit : déplacer ou attaquer.<br>Aide : toutes les commandes.</div>';
    } else if (ents.length === 1) {
      html = this.describe(ents[0]);
    } else {
      const counts = new Map<string, { n: number; e: Entity }>();
      for (const e of ents) {
        const c = counts.get(e.type);
        if (c) c.n++;
        else counts.set(e.type, { n: 1, e });
      }
      html = `<div class="title">${ents.length} unités</div><div class="chips">` +
        [...counts].map(([type, c]) => `<button data-type="${type}" aria-label="Garder seulement : ${entityName(c.e)}">${c.n}× ${entityName(c.e)}</button>`).join('') +
        '</div>';
    }
    if (html !== this.lastSelection) {
      this.lastSelection = html;
      this.selection.innerHTML = html;
    }
  }

  private describe(e: Entity): string {
    const own = e.faction === this.player;
    const lines: string[] = [];
    lines.push(`<div class="title">${entityName(e)}</div>`);
    lines.push(`<div class="muted">${FACTIONS[e.faction].name}${own ? '' : ' (ennemi)'}</div>`);
    lines.push(`Coque ${Math.ceil(e.hull)} / ${e.maxHull}<div class="bar"><i style="width:${pct(e.hull / e.maxHull)};background:var(--ok)"></i></div>`);
    if (e.maxShield > 0) {
      lines.push(`Bouclier ${Math.floor(e.shield)} / ${e.maxShield}<div class="bar"><i style="width:${pct(e.shield / e.maxShield)};background:var(--shield)"></i></div>`);
    }
    if (e.build < 1) lines.push(`En construction : ${pct(e.build)}`);
    if (e.type === 'station') {
      lines.push(`Niveau ${e.level}, ${childrenOf(this.world, e.id).length} points d'attache`);
      if (own) lines.push('<span class="muted">Clic droit : point de ralliement</span>');
    }
    if (e.hangar) lines.push(`Appareils lancés : ${e.hangar.launched.length} / ${e.hangar.def.cap}`);
    if (e.income > 0) lines.push(`Revenu : +${e.income} cr/s`);
    if (e.kind === 'ship' && own) {
      lines.push(`<span class="muted">${e.order ? { move: 'En route', attackMove: 'Attaque-mouvement', attack: 'Attaque' }[e.order.type] : 'En position'}</span>`);
    }
    return lines.join('');
  }

  private drawMinimap(): void {
    const c = this.minimap;
    const ctx = c.getContext('2d')!;
    const w = this.world;
    const sx = c.width / w.width;
    const sy = c.height / w.height;
    ctx.fillStyle = '#0b0d12';
    ctx.fillRect(0, 0, c.width, c.height);
    ctx.fillStyle = '#2a3446';
    for (const f of w.fields) {
      ctx.beginPath();
      ctx.arc(f.x * sx, f.y * sy, Math.max(2, f.radius * sx), 0, Math.PI * 2);
      ctx.fill();
    }
    for (const e of w.entities.values()) {
      if (e.kind === 'hardpoint') continue;
      ctx.fillStyle = hex(FACTIONS[e.faction].color);
      const s = e.kind === 'ship' ? (e.size === 'small' ? 1.5 : 2.5) : e.type === 'station' ? 7 : 4;
      ctx.fillRect(e.x * sx - s / 2, e.y * sy - s / 2, s, s);
    }
    const v = this.scene.cameras.main.worldView;
    ctx.strokeStyle = '#d9dee7';
    ctx.lineWidth = 1;
    ctx.strokeRect(v.x * sx, v.y * sy, v.width * sx, v.height * sy);
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

  showEnd(won: boolean): void {
    const p = el('div', { class: 'panel' },
      `<h2>${won ? 'Victoire' : 'Défaite'}</h2>` +
      `<p>${won ? 'La station ennemie est détruite.' : 'Ta station est détruite.'}</p>` +
      '<div class="actions"><button class="primary" data-act="again">Rejouer</button><button data-act="menu">Menu</button><button data-act="close">Continuer</button></div>');
    p.querySelector<HTMLButtonElement>('[data-act=again]')!.onclick = () => this.scene.restart();
    p.querySelector<HTMLButtonElement>('[data-act=menu]')!.onclick = () => this.scene.toMenu();
    p.querySelector<HTMLButtonElement>('[data-act=close]')!.onclick = () => this.closeModal();
    this.openModal(p);
  }

  /** End of a campaign battle: the only way on is back to the galaxy map. */
  showBattleEnd(won: boolean, onBack: () => void): void {
    const p = el('div', { class: 'panel' },
      `<h2>${won ? 'Victoire' : 'Défaite'}</h2>` +
      `<p>${won ? 'La bataille est gagnée.' : 'La bataille est perdue.'} Les survivants regagnent la carte galactique.</p>` +
      '<div class="actions"><button class="primary">Retour à la carte</button></div>');
    p.querySelector('button')!.onclick = onBack;
    this.openModal(p);
  }

  private showHelp(): void {
    const keys: [string, string][] = [
      ['Clic gauche', 'Sélectionner (glisser : rectangle, Maj : ajouter)'],
      ['Clic droit', 'Déplacer, ou attaquer la cible visée'],
      ['A puis clic', 'Attaque-mouvement : engage tout ce qui croise la route'],
      ['S', 'Stop'],
      ['Ctrl ou Maj + 1 à 9', 'Créer un groupe'],
      ['1 à 9', 'Rappeler un groupe (deux fois : centrer)'],
      ['H', 'Centrer sur la station'],
      ['Flèches, bords, clic molette', 'Déplacer la vue'],
      ['Molette', 'Zoom'],
      ['Espace', 'Pause'],
      ['Échap', 'Annuler le mode en cours'],
    ];
    const p = el('div', { class: 'panel' },
      '<h2>Commandes</h2><dl>' + keys.map(([k, v]) => `<dt>${k}</dt><dd>${v}</dd>`).join('') + '</dl>' +
      '<p class="muted">Les chasseurs et bombardiers sortent seuls des hangars. Détruire les points d\'attache d\'une station la désarme : chantier naval, hangar, générateur de bouclier, tourelles.</p>' +
      '<div class="actions"><button class="primary">Fermer</button></div>');
    p.querySelector('button')!.onclick = () => this.closeModal();
    this.openModal(p);
  }
}
