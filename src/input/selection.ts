import Phaser from 'phaser';
import { ABILITIES, type AbilityId } from '../data/abilities';
import type { HeroRole } from '../data/heroes';
import { SHIPS, type FactionId, type ShipClass } from '../data/units';
import { useAbility, type Aim } from '../sim/abilities';
import { aimedTarget } from '../sim/combat';
import { isVisible } from '../sim/vision';
import { BUILDABLES, type BuildableType } from '../data/structures';
import { issueAttack, issueMove, issueStop } from '../sim/movement';
import { checkPlacement, placeStructure } from '../sim/structures';
import {
  addFaction, dist, spawnHero, spawnShip, spawnStation, spawnStructure, stationOf, type Entity, type World,
} from '../sim/world';
import type { BattleScene } from '../scenes/BattleScene';
import { saveSettings, settings } from '../ui/settings';
import { onceEach } from './keys';

/** In the sandbox, a hero spawns for whichever faction is picked. */
export type SpawnWhat = ShipClass | BuildableType | 'station' | HeroRole;

export type Mode =
  | { kind: 'select' }
  | { kind: 'place'; type: BuildableType }
  | { kind: 'attackMove' }
  /** Picking where (or whom) an ability is aimed at. */
  | { kind: 'ability'; id: AbilityId }
  | { kind: 'spawn'; faction: FactionId; what: SpawnWhat };

export const ZOOMS = [0.25, 0.5, 0.75, 1, 1.5, 2, 3];
const EDGE = 10;
const PAN_SPEED = 900;
/** Shift held: the view pans this much faster. */
const PAN_FAST = 2.5;
const DOUBLE_TAP_MS = 350;
/** Time constant of the eased zoom, in milliseconds. */
const ZOOM_EASE_MS = 70;

/**
 * The entity under a point; small entities win over the big ones they overlap. With
 * `viewer`, enemies that faction cannot see are left out.
 */
export function pickAt(world: World, x: number, y: number, slop: number, viewer?: FactionId): Entity | null {
  let best: Entity | null = null;
  let bestRatio = Infinity;
  for (const e of world.entities.values()) {
    if (viewer && !isVisible(world, viewer, e)) continue;
    const ratio = dist(e, { x, y }) / (e.radius + slop);
    if (ratio <= 1 && ratio < bestRatio) {
      bestRatio = ratio;
      best = e;
    }
  }
  return best;
}

/** Sandbox spawns stop here: past it, the game would crawl. */
export const MAX_ENTITIES = 2500;

/** Places a unit or structure for the sandbox; false when the battle is already full. */
export function spawnAt(world: World, faction: FactionId, what: SpawnWhat, x: number, y: number): boolean {
  if (world.entities.size >= MAX_ENTITIES) return false;
  if (!world.factions.has(faction)) addFaction(world, faction, 0);
  if (what === 'station') {
    if (!stationOf(world, faction)) spawnStation(world, faction, x, y);
  } else if (what === 'admiral' || what === 'ace' || what === 'engineer') {
    spawnHero(world, faction, `${faction}-${what}`, x, y, Math.random() * Math.PI * 2);
  } else if (what in SHIPS) {
    spawnShip(world, faction, what as ShipClass, x, y, Math.random() * Math.PI * 2);
  } else {
    const e = spawnStructure(world, faction, what as BuildableType, x, y);
    const field = what === 'mine' ? world.fields.find((f) => f.mineId == null && dist(f, e) <= f.radius) : null;
    if (field) {
      e.x = field.x;
      e.y = field.y;
      field.mineId = e.id;
      e.fieldId = field.id;
    }
  }
  return true;
}

export class SelectionController {
  mode: Mode = { kind: 'select' };
  /** Selection rectangle in world coordinates, while dragging. */
  box: { x0: number; y0: number; x1: number; y1: number } | null = null;
  /** Pointer position in world coordinates. */
  hover = { x: 0, y: 0 };
  private pointerInside = false;
  private groups = new Map<number, number[]>();
  private lastTap = { group: -1, time: 0 };
  private panFrom: { x: number; y: number } | null = null;
  /** Zoom level the camera eases towards, and the screen point that stays put meanwhile. */
  private zoomGoal: { zoom: number; x: number; y: number };
  /** Ship flown by hand, if any. */
  pilotId: number | null = null;
  private firing = false;
  /** Physical keys held down (KeyboardEvent.code): WASD sits where ZQSD is on AZERTY. */
  private keys = new Set<string>();
  private lastClick = { id: -1, time: 0 };
  /** Which idle ship I selects next. */
  private idleTurn = 0;

