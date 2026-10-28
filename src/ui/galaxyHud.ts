// HTML overlay of the galaxy map. Like the battle HUD, it reads the simulation and calls
// its commands.
import './hud.css';
import { GALAXY_RULES, SPECIALS } from '../data/galaxy';
import { HEROES, heroesOf, type HeroId } from '../data/heroes';
import { MAX_TECH_LEVEL, TECHS, TECH_IDS, type TechId } from '../data/research';
import { FACTIONS, SHIPS, SHIP_CLASSES, orderName, type ShipClass } from '../data/units';
import type { GalaxyScene } from '../scenes/GalaxyScene';
import { defenseLeft, heroSurvivors, sideStrength, survivors, type ShipCounts } from '../sim/battle';
import {
  buildName, buildPrice, buildTime, cancelBuild, cancelTech, canBuild, canResearchTech, enqueueBuild, fleetsAt,
  incomeOf, platformCap, researchSpeed, startTech, visiblePlanets,
  type BuildingKind, type GalaxyBattle, type GalaxyBuild, type GalaxyEvent, type Planet,
} from '../sim/galaxy';
import { GALAXY_CONTROLS, controlsList } from './controls';
import { el, pct, shortReason, toast } from './dom';
import { emblemUrl } from './logos';

const REFRESH_MS = 100;
const SPEEDS = [1, 2, 4];
const SHIPYARD_CLASSES = SHIP_CLASSES.filter((c) => SHIPS[c].source === 'shipyard');
const BUILDINGS: BuildingKind[] = ['station', 'mine', 'platform', 'platformTier', 'lab', 'repairDock', 'ionCannon'];

type Tab = 'planet' | 'buildings' | 'ships' | 'research';
const TABS: [Tab, string][] = [['planet', 'Planète'], ['buildings', 'Bâtiments'], ['ships', 'Vaisseaux'], ['research', 'Recherche']];

function describeShips(ships: ShipCounts, heroes: HeroId[] = []): string {
  const parts = [
    ...heroes.map((h) => `${HEROES[h].name} (${HEROES[h].title.toLowerCase()})`),
    ...SHIPYARD_CLASSES.filter((c) => ships[c]).map((c) => `${ships[c]}× ${SHIPS[c].name}`),
  ];
  return parts.length > 0 ? parts.join(', ') : 'aucun vaisseau';
}

const emblem = (faction: keyof typeof FACTIONS, cls = 'emblem') => `<img class="${cls}" src="${emblemUrl(faction, 2)}" alt="">`;

export class GalaxyHud {
  private root = el('div', { class: 'ui' });
  private credits!: HTMLElement;
  private researching!: HTMLElement;
  private panel!: HTMLElement;
  private toasts!: HTMLElement;
  private modal: HTMLElement | null = null;
  private tip!: HTMLElement;
  private tab: Tab = 'planet';
  private timer = 0;
  private lastPanel = '';
  /** Planet and fleet the send quantities were typed for: they are kept only while both stay the same. */
  private lastFleet = '';
  private buttons: { b: HTMLButtonElement; build: GalaxyBuild }[] = [];
  private techButtons = new Map<TechId, HTMLButtonElement>();

