// Sound effects, synthesised in code: oscillators and filtered noise, no sound files. Port of
// src/audio/sfx.ts. The web version builds each sound with Web Audio nodes on every play;
// here each one is rendered once to 16-bit PCM with the same waveforms, glides, filters and
// envelopes, in stereo copies at a few pan positions, and played through a pool of players.
using Godot;

namespace SandboxShip.Core;

public enum Sound
{
    Bullet, Laser, Torpedo, Ray, Hit, Shield, Explosion, BigExplosion, Ability, Beam, Alert, Built, Click,
    // Not in the web version: a portal opening, and the horn that greets a titan or a leviathan.
    Warp, Horn,
    // This version's weapons: a burst cannon's round, a swarm missile leaving, an ion bolt, a round
    // of point defense meeting what it shot at.
    Burst, Missile, Ion, Intercept,
}

/// <summary>Lets a sound through at most once per its gap.</summary>
public sealed class Limiter(IReadOnlyDictionary<Sound, double> gaps)
{
    readonly Dictionary<Sound, double> last = [];

    public bool Allow(Sound sound, double now)
    {
        if (last.TryGetValue(sound, out var t) && now - t < gaps.GetValueOrDefault(sound)) return false;
        last[sound] = now;
        return true;
    }
}

public static class Sfx
{
    /// <summary>Shortest gap between two plays of a sound, in ms: a battle fires hundreds of shots.</summary>
    static readonly Dictionary<Sound, double> Gap = new()
    {
        [Sound.Bullet] = 45, [Sound.Laser] = 60, [Sound.Torpedo] = 120, [Sound.Ray] = 150, [Sound.Hit] = 50, [Sound.Shield] = 70,
        [Sound.Explosion] = 90, [Sound.BigExplosion] = 250, [Sound.Ability] = 120, [Sound.Beam] = 300, [Sound.Alert] = 1500,
        [Sound.Built] = 400, [Sound.Click] = 30, [Sound.Warp] = 400, [Sound.Horn] = 2500,
        [Sound.Burst] = 40, [Sound.Missile] = 70, [Sound.Ion] = 90, [Sound.Intercept] = 60,
    };

    const int Rate = 44100;
    /// <summary>Stereo copies of each sound, from hard left to hard right.</summary>
    const int PanSteps = 9;
    /// <summary>Sounds at once; past that, the oldest is cut.</summary>
    const int Voices = 32;

    static readonly Limiter limiter = new(Gap);
    static readonly Dictionary<Sound, float[]> rendered = [];
    static readonly Dictionary<(Sound, int), AudioStreamWav> streams = [];
    static readonly AudioStreamPlayer[] players = new AudioStreamPlayer[Voices];
    static readonly ulong[] startedAt = new ulong[Voices];
    static bool attached;

    /// <summary>
    /// The held hum of continuous beams: one loop on two players of its own, one heard on the left
    /// and one on the right, panned by their volumes so that it moves without a click.
    /// </summary>
    static readonly AudioStreamPlayer[] hum = new AudioStreamPlayer[2];
    static double humLevel, humPan;
    /// <summary>The hum's loop, in seconds: every partial makes whole cycles in it, so it joins up seamlessly.</summary>
    const double HumLength = 1;

    /// <summary>
    /// Creates the players under `host`, which stays for the whole session, and gives every
    /// button the click sound, as the web version does for any button clicked.
    /// </summary>
    public static void Attach(Node host)
    {
        if (attached) return;
        attached = true;
        var node = new Node { Name = "Sfx", ProcessMode = Node.ProcessModeEnum.Always };
        host.AddChild(node);
        for (var i = 0; i < Voices; i++)
        {
            players[i] = new AudioStreamPlayer();
            node.AddChild(players[i]);
        }
        var loop = RenderHum();
        for (var i = 0; i < hum.Length; i++)
        {
            hum[i] = new AudioStreamPlayer { Stream = HumStream(loop, right: i == 1) };
            node.AddChild(hum[i]);
        }
        foreach (var s in Enum.GetValues<Sound>()) rendered[s] = Render(s);
        // Every stereo copy now, while loading: made on first play, each one stalled a frame of
        // the battle (a long sound takes milliseconds) and left a large array to the collector.
        foreach (var s in Enum.GetValues<Sound>()) for (var step = 0; step < PanSteps; step++) Stream(s, step);
        host.GetTree().NodeAdded += n =>
        {
            if (n is Button b and not (CheckBox or CheckButton or OptionButton or MenuButton)) b.Pressed += () => Play(Sound.Click);
        };
    }