  constructor(private scene: BattleScene) {
    const input = scene.input;
    input.mouse?.disableContextMenu();
    input.on('pointerdown', this.onDown, this);
    input.on('pointermove', this.onMove, this);
    input.on('pointerup', this.onUp, this);
    // A button released over the HUD comes as an "up outside": without it, a drag would never end.
    input.on('pointerupoutside', this.onUp, this);
    input.on('wheel', this.onWheel, this);
    input.on('gameover', () => (this.pointerInside = true));
    input.on('gameout', () => (this.pointerInside = false));
    const cam = scene.cameras.main;
    this.zoomGoal = { zoom: cam.zoom, x: cam.width / 2, y: cam.height / 2 };
    const kb = input.keyboard!;
    kb.on('keydown', onceEach((ev: KeyboardEvent) => this.onKey(ev)));
    kb.on('keyup', onceEach((ev: KeyboardEvent) => this.keys.delete(ev.code)));
    // Keep Space and the arrows from scrolling or pressing a focused HUD button.
    kb.addCapture('SPACE,UP,DOWN,LEFT,RIGHT');
    const release = () => {
      this.keys.clear();
      this.firing = false;
      this.box = null;
      this.panFrom = null;
      dragging(false);
    };
    window.addEventListener('blur', release);
    scene.events.once('shutdown', () => {
      window.removeEventListener('blur', release);
      dragging(false);
    });
  }

  private get world(): World {
    return this.scene.world;
  }

  private get player(): FactionId {
    return this.scene.opts.player;
  }

  ownShips(): Entity[] {
    const out: Entity[] = [];
    for (const id of this.scene.selected) {
      const e = this.world.entities.get(id);
      if (e && e.kind === 'ship' && e.faction === this.player) out.push(e);
    }
    return out;
  }

  setMode(mode: Mode): void {
    this.mode = mode;
    this.scene.hud.refreshModes();
  }

  private worldPoint(p: Phaser.Input.Pointer): { x: number; y: number } {
    const v = this.scene.cameras.main.getWorldPoint(p.x, p.y);
    return { x: v.x, y: v.y };
  }

  private onDown(p: Phaser.Input.Pointer): void {
    const w = this.worldPoint(p);
    if (this.pilotId != null) {
      if (p.leftButtonDown()) this.firing = true;
      return;
    }
    if (p.middleButtonDown()) {
      this.panFrom = { x: p.x, y: p.y };
      dragging(true);
      return;
    }
    if (p.rightButtonDown()) {
      if (this.mode.kind !== 'select') this.setMode({ kind: 'select' });
      else if (p.event.ctrlKey) this.attackMove(w.x, w.y);
      else this.command(w.x, w.y);
      return;
    }
    const shift = p.event.shiftKey;
    const m = this.mode;
    if (m.kind === 'place') {
      const r = placeStructure(this.world, this.player, m.type, w.x, w.y);
      if (!r.ok) this.scene.hud.toast(r.reason);
      else if (!shift) this.setMode({ kind: 'select' });
    } else if (m.kind === 'attackMove') {
      this.attackMove(w.x, w.y);
      if (!shift) this.setMode({ kind: 'select' });
    } else if (m.kind === 'ability') {
      const t = pickAt(this.world, w.x, w.y, 6 / this.scene.cameras.main.zoom, this.player);
      const used = this.aimAbility(m.id, { x: w.x, y: w.y, id: t && t.faction !== this.player ? t.id : undefined });
      if (used && !shift) this.setMode({ kind: 'select' });
    } else if (m.kind === 'spawn') {
      if (!spawnAt(this.world, m.faction, m.what, w.x, w.y)) this.scene.hud.toast(`Limite de ${MAX_ENTITIES} éléments atteinte`);
    } else {
      this.box = { x0: w.x, y0: w.y, x1: w.x, y1: w.y };
      dragging(true);
    }
  }

  private attackMove(x: number, y: number): void {
    const ids = this.ownShips().map((s) => s.id);
    if (ids.length === 0) return;
    issueMove(this.world, ids, x, y, true);
    this.scene.marker(x, y, 0xe8a13a);
  }

