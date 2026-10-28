import Phaser from 'phaser';
import { ABILITIES, COMMAND_RADIUS, type AbilityId } from '../data/abilities';
import { play, type Sound } from '../audio/sfx';
import { isNews, type Alert } from '../ui/alerts';
import { FACTIONS, type FactionId } from '../data/units';
import { STATION } from '../data/structures';
import { SelectionController } from '../input/selection';
import { opaqueSize } from '../render/opaque';
import { factionKey } from '../render/recolor';
import { asteroidKeys, lookFor, projectileKey, type Layer, type Look, type Manifest } from '../render/sprites';
import { STALEMATE, battleOutcome, updateBattleAI, type BattleResult } from '../sim/battle';
import { HEROES } from '../data/heroes';
import { ownerOf } from '../sim/abilities';
import { aimedTarget } from '../sim/combat';
import { buildZones } from '../sim/structures';
import { isVisible, visionOf, wasSeen } from '../sim/vision';
import { createSkirmish, type MapSize } from '../sim/scenario';
import { STEP, step } from '../sim/step';
import { stationOf, type Difficulty, type Entity, type Projectile, type Strike, type World } from '../sim/world';
import { hex } from '../ui/dom';
import { Hud, typeName } from '../ui/hud';
import { settings } from '../ui/settings';
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
  /** Everyone else: one or two factions, each against all. */
  enemies: FactionId[];
  mode: 'skirmish' | 'sandbox' | 'campaign';
  campaign?: CampaignBattle;
  /** Map of a skirmish or sandbox game. */
  seed?: number;
  map?: 'classic' | 'random';
  size?: MapSize;
  difficulty?: Difficulty;
}

interface View {
  root: Phaser.GameObjects.Container;
  engine: Phaser.GameObjects.Sprite | null;
  look: Look;
  /** Scale of the first layer, shared by the engine and destruction animations. */
  scale: number;
  /** Name shown above a hero. */
  label: Phaser.GameObjects.Text | null;
  /** Milliseconds before the next puff of smoke, once badly damaged. */
  smoke: number;
}

/** Sprites face up; a sim angle of 0 points right. */
const FACING = Math.PI / 2;
const MAX_STEPS_PER_FRAME = 20;
const MAX_SPARKS = 120;
/** Ability names floating up at once, at most. */
const MAX_CALLOUTS = 10;
/** Below this share of its hull, a ship or structure smokes. */
const SMOKING = 0.35;
const SHIELD_FLASH_MS = 250;
const SHOT_SOUND: Record<Projectile['kind'], Sound> = { bullet: 'bullet', laser: 'laser', torpedo: 'torpedo', ray: 'ray' };
const DEPTH = { field: 0, structure: 10, hardpoint: 12, ship: 20, shot: 30, fx: 40, fog: 45, overlay: 50 };
const FOG_BRUSH = 256;
/**
 * World pixels per fog texel. The veil lives in world space, so the camera moves and zooms
 * it exactly like everything else; drawn small and stretched with smoothing, its edges blur.
 */
const FOG_CELL = 16;
/** Room around the map that the camera can show, as in its bounds. */
const MARGIN = 600;
/**
 * Detail levels by zoom. Below `simple`, engine flames, ship turrets and sparks go and shots
 * become strokes; below `icons`, small craft become chevrons. Fewer, simpler things to draw
 * when the whole battle is on screen, and easier to read too.
 */
