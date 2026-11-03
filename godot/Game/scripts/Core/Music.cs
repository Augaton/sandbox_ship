// The music: themes for the menu (under the opening too), the galaxy and battle, a few pieces to
// each, a battle's in three layers that come in as the fighting grows, and two stings for a
// battle's or a campaign's end. MusicScore writes it, MusicTrack renders it on a background thread
// as the game starts, the menu's first, and Godot keeps it compressed. A theme plays its first
// piece the first time, another the next time or after a few minutes; the themes cross-fade as the
// screens change. Nothing of it on a dedicated or online server, nor in a capture unless
// --music-record asks for it.
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

    /// <summary>
    /// A rendered piece, or one layer of a battle's (Layer, from 0): its theme (None for a sting),
    /// its WAV file of 16-bit stereo samples, its length in frames, whether it loops.
    /// </summary>
    internal sealed record Stem(string Piece, int Layer, MusicTheme Theme, byte[] Wav, int Frames, bool Loop)
    {
        public string Name => Layer < 0 ? Piece : $"{Piece}-{LayerNames[Layer]}";
    }

    /// <summary>A battle piece's layers: always there, once ships trade fire, at the battle's height.</summary>
    internal static readonly string[] LayerNames = ["calm", "motion", "fury"];

    /// <summary>
    /// Every piece, the menu's first: the opening plays it while the rest is rendered. Then each
    /// theme's first and the stings, then the others, each theme's second before any third.
    /// </summary>
    internal static IEnumerable<Stem> Render()
    {
        yield return Alone("menu", MusicTheme.Menu, MusicScore.Menu());
        foreach (var s in Layers("battle", MusicScore.BattleCalm, MusicScore.BattleMotion, MusicScore.BattleFury)) yield return s;
        yield return Alone("galaxy", MusicTheme.Galaxy, MusicScore.Galaxy());
        yield return Alone("victory", MusicTheme.None, MusicScore.Victory(), false);
        yield return Alone("defeat", MusicTheme.None, MusicScore.Defeat(), false);
        foreach (var s in Layers("assault", MusicScore.AssaultCalm, MusicScore.AssaultMotion, MusicScore.AssaultFury)) yield return s;
        yield return Alone("horizon", MusicTheme.Galaxy, MusicScore.Horizon());
        yield return Alone("drift", MusicTheme.Menu, MusicScore.Drift());
        foreach (var s in Layers("siege", MusicScore.SiegeCalm, MusicScore.SiegeMotion, MusicScore.SiegeFury)) yield return s;
        yield return Alone("front", MusicTheme.Galaxy, MusicScore.Front());
    }

    static Stem Alone(string name, MusicTheme theme, MusicTrack track, bool loop = true)
    {
        var (l, r) = track.Mix(loop);
        // A sting is measured over its first seconds, not over its fading tail.
        var measured = loop ? l.Length : Math.Min(l.Length, 3 * MusicTrack.Rate);
        return new Stem(name, -1, theme, MusicTrack.Wav(l, r, Gain(l[..measured], r[..measured], loop ? TargetRms : TargetRms * 1.25), loop), l.Length, loop);
    }

    /// <summary>
    /// A battle piece's layers, each mixed as soon as it is written (its buffers free then), under
    /// one gain set on the three together: their balance is the score's.
    /// </summary>
    static IEnumerable<Stem> Layers(string piece, params Func<MusicTrack>[] scores)
    {
        var layers = scores.Select(s => s().Mix(true)).ToArray();
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
        for (var i = 0; i < layers.Length; i++) yield return new Stem(piece, i, MusicTheme.Battle, MusicTrack.Wav(layers[i].L, layers[i].R, gain, true), layers[i].L.Length, true);
    }

    /// <summary>
    /// A piece as Godot plays it, compressed to QOA: a fifth of its samples' size, its noise 30 dB
    /// under the music where it is loudest and some 40 over a whole piece (measured on the menu's,
    /// the galaxy's and the battle's). Made on the rendering thread.
    /// </summary>
    internal static AudioStreamWav Stream(Stem s) => AudioStreamWav.LoadFromBuffer(s.Wav, new Godot.Collections.Dictionary
    {
        ["compress/mode"] = 2,
        ["edit/loop_mode"] = s.Loop ? 2 : 1,
        ["edit/loop_begin"] = 0,
        ["edit/loop_end"] = s.Loop ? s.Frames : -1,
    });

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
            bytes += s.Wav.Length;
            // The loop alone, without the start copied past its end for Godot.
            var head = new byte[MusicTrack.HeaderSize];
            MusicTrack.Header(head, s.Frames);
            using var f = System.IO.File.Create(System.IO.Path.Combine(dir, $"{s.Name}.wav"));
            f.Write(head);
            f.Write(s.Wav, MusicTrack.HeaderSize, s.Frames * 4);
        }
        GD.Print($"Musique : rendue en {watch.Elapsed.TotalSeconds:F2} s, {bytes / 1048576.0:F1} Mio de samples, dans {dir}");
    }
}

