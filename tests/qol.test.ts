import { describe, expect, it } from 'vitest';
import { Limiter } from '../src/audio/sfx';
import { shiftTrim } from '../src/render/recolor';
import { enqueueShip } from '../src/sim/structures';
import { SHIPS } from '../src/data/units';
import { run, step } from '../src/sim/step';
import { addFaction, createWorld, spawnShip, spawnStation, type SimEvent } from '../src/sim/world';
import { ALERT_GAP, isNews, type Alert } from '../src/ui/alerts';

describe('looped production', () => {
  it('puts the class back in the queue as each one comes out, until stopped', () => {
    const w = createWorld(6000, 4000);
    const f = addFaction(w, 'klaed', 10000);
    addFaction(w, 'nairan', 0);
    spawnStation(w, 'klaed', 700, 2000);
    f.repeat = ['frigate'];
    step(w);
    expect(f.queue).toHaveLength(1);
    run(w, SHIPS.frigate.buildTime * 2 + 1);
    const frigates = () => [...w.entities.values()].filter((e) => e.type === 'frigate' && e.faction === 'klaed').length;
    expect(frigates()).toBe(2);
    expect(f.queue).toHaveLength(1);
    f.repeat = [];
    run(w, SHIPS.frigate.buildTime + 1);
    expect(frigates()).toBe(3);
    expect(f.queue).toHaveLength(0);
  });

  it('waits when credits run short', () => {
    const w = createWorld(6000, 4000);
    const f = addFaction(w, 'klaed', 0);
    addFaction(w, 'nairan', 0);
    spawnStation(w, 'klaed', 700, 2000);
    f.repeat = ['frigate'];
    step(w);
    expect(f.queue).toHaveLength(0);
    f.credits = 1000;
    step(w);
    expect(f.queue).toHaveLength(1);
  });
});

describe('alerts', () => {
  it('one per spot until the dust settles', () => {
    const alerts: Alert[] = [{ text: 'Attaque', x: 1000, y: 1000, time: 0 }];
    expect(isNews(alerts, 1200, 1100, 5000)).toBe(false);
    expect(isNews(alerts, 3000, 1000, 5000)).toBe(true);
    expect(isNews(alerts, 1200, 1100, ALERT_GAP + 1)).toBe(true);
  });
});

describe('sounds', () => {
  it('a sound plays at most once per its gap, each sound on its own', () => {
    const l = new Limiter({ laser: 60, hit: 50 });
    expect(l.allow('laser', 0)).toBe(true);
    expect(l.allow('laser', 30)).toBe(false);
    expect(l.allow('hit', 30)).toBe(true);
    expect(l.allow('laser', 61)).toBe(true);
  });
});

describe('hits', () => {
  it('say what was hit, and whose shield took it', () => {
    const w = createWorld(6000, 4000);
    addFaction(w, 'klaed', 0);
    addFaction(w, 'nairan', 0);
    spawnShip(w, 'klaed', 'scout', 1000, 1000);
    const target = spawnShip(w, 'nairan', 'frigate', 1150, 1000);
    target.weapons = [];
    let hit: Extract<SimEvent, { type: 'hit' }> | undefined;
    for (let i = 0; i < 600 && !hit; i++) {
      step(w);
      hit = w.events.find((e): e is Extract<SimEvent, { type: 'hit' }> => e.type === 'hit');
      w.events.length = 0;
    }
    expect(hit?.faction).toBe('nairan');
    // On the frigate or one of its parts, the frigate's shield takes it.
    const struck = w.entities.get(hit!.targetId)!;
    expect(struck === target || struck.parentId === target.id).toBe(true);
    expect(hit!.shieldId).toBe(target.id);
  });
});

describe('battle statistics', () => {
  it('count ships built and lost, kills, damage and credits', () => {
    const w = createWorld(6000, 4000);
    const k = addFaction(w, 'klaed', 1000);
    const n = addFaction(w, 'nairan', 0);
    spawnStation(w, 'klaed', 700, 2000);
    enqueueShip(w, 'klaed', 'scout');
    run(w, SHIPS.scout.buildTime + 0.5);
    expect(k.stats.built).toBe(3);
    expect(k.stats.earned).toBeGreaterThan(0);
    // A fight far from the station's guns.
    spawnShip(w, 'klaed', 'scout', 4000, 2000);
    spawnShip(w, 'nairan', 'frigate', 4150, 2000);
    for (let i = 0; i < 60 * 60 && k.stats.lost === 0; i++) step(w);
    expect(k.stats.lost).toBeGreaterThan(0);
    expect(n.stats.kills).toBe(k.stats.lost);
    expect(n.stats.damage).toBeGreaterThan(0);
  });
});

describe('faction colors', () => {
  it('turn red trim to the faction hue, and leave greys and blue panels alone', () => {
    const px = new Uint8ClampedArray([200, 30, 30, 255, 128, 128, 128, 255, 60, 120, 220, 255]);
    shiftTrim(px, 120);
    const [r, g, b] = px.slice(0, 3);
    expect(g).toBeGreaterThan(r);
    expect(g).toBeGreaterThan(b);
    expect([...px.slice(4, 7)]).toEqual([128, 128, 128]);
    expect([...px.slice(8, 11)]).toEqual([60, 120, 220]);
  });
});
