import Phaser from 'phaser';
import { ABILITIES, type AbilityId } from '../data/abilities';
import type { HeroRole } from '../data/heroes';
import { SHIPS, type FactionId, type ShipClass } from '../data/units';
import { useAbility } from '../sim/abilities';
import { isVisible } from '../sim/vision';
import { BUILDABLES, type BuildableType } from '../data/structures';
import { issueAttack, issueMove, issueStop } from '../sim/movement';
import { checkPlacement, placeStructure } from '../sim/structures';
import {
  addFaction, dist, spawnHero, spawnShip, spawnStation, spawnStructure, stationOf, type Entity, type World,
} from '../sim/world';
import type { BattleScene } from '../scenes/BattleScene';
import { settings } from '../ui/settings';

/** In the sandbox, a hero spawns for whichever faction is picked. */
export type SpawnWhat = ShipClass | BuildableType | 'station' | HeroRole;

export type Mode =
  | { kind: 'select' }
  | { kind: 'place'; type: BuildableType }
  | { kind: 'attackMove' }
  | { kind: 'spawn'; faction: FactionId; what: SpawnWhat };

export const ZOOMS = [0.25, 0.5, 0.75, 1, 1.5, 2, 3];
const EDGE = 10;
const PAN_SPEED = 900;
const DOUBLE_TAP_MS = 350;

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

