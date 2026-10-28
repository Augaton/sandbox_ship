import Phaser from 'phaser';
import { FACTIONS, type FactionId } from '../data/units';
import { STATION } from '../data/structures';
import { SelectionController } from '../input/selection';
import { asteroidKeys, lookFor, projectileKey, type Look, type Manifest } from '../render/sprites';
import { battleOutcome, updateBattleAI, type BattleResult } from '../sim/battle';
import { createSkirmish } from '../sim/scenario';
import { STEP, step } from '../sim/step';
import { stationOf, type Entity, type World } from '../sim/world';
import { Hud } from '../ui/hud';
import type { BackgroundScene } from './BackgroundScene';
import { showStartMenu } from './flow';
import type { GalaxyScene } from './GalaxyScene';

/** A battle of the galaxy campaign, fought in this view. */
export interface CampaignBattle {
  world: World;
  battleId: number;
  planet: string;
  attacker: FactionId;
  defender: FactionId;
}

export interface BattleOptions {
  player: FactionId;
  enemy: FactionId;
  mode: 'skirmish' | 'sandbox' | 'campaign';
  campaign?: CampaignBattle;
}

interface View {
  root: Phaser.GameObjects.Container;
  engine: Phaser.GameObjects.Sprite | null;
  look: Look;
}

/** Sprites face up; a sim angle of 0 points right. */
const FACING = Math.PI / 2;
const MAX_STEPS_PER_FRAME = 20;
const MAX_SPARKS = 120;
const DEPTH = { field: 0, structure: 10, hardpoint: 12, ship: 20, shot: 30, fx: 40, overlay: 50 };

export class BattleScene extends Phaser.Scene {
  world!: World;
  opts!: BattleOptions;
  manifest!: Manifest;
  hud!: Hud;
  controls!: SelectionController;
  selected = new Set<number>();
  speed = 1;
  paused = false;
  private acc = 0;
  private views = new Map<number, View>();
  private shots = new Map<number, Phaser.GameObjects.Image>();
  private shotPool: Phaser.GameObjects.Image[] = [];
  private sparks = 0;
  private overlay!: Phaser.GameObjects.Graphics;
  /** Set once a campaign battle is decided. */
  private result: BattleResult | null = null;

  constructor() {
    super('battle');
  }

  create(opts: BattleOptions): void {
    this.opts = opts;
    this.manifest = this.cache.json.get('manifest') ?? {};
    const sandbox = opts.mode === 'sandbox';
    this.world = opts.campaign?.world ?? createSkirmish({
      player: opts.player, enemy: opts.enemy, enemyAI: !sandbox,
      credits: sandbox ? 50000 : undefined, seed: (Math.random() * 1e9) | 0,
    });
    this.result = null;
    this.selected = new Set();
    this.views = new Map();
    this.shots = new Map();
    this.shotPool = [];
    this.sparks = 0;
    this.speed = 1;
    this.paused = false;
    this.acc = 0;

    this.drawFields();
    this.overlay = this.add.graphics().setDepth(DEPTH.overlay);
    const cam = this.cameras.main;
    cam.setBounds(-600, -600, this.world.width + 1200, this.world.height + 1200);
    const home = stationOf(this.world, opts.player) ?? this.fleetCenter(opts.player);
    cam.centerOn(home.x, home.y);

    this.controls = new SelectionController(this);
    this.hud = new Hud(this, { sandbox, campaign: opts.campaign?.planet ?? null });
    this.events.once('shutdown', () => this.hud.destroy());
  }

  private fleetCenter(faction: FactionId): { x: number; y: number } {
    const ships = [...this.world.entities.values()].filter((e) => e.faction === faction);
    if (ships.length === 0) return { x: this.world.width / 2, y: this.world.height / 2 };
    return {
      x: ships.reduce((s, e) => s + e.x, 0) / ships.length,
      y: ships.reduce((s, e) => s + e.y, 0) / ships.length,
    };
  }