  constructor(private scene: GalaxyScene) {
    const f = FACTIONS[scene.opts.player];
    const bar = this.root.appendChild(el('div', { class: 'topbar panel' }));
    bar.append(el('span', { class: 'faction', title: `${f.trait.name} : ${f.trait.description}` }, `${emblem(scene.opts.player)}${f.name}`));
    this.credits = bar.appendChild(el('span', { class: 'credits' }));
    this.researching = bar.appendChild(el('span', { class: 'muted' }));
    bar.append(el('span', { class: 'spacer' }));
    const speeds = bar.appendChild(el('div', { class: 'group', role: 'group', 'aria-label': 'Vitesse du jeu' }));
    speeds.appendChild(el('button', { 'data-speed': '0', title: 'Pause (Espace)' }, 'Pause')).onclick = () => scene.togglePause();
    for (const s of SPEEDS) {
      speeds.appendChild(el('button', { 'data-speed': String(s), 'aria-label': `Vitesse ×${s}` }, `×${s}`)).onclick = () => scene.setSpeed(s);
    }
    const group = bar.appendChild(el('div', { class: 'group' }));
    group.appendChild(el('button', {}, 'Aide')).onclick = () => this.showHelp();
    group.appendChild(el('button', {}, 'Menu')).onclick = () => this.confirmMenu();

    this.panel = this.root.appendChild(el('aside', { class: 'galaxy-panel panel', 'aria-label': 'Planète' }));
    this.panel.addEventListener('click', (ev) => this.onPanelClick(ev));
    this.toasts = this.root.appendChild(el('div', { class: 'toasts', role: 'status', 'aria-live': 'polite' }));
    this.tip = this.root.appendChild(el('div', { class: 'tooltip panel', 'aria-hidden': 'true' }));
    this.tip.hidden = true;
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

  private onPanelClick(ev: MouseEvent): void {
    const b = (ev.target as HTMLElement).closest('button');
    const g = this.scene.galaxy;
    const player = this.scene.opts.player;
    const planet = this.scene.selected;
    if (!b) return;
    if (b.dataset.tab) this.tab = b.dataset.tab as Tab;
    if (b.dataset.cancel && planet != null) {
      const [which, i] = b.dataset.cancel.split(':');
      cancelBuild(g, planet, Number(i), which as 'units' | 'buildings');
    }
    if (b.dataset.act === 'cancel-research') cancelTech(g, player);
    const item = this.buttons.find((x) => x.b === b);
    if (item && planet != null) {
      const r = enqueueBuild(g, player, planet, item.build);
      if (!r.ok) this.toast(r.reason);
    }
    for (const [tech, tb] of this.techButtons) {
      if (tb !== b) continue;
      const r = startTech(g, player, tech);
      if (!r.ok) this.toast(r.reason);
    }
    this.refresh();
  }

  /** Ships and heroes to send, as chosen in the fleet section of the panel (everything by default). */
  sendCounts(): { ships: ShipCounts; heroes: HeroId[] } {
    const ships: ShipCounts = {};
    for (const input of this.panel.querySelectorAll<HTMLInputElement>('input[data-cls]')) {
      const n = Math.max(0, Math.min(Number(input.max), Math.floor(Number(input.value) || 0)));
      if (n > 0) ships[input.dataset.cls as ShipClass] = n;
    }
    const heroes = [...this.panel.querySelectorAll<HTMLInputElement>('input[data-hero]')]
      .filter((i) => i.checked).map((i) => i.dataset.hero as HeroId);
    return { ships, heroes };
  }

  refresh(): void {
    const g = this.scene.galaxy;
    const player = this.scene.opts.player;
    const f = g.factions.get(player)!;
    const owned = g.planets.filter((p) => p.owner === player).length;
    this.credits.innerHTML = `Crédits <strong>${Math.floor(f.credits)}</strong> <span class="muted">+${Math.round(incomeOf(g, player))}/s</span>` +
      ` <span class="muted">${owned} planète${owned > 1 ? 's' : ''}</span>`;
    const job = f.researching;
    this.researching.textContent = job ? `Recherche : ${TECHS[job.tech].name} ${pct(1 - job.remaining / job.total)}` : '';
    const id = this.scene.selected;
    const p = id == null ? null : g.planets[id];
    const sig = this.signature(p);
    if (sig !== this.lastPanel) {
      this.lastPanel = sig;
      this.renderPanel(p);
    }
    this.updatePanel(p);
  }

  /** Changes that need the panel rebuilt; everything else is updated in place. */
  private signature(p: Planet | null): string {
    const g = this.scene.galaxy;
    const f = g.factions.get(this.scene.opts.player)!;
    const research = `${TECH_IDS.map((t) => f.research[t]).join('')}:${f.researching?.tech ?? ''}`;
    if (!p) return `${this.tab}|none|${research}`;
    const fleets = fleetsAt(g, p.id).map((fl) => `${fl.faction}:${describeShips(fl.ships, fl.heroes)}`).join(';');
    const battle = g.battles.some((b) => b.planetId === p.id);
    const visible = visiblePlanets(g, this.scene.opts.player).has(p.id);
    return [
      this.tab, p.id, visible, p.owner, p.station, p.mines, p.platforms, p.platformTier, p.lab, p.repairDock, p.ionCannon, fleets,
      p.units.map((q) => q.kind).join(','), p.buildings.map((q) => q.kind).join(','), battle, p.occupation?.faction, research,
    ].join('|');
  }

  private renderPanel(p: Planet | null): void {
    const own = p?.owner === this.scene.opts.player;
    const tabs = '<div class="tabs" role="tablist">' + TABS.map(([id, label]) => {
      const disabled = (id === 'buildings' || id === 'ships') && !own;
      return `<button role="tab" data-tab="${id}" aria-selected="${this.tab === id}"${disabled ? ' disabled' : ''}>${label}</button>`;
    }).join('') + '</div>';
    if ((this.tab === 'buildings' || this.tab === 'ships') && !own) this.tab = 'planet';
    const head = p
      ? `<h2>${p.owner ? emblem(p.owner) : ''}${p.name}</h2><div class="muted">${p.owner ? FACTIONS[p.owner].name : 'Neutre'}</div>`
      : '<h2>Carte galactique</h2>';
    this.buttons = [];
    this.techButtons.clear();
    this.panel.innerHTML = head + tabs + '<div class="tab-body"></div>';
    for (const b of this.panel.querySelectorAll<HTMLButtonElement>('[data-tab]')) b.setAttribute('aria-selected', String(b.dataset.tab === this.tab));
    const body = this.panel.querySelector<HTMLElement>('.tab-body')!;
    if (this.tab === 'research') this.renderResearch(body);
    else if (!p) body.innerHTML = '<p class="muted">Clic sur une planète : la sélectionner.<br>Clic droit sur une autre : y envoyer ta flotte.</p>' +
      '<p class="muted">Une flotte seule sur une planète sans défense la prend après quelques secondes.</p>';
    else if (this.tab === 'planet') this.renderPlanet(body, p);
    else if (this.tab === 'ships') {
      const ships: GalaxyBuild[] = SHIPYARD_CLASSES.map((cls) => ({ kind: 'ship', cls }));
      const heroes: GalaxyBuild[] = heroesOf(this.scene.opts.player).map((h) => ({ kind: 'hero', hero: h.id }));
      this.renderBuilds(body, p, [...ships, ...heroes]);
    } else this.renderBuilds(body, p, BUILDINGS.map((kind) => ({ kind })));
  }

  private renderPlanet(body: HTMLElement, p: Planet): void {
    const g = this.scene.galaxy;
    const player = this.scene.opts.player;
    const previous = new Map([...this.panel.querySelectorAll<HTMLInputElement>('input[data-cls]')].map((i) => [i.dataset.cls, i.value]));
    const checked = new Map([...this.panel.querySelectorAll<HTMLInputElement>('input[data-hero]')].map((i) => [i.dataset.hero, i.checked]));
    const lines: string[] = [];
    if (p.special) lines.push(`<div class="special"><strong>${SPECIALS[p.special].name}</strong><br>${SPECIALS[p.special].description}</div>`);
    if (!visiblePlanets(g, player).has(p.id)) {
      lines.push('<p class="muted">Hors de portée de tes capteurs : défenses et flottes inconnues. Approche une flotte ou prends une planète voisine pour voir.</p>');
      body.innerHTML = lines.join('');
      return;
    }
    lines.push(`<div>Revenu +${p.income}/s, plus ${GALAXY_RULES.mineIncome * (p.special === 'rich' ? 2 : 1)}/s par mine</div>`);
    lines.push(`<div>Mines ${p.mines} / ${p.mineSlots}</div>`);
    lines.push(`<div>${p.station > 0 ? `Station niveau ${p.station}` : 'Pas de station'}, plateformes ${p.platforms} / ${platformCap(p)}</div>`);
    const extra = [p.lab && 'laboratoire', p.repairDock && 'dock de réparation', p.ionCannon && 'canon ionique'].filter(Boolean);
    if (extra.length > 0) lines.push(`<div>Aussi : ${extra.join(', ')}</div>`);
    if (p.occupation) lines.push('<div class="note" data-occupation></div>');
    if (g.battles.some((b) => b.planetId === p.id)) lines.push('<div><strong>Combat en cours</strong></div>');

    const own = fleetsAt(g, p.id, player)[0];
    const fleetSig = own ? `${p.id}:${describeShips(own.ships, own.heroes)}` : '';
    if (fleetSig !== this.lastFleet) {
      previous.clear();
      checked.clear();
    }
    this.lastFleet = fleetSig;
    if (own) {
      lines.push('<h3>Ta flotte</h3>');
      for (const c of SHIPYARD_CLASSES) {
        const n = own.ships[c];
        if (!n) continue;
        const value = Math.min(n, Number(previous.get(c) ?? n));
        lines.push(`<label class="fleet-row">${SHIPS[c].name} (${n})<input type="number" data-cls="${c}" min="0" max="${n}" value="${value}" aria-label="${SHIPS[c].name} à envoyer"></label>`);
      }
      for (const h of own.heroes) {
        const on = checked.get(h) ?? true;
        lines.push(`<label class="fleet-row">${HEROES[h].name} (${HEROES[h].title.toLowerCase()})<input type="checkbox" data-hero="${h}"${on ? ' checked' : ''}></label>`);
      }
      lines.push('<p class="muted">Clic droit sur une planète : envoyer ces vaisseaux.</p>');
    }
    for (const f of fleetsAt(g, p.id).filter((x) => x.faction !== player)) {
      lines.push(`<h3>Flotte ${FACTIONS[f.faction].name}</h3><div>${describeShips(f.ships, f.heroes)}</div>`);
    }
    body.innerHTML = lines.join('');
  }

  private renderBuilds(body: HTMLElement, p: Planet, builds: GalaxyBuild[]): void {
    const which = builds[0]?.kind === 'ship' ? 'units' : 'buildings';
    const grid = body.appendChild(el('div', { class: 'grid' }));
    for (const build of builds) {
      const b = grid.appendChild(el('button', {}, `<span>${buildName(p, build)}</span><small></small>`));
      this.buttons.push({ b, build });
    }
    const queue = p[which];
    body.appendChild(el('div', { class: 'queue' }, queue.length === 0
      ? '<span class="muted">File vide</span>'
      : '<span class="muted">File (clic : annuler)</span>' + queue.map((q, i) => {
        const name = q.kind === 'ship' ? orderName(q.cls) : buildName({ ...p, station: 0 }, q);
        return `<button data-cancel="${which}:${i}" aria-label="Annuler ${name}">${name}<span class="progress"></span></button>`;
      }).join('')));
  }

  private renderResearch(body: HTMLElement): void {
    const g = this.scene.galaxy;
    const player = this.scene.opts.player;
    const f = g.factions.get(player)!;
    const speed = researchSpeed(g, player);
    body.append(el('p', { class: 'muted' },
      `Recherche commune à toute ta faction. Il faut un laboratoire, et une station du niveau visé.${speed > 1 ? ' Les archives anciennes accélèrent la recherche.' : ''}`));
    const grid = body.appendChild(el('div', { class: 'grid' }));
    for (const tech of TECH_IDS) {
      const b = grid.appendChild(el('button', { title: TECHS[tech].effect }, `<span>${TECHS[tech].name} ${f.research[tech]}/${MAX_TECH_LEVEL}</span><small></small>`));
      this.techButtons.set(tech, b);
    }
    const job = f.researching;
    body.appendChild(el('div', { class: 'queue' }, job
      ? `<span>${TECHS[job.tech].name} niveau ${f.research[job.tech] + 1}</span><button data-act="cancel-research" aria-label="Annuler la recherche">Annuler<span class="progress"></span></button>`
      : '<span class="muted">Aucune recherche en cours</span>'));
  }

  private updatePanel(p: Planet | null): void {
    const g = this.scene.galaxy;
    const player = this.scene.opts.player;
    const f = g.factions.get(player)!;
    if (p) {
      for (const { b, build } of this.buttons) {
        const r = canBuild(g, player, p.id, build);
        b.disabled = !r.ok;
        const detail = `${buildPrice(p, build)} cr, ${Math.round(buildTime(p, build))} s`;
        b.querySelector('small')!.textContent = r.ok ? detail : shortReason(r.reason);
        b.title = r.ok ? detail : r.reason;
      }
      const which = this.tab === 'ships' ? p.units : p.buildings;
      const bar = this.panel.querySelector<HTMLElement>('[data-cancel] .progress');
      if (bar && which[0]) bar.style.width = pct(1 - which[0].remaining / which[0].total);
      const occ = this.panel.querySelector('[data-occupation]');
      if (occ && p.occupation) occ.textContent = `Prise par ${FACTIONS[p.occupation.faction].name} dans ${Math.ceil(p.occupation.remaining)} s`;
    }
    for (const [tech, b] of this.techButtons) {
      const level = f.research[tech];
      const r = canResearchTech(g, player, tech);
      b.disabled = !r.ok;
      const detail = level < MAX_TECH_LEVEL ? `${TECHS[tech].costs[level]} cr, ${TECHS[tech].times[level]} s` : 'Terminé';
      b.querySelector('small')!.textContent = r.ok ? detail : shortReason(r.reason);
      b.title = `${TECHS[tech].effect}. ${r.ok ? detail : r.reason}`;
    }
    const job = f.researching;
    const bar = this.panel.querySelector<HTMLElement>('[data-act=cancel-research] .progress');
    if (bar && job) bar.style.width = pct(1 - job.remaining / job.total);
  }

  /** Summary of the planet under the pointer, so that it can be read without selecting it. */
  tooltip(planetId: number | null, x: number, y: number): void {
    if (planetId == null || this.modal) {
      this.tip.hidden = true;
      return;
    }
    const g = this.scene.galaxy;
    const p = g.planets[planetId];
    const lines = [`<strong>${p.name}</strong>`, p.owner ? FACTIONS[p.owner].name : 'Neutre'];
    if (p.special) lines.push(SPECIALS[p.special].name);
    if (!visiblePlanets(g, this.scene.opts.player).has(p.id)) {
      lines.push('Hors de portée de tes capteurs');
      this.tip.innerHTML = lines.join('<br>');
      this.tip.hidden = false;
      this.tip.style.left = `${Math.min(x + 16, window.innerWidth - this.tip.offsetWidth - 8)}px`;
      this.tip.style.top = `${Math.min(y + 16, window.innerHeight - this.tip.offsetHeight - 8)}px`;
      return;
    }
    if (p.station > 0) lines.push(`Station niveau ${p.station}`);
    if (p.platforms > 0) lines.push(`${p.platforms} plateforme${p.platforms > 1 ? 's' : ''}`);
    if (p.ionCannon > 0) lines.push('Canon ionique');
    lines.push(`Revenu +${p.income + p.mines * GALAXY_RULES.mineIncome * (p.special === 'rich' ? 2 : 1)}/s`);
    for (const f of fleetsAt(g, p.id)) lines.push(`${FACTIONS[f.faction].name} : ${describeShips(f.ships, f.heroes)}`);
    if (g.battles.some((b) => b.planetId === p.id)) lines.push('Combat en cours');
    else if (p.occupation) lines.push(`Prise par ${FACTIONS[p.occupation.faction].name} dans ${Math.ceil(p.occupation.remaining)} s`);
    this.tip.innerHTML = lines.join('<br>');
    this.tip.hidden = false;
    const w = this.tip.offsetWidth;
    const h = this.tip.offsetHeight;
    this.tip.style.left = `${Math.min(x + 16, window.innerWidth - w - 8)}px`;
    this.tip.style.top = `${Math.min(y + 16, window.innerHeight - h - 8)}px`;
  }

  private confirmMenu(): void {
    const p = el('div', { class: 'panel' },
      '<h2>Retour au menu ?</h2><p>La campagne est sauvegardée : « Continuer la campagne » la reprend où tu l\'as laissée.</p>' +
      '<div class="actions"><button data-act="no">Annuler</button><button class="primary" data-act="yes">Menu</button></div>');
    p.querySelector<HTMLButtonElement>('[data-act=no]')!.onclick = () => this.closeModal();
    p.querySelector<HTMLButtonElement>('[data-act=yes]')!.onclick = () => this.scene.toMenu();
    this.openModal(p);
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
    } else if (ev.type === 'researched' && ev.faction === player) {
      this.toast(`Recherche terminée : ${TECHS[ev.tech].name} niveau ${g.factions.get(player)!.research[ev.tech]}`);
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
    const theirs = [describeShips(survivors(b.world, enemy), heroSurvivors(b.world, enemy))];
    if (def.ionCannon > 0) theirs.unshift('canon ionique');
    if (def.station > 0) theirs.unshift(`station niveau ${def.station}`);
    if (def.platforms > 0) theirs.unshift(`${def.platforms} plateforme${def.platforms > 1 ? 's' : ''}`);
    const p = el('div', { class: 'panel' },
      `<h2>${emblem(enemy)}Bataille de ${planet.name}</h2>` +
      `<p>${b.attacker === player ? 'Tu attaques' : 'Tu es attaqué par'} ${FACTIONS[enemy].name}.</p>` +
      '<dl>' +
      `<dt>Tes forces</dt><dd>${describeShips(survivors(b.world, player), heroSurvivors(b.world, player))}</dd>` +
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
      `<h2>${emblem(this.scene.opts.player)}${won ? 'Victoire' : 'Défaite'}</h2>` +
      `<p>${won ? "L'adversaire n'a plus aucune planète." : "Tu n'as plus aucune planète."}</p>` +
      '<div class="actions"><button class="primary">Menu</button></div>');
    p.querySelector('button')!.onclick = () => this.scene.toMenu();
    this.openModal(p);
  }

  private showHelp(): void {
    const p = el('div', { class: 'panel' },
      '<h2>Carte galactique</h2>' + controlsList(GALAXY_CONTROLS) +
      `<p class="muted">Une flotte seule prend une planète sans défense en ${GALAXY_RULES.captureTime} s (${GALAXY_RULES.neutralCaptureTime} s si elle est neutre). ` +
      'Quand deux camps se rencontrent, la bataille se commande dans la vue tactique ou se résout seule. ' +
      "Les flottes traversent les planètes neutres ou alliées et s'arrêtent aux planètes ennemies. Tu perds quand tu n'as plus de planète. " +
      'Chaque planète a deux files : bâtiments et vaisseaux. Certaines planètes ont un bonus. ' +
      'La campagne se sauvegarde toute seule ; « Continuer la campagne » la reprend depuis le menu.</p>' +
      '<div class="actions"><button class="primary">Fermer</button></div>');
    p.querySelector('button')!.onclick = () => this.closeModal();
    this.openModal(p);
  }
}
