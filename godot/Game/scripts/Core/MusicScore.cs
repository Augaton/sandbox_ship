// What the music plays: chords, basses, arpeggios, melodies and drums, written out for
// MusicTrack. Each piece is a loop of whole bars; a battle's three layers share one loop, to be
// heard together.
namespace SandboxShip.Core;

public static class MusicScore
{
    /// <summary>A chord: its bass note and the pad's notes, MIDI.</summary>
    readonly record struct Chord(int Bass, int[] Pad);

    /// <summary>A note of a melody: when, in beats (or seconds) into its chord; how long it is held; which, MIDI.</summary>
    readonly record struct Tune(double At, double Hold, int Midi);

    /// <summary>
    /// The menu's, under the opening too: slow pads over a deep bass, a sparse pluck echoing
    /// across, a bell now and then. D minor, a chord every five seconds.
    /// </summary>
    public static MusicTrack Menu()
    {
        var t = new MusicTrack(40, 12, 11, echoTime: 0.75, echoFeedback: 0.4, roomSize: 0.86, roomWet: 0.35);
        Chord[] chords =
        [
            new(38, [53, 57, 60, 64]), new(34, [53, 57, 62, 65]), new(41, [52, 57, 60, 64]), new(36, [52, 55, 62, 64]),
            new(43, [53, 58, 62, 69]), new(45, [53, 57, 62, 65]), new(34, [52, 57, 62, 65]), new(33, [50, 55, 57, 64]),
        ];
        const double Length = 5;
        for (var i = 0; i < chords.Length; i++)
        {
            var at = i * Length;
            var (bass, pad) = (chords[i].Bass, chords[i].Pad);
            foreach (var m in pad) t.Pad(at, Length, m, 0.11, 1900);
            t.Bass(at, Length - 0.3, bass + 12, 0.1, 1.2);
            int[] steps = [0, 2, 1, 3, 2, 0, 3, 1];
            for (var k = 0; k < steps.Length; k++)
            {
                if (t.Chance() < 0.35) continue;
                t.Pluck(at + k * Length / 8 + t.Jitter(0.015), pad[steps[k]] + 12, 0.12 + t.Jitter(0.025), 0.9, t.Jitter(0.6), 0.45, 0.5, 4200);
            }
            if (i % 2 == 0) t.Bell(at + 2.5, pad[^1] + 24, 0.08, t.Jitter(0.5));
        }
        return t;
    }

    /// <summary>
    /// The galaxy's: a patient pulse of eighths over warm pads, its filter opening and closing over
    /// the loop, a soft bass on the strong beats, a tick on the off ones. E minor, 90 a minute.
    /// </summary>
    public static MusicTrack Galaxy()
    {
        const double Beat = 60.0 / 90, Bars = 16;
        var loop = Bars * 4 * Beat;
        var t = new MusicTrack(loop, 10, 23, echoTime: Beat * 0.75, echoFeedback: 0.3, roomSize: 0.84, roomWet: 0.3);
        Chord[] chords =
        [
            new(40, [55, 59, 62, 66]), new(36, [52, 55, 59, 62]), new(43, [54, 59, 62, 66]), new(38, [54, 57, 62, 64]),
            new(40, [55, 59, 62, 66]), new(45, [55, 60, 64, 71]), new(36, [52, 55, 59, 64]), new(35, [52, 54, 59, 64]),
        ];
        var length = 8 * Beat;
        int[] pattern = [0, 1, 2, 3, 2, 1, 3, 2];
        for (var i = 0; i < chords.Length; i++)
        {
            var at = i * length;
            var (bass, pad) = (chords[i].Bass, chords[i].Pad);
            foreach (var m in pad) t.Pad(at, length, m, 0.09, 1600, 1.2, 1.8);
            for (var b = 0; b < 8; b++)
            {
                if (b % 2 == 0) t.Bass(at + b * Beat, Beat * 1.6, bass + 12, 0.1, 0.3);
                else t.Hat(at + b * Beat + t.Jitter(0.008), 0.05, t.Jitter(0.4));
            }
            for (var e = 0; e < 16; e++)
            {
                var when = at + e * Beat / 2;
                // The filter's slow round trip over the whole loop: it ends where it starts.
                var cutoff = 2800 + 1300 * Math.Sin(Math.Tau * when / loop);
                t.Pluck(when + t.Jitter(0.006), pad[pattern[e % 8]] + 12, (e % 4 == 0 ? 0.11 : 0.08) + t.Jitter(0.012), 0.35, e % 2 == 0 ? -0.35 : 0.35, 0.3, 0.3, cutoff);
            }
        }
        return t;
    }

