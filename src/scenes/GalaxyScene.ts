import Phaser from 'phaser';
import { GALAXY_H, GALAXY_RULES, GALAXY_W } from '../data/galaxy';
import { FACTIONS, type FactionId } from '../data/units';
import { opaqueSize } from '../render/opaque';
import { backdrop, planetKey, type Manifest } from '../render/sprites';
import { countShips, type BattleResult } from '../sim/battle';
import {
  chooseBattle, concludeBattle, createGalaxy, delegateBattle, findPath, fleetVisible, fleetsAt, sendFleet, visiblePlanets,
  type Galaxy, type Planet,
} from '../sim/galaxy';
import { CLASSIC, generateLayout } from '../sim/galaxyGen';
import { GALAXY_STEP, stepGalaxy } from '../sim/galaxyStep';
import { clearCampaign, loadCampaign, saveCampaign } from '../sim/galaxySave';
import { onceEach } from '../input/keys';
import { hex } from '../ui/dom';
import { GalaxyHud } from '../ui/galaxyHud';
import type { MenuChoice } from '../ui/menu';
import type { BattleOptions } from './BattleScene';
import { showStartMenu } from './flow';

/** Room kept free for the HUD: top bar and the planet panel on the right. */
const TOP = 64;
const PANEL_W = 340;
const NEUTRAL = 0x6b7588;
const ACCENT = 0xe8a13a;
/** Planet tints, so that the placeholder planets do not all look alike. */
const AUTOSAVE_MS = 20000;
/** Time the galaxy may take per frame, in milliseconds. */
const STEP_BUDGET_MS = 12;
const TINTS = [0xffffff, 0xffd2a8, 0xc8f0c8, 0xffc0c0, 0xd8ccff, 0xfff0b0];

export class GalaxyScene extends Phaser.Scene {
  galaxy!: Galaxy;
  opts!: MenuChoice;
  hud!: GalaxyHud;
  /** Planet shown in the panel. */
  selected: number | null = null;
  speed = 1;
  paused = false;
  private hover: number | null = null;
  private acc = 0;
  private under!: Phaser.GameObjects.Graphics;
  private over!: Phaser.GameObjects.Graphics;
  private planets: Phaser.GameObjects.Sprite[] = [];
  private names: Phaser.GameObjects.Text[] = [];
  /** Owner emblem next to each planet. */
  private emblems: Phaser.GameObjects.Image[] = [];
  private labels: Phaser.GameObjects.Text[] = [];
  private stars: Phaser.GameObjects.TileSprite[] = [];
  private view = { s: 1, ox: 0, oy: 0 };
  private ended = false;
  /** Milliseconds since the last autosave. */
  private saveTimer = 0;

  constructor() {
    super('galaxy');
  }

  create(opts: MenuChoice): void {
    const saved = opts.resume ? loadCampaign() : null;
    const layout = opts.map === 'classic' && opts.enemies.length === 1 ? CLASSIC : generateLayout(opts.seed, 1 + opts.enemies.length);
    this.galaxy = saved ?? createGalaxy(opts.player, opts.enemies, true, opts.seed, layout, opts.difficulty);
    const player = this.galaxy.player;
    this.opts = { ...opts, player, enemies: [...this.galaxy.factions.keys()].filter((f) => f !== player) };
    this.saveTimer = 0;
    this.selected = this.galaxy.planets.findIndex((p) => p.owner === player);
    this.hover = null;
    this.speed = 1;
    this.paused = false;
    this.acc = 0;
    this.ended = false;
    this.labels = [];

    const manifest: Manifest = this.cache.json.get('manifest') ?? {};
    this.stars = backdrop(manifest).tiles.map((t) => {
      const sprite = this.add.tileSprite(0, 0, 1, 1, t.key, t.frame).setOrigin(0);
      if (sprite.frame.cutWidth < 512) sprite.setTileScale(2);
      return sprite;
    });
    this.under = this.add.graphics();
    const key = planetKey(manifest);
    const frames = manifest[key]?.frames ?? 1;
    if (frames > 1 && !this.anims.exists(key)) {
      this.anims.create({ key, frames: this.anims.generateFrameNumbers(key), frameRate: 8, repeat: -1 });
    }
    this.planets = this.galaxy.planets.map((p) => {
      const s = this.add.sprite(0, 0, key).setTint(TINTS[p.id % TINTS.length]);
      // Each planet starts its rotation at a different frame.
      if (frames > 1) s.play({ key, startFrame: (p.id * 11) % frames });
      return s;
    });
    this.names = this.galaxy.planets.map((p) =>
      this.add.text(0, 0, p.name, { fontFamily: 'ui-monospace, monospace', fontSize: '14px', color: '#d9dee7' }).setOrigin(0.5, 0),
    );
    this.emblems = this.galaxy.planets.map(() => this.add.image(0, 0, 'emblem-klaed').setVisible(false));
    this.over = this.add.graphics();

    this.input.mouse?.disableContextMenu();
    this.input.on('pointerdown', this.onPointerDown, this);
    this.input.on('pointermove', (p: Phaser.Input.Pointer) => {
      this.hover = this.pick(p.x, p.y);
      this.hud.tooltip(this.hover, p.x, p.y);
    });
    this.input.on('gameout', () => this.hud.tooltip(null, 0, 0));
    this.input.keyboard!.on('keydown', onceEach((ev: KeyboardEvent) => this.onKey(ev)));
    this.scale.on('resize', this.layout, this);
    this.hud = new GalaxyHud(this);
    this.events.on('sleep', () => this.hud.setVisible(false));
    this.events.on('wake', () => this.hud.setVisible(true));
    this.events.once('shutdown', () => {
      this.scale.off('resize', this.layout, this);
      this.events.off('sleep');
      this.events.off('wake');
      this.hud.destroy();
    });
    this.layout();
  }

