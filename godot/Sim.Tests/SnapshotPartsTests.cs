// Battle snapshots in parts (not in the web version): read together, the parts bring a replica
// to exactly what the whole snapshot does; each fits a packet; one lost leaves only what it
// covers behind, the snapshot unacknowledged, and the next one mends it.
using SandboxShip.Sim.Data;
using Xunit;
using Xunit.Abstractions;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim.Tests;

public class SnapshotPartsTests(ITestOutputHelper output)
{
    static readonly Side Viewer = Faction.Klaed;

    /// <summary>The host's world, and two players fed the same: one whole snapshots, the other in parts.</summary>
    sealed class Pair
    {
        public readonly World World;
        readonly SnapshotFrames wholeSent = new(), partsSent = new(), wholeRead = new(), partsRead = new();
        public readonly World Whole = new(), Parts = new();

        public Pair(World world) => World = world;

        public void Step(double seconds)
        {
            for (var i = Js.Round(seconds / Steps.Step); i > 0; i--)
            {
                Steps.StepWorld(World);
                World.Events.Clear();
            }
        }

        /// <summary>One snapshot each; `lose` drops the part of that index. The parts written, and the snapshot they completed (0: none).</summary>
        public (List<byte[]> Parts, int Completed) Send(int lose = -1)
        {
            using (var ms = new MemoryStream())
            {
                using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) Snapshot.Write(World, Viewer, w, [], null, wholeSent);
                ms.Position = 0;
                Assert.True(Snapshot.Read(new BinaryReader(ms), Whole, wholeRead));
                wholeSent.Ack(wholeRead.Latest);
            }
            var parts = Snapshot.WriteParts(World, Viewer, [], null, partsSent);
            var completed = 0;
            for (var i = 0; i < parts.Count; i++)
            {
                if (i == lose) continue;
                var p = Snapshot.ReadPart(new BinaryReader(new MemoryStream(parts[i])), Parts, partsRead);
                Assert.True(p.Read);
                completed = p.Completed;
            }
            if (completed > 0) partsSent.Ack(completed);
            return (parts, completed);
        }
    }

    static void Same(World a, World b)
    {
        Assert.Equal(a.Time, b.Time);
        Assert.Equal(a.Entities.Select(e => e.Id).Order(), b.Entities.Select(e => e.Id).Order());
        foreach (var e in a.Entities)
        {
            var f = b.Entities.Get(e.Id)!;
            Assert.Equal((e.X, e.Y, e.Angle, e.Hull, e.Shield, e.Type, e.Side), (f.X, f.Y, f.Angle, f.Hull, f.Shield, f.Type, f.Side));
        }
        // A shot flying on is carried along its speed from the baseline: from another baseline, to the same place but its last bits.
        Assert.Equal(a.Projectiles.Select(p => (p.Id, p.Vx, p.Vy)), b.Projectiles.Select(p => (p.Id, p.Vx, p.Vy)));
        Assert.All(a.Projectiles.Zip(b.Projectiles), q => Assert.True(Math.Abs(q.First.X - q.Second.X) + Math.Abs(q.First.Y - q.Second.Y) < 0.01));
        Assert.Equal(a.Beams, b.Beams);
        Assert.Equal(a.Factions[Viewer].Credits, b.Factions[Viewer].Credits);
    }

    [Fact]
    public void Read_together_the_parts_bring_the_replica_where_the_whole_snapshot_does()
    {
        var t = new Pair(Stress.Create());
        for (var i = 0; i < 12; i++)
        {
            t.Step(0.05);
            var (parts, completed) = t.Send();
            Assert.True(completed > 0);
            Assert.True(parts.Count > 1);
            Same(t.Whole, t.Parts);
        }
    }

    [Fact]
    public void Each_part_of_a_big_battles_delta_fits_a_packet()
    {
        var t = new Pair(Stress.Create());
        t.Step(5);
        t.Send();
        for (var i = 0; i < 6; i++)
        {
            t.Step(0.05);
            var (parts, _) = t.Send();
            // The budget, the part's header, and at most one record past the budget.
            Assert.All(parts, p => Assert.True(p.Length < 1300, $"{p.Length} octets"));
        }
    }

    [Fact]
    public void A_lost_part_leaves_only_what_it_covers_behind_and_the_next_snapshot_mends_it()
    {
        var t = new Pair(Stress.Create());
        t.Step(2);
        t.Send();
        t.Step(0.05);
        t.Send();
        t.Step(0.05);
        // The first part of entities lost: the snapshot is not whole, so not acknowledged.
        var (parts, completed) = t.Send(lose: 1);
        Assert.Equal(0, completed);
        // What the other parts cover moved on: the shots, as the whole snapshot has them.
        Assert.Equal(t.Whole.Projectiles.Select(p => p.Id), t.Parts.Projectiles.Select(p => p.Id));
        Assert.Equal(t.Whole.Time, t.Parts.Time);
        t.Step(0.05);
        // The next one, against an older snapshot, reads and mends everything.
        Assert.True(t.Send().Completed > 0);
        Same(t.Whole, t.Parts);
    }

    [Fact]
    public void Measure_what_a_big_battle_sends_whole_and_in_parts()
    {
        var w = Stress.Create();
        var sentWhole = new SnapshotFrames();
        var sentParts = new SnapshotFrames();
        Steps.Run(w, 8);
        var (whole, count, biggest, packets, inParts) = (0L, 0L, 0, 0L, 0L);
        const int Mtu = 1200;
        for (var i = 0; i < 40; i++)
        {
            Steps.Run(w, 0.05);
            using var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) Snapshot.Write(w, Viewer, bw, [], null, sentWhole);
            var parts = Snapshot.WriteParts(w, Viewer, [], null, sentParts);
            if (i < 2) continue;
            // Acknowledged at once: deltas against the snapshot before, as a player that loses nothing has them.
            sentWhole.Ack(sentWhole.Latest);
            sentParts.Ack(sentParts.Latest);
            whole += ms.Length;
            packets += (ms.Length + Mtu - 1) / Mtu;
            count += parts.Count;
            inParts += parts.Sum(p => p.Length);
            biggest = Math.Max(biggest, parts.Max(p => p.Length));
        }
        var n = 38.0;
        output.WriteLine($"{w.Entities.Count} entités : delta entier {whole / n / 1024:0.0} Kio, soit {packets / n:0.0} fragments de {Mtu} octets ; "
            + $"en parties {count / n:0.0} parties, {inParts / n / 1024:0.0} Kio en tout, la plus grosse {biggest} octets");
        Assert.True(biggest < 1300);
    }
}
