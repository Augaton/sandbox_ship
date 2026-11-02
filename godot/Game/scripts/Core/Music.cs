// The music: a theme for the menu (under the opening too), one for the galaxy, one for battle in
// three layers that come in as the fighting grows, and two stings for a battle's or a campaign's
// end. MusicScore writes it, MusicTrack renders it on a background thread as the game starts, the
// menu's first; each theme then loops, and they cross-fade as the screens change. Nothing of it on
// a dedicated or online server, nor in a capture unless --music-record asks for it.
using System.Collections.Concurrent;
using Godot;

namespace SandboxShip.Core;

public enum MusicTheme { None, Menu, Galaxy, Battle }

public static class Music
{
    /// <summary>The loudness every theme is brought to, as RMS: some 18 dB under full scale, under the effects.</summary>
    const double TargetRms = 0.126;
    /// <summary>
    /// The music at the options' full volume, against the effects at theirs: in a big battle, at
    /// the default volumes, it sits a few decibels under the effects' din.
    /// </summary>
    public const double Level = 0.6;

    static MusicPlayer? player;

    /// <summary>Starts rendering, under `host` which stays for the whole session; the themes play once rendered.</summary>
    public static void Attach(Node host)
    {
        if (player is not null || (Dev.Shot is not null && Dev.MusicRecord is null)) return;
        player = new MusicPlayer();
        host.AddChild(player);
    }

    /// <summary>Plays a theme, cross-fading from the one playing; None fades out.</summary>
    public static void Play(MusicTheme theme) => player?.Request(theme);

    /// <summary>What a battle frame did: its hits and losses raise the battle's intensity, which falls back by itself.</summary>
    public static void Feel(IReadOnlyList<Sim.SimEvent> events, int beams) => player?.Feel(events, beams);

    /// <summary>A battle's or a campaign's end: its sting over the music, which steps back.</summary>
    public static void Sting(bool won) => player?.Sting(won);

    /// <summary>--music-record: the game's whole sound as it was mixed, saved as it quits.</summary>
    public static void SaveRecording() => player?.SaveRecording();

    // Rendering.

    /// <summary>A rendered piece: which, its 16-bit stereo samples, its length in frames, whether it loops.</summary>
    internal sealed record Stem(string Name, byte[] Pcm, int Frames, bool Loop);

    /// <summary>Every piece, the menu's first: the opening plays it while the rest is rendered.</summary>
    internal static IEnumerable<Stem> Render()
    {
        yield return Alone("menu", MusicScore.Menu());
        // The battle's layers share one gain, set on the three together: their balance is the score's.
        var layers = new[] { MusicScore.BattleCalm(), MusicScore.BattleMotion(), MusicScore.BattleFury() }.Select(t => t.Mix(true)).ToArray();
        var sumL = new float[layers[0].L.Length];
        var sumR = new float[layers[0].R.Length];
        foreach (var (l, r) in layers)
        {
            for (var i = 0; i < sumL.Length; i++)
            {
                sumL[i] += l[i];
                sumR[i] += r[i];
            }
        }
        var gain = Gain(sumL, sumR, TargetRms);
        string[] names = ["battle-calm", "battle-motion", "battle-fury"];
        for (var i = 0; i < 3; i++) yield return new Stem(names[i], MusicTrack.Pcm(layers[i].L, layers[i].R, gain, true), layers[i].L.Length, true);
        yield return Alone("galaxy", MusicScore.Galaxy());
        yield return Alone("victory", MusicScore.Victory(), false);
        yield return Alone("defeat", MusicScore.Defeat(), false);
    }

    static Stem Alone(string name, MusicTrack track, bool loop = true)
    {
        var (l, r) = track.Mix(loop);
        // A sting is measured over its first seconds, not over its fading tail.
        var measured = loop ? l.Length : Math.Min(l.Length, 3 * MusicTrack.Rate);
        return new Stem(name, MusicTrack.Pcm(l, r, Gain(l[..measured], r[..measured], loop ? TargetRms : TargetRms * 1.25), loop), l.Length, loop);
    }