  /** Fits the galaxy into the screen space the HUD leaves free. */
  private layout(): void {
    const { width, height } = this.scale;
    const w = width - PANEL_W - 48;
    const h = height - TOP - 32;
    const s = Math.max(0.2, Math.min(w / GALAXY_W, h / GALAXY_H));
    this.view = { s, ox: 24 + (w - GALAXY_W * s) / 2, oy: TOP + 16 + (h - GALAXY_H * s) / 2 };
    for (const t of this.stars) t.setSize(width, height);
    this.galaxy.planets.forEach((p, i) => {
      const r = this.radius(p);
      const img = this.planets[i];
      img.setPosition(this.sx(p.x), this.sy(p.y)).setScale((r * 2) / opaqueSize(this, img.texture.key));
      this.names[i].setPosition(this.sx(p.x), this.sy(p.y) + r + 10);
      this.emblems[i].setPosition(this.sx(p.x) - r - 6, this.sy(p.y) - r - 6).setScale(0.6);
    });
  }

  private sx(x: number): number {
    return this.view.ox + x * this.view.s;
  }

  private sy(y: number): number {
    return this.view.oy + y * this.view.s;
  }

  /** Screen radius: richer planets look bigger. */
  private radius(p: { income: number }): number {
    return Math.max(10, (14 + p.income * 3) * this.view.s);
  }

  private pick(x: number, y: number): number | null {
    for (const p of this.galaxy.planets) {
      if (Math.hypot(this.sx(p.x) - x, this.sy(p.y) - y) <= this.radius(p) + 10) return p.id;
    }
    return null;
  }

  private onPointerDown(p: Phaser.Input.Pointer): void {
    const id = this.pick(p.x, p.y);
    if (p.rightButtonDown()) {
      if (id != null) this.order(id);
      return;
    }
    this.selected = id;
  }

  private onKey(ev: KeyboardEvent): void {
    if (ev.target instanceof HTMLInputElement) return;
    if (ev.key === ' ') {
      ev.preventDefault();
      this.togglePause();
    } else if (ev.key === 'Escape') {
      this.selected = null;
    }
  }

  /** Sends the ships picked in the panel from the selected planet to `dest`. */
  private order(dest: number): void {
    const from = this.selected;
    if (from == null || from === dest) return;
    const fleet = fleetsAt(this.galaxy, from, this.opts.player)[0];
    if (!fleet) {
      this.hud.toast('Aucune flotte à toi sur cette planète');
      return;
    }
    const pick = this.hud.sendCounts();
    const r = sendFleet(this.galaxy, fleet.id, dest, pick.ships, pick.heroes);
    if (!r.ok) this.hud.toast(r.reason);
    // The fleet left: show what stays, with fresh quantities, before the next order.
    else this.hud.refresh();
  }

