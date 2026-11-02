// The music's instruments, rendered note by note into a stereo buffer as the sound effects are
// (Sfx), then through an echo and a reverb; a loop's tail is folded back onto its start, so that
// it plays on without a seam. Pure C#: it runs on a background thread while the game loads.
namespace SandboxShip.Core;

public sealed class MusicTrack
{
    /// <summary>Half the effects' rate: pads, basses and plucks have nothing above 11 kHz worth the memory.</summary>
    public const int Rate = 22050;

    /// <summary>The loop, in frames; past it, the tail that goes back onto its start.</summary>
    public readonly int Frames;
    readonly int total;
    readonly float[] l, r, echoL, echoR, roomL, roomR;
    readonly Random rng;
    /// <summary>The echo's time, in seconds, and how much of it comes back.</summary>
    readonly double echoTime, echoFeedback;
    /// <summary>The reverb's size (comb feedback) and how much of it is heard.</summary>
    readonly double roomSize, roomWet;

    public MusicTrack(double seconds, double tail, int seed, double echoTime = 0.5, double echoFeedback = 0.35, double roomSize = 0.84, double roomWet = 0.3)
    {
        Frames = (int)Math.Round(seconds * Rate);
        total = Frames + (int)Math.Round(tail * Rate);
        (l, r, echoL, echoR, roomL, roomR) = (new float[total], new float[total], new float[total], new float[total], new float[total], new float[total]);
        rng = new Random(seed);
        (this.echoTime, this.echoFeedback, this.roomSize, this.roomWet) = (echoTime, echoFeedback, roomSize, roomWet);
    }

    /// <summary>Between -amount and amount: a touch of life in timings and loudness, the same at every render.</summary>
    public double Jitter(double amount) => (rng.NextDouble() * 2 - 1) * amount;
    public double Chance() => rng.NextDouble();

    public static double Hz(double midi) => 440 * Math.Pow(2, (midi - 69) / 12);

    // Instruments. Each writes its note dry, and into the echo and the reverb by its sends.

    /// <summary>
    /// A pad: three saws a few cents apart, the low one left, the high one right, the middle one
    /// on both, each side through a lowpass that breathes slowly; it swells in over `attack` and
    /// dies out over `release` past `length`.
    /// </summary>
    public void Pad(double at, double length, double midi, double volume, double cutoff, double attack = 1.6, double release = 2.2, double room = 0.5)
    {
        var f = Hz(midi);
        var (h1, h2, h3) = (f * Math.Pow(2, -0.07 / 12), f, f * Math.Pow(2, 0.07 / 12));
        var (p1, p2, p3) = (rng.NextDouble(), rng.NextDouble(), rng.NextDouble());
        var (left, right) = (new Svf(0.7), new Svf(0.7));
        var breathe = rng.NextDouble() * Math.Tau;
        var (cut, tick) = (cutoff, 0);
        NoteStereo(at, length + release, 0, room, t =>
        {
            var env = Swell(t, length, attack, release) * volume / 2.2;
            if (env <= 0) return (0, 0);
            // The breath moves slowly: worked out every 32 samples.
            if ((tick++ & 31) == 0) cut = cutoff * (1 + 0.18 * Math.Sin(t * 0.7 + breathe));
            var (a, b, c) = (Saw(p1, h1), Saw(p2, h2), Saw(p3, h3));
            p1 = Wrap(p1 + h1 / Rate);
            p2 = Wrap(p2 + h2 / Rate);
            p3 = Wrap(p3 + h3 / Rate);
            return (left.Low(a + 0.6 * b, cut) * env, right.Low(c + 0.6 * b, cut) * env);
        });
    }

    /// <summary>A round bass: a sine and its octave, gently saturated.</summary>
    public void Bass(double at, double length, double midi, double volume, double release = 0.35)
    {
        var f = Hz(midi);
        var phase = 0.0;
        Note(at, length + release, 0, 0, 0.08, t =>
        {
            var env = Swell(t, length, 0.03, release) * volume;
            var x = Math.Sin(phase * Math.Tau) + 0.25 * Math.Sin(2 * phase * Math.Tau);
            phase = Wrap(phase + f / Rate);
            return Math.Tanh(x * 1.3) * env;
        });
    }