/// <summary>
/// Plays the music: two players to cross-fade the themes, and a piece of a theme into the next;
/// a battle's three layers in one synchronized stream whose volumes follow the intensity; a player
/// for the stings.
/// </summary>
public partial class MusicPlayer : Node
{
    /// <summary>Seconds to cross-fade from a theme to the next; the first one, under the opening, comes in slower.</summary>
    const double CrossFade = 2.5, FirstFade = 4;
    /// <summary>
    /// A piece that has played this long gives way to another of its theme, cross-fading over
    /// TurnFade seconds so that it is gone as its loop ends.
    /// </summary>
    const double Turn = 150, TurnFade = 4;
    /// <summary>How long the intensity takes to fall by e, with no more fighting: some ten seconds to calm down.</summary>
    const double Settle = 6;
    /// <summary>A layer comes in over two seconds and goes over five.</summary>
    const double LayerIn = 2, LayerOut = 5;
    /// <summary>Under a sting, the music steps back to this share.</summary>
    const double Duck = 0.3;

    readonly ConcurrentQueue<(Music.Stem Stem, AudioStreamWav Stream)> rendered = new();
    /// <summary>Each theme's pieces ready to play, in the order they came.</summary>
    readonly Dictionary<MusicTheme, List<AudioStream>> pieces = [];
    /// <summary>The piece each theme played last: the next one is another.</summary>
    readonly Dictionary<MusicTheme, AudioStream> last = [];
    /// <summary>A battle piece's layers as they come, until all are in.</summary>
    readonly Dictionary<string, AudioStreamWav?[]> layering = [];
    readonly Dictionary<string, AudioStreamWav> stings = [];
    readonly Deck[] decks = new Deck[2];
    AudioStreamPlayer sting = null!;
    MusicTheme wanted = MusicTheme.None;
    bool started;
    double master, intensity, energy, duck = 1;
    readonly double[] layers = [1, 0, 0];
    AudioEffectRecord? recorder;
    /// <summary>Set by the rendering thread once every piece is out of it, for a recording run's measure.</summary>
    volatile bool allRendered;
    bool measured;

