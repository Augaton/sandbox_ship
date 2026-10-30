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