    /// <summary>What brings a buffer to `rms`, short of peaks above 0.85.</summary>
    static double Gain(float[] l, float[] r, double rms)
    {
        double sum = 0, peak = 0;
        for (var i = 0; i < l.Length; i++)
        {
            sum += l[i] * l[i] + r[i] * r[i];
            peak = Math.Max(peak, Math.Max(Math.Abs(l[i]), Math.Abs(r[i])));
        }
        var now = Math.Sqrt(sum / (2.0 * l.Length));
        return Math.Min(now > 0 ? rms / now : 1, peak > 0 ? 0.85 / peak : 1);
    }

    /// <summary>--music-dump=dir: every piece as a WAV file there (the battle's layers apart), for listening or measuring.</summary>
    public static void Dump(string dir)
    {
        System.IO.Directory.CreateDirectory(dir);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        long bytes = 0;
        foreach (var s in Render())
        {
            bytes += s.Pcm.Length;
            // The loop alone, without the start copied past its end for Godot.
            var length = s.Frames * 4;
            using var f = System.IO.File.Create(System.IO.Path.Combine(dir, $"{s.Name}.wav"));
            using var w = new System.IO.BinaryWriter(f);
            w.Write("RIFF"u8);
            w.Write(36 + length);
            w.Write("WAVEfmt "u8);
            w.Write(16);
            w.Write((short)1);
            w.Write((short)2);
            w.Write(MusicTrack.Rate);
            w.Write(MusicTrack.Rate * 4);
            w.Write((short)4);
            w.Write((short)16);
            w.Write("data"u8);
            w.Write(length);
            w.Write(s.Pcm, 0, length);
        }
        GD.Print($"Musique : rendue en {watch.Elapsed.TotalSeconds:F2} s, {bytes / 1048576.0:F1} Mio de samples, dans {dir}");
    }
}

/// <summary>
/// Plays the music: two players to cross-fade the themes, the battle's three layers in one
/// synchronized stream whose volumes follow the intensity, and a player for the stings.
/// </summary>
public partial class MusicPlayer : Node
{
    /// <summary>Seconds to cross-fade from a theme to the next; the first one, under the opening, comes in slower.</summary>
    const double CrossFade = 2.5, FirstFade = 4;
    /// <summary>How long the intensity takes to fall by e, with no more fighting: some ten seconds to calm down.</summary>
    const double Settle = 6;
    /// <summary>A layer comes in over two seconds and goes over five.</summary>
    const double LayerIn = 2, LayerOut = 5;
    /// <summary>Under a sting, the music steps back to this share.</summary>
    const double Duck = 0.3;

    readonly ConcurrentQueue<Music.Stem> rendered = new();
    readonly Dictionary<string, AudioStreamWav> stems = [];
    AudioStreamSynchronized? battle;
    readonly Deck[] decks = new Deck[2];
    AudioStreamPlayer sting = null!;
    MusicTheme wanted = MusicTheme.None;
    bool started;
    double master, intensity, energy, duck = 1;
    readonly double[] layers = [1, 0, 0];
    AudioEffectRecord? recorder;

