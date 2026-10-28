// HTML overlay above the game canvas. It reads the simulation and calls its commands;
// it never changes simulation state on its own.
import './hud.css';
import { ABILITIES, type AbilityId } from '../data/abilities';
import { HEROES, heroesOf, type HeroId } from '../data/heroes';
import { MAX_TECH_LEVEL, TECHS, TECH_IDS, type TechId } from '../data/research';
import { FACTIONS, FACTION_IDS, SHIPS, SHIP_CLASSES, orderName, shipCost, type FactionId, type ShipClass } from '../data/units';
import { BUILDABLES, BUILDABLE_TYPES, HARDPOINTS, STATION, type BuildableType } from '../data/structures';
import type { SpawnWhat } from '../input/selection';
import type { BattleScene } from '../scenes/BattleScene';
import {
  canBuildHero, canBuildShip, canResearch, canUpgrade, canUpgradeStructure, cancelConstruction, cancelQueued, cancelResearch,
  enqueueHero, enqueueShip, enqueueUpgrade, startResearch, upgradeStructure,
} from '../sim/structures';
import { childrenOf, newAI, stationOf, type Entity, type QueueItem, type Result } from '../sim/world';
import { isVisible, wasSeen } from '../sim/vision';
import { BATTLE_CONTROLS, controlsList } from './controls';
import { el, hex, pct, shortReason, toast } from './dom';
import { emblemUrl } from './logos';
import { optionsPanel } from './options';
import { saveSettings, settings } from './settings';

type Tab = 'ships' | 'buildings' | 'research';

const REFRESH_MS = 100;
/** How long an alert pings on the minimap. */
const PING_MS = 4000;

/**
 * Queue buttons cancel on a right click, or on Enter or Space: a stray left click does not
 * throw away an order.
 */
function onCancel(list: HTMLElement, cancel: (b: HTMLElement) => void): void {
  list.addEventListener('contextmenu', (ev) => {
    const b = (ev.target as HTMLElement).closest('button');
    if (!b) return;
    ev.preventDefault();
    cancel(b);
  });
  list.addEventListener('click', (ev) => {
    const b = (ev.target as HTMLElement).closest('button');
    if (b && ev.detail === 0) cancel(b);
  });
}
const SHIPYARD_CLASSES = SHIP_CLASSES.filter((c) => SHIPS[c].source === 'shipyard');
const SPEEDS = [0.5, 1, 2, 4];

/** Name of a ship class, structure or part from its type. */
export function typeName(type: string): string {
  if (type === 'station') return STATION.name;
  if (type in HARDPOINTS) return HARDPOINTS[type as keyof typeof HARDPOINTS].name;
  if (type in SHIPS) return SHIPS[type as ShipClass].name;
  return BUILDABLES[type as BuildableType]?.name ?? type;
}

/** "12 min 05 s" */
function duration(seconds: number): string {
  const m = Math.floor(seconds / 60);
  const s = Math.floor(seconds % 60);
  return m > 0 ? `${m} min ${String(s).padStart(2, '0')} s` : `${s} s`;
}

export function entityName(e: Entity): string {
  if (e.hero) return HEROES[e.hero].name;
  if (e.kind === 'ship') return SHIPS[e.type as ShipClass].name;
  if (e.kind === 'hardpoint') return HARDPOINTS[e.type as keyof typeof HARDPOINTS].name;
  return e.type === 'station' ? STATION.name : BUILDABLES[e.type as BuildableType].name;
}

export class Hud {
  private root = el('div', { class: 'ui' });
  private credits!: HTMLElement;
  private level!: HTMLElement;
  private selection!: HTMLElement;
  private unitsQueue: HTMLElement | null = null;
  private buildingsQueue: HTMLElement | null = null;
  private researchStatus: HTMLElement | null = null;
  private tabs = new Map<Tab, { button: HTMLButtonElement; body: HTMLElement }>();
  private techButtons = new Map<TechId, HTMLButtonElement>();
  private heroButtons = new Map<HeroId, HTMLButtonElement>();
  /** Last rendered signature of each list. */
  private lists = new Map<HTMLElement, string>();
  private minimap!: HTMLCanvasElement;
  private toasts!: HTMLElement;
  private sandboxPanel: HTMLElement | null = null;
  private shipButtons = new Map<ShipClass, HTMLButtonElement>();
  private structureButtons = new Map<BuildableType, HTMLButtonElement>();
  private upgradeButton: HTMLButtonElement | null = null;
  private modal: HTMLElement | null = null;
  /** Run when the open dialog closes: resume the game, or go back to the pause menu. */
  private onModalClose: (() => void) | null = null;
  /** The pause menu paused the game, and resumes it when it closes. */
  private menuPaused = false;
  private pilotBanner!: HTMLElement;
  /** Control groups, idle ships, last alert. */
  private quickbar!: HTMLElement;
  private tip!: HTMLElement;
  private timer = 0;
  private lastSelection = '';