  /** Sends the selected ships to a point, from the minimap: a plain move or an attack-move. */
  moveSelection(x: number, y: number, attack: boolean): void {
    if (attack) return this.attackMove(x, y);
    const ids = this.ownShips().map((s) => s.id);
    if (ids.length === 0) return;
    issueMove(this.world, ids, x, y);
    this.scene.marker(x, y, 0x6fd08c);
  }

  /** Warships with nothing to do: no order, no target, not flown. Hangar craft do not count. */
  idleShips(): Entity[] {
    const out: Entity[] = [];
    for (const e of this.world.entities.values()) {
      if (e.kind === 'ship' && e.faction === this.player && !e.launched && !e.order && e.targetId == null && !e.pilot) out.push(e);
    }
    return out;
  }

  /** Selects the next idle ship and centers on it; with `all`, selects every idle ship. */
  selectIdle(all: boolean): void {
    const idle = this.idleShips();
    if (idle.length === 0) {
      this.scene.hud.toast('Aucun vaisseau inactif');
      return;
    }
    const sel = this.scene.selected;
    sel.clear();
    if (all) {
      for (const e of idle) sel.add(e.id);
      return;
    }
    const e = idle[this.idleTurn++ % idle.length];
    sel.add(e.id);
    this.scene.cameras.main.centerOn(e.x, e.y);
  }

  /** Control groups that still have ships: [number, ships alive]. */
  groupSizes(): [number, number][] {
    const out: [number, number][] = [];
    for (const [n, ids] of [...this.groups].sort((a, b) => a[0] - b[0])) {
      const alive = ids.filter((id) => this.world.entities.has(id)).length;
      if (alive > 0) out.push([n, alive]);
    }
    return out;
  }

  private onMove(p: Phaser.Input.Pointer): void {
    this.pointerInside = true;
    this.hover = this.worldPoint(p);
    if (this.panFrom) {
      const cam = this.scene.cameras.main;
      cam.scrollX -= (p.x - this.panFrom.x) / cam.zoom;
      cam.scrollY -= (p.y - this.panFrom.y) / cam.zoom;
      this.panFrom = { x: p.x, y: p.y };
    }
    if (this.box) {
      this.box.x1 = this.hover.x;
      this.box.y1 = this.hover.y;
    }
  }

  private onUp(p: Phaser.Input.Pointer): void {
    if (p.button === 1) this.panFrom = null;
    if (p.button === 0) this.firing = false;
    if (!this.box || p.button !== 0) {
      if (!this.box && !this.panFrom) dragging(false);
      return;
    }
    const b = this.box;
    this.box = null;
    dragging(this.panFrom != null);
    const shift = p.event.shiftKey;
    const sel = this.scene.selected;
    const zoom = this.scene.cameras.main.zoom;
    if (Math.abs(b.x1 - b.x0) * zoom < 6 && Math.abs(b.y1 - b.y0) * zoom < 6) {
      const e = this.hovered(b.x1, b.y1);
      const now = performance.now();
      const double = e != null && e.id === this.lastClick.id && now - this.lastClick.time < DOUBLE_TAP_MS;
      this.lastClick = { id: e?.id ?? -1, time: now };
      if ((double || p.event.ctrlKey) && e?.kind === 'ship' && e.faction === this.player) {
        // Double click or Ctrl + click: every ship of that class on screen.
        if (!shift && !double) sel.clear();
        const view = this.scene.cameras.main.worldView;
        for (const s of this.world.entities.values()) {
          if (s.kind === 'ship' && s.faction === this.player && s.type === e.type && view.contains(s.x, s.y)) sel.add(s.id);
        }
        return;
      }
      if (!shift) sel.clear();
      if (e && shift && sel.has(e.id)) sel.delete(e.id);
      else if (e) sel.add(e.id);
      return;
    }
    const [x0, x1] = [Math.min(b.x0, b.x1), Math.max(b.x0, b.x1)];
    const [y0, y1] = [Math.min(b.y0, b.y1), Math.max(b.y0, b.y1)];
    if (!shift) sel.clear();
    // A ship counts as soon as its hull overlaps the rectangle: big ships are easy to catch.
    for (const e of this.world.entities.values()) {
      if (e.kind !== 'ship' || e.faction !== this.player) continue;
      if (e.x + e.radius >= x0 && e.x - e.radius <= x1 && e.y + e.radius >= y0 && e.y - e.radius <= y1) sel.add(e.id);
    }
  }