  togglePause(): void {
    this.paused = !this.paused;
    this.hud.refreshModes();
  }

  setSpeed(speed: number): void {
    this.speed = speed;
    this.paused = false;
    this.hud.refreshModes();
  }

  /** Saves the campaign, unless it is over. */
  save(): void {
    if (!this.galaxy.winner && !saveCampaign(this.galaxy)) this.hud.toast('Sauvegarde impossible dans ce navigateur');
    this.saveTimer = 0;
  }

  toMenu(): void {
    this.save();
    showStartMenu(this.game);
  }

  /** Plays a battle in the tactical view; the galaxy sleeps until it ends. */
  fight(battleId: number): void {
    const b = this.galaxy.battles.find((x) => x.id === battleId);
    if (!b) return;
    chooseBattle(this.galaxy, battleId, 'tactical');
    const player = this.opts.player;
    const opts: BattleOptions = {
      player,
      enemies: [b.attacker === player ? b.defender : b.attacker],
      mode: 'campaign',
      campaign: { world: b.world, battleId, planet: this.galaxy.planets[b.planetId].name, attacker: b.attacker, defender: b.defender },
    };
    this.save();
    this.scene.sleep();
    this.game.scene.start('background');
    this.game.scene.start('battle', opts);
  }

  autoResolve(battleId: number): void {
    chooseBattle(this.galaxy, battleId, 'auto');
  }

  /** The player left a battle they were commanding: it plays on by itself. */
  delegateBattle(battleId: number): void {
    delegateBattle(this.galaxy, battleId);
    this.save();
  }

  /** Called by the battle view when a tactical battle ends. */
  finishBattle(battleId: number, result: BattleResult): void {
    concludeBattle(this.galaxy, battleId, result);
    this.save();
  }

  update(_time: number, delta: number): void {
    const g = this.galaxy;
    if (!this.paused) {
      this.acc += Math.min(delta / 1000, 0.25) * this.speed;
      // A big battle resolving on its own can make a step slow: past the budget, the rest
      // carries over to the next frames rather than freezing this one.
      const deadline = performance.now() + STEP_BUDGET_MS;
      while (this.acc >= GALAXY_STEP && performance.now() < deadline) {
        stepGalaxy(g);
        this.acc -= GALAXY_STEP;
      }
      this.acc = Math.min(this.acc, 1);
    }
    for (const ev of g.events) this.hud.onEvent(ev);
    g.events.length = 0;
    // A battle commanded from a view that is no longer running has nobody to finish it.
    if (!this.game.scene.isActive('battle')) {
      for (const b of g.battles) if (b.status === 'tactical') b.status = 'pending';
    }
    // A battle waiting for the player is asked about whenever no other dialog is open: a
    // prompt pushed aside (by the help, or by a second battle at once) comes back, rather
    // than leaving the galaxy waiting forever.
    const pending = g.battles.find((b) => b.status === 'pending');
    if (pending && !g.winner && !this.hud.dialogOpen()) this.hud.promptBattle(pending);
    if (g.winner && !this.ended) {
      this.ended = true;
      this.hud.showEnd(g.winner === this.opts.player);
      clearCampaign();
    }
    this.saveTimer += delta;
    if (this.saveTimer >= AUTOSAVE_MS) this.save();
    for (const [i, t] of this.stars.entries()) t.tilePositionX += delta * 0.002 * (i + 1);
    this.draw();
    this.hud.update(delta);
  }

  private label(i: number, x: number, y: number, text: string, color: string): void {
    let t = this.labels[i];
    if (!t) {
      t = this.add.text(0, 0, '', { fontFamily: 'ui-monospace, monospace', fontSize: '13px', fontStyle: 'bold' }).setOrigin(0.5);
      this.labels.push(t);
    }
    t.setVisible(true).setPosition(x, y).setText(text).setColor(color);
  }

  /** Fleet marker: a triangle pointing along `angle`, with the ship count next to it. */
  private fleetMarker(n: number, x: number, y: number, angle: number, faction: FactionId, count: number): number {
    const c = FACTIONS[faction].color;
    const r = 8;
    const pt = (a: number, d: number) => [x + Math.cos(angle + a) * d, y + Math.sin(angle + a) * d] as const;
    const [ax, ay] = pt(0, r);
    const [bx, by] = pt(2.5, r);
    const [cx, cy] = pt(-2.5, r);
    this.over.fillStyle(c, 1).fillTriangle(ax, ay, bx, by, cx, cy);
    this.over.lineStyle(1, 0x0b0d12, 1).strokeTriangle(ax, ay, bx, by, cx, cy);
    this.label(n, x, y - 16, String(count), hex(c));
    return n + 1;
  }