  update(_time: number, delta: number): void {
    if (!this.paused) {
      this.acc += Math.min(delta / 1000, 0.1) * this.speed;
      let n = 0;
      while (this.acc >= STEP && n < MAX_STEPS_PER_FRAME) {
        step(this.world);
        if (this.opts.campaign) updateBattleAI(this.world, [this.opts.enemy], STEP);
        this.acc -= STEP;
        n++;
      }
      // Too slow to keep up: drop the backlog rather than spiral.
      if (n === MAX_STEPS_PER_FRAME) this.acc = 0;
    }
    this.checkCampaignEnd();
    this.controls.update(delta);
    this.consumeEvents();
    this.syncViews();
    this.syncShots();
    this.drawOverlay();
    const cam = this.cameras.main;
    (this.scene.get('background') as BackgroundScene).sync(cam.scrollX, cam.scrollY);
    this.hud.update(delta);
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

  restart(): void {
    this.scene.restart(this.opts);
  }

  toMenu(): void {
    showStartMenu(this.game);
  }

  private checkCampaignEnd(): void {
    const c = this.opts.campaign;
    if (!c || this.result) return;
    const result = battleOutcome(this.world, c.attacker, c.defender);
    if (!result) return;
    this.result = result;
    const winner = result === 'attacker' ? c.attacker : c.defender;
    this.hud.showBattleEnd(winner === this.opts.player, () => this.returnToGalaxy());
  }

  /** The attacker can fall back: its surviving ships return to the planet they came from. */
  retreat(): void {
    if (this.opts.campaign?.attacker !== this.opts.player || this.result) return;
    this.result = 'retreat';
    this.returnToGalaxy();
  }

  private returnToGalaxy(): void {
    const c = this.opts.campaign!;
    const game = this.game;
    game.scene.stop('battle');
    game.scene.stop('background');
    (game.scene.getScene('galaxy') as GalaxyScene).finishBattle(c.battleId, this.result!);
    game.scene.wake('galaxy');
  }

  /** Brief ring where an order was given. */
  marker(x: number, y: number, color: number): void {
    const g = this.add.graphics().setDepth(DEPTH.fx);
    const zoom = this.cameras.main.zoom;
    g.lineStyle(2 / zoom, color, 1).strokeCircle(0, 0, 14 / zoom).setPosition(x, y);
    this.tweens.add({ targets: g, scale: 0.3, alpha: 0, duration: 350, onComplete: () => g.destroy() });
  }

  private drawFields(): void {
    const keys = asteroidKeys(this.manifest);
    const g = this.add.graphics().setDepth(DEPTH.field);
    let seed = 1;
    const rnd = () => ((seed = (seed * 16807) % 2147483647) / 2147483647);
    for (const f of this.world.fields) {
      g.lineStyle(2, 0x8a94a6, 0.15).strokeCircle(f.x, f.y, f.radius);
      const n = Math.round(f.radius / 14);
      for (let i = 0; i < n; i++) {
        const a = rnd() * Math.PI * 2;
        const d = Math.sqrt(rnd()) * f.radius * 0.9;
        this.add.image(f.x + Math.cos(a) * d, f.y + Math.sin(a) * d, keys[i % keys.length])
          .setRotation(rnd() * Math.PI * 2)
          .setScale(0.6 + rnd() * 0.7)
          .setDepth(DEPTH.field);
      }
    }
  }

  private ensureAnim(key: string, repeat: number): string {
    if (!this.anims.exists(key)) {
      this.anims.create({ key, frames: this.anims.generateFrameNumbers(key), frameRate: 12, repeat });
    }
    return key;
  }

  private layer(key: string, size: number, tint: number | null): Phaser.GameObjects.Sprite {
    const s = this.add.sprite(0, 0, key);
    s.setScale(size / Math.max(s.width, s.height));
    if (tint != null) s.setTint(tint);
    if (this.manifest[key]?.frames > 1) s.play(this.ensureAnim(key, -1));
    return s;
  }

  private createView(e: Entity): View {
    const look = lookFor(this.manifest, e);
    const size = e.radius * 2 * look.fit;
    const tint = look.tint ? FACTIONS[e.faction].color : null;
    const children: Phaser.GameObjects.Sprite[] = [];
    const engine = look.engine ? this.layer(look.engine, size, null) : null;
    if (engine) children.push(engine);
    for (const key of look.layers) children.push(this.layer(key, size, tint));
    const root = this.add.container(e.x, e.y, children).setDepth(DEPTH[e.kind] + e.radius / 100);
    const view = { root, engine, look };
    this.views.set(e.id, view);
    return view;
  }

  private syncViews(): void {
    for (const e of this.world.entities.values()) {
      const v = this.views.get(e.id) ?? this.createView(e);
      v.root.setPosition(e.x, e.y);
      if (e.kind === 'ship') {
        v.root.rotation = e.angle + FACING;
      } else if (e.kind === 'hardpoint' && e.targetId != null) {
        // Turrets track their target.
        const t = this.world.entities.get(e.targetId);
        if (t) v.root.rotation = Math.atan2(t.y - e.y, t.x - e.x) + FACING;
      }
      v.root.alpha = e.build < 1 ? 0.4 + 0.4 * e.build : 1;
      v.engine?.setVisible(e.speed > 5);
    }
    for (const [id, v] of this.views) {
      if (this.world.entities.has(id)) continue;
      v.root.destroy();
      this.views.delete(id);
    }
  }

  private syncShots(): void {
    const alive = new Set<number>();
    for (const p of this.world.projectiles) {
      alive.add(p.id);
      let img = this.shots.get(p.id);
      if (!img) {
        const { key, tint } = projectileKey(this.manifest, p.faction, p.kind);
        img = this.shotPool.pop()?.setActive(true).setVisible(true) ?? this.add.image(0, 0, key).setDepth(DEPTH.shot);
        img.setTexture(key);
        if (tint) img.setTint(FACTIONS[p.faction].color);
        else img.clearTint();
        this.shots.set(p.id, img);
      }
      img.setPosition(p.x, p.y).setRotation(Math.atan2(p.vy, p.vx) + FACING);
    }
    for (const [id, img] of this.shots) {
      if (alive.has(id)) continue;
      this.shots.delete(id);
      this.shotPool.push(img.setActive(false).setVisible(false));
    }
  }

  private consumeEvents(): void {
    const w = this.world;
    for (const ev of w.events) {
      if (ev.type === 'destroyed') {
        this.explode(ev.id, ev.x, ev.y, ev.radius);
        if (ev.faction === this.opts.player && ev.kind !== 'ship') this.hud.lost(ev.entityType);
      } else if (ev.type === 'hit') {
        this.spark(ev.x, ev.y);
      } else if (ev.type === 'built') {
        if (w.entities.get(ev.id)?.faction === this.opts.player) this.hud.toast('Construction terminée');
      } else if (ev.type === 'victory') {
        this.hud.showEnd(ev.faction === this.opts.player);
      }
    }
    w.events.length = 0;
  }

  private explode(id: number, x: number, y: number, radius: number): void {
    const v = this.views.get(id);
    const anim = v?.look.destruction;
    if (anim) {
      const s = this.add.sprite(x, y, anim).setDepth(DEPTH.fx).setRotation(v.root.rotation);
      s.setScale((radius * 2 * v.look.fit) / Math.max(s.width, s.height));
      s.play(this.ensureAnim(anim, 0));
      s.once('animationcomplete', () => s.destroy());
      return;
    }
    const boom = this.add.image(x, y, 'ph-boom').setDepth(DEPTH.fx).setScale((radius * 2.5) / 64);
    this.tweens.add({ targets: boom, scale: boom.scale * 1.8, alpha: 0, duration: 450 + radius * 4, onComplete: () => boom.destroy() });
    if (radius >= STATION.radius) this.cameras.main.shake(400, 0.006);
  }

  private spark(x: number, y: number): void {
    if (this.sparks >= MAX_SPARKS) return;
    this.sparks++;
    const s = this.add.image(x, y, 'ph-spark').setDepth(DEPTH.fx).setScale(1.5);
    this.tweens.add({
      targets: s, alpha: 0, scale: 0.5, duration: 160,
      onComplete: () => {
        s.destroy();
        this.sparks--;
      },
    });
  }

  private drawOverlay(): void {
    const g = this.overlay.clear();
    const cam = this.cameras.main;
    const z = cam.zoom;
    const view = cam.worldView;
    const player = this.opts.player;

    for (const e of this.world.entities.values()) {
      const r = e.radius + 12 / z;
      if (e.x + r < view.x || e.x - r > view.right || e.y + r < view.y || e.y - r > view.bottom) continue;
      const sel = this.selected.has(e.id);
      const color = FACTIONS[e.faction].color;
      if (e.kind !== 'ship') g.lineStyle(1.5 / z, color, 0.45).strokeCircle(e.x, e.y, e.radius + 2 / z);
      if (sel) g.lineStyle(2 / z, e.faction === player ? 0x6fd08c : 0xe05a4a, 0.9).strokeCircle(e.x, e.y, e.radius + 5 / z);
      if (sel && e.order) {
        const t = e.order.type === 'attack' ? this.world.entities.get(e.order.targetId) : e.order;
        const c = e.order.type === 'move' ? 0x6fd08c : e.order.type === 'attack' ? 0xe05a4a : 0xe8a13a;
        if (t) g.lineStyle(1 / z, c, 0.5).lineBetween(e.x, e.y, t.x, t.y);
      }
      const damaged = e.hull < e.maxHull || e.shield < e.maxShield;
      if (sel || damaged || e.build < 1) this.bars(g, e, z);
    }

    const station = stationOf(this.world, player);
    const ghost = this.controls.placementGhost();
    if (station && (ghost || this.selected.has(station.id))) {
      g.lineStyle(1.5 / z, 0xe8a13a, 0.35).strokeCircle(station.x, station.y, STATION.constructionRadius);
    }
    if (station && this.selected.has(station.id)) {
      const f = this.world.factions.get(player)!;
      g.lineStyle(1 / z, 0x6fd08c, 0.6).lineBetween(station.x, station.y, f.rallyX, f.rallyY);
      g.fillStyle(0x6fd08c, 0.8).fillCircle(f.rallyX, f.rallyY, 5 / z);
    }
    if (ghost) {
      const c = ghost.ok ? 0x6fd08c : 0xe05a4a;
      g.fillStyle(c, 0.2).fillCircle(ghost.x, ghost.y, ghost.radius);
      g.lineStyle(2 / z, c, 0.9).strokeCircle(ghost.x, ghost.y, ghost.radius);
    }
    const b = this.controls.box;
    if (b) {
      g.fillStyle(0x6fd08c, 0.08).fillRect(Math.min(b.x0, b.x1), Math.min(b.y0, b.y1), Math.abs(b.x1 - b.x0), Math.abs(b.y1 - b.y0));
      g.lineStyle(1 / z, 0x6fd08c, 0.8).strokeRect(Math.min(b.x0, b.x1), Math.min(b.y0, b.y1), Math.abs(b.x1 - b.x0), Math.abs(b.y1 - b.y0));
    }
  }

  /** Hull bar (green to red), shield bar above it, construction progress below. */
  private bars(g: Phaser.GameObjects.Graphics, e: Entity, z: number): void {
    const w = Math.max(24 / z, e.radius * 1.6);
    const h = 3 / z;
    const x = e.x - w / 2;
    let y = e.y - e.radius - 8 / z;
    const hull = Math.max(0, e.hull / e.maxHull);
    g.fillStyle(0x0b0d12, 0.8).fillRect(x - 1 / z, y - 1 / z, w + 2 / z, h + 2 / z);
    g.fillStyle(hull > 0.5 ? 0x6fd08c : hull > 0.25 ? 0xe8c14a : 0xe05a4a, 1).fillRect(x, y, w * hull, h);
    if (e.maxShield > 0) {
      y -= h + 1 / z;
      g.fillStyle(0x0b0d12, 0.8).fillRect(x - 1 / z, y - 1 / z, w + 2 / z, h + 1 / z);
      g.fillStyle(0x5aa9e6, 1).fillRect(x, y, w * (e.shield / e.maxShield), h);
    }
    if (e.build < 1) {
      const by = e.y + e.radius + 6 / z;
      g.fillStyle(0x0b0d12, 0.8).fillRect(x, by, w, h);
      g.fillStyle(0xe8a13a, 1).fillRect(x, by, w * e.build, h);
    }
  }
}