  /** What a click at this point would pick: a part of one of your ships stands for the ship. */
  hovered(x = this.hover.x, y = this.hover.y): Entity | null {
    const e = pickAt(this.world, x, y, 6 / this.scene.cameras.main.zoom, this.player);
    const parent = e?.parentId == null ? undefined : this.world.entities.get(e.parentId);
    return e && parent?.kind === 'ship' && e.faction === this.player ? parent : e;
  }

  /** Whether the pointer is over the battlefield, not over the HUD or outside the window. */
  get pointerOver(): boolean {
    return this.pointerInside;
  }

  /** Right click: attack what is under the cursor, or move there. With the station selected, sets the rally point. */
  private command(x: number, y: number): void {
    const ships = this.ownShips();
    if (ships.length > 0) {
      const t = pickAt(this.world, x, y, 6 / this.scene.cameras.main.zoom, this.player);
      const ids = ships.map((s) => s.id);
      if (t && t.faction !== this.player) {
        issueAttack(this.world, ids, t.id);
        this.scene.marker(t.x, t.y, 0xe05a4a);
      } else {
        issueMove(this.world, ids, x, y);
        this.scene.marker(x, y, 0x6fd08c);
      }
      return;
    }
    const station = stationOf(this.world, this.player);
    if (station && this.scene.selected.has(station.id)) {
      const f = this.world.factions.get(this.player)!;
      f.rallyX = x;
      f.rallyY = y;
      this.scene.marker(x, y, 0x6fd08c);
    }
  }

  private onWheel(p: Phaser.Input.Pointer, _over: unknown, _dx: number, dy: number): void {
    this.zoomStep(dy > 0 ? -1 : 1, p.x, p.y);
  }

  /** Aims for the next zoom level in or out; the world point under (sx, sy) stays put. */
  private zoomStep(dir: number, sx: number, sy: number): void {
    const i = ZOOMS.indexOf(this.zoomGoal.zoom);
    const zoom = ZOOMS[Math.max(0, Math.min(ZOOMS.length - 1, (i < 0 ? 3 : i) + dir))];
    this.zoomGoal = { zoom, x: sx, y: sy };
  }

  /** Eases the camera zoom towards its goal. */
  private easeZoom(delta: number): void {
    const cam = this.scene.cameras.main;
    const goal = this.zoomGoal;
    if (cam.zoom === goal.zoom) return;
    // Interpolated in log space, so that zooming in and out feel alike.
    const t = 1 - Math.exp(-delta / ZOOM_EASE_MS);
    const ratio = Math.log(goal.zoom / cam.zoom);
    const next = Math.abs(ratio) < 0.01 ? goal.zoom : cam.zoom * Math.exp(ratio * t);
    // The camera zooms around its center: shift the scroll to keep the anchor point fixed.
    const wx = cam.scrollX + cam.width / 2 + (goal.x - cam.width / 2) / cam.zoom;
    const wy = cam.scrollY + cam.height / 2 + (goal.y - cam.height / 2) / cam.zoom;
    cam.setZoom(next);
    cam.scrollX = wx - cam.width / 2 - (goal.x - cam.width / 2) / next;
    cam.scrollY = wy - cam.height / 2 - (goal.y - cam.height / 2) / next;
  }

  /** Centers the view on the selected units. */
  private centerOnSelection(): void {
    let x = 0;
    let y = 0;
    let n = 0;
    for (const id of this.scene.selected) {
      const e = this.world.entities.get(id);
      if (!e) continue;
      x += e.x;
      y += e.y;
      n++;
    }
    if (n > 0) this.scene.cameras.main.centerOn(x / n, y / n);
  }

