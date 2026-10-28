// Sound effects, synthesised with Web Audio: oscillators and filtered noise, no sound files.
// Browsers only allow sound after a gesture, so the context starts on the first click or key.
import { settings } from '../ui/settings';

export type Sound =
  | 'bullet' | 'laser' | 'torpedo' | 'ray'
  | 'hit' | 'shield' | 'explosion' | 'bigExplosion'
  | 'ability' | 'beam' | 'alert' | 'built' | 'click';

/** Shortest gap between two plays of a sound, in ms: a battle fires hundreds of shots. */
const GAP: Record<Sound, number> = {
  bullet: 45, laser: 60, torpedo: 120, ray: 150, hit: 50, shield: 70, explosion: 90, bigExplosion: 250,
  ability: 120, beam: 300, alert: 1500, built: 400, click: 30,
};

/** Lets a sound through at most once per its gap. */
export class Limiter {
  private last = new Map<string, number>();
  constructor(private gaps: Record<string, number>) {}

  allow(sound: string, now: number): boolean {
    const t = this.last.get(sound);
    if (t != null && now - t < (this.gaps[sound] ?? 0)) return false;
    this.last.set(sound, now);
    return true;
  }
}

let ctx: AudioContext | null = null;
let master: GainNode | null = null;
let noise: AudioBuffer | null = null;
const limiter = new Limiter(GAP);

/** Starts the audio context; called on the first user gesture. */
export function unlockAudio(): void {
  if (!ctx) {
    try {
      ctx = new AudioContext();
    } catch {
      return;
    }
    master = ctx.createGain();
    master.connect(ctx.destination);
    noise = ctx.createBuffer(1, ctx.sampleRate, ctx.sampleRate);
    const data = noise.getChannelData(0);
    for (let i = 0; i < data.length; i++) data[i] = Math.random() * 2 - 1;
  }
  if (ctx.state === 'suspended') void ctx.resume();
}

/**
 * Plays a sound. `pan` goes from -1 (left) to 1 (right), `gain` scales it (distance,
 * size). Quiet when muted, and skipped when the same sound just played.
 */
export function play(sound: Sound, pan = 0, gain = 1): void {
  if (!ctx || !master || !noise || settings.muted || settings.volume <= 0 || ctx.state !== 'running') return;
  if (!limiter.allow(sound, performance.now())) return;
  master.gain.value = settings.volume;
  const out = ctx.createStereoPanner();
  out.pan.value = Math.max(-1, Math.min(1, pan));
  out.connect(master);
  const t = ctx.currentTime;
  const g = gain;
  switch (sound) {
    case 'bullet':
      burst(out, t, 0.05, 0.1 * g, 'bandpass', 2600, 2600);
      break;
    case 'laser':
      tone(out, t, 'square', 900, 220, 0.12, 0.05 * g, 3000);
      break;
    case 'torpedo':
      tone(out, t, 'sawtooth', 160, 60, 0.35, 0.07 * g, 900);
      burst(out, t, 0.3, 0.06 * g, 'lowpass', 800, 300);
      break;
    case 'ray':
      tone(out, t, 'sine', 1300, 260, 0.28, 0.1 * g);
      tone(out, t, 'triangle', 650, 130, 0.28, 0.06 * g);
      break;
    case 'hit':
      burst(out, t, 0.04, 0.06 * g, 'highpass', 1800, 1800);
      break;
    case 'shield':
      tone(out, t, 'sine', 700, 1300, 0.09, 0.04 * g);
      break;
    case 'explosion':
      burst(out, t, 0.6, 0.3 * g, 'lowpass', 1400, 150);
      tone(out, t, 'sine', 90, 35, 0.4, 0.25 * g);
      break;
    case 'bigExplosion':
      burst(out, t, 1.5, 0.45 * g, 'lowpass', 700, 80);
      tone(out, t, 'sine', 60, 22, 1.1, 0.4 * g);
      break;
    case 'ability':
      tone(out, t, 'sine', 523, 523, 0.18, 0.07 * g);
      tone(out, t + 0.08, 'sine', 784, 784, 0.22, 0.07 * g);
      break;
    case 'beam':
      tone(out, t, 'sawtooth', 220, 110, 0.8, 0.1 * g, 1600);
      burst(out, t, 0.8, 0.12 * g, 'bandpass', 900, 400);
      break;
    case 'alert':
      tone(out, t, 'square', 660, 660, 0.12, 0.05 * g, 2000);
      tone(out, t + 0.16, 'square', 440, 440, 0.16, 0.05 * g, 2000);
      break;
    case 'built':
      [523, 659, 784].forEach((f, i) => tone(out, t + i * 0.09, 'triangle', f, f, 0.18, 0.06 * g));
      break;
    case 'click':
      tone(out, t, 'sine', 1800, 1500, 0.03, 0.03 * g);
      break;
  }
}

/** An oscillator gliding from `from` to `to` Hz, fading out over `length` seconds. */
function tone(out: AudioNode, t: number, type: OscillatorType, from: number, to: number, length: number, volume: number, lowpass?: number): void {
  const c = ctx!;
  const osc = c.createOscillator();
  osc.type = type;
  osc.frequency.setValueAtTime(from, t);
  osc.frequency.exponentialRampToValueAtTime(Math.max(1, to), t + length);
  const env = envelope(t, length, volume);
  let node: AudioNode = osc;
  if (lowpass) {
    const f = c.createBiquadFilter();
    f.type = 'lowpass';
    f.frequency.value = lowpass;
    node.connect(f);
    node = f;
  }
  node.connect(env).connect(out);
  osc.start(t);
  osc.stop(t + length + 0.02);
}

/** Filtered noise whose filter glides from `from` to `to` Hz, fading out over `length`. */
function burst(out: AudioNode, t: number, length: number, volume: number, type: BiquadFilterType, from: number, to: number): void {
  const c = ctx!;
  const src = c.createBufferSource();
  src.buffer = noise;
  const f = c.createBiquadFilter();
  f.type = type;
  f.frequency.setValueAtTime(from, t);
  f.frequency.exponentialRampToValueAtTime(Math.max(1, to), t + length);
  src.connect(f).connect(envelope(t, length, volume)).connect(out);
  src.start(t, Math.random() * 0.5);
  src.stop(t + length + 0.02);
}

function envelope(t: number, length: number, volume: number): GainNode {
  const g = ctx!.createGain();
  g.gain.setValueAtTime(0.0001, t);
  g.gain.exponentialRampToValueAtTime(Math.max(0.0002, volume), t + 0.005);
  g.gain.exponentialRampToValueAtTime(0.0001, t + length);
  return g;
}