    /// <summary>Battle: 104 a minute, A minor, sixteen bars; the three layers below, in time with one another.</summary>
    const double BattleBeat = 60.0 / 104;
    const double BattleLoop = 16 * 4 * BattleBeat;

    static readonly Chord[] BattleChords =
    [
        new(33, [57, 60, 64]), new(29, [57, 60, 65]), new(38, [57, 62, 65]), new(28, [56, 59, 64]),
        new(33, [57, 60, 64]), new(34, [58, 62, 65]), new(31, [55, 59, 62]), new(28, [56, 59, 64]),
    ];

    static MusicTrack BattleTrack(int seed, double room = 0.28) =>
        new(BattleLoop, 8, seed, echoTime: BattleBeat * 0.75, echoFeedback: 0.3, roomSize: 0.82, roomWet: room);

    /// <summary>The first layer, always there in battle: a dark pad, a drone on the root, a slow drum on each bar.</summary>
    public static MusicTrack BattleCalm()
    {
        var t = BattleTrack(31, 0.35);
        var length = 8 * BattleBeat;
        for (var i = 0; i < BattleChords.Length; i++)
        {
            var at = i * length;
            var c = BattleChords[i];
            foreach (var m in c.Pad) t.Pad(at, length, m - 12, 0.1, 1300, 0.9, 1.6);
            foreach (var m in c.Pad) t.Pad(at, length, m, 0.05, 1800, 1.2, 1.6);
            t.Drone(at, length - 1.5, c.Bass + 12, 0.07, 500);
            t.Tom(at, c.Bass + 24, 0.2);
            t.Tom(at + 4 * BattleBeat, c.Bass + 24, 0.13);
        }
        return t;
    }

    /// <summary>The second, once ships trade fire: a driving saw bass in eighths, a quick arpeggio over it.</summary>
    public static MusicTrack BattleMotion()
    {
        var t = BattleTrack(37);
        var length = 8 * BattleBeat;
        int[] bass = [0, 0, 12, 0, 0, 12, 0, 7];
        int[] arp = [0, 1, 2, 1, 0, 2, 1, 2];
        for (var i = 0; i < BattleChords.Length; i++)
        {
            var at = i * length;
            var c = BattleChords[i];
            for (var e = 0; e < 16; e++) t.SawBass(at + e * BattleBeat / 2, BattleBeat * 0.42, c.Bass + 12 + bass[e % 8], e % 4 == 0 ? 0.18 : 0.13, 1500);
            for (var s = 0; s < 32; s++)
            {
                if (s % 8 == 7) continue;
                t.Pluck(at + s * BattleBeat / 4 + t.Jitter(0.004), c.Pad[arp[s % 8]] + 12 + (s % 16 >= 8 ? 12 : 0), s % 4 == 0 ? 0.09 : 0.06, 0.16,
                    s % 2 == 0 ? -0.45 : 0.45, 0.25, 0.15, 4800);
            }
        }
        return t;
    }