    /// <summary>A soft pluck: a triangle, a sine an octave up and a touch of saw for its edge, struck, ringing out over `decay`.</summary>
    public void Pluck(double at, double midi, double volume, double decay, double pan = 0, double echo = 0.3, double room = 0.35, double cutoff = 2600)
    {
        var f = Hz(midi);
        var (p1, p2) = (0.0, 0.0);
        var filter = new Svf(0.6);
        var ring = new Decay(0.004, decay);
        // Four and a half decays: some 40 dB down, then the note's last fade.
        Note(at, decay * 4.5, pan, echo, room, t =>
        {
            var env = ring.Next() * volume;
            var x = Triangle(p1) + 0.3 * Math.Sin(p2 * Math.Tau) + 0.25 * Saw(p1, f);
            p1 = Wrap(p1 + f / Rate);
            p2 = Wrap(p2 + 2 * f / Rate);
            return filter.Low(x, cutoff) * env;
        });
    }

    /// <summary>A bell: a sine and two partials out of tune with it, the higher ones dying first.</summary>
    public void Bell(double at, double midi, double volume, double pan = 0, double echo = 0.35, double room = 0.6)
    {
        var f = Hz(midi);
        var (p1, p2, p3) = (0.0, 0.0, 0.0);
        var (d1, d2, d3) = (new Decay(0.003, 1.6), new Decay(0.002, 0.9), new Decay(0.002, 0.4));
        Note(at, 8, pan, echo, room, t =>
        {
            var x = d1.Next() * Math.Sin(p1 * Math.Tau) + 0.35 * d2.Next() * Math.Sin(p2 * Math.Tau) + 0.15 * d3.Next() * Math.Sin(p3 * Math.Tau);
            p1 = Wrap(p1 + f / Rate);
            p2 = Wrap(p2 + 2.76 * f / Rate);
            p3 = Wrap(p3 + 5.4 * f / Rate);
            return x * volume;
        });
    }

    /// <summary>A driving saw bass: its filter snaps open on the note and closes behind it.</summary>
    public void SawBass(double at, double length, double midi, double volume, double open = 900)
    {
        var f = Hz(midi);
        var phase = 0.0;
        var filter = new Svf(1.4);
        Note(at, length + 0.15, 0, 0, 0.05, t =>
        {
            var env = Swell(t, length, 0.006, 0.12) * volume;
            var cut = 130 + open * Math.Exp(-t / 0.09);
            var x = Saw(phase, f);
            phase = Wrap(phase + f / Rate);
            return filter.Low(x, cut) * env;
        });
    }

    /// <summary>A brass stab: two saws a little apart, the filter opening as it is blown, then held.</summary>
    public void Brass(double at, double length, double midi, double volume, double pan = 0, double room = 0.45)
    {
        var f = Hz(midi);
        foreach (var (detune, side) in new[] { (-0.05, -0.4), (0.05, 0.4) })
        {
            var hz = f * Math.Pow(2, detune / 12);
            var phase = rng.NextDouble();
            var filter = new Svf(0.9);
            Note(at, length + 0.45, Math.Clamp(pan + side, -1, 1), 0.1, room, t =>
            {
                var env = Swell(t, length, 0.05, 0.4) * volume / 2;
                var cut = 500 + 1700 * Math.Min(1, t / 0.12) * (0.75 + 0.25 * Math.Exp(-t / 0.3));
                var x = Saw(phase, hz);
                phase = Wrap(phase + hz / Rate);
                return filter.Low(x, cut) * env;
            });
        }
    }

    /// <summary>A kick: a sine falling from 120 to 45 Hz, and the click of the beater.</summary>
    public void Kick(double at, double volume)
    {
        var phase = 0.0;
        Note(at, 0.9, 0, 0, 0.05, t =>
        {
            var hz = 45 + 75 * Math.Exp(-t / 0.035);
            phase = Wrap(phase + hz / Rate);
            return (Math.Sin(phase * Math.Tau) * Strike(t, 0.002, 0.16) + 0.3 * Strike(t, 0.0005, 0.004) * NoiseAt(t)) * volume;
        });
    }