    /// <summary>A player and its share of the volume, going to `Target`.</summary>
    sealed class Deck(AudioStreamPlayer player)
    {
        public readonly AudioStreamPlayer Player = player;
        public MusicTheme Theme;
        public double Gain, Target, Speed;
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        for (var i = 0; i < decks.Length; i++)
        {
            var p = new AudioStreamPlayer();
            AddChild(p);
            decks[i] = new Deck(p);
        }
        sting = new AudioStreamPlayer();
        AddChild(sting);
        if (Dev.MusicRecord is not null)
        {
            recorder = new AudioEffectRecord();
            AudioServer.AddBusEffect(0, recorder);
            recorder.SetRecordingActive(true);
        }
        if (recorder is not null) Measure("avant la musique");
        // Below the loading's own work: the forge has every core first.
        var thread = new System.Threading.Thread(() =>
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            foreach (var s in Music.Render()) rendered.Enqueue(s);
            if (recorder is not null) GD.Print($"Musique rendue en {watch.Elapsed.TotalSeconds:F2} s");
        }) { IsBackground = true, Priority = System.Threading.ThreadPriority.BelowNormal, Name = "Musique" };
        thread.Start();
    }

    public void Request(MusicTheme theme)
    {
        wanted = theme;
        if (Current() is { } d && d.Theme == theme && d.Target > 0) return;
        Switch();
    }

    /// <summary>The deck playing the wanted theme, or the one coming in.</summary>
    Deck? Current() => decks.FirstOrDefault(d => d.Target > 0);

    /// <summary>Fades out what plays and brings the wanted theme in, once it is rendered.</summary>
    void Switch()
    {
        var fade = started ? CrossFade : FirstFade;
        foreach (var d in decks)
        {
            if (d.Theme == wanted && wanted != MusicTheme.None) continue;
            d.Target = 0;
            d.Speed = 1 / CrossFade;
        }
        if (wanted == MusicTheme.None || decks.Any(d => d.Theme == wanted && d.Target > 0)) return;
        var stream = StreamOf(wanted);
        if (stream is null) return;
        // The quieter deck takes the new theme; one still fading out of it comes back up from where it is.
        var deck = decks.FirstOrDefault(d => d.Theme == wanted) ?? decks.OrderBy(d => d.Gain).First();
        if (deck.Theme != wanted || !deck.Player.Playing)
        {
            deck.Theme = wanted;
            deck.Gain = 0;
            deck.Player.Stream = stream;
            deck.Player.VolumeDb = -80;
            deck.Player.Play();
        }
        deck.Target = 1;
        deck.Speed = 1 / fade;
        started = true;
        if (wanted == MusicTheme.Battle)
        {
            energy = intensity = 0;
            layers[0] = 1;
            layers[1] = layers[2] = 0;
            duck = 1;
        }
    }

    AudioStream? StreamOf(MusicTheme theme) => theme switch
    {
        MusicTheme.Menu => stems.GetValueOrDefault("menu"),
        MusicTheme.Galaxy => stems.GetValueOrDefault("galaxy"),
        MusicTheme.Battle => battle,
        _ => null,
    };

    public void Feel(IReadOnlyList<Sim.SimEvent> events, int beams)
    {
        foreach (var e in events)
        {
            energy += e switch
            {
                Sim.Hit => 0.012,
                Sim.Intercepted => 0.004,
                Sim.Destroyed { Radius: >= 40 } => 0.4,
                Sim.Destroyed { Kind: Sim.EntityKind.Ship or Sim.EntityKind.Structure } => 0.15,
                _ => 0,
            };
        }
        energy += beams * 0.0015;
    }

    public void Sting(bool won)
    {
        if (!stems.TryGetValue(won ? "victory" : "defeat", out var s)) return;
        sting.Stream = s;
        sting.Play();
        duck = Duck;
        energy = 0;
    }

    public void SaveRecording()
    {
        if (recorder is null || Dev.MusicRecord is not string path) return;
        recorder.SetRecordingActive(false);
        var err = recorder.GetRecording()?.SaveToWav(path) ?? Error.Unavailable;
        GD.Print($"Enregistrement du son {path} : {err}");
    }

    public override void _Process(double delta)
    {
        while (rendered.TryDequeue(out var s)) Take(s);
        var settings = Settings.Current;
        var target = settings.Muted ? 0 : Math.Clamp(settings.MusicVolume, 0, 1) * Music.Level;
        // Quick but never a jump: muting with M must not click.
        master += (target - master) * Math.Min(1, delta / 0.15);
        energy *= Math.Exp(-delta / Settle);
        intensity = 1 - Math.Exp(-energy);
        // The stings duck the music, which comes back slowly once they are over.
        if (!sting.Playing && duck < 1) duck = Math.Min(1, duck + delta / 6);
        foreach (var d in decks)
        {
            if (d.Gain != d.Target) d.Gain = d.Gain < d.Target ? Math.Min(d.Target, d.Gain + d.Speed * delta) : Math.Max(d.Target, d.Gain - d.Speed * delta);
            if (!d.Player.Playing) continue;
            var level = d.Gain * master * (d.Theme == MusicTheme.Battle ? duck : 1);
            if (d.Gain <= 0 && d.Target <= 0)
            {
                d.Player.Stop();
                d.Theme = MusicTheme.None;
                continue;
            }
            d.Player.VolumeDb = Mathf.LinearToDb((float)Math.Max(level, 1e-5));
        }
        sting.VolumeDb = Mathf.LinearToDb((float)Math.Max(master, 1e-5));
        if (battle is null) return;
        double[] goals = [1, Step(0.12, 0.4, intensity), Step(0.45, 0.8, intensity)];
        for (var i = 0; i < 3; i++)
        {
            var g = goals[i];
            layers[i] = g > layers[i] ? Math.Min(g, layers[i] + delta / LayerIn) : Math.Max(g, layers[i] - delta / LayerOut);
            battle.SetSyncStreamVolume(i, Mathf.LinearToDb((float)Math.Max(layers[i], 1e-5)));
        }
        // A recording run says how the battle's music followed the fighting.
        if (recorder is not null && (logged += delta) >= 2)
        {
            logged = 0;
            GD.Print($"Musique {Time.GetTicksMsec() / 1000.0:F1} s : {string.Join(", ", decks.Select(d => $"{d.Theme} {d.Gain:F2}"))}, intensité {intensity:F2}, couches {layers[0]:F2} {layers[1]:F2} {layers[2]:F2}");
        }
    }

    double logged;

    static double Step(double from, double to, double x)
    {
        var k = Math.Clamp((x - from) / (to - from), 0, 1);
        return k * k * (3 - 2 * k);
    }

    /// <summary>A rendered piece into a stream, here on the main thread; the battle once its three layers are in.</summary>
    void Take(Music.Stem s)
    {
        var before = OS.GetStaticMemoryUsage();
        var wav = new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = MusicTrack.Rate, Stereo = true, Data = s.Pcm,
            LoopMode = s.Loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled, LoopBegin = 0, LoopEnd = s.Loop ? s.Frames : 0,
        };
        held += OS.GetStaticMemoryUsage() - before;
        stems[s.Name] = wav;
        if (stems.ContainsKey("battle-calm") && stems.ContainsKey("battle-motion") && stems.ContainsKey("battle-fury") && battle is null)
        {
            battle = new AudioStreamSynchronized { StreamCount = 3 };
            string[] names = ["battle-calm", "battle-motion", "battle-fury"];
            for (var i = 0; i < 3; i++)
            {
                battle.SetSyncStream(i, stems[names[i]]);
                battle.SetSyncStreamVolume(i, i == 0 ? 0 : -80);
            }
        }
        // The theme asked for before it was ready.
        if (wanted != MusicTheme.None && Current() is null) Switch();
        if (recorder is not null && stems.Count == 7) Measure($"musique prête, ses flux tiennent {held / 1048576.0:F1} Mio");
    }

    /// <summary>What Godot's memory grew by as the streams were made: their samples, measured on a recording run.</summary>
    ulong held;

    /// <summary>A recording run's memory measure: Godot's own, where the streams' samples live, and .NET's once collected.</summary>
    static void Measure(string when)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GD.Print($"Musique, {when} : mémoire de Godot {OS.GetStaticMemoryUsage() / 1048576.0:F1} Mio, tas .NET {GC.GetTotalMemory(true) / 1048576.0:F1} Mio");
    }
}