  /**
   * `campaign` is the planet fought over in a campaign battle: no building, no sandbox,
   * and the attacker may retreat.
   */
  constructor(private scene: BattleScene, private mode: { sandbox: boolean; campaign: string | null }) {
    this.buildTopbar();
    this.buildBottombar();
    if (!mode.campaign) this.buildSandbox(mode.sandbox);
    this.toasts = this.root.appendChild(el('div', { class: 'toasts', role: 'status', 'aria-live': 'polite' }));
    this.pilotBanner = this.root.appendChild(el('div', { class: 'pilot-banner panel', role: 'status' }));
    this.pilotBanner.hidden = true;
    this.tip = this.root.appendChild(el('div', { class: 'tooltip panel', role: 'tooltip' }));
    this.tip.hidden = true;
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
    bar.append(el('span', { class: 'faction', title: `${f.trait.name} : ${f.trait.description}` },
      `<img class="emblem" src="${emblemUrl(this.player, 2)}" alt="">${f.name}`));
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
      group.appendChild(el('button', {}, 'Options')).onclick = () => this.showOptions();
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
    group.appendChild(el('button', {}, 'Options')).onclick = () => this.showOptions();
    group.appendChild(el('button', {}, 'Menu')).onclick = () =>
      this.confirm('Quitter la partie ?', 'La bataille en cours sera perdue.', 'Quitter', () => this.scene.toMenu());
  }

  private buildBottombar(): void {
    const bar = this.root.appendChild(el('div', { class: 'bottombar' }));
    const left = bar.appendChild(el('div', { class: 'left' }));
    this.quickbar = left.appendChild(el('div', { class: 'quickbar', role: 'group', 'aria-label': 'Groupes et raccourcis' }));
    this.quickbar.addEventListener('click', (ev) => {
      const b = (ev.target as HTMLElement).closest('button');
      if (!b) return;
      if (b.dataset.group) this.scene.controls.recallGroup(Number(b.dataset.group));
      if (b.dataset.act === 'idle') this.scene.controls.selectIdle(ev.shiftKey);
      if (b.dataset.act === 'alert') this.scene.jumpToAlert();
    });
    this.selection = left.appendChild(el('section', { class: 'selection panel', 'aria-label': 'Sélection' }));
    // Ability buttons react on press: the panel redraws every second with their cooldown.
    this.selection.addEventListener('pointerdown', (ev) => {
      const id = (ev.target as HTMLElement).closest('button')?.dataset.ability;
      if (id) this.scene.controls.useAbilities(id as AbilityId);
    });
    this.selection.addEventListener('change', (ev) => {
      const input = ev.target as HTMLInputElement;
      if (input.dataset.act !== 'auto-abilities') return;
      settings.autoAbilities = input.checked;
      saveSettings();
    });
    this.selection.addEventListener('click', (ev) => {
      const b = (ev.target as HTMLElement).closest('button');
      if (b?.dataset.ability && ev.detail === 0) return this.scene.controls.useAbilities(b.dataset.ability as AbilityId);
      if (b?.dataset.act === 'pilot') return this.scene.controls.togglePilot();
      if (b?.dataset.tier) return this.report(upgradeStructure(this.world, this.player, Number(b.dataset.tier)));
      const type = b?.dataset.type;
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
      // Right button: send the selection there (Ctrl: attack-move); left: look there.
      if (ev.button === 2) {
        const r = this.minimap.getBoundingClientRect();
        const x = ((ev.clientX - r.left) / r.width) * this.world.width;
        const y = ((ev.clientY - r.top) / r.height) * this.world.height;
        this.scene.controls.moveSelection(x, y, ev.ctrlKey);
        return;
      }
      this.minimap.setPointerCapture(ev.pointerId);
      jump(ev);
    });
    this.minimap.addEventListener('contextmenu', (ev) => ev.preventDefault());
    this.minimap.addEventListener('pointermove', (ev) => {
      if (ev.buttons & 1) jump(ev);
    });
  }