    /// <summary>The third, when the battle is at its height: drums, brass stabs on the chords, a tom fill every four bars.</summary>
    public static MusicTrack BattleFury()
    {
        var t = BattleTrack(41, 0.22);
        for (var bar = 0; bar < 16; bar++)
        {
            var at = bar * 4 * BattleBeat;
            foreach (var k in new[] { 0, 2, 2.5 }) t.Kick(at + k * BattleBeat, 0.3);
            foreach (var s in new[] { 1, 3 }) t.Snare(at + s * BattleBeat, 0.3, 0.1);
            for (var h = 0; h < 8; h++)
            {
                if (bar % 2 == 1 && h == 7) t.Hat(at + h * BattleBeat / 2, 0.09, -0.3, true);
                else t.Hat(at + h * BattleBeat / 2 + t.Jitter(0.004), h % 2 == 1 ? 0.1 : 0.065, -0.3);
            }
            var c = BattleChords[bar / 2];
            if (bar % 2 == 0) foreach (var m in c.Pad) t.Brass(at, BattleBeat * 0.9, m, 0.1, (m % 3 - 1) * 0.3);
            else foreach (var m in c.Pad) t.Brass(at + 2.5 * BattleBeat, BattleBeat * 0.5, m, 0.08, (m % 3 - 1) * 0.3);
            if (bar % 4 == 3)
            {
                for (var f = 0; f < 4; f++) t.Tom(at + (3 + f / 4.0) * BattleBeat, 50 - f * 3, 0.2, 0.5 - f * 0.3);
            }
        }
        return t;
    }

