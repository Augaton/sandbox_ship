// The battle modes in snapshots (not in the web version): each mode's state reads back to the
// very bytes and into the replica's own, the escorts excepted; the modes' news reach every
// player; a replica of a zone control game shows the host's scores and beacons all along; in
// regicide, a foe flagship out of sight comes by its place only, for the minimap.
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim.Tests;

public class SnapshotModesTests
{
    static readonly Side Klaed = Faction.Klaed, Nairan = Faction.Nairan, Nautolan = Faction.Nautolan;

    static byte[] Write(World w, Side viewer, IReadOnlyList<SimEvent>? events = null)
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms)) Snapshot.Write(w, viewer, bw, events);
        return ms.ToArray();
    }

    static void Read(byte[] bytes, World replica) => Snapshot.Read(new BinaryReader(new MemoryStream(bytes)), replica);

    /// <summary>Kla'ed against Nairan on a small map; in an assault, Kla'ed defends.</summary>
    static World Battle(BattleMode mode, Difficulty? ai = Difficulty.Normal) => Scenario.CreateSkirmish(new SkirmishOptions
    {
        Mode = mode, Seed = 9, Size = MapSize.Small, ZoneTarget = 100, AssaultMinutes = 10,
        Slots = mode == BattleMode.Assault
            ? [new(Klaed, Faction.Klaed, Assault.Defenders, ai), new(Nairan, Faction.Nairan, Assault.Attackers, ai)]
            : [new(Klaed, Faction.Klaed, 0, ai), new(Nairan, Faction.Nairan, 0, ai)],
    });

    static void SameMode(ModeState h, ModeState r)
    {
        Assert.Equal(h.Kind, r.Kind);
        Assert.Equal((h.Wave, h.Cleared, h.NextWave, h.ZoneTarget, h.Deadline), (r.Wave, r.Cleared, r.NextWave, r.ZoneTarget, r.Deadline));
        Assert.Equal(h.Beacons.Select(b => ((float)b.X, (float)b.Y, b.Holder, b.Claim, (float)b.Progress, b.Contested)),
            r.Beacons.Select(b => ((float)b.X, (float)b.Y, b.Holder, b.Claim, (float)b.Progress, b.Contested)));
        Assert.Equal(h.Scores.OrderBy(s => s.Key.Id), r.Scores.OrderBy(s => s.Key.Id));
        Assert.Equal(h.Flagships.OrderBy(s => s.Key.Id), r.Flagships.OrderBy(s => s.Key.Id));
        // The escorts are the host AI's business.
        Assert.Empty(r.Escorts);
    }

    [Theory]
    [InlineData(BattleMode.Standard)]
    [InlineData(BattleMode.Survival)]
    [InlineData(BattleMode.Zones)]
    [InlineData(BattleMode.Assault)]
    [InlineData(BattleMode.Regicide)]
    public void Each_modes_state_reads_back_to_the_very_bytes_and_into_the_replica(BattleMode mode)
    {
        var w = Battle(mode);
        // Survival: once the first wave is in.
        Steps.Run(w, mode == BattleMode.Survival ? Survival.Grace + 5 : 20);
        var bytes = Write(w, Klaed);
        var replica = new World();
        Read(bytes, replica);
        Assert.Equal(bytes, Write(replica, Klaed));
        SameMode(w.Mode, replica.Mode);
        // The test saw what it is about.
        switch (mode)
        {
            case BattleMode.Survival: Assert.True(w.Mode.Wave >= 1 && double.IsFinite(w.Mode.NextWave)); break;
            case BattleMode.Zones: Assert.Equal(3, replica.Mode.Beacons.Count); break;
            case BattleMode.Assault: Assert.True(double.IsFinite(replica.Mode.Deadline)); break;
            case BattleMode.Regicide: Assert.Equal(2, replica.Mode.Flagships.Count); break;
        }
    }

    [Fact]
    public void Every_player_hears_the_news_of_the_modes()
    {
        SimEvent[] news =
        [
            new WaveIncoming(3, 1200.5, 800.25, ShipClass.Titan), new WaveIncoming(4, 10, 20, null), new WaveCleared(3),
            new BeaconTaken(1, Nairan), new BeaconLost(2, Nautolan), new FlagshipLost(Nairan, 300.5, 400.75), new ClockWarning(120),
        ];
        var replica = new World();
        Read(Write(Battle(BattleMode.Zones, null), Klaed, news), replica);
        Assert.Equal(news, replica.Events);
    }

    /// <summary>A ship that will not die, its guns silent: only its presence counts at a beacon.</summary>
    static void Sentinel(World w, Side side, IPoint at)
    {
        var e = SpawnShip(w, side, ShipClass.Bulwark, at.X, at.Y);
        e.Hull = e.MaxHull = 1e9;
        e.Weapons = [];
    }

    [Fact]
    public void A_replica_of_a_zone_control_game_shows_the_hosts_scores_and_beacons()
    {
        var host = Battle(BattleMode.Zones, null);
        var b = host.Mode.Beacons;
        // Kla'ed sits on A, Nairan on C, both on the center beacon: two taken, one contested.
        Sentinel(host, Klaed, b[0]);
        Sentinel(host, Nairan, b[2]);
        Sentinel(host, Klaed, b[1]);
        Sentinel(host, Nairan, b[1]);
        var replica = new World();
        var heard = new List<SimEvent>();
        for (var step = 1; step <= 40 * 60; step++)
        {
            Steps.StepWorld(host);
            // Twenty snapshots a second, each with the events since the last one.
            if (step % 3 != 0) continue;
            Read(Write(host, Klaed), replica);
            host.Events.Clear();
            heard.AddRange(replica.Events);
            replica.Events.Clear();
            SameMode(host.Mode, replica.Mode);
        }
        Assert.Equal([Klaed, null, Nairan], replica.Mode.Beacons.Select(x => x.Holder));
        Assert.True(replica.Mode.Beacons[1].Contested);
        Expect.Greater(replica.Mode.Scores[Klaed], 0);
        Expect.Greater(replica.Mode.Scores[Nairan], 0);
        // Beacon C is far from any Kla'ed eye: its taking is heard all the same.
        Assert.Contains(new BeaconTaken(0, Klaed), heard);
        Assert.Contains(new BeaconTaken(2, Nairan), heard);
    }

    [Fact]
    public void In_regicide_a_foe_flagship_out_of_sight_comes_by_its_place_only()
    {
        var w = Battle(BattleMode.Regicide, null);
        Steps.Run(w, 1);
        var foe = w.Entities.Get(w.Mode.Flagships[Nairan])!;
        Assert.False(Vision.IsVisible(w, Klaed, foe));
        var replica = new World();
        Read(Write(w, Klaed), replica);
        var shown = replica.Entities.Get(foe.Id)!;
        Assert.Equal(((float)foe.X, (float)foe.Y), ((float)shown.X, (float)shown.Y));
        Assert.False(Vision.IsVisible(replica, Klaed, shown));
        Assert.False(Vision.WasSeen(replica, Klaed, shown));
        Assert.DoesNotContain(replica.Entities, e => e.ParentId == foe.Id);
        // It moves: the replica follows; once in sight, it comes whole, parts and all.
        foe.X += 200;
        Read(Write(w, Klaed), replica);
        Assert.Equal((float)foe.X, (float)shown.X);
        (foe.X, foe.Y) = (StationOf(w, Klaed)!.X + 300, StationOf(w, Klaed)!.Y);
        Steps.Run(w, 0.5);
        Assert.True(Vision.IsVisible(w, Klaed, foe));
        Read(Write(w, Klaed), replica);
        Assert.True(Vision.IsVisible(replica, Klaed, shown));
        Assert.Equal((float)foe.Hull, (float)shown.Hull);
        Assert.Equal(ChildrenOf(w, foe.Id).Select(p => p.Id).Order(), replica.Entities.Where(e => e.ParentId == foe.Id).Select(e => e.Id).Order());
    }
}