const LOD = { simple: 0.5, icons: 0.35 };

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
  private shots = new Map<number, Phaser.GameObjects.Sprite>();
  private shotPool: Phaser.GameObjects.Sprite[] = [];
  private sparks = 0;
  private overlay!: Phaser.GameObjects.Graphics;
  /** Strokes and chevrons standing in for shots and small craft when zoomed out; under the fog. */
  private lodGfx!: Phaser.GameObjects.Graphics;
  /** What abilities do, where they act: zones, blows charging up, effects running. */
  private fx!: Phaser.GameObjects.Graphics;
  private callouts = 0;
  private trails!: Phaser.GameObjects.Graphics;
  private debris!: Phaser.GameObjects.Particles.ParticleEmitter;
  private smoke!: Phaser.GameObjects.Particles.ParticleEmitter;
  /** When each shield last took a hit, by entity id: a bubble lights up around it. */
  private shieldFlash = new Map<number, number>();
  /** Highest projectile id already heard: newer ones are new shots. */
  private lastShot = 0;
  /** Attacks and losses, newest last; Backspace goes back through them. */
  alerts: Alert[] = [];
  private alertCursor = -1;
  /** Recent callouts: a group casting the same ability at once gets a single one. */
  private recent: { id: AbilityId; x: number; y: number; time: number }[] = [];
  private fog!: Phaser.GameObjects.RenderTexture;
  private fogBrush!: Phaser.GameObjects.Image;
  /** Set once a campaign battle is decided. */
  private result: BattleResult | null = null;
  /** The player's station fell while others fight on. */
  private defeated = false;
  private stalemateWarned = false;

  constructor() {
    super('battle');
  }

  create(opts: BattleOptions): void {
    this.opts = opts;
    this.manifest = this.cache.json.get('manifest') ?? {};
    const sandbox = opts.mode === 'sandbox';
    this.world = opts.campaign?.world ?? createSkirmish({
      player: opts.player, enemies: opts.enemies, enemyAI: !sandbox,
      credits: sandbox ? 50000 : undefined, seed: opts.seed ?? ((Math.random() * 1e9) | 0),
      map: opts.map ?? 'random', size: opts.size, fog: !sandbox, difficulty: opts.difficulty,
    });
    this.result = null;
    this.defeated = false;
    this.stalemateWarned = false;
    // The player's ships use their abilities by themselves if the player wants so.
    const own = this.world.factions.get(opts.player);
    if (own) own.autoAbilities = settings.autoAbilities;
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
    this.lodGfx = this.add.graphics().setDepth(DEPTH.shot);
    this.fx = this.add.graphics().setDepth(DEPTH.fx);
    this.trails = this.add.graphics().setDepth(DEPTH.ship - 1);
    this.debris = this.add.particles(0, 0, 'ph-debris', {
      lifespan: { min: 500, max: 1400 }, speed: { min: 60, max: 320 }, scale: { start: 1.1, end: 0.3 },
      alpha: { start: 1, end: 0 }, rotate: { min: 0, max: 360 }, emitting: false,
    }).setDepth(DEPTH.fx);
    this.smoke = this.add.particles(0, 0, 'ph-smoke', {
      lifespan: 1600, speed: { min: 8, max: 34 }, scale: { start: 0.8, end: 2.4 }, alpha: { start: 0.7, end: 0 }, emitting: false,
    }).setDepth(DEPTH.fx - 1);
    this.shieldFlash = new Map();
    this.lastShot = 0;
    this.alerts = [];
    this.alertCursor = -1;
    this.callouts = 0;
    this.recent = [];
    // A dark veil over the whole map, with holes where the player's units see.
    const fogW = Math.ceil((this.world.width + 2 * MARGIN) / FOG_CELL);
    const fogH = Math.ceil((this.world.height + 2 * MARGIN) / FOG_CELL);
    this.fog = this.add.renderTexture(-MARGIN, -MARGIN, fogW, fogH).setOrigin(0).setScale(FOG_CELL).setDepth(DEPTH.fog);
    this.fog.texture.setFilter(Phaser.Textures.FilterMode.LINEAR);
    this.fogBrush = this.make.image({ key: 'fog-brush', add: false });
    const cam = this.cameras.main;
    cam.setBounds(-MARGIN, -MARGIN, this.world.width + 2 * MARGIN, this.world.height + 2 * MARGIN);
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
        if (this.opts.campaign) updateBattleAI(this.world, this.opts.enemies, STEP);
        this.acc -= STEP;
        n++;
      }
      // Too slow to keep up: drop the backlog rather than spiral.
      if (n === MAX_STEPS_PER_FRAME) this.acc = 0;
    }
    this.checkCampaignEnd();
    this.controls.update(delta);
    // Clamp now, as Phaser would just before drawing, so that what follows the camera this
    // frame (the backdrop) uses the scroll the battle is drawn with.
    const view = this.cameras.main;
    view.setScroll(view.clampX(view.scrollX), view.clampY(view.scrollY));
    this.lodGfx.clear();
    this.consumeEvents();
    this.syncViews();
    this.syncShots();
    this.drawEffects();
    this.drawOverlay();
    this.drawFog();
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

  /** Starts the battle again, on the same map or on a new one. */
  restart(newMap = false): void {
    this.scene.restart(newMap ? { ...this.opts, seed: (Math.random() * 1e9) | 0 } : this.opts);
  }

  toMenu(): void {
    showStartMenu(this.game);
  }

  private checkCampaignEnd(): void {
    const c = this.opts.campaign;
    if (!c || this.result) return;
    // Warn before a battle where nobody hits anything ends on its own.
    const quiet = this.world.time - this.world.lastDamage;
    if (quiet > STALEMATE - 30 && !this.stalemateWarned) {
      this.stalemateWarned = true;
      this.hud.toast("Aucun tir depuis longtemps : dans 30 s, l'attaquant se replie");
    } else if (quiet < STALEMATE - 30) this.stalemateWarned = false;
    const result = battleOutcome(this.world, c.attacker, c.defender);
    if (!result) return;
    this.result = result;
    const winner = result === 'attacker' ? c.attacker : c.defender;
    this.hud.showBattleEnd(winner === this.opts.player, () => this.returnToGalaxy());
  }

  /** Leaves a campaign battle to play on by itself, back on the galaxy map. */
  delegate(): void {
    const c = this.opts.campaign;
    if (!c || this.result) return;
    const game = this.game;
    game.scene.stop('battle');
    game.scene.stop('background');
    (game.scene.getScene('galaxy') as GalaxyScene).delegateBattle(c.battleId);
    game.scene.wake('galaxy');
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
        const key = keys[i % keys.length];
        this.add.image(f.x + Math.cos(a) * d, f.y + Math.sin(a) * d, key)
          .setRotation(rnd() * Math.PI * 2)
          .setScale((20 + rnd() * 25) / opaqueSize(this, key))
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

  private layer(key: string, scale: number, tint: number | null): Phaser.GameObjects.Sprite {
    const s = this.add.sprite(0, 0, key).setScale(scale);
    if (tint != null) s.setTint(tint);
    if (this.manifest[key]?.frames > 1) s.play(this.ensureAnim(key, -1));
    return s;
  }

  /** Scale that gives the visible part of a layer its intended size on screen. */
  private layerScale(e: Entity, l: Layer): number {
    return (e.radius * 2 * l.size) / opaqueSize(this, l.key);
  }

  private createView(e: Entity): View {
    const look = lookFor(this.manifest, e);
    const tint = look.tint ? FACTIONS[e.faction].color : null;
    const scale = this.layerScale(e, look.layers[0]);
    const children: Phaser.GameObjects.Sprite[] = [];
    const engine = look.engine ? this.layer(look.engine, scale, null) : null;
    if (engine) children.push(engine);
    // Structures and parts come from one pack, in each faction's colors.
    for (const l of look.layers) {
      const key = e.kind === 'ship' ? l.key : factionKey(this, l.key, e.faction);
      children.push(this.layer(key, this.layerScale(e, { ...l, key }), tint));
    }
    // Ship parts sit on top of their ship.
    const parent = e.parentId == null ? undefined : this.world.entities.get(e.parentId);
    const depth = parent?.kind === 'ship' ? DEPTH.ship + 1 + parent.radius / 100 : DEPTH[e.kind] + e.radius / 100;
    const root = this.add.container(e.x, e.y, children).setDepth(depth);
    const label = e.hero
      ? this.add.text(0, 0, HEROES[e.hero].name, { fontFamily: 'ui-monospace, monospace', fontSize: '13px', color: '#e8a13a' })
        .setOrigin(0.5, 1).setDepth(DEPTH.overlay)
      : null;
    const view = { root, engine, look, scale, label, smoke: 0 };
    this.views.set(e.id, view);
    return view;
  }

  private syncViews(): void {
    const cam = this.cameras.main;
    const z = cam.zoom;
    const view = this.viewRect();
    const simple = z < LOD.simple;
    const icons = z < LOD.icons;
    const g = this.lodGfx;
    for (const e of this.world.entities.values()) {
      const v = this.views.get(e.id) ?? this.createView(e);
      // Off screen, or a detail too small to matter at this zoom: not drawn at all.
      const r = e.radius + 40;
      const onScreen = e.x + r > view.x && e.x - r < view.right && e.y + r > view.y && e.y - r < view.bottom;
      const shipPart = e.kind === 'hardpoint' && e.parentId != null && this.world.entities.get(e.parentId)?.kind === 'ship';
      const chevron = icons && e.kind === 'ship' && e.size === 'small';
      if (!onScreen || (simple && shipPart) || chevron) {
        v.root.setVisible(false);
        v.label?.setVisible(false);
        if (chevron && onScreen && this.canSee(e)) this.chevron(g, e, z);
        continue;
      }
      v.root.setPosition(e.x, e.y);
      if (e.kind === 'ship') {
        v.root.rotation = e.angle + FACING;
      } else if (e.kind === 'hardpoint') {
        // Turrets track their target; ship parts otherwise turn with their ship.
        const t = e.weapons.length > 0 && e.targetId != null ? this.world.entities.get(e.targetId) : undefined;
        if (t) v.root.rotation = Math.atan2(t.y - e.y, t.x - e.x) + FACING;
        else if (shipPart) v.root.rotation = e.angle + FACING;
      }
      // Enemies out of sight are hidden; structures seen before stay, greyed out.
      const seen = this.canSee(e);
      const remembered = !seen && e.kind !== 'ship' && wasSeen(this.world, this.opts.player, e);
      v.root.setVisible(seen || remembered);
      const cloaked = e.ability?.id === 'cloak' && e.ability.active > 0;
      v.root.alpha = remembered ? 0.45 : cloaked ? 0.35 : e.build < 1 ? 0.4 + 0.4 * e.build : 1;
      v.engine?.setVisible(e.speed > 5 && !simple);
      if (!simple && seen && e.kind !== 'hardpoint' && e.build >= 1 && e.hull < e.maxHull * SMOKING) {
        v.smoke -= this.game.loop.delta;
        if (v.smoke <= 0) {
          v.smoke = 160;
          const r = e.radius * 0.5;
          this.smoke.emitParticleAt(e.x + (Math.random() - 0.5) * r, e.y + (Math.random() - 0.5) * r, e.radius > 30 ? 2 : 1);
        }
      }
      v.label?.setVisible(seen).setPosition(e.x, e.y - e.radius - 14 / z).setScale(1 / z);
    }
    for (const [id, v] of this.views) {
      if (this.world.entities.has(id)) continue;
      v.root.destroy();
      v.label?.destroy();
      this.views.delete(id);
    }
  }

  /**
   * The world rectangle on screen this frame. The camera's own `worldView` is only updated
   * when it draws, a frame late: while zooming out, things at the edges would pop in late.
   */
  private viewRect(): { x: number; y: number; right: number; bottom: number } {
    const cam = this.cameras.main;
    const hw = cam.width / 2 / cam.zoom;
    const hh = cam.height / 2 / cam.zoom;
    const cx = cam.scrollX + cam.width / 2;
    const cy = cam.scrollY + cam.height / 2;
    return { x: cx - hw, y: cy - hh, right: cx + hw, bottom: cy + hh };
  }

  /** A small craft seen from afar: a chevron in its faction's color, pointing where it flies. */
  private chevron(g: Phaser.GameObjects.Graphics, e: Entity, z: number): void {
    const s = 5 / z;
    const c = Math.cos(e.angle);
    const n = Math.sin(e.angle);
    g.fillStyle(FACTIONS[e.faction].color, 1).fillTriangle(
      e.x + c * s * 1.4, e.y + n * s * 1.4,
      e.x - c * s + n * s, e.y - n * s - c * s,
      e.x - c * s - n * s, e.y - n * s + c * s,
    );
  }

  private syncShots(): void {
    const alive = new Set<number>();
    let newest = this.lastShot;
    for (const p of this.world.projectiles) {
      if (p.id <= this.lastShot) continue;
      newest = Math.max(newest, p.id);
      this.sfx(SHOT_SOUND[p.kind], p.x, p.y, 0.7);
    }
    this.lastShot = newest;
    if (this.cameras.main.zoom < LOD.simple) {
      // Zoomed out: shots are strokes along their path, all in one batch.
      const g = this.lodGfx;
      const z = this.cameras.main.zoom;
      for (const p of this.world.projectiles) {
        g.lineStyle(1.5 / z, FACTIONS[p.faction].color, 0.9).lineBetween(p.x, p.y, p.x - p.vx * 0.04, p.y - p.vy * 0.04);
      }
    } else {
      for (const p of this.world.projectiles) this.shotSprite(p, alive);
    }
    for (const [id, img] of this.shots) {
      if (alive.has(id)) continue;
      this.shots.delete(id);
      this.shotPool.push(img.setActive(false).setVisible(false));
    }
  }

  private shotSprite(p: Projectile, alive: Set<number>): void {
    alive.add(p.id);
    let img = this.shots.get(p.id);
    if (!img) {
      const { key, tint } = projectileKey(this.manifest, p.faction, p.kind);
      img = this.shotPool.pop()?.setActive(true).setVisible(true) ?? this.add.sprite(0, 0, key).setDepth(DEPTH.shot);
      img.setTexture(key);
      if (this.manifest[key]?.frames > 1) img.play(this.ensureAnim(key, -1));
      else img.stop();
      if (tint) img.setTint(FACTIONS[p.faction].color);
      else img.clearTint();
      this.shots.set(p.id, img);
    }
    img.setPosition(p.x, p.y).setRotation(Math.atan2(p.vy, p.vx) + FACING);
  }

  private consumeEvents(): void {
    const w = this.world;
    for (const ev of w.events) {
      if (ev.type === 'destroyed') {
        this.explode(ev.id, ev.x, ev.y, ev.radius);
        this.sfx(ev.radius >= 50 ? 'bigExplosion' : 'explosion', ev.x, ev.y, Math.min(1, 0.4 + ev.radius / 60));
        if (ev.faction === this.opts.player && ev.kind !== 'ship') this.alert(`Perte : ${typeName(ev.entityType)}`, ev.x, ev.y, true);
        // Out of a free-for-all before it ends: say so now, the others may fight on for long.
        if (ev.entityType === 'station' && ev.faction === this.opts.player && !this.opts.campaign && !w.winner && !this.defeated) {
          this.defeated = true;
          this.hud.showEnd(false);
        }
        else if (ev.faction === this.opts.player && !this.onScreen(ev.x, ev.y)) this.alert(`Perte : ${typeName(ev.entityType)}`, ev.x, ev.y);
      } else if (ev.type === 'hit') {
        this.spark(ev.x, ev.y);
        if (ev.shieldId != null) this.shieldFlash.set(ev.shieldId, this.time.now);
        this.sfx(ev.shieldId != null ? 'shield' : 'hit', ev.x, ev.y, 0.6);
        if (ev.faction === this.opts.player) this.underFire(ev.targetId, ev.x, ev.y);
      } else if (ev.type === 'built') {
        const e = w.entities.get(ev.id);
        if (e?.faction === this.opts.player) {
          this.hud.toast('Construction terminée');
          play('built');
        }
      } else if (ev.type === 'victory') {
        if (!this.defeated) this.hud.showEnd(ev.faction === this.opts.player);
      } else if (ev.type === 'rebuilt') {
        if (ev.faction === this.opts.player) this.hud.toast(`${typeName(ev.role)} reconstruit`);
      } else if (ev.type === 'ability') {
        const e = w.entities.get(ev.entityId);
        if (e && this.canSee(e)) {
          this.abilityUsed(e, ev.id);
          this.sfx('ability', e.x, e.y);
        }
      } else if (ev.type === 'strike') {
        this.strikeLanded(ev.strike);
        this.sfx(ev.strike.kind === 'orbital' ? 'bigExplosion' : 'beam', ev.strike.x2, ev.strike.y2);
      }
    }
    w.events.length = 0;
  }

  /** Plays a sound from (x, y): panned to its side of the screen, and only if in view. */
  private sfx(s: Sound, x: number, y: number, gain = 1): void {
    const v = this.viewRect();
    const w = v.right - v.x;
    const h = v.bottom - v.y;
    if (x < v.x - w * 0.1 || x > v.right + w * 0.1 || y < v.y - h * 0.1 || y > v.bottom + h * 0.1) return;
    // Seen from afar, a battle is quieter.
    play(s, (((x - v.x) / w) * 2 - 1) * 0.8, gain * Math.min(1, 0.45 + this.cameras.main.zoom * 0.55));
  }

  private onScreen(x: number, y: number): boolean {
    const v = this.viewRect();
    return x >= v.x && x <= v.right && y >= v.y && y <= v.bottom;
  }

  /** One of the player's things took a hit: structures always raise an alert, ships only out of sight. */
  private underFire(id: number, x: number, y: number): void {
    const t = this.world.entities.get(id);
    if (!t) return;
    const owner = ownerOf(this.world, t);
    if (owner.kind === 'structure') this.alert(`Attaque : ${typeName(owner.type)}`, x, y, true);
    else if (!this.onScreen(x, y)) this.alert('Attaque : tes vaisseaux', x, y);
  }

  /** Reports an attack or a loss, unless one was reported near there lately. */
  private alert(text: string, x: number, y: number, always = false): void {
    const now = this.time.now;
    if (!isNews(this.alerts, x, y, now) && !(always && text.startsWith('Perte'))) return;
    this.alerts.push({ text, x, y, time: now });
    if (this.alerts.length > 12) this.alerts.shift();
    this.alertCursor = -1;
    play('alert');
    this.hud.alert(text, () => this.cameras.main.centerOn(x, y));
  }

  /** Centers the view on the latest alert; again, on the one before, and so on. */
  jumpToAlert(): void {
    if (this.alerts.length === 0) {
      this.hud.toast('Aucune alerte');
      return;
    }
    this.alertCursor = this.alertCursor <= 0 ? this.alerts.length - 1 : this.alertCursor - 1;
    const a = this.alerts[this.alertCursor];
    this.cameras.main.centerOn(a.x, a.y);
  }

  /** The ability's name rises above the ship, and an area ability ripples out to its reach. */
  private abilityUsed(e: Entity, id: AbilityId): void {
    const def = ABILITIES[id];
    const z = this.cameras.main.zoom;
    const now = this.time.now;
    this.recent = this.recent.filter((r) => now - r.time < 600);
    const echo = this.recent.some((r) => r.id === id && Math.hypot(r.x - e.x, r.y - e.y) < 300);
    if (!echo) this.recent.push({ id, x: e.x, y: e.y, time: now });
    if (!echo && this.callouts < MAX_CALLOUTS) {
      this.callouts++;
      const text = this.add.text(e.x, e.y - e.radius - 10 / z, def.name, {
        fontFamily: 'ui-monospace, monospace', fontSize: '14px', color: hex(def.color), stroke: '#0b0d12', strokeThickness: 4,
      }).setOrigin(0.5, 1).setScale(1 / z).setDepth(DEPTH.overlay);
      this.tweens.add({
        targets: text, y: text.y - 40 / z, alpha: 0, duration: 1400, ease: 'Cubic.easeIn',
        onComplete: () => {
          text.destroy();
          this.callouts--;
        },
      });
    }
    if (def.target === 'self' && def.radius && def.duration === 0) this.ripple(e.x, e.y, def.radius, def.color);
  }

  private ripple(x: number, y: number, radius: number, color: number): void {
    const g = this.add.graphics().setDepth(DEPTH.fx).setPosition(x, y);
    g.lineStyle(4 / this.cameras.main.zoom, color, 0.9).strokeCircle(0, 0, radius);
    g.fillStyle(color, 0.12).fillCircle(0, 0, radius);
    g.setScale(0.2);
    this.tweens.add({ targets: g, scale: 1, alpha: 0, duration: 600, ease: 'Cubic.easeOut', onComplete: () => g.destroy() });
  }

  private strikeLanded(s: Strike): void {
    if (s.kind === 'orbital') {
      this.ripple(s.x, s.y, s.radius, 0xe05a4a);
      const boom = this.add.image(s.x, s.y, 'ph-boom').setDepth(DEPTH.fx).setScale((s.radius * 2) / 64);
      this.tweens.add({ targets: boom, scale: boom.scale * 1.4, alpha: 0, duration: 700, onComplete: () => boom.destroy() });
      this.cameras.main.shake(250, 0.004);
      return;
    }
    const g = this.add.graphics().setDepth(DEPTH.fx);
    g.lineStyle(s.radius * 2, 0xe8a13a, 0.7).lineBetween(s.x, s.y, s.x2, s.y2);
    g.lineStyle(s.radius * 0.8, 0xfff2d0, 1).lineBetween(s.x, s.y, s.x2, s.y2);
    this.tweens.add({ targets: g, alpha: 0, duration: 600, ease: 'Cubic.easeIn', onComplete: () => g.destroy() });
  }

  /** Zones, blows charging up and effects running, so that every ability shows what it does. */
  private drawEffects(): void {
    const g = this.fx.clear();
    const z = this.cameras.main.zoom;
    this.drawTrails(z);
    // A shield that just took a hit lights up as a bubble.
    const now = this.time.now;
    for (const [id, at] of this.shieldFlash) {
      const k = 1 - (now - at) / SHIELD_FLASH_MS;
      const e = this.world.entities.get(id);
      if (k <= 0 || !e) {
        this.shieldFlash.delete(id);
        continue;
      }
      if (!this.canSee(e)) continue;
      g.fillStyle(0x7cc4ff, 0.12 * k).fillCircle(e.x, e.y, e.radius + 5 / z);
      g.lineStyle(2 / z, 0x9fd4ff, 0.7 * k).strokeCircle(e.x, e.y, e.radius + 5 / z);
    }
    const pulse = 0.5 + 0.5 * Math.sin(this.time.now / 160);
    for (const zone of this.world.zones) {
      // An admiral's command shows only while the admiral is selected (see the overlay).
      if (zone.kind === 'command') continue;
      const color = zone.kind === 'dome' ? 0x5aa9e6 : 0x6fd08c;
      g.fillStyle(color, 0.07).fillCircle(zone.x, zone.y, zone.r);
      g.lineStyle(2 / z, color, 0.45 + 0.25 * pulse).strokeCircle(zone.x, zone.y, zone.r);
    }
    for (const s of this.world.strikes) {
      const k = 1 - s.delay / s.total;
      if (s.kind === 'orbital') {
        g.fillStyle(0xe05a4a, 0.06 + 0.18 * k).fillCircle(s.x, s.y, s.radius);
        g.lineStyle(2 / z, 0xe05a4a, 0.9).strokeCircle(s.x, s.y, s.radius).strokeCircle(s.x, s.y, s.radius * (1 - k));
      } else {
        g.lineStyle(Math.max(1.5 / z, s.radius * 0.5 * k), 0xe8a13a, 0.25 + 0.45 * pulse).lineBetween(s.x, s.y, s.x2, s.y2);
      }
    }
    const view = this.viewRect();
    for (const e of this.world.entities.values()) {
      if (e.x < view.x - e.radius || e.x > view.right + e.radius || e.y < view.y - e.radius || e.y > view.bottom + e.radius) continue;
      if (!this.canSee(e)) continue;
      const a = e.ability;
      if (a && a.active > 0) g.lineStyle(2 / z, ABILITIES[a.id].color, 0.4 + 0.5 * pulse).strokeCircle(e.x, e.y, e.radius + 7 / z);
      const fx = e.effects;
      const r = e.radius + 11 / z;
      if (fx.marked > 0) {
        g.lineStyle(2 / z, 0xe05a4a, 0.9).strokeCircle(e.x, e.y, r);
        for (let i = 0; i < 4; i++) {
          const c = Math.cos((i * Math.PI) / 2);
          const n = Math.sin((i * Math.PI) / 2);
          g.lineBetween(e.x + c * (r - 5 / z), e.y + n * (r - 5 / z), e.x + c * (r + 7 / z), e.y + n * (r + 7 / z));
        }
      }
      if (fx.stunned > 0) {
        // Crackling: a ring that flickers between two sizes.
        g.lineStyle(2 / z, 0x9fd4ff, 0.5 + 0.5 * pulse).strokeCircle(e.x, e.y, r + (pulse > 0.5 ? 3 : -2) / z);
      }
      if (fx.corroded > 0) {
        g.fillStyle(0x6fd08c, 0.25 + 0.2 * pulse).fillCircle(e.x, e.y, e.radius);
      }
    }
  }

  private explode(id: number, x: number, y: number, radius: number): void {
    this.debrisAt(x, y, radius);
    const v = this.views.get(id);
    const anim = v?.look.destruction;
    if (anim) {
      const s = this.add.sprite(x, y, anim).setDepth(DEPTH.fx).setRotation(v.root.rotation).setScale(v.scale);
      s.play(this.ensureAnim(anim, 0));
      s.once('animationcomplete', () => s.destroy());
      return;
    }
    const boom = this.add.image(x, y, 'ph-boom').setDepth(DEPTH.fx).setScale((radius * 2.5) / 64);
    this.tweens.add({ targets: boom, scale: boom.scale * 1.8, alpha: 0, duration: 450 + radius * 4, onComplete: () => boom.destroy() });
    if (radius >= STATION.radius) this.cameras.main.shake(400, 0.006);
  }

  /** Fragments and a flash: on top of the destruction animation, so that a kill reads at a glance. */
  private debrisAt(x: number, y: number, radius: number): void {
    if (this.cameras.main.zoom < LOD.simple || !this.onScreen(x, y)) return;
    this.debris.explode(Math.round(Math.min(24, Math.max(4, radius / 3))), x, y);
    const flash = this.add.image(x, y, 'ph-boom').setDepth(DEPTH.fx).setScale((radius * 1.6) / 64).setAlpha(0.8);
    this.tweens.add({ targets: flash, alpha: 0, scale: flash.scale * 1.3, duration: 260, onComplete: () => flash.destroy() });
  }

  private spark(x: number, y: number): void {
    if (this.sparks >= MAX_SPARKS || this.cameras.main.zoom < LOD.simple) return;
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
    const view = this.viewRect();
    const player = this.opts.player;

    for (const e of this.world.entities.values()) {
      const r = e.radius + 12 / z;
      if (e.x + r < view.x || e.x - r > view.right || e.y + r < view.y || e.y - r > view.bottom) continue;
      if (!this.canSee(e)) continue;
      const sel = this.selected.has(e.id);
      if (e.hero) g.lineStyle(1.5 / z, 0xe8a13a, 0.8).strokeCircle(e.x, e.y, e.radius + 9 / z);
      const color = FACTIONS[e.faction].color;
      if (e.kind !== 'ship') g.lineStyle(1.5 / z, color, 0.45).strokeCircle(e.x, e.y, e.radius + 2 / z);
      if (sel) g.lineStyle(2 / z, e.faction === player ? 0x6fd08c : 0xe05a4a, 0.9).strokeCircle(e.x, e.y, e.radius + 5 / z);
      if (sel && e.order) {
        const t = e.order.type === 'attack' ? this.world.entities.get(e.order.targetId) : e.order;
        const c = e.order.type === 'move' ? 0x6fd08c : e.order.type === 'attack' ? 0xe05a4a : 0xe8a13a;
        if (t) g.lineStyle(1 / z, c, 0.5).lineBetween(e.x, e.y, t.x, t.y);
      }
      const damaged = e.hull < e.maxHull || e.shield < e.maxShield;
      if (sel || damaged || e.build < 1 || settings.alwaysBars) this.bars(g, e, z);
    }

    // Ring under the cursor: green for your units, red for a target.
    const c = this.controls;
    const over = c.mode.kind === 'select' && c.pilotId == null && !c.box && c.pointerOver ? c.hovered() : null;
    if (over && !this.selected.has(over.id) && this.canSee(over)) {
      g.lineStyle(1.5 / z, over.faction === player ? 0x6fd08c : 0xe05a4a, 0.5).strokeCircle(over.x, over.y, over.radius + 5 / z);
    }

    const piloted = this.controls.pilotId == null ? undefined : this.world.entities.get(this.controls.pilotId);
    if (piloted?.pilot) {
      const p = piloted.pilot;
      const t = aimedTarget(this.world, piloted, p, piloted.range);
      g.lineStyle(2 / z, 0x6fd08c, 0.9).strokeCircle(piloted.x, piloted.y, piloted.radius + 6 / z);
      g.lineStyle(1.5 / z, 0xe8a13a, 0.9).strokeCircle(p.aimX, p.aimY, 10 / z)
        .lineBetween(p.aimX - 16 / z, p.aimY, p.aimX - 6 / z, p.aimY).lineBetween(p.aimX + 6 / z, p.aimY, p.aimX + 16 / z, p.aimY)
        .lineBetween(p.aimX, p.aimY - 16 / z, p.aimX, p.aimY - 6 / z).lineBetween(p.aimX, p.aimY + 6 / z, p.aimX, p.aimY + 16 / z);
      if (t) g.lineStyle(2 / z, 0xe05a4a, p.firing ? 1 : 0.6).strokeCircle(t.x, t.y, t.radius + 6 / z);
    }

    const mode = this.controls.mode;
    if (mode.kind === 'ability') this.drawAiming(g, mode.id, z);
    // An admiral's command, around the selected admirals.
    for (const id of this.selected) {
      const e = this.world.entities.get(id);
      if (e?.hero && HEROES[e.hero].role === 'admiral') g.lineStyle(1.5 / z, 0xe8a13a, 0.3).strokeCircle(e.x, e.y, COMMAND_RADIUS);
    }

    const station = stationOf(this.world, player);
    const ghost = this.controls.placementGhost();
    if (mode.kind === 'place' && mode.type !== 'mine') {
      for (const zone of buildZones(this.world, player, mode.type)) g.lineStyle(1.5 / z, 0xe8a13a, 0.35).strokeCircle(zone.x, zone.y, zone.r);
    } else if (station && this.selected.has(station.id)) {
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

  /** Engine trails behind moving ships, under the ships themselves. */
  private drawTrails(z: number): void {
    const g = this.trails.clear();
    if (z < LOD.simple) return;
    const view = this.viewRect();
    for (const e of this.world.entities.values()) {
      if (e.kind !== 'ship' || e.speed < 20 || !this.canSee(e)) continue;
      if (e.x < view.x || e.x > view.right || e.y < view.y || e.y > view.bottom) continue;
      const c = Math.cos(e.angle);
      const n = Math.sin(e.angle);
      const sx = e.x - c * e.radius * 0.7;
      const sy = e.y - n * e.radius * 0.7;
      const len = e.speed * 0.45;
      const w = Math.max(1.5 / z, e.radius * 0.22);
      g.lineStyle(w, 0x9fd4ff, 0.3).lineBetween(sx, sy, sx - c * len * 0.5, sy - n * len * 0.5);
      g.lineStyle(w * 0.6, 0x9fd4ff, 0.12).lineBetween(sx - c * len * 0.5, sy - n * len * 0.5, sx - c * len, sy - n * len);
    }
  }

  /** Reach of the casters, and where the ability would land under the cursor. */
  private drawAiming(g: Phaser.GameObjects.Graphics, id: AbilityId, z: number): void {
    const def = ABILITIES[id];
    const h = this.controls.hover;
    for (const s of this.controls.ownShips()) {
      if (s.ability?.id !== id) continue;
      if (def.range) g.lineStyle(1.5 / z, def.color, 0.35).strokeCircle(s.x, s.y, def.range);
      if (id === 'spinal') {
        const a = Math.atan2(h.y - s.y, h.x - s.x);
        g.lineStyle(80, def.color, 0.15).lineBetween(s.x, s.y, s.x + Math.cos(a) * def.range!, s.y + Math.sin(a) * def.range!);
      }
    }
    if (def.target === 'point' && def.radius) g.lineStyle(2 / z, def.color, 0.8).strokeCircle(h.x, h.y, def.radius);
    if (def.target === 'enemy') {
      const t = this.controls.hovered();
      if (t && t.faction !== this.opts.player) g.lineStyle(2 / z, def.color, 0.9).strokeCircle(t.x, t.y, t.radius + 10 / z);
    }
  }

  /** Whether the player sees this entity right now (always true for their own). */
  canSee(e: Entity): boolean {
    return isVisible(this.world, this.opts.player, e);
  }

  /** Dark veil over everything the player's ships and structures do not see. */
  private drawFog(): void {
    const fog = this.fog;
    fog.setVisible(this.world.fog);
    if (!this.world.fog) return;
    fog.clear().fill(0x05060a, 0.75);
    // Every hole in one batch, erased in a single pass.
    fog.beginDraw();
    for (const e of this.world.entities.values()) {
      if (e.faction !== this.opts.player) continue;
      const r = visionOf(e);
      if (r <= 0) continue;
      this.fogBrush.setScale((2 * r) / FOG_CELL / FOG_BRUSH);
      fog.batchDraw(this.fogBrush, (e.x + MARGIN) / FOG_CELL, (e.y + MARGIN) / FOG_CELL);
    }
    fog.endDraw(true);
  }

  /** Hull bar (green to red), shield bar above it, construction progress below. */
  private bars(g: Phaser.GameObjects.Graphics, e: Entity, z: number): void {
    const w = Math.max(28 / z, e.radius * 1.6);
    const h = 4 / z;
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