    /// <summary>
    /// Plays a sound, panned from -1 (left) to 1 (right), at a share of the volume (distance,
    /// size). Quiet when muted, and skipped when the same sound just played.
    /// </summary>
    public static void Play(Sound sound, double pan = 0, double gain = 1)
    {
        var settings = Settings.Current;
        if (!attached || settings.Muted || settings.Volume <= 0) return;
        if (!limiter.Allow(sound, Time.GetTicksMsec())) return;
        var level = settings.Volume * gain;
        if (level <= 0) return;
        var step = (int)Math.Round((Math.Clamp(pan, -1, 1) + 1) / 2 * (PanSteps - 1));
        var voice = 0;
        for (var i = 0; i < Voices; i++)
        {
            if (!players[i].Playing)
            {
                voice = i;
                break;
            }
            if (startedAt[i] < startedAt[voice]) voice = i;
        }
        var p = players[voice];
        p.Stream = Stream(sound, step);
        p.VolumeDb = Mathf.LinearToDb((float)level);
        p.Play();
        startedAt[voice] = Time.GetTicksMsec();
    }

    /// <summary>
    /// The beams' hum, held as long as it is asked for every frame: `level` 0 to 1 (0 lets it fade
    /// out), panned from -1 (left) to 1 (right). It eases to what is asked within a tenth of a
    /// second or so, never jumping; quiet when muted.
    /// </summary>
    public static void Hum(double level, double pan, double delta)
    {
        if (!attached) return;
        var settings = Settings.Current;
        var target = settings.Muted ? 0 : Math.Clamp(level, 0, 1) * settings.Volume;
        var ease = 1 - Math.Exp(-delta * 10);
        humLevel += (target - humLevel) * ease;
        humPan += (Math.Clamp(pan, -1, 1) - humPan) * ease;
        if (target == 0 && humLevel < 0.002)
        {
            StopHum();
            return;
        }
        var x = (humPan + 1) / 2;
        for (var i = 0; i < hum.Length; i++)
        {
            var gain = humLevel * (i == 0 ? Math.Cos(x * Math.PI / 2) : Math.Sin(x * Math.PI / 2));
            hum[i].VolumeDb = Mathf.LinearToDb((float)Math.Max(gain, 1e-5));
        }
        // Both started together, they stay in step.
        if (!hum[0].Playing)
        {
            foreach (var p in hum) p.Play();
        }
    }

    /// <summary>Silences the hum at once: the battle is gone or paused.</summary>
    public static void StopHum()
    {
        if (!attached) return;
        humLevel = 0;
        foreach (var p in hum) p.Stop();
    }