  private buildProduction(bar: HTMLElement): void {
    const build = bar.appendChild(el('section', { class: 'build panel', 'aria-label': 'Production' }));
    const tabs = build.appendChild(el('div', { class: 'tabs', role: 'tablist' }));
    const addTab = (id: Tab, label: string): HTMLElement => {
      const button = tabs.appendChild(el('button', { role: 'tab', 'data-tab': id }, label));
      const body = build.appendChild(el('div', { class: 'tab-body', role: 'tabpanel', 'aria-label': label }));
      button.onclick = () => this.showTab(id);
      this.tabs.set(id, { button, body });
      return body;
    };

    const ships = addTab('ships', 'Vaisseaux');
    const shipRow = ships.appendChild(el('div', { class: 'row' }));
    for (const cls of SHIPYARD_CLASSES) {
      const b = shipRow.appendChild(el('button', {}, `<span>${orderName(cls)}</span><small></small>`));
      // Shift: five at once, as many as the queue and the credits allow.
      b.onclick = (ev) => {
        const first = enqueueShip(this.world, this.player, cls);
        this.report(first);
        for (let i = 1; first.ok && ev.shiftKey && i < 5; i++) if (!enqueueShip(this.world, this.player, cls).ok) break;
      };
      // Right click: keep this class in production.
      b.addEventListener('contextmenu', (ev) => {
        ev.preventDefault();
        const f = this.world.factions.get(this.player)!;
        const on = f.repeat.includes(cls);
        f.repeat = on ? f.repeat.filter((c) => c !== cls) : [...f.repeat, cls];
        this.toast(`${SHIPS[cls].name} : production en boucle ${on ? 'arrêtée' : 'lancée'}`);
        this.refresh();
      });
      this.shipButtons.set(cls, b);
    }
    const heroRow = ships.appendChild(el('div', { class: 'row heroes', 'aria-label': 'Héros' }));
    for (const h of heroesOf(this.player)) {
      const power = ABILITIES[h.ability];
      const b = heroRow.appendChild(el('button', { title: `${h.description} ${power.name} : ${power.description}` }, `<span>${h.name}, ${h.title.toLowerCase()}</span><small></small>`));
      b.onclick = () => this.report(enqueueHero(this.world, this.player, h.id));
      this.heroButtons.set(h.id, b);
    }
    this.unitsQueue = ships.appendChild(el('div', { class: 'queue', 'aria-label': 'File des vaisseaux' }));
    onCancel(this.unitsQueue, (b) => {
      if (b.dataset.index != null) cancelQueued(this.world, this.player, Number(b.dataset.index), 'units');
    });

    const buildings = addTab('buildings', 'Bâtiments');
    this.upgradeButton = buildings.appendChild(el('button', { class: 'wide' }));
    this.upgradeButton.onclick = () => this.report(enqueueUpgrade(this.world, this.player));
    const structureRow = buildings.appendChild(el('div', { class: 'row' }));
    for (const type of BUILDABLE_TYPES) {
      const b = structureRow.appendChild(el('button', {}, `<span>${BUILDABLES[type].name}</span><small></small>`));
      b.onclick = () => this.scene.controls.setMode({ kind: 'place', type });
      this.structureButtons.set(type, b);
    }
    this.buildingsQueue = buildings.appendChild(el('div', { class: 'queue', 'aria-label': 'File des bâtiments' }));
    onCancel(this.buildingsQueue, (b) => {
      if (b.dataset.index != null) cancelQueued(this.world, this.player, Number(b.dataset.index), 'buildings');
      if (b.dataset.site != null) cancelConstruction(this.world, this.player, Number(b.dataset.site));
    });

    const research = addTab('research', 'Recherche');
    const techRow = research.appendChild(el('div', { class: 'row' }));
    for (const tech of TECH_IDS) {
      const b = techRow.appendChild(el('button', {}, '<span></span><small></small>'));
      b.onclick = () => this.report(startResearch(this.world, this.player, tech));
      this.techButtons.set(tech, b);
    }
    this.researchStatus = research.appendChild(el('div', { class: 'queue', 'aria-label': 'Recherche en cours' }));
    this.researchStatus.addEventListener('click', (ev) => {
      if ((ev.target as HTMLElement).closest('button')?.dataset.act === 'cancel-research') cancelResearch(this.world, this.player);
    });
    this.showTab('ships');
  }

  private showTab(id: Tab): void {
    for (const [key, t] of this.tabs) {
      t.button.setAttribute('aria-selected', String(key === id));
      t.body.hidden = key !== id;
    }
  }

  /** Re-renders a queue only when its items change, so that its buttons stay clickable. */
  private renderList(target: HTMLElement, items: { label: string; attr: string; progress: number }[], empty: string): void {
    const sig = items.map((i) => i.attr + i.label).join('|');
    if (this.lists.get(target) !== sig) {
      this.lists.set(target, sig);
      target.innerHTML = items.length === 0
        ? `<span class="muted">${empty}</span>`
        : '<span class="muted">File (clic droit : annuler)</span>' + items.map((i) =>
          `<button ${i.attr} aria-label="Annuler ${i.label}">${i.label}<span class="progress"></span></button>`).join('');
    }
    target.querySelectorAll<HTMLElement>('.progress').forEach((bar, k) => (bar.style.width = pct(items[k]?.progress ?? 0)));
  }

