// Battle snapshots as deltas (not in the web version): written against the last one the player
// acknowledged, they rebuild on the replica the very snapshot the host wrote, through lost
// snapshots and lost acknowledgements; a snapshot whose baseline the player lacks changes
// nothing, a broken one is not kept, and a baseline forgotten makes the next snapshot whole.
using SandboxShip.Sim.Data;
using Xunit;
using Xunit.Abstractions;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim.Tests;

public class SnapshotDeltaTests(ITestOutputHelper output)
{
    static readonly Side Klaed = Faction.Klaed, Nairan = Faction.Nairan;

    static byte[] Whole(World w, Side viewer)
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms)) Snapshot.Write(w, viewer, bw, []);
        return ms.ToArray();
    }

    /// <summary>The baseline a snapshot names in its header: 0 for a whole one.</summary>
    static int Baseline(byte[] bytes)
    {
        using var r = new BinaryReader(new MemoryStream(bytes));
        r.ReadUInt32();
        r.ReadUInt16();
        r.Read7BitEncodedInt();
        r.Read7BitEncodedInt();
        return r.Read7BitEncodedInt();
    }

    /// <summary>A host serving one player by hand: the snapshots it writes, those that reach the player, the acknowledgements that come back.</summary>
    sealed class Link(World host, Side viewer)
    {
        public readonly World Host = host, Replica = new();
        public readonly SnapshotFrames Sent = new(), Read = new();
        public int Wholes, Deltas;
        public long WholeBytes, DeltaBytes;

        public byte[] Write()
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms)) Snapshot.Write(Host, viewer, w, [], null, Sent);
            var bytes = ms.ToArray();
            if (Baseline(bytes) == 0) (Wholes, WholeBytes) = (Wholes + 1, WholeBytes + bytes.Length);
            else (Deltas, DeltaBytes) = (Deltas + 1, DeltaBytes + bytes.Length);
            return bytes;
        }

        /// <summary>The snapshot reaches the player; read, it is acknowledged, unless that is lost.</summary>
        public bool Deliver(byte[] bytes, bool ack = true)
        {
            var read = Snapshot.Read(new BinaryReader(new MemoryStream(bytes)), Replica, Read);
            Replica.Events.Clear();
            if (read && ack) Sent.Ack(Read.Latest);
            return read;
        }

        /// <summary>
        /// Written whole, the replica is the host written whole: it holds the very snapshot, every
        /// entity's record to the byte. Shots it carried along their speed are within a hair of the host's.
        /// </summary>
        public void Same()
        {
            var whole = new World();
            Snapshot.Read(new BinaryReader(new MemoryStream(Whole(Host, viewer))), whole);
            Assert.Equal(whole.Projectiles.Select(p => (p.Id, p.Side, p.Kind, p.Vx, p.Vy)), Replica.Projectiles.Select(p => (p.Id, p.Side, p.Kind, p.Vx, p.Vy)));
            foreach (var (a, b) in whole.Projectiles.Zip(Replica.Projectiles)) Assert.True(Math.Abs(a.X - b.X) < 0.01 && Math.Abs(a.Y - b.Y) < 0.01, $"shot {a.Id}");
            Assert.Equal(Shotless(Host), Shotless(Replica));
        }

        byte[] Shotless(World w)
        {
            var shots = w.Projectiles;
            w.Projectiles = [];
            try
            {
                return Whole(w, viewer);
            }
            finally
            {
                w.Projectiles = shots;
            }
        }
    }

    /// <summary>The stress battle, fleets engaged: ships and parts dying, craft launched, foes coming in and out of sight.</summary>
    static World Fight()
    {
        var w = Stress.Create();
        Steps.Run(w, 10);
        w.Events.Clear();
        return w;
    }

    static void Twentieth(World w)
    {
        Steps.Run(w, 3 * Steps.Step);
        w.Events.Clear();
    }

    [Fact]
    public void Deltas_rebuild_on_the_replica_the_very_snapshot_the_host_wrote()
    {
        var link = new Link(Fight(), Klaed);
        var whole = Whole(link.Host, Klaed).Length;
        for (var i = 0; i < 60; i++)
        {
            Twentieth(link.Host);
            Assert.True(link.Deliver(link.Write()));
            if (i % 6 == 0) link.Same();
        }
        link.Same();
        // Only the first went whole; the others are a fraction of it.
        Assert.Equal(1, link.Wholes);
        var delta = link.DeltaBytes / (double)link.Deltas;
        output.WriteLine($"Stress, {link.Host.Entities.Count} entities: whole {whole} bytes, deltas at 20 a second {delta:0} bytes on average");
        Expect.Less(delta, whole / 2.0);
    }

    [Fact]
    public void Lost_snapshots_and_lost_acknowledgements_leave_the_replica_right()
    {
        var link = new Link(Fight(), Klaed);
        var (wholes, deltas) = (0, 0);
        for (var i = 0; i < 120; i++)
        {
            Twentieth(link.Host);
            var bytes = link.Write();
            // One snapshot in three lost on the way, one acknowledgement in four; then forty in a row, longer than the host keeps a baseline.
            if (i % 3 == 1 || i is >= 50 and < 90) continue;
            Assert.True(link.Deliver(bytes, ack: i % 4 != 2));
            link.Same();
            if (Baseline(bytes) == 0) wholes++;
            else deltas++;
            // After the long loss, snapshots go whole until the player acknowledges one: its baseline was forgotten.
            if (i < 50) Assert.Equal(i == 0, Baseline(bytes) == 0);
        }
        Assert.InRange(wholes, 2, 3);
        Expect.Greater(deltas, 40);
    }

    [Fact]
    public void A_delta_against_a_snapshot_the_player_lacks_changes_nothing()
    {
        var link = new Link(Fight(), Klaed);
        Assert.True(link.Deliver(link.Write()));
        Twentieth(link.Host);
        var delta = link.Write();
        Assert.NotEqual(0, Baseline(delta));
        // Another player, who never read its baseline, and a reader that keeps none.
        var stranger = new World();
        var frames = new SnapshotFrames();
        Assert.False(Snapshot.Read(new BinaryReader(new MemoryStream(delta)), stranger, frames));
        Assert.False(Snapshot.Read(new BinaryReader(new MemoryStream(delta)), stranger));
        Assert.Empty(stranger.Entities);
        Assert.Empty(stranger.Factions);
        Assert.Equal(0, stranger.Time);
        Assert.Equal(0, frames.Latest);
        // An acknowledgement of a snapshot never sent is ignored: the next delta is still against the one read.
        link.Sent.Ack(9999);
        Twentieth(link.Host);
        var next = link.Write();
        Assert.Equal(link.Read.Latest, Baseline(next));
        Assert.True(link.Deliver(next));
        link.Same();
    }

    [Fact]
    public void A_broken_delta_is_not_kept_and_the_next_one_mends_the_replica()
    {
        var link = new Link(Fight(), Klaed);
        Assert.True(link.Deliver(link.Write()));
        var good = link.Read.Latest;
        Twentieth(link.Host);
        var delta = link.Write();
        foreach (var cut in new[] { delta.Length - 3, delta.Length / 2 })
        {
            var ex = Record.Exception(() => link.Deliver(delta[..cut]));
            Assert.True(ex is EndOfStreamException or InvalidDataException, ex?.ToString());
            Assert.Equal(good, link.Read.Latest);
        }
        // Not acknowledged: the host writes against the last one read, which mends everything.
        Twentieth(link.Host);
        var next = link.Write();
        Assert.Equal(good, Baseline(next));
        Assert.True(link.Deliver(next));
        link.Same();
    }

    [Fact]
    public void Entities_that_come_go_and_are_remembered_follow_the_host_through_deltas()
    {
        var w = CreateWorld(6000, 4000);
        w.Fog = true;
        AddFaction(w, Faction.Klaed, 0);
        AddFaction(w, Faction.Nairan, 0);
        var eye = SpawnShip(w, Klaed, ShipClass.Scout, 1000, 1000);
        var doomed = SpawnShip(w, Klaed, ShipClass.Frigate, 900, 1000);
        var foe = SpawnShip(w, Nairan, ShipClass.Dreadnought, 1600, 1000, Math.PI);
        var platform = SpawnStructure(w, Nairan, BuildableType.Platform, 1500, 1500);
        w.Factions[Nairan].AI = null;
        var link = new Link(w, Klaed);
        void Sync()
        {
            Steps.Run(w, 0.5);
            w.Events.Clear();
            Assert.True(link.Deliver(link.Write()));
            link.Same();
        }
        Sync();
        Assert.True(link.Replica.Entities.Has(foe.Id) && link.Replica.Entities.Has(platform.Id));
        var turret = ChildrenOf(w, foe.Id).First(p => p.Weapons.Count > 0);
        // Its parts sit on it, put there by the replica.
        var shown = link.Replica.Entities.Get(turret.Id)!;
        Assert.True(Math.Abs(shown.X - turret.X) < 0.02 && Math.Abs(shown.Y - turret.Y) < 0.02);
        // Destroyed: a frigate, a turret off the dreadnought; and a ship comes.
        doomed.Hull = 0;
        turret.Hull = 0;
        var newcomer = SpawnShip(w, Klaed, ShipClass.Bomber, 1100, 900);
        Sync();
        Assert.False(link.Replica.Entities.Has(doomed.Id) || link.Replica.Entities.Has(turret.Id));
        Assert.True(link.Replica.Entities.Has(newcomer.Id));
        // Out of sight: the dreadnought goes, parts and all; the platform stays, remembered.
        eye.X = 4500;
        newcomer.X = 4600;
        (foe.X, foe.Y) = (5800, 3800);
        Movement.IssueStop(w, [eye.Id, newcomer.Id, foe.Id]);
        Sync();
        Assert.False(link.Replica.Entities.Has(foe.Id));
        Assert.True(link.Replica.Entities.Has(platform.Id));
        Assert.True(Vision.WasSeen(link.Replica, Klaed, link.Replica.Entities.Get(platform.Id)!));
        Assert.False(Vision.IsVisible(link.Replica, Klaed, link.Replica.Entities.Get(platform.Id)!));
        // Back in sight, both: the platform whole again, the dreadnought with its parts.
        (eye.X, eye.Y) = (1000, 1000);
        (foe.X, foe.Y) = (1600, 1000);
        Movement.IssueStop(w, [eye.Id, foe.Id]);
        Sync();
        Assert.True(Vision.IsVisible(link.Replica, Klaed, link.Replica.Entities.Get(platform.Id)!));
        Assert.True(link.Replica.Entities.Has(foe.Id));
        Assert.Equal(ChildrenOf(w, foe.Id).Count, link.Replica.Entities.Count(e => e.ParentId == foe.Id));
        // Destroyed out of sight, remembered no more.
        eye.X = 4500;
        Movement.IssueStop(w, [eye.Id]);
        Sync();
        platform.Hull = 0;
        Sync();
        Assert.False(link.Replica.Entities.Has(platform.Id));
        Assert.Equal(1, link.Wholes);
    }
}