    /// <summary>A snare: a short tone under a band of noise.</summary>
    public void Snare(double at, double volume, double pan = 0)
    {
        var phase = 0.0;
        var band = new Svf(0.9);
        Note(at, 0.4, pan, 0, 0.25, t =>
        {
            phase = Wrap(phase + 190.0 / Rate);
            var tone = Math.Sin(phase * Math.Tau) * Strike(t, 0.001, 0.05);
            var noise = band.Band(NoiseAt(t), 1900) * Strike(t, 0.001, 0.09) * 1.6;
            return (0.5 * tone + noise) * volume;
        });
    }

    /// <summary>A hat: a tick of high noise, longer when open.</summary>
    public void Hat(double at, double volume, double pan = 0, bool open = false)
    {
        var high = new Svf(0.7);
        Note(at, open ? 0.5 : 0.12, pan, 0, 0.15, t => high.High(NoiseAt(t), 6500) * Strike(t, 0.001, open ? 0.16 : 0.025) * volume);
    }

    /// <summary>A low tom, falling a little in pitch: the slow drum of a fleet coming.</summary>
    public void Tom(double at, double midi, double volume, double pan = 0)
    {
        var f = Hz(midi);
        var phase = 0.0;
        Note(at, 1.5, pan, 0, 0.4, t =>
        {
            phase = Wrap(phase + f * (1 + 0.4 * Math.Exp(-t / 0.05)) / Rate);
            return Math.Sin(phase * Math.Tau) * Strike(t, 0.003, 0.28) * volume;
        });
    }

    /// <summary>A drone under everything: a sine and a dark saw, swelling in and out.</summary>
    public void Drone(double at, double length, double midi, double volume, double cutoff = 320)
    {
        var f = Hz(midi);
        var (p1, p2) = (0.0, rng.NextDouble());
        var filter = new Svf(0.8);
        Note(at, length + 2, 0, 0, 0.4, t =>
        {
            var env = Swell(t, length, 2, 2) * volume;
            var x = 0.7 * Math.Sin(p1 * Math.Tau) + 0.5 * filter.Low(Saw(p2, f * 1.003), cutoff);
            p1 = Wrap(p1 + f / Rate);
            p2 = Wrap(p2 + f * 1.003 / Rate);
            return x * env;
        });
    }

    // The mix.

    /// <summary>
    /// Writes a note's samples from `at`, panned with equal power, and sends them to the echo and
    /// the reverb. A note running past the end of the buffer is cut there; past the loop, it lands
    /// in the tail that goes back onto the start.
    /// </summary>
    void Note(double at, double length, double pan, double echo, double room, Func<double, double> sample)
    {
        var a = (Math.Clamp(pan, -1, 1) + 1) * Math.PI / 4;
        var (gl, gr) = (Math.Cos(a), Math.Sin(a));
        NoteStereo(at, length, echo, room, t =>
        {
            var x = sample(t);
            return (x * gl, x * gr);
        });
    }

    /// <summary>A note whose samples come as a left and a right: placed in the stereo field by itself.</summary>
    void NoteStereo(double at, double length, double echo, double room, Func<double, (double L, double R)> sample)
    {
        var start = Math.Max(0, (int)Math.Round(at * Rate));
        var n = Math.Min(total - start, (int)Math.Ceiling(length * Rate));
        // Whatever is left of the note at the end of its window fades out in 30 ms, never a cut.
        var fade = Math.Min(n, (int)(0.03 * Rate));
        for (var i = 0; i < n; i++)
        {
            var (sl, sr) = sample((double)i / Rate);
            var (xl, xr) = ((float)sl, (float)sr);
            if (i >= n - fade)
            {
                var g = (float)(0.5 + 0.5 * Math.Cos(Math.PI * (i - (n - fade) + 1) / fade));
                (xl, xr) = (xl * g, xr * g);
            }
            var k = start + i;
            l[k] += xl;
            r[k] += xr;
            if (echo > 0)
            {
                echoL[k] += xl * (float)echo;
                echoR[k] += xr * (float)echo;
            }
            if (room > 0)
            {
                roomL[k] += xl * (float)room;
                roomR[k] += xr * (float)room;
            }
        }
    }