  private refreshProduction(): void {
    const w = this.world;
    const f = w.factions.get(this.player)!;
    const station = stationOf(w, this.player);
    for (const [cls, b] of this.shipButtons) {
      const loop = f.repeat.includes(cls);
      this.setButton(b, canBuildShip(w, this.player, cls), `${shipCost(cls, this.player)} cr, ${SHIPS[cls].buildTime} s${loop ? ', en boucle' : ''}`);
      b.classList.toggle('loop', loop);
      b.title += '. Maj + clic : 5 d\'un coup. Clic droit : production en boucle.';
    }
    for (const [type, b] of this.structureButtons) {
      const def = BUILDABLES[type];
      // Without a station (an invasion force), what a level 1 station would allow.
      const r: Result =
        (station?.level ?? 1) < def.minStationLevel ? { ok: false, reason: `Station niveau ${def.minStationLevel} requise` }
        : f.credits < def.cost ? { ok: false, reason: 'Crédits insuffisants' }
        : { ok: true };
      this.setButton(b, r, `${def.cost} cr, ${def.buildTime} s`);
    }
    const next = station && STATION.levels[station.level];
    const up = canUpgrade(w, this.player);
    this.upgradeButton!.disabled = !up.ok;
    this.upgradeButton!.textContent = next
      ? `Améliorer la station au niveau ${station.level + 1} (${next.upgradeCost} cr)${up.ok ? '' : ` : ${up.reason}`}`
      : 'Station au niveau maximum';
    for (const [tech, b] of this.techButtons) {
      const level = f.research[tech];
      b.querySelector('span')!.textContent = `${TECHS[tech].name} ${level}/${MAX_TECH_LEVEL}`;
      const detail = level < MAX_TECH_LEVEL ? `${TECHS[tech].costs[level]} cr, ${TECHS[tech].times[level]} s` : 'Terminé';
      this.setButton(b, canResearch(w, this.player, tech), detail);
      b.title = `${TECHS[tech].effect}. ${b.title}`;
    }

    for (const [id, b] of this.heroButtons) {
      this.setButton(b, canBuildHero(w, this.player, id), `${HEROES[id].cost} cr, ${HEROES[id].buildTime} s`);
    }
    this.renderList(this.unitsQueue!, f.queue.map((q, i) => ({
      label: q.kind === 'ship' ? orderName(q.cls) : q.kind === 'hero' ? HEROES[q.hero].name : '',
      attr: `data-index="${i}"`, progress: i === 0 ? 1 - q.remaining / q.total : 0,
    })), 'File vide : choisis un vaisseau ci-dessus.');
    const sites = [...w.entities.values()].filter((e) => e.faction === this.player && e.kind === 'structure' && e.build < 1);
    const workName = (q: QueueItem): string => {
      if (q.kind !== 'tier') return 'Amélioration de la station';
      const target = w.entities.get(q.id);
      return target ? `${entityName(target)} niveau ${target.level + 1}` : 'Amélioration';
    };
    this.renderList(this.buildingsQueue!, [
      ...f.buildQueue.map((q, i) => ({ label: workName(q), attr: `data-index="${i}"`, progress: i === 0 ? 1 - q.remaining / q.total : 0 })),
      ...sites.map((e) => ({ label: entityName(e), attr: `data-site="${e.id}"`, progress: e.build })),
    ], 'Rien en construction.');
    const job = f.researching;
    const sig = job ? `job:${job.tech}` : 'none';
    if (this.lists.get(this.researchStatus!) !== sig) {
      this.lists.set(this.researchStatus!, sig);
      this.researchStatus!.innerHTML = job
        ? `<span>${TECHS[job.tech].name} niveau ${f.research[job.tech] + 1}</span>` +
          '<button data-act="cancel-research" aria-label="Annuler la recherche">Annuler<span class="progress"></span></button>'
        : '<span class="muted">Aucune recherche en cours. Chaque niveau demande une station du même niveau.</span>';
    }
    const bar = this.researchStatus!.querySelector<HTMLElement>('.progress');
    if (bar && job) bar.style.width = pct(1 - job.remaining / job.total);
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
      `<optgroup label="Structures">${structures}<option value="station">${STATION.name}</option></optgroup>` +
      '<optgroup label="Héros"><option value="admiral">Amiral</option><option value="ace">As</option><option value="engineer">Ingénieur</option></optgroup></select>'));
    const faction = p.querySelector<HTMLSelectElement>('[name=faction]')!;
    const what = p.querySelector<HTMLSelectElement>('[name=what]')!;
    faction.value = this.scene.opts.enemies[0];
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

    const fog = p.appendChild(el('label', { class: 'check' }, '<input type="checkbox" name="fog"> Brouillard de guerre'));
    const fogBox = fog.querySelector('input')!;
    fogBox.checked = this.world.fog;
    fogBox.onchange = () => (this.world.fog = fogBox.checked);
    const ai = p.appendChild(el('label', { class: 'check' }, '<input type="checkbox" name="ai"> IA ennemie active'));
    const box = ai.querySelector('input')!;
    box.checked = !open;
    box.onchange = () => {
      for (const id of this.scene.opts.enemies) {
        const enemy = this.world.factions.get(id);
        if (enemy) enemy.ai = box.checked ? newAI(this.scene.opts.difficulty) : null;
      }
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

  /** An attack or a loss, with a button to go and see. */
  alert(text: string, see: () => void): void {
    toast(this.toasts, text, see);
  }

  /** Reflects speed, pause, sandbox and mode states in the buttons. */
  refreshModes(): void {
    const piloted = this.scene.controls?.pilotId == null ? undefined : this.world.entities.get(this.scene.controls.pilotId);
    this.pilotBanner.hidden = !piloted;
    if (piloted) {
      this.pilotBanner.innerHTML = `<strong>Pilotage : ${entityName(piloted)}</strong> ` +
        '<span class="muted">ZQSD (ou WASD) ou flèches : piloter. Clic gauche maintenu : tirer sur la cible marquée. P ou Échap : rendre la main.</span>';
    }
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
    this.placeTip();
    this.timer -= delta;
    if (this.timer > 0) return;
    this.timer = REFRESH_MS;
    this.refresh();
  }

  /** The tooltip follows the pointer every frame; its text changes with the refresh. */
  private placeTip(): void {
    if (this.tip.hidden) return;
    const p = this.scene.input.activePointer;
    const r = this.tip.getBoundingClientRect();
    this.tip.style.left = `${Math.min(p.x + 16, window.innerWidth - r.width - 8)}px`;
    this.tip.style.top = `${Math.max(8, p.y - r.height - 12)}px`;
  }

  /** What is under the pointer on the battlefield: name, hull, shields, what it is doing. */
  private refreshTip(): void {
    const c = this.scene.controls;
    const e = c && c.pointerOver && !c.box && c.pilotId == null ? c.hovered() : null;
    if (!e || !this.scene.canSee(e)) {
      this.tip.hidden = true;
      return;
    }
    const own = e.faction === this.player;
    const lines = [`<strong>${entityName(e)}</strong> <span class="muted">${FACTIONS[e.faction].name}${own ? '' : ', ennemi'}</span>`];
    lines.push(`Coque ${Math.ceil(e.hull)} / ${Math.round(e.maxHull)}${e.maxShield > 0 ? `, bouclier ${Math.ceil(e.shield)} / ${Math.round(e.maxShield)}` : ''}`);
    if (e.build < 1) lines.push(`En construction : ${pct(e.build)}`);
    const a = e.ability;
    if (a) lines.push(`${ABILITIES[a.id].name} : ${a.active > 0 ? 'active' : a.cooldown > 0 ? `recharge ${Math.ceil(a.cooldown)} s` : 'prête'}`);
    if (own && e.kind === 'ship') lines.push(`<span class="muted">${e.order ? { move: 'En route', attackMove: 'Attaque-mouvement', attack: 'Attaque' }[e.order.type] : e.targetId != null ? 'Au combat' : 'Inactif'}</span>`);
    const html = lines.join('<br>');
    if (this.tip.innerHTML !== html) this.tip.innerHTML = html;
    this.tip.hidden = false;
  }

  /** Control groups, idle ships and the last alert, one click away. */
  private refreshQuickbar(): void {
    const c = this.scene.controls;
    if (!c) return;
    const groups = c.groupSizes().map(([n, count]) =>
      `<button data-group="${n}" title="Groupe ${n} (touche ${n}, deux fois : centrer)">${n}<small>${count}</small></button>`).join('');
    const idle = c.idleShips().length;
    const alerts = this.scene.alerts.length;
    const html = groups +
      `<button data-act="idle" title="Vaisseaux inactifs (I : le suivant, Maj + I : tous)"${idle === 0 ? ' disabled' : ''}>Inactifs<small>${idle}</small></button>` +
      `<button data-act="alert" title="Aller à la dernière alerte (Retour arrière)"${alerts === 0 ? ' disabled' : ''}>Alerte</button>`;
    if (this.quickbar.innerHTML !== html) this.quickbar.innerHTML = html;
  }

  private refresh(): void {
    const w = this.world;
    const f = w.factions.get(this.player)!;
    // The setting may change here or in the options dialog.
    f.autoAbilities = settings.autoAbilities;
    const station = stationOf(w, this.player);
    this.refreshTip();
    this.refreshQuickbar();
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

    this.refreshProduction();
    this.refreshSelection();
    this.drawMinimap();
  }

  private setButton(b: HTMLButtonElement, r: Result, detail: string): void {
    b.disabled = !r.ok;
    b.querySelector('small')!.textContent = r.ok ? detail : shortReason(r.reason);
    b.title = r.ok ? detail : r.reason;
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
    html += this.abilityButtons(ents);
    if (html !== this.lastSelection) {
      this.lastSelection = html;
      this.selection.innerHTML = html;
    }
  }

  /**
   * One button per ability among the selected own ships: what it does, and whether it is
   * running, ready or recharging. With "automatic" on, the ships use it by themselves.
   */
  private abilityButtons(ents: Entity[]): string {
    const byId = new Map<AbilityId, { ready: number; active: number; wait: number }>();
    for (const e of ents) {
      if (e.faction !== this.player || !e.ability) continue;
      const a = byId.get(e.ability.id) ?? { ready: 0, active: 0, wait: Infinity };
      if (e.ability.cooldown === 0) a.ready++;
      else a.wait = Math.min(a.wait, e.ability.cooldown);
      a.active = Math.max(a.active, e.ability.active);
      byId.set(e.ability.id, a);
    }
    if (byId.size === 0) return '';
    const mode = this.scene.controls?.mode;
    const auto = this.world.factions.get(this.player)!.autoAbilities;
    return '<div class="abilities">' + [...byId].map(([id, a]) => {
      const def = ABILITIES[id];
      const aim = def.target === 'point' ? ', puis clic où viser' : def.target === 'enemy' ? ', puis clic sur un ennemi' : '';
      const state = a.active > 0 ? `active encore ${Math.ceil(a.active)} s`
        : a.ready > 0 ? `prête : E${aim}`
        : `recharge ${Math.ceil(a.wait)} s`;
      const aiming = mode?.kind === 'ability' && mode.id === id;
      return `<button data-ability="${id}" aria-pressed="${aiming}"${a.active > 0 ? ' class="running"' : ''}>` +
        `<span>${def.name}</span><small>${aiming ? 'clique pour viser, Échap pour annuler' : state}</small><small class="desc">${def.description}</small></button>`;
    }).join('') +
      `<label class="check" title="Tes vaisseaux utilisent leurs capacités seuls quand c'est utile"><input type="checkbox" data-act="auto-abilities"${auto ? ' checked' : ''}> Automatique</label></div>`;
  }

  private describe(e: Entity): string {
    const own = e.faction === this.player;
    const lines: string[] = [];
    lines.push(`<div class="title">${entityName(e)}</div>`);
    lines.push(`<div class="muted">${FACTIONS[e.faction].name}${own ? '' : ' (ennemi)'}</div>`);
    lines.push(`Coque ${Math.ceil(e.hull)} / ${Math.round(e.maxHull)}<div class="bar"><i style="width:${pct(e.hull / e.maxHull)};background:var(--ok)"></i></div>`);
    if (e.maxShield > 0) {
      lines.push(`Bouclier ${Math.floor(e.shield)} / ${Math.round(e.maxShield)}<div class="bar"><i style="width:${pct(e.shield / e.maxShield)};background:var(--shield)"></i></div>`);
    }
    if (e.build < 1) lines.push(`En construction : ${pct(e.build)}`);
    if (e.type === 'station') {
      lines.push(`Niveau ${e.level}, ${childrenOf(this.world, e.id).length} points d'attache`);
      if (own) lines.push('<span class="muted">Clic droit : point de ralliement</span>');
    }
    if (e.hero) lines.splice(1, 0, `<div class="muted">${HEROES[e.hero].title}, ${HEROES[e.hero].description}</div>`);
    const tiers = e.kind === 'structure' ? BUILDABLES[e.type as BuildableType]?.tiers : undefined;
    if (tiers) {
      lines.push(`<div>Niveau ${e.level} / ${tiers.length + 1}</div>`);
      if (own && e.level <= tiers.length) {
        const r = canUpgradeStructure(this.world, this.player, e.id);
        const label = `Améliorer au niveau ${e.level + 1} (${tiers[e.level - 1].cost} cr)`;
        lines.push(`<div><button data-tier="${e.id}"${r.ok ? '' : ` disabled title="${r.reason}"`}>${label}</button>${r.ok ? '' : `<br><span class="muted">${r.reason}</span>`}</div>`);
      }
    }
    if (e.hangar) lines.push(`Appareils lancés : ${e.hangar.launched.length} / ${e.hangar.def.cap}`);
    if (e.income > 0) lines.push(`Revenu : +${e.income} cr/s`);
    if (e.kind === 'hardpoint' && e.parentId != null) {
      const parent = this.world.entities.get(e.parentId);
      if (parent) lines.push(`<span class="muted">Sur : ${entityName(parent)}</span>`);
    }
    const slots = e.kind === 'ship' ? SHIPS[e.type as ShipClass].hardpoints : undefined;
    if (slots) {
      const parts = childrenOf(this.world, e.id);
      const turrets = parts.filter((p) => p.weapons.length > 0).length;
      const maxTurrets = slots.filter((s) => s.role === 'turret' || s.role === 'heavyTurret').length;
      const status = [`tourelles ${turrets} / ${maxTurrets}`];
      if (e.maxEngines > 0) status.push(`réacteurs ${e.engines} / ${e.maxEngines}`);
      lines.push(`<div>Pièces : ${status.join(', ')}</div>`);
      if (e.shieldGens === 0) lines.push('<div>Générateur de bouclier détruit</div>');
    }
    const fx = e.effects;
    const states = [
      fx.marked > 0 && `marqué ${Math.ceil(fx.marked)} s`, fx.stunned > 0 && `paralysé ${Math.ceil(fx.stunned)} s`,
      fx.corroded > 0 && `rongé ${Math.ceil(fx.corroded)} s`, e.expires != null && `disparaît dans ${Math.ceil(e.expires)} s`,
    ].filter(Boolean);
    if (states.length > 0) lines.push(`<div class="warn">${states.join(', ')}</div>`);
    if (!own && e.ability) lines.push(`<div class="muted">Capacité : ${ABILITIES[e.ability.id].name}. ${ABILITIES[e.ability.id].description}</div>`);
    if (e.kind === 'ship' && own) {
      lines.push(`<span class="muted">${e.pilot ? 'Piloté' : e.order ? { move: 'En route', attackMove: 'Attaque-mouvement', attack: 'Attaque' }[e.order.type] : 'En position'}</span>`);
      lines.push('<div><button data-act="pilot">Piloter (P)</button></div>');
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
      if (!isVisible(w, this.player, e) && !(e.kind === 'structure' && wasSeen(w, this.player, e))) continue;
      ctx.fillStyle = hex(FACTIONS[e.faction].color);
      const s = e.kind === 'ship' ? (e.size === 'small' ? 1.5 : 2.5) : e.type === 'station' ? 7 : 4;
      ctx.fillRect(e.x * sx - s / 2, e.y * sy - s / 2, s, s);
    }
    const v = this.scene.cameras.main.worldView;
    ctx.strokeStyle = '#d9dee7';
    ctx.lineWidth = 1;
    ctx.strokeRect(v.x * sx, v.y * sy, v.width * sx, v.height * sy);
    // Recent alerts ping where they happened.
    const now = this.scene.time.now;
    ctx.strokeStyle = '#e8705f';
    ctx.lineWidth = 2;
    for (const a of this.scene.alerts) {
      const age = (now - a.time) / PING_MS;
      if (age >= 1) continue;
      ctx.globalAlpha = 1 - age;
      ctx.beginPath();
      ctx.arc(a.x * sx, a.y * sy, 4 + age * 18, 0, Math.PI * 2);
      ctx.stroke();
    }
    ctx.globalAlpha = 1;
  }

  /** Opens a dialog in place of the open one; `onClose` runs when it is closed. */
  private openModal(content: HTMLElement, onClose: (() => void) | null = null): void {
    this.modal?.remove();
    this.modal = this.root.appendChild(el('div', { class: 'modal', role: 'dialog', 'aria-modal': 'true' }));
    this.modal.append(content);
    this.onModalClose = onClose;
    content.querySelector<HTMLElement>('button')?.focus();
  }

  private closeModal(): void {
    this.modal?.remove();
    this.modal = null;
    const then = this.onModalClose;
    this.onModalClose = null;
    then?.();
  }

  /** Escape closes the open dialog; false when there was none. */
  escape(): boolean {
    if (!this.modal) return false;
    this.closeModal();
    return true;
  }

  /** The game pauses behind a menu: resume, options, controls, restart or leave. */
  pauseMenu(): void {
    if (!this.scene.paused) {
      this.scene.togglePause();
      this.menuPaused = true;
    }
    const campaign = !!this.mode.campaign;
    const retreat = campaign && this.scene.opts.campaign?.attacker === this.player;
    const p = el('div', { class: 'panel pause' },
      `<h2>Pause</h2><p class="muted">Bataille en cours depuis ${duration(this.world.time)}.</p>` +
      '<div class="menu-list">' +
      '<button class="primary" data-act="resume">Reprendre</button>' +
      '<button data-act="options">Options</button><button data-act="help">Commandes</button>' +
      (campaign ? '' : '<button data-act="restart">Recommencer</button><button data-act="menu">Menu principal</button>') +
      (retreat ? '<button data-act="retreat">Retraite</button>' : '') +
      '</div>');
    const back = () => this.pauseMenu();
    p.addEventListener('click', (ev) => {
      const act = (ev.target as HTMLElement).closest('button')?.dataset.act;
      if (act === 'resume') this.closeModal();
      if (act === 'options') this.openModal(optionsPanel(() => this.closeModal()), back);
      if (act === 'help') this.showHelp(back);
      if (act === 'restart') this.confirm('Recommencer ?', 'La bataille repart du début, sur la même carte.', 'Recommencer', () => this.scene.restart(), back);
      if (act === 'menu') this.confirm('Quitter la partie ?', 'La bataille en cours sera perdue.', 'Quitter', () => this.scene.toMenu(), back);
      if (act === 'retreat') this.scene.retreat();
    });
    this.openModal(p, () => {
      if (!this.menuPaused) return;
      this.menuPaused = false;
      if (this.scene.paused) this.scene.togglePause();
    });
  }

  /** Each faction's record: built, lost, destroyed, damage dealt, credits earned. */
  private statsTable(): string {
    const rows = [...this.world.factions.values()].map((f) => {
      const s = f.stats;
      const own = f.id === this.player;
      return `<tr${own ? ' class="own"' : ''}><th scope="row"><span class="who"><img class="emblem" src="${emblemUrl(f.id, 2)}" alt="">${FACTIONS[f.id].name}</span></th>` +
        `<td>${s.built}</td><td>${s.lost}</td><td>${s.kills}</td><td>${Math.round(s.damage)}</td><td>${Math.round(s.earned)}</td></tr>`;
    }).join('');
    return `<p class="muted">Durée : ${duration(this.world.time)}</p>` +
      '<table class="stats"><thead><tr><th scope="col">Faction</th><th scope="col">Construits</th><th scope="col">Perdus</th>' +
      `<th scope="col">Détruits</th><th scope="col">Dégâts</th><th scope="col">Crédits gagnés</th></tr></thead><tbody>${rows}</tbody></table>`;
  }

  showEnd(won: boolean): void {
    const p = el('div', { class: 'panel' },
      `<h2>${won ? 'Victoire' : 'Défaite'}</h2>` +
      `<p>${won ? 'La station ennemie est détruite.' : 'Ta station est détruite.'}</p>` + this.statsTable() +
      '<div class="actions"><button class="primary" data-act="again">Rejouer la même carte</button><button data-act="reroll">Nouvelle carte</button>' +
      '<button data-act="menu">Menu</button><button data-act="close">Continuer à regarder</button></div>');
    p.querySelector<HTMLButtonElement>('[data-act=again]')!.onclick = () => this.scene.restart();
    p.querySelector<HTMLButtonElement>('[data-act=reroll]')!.onclick = () => this.scene.restart(true);
    p.querySelector<HTMLButtonElement>('[data-act=menu]')!.onclick = () => this.scene.toMenu();
    p.querySelector<HTMLButtonElement>('[data-act=close]')!.onclick = () => this.closeModal();
    this.openModal(p);
  }

  /** End of a campaign battle: the only way on is back to the galaxy map. */
  showBattleEnd(won: boolean, onBack: () => void): void {
    const p = el('div', { class: 'panel' },
      `<h2>${won ? 'Victoire' : 'Défaite'}</h2>` +
      `<p>${won ? 'La bataille est gagnée.' : 'La bataille est perdue.'} Les survivants regagnent la carte galactique.</p>` + this.statsTable() +
      '<div class="actions"><button class="primary">Retour à la carte</button></div>');
    p.querySelector('button')!.onclick = onBack;
    this.openModal(p);
  }

  /** Asks before an action that loses progress. */
  /** Asks before an action that loses progress; `back` runs if the player changes their mind. */
  private confirm(title: string, text: string, action: string, onYes: () => void, back: (() => void) | null = null): void {
    const p = el('div', { class: 'panel' },
      `<h2>${title}</h2><p>${text}</p>` +
      `<div class="actions"><button data-act="no">Annuler</button><button class="primary" data-act="yes">${action}</button></div>`);
    p.querySelector<HTMLButtonElement>('[data-act=no]')!.onclick = () => this.closeModal();
    p.querySelector<HTMLButtonElement>('[data-act=yes]')!.onclick = () => {
      this.onModalClose = null;
      this.closeModal();
      onYes();
    };
    this.openModal(p, back);
  }

  private showOptions(): void {
    this.openModal(optionsPanel(() => this.closeModal()));
  }

  private showHelp(back: (() => void) | null = null): void {
    const p = el('div', { class: 'panel' },
      '<h2>Commandes</h2>' + controlsList(BATTLE_CONTROLS) +
      '<p class="muted"><strong>Capacités.</strong> Chaque classe de vaisseau et chaque héros en a une. Le panneau de sélection dit ce qu\'elle fait et si elle est prête, active ou en recharge. ' +
      'E (ou son bouton) la déclenche ; certaines se visent d\'un clic. Case « Automatique » : tes vaisseaux s\'en servent seuls quand c\'est utile. Sur le champ de bataille, un anneau coloré montre une capacité active, et son nom s\'affiche quand elle part.</p>' +
      '<p class="muted"><strong>Construction.</strong> Autour de la station et des avant-postes. Un avant-poste se pose loin de la base, là où un de tes vaisseaux tient la position, hors combat ; il ouvre une nouvelle zone de construction. ' +
      'Plateformes et docks se construisent aussi près de tes mines. Sélectionne une plateforme ou un avant-poste pour l\'améliorer.</p>' +
      '<p class="muted">Détruire les points d\'attache d\'une station la désarme : chantier naval, hangar, générateur de bouclier, tourelles. Le brouillard de guerre cache ce que tes unités ne voient pas, l\'IA comprise. ' +
      'Les héros et le Titan sont uniques ; un héros détruit se reconstruit après un délai.</p>' +
      '<div class="actions"><button class="primary">Fermer</button></div>');
    p.querySelector('button')!.onclick = () => this.closeModal();
    this.openModal(p, back);
  }
}