    /// <summary>One side of the hum's loop: its samples in one channel, silence in the other.</summary>
    static AudioStreamWav HumStream(float[] loop, bool right)
    {
        var data = new byte[loop.Length * 4];
        for (var i = 0; i < loop.Length; i++) Write(data, i * 4 + (right ? 2 : 0), loop[i]);
        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = true, Data = data,
            LoopMode = AudioStreamWav.LoopModeEnum.Forward, LoopBegin = 0, LoopEnd = loop.Length,
        };
    }

    /// <summary>
    /// The hum: two saws a fifth apart (110 and 165 Hz) and a soft 440 Hz, under a 6 Hz throb, low
    /// passed. Rendered twice over and the second second kept: the filter has settled by then, and
    /// every partial making whole cycles in a second, its end meets its start.
    /// </summary>
    static float[] RenderHum()
    {
        var n = (int)(HumLength * Rate);
        var buffer = new float[2 * n];
        var lowpass = new Biquad(Filter.Lowpass, 900);
        for (var i = 0; i < buffer.Length; i++)
        {
            var t = (double)i / Rate;
            var x = 0.6 * Oscillator(Wave.Sawtooth, 110 * t % 1, 110.0 / Rate) + 0.35 * Oscillator(Wave.Sawtooth, 165 * t % 1, 165.0 / Rate)
                + 0.25 * Math.Sin(Math.Tau * 440 * t);
            var throb = 0.8 + 0.2 * Math.Sin(Math.Tau * 6 * t);
            buffer[i] = (float)(lowpass.Process(x) * throb * 0.09);
        }
        return buffer[n..];
    }

    /// <summary>--sfx-dump: every sound as a mono WAV file in `dir`, and the hum's loop twice over (its seam shows), to measure them.</summary>
    public static void Dump(string dir)
    {
        System.IO.Directory.CreateDirectory(dir);
        foreach (var s in Enum.GetValues<Sound>()) WriteWav(System.IO.Path.Combine(dir, $"{s}.wav"), Render(s));
        var loop = RenderHum();
        WriteWav(System.IO.Path.Combine(dir, "BeamHum.wav"), [.. loop, .. loop]);
    }

    /// <summary>A mono, 16-bit WAV file.</summary>
    static void WriteWav(string path, float[] mono)
    {
        var data = new byte[mono.Length * 2];
        for (var i = 0; i < mono.Length; i++) Write(data, i * 2, mono[i]);
        using var f = new System.IO.BinaryWriter(System.IO.File.Create(path));
        f.Write("RIFF"u8);
        f.Write(36 + data.Length);
        f.Write("WAVEfmt "u8);
        f.Write(16);
        f.Write((short)1);
        f.Write((short)1);
        f.Write(Rate);
        f.Write(Rate * 2);
        f.Write((short)2);
        f.Write((short)16);
        f.Write("data"u8);
        f.Write(data.Length);
        f.Write(data);
    }

    /// <summary>The sound at one pan step, as the web's equal-power stereo panner places a mono source.</summary>
    static AudioStreamWav Stream(Sound sound, int step)
    {
        if (streams.TryGetValue((sound, step), out var s)) return s;
        var x = (double)step / (PanSteps - 1);
        var (left, right) = (Math.Cos(x * Math.PI / 2), Math.Sin(x * Math.PI / 2));
        var mono = rendered[sound];
        var data = new byte[mono.Length * 4];
        for (var i = 0; i < mono.Length; i++)
        {
            Write(data, i * 4, mono[i] * left);
            Write(data, i * 4 + 2, mono[i] * right);
        }
        s = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = true, Data = data };
        streams[(sound, step)] = s;
        return s;
    }

    static void Write(byte[] data, int at, double v)
    {
        var n = (short)Math.Round(Math.Clamp(v, -1, 1) * short.MaxValue);
        data[at] = (byte)n;
        data[at + 1] = (byte)(n >> 8);
    }

    // Synthesis.

    enum Wave { Sine, Square, Sawtooth, Triangle }

    enum Filter { Lowpass, Highpass, Bandpass }

    abstract record Part(double At, double Length);

    /// <summary>An oscillator gliding from `From` to `To` Hz, fading out over `Length` seconds.</summary>
    sealed record Tone(double At, Wave Wave, double From, double To, double Length, double Volume, double? Lowpass = null) : Part(At, Length);

    /// <summary>Filtered noise whose filter glides from `From` to `To` Hz, fading out over `Length`.</summary>
    sealed record Burst(double At, double Length, double Volume, Filter Filter, double From, double To) : Part(At, Length);

    static Part[] Recipe(Sound sound) => sound switch
    {
        Sound.Bullet => [new Burst(0, 0.05, 0.1, Filter.Bandpass, 2600, 2600)],
        Sound.Laser => [new Tone(0, Wave.Square, 900, 220, 0.12, 0.05, 3000)],
        Sound.Torpedo => [new Tone(0, Wave.Sawtooth, 160, 60, 0.35, 0.07, 900), new Burst(0, 0.3, 0.06, Filter.Lowpass, 800, 300)],
        Sound.Ray => [new Tone(0, Wave.Sine, 1300, 260, 0.28, 0.1), new Tone(0, Wave.Triangle, 650, 130, 0.28, 0.06)],
        Sound.Hit => [new Burst(0, 0.04, 0.06, Filter.Highpass, 1800, 1800)],
        Sound.Shield => [new Tone(0, Wave.Sine, 700, 1300, 0.09, 0.04)],
        Sound.Explosion => [new Burst(0, 0.6, 0.3, Filter.Lowpass, 1400, 150), new Tone(0, Wave.Sine, 90, 35, 0.4, 0.25)],
        Sound.BigExplosion => [new Burst(0, 1.5, 0.45, Filter.Lowpass, 700, 80), new Tone(0, Wave.Sine, 60, 22, 1.1, 0.4)],
        Sound.Ability => [new Tone(0, Wave.Sine, 523, 523, 0.18, 0.07), new Tone(0.08, Wave.Sine, 784, 784, 0.22, 0.07)],
        Sound.Beam => [new Tone(0, Wave.Sawtooth, 220, 110, 0.8, 0.1, 1600), new Burst(0, 0.8, 0.12, Filter.Bandpass, 900, 400)],
        Sound.Alert => [new Tone(0, Wave.Square, 660, 660, 0.12, 0.05, 2000), new Tone(0.16, Wave.Square, 440, 440, 0.16, 0.05, 2000)],
        Sound.Built => [.. new[] { 523, 659, 784 }.Select((f, i) => new Tone(i * 0.09, Wave.Triangle, f, f, 0.18, 0.06))],
        // A rising whine, then the rush of the portal opening.
        Sound.Warp => [new Tone(0, Wave.Sine, 160, 1100, 0.9, 0.07), new Tone(0.1, Wave.Triangle, 80, 550, 0.8, 0.05), new Burst(0.7, 0.7, 0.12, Filter.Bandpass, 2400, 300)],
        // A brass horn far below: low saws a fifth apart, restruck to hold, under a sub-bass rumble.
        Sound.Horn =>
        [
            .. new[] { 0.0, 0.55, 1.1 }.SelectMany(at => new Part[]
            {
                new Tone(at, Wave.Sawtooth, 55, 52, 1.6, 0.14, 520),
                new Tone(at, Wave.Sawtooth, 82.5, 78, 1.6, 0.08, 700),
                new Tone(at + 0.02, Wave.Square, 110, 104, 1.4, 0.035, 900),
            }),
            new Tone(0, Wave.Sine, 41, 37, 2.8, 0.24),
            new Burst(0, 0.7, 0.07, Filter.Lowpass, 420, 120),
        ],
        // A short, dry clap with a thump under it: six of them in half a second make the burst.
        Sound.Burst => [new Burst(0, 0.05, 0.16, Filter.Bandpass, 1500, 900), new Tone(0, Wave.Square, 180, 90, 0.05, 0.05, 1200)],
        // A rising hiss, a thin whistle over it.
        Sound.Missile => [new Burst(0, 0.32, 0.08, Filter.Highpass, 2500, 6000), new Tone(0, Wave.Sine, 900, 1900, 0.3, 0.03)],
        // An electric crack: a falling buzz, crackle over it, a zap after.
        Sound.Ion => [new Tone(0, Wave.Square, 2400, 300, 0.09, 0.04, 5000), new Burst(0, 0.07, 0.12, Filter.Bandpass, 4200, 1800), new Tone(0.02, Wave.Sawtooth, 1200, 150, 0.08, 0.03, 3000)],
        // A small bright pop, where a torpedo or a missile is shot down.
        Sound.Intercept => [new Burst(0, 0.035, 0.08, Filter.Bandpass, 3400, 2200), new Tone(0, Wave.Triangle, 1600, 2400, 0.04, 0.025)],
        _ => [new Tone(0, Wave.Sine, 1800, 1500, 0.03, 0.03)],
    };

    /// <summary>Each part plays this long past its fade, as the web version stops its nodes.</summary>
    const double Tail = 0.02;

    /// <summary>A sound as mono samples in [-1, 1], at full volume.</summary>
    static float[] Render(Sound sound)
    {
        var parts = Recipe(sound);
        var output = new float[(int)Math.Ceiling(parts.Max(p => p.At + p.Length + Tail) * Rate)];
        foreach (var p in parts)
        {
            var start = (int)Math.Round(p.At * Rate);
            var n = Math.Min(output.Length - start, (int)Math.Ceiling((p.Length + Tail) * Rate));
            if (p is Tone t) AddTone(output, start, n, t);
            else AddBurst(output, start, n, (Burst)p);
        }
        return output;
    }

    /// <summary>An exponential glide from `from` to `to` over `length`, then held, as Web Audio ramps a parameter.</summary>
    static double Glide(double from, double to, double length, double t) =>
        t >= length ? Math.Max(1, to) : from * Math.Pow(Math.Max(1, to) / from, t / length);

    /// <summary>Gain: up from silence in 5 ms, then an exponential fade to silence at `length`.</summary>
    static double Envelope(double t, double length, double volume)
    {
        const double Floor = 0.0001, Attack = 0.005;
        var peak = Math.Max(0.0002, volume);
        if (t < Attack) return Floor * Math.Pow(peak / Floor, t / Attack);
        if (t < length) return peak * Math.Pow(Floor / peak, (t - Attack) / (length - Attack));
        return Floor;
    }

    static void AddTone(float[] output, int start, int n, Tone p)
    {
        var lowpass = p.Lowpass is double cut ? new Biquad(Filter.Lowpass, cut) : null;
        var phase = 0.0;
        for (var i = 0; i < n; i++)
        {
            var t = (double)i / Rate;
            var dt = Glide(p.From, p.To, p.Length, t) / Rate;
            var x = Oscillator(p.Wave, phase, dt);
            phase += dt;
            phase -= Math.Floor(phase);
            if (lowpass is not null) x = lowpass.Process(x);
            output[start + i] += (float)(x * Envelope(t, p.Length, p.Volume));
        }
    }

    /// <summary>One second of white noise, as the web version's shared buffer.</summary>
    static readonly float[] Noise = MakeNoise();

    static float[] MakeNoise()
    {
        var r = new Random(1);
        var noise = new float[Rate];
        for (var i = 0; i < noise.Length; i++) noise[i] = (float)(r.NextDouble() * 2 - 1);
        return noise;
    }

    /// <summary>
    /// Where a burst starts in the noise buffer. The web version picks 0 to 0.5 s at random on
    /// every play, so its longest sounds run out of noise before their fade ends: here, halfway.
    /// </summary>
    const double NoiseOffset = 0.25;

    static void AddBurst(float[] output, int start, int n, Burst p)
    {
        var filter = new Biquad(p.Filter, p.From);
        var from = (int)(NoiseOffset * Rate);
        for (var i = 0; i < n; i++)
        {
            var t = (double)i / Rate;
            filter.Set(Glide(p.From, p.To, p.Length, t));
            var x = filter.Process(from + i < Noise.Length ? Noise[from + i] : 0);
            output[start + i] += (float)(x * Envelope(t, p.Length, p.Volume));
        }
    }

    /// <summary>Band-limited where it matters (polyBLEP on the jumps), with Web Audio's phase: every wave starts at 0 going up.</summary>
    static double Oscillator(Wave wave, double phase, double dt)
    {
        switch (wave)
        {
            case Wave.Sine:
                return Math.Sin(phase * Math.Tau);
            case Wave.Square:
                return (phase < 0.5 ? 1 : -1) + Blep(phase, dt) - Blep((phase + 0.5) % 1, dt);
            case Wave.Sawtooth:
                var s = (phase + 0.5) % 1;
                return 2 * s - 1 - Blep(s, dt);
            default:
                return phase < 0.25 ? 4 * phase : phase < 0.75 ? 2 - 4 * phase : 4 * phase - 4;
        }
    }

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

    /// <summary>Web Audio's BiquadFilterNode with its default Q of 1 (in dB for low and high pass, as the spec reads it).</summary>
    sealed class Biquad
    {
        readonly Filter kind;
        double b0, b1, b2, a1, a2, x1, x2, y1, y2;

        public Biquad(Filter kind, double frequency)
        {
            this.kind = kind;
            Set(frequency);
        }

        public void Set(double frequency)
        {
            var w = Math.Tau * Math.Min(frequency, Rate / 2.0) / Rate;
            var (cos, sin) = (Math.Cos(w), Math.Sin(w));
            var alpha = kind == Filter.Bandpass ? sin / 2 : sin / (2 * Math.Pow(10, 1 / 20.0));
            var a0 = 1 + alpha;
            (b0, b1, b2) = kind switch
            {
                Filter.Lowpass => ((1 - cos) / 2, 1 - cos, (1 - cos) / 2),
                Filter.Highpass => ((1 + cos) / 2, -(1 + cos), (1 + cos) / 2),
                _ => (alpha, 0.0, -alpha),
            };
            (b0, b1, b2, a1, a2) = (b0 / a0, b1 / a0, b2 / a0, -2 * cos / a0, (1 - alpha) / a0);
        }

        public double Process(double x)
        {
            var y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            (x2, x1, y2, y1) = (x1, x, y1, y);
            return y;
        }
    }
}