    /// <summary>
    /// The echo (ping-pong, darker at each repeat, into the reverb too), the reverb, then the tail
    /// folded onto the start: the loop as one stereo buffer of `Frames` frames, unscaled.
    /// </summary>
    public (float[] L, float[] R) Mix(bool loop)
    {
        var d = (int)Math.Round(echoTime * Rate);
        var (dl, dr) = (new float[total], new float[total]);
        double dampL = 0, dampR = 0;
        for (var i = 0; i < total; i++)
        {
            var backL = i >= d ? dr[i - d] : 0;
            var backR = i >= d ? dl[i - d] : 0;
            dampL += 0.45 * (backL - dampL);
            dampR += 0.45 * (backR - dampR);
            dl[i] = echoL[i] + (float)(dampL * echoFeedback);
            dr[i] = echoR[i] + (float)(dampR * echoFeedback);
            l[i] += dl[i] * 0.6f;
            r[i] += dr[i] * 0.6f;
            roomL[i] += dl[i] * 0.4f;
            roomR[i] += dr[i] * 0.4f;
        }
        // The echo's sends are spent: the reverb writes over them rather than into new buffers.
        Reverb(roomL, 0, echoL);
        Reverb(roomR, 23, echoR);
        for (var i = 0; i < total; i++)
        {
            l[i] += (float)(echoL[i] * roomWet);
            r[i] += (float)(echoR[i] * roomWet);
        }
        var frames = loop ? Frames : total;
        var (ol, or) = (new float[frames], new float[frames]);
        for (var i = 0; i < total; i++)
        {
            var k = loop ? i % Frames : i;
            ol[k] += l[i];
            or[k] += r[i];
        }
        return (ol, or);
    }

    /// <summary>A plain Schroeder reverb into `output`: four damped combs side by side, then two allpasses; `spread` sets the right channel apart.</summary>
    void Reverb(float[] input, int spread, float[] output)
    {
        int[] combs = [558, 594, 638, 678];
        int[] passes = [278, 220];
        Array.Clear(output);
        foreach (var c in combs)
        {
            var size = c + spread;
            var buffer = new double[size];
            var (index, store) = (0, 0.0);
            for (var i = 0; i < total; i++)
            {
                var y = buffer[index];
                store = y * 0.8 + store * 0.2;
                buffer[index] = input[i] + store * roomSize;
                if (++index == size) index = 0;
                output[i] += (float)(y * 0.25);
            }
        }
        foreach (var p in passes)
        {
            var size = p + spread;
            var buffer = new double[size];
            var index = 0;
            for (var i = 0; i < total; i++)
            {
                var b = buffer[index];
                var y = -output[i] + b;
                buffer[index] = output[i] + b * 0.5;
                if (++index == size) index = 0;
                output[i] = (float)y;
            }
        }
    }

    /// <summary>Frames of a loop's start copied past its end: Godot's resampling reads a few samples beyond the loop's end, which must be its start, not silence.</summary>
    public const int LoopGuard = 64;

    /// <summary>
    /// A stereo buffer as 16-bit PCM, scaled by `gain`, with a gentle limiter above 0.9 so that
    /// nothing clips; a loop gets its start again past its end (LoopGuard).
    /// </summary>
    public static byte[] Pcm(float[] left, float[] right, double gain, bool loop)
    {
        var frames = left.Length + (loop ? LoopGuard : 0);
        var data = new byte[frames * 4];
        for (var i = 0; i < frames; i++)
        {
            var k = i % left.Length;
            Write(data, i * 4, Limit(left[k] * gain));
            Write(data, i * 4 + 2, Limit(right[k] * gain));
        }
        return data;
    }