    /// <summary>A player and its share of the volume, going to `Target`; how long its piece has played.</summary>
    sealed class Deck(AudioStreamPlayer player)
    {
        public readonly AudioStreamPlayer Player = player;
        public MusicTheme Theme;
        public double Gain, Target, Speed, Played;
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
        // Below the loading's own work: the forge has every core first. The samples are
        // compressed here too, and only the compressed stream goes to the main thread.
        var thread = new System.Threading.Thread(() =>
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            // Godot keeps the stream: the samples stay here, to be collected.
            foreach (var s in Music.Render()) rendered.Enqueue((s with { Wav = [] }, Music.Stream(s)));
            if (recorder is not null) GD.Print($"Musique rendue en {watch.Elapsed.TotalSeconds:F2} s");
            // .NET would keep the rendering's buffers reserved for good: one collection, a few
            // milliseconds, gives them back.
            MusicTrack.Release();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            allRendered = true;
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

    /// <summary>Fades out what plays and brings the wanted theme in, once a piece of it is rendered.</summary>
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
        // A deck still fading out of the theme comes back up from where it is; else the quieter one takes a piece of it.
        var deck = decks.FirstOrDefault(d => d.Theme == wanted && d.Player.Playing);
        if (deck is null)
        {
            if (Pick(wanted) is not { } stream) return;
            deck = decks.OrderBy(d => d.Gain).First();
            Load(deck, wanted, stream);
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

    /// <summary>The piece a theme plays next: its first the first time, then any other than the one it played last.</summary>
    AudioStream? Pick(MusicTheme theme)
    {
        if (!pieces.TryGetValue(theme, out var list)) return null;
        if (!last.TryGetValue(theme, out var before)) return last[theme] = list[0];
        var others = list.Where(p => p != before).ToList();
        return last[theme] = others.Count == 0 ? before : others[Random.Shared.Next(others.Count)];
    }

    static void Load(Deck deck, MusicTheme theme, AudioStream stream)
    {
        deck.Theme = theme;
        deck.Gain = deck.Played = 0;
        deck.Player.Stream = stream;
        deck.Player.VolumeDb = -80;
        deck.Player.Play();
    }

    /// <summary>The piece playing gives way, as its loop ends, to another of its theme on the other deck.</summary>
    void TurnFrom(Deck from)
    {
        if (Pick(from.Theme) is not { } stream || stream == from.Player.Stream) return;
        var to = decks.First(d => d != from);
        Load(to, from.Theme, stream);
        (to.Target, to.Speed) = (1, 1 / TurnFade);
        (from.Target, from.Speed) = (0, 1 / TurnFade);
    }

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
        if (!stings.TryGetValue(won ? "victory" : "defeat", out var s)) return;
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
        while (rendered.TryDequeue(out var r)) Take(r.Stem, r.Stream);
        if (recorder is not null && allRendered && rendered.IsEmpty && !measured)
        {
            measured = true;
            Measure($"musique prête, ses flux tiennent {held / 1048576.0:F1} Mio");
        }
        var settings = Settings.Current;
        var target = settings.Muted ? 0 : Math.Clamp(settings.MusicVolume, 0, 1) * Music.Level;
        // Quick but never a jump: muting with M must not click.
        master += (target - master) * Math.Min(1, delta / 0.15);
        energy *= Math.Exp(-delta / Settle);
        intensity = 1 - Math.Exp(-energy);
        // The stings duck the music, which comes back slowly once they are over.
        if (!sting.Playing && duck < 1) duck = Math.Min(1, duck + delta / 6);
        if (Current() is { Gain: >= 1 } now && now.Played >= Turn && now.Player.Stream is { } playing
            && playing.GetLength() - now.Player.GetPlaybackPosition() <= TurnFade && pieces.GetValueOrDefault(now.Theme)?.Count > 1)
        {
            TurnFrom(now);
        }
        double[] goals = [1, Step(0.12, 0.4, intensity), Step(0.45, 0.8, intensity)];
        for (var i = 0; i < 3; i++)
        {
            var g = goals[i];
            layers[i] = g > layers[i] ? Math.Min(g, layers[i] + delta / LayerIn) : Math.Max(g, layers[i] - delta / LayerOut);
        }
        foreach (var d in decks)
        {
            if (d.Gain != d.Target) d.Gain = d.Gain < d.Target ? Math.Min(d.Target, d.Gain + d.Speed * delta) : Math.Max(d.Target, d.Gain - d.Speed * delta);
            if (!d.Player.Playing) continue;
            d.Played += delta;
            var level = d.Gain * master * (d.Theme == MusicTheme.Battle ? duck : 1);
            if (d.Gain <= 0 && d.Target <= 0)
            {
                d.Player.Stop();
                d.Theme = MusicTheme.None;
                continue;
            }
            d.Player.VolumeDb = Mathf.LinearToDb((float)Math.Max(level, 1e-5));
            if (d.Player.Stream is AudioStreamSynchronized battle)
            {
                for (var i = 0; i < 3; i++) battle.SetSyncStreamVolume(i, Mathf.LinearToDb((float)Math.Max(layers[i], 1e-5)));
            }
        }
        sting.VolumeDb = Mathf.LinearToDb((float)Math.Max(master, 1e-5));
        // A recording run says how the music followed the screens and the fighting.
        if (recorder is not null && (logged += delta) >= 2)
        {
            logged = 0;
            var decksNow = string.Join(", ", decks.Select(d => $"{d.Theme} {pieces.GetValueOrDefault(d.Theme)?.IndexOf(d.Player.Stream!) ?? -1} {d.Gain:F2} à {d.Player.GetPlaybackPosition():F1} s"));
            GD.Print($"Musique {Time.GetTicksMsec() / 1000.0:F1} s : {decksNow}, intensité {intensity:F2}, couches {layers[0]:F2} {layers[1]:F2} {layers[2]:F2}");
        }
    }

    double logged;

    static double Step(double from, double to, double x)
    {
        var k = Math.Clamp((x - from) / (to - from), 0, 1);
        return k * k * (3 - 2 * k);
    }

    /// <summary>A rendered piece, here on the main thread: a sting, a theme's piece, or a battle's layer, the piece once all its layers are in.</summary>
    void Take(Music.Stem s, AudioStreamWav wav)
    {
        if (recorder is not null) held += (ulong)wav.Data.Length;
        if (s.Theme == MusicTheme.None) stings[s.Piece] = wav;
        else if (s.Layer < 0) Add(s.Theme, wav);
        else
        {
            if (!layering.TryGetValue(s.Piece, out var parts)) layering[s.Piece] = parts = new AudioStreamWav?[Music.LayerNames.Length];
            parts[s.Layer] = wav;
            if (parts.All(p => p is not null))
            {
                layering.Remove(s.Piece);
                var battle = new AudioStreamSynchronized { StreamCount = parts.Length };
                for (var i = 0; i < parts.Length; i++)
                {
                    battle.SetSyncStream(i, parts[i]);
                    battle.SetSyncStreamVolume(i, i == 0 ? 0 : -80);
                }
                Add(s.Theme, battle);
            }
        }
        // The theme asked for before it was ready.
        if (wanted != MusicTheme.None && Current() is null) Switch();
    }

    void Add(MusicTheme theme, AudioStream stream)
    {
        if (!pieces.TryGetValue(theme, out var list)) pieces[theme] = list = [];
        list.Add(stream);
    }

    /// <summary>The compressed streams' size, counted on a recording run.</summary>
    ulong held;

    /// <summary>A recording run's memory measure: Godot's own, where the streams' samples live, and .NET's once collected.</summary>
    static void Measure(string when)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GD.Print($"Musique, {when} : mémoire de Godot {OS.GetStaticMemoryUsage() / 1048576.0:F1} Mio, tas .NET {GC.GetTotalMemory(true) / 1048576.0:F1} Mio"
            + $" ({GC.GetGCMemoryInfo().TotalCommittedBytes / 1048576.0:F1} Mio réservés), processus {System.Environment.WorkingSet / 1048576.0:F0} Mio");
    }
}
