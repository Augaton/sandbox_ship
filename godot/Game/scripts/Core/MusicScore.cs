// What the music plays: chords, basses, arpeggios and drums, written out for MusicTrack. Each
// theme is a loop of whole bars; the battle's three layers share one loop, to be heard together.
namespace SandboxShip.Core;

public static class MusicScore
{
    /// <summary>A chord: its bass note and the pad's notes, MIDI.</summary>
    readonly record struct Chord(int Bass, int[] Pad);

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