  private draw(): void {
    const g = this.galaxy;
    const u = this.under.clear();
    const o = this.over.clear();
    const t = this.time.now / 1000;
    const player = this.opts.player;

    for (const [a, b] of g.lanes) {
      const pa = g.planets[a];
      const pb = g.planets[b];
      u.lineStyle(2, 0x2a3446, 1).lineBetween(this.sx(pa.x), this.sy(pa.y), this.sx(pb.x), this.sy(pb.y));
    }
    // Route preview from the selected planet to the hovered one, when the player has a fleet there.
    if (this.selected != null && this.hover != null && this.hover !== this.selected && fleetsAt(g, this.selected, player).length > 0) {
      const path = findPath(g, this.selected, this.hover) ?? [];
      let prev: Planet = g.planets[this.selected];
      for (const id of path) {
        const next = g.planets[id];
        u.lineStyle(3, ACCENT, 0.8).lineBetween(this.sx(prev.x), this.sy(prev.y), this.sx(next.x), this.sy(next.y));
        prev = next;
      }
    }

    // Fog of war: ownership is known everywhere, the rest only where the player has eyes.
    const seen = visiblePlanets(g, player);
    for (const p of g.planets) {
      const x = this.sx(p.x);
      const y = this.sy(p.y);
      const r = this.radius(p);
      const visible = seen.has(p.id);
      this.planets[p.id].setAlpha(visible ? 1 : 0.45);
      o.lineStyle(3, p.owner ? FACTIONS[p.owner].color : NEUTRAL, visible ? 1 : 0.5).strokeCircle(x, y, r + 3);
      if (p.id === this.selected) o.lineStyle(2, ACCENT, 1).strokeCircle(x, y, r + 9);
      else if (p.id === this.hover) o.lineStyle(1, ACCENT, 0.6).strokeCircle(x, y, r + 9);
      const badge = this.emblems[p.id];
      badge.setVisible(p.owner != null);
      if (p.owner && badge.texture.key !== `emblem-${p.owner}`) badge.setTexture(`emblem-${p.owner}`);
      if (!visible) continue;
      if (p.occupation) {
        const total = p.owner ? GALAXY_RULES.captureTime : GALAXY_RULES.neutralCaptureTime;
        const done = 1 - p.occupation.remaining / total;
        o.lineStyle(4, FACTIONS[p.occupation.faction].color, 1)
          .beginPath().arc(x, y, r + 7, -Math.PI / 2, -Math.PI / 2 + done * Math.PI * 2).strokePath();
      }
      if (g.battles.some((b) => b.planetId === p.id)) {
        o.lineStyle(3, 0xe8705f, 0.5 + 0.5 * Math.sin(t * 6)).strokeCircle(x, y, r + 14);
      }
      // One pip per station level, above the planet.
      for (let i = 0; i < p.station; i++) o.fillStyle(0xd9dee7, 1).fillRect(x - p.station * 4 + i * 8, y - r - 12, 6, 6);
    }

    let n = 0;
    for (const f of g.fleets.values()) {
      if (!fleetVisible(g, player, f, seen)) continue;
      const count = countShips(f.ships) + f.heroes.length;
      if (f.at != null) {
        const p = g.planets[f.at];
        const side = f.faction === p.owner || (p.owner == null && f.faction === player) ? -1 : 1;
        n = this.fleetMarker(n, this.sx(p.x) + side * (this.radius(p) + 18), this.sy(p.y), side < 0 ? 0 : Math.PI, f.faction, count);
      } else if (f.to != null) {
        const a = g.planets[f.from];
        const b = g.planets[f.to];
        const x = this.sx(a.x + (b.x - a.x) * f.progress);
        const y = this.sy(a.y + (b.y - a.y) * f.progress);
        n = this.fleetMarker(n, x, y, Math.atan2(b.y - a.y, b.x - a.x), f.faction, count);
      }
    }
    for (let i = n; i < this.labels.length; i++) this.labels[i].setVisible(false);
  }
}