export function spawnAt(world: World, faction: FactionId, what: SpawnWhat, x: number, y: number): void {
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
  private arrows: Phaser.Types.Input.Keyboard.CursorKeys;
  /** Ship flown by hand, if any. */
  pilotId: number | null = null;
  private firing = false;
  /** Physical keys held down (KeyboardEvent.code): WASD sits where ZQSD is on AZERTY. */
  private keys = new Set<string>();
  private lastClick = { id: -1, time: 0 };

  constructor(private scene: BattleScene) {
    const input = scene.input;
    input.mouse?.disableContextMenu();
    input.on('pointerdown', this.onDown, this);
    input.on('pointermove', this.onMove, this);
    input.on('pointerup', this.onUp, this);
    input.on('wheel', this.onWheel, this);
    input.on('gameover', () => (this.pointerInside = true));
    input.on('gameout', () => (this.pointerInside = false));
    const kb = input.keyboard!;
    this.arrows = kb.createCursorKeys();
    kb.on('keydown', this.onKey, this);
    kb.on('keyup', (ev: KeyboardEvent) => this.keys.delete(ev.code));
    // Keep Space from scrolling or pressing a focused HUD button.
    kb.addCapture('SPACE');
    const release = () => {
      this.keys.clear();
      this.firing = false;
    };
    window.addEventListener('blur', release);
    scene.events.once('shutdown', () => window.removeEventListener('blur', release));
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
      return;
    }
    if (p.rightButtonDown()) {
      if (this.mode.kind !== 'select') this.setMode({ kind: 'select' });
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
      issueMove(this.world, this.ownShips().map((s) => s.id), w.x, w.y, true);
      this.scene.marker(w.x, w.y, 0xe8a13a);
      if (!shift) this.setMode({ kind: 'select' });
    } else if (m.kind === 'spawn') {
      spawnAt(this.world, m.faction, m.what, w.x, w.y);
    } else {
      this.box = { x0: w.x, y0: w.y, x1: w.x, y1: w.y };
    }
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
    if (!this.box || p.button !== 0) return;
    const b = this.box;
    this.box = null;
    const shift = p.event.shiftKey;
    const sel = this.scene.selected;
    const zoom = this.scene.cameras.main.zoom;
    if (Math.abs(b.x1 - b.x0) * zoom < 6 && Math.abs(b.y1 - b.y0) * zoom < 6) {
      let e = pickAt(this.world, b.x1, b.y1, 6 / zoom, this.player);
      // A click on one of your ship's parts selects the ship.
      const parent = e?.parentId == null ? undefined : this.world.entities.get(e.parentId);
      if (e && parent?.kind === 'ship' && e.faction === this.player) e = parent;
      const now = performance.now();
      const double = e != null && e.id === this.lastClick.id && now - this.lastClick.time < DOUBLE_TAP_MS;
      this.lastClick = { id: e?.id ?? -1, time: now };
      if (double && e!.kind === 'ship' && e!.faction === this.player) {
        // Double click: every ship of that class on screen.
        const view = this.scene.cameras.main.worldView;
        for (const s of this.world.entities.values()) {
          if (s.kind === 'ship' && s.faction === this.player && s.type === e!.type && view.contains(s.x, s.y)) sel.add(s.id);
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
    for (const e of this.world.entities.values()) {
      if (e.kind === 'ship' && e.faction === this.player && e.x >= x0 && e.x <= x1 && e.y >= y0 && e.y <= y1) sel.add(e.id);
    }
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
    const cam = this.scene.cameras.main;
    const i = ZOOMS.indexOf(cam.zoom);
    const next = ZOOMS[Math.max(0, Math.min(ZOOMS.length - 1, (i < 0 ? 3 : i) + (dy > 0 ? -1 : 1)))];
    if (next === cam.zoom) return;
    // Keep the world point under the cursor fixed (the camera zooms around its center).
    const wx = cam.scrollX + cam.width / 2 + (p.x - cam.width / 2) / cam.zoom;
    const wy = cam.scrollY + cam.height / 2 + (p.y - cam.height / 2) / cam.zoom;
    cam.setZoom(next);
    cam.scrollX = wx - cam.width / 2 - (p.x - cam.width / 2) / next;
    cam.scrollY = wy - cam.height / 2 - (p.y - cam.height / 2) / next;
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
    if (key === ' ') this.scene.togglePause();
    else if (key === 'escape') {
      if (this.mode.kind !== 'select') this.setMode({ kind: 'select' });
      else this.scene.selected.clear();
    } else if (key === 'a' && this.ownShips().length > 0) this.setMode({ kind: 'attackMove' });
    else if (key === 's') issueStop(this.world, this.ownShips().map((s) => s.id));
    else if (key === 'h') {
      const st = stationOf(this.world, this.player);
      if (st) this.scene.cameras.main.centerOn(st.x, st.y);
    }
  }

  private recallGroup(n: number): void {
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
   * those of one kind, when they are ready.
   */
  useAbilities(id?: AbilityId): void {
    const piloted = this.pilotId == null ? undefined : this.world.entities.get(this.pilotId);
    const ships = piloted ? [piloted] : this.ownShips();
    const ready = ships.filter((s) => s.ability && (!id || s.ability.id === id) && s.ability.cooldown === 0);
    if (ready.length === 0) {
      if (ships.some((s) => s.ability)) this.scene.hud.toast('Capacité en recharge');
      return;
    }
    for (const s of ready) useAbility(this.world, s);
    this.scene.hud.toast(ABILITIES[ready[0].ability!.id].name);
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

  /** Arrow keys and screen edges pan the camera; while piloting, the camera follows the ship. */
  update(delta: number): void {
    if (this.pilotId != null) return this.updatePilot();
    const cam = this.scene.cameras.main;
    const p = this.scene.input.activePointer;
    const step = (PAN_SPEED * delta) / 1000 / cam.zoom;
    const edge = settings.edgePan && this.pointerInside && !this.panFrom;
    if (this.arrows.left!.isDown || (edge && p.x < EDGE)) cam.scrollX -= step;
    if (this.arrows.right!.isDown || (edge && p.x > cam.width - EDGE)) cam.scrollX += step;
    if (this.arrows.up!.isDown || (edge && p.y < EDGE)) cam.scrollY -= step;
    if (this.arrows.down!.isDown || (edge && p.y > cam.height - EDGE)) cam.scrollY += step;
  }

  /** Ghost of the structure being placed: where it would go and whether it can. */
  placementGhost(): { x: number; y: number; radius: number; ok: boolean } | null {
    if (this.mode.kind !== 'place') return null;
    const p = checkPlacement(this.world, this.player, this.mode.type, this.hover.x, this.hover.y);
    return { x: p.x, y: p.y, radius: BUILDABLES[this.mode.type].radius, ok: p.ok };
  }
}