    static double Limit(double x)
    {
        const double Knee = 0.9;
        var a = Math.Abs(x);
        return a <= Knee ? x : Math.Sign(x) * (Knee + (1 - Knee) * Math.Tanh((a - Knee) / (1 - Knee)));
    }

    static void Write(byte[] data, int at, double v)
    {
        var n = (short)Math.Round(Math.Clamp(v, -1, 1) * short.MaxValue);
        data[at] = (byte)n;
        data[at + 1] = (byte)(n >> 8);
    }

    // Building blocks.

    /// <summary>Up over `attack` (a quarter sine), held, then down over `release` past `length`.</summary>
    static double Swell(double t, double length, double attack, double release)
    {
        if (t < attack) return Math.Sin(t / attack * Math.PI / 2);
        if (t < length) return 1;
        var k = (t - length) / release;
        return k >= 1 ? 0 : Math.Cos(k * Math.PI / 2);
    }

    /// <summary>Struck: up over `attack`, then an exponential decay with time constant `decay`.</summary>
    static double Strike(double t, double attack, double decay) => t < attack ? t / attack : Math.Exp(-(t - attack) / decay);

    /// <summary>Strike, sample after sample, by a multiplication rather than an exponential each time: for the long notes.</summary>
    sealed class Decay(double attack, double decay)
    {
        readonly int rise = Math.Max(1, (int)(attack * Rate));
        readonly double fall = Math.Exp(-1 / (decay * Rate));
        int n;
        double level = 1;

        public double Next()
        {
            if (n < rise) return (double)n++ / rise;
            var x = level;
            level *= fall;
            return x;
        }
    }

    static double Wrap(double phase) => phase - Math.Floor(phase);

    static double Saw(double phase, double hz)
    {
        var dt = hz / Rate;
        var s = Wrap(phase + 0.5);
        return 2 * s - 1 - Blep(s, dt);
    }

    static double Triangle(double phase) => phase < 0.25 ? 4 * phase : phase < 0.75 ? 2 - 4 * phase : 4 * phase - 4;

    static double Blep(double t, double dt)
    {
        if (t < dt)
        {
            t /= dt;
            return t + t - t * t - 1;
        }
        if (t > 1 - dt)
        {
            t = (t - 1) / dt;
            return t * t + t + t + 1;
        }
        return 0;
    }

    /// <summary>Two seconds of white noise, shared: a note reads it from its own time, so two hits of a drum sound alike.</summary>
    static readonly float[] Noise = MakeNoise();

    static float[] MakeNoise()
    {
        var r = new Random(3);
        var noise = new float[Rate * 2];
        for (var i = 0; i < noise.Length; i++) noise[i] = (float)(r.NextDouble() * 2 - 1);
        return noise;
    }

    static double NoiseAt(double t) => Noise[(int)(t * Rate) % Noise.Length];

    /// <summary>A state-variable filter (the trapezoidal form): steady while its cutoff moves.</summary>
    sealed class Svf(double q)
    {
        double ic1, ic2, lastCut = -1, a1, a2, a3;
        readonly double k = 1 / q;

        void Tune(double cutoff)
        {
            if (Math.Abs(cutoff - lastCut) < 0.5) return;
            lastCut = cutoff;
            var g = Math.Tan(Math.PI * Math.Clamp(cutoff, 20, Rate * 0.45) / Rate);
            a1 = 1 / (1 + g * (g + k));
            a2 = g * a1;
            a3 = g * a2;
        }

        (double Low, double Band, double High) Step(double x, double cutoff)
        {
            Tune(cutoff);
            var v3 = x - ic2;
            var v1 = a1 * ic1 + a2 * v3;
            var v2 = ic2 + a2 * ic1 + a3 * v3;
            ic1 = 2 * v1 - ic1;
            ic2 = 2 * v2 - ic2;
            return (v2, v1, x - k * v1 - v2);
        }

        public double Low(double x, double cutoff) => Step(x, cutoff).Low;
        public double Band(double x, double cutoff) => Step(x, cutoff).Band;
        public double High(double x, double cutoff) => Step(x, cutoff).High;
    }
}