  private onKey(ev: KeyboardEvent): void {
    if (ev.target instanceof HTMLInputElement || ev.target instanceof HTMLSelectElement) return;
    this.keys.add(ev.code);
    const key = ev.key.toLowerCase();
    if (key === 'p') return this.togglePilot();
    if (key === 'e') return this.useAbilities();
    if (this.pilotId != null) {
      if (key === 'escape') this.stopPilot();
      else if (key === ' ') this.scene.togglePause();
      return;
    }
    if (key === 'a' && ev.ctrlKey) {
      ev.preventDefault();
      for (const e of this.world.entities.values()) if (e.kind === 'ship' && e.faction === this.player) this.scene.selected.add(e.id);
      return;
    }
    const digit = /^Digit([1-9])$/.exec(ev.code);
    if (digit) {
      ev.preventDefault();
      const n = Number(digit[1]);
      if (ev.ctrlKey || ev.shiftKey) this.groups.set(n, this.ownShips().map((s) => s.id));
      else this.recallGroup(n);
      return;
    }
    const cam = this.scene.cameras.main;
    if (key === ' ') this.scene.togglePause();
    else if (key === 'escape') {
      // A dialog first, then the current mode, then the selection; with nothing left, the pause menu.
      if (this.scene.hud.escape()) return;
      if (this.mode.kind !== 'select') this.setMode({ kind: 'select' });
      else if (this.scene.selected.size > 0) this.scene.selected.clear();
      else this.scene.hud.pauseMenu();
    } else if (key === 'f' && this.ownShips().length > 0) this.setMode({ kind: 'attackMove' });
    else if (key === 'x') issueStop(this.world, this.ownShips().map((s) => s.id));
    else if (key === 'c') this.centerOnSelection();
    else if (ev.code === 'Backspace') {
      ev.preventDefault();
      this.scene.jumpToAlert();
    } else if (key === 'i') this.selectIdle(ev.shiftKey);
    else if (key === 'm') {
      settings.muted = !settings.muted;
      saveSettings();
      this.scene.hud.toast(settings.muted ? 'Son coupé' : 'Son rétabli');
    }
    else if (key === '+' || key === '=' || ev.code === 'NumpadAdd') this.zoomStep(1, cam.width / 2, cam.height / 2);
    else if (key === '-' || ev.code === 'NumpadSubtract') this.zoomStep(-1, cam.width / 2, cam.height / 2);
    else if (key === 'h') {
      const st = stationOf(this.world, this.player);
      if (st) this.scene.cameras.main.centerOn(st.x, st.y);
    }
  }

  recallGroup(n: number): void {
    const ids = (this.groups.get(n) ?? []).filter((id) => this.world.entities.has(id));
    if (ids.length === 0) return;
    const sel = this.scene.selected;
    sel.clear();
    for (const id of ids) sel.add(id);
    const now = performance.now();
    if (this.lastTap.group === n && now - this.lastTap.time < DOUBLE_TAP_MS) {
      const e = this.world.entities.get(ids[0])!;
      this.scene.cameras.main.centerOn(e.x, e.y);
    }
    this.lastTap = { group: n, time: now };
  }

  /**
   * Fires the abilities of the selected ships (or of the piloted one), all of them or only
   * those of one kind, when they are ready. Those that need a target wait for the next click;
   * a pilot aims them with the crosshair.
   */
  useAbilities(id?: AbilityId): void {
    const piloted = this.pilotId == null ? undefined : this.world.entities.get(this.pilotId);
    const ships = piloted ? [piloted] : this.ownShips();
    const ready = ships.filter((s) => s.ability && (!id || s.ability.id === id) && s.ability.cooldown === 0);
    if (ready.length === 0) {
      if (ships.some((s) => s.ability)) this.scene.hud.toast('Capacité en recharge');
      return;
    }
    const aimed = ready.find((s) => ABILITIES[s.ability!.id].target !== 'self');
    for (const s of ready) if (ABILITIES[s.ability!.id].target === 'self') useAbility(this.world, s);
    if (!aimed) return;
    const aid = aimed.ability!.id;
    if (piloted?.pilot) {
      const t = aimedTarget(this.world, piloted, piloted.pilot, ABILITIES[aid].range ?? 0);
      this.aimAbility(aid, { x: piloted.pilot.aimX, y: piloted.pilot.aimY, id: t?.id });
      return;
    }
    this.setMode({ kind: 'ability', id: aid });
    this.scene.hud.toast(`${ABILITIES[aid].name} : ${ABILITIES[aid].target === 'enemy' ? 'clique sur un ennemi' : 'clique où viser'}`);
  }

  /** Uses this ability of every ready selected ship at the aim; false if none could. */
  private aimAbility(id: AbilityId, aim: Aim): boolean {
    const piloted = this.pilotId == null ? undefined : this.world.entities.get(this.pilotId);
    const ships = (piloted ? [piloted] : this.ownShips()).filter((s) => s.ability?.id === id && s.ability.cooldown === 0);
    let reason = 'Capacité en recharge';
    let used = false;
    for (const s of ships) {
      const r = useAbility(this.world, s, aim);
      if (r.ok) used = true;
      else reason = r.reason;
    }
    if (!used) this.scene.hud.toast(reason);
    return used;
  }