    /// <summary>
    /// The menu's second: F lydian, brighter and slower to move than the first, a chord every four
    /// seconds and a half, twice through: bells drifting down the chords the first time, a soft
    /// lead singing over them the second.
    /// </summary>
    public static MusicTrack Drift()
    {
        const double Length = 4.5;
        Chord[] chords =
        [
            new(41, [57, 60, 64, 67]), new(41, [59, 62, 64, 67]), new(45, [57, 60, 64, 67]), new(40, [55, 59, 62, 64]),
            new(38, [53, 57, 60, 64]), new(36, [55, 59, 62, 64]), new(35, [55, 59, 62, 67]), new(43, [59, 62, 64, 67]),
        ];
        // In seconds into each chord.
        Tune[][] tune =
        [
            [new(0.5, 2, 72), new(2.5, 2, 76)],
            [new(0, 1.5, 74), new(1.5, 3, 71)],
            [new(0.5, 1.5, 72), new(2, 2.5, 76)],
            [new(0, 3, 79), new(3, 1.5, 74)],
            [new(0.5, 2, 77), new(2.5, 2, 76)],
            [new(0, 2.5, 74), new(2.5, 2, 72)],
            [new(0, 2, 71), new(2, 2.5, 74)],
            [new(0.5, 1.5, 71)],
        ];
        var t = new MusicTrack(2 * chords.Length * Length, 12, 61, echoTime: 0.6, echoFeedback: 0.42, roomSize: 0.87, roomWet: 0.38);
        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 0; i < chords.Length; i++)
            {
                var at = (pass * chords.Length + i) * Length;
                var (bass, pad) = (chords[i].Bass, chords[i].Pad);
                foreach (var m in pad) t.Pad(at, Length, m, 0.1, 2100, 1.8, 2.4);
                t.Bass(at, Length - 0.3, bass + 12, 0.09, 1.4);
                if (pass == 0)
                {
                    for (var k = 0; k < 4; k++)
                    {
                        if (t.Chance() < 0.3) continue;
                        t.Bell(at + 0.4 + k * 1.05 + t.Jitter(0.05), pad[3 - k] + 12, 0.05 + t.Jitter(0.01), t.Jitter(0.7));
                    }
                    continue;
                }
                foreach (var n in tune[i]) t.Lead(at + n.At, n.Hold, n.Midi, 0.12, t.Jitter(0.2));
                if (i % 2 == 1) t.Bell(at + 3.5, pad[^1] + 24, 0.04, t.Jitter(0.6));
            }
        }
        return t;
    }

    /// <summary>
    /// The galaxy's second: D dorian, 84 a minute, an arpeggio in triplets over warm pads, its
    /// filter opening towards the middle of the loop and closing again; the second time through, a
    /// lead and a light beat. Thirty-two bars.
    /// </summary>
    public static MusicTrack Horizon()
    {
        const double Beat = 60.0 / 84;
        Chord[] chords =
        [
            new(38, [53, 57, 60, 64]), new(43, [55, 59, 62, 64]), new(41, [53, 57, 60, 64]), new(36, [52, 55, 60, 64]),
            new(33, [52, 55, 60, 64]), new(40, [55, 59, 62, 64]), new(34, [53, 57, 62, 65]), new(33, [52, 57, 62, 64]),
        ];
        // In beats into each chord, two bars.
        Tune[][] tune =
        [
            [new(0, 3, 69), new(3, 1, 72), new(4, 4, 74)],
            [new(0, 2, 76), new(2, 2, 74), new(4, 4, 71)],
            [new(0, 3, 72), new(3, 1, 69), new(4, 2, 72), new(6, 2, 74)],
            [new(0, 6, 76)],
            [new(0, 3, 76), new(3, 1, 74), new(4, 4, 72)],
            [new(0, 3, 71), new(3, 1, 67), new(4, 4, 71)],
            [new(0, 3, 77), new(3, 1, 76), new(4, 4, 74)],
            [new(0, 6, 76)],
        ];
        var length = 8 * Beat;
        var loop = 2 * chords.Length * length;
        var t = new MusicTrack(loop, 10, 67, echoTime: Beat * 2 / 3, echoFeedback: 0.32, roomSize: 0.85, roomWet: 0.32);
        int[] arp = [0, 1, 2, 3, 2, 1];
        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 0; i < chords.Length; i++)
            {
                var at = (pass * chords.Length + i) * length;
                var (bass, pad) = (chords[i].Bass, chords[i].Pad);
                foreach (var m in pad) t.Pad(at, length, m, 0.08, 1700, 1.4, 2);
                t.Bass(at, Beat * 2.8, bass + 12, 0.1, 0.5);
                t.Bass(at + 4 * Beat, Beat * 2.8, bass + 12, 0.085, 0.5);
                for (var k = 0; k < 24; k++)
                {
                    var when = at + k * Beat / 3;
                    var cutoff = 1900 + 1800 * Math.Sin(Math.PI * when / loop);
                    t.Pluck(when + t.Jitter(0.005), pad[arp[k % 6]] + 12, (k % 3 == 0 ? 0.09 : 0.06) + t.Jitter(0.01), 0.3, 0.4 * Math.Sin(k * 0.7), 0.3, 0.3, cutoff);
                }
                if (pass == 0)
                {
                    if (i % 2 == 0) t.Bell(at + 6 * Beat, pad[^1] + 12, 0.045, t.Jitter(0.5));
                    continue;
                }
                foreach (var n in tune[i]) t.Lead(at + n.At * Beat, n.Hold * Beat, n.Midi, 0.11, 0.15);
                t.Kick(at, 0.1);
                t.Kick(at + 4 * Beat, 0.08);
                for (var b = 1; b < 8; b += 2) t.Hat(at + b * Beat + t.Jitter(0.006), 0.045, t.Jitter(0.4));
            }
        }
        return t;
    }

    /// <summary>
    /// The galaxy's third, for a war that goes on: C minor, 100 a minute, a muted pulse in
    /// sixteenths under dark pads, a bell falling down each chord; the second time through, a lead
    /// over a slow beat. Thirty-two bars.
    /// </summary>
    public static MusicTrack Front()
    {
        const double Beat = 60.0 / 100;
        Chord[] chords =
        [
            new(36, [51, 55, 60, 62]), new(32, [51, 55, 60, 63]), new(39, [51, 55, 58, 62]), new(34, [50, 53, 58, 62]),
            new(36, [51, 55, 60, 62]), new(41, [53, 56, 60, 63]), new(32, [51, 56, 60, 63]), new(31, [50, 55, 59, 62]),
        ];
        Tune[][] tune =
        [
            [new(0, 2, 72), new(2, 2, 75), new(4, 4, 74)],
            [new(0, 3, 72), new(3, 1, 70), new(4, 4, 67)],
            [new(0, 2, 70), new(2, 2, 67), new(4, 4, 70)],
            [new(0, 6, 74)],
            [new(0, 2, 75), new(2, 2, 79), new(4, 2, 77), new(6, 2, 75)],
            [new(0, 4, 72), new(4, 4, 68)],
            [new(0, 3, 75), new(3, 1, 74), new(4, 4, 72)],
            [new(0, 4, 71), new(4, 4, 74)],
        ];
        var length = 8 * Beat;
        var t = new MusicTrack(2 * chords.Length * length, 10, 71, echoTime: Beat * 0.75, echoFeedback: 0.33, roomSize: 0.84, roomWet: 0.3);
        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 0; i < chords.Length; i++)
            {
                var at = (pass * chords.Length + i) * length;
                var (bass, pad) = (chords[i].Bass, chords[i].Pad);
                foreach (var m in pad) t.Pad(at, length, m, 0.085, 1300, 1.2, 1.8);
                t.Drone(at, length - 1, bass + 12, 0.05, 380);
                for (var s = 0; s < 32; s++) t.SawBass(at + s * Beat / 4, Beat * 0.18, bass + 12, s % 4 == 0 ? 0.075 : 0.05, 420);
                for (var k = 0; k < 3; k++) t.Bell(at + (pass == 0 ? 2 : 6) * Beat + k * Beat / 2, pad[3 - k] + 12, 0.04, -0.3 + k * 0.3);
                if (pass == 0) continue;
                foreach (var n in tune[i]) t.Lead(at + n.At * Beat, n.Hold * Beat, n.Midi, 0.1, -0.1, 2200);
                t.Kick(at, 0.16);
                t.Kick(at + 4 * Beat, 0.16);
                t.Snare(at + 2 * Beat, 0.07, 0.15);
                t.Snare(at + 6 * Beat, 0.07, 0.15);
                for (var h = 0; h < 16; h++) t.Hat(at + h * Beat / 2 + t.Jitter(0.005), h % 2 == 1 ? 0.04 : 0.025, 0.3);
            }
        }
        return t;
    }

    /// <summary>The battle's second piece: 126 a minute, D minor, sixteen bars, quicker and harder than the first.</summary>
    const double AssaultBeat = 60.0 / 126;
    const double AssaultLoop = 16 * 4 * AssaultBeat;

    static readonly Chord[] AssaultChords =
    [
        new(38, [57, 62, 65]), new(34, [58, 62, 65]), new(36, [55, 60, 64]), new(33, [57, 60, 64]),
        new(38, [57, 62, 65]), new(31, [55, 58, 62]), new(34, [58, 62, 65]), new(33, [57, 61, 64]),
    ];

    static MusicTrack AssaultTrack(int seed, double room = 0.26) =>
        new(AssaultLoop, 8, seed, echoTime: AssaultBeat * 0.75, echoFeedback: 0.28, roomSize: 0.82, roomWet: room);

    /// <summary>Its first layer: dark pads, a drone on the root, war drums on every bar (one, the and of two, three), a bell at each chord.</summary>
    public static MusicTrack AssaultCalm()
    {
        var t = AssaultTrack(73, 0.35);
        var length = 8 * AssaultBeat;
        for (var i = 0; i < AssaultChords.Length; i++)
        {
            var at = i * length;
            var c = AssaultChords[i];
            foreach (var m in c.Pad) t.Pad(at, length, m - 12, 0.1, 1200, 0.8, 1.4);
            t.Drone(at, length - 1.2, c.Bass + 12, 0.07, 450);
            for (var bar = 0; bar < 2; bar++)
            {
                var b = at + bar * 4 * AssaultBeat;
                t.Tom(b, 45, 0.2);
                t.Tom(b + 1.5 * AssaultBeat, 50, 0.12, -0.2);
                t.Tom(b + 2 * AssaultBeat, 47, 0.15, 0.2);
            }
            t.Bell(at, c.Pad[^1] + 12, 0.035, t.Jitter(0.5));
        }
        return t;
    }

    /// <summary>Its second: a galloping saw bass (an eighth and two sixteenths to the beat), an arpeggio leaping an octave in sixteenths.</summary>
    public static MusicTrack AssaultMotion()
    {
        var t = AssaultTrack(79);
        var length = 8 * AssaultBeat;
        int[] arp = [0, 1, 2, 3, 2, 1, 3, 1];
        for (var i = 0; i < AssaultChords.Length; i++)
        {
            var at = i * length;
            var c = AssaultChords[i];
            for (var b = 0; b < 8; b++)
            {
                var on = at + b * AssaultBeat;
                t.SawBass(on, AssaultBeat * 0.4, c.Bass + 12, 0.17, 1400);
                t.SawBass(on + AssaultBeat * 0.5, AssaultBeat * 0.2, c.Bass + 12, 0.12, 1100);
                t.SawBass(on + AssaultBeat * 0.75, AssaultBeat * 0.2, c.Bass + (b % 2 == 1 ? 24 : 12), 0.12, 1100);
            }
            int[] notes = [c.Pad[0], c.Pad[1], c.Pad[2], c.Pad[0] + 12];
            for (var s = 0; s < 32; s++)
            {
                if (s % 8 == 7) continue;
                t.Pluck(at + s * AssaultBeat / 4 + t.Jitter(0.003), notes[arp[s % 8]] + 12, s % 4 == 0 ? 0.08 : 0.055, 0.14,
                    s % 2 == 0 ? 0.4 : -0.4, 0.22, 0.15, 5200);
            }
        }
        return t;
    }

    /// <summary>Its third: drums driving in sixteenths, brass on and off the beat, a tom fill every four bars, and a lead with the theme.</summary>
    public static MusicTrack AssaultFury()
    {
        var t = AssaultTrack(83, 0.2);
        for (var bar = 0; bar < 16; bar++)
        {
            var at = bar * 4 * AssaultBeat;
            foreach (var k in new[] { 0, 0.75, 2, 2.5 }) t.Kick(at + k * AssaultBeat, 0.3);
            t.Snare(at + AssaultBeat, 0.28, 0.1);
            t.Snare(at + 3 * AssaultBeat, 0.28, 0.1);
            for (var h = 0; h < 16; h++) t.Hat(at + h * AssaultBeat / 4 + t.Jitter(0.003), h % 4 == 2 ? 0.08 : 0.04, 0.3);
            var c = AssaultChords[bar / 2];
            if (bar % 2 == 0) foreach (var m in c.Pad) t.Brass(at, AssaultBeat * 1.2, m, 0.07, (m % 3 - 1) * 0.3);
            foreach (var m in c.Pad) t.Brass(at + 2.5 * AssaultBeat, AssaultBeat * 0.4, m, 0.055, (m % 3 - 1) * 0.3);
            if (bar % 4 == 3)
            {
                for (var f = 0; f < 4; f++) t.Tom(at + (3 + f / 4.0) * AssaultBeat, 52 - f * 3, 0.2, 0.5 - f * 0.3);
            }
            else t.Snare(at + 3.75 * AssaultBeat, 0.08, -0.2);
        }
        Tune[][] tune =
        [
            [new(0, 1.5, 74), new(1.5, 0.5, 69), new(2, 1, 74), new(3, 1, 76), new(4, 3, 77), new(7, 1, 76)],
            [new(0, 2, 74), new(2, 2, 70), new(4, 4, 77)],
            [new(0, 1.5, 76), new(1.5, 0.5, 72), new(2, 2, 79), new(4, 4, 76)],
            [new(0, 4, 72), new(4, 2, 76), new(6, 2, 74)],
            [new(0, 1.5, 74), new(1.5, 0.5, 69), new(2, 1, 74), new(3, 1, 76), new(4, 3, 77), new(7, 1, 79)],
            [new(0, 3, 79), new(3, 1, 77), new(4, 4, 74)],
            [new(0, 2, 77), new(2, 2, 74), new(4, 2, 70), new(6, 2, 74)],
            [new(0, 4, 76), new(4, 2, 73), new(6, 2, 76)],
        ];
        for (var i = 0; i < tune.Length; i++)
        {
            foreach (var n in tune[i]) t.Lead((i * 8 + n.At) * AssaultBeat, n.Hold * AssaultBeat * 0.95, n.Midi, 0.1, 0, 3200, 0.25, 0.4);
        }
        return t;
    }

    /// <summary>The battle's third piece: 88 a minute in half time, E minor turning phrygian, sixteen bars, heavy and slow.</summary>
    const double SiegeBeat = 60.0 / 88;
    const double SiegeLoop = 16 * 4 * SiegeBeat;

    static readonly Chord[] SiegeChords =
    [
        new(28, [55, 59, 64]), new(24, [55, 60, 64]), new(21, [57, 60, 64]), new(23, [59, 63, 66]),
        new(28, [55, 59, 64]), new(29, [57, 60, 65]), new(31, [55, 59, 62]), new(23, [59, 63, 66]),
    ];

    static MusicTrack SiegeTrack(int seed, double room = 0.28) =>
        new(SiegeLoop, 8, seed, echoTime: SiegeBeat * 0.75, echoFeedback: 0.3, roomSize: 0.84, roomWet: room);

    /// <summary>Its first layer: low pads with a thin shimmer over them, a drone, a heartbeat on every bar, two bells to a chord.</summary>
    public static MusicTrack SiegeCalm()
    {
        var t = SiegeTrack(89, 0.38);
        var length = 8 * SiegeBeat;
        for (var i = 0; i < SiegeChords.Length; i++)
        {
            var at = i * length;
            var c = SiegeChords[i];
            foreach (var m in c.Pad) t.Pad(at, length, m - 12, 0.1, 1100, 1, 1.6);
            foreach (var m in c.Pad) t.Pad(at, length, m + 12, 0.03, 2000, 1.4, 1.6);
            t.Drone(at, length - 1.5, c.Bass + 12, 0.08, 420);
            for (var bar = 0; bar < 2; bar++)
            {
                var b = at + bar * 4 * SiegeBeat;
                t.Tom(b, 45, 0.2);
                t.Tom(b + 0.3 * SiegeBeat, 43, 0.12);
            }
            t.Bell(at + 2 * SiegeBeat, c.Pad[^1] + 12, 0.035, -0.4);
            t.Bell(at + 6 * SiegeBeat, c.Pad[1] + 12, 0.03, 0.4);
        }
        return t;
    }

    /// <summary>Its second: a syncopated saw bass, an ostinato in eighths low on the chord, hats off the beat.</summary>
    public static MusicTrack SiegeMotion()
    {
        var t = SiegeTrack(97);
        var length = 8 * SiegeBeat;
        double[] hits = [0, 0.75, 1.5, 2, 2.75, 3.5];
        int[] figure = [0, 2, 1, 2];
        for (var i = 0; i < SiegeChords.Length; i++)
        {
            var c = SiegeChords[i];
            for (var bar = 0; bar < 2; bar++)
            {
                var b = i * length + bar * 4 * SiegeBeat;
                for (var k = 0; k < hits.Length; k++) t.SawBass(b + hits[k] * SiegeBeat, SiegeBeat * 0.35, c.Bass + (k == 2 ? 24 : 12), k == 0 ? 0.19 : 0.13, 1300);
                for (var e = 0; e < 8; e++)
                {
                    t.Pluck(b + e * SiegeBeat / 2 + t.Jitter(0.004), c.Pad[figure[e % 4]], e % 4 == 0 ? 0.075 : 0.05, 0.2, e % 2 == 0 ? -0.35 : 0.35, 0.25, 0.2, 2600);
                }
                for (var h = 0; h < 4; h++) t.Hat(b + (h + 0.5) * SiegeBeat, 0.05, 0.3);
            }
        }
        return t;
    }

    /// <summary>Its third: drums in half time, a long tom fill every four bars, and the theme on brass, doubled an octave under.</summary>
    public static MusicTrack SiegeFury()
    {
        var t = SiegeTrack(101, 0.24);
        for (var bar = 0; bar < 16; bar++)
        {
            var at = bar * 4 * SiegeBeat;
            foreach (var k in new[] { 0, 0.75, 2.75, 3.25 }) t.Kick(at + k * SiegeBeat, 0.32);
            t.Snare(at + 2 * SiegeBeat, 0.34, 0.05);
            for (var h = 0; h < 8; h++) t.Hat(at + h * SiegeBeat / 2 + t.Jitter(0.004), h % 2 == 1 ? 0.08 : 0.05, -0.25, h == 7 && bar % 2 == 1);
            if (bar % 2 == 0) t.Hat(at, 0.09, 0.2, true);
            if (bar % 4 == 3)
            {
                int[] toms = [55, 52, 50, 47, 45, 43];
                for (var f = 0; f < toms.Length; f++) t.Tom(at + (2.5 + f / 4.0) * SiegeBeat, toms[f], 0.2, 0.5 - f * 0.2);
            }
        }
        Tune[][] tune =
        [
            [new(0, 3, 64), new(3, 1, 67), new(4, 4, 71)],
            [new(0, 3, 72), new(3, 1, 71), new(4, 4, 67)],
            [new(0, 3, 69), new(3, 1, 72), new(4, 4, 76)],
            [new(0, 4, 75), new(4, 4, 71)],
            [new(0, 3, 76), new(3, 1, 74), new(4, 4, 71)],
            [new(0, 3, 72), new(3, 1, 69), new(4, 4, 77)],
            [new(0, 3, 74), new(3, 1, 71), new(4, 4, 67)],
            [new(0, 2, 66), new(2, 2, 71), new(4, 4, 75)],
        ];
        for (var i = 0; i < tune.Length; i++)
        {
            foreach (var n in tune[i])
            {
                var at = (i * 8 + n.At) * SiegeBeat;
                t.Brass(at, n.Hold * SiegeBeat * 0.92, n.Midi, 0.09, 0.15);
                t.Brass(at, n.Hold * SiegeBeat * 0.92, n.Midi - 12, 0.05, -0.15);
            }
        }
        return t;
    }

    /// <summary>Victory: the battle's A minor turned to A major, brass swelling, a rising arpeggio, a bell.</summary>
    public static MusicTrack Victory()
    {
        var t = new MusicTrack(4, 6, 51, echoTime: 0.3, echoFeedback: 0.3, roomSize: 0.86, roomWet: 0.4);
        t.Kick(0, 0.25);
        t.Hat(0, 0.08, 0, true);
        foreach (var m in new[] { 57, 61, 64, 69 }) t.Brass(0, 1.8, m, 0.11, (m % 3 - 1) * 0.4);
        foreach (var m in new[] { 57, 61, 64 }) t.Pad(0.1, 2.6, m, 0.08, 2000, 0.6, 2);
        t.Bass(0, 2.4, 45, 0.12, 1.2);
        int[] up = [69, 73, 76, 81, 85];
        for (var i = 0; i < up.Length; i++) t.Pluck(0.15 + i * 0.12, up[i], 0.12, 0.6, -0.4 + i * 0.2, 0.3, 0.4, 4500);
        t.Bell(0.8, 88, 0.09);
        return t;
    }

    /// <summary>Defeat: a low D minor fading out under a slow fall of four notes.</summary>
    public static MusicTrack Defeat()
    {
        var t = new MusicTrack(4.5, 6, 53, echoTime: 0.45, echoFeedback: 0.35, roomSize: 0.88, roomWet: 0.45);
        t.Tom(0, 50, 0.25);
        foreach (var m in new[] { 50, 53, 57 }) t.Pad(0, 3, m, 0.1, 1200, 0.4, 2.4);
        t.Drone(0, 3, 38, 0.08, 400);
        int[] down = [74, 72, 70, 69];
        for (var i = 0; i < down.Length; i++) t.Pluck(0.3 + i * 0.55, down[i], 0.11, 1.1, 0.2 - i * 0.1, 0.35, 0.5, 3200);
        return t;
    }
}