  /** Takes the controls of the one selected ship, or gives them back. */
  togglePilot(): void {
    if (this.pilotId != null) return this.stopPilot();
    const ships = this.ownShips();
    if (ships.length !== 1) {
      this.scene.hud.toast('Sélectionne un seul de tes vaisseaux pour le piloter');
      return;
    }
    const s = ships[0];
    s.order = null;
    s.pilot = { thrust: 0, turn: 0, firing: false, aimX: s.x, aimY: s.y };
    this.pilotId = s.id;
    this.box = null;
    this.setMode({ kind: 'select' });
  }

  stopPilot(): void {
    const s = this.pilotId == null ? undefined : this.world.entities.get(this.pilotId);
    if (s) {
      s.pilot = null;
      s.guardX = s.x;
      s.guardY = s.y;
    }
    this.pilotId = null;
    this.firing = false;
    this.scene.hud.refreshModes();
  }

  private updatePilot(): void {
    const s = this.world.entities.get(this.pilotId!);
    if (!s?.pilot) {
      this.stopPilot();
      this.scene.hud.toast('Vaisseau perdu');
      return;
    }
    const held = (...codes: string[]) => codes.some((c) => this.keys.has(c));
    s.pilot.thrust = (held('KeyW', 'ArrowUp') ? 1 : 0) - (held('KeyS', 'ArrowDown') ? 1 : 0);
    s.pilot.turn = (held('KeyD', 'ArrowRight') ? 1 : 0) - (held('KeyA', 'ArrowLeft') ? 1 : 0);
    s.pilot.firing = this.firing;
    const cam = this.scene.cameras.main;
    const p = this.scene.input.activePointer;
    const aim = cam.getWorldPoint(p.x, p.y);
    s.pilot.aimX = aim.x;
    s.pilot.aimY = aim.y;
    // The camera follows the ship, a little behind.
    cam.scrollX += (s.x - cam.width / 2 - cam.scrollX) * 0.15;
    cam.scrollY += (s.y - cam.height / 2 - cam.scrollY) * 0.15;
  }

  /**
   * ZQSD (physical WASD keys), arrows and screen edges pan the camera; while piloting, the
   * camera follows the ship.
   */
  update(delta: number): void {
    this.easeZoom(delta);
    // Its casters gone (destroyed, deselected), an ability has nothing left to aim with.
    const m = this.mode;
    if (m.kind === 'ability' && !this.ownShips().some((s) => s.ability?.id === m.id)) this.setMode({ kind: 'select' });
    if (this.pilotId != null) return this.updatePilot();
    const cam = this.scene.cameras.main;
    const p = this.scene.input.activePointer;
    const held = (...codes: string[]) => codes.some((c) => this.keys.has(c));
    const fast = held('ShiftLeft', 'ShiftRight') ? PAN_FAST : 1;
    const step = (PAN_SPEED * fast * delta) / 1000 / cam.zoom;
    const edge = settings.edgePan && this.pointerInside && !this.panFrom;
    // Ctrl + A selects everything: do not pan while Ctrl is down.
    const letters = !held('ControlLeft', 'ControlRight');
    if (held('ArrowLeft') || (letters && held('KeyA')) || (edge && p.x < EDGE)) cam.scrollX -= step;
    if (held('ArrowRight') || (letters && held('KeyD')) || (edge && p.x > cam.width - EDGE)) cam.scrollX += step;
    if (held('ArrowUp') || (letters && held('KeyW')) || (edge && p.y < EDGE)) cam.scrollY -= step;
    if (held('ArrowDown') || (letters && held('KeyS')) || (edge && p.y > cam.height - EDGE)) cam.scrollY += step;
  }

  /** Ghost of the structure being placed: where it would go and whether it can. */
  placementGhost(): { x: number; y: number; radius: number; ok: boolean } | null {
    if (this.mode.kind !== 'place') return null;
    const p = checkPlacement(this.world, this.player, this.mode.type, this.hover.x, this.hover.y);
    return { x: p.x, y: p.y, radius: BUILDABLES[this.mode.type].radius, ok: p.ok };
  }
}

/** While a drag is on, the HUD lets the pointer through, so that the drag can cross it. */
function dragging(on: boolean): void {
  document.body.classList.toggle('dragging', on);
}
