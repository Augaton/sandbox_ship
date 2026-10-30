// Battle snapshots (not in the web version): what a host sends each player of the battle, and
// the replica the player's screen draws from. A replica reads back to the same bytes, holds what
// its viewer's team sees and nothing else, follows the host step after step, and loses what the
// host lost. A snapshot of another version is refused before anything changes.
using SandboxShip.Sim.Data;
using Xunit;
using Xunit.Abstractions;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim.Tests;

public class SnapshotTests(ITestOutputHelper output)
{
    static readonly Side Klaed = Faction.Klaed, Nairan = Faction.Nairan, Nautolan = Faction.Nautolan;

    static byte[] Write(World w, Side viewer, IReadOnlyList<SimEvent>? events = null)
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms)) Snapshot.Write(w, viewer, bw, events);
        return ms.ToArray();
    }

    static void Read(byte[] bytes, World replica) => Snapshot.Read(new BinaryReader(new MemoryStream(bytes)), replica);

    /// <summary>What a snapshot to `viewer` should hold: its team's, the foes it sees, and the foes' structures it remembers; parts with what they belong to.</summary>
    static Dictionary<int, bool> Shown(World w, Side viewer)
    {
        var shown = new Dictionary<int, bool>();
        foreach (var e in w.Entities)
        {
            if (e.ParentId is int p && !shown.ContainsKey(p)) continue;
            if (Vision.IsVisible(w, viewer, e)) shown[e.Id] = false;
            else if (e.Kind != EntityKind.Ship && Vision.WasSeen(w, viewer, e)) shown[e.Id] = true;
        }
        return shown;
    }

    static void Near(double expected, double actual, double tolerance, string what) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance * Math.Max(1, Math.Abs(expected)), $"{what}: {actual}, expected {expected}");

    /// <summary>The replica holds exactly what its viewer may see of the host, as the host has it now.</summary>
    static (int Foes, int Remembered) SameAsHost(World host, World replica, Side viewer)
    {
        var shown = Shown(host, viewer);
        Assert.Equal(shown.Keys.Order(), replica.Entities.Select(e => e.Id).Order());
        Assert.Equal(host.Time, replica.Time);
        Assert.Equal(host.Winner, replica.Winner);
        Assert.Equal(host.Fog, replica.Fog);
        int foes = 0, remembered = 0;
        foreach (var r in replica.Entities)
        {
            var h = host.Entities.Get(r.Id)!;
            var what = $"{h.Type} {h.Id}";
            Assert.Equal((h.Kind, h.Type, h.Side, h.Race, h.Level, h.ParentId, h.Hero), (r.Kind, r.Type, r.Side, r.Race, r.Level, r.ParentId, r.Hero));
            Near(h.X, r.X, 1e-6, what);
            Near(h.Y, r.Y, 1e-6, what);
            Assert.Equal(Vision.IsVisible(host, viewer, h), Vision.IsVisible(replica, viewer, r));
            if (Foes(host, h.Side, viewer)) foes++;
            if (shown[r.Id])
            {
                remembered++;
                Assert.False(Vision.IsVisible(replica, viewer, r));
                Assert.True(Vision.WasSeen(replica, viewer, r));
                continue;
            }
            Near(h.Hull, r.Hull, 1e-6, what);
            Near(h.MaxHull, r.MaxHull, 1e-6, what);
            Near(h.Shield, r.Shield, 1e-6, what);
            Near(h.Build, r.Build, 1e-6, what);
            Near(0, Movement.WrapAngle(h.Angle - r.Angle), 1e-4, what);
            Near(h.Speed, r.Speed, 1e-3, what);
            Assert.Equal((h.TargetId, h.Launched, h.Weapons.Count, h.Engines, h.ShieldGens, h.Hangar?.Launched.Count), (r.TargetId, r.Launched, r.Weapons.Count, r.Engines, r.ShieldGens, r.Hangar?.Launched.Count));
            Assert.Equal(h.Abilities.Select(a => a.Id), r.Abilities.Select(a => a.Id));
            for (var i = 0; i < h.Abilities.Count; i++)
            {
                Near(h.Abilities[i].Cooldown, r.Abilities[i].Cooldown, 1e-6, what);
                Near(h.Abilities[i].Active, r.Abilities[i].Active, 1e-6, what);
            }
            Near(h.Effects.Stunned, r.Effects.Stunned, 1e-6, what);
            if (h.Side != viewer) continue;
            Assert.Equal(h.Order?.Type, r.Order?.Type);
            Assert.Equal(h.Pilot is null, r.Pilot is null);
        }
        foreach (var (side, h) in host.Factions)
        {
            var r = replica.Factions[side];
            Assert.Equal((h.Race, h.Team, h.Color, h.StationId, h.HadStation, h.Stats.Kills, h.Stats.Lost), (r.Race, r.Team, r.Color, r.StationId, r.HadStation, r.Stats.Kills, r.Stats.Lost));
        }
        Assert.Equal(host.Factions.Keys, replica.Factions.Keys);
        var (hf, rf) = (host.Factions[viewer], replica.Factions[viewer]);
        Assert.Equal(hf.Credits, rf.Credits);
        Assert.Equal(hf.Queue.Select(q => (q.Kind, q.Cls, q.Hero)), rf.Queue.Select(q => (q.Kind, q.Cls, q.Hero)));
        Assert.Equal(hf.BuildQueue.Select(q => (q.Kind, q.TargetId)), rf.BuildQueue.Select(q => (q.Kind, q.TargetId)));
        Assert.Equal(Techs.All.Select(t => hf.Research[t]), Techs.All.Select(t => rf.Research[t]));
        Assert.Equal(ResearchQueue.Jobs(hf).Select(j => j.Tech), ResearchQueue.Jobs(rf).Select(j => j.Tech));
        Assert.Equal(hf.Repeat, rf.Repeat);
        Assert.Equal(hf.HeroCooldowns.Keys.Order(), rf.HeroCooldowns.Keys.Order());
        return (foes, remembered);
    }

    [Fact]
    public void A_replica_writes_back_the_very_bytes_it_was_read_from()
    {
        var w = Stress.Create();
        Steps.Run(w, 5);
        foreach (var fog in new[] { true, false })
        {
            w.Fog = fog;
            var bytes = Write(w, Klaed);
            var replica = new World();
            Read(bytes, replica);
            Assert.Equal(bytes, Write(replica, Klaed));
            // Read again over itself, the replica stays the same: entities update in place.
            var ids = replica.Entities.Select(e => e).ToList();
            replica.Events.Clear();
            Read(bytes, replica);
            Assert.Equal(bytes, Write(replica, Klaed));
            Assert.Equal(ids, replica.Entities.Select(e => e));
        }
    }

    [Fact]
    public void A_foe_out_of_the_viewers_sight_never_shows_in_its_snapshot()
    {
        var w = CreateWorld(6000, 4000);
        w.Fog = true;
        foreach (var f in new[] { Faction.Klaed, Faction.Nairan, Faction.Nautolan }) AddFaction(w, f, 0);
        SpawnStation(w, Klaed, 800, 2000);
        var hiddenStation = SpawnStation(w, Nairan, 5300, 900);
        SpawnStation(w, Nautolan, 5300, 3100);
        var mine = SpawnShip(w, Klaed, ShipClass.Frigate, 1000, 2000);
        var seen = SpawnShip(w, Nairan, ShipClass.Frigate, 1500, 2100, Math.PI);
        // Hidden: Nairan and Nautolan fighting far off, a dreadnought with its parts, and a cloaked ace close by.
        var far = Enumerable.Range(0, 4).Select(i => SpawnShip(w, Nairan, ShipClass.Frigate, 4800, 1800 + i * 60)).ToList();
        far.AddRange(Enumerable.Range(0, 4).Select(i => SpawnShip(w, Nautolan, ShipClass.Frigate, 5150, 1800 + i * 60, Math.PI)));
        var dreadnought = SpawnShip(w, Nautolan, ShipClass.Dreadnought, 5000, 2600);
        var ace = SpawnHero(w, Nairan, new HeroId(Faction.Nairan, HeroRole.Ace), 1300, 1800);
        Steps.Run(w, 2);
        // Until a shot of the far fight is in flight.
        for (var i = 0; i < 600 && !w.Projectiles.Exists(p => p.X > 4000); i++) Steps.StepWorld(w);
        ace.Abilities[0].Active = 5;
        Assert.Equal(AbilityId.Cloak, ace.Abilities[0].Id);
        Assert.Contains(w.Projectiles, p => p.X > 4000);
        Assert.Contains(w.Events, e => e is Hit { X: > 4000 });
        var replica = new World();
        Read(Write(w, Klaed), replica);
        Assert.True(replica.Entities.Has(mine.Id));
        Assert.True(replica.Entities.Has(seen.Id));
        Assert.True(Vision.IsVisible(replica, Klaed, replica.Entities.Get(seen.Id)!));
        int[] hidden = [.. far.Select(s => s.Id), dreadnought.Id, .. ChildrenOf(w, dreadnought.Id).Select(p => p.Id), ace.Id, hiddenStation.Id];
        Assert.All(hidden, id => Assert.False(replica.Entities.Has(id)));
        Assert.All(replica.Entities, e => Assert.True(!Foes(w, e.Side, Klaed) || Vision.IsVisible(w, Klaed, w.Entities.Get(e.Id)!)));
        // Nor do the shots, hits and losses of that far fight.
        Assert.DoesNotContain(replica.Projectiles, p => p.X > 4000);
        Assert.DoesNotContain(replica.Events, e => e is Hit { X: > 4000 } or Destroyed { X: > 4000 });
        Assert.All(w.Projectiles.Where(p => p.Side == Klaed), p => Assert.Contains(replica.Projectiles, q => q.Id == p.Id));
        // Every side is listed, but only the viewer's own economy is told.
        w.Factions[Nairan].Credits = 1234;
        Read(Write(w, Klaed), replica);
        Assert.Equal(3, replica.Factions.Count);
        Assert.Equal(0, replica.Factions[Nairan].Credits);
    }

    /// <summary>
    /// A small skirmish of AIs, Kla'ed and Nairan, with fleets that meet at once between their
    /// stations; a Kla'ed scout starts by the Nairan station, to see it before it is brought home.
    /// </summary>
    static World Clash()
    {
        var w = Scenario.CreateSkirmish(new SkirmishOptions
        {
            Slots = [new Slot(Klaed, Faction.Klaed, AI: Difficulty.Normal), new Slot(Nairan, Faction.Nairan, AI: Difficulty.Normal)],
            Seed = 3, Size = MapSize.Small,
        });
        var (a, b) = (StationOf(w, Klaed)!, StationOf(w, Nairan)!);
        ShipClass[] fleet = [ShipClass.Frigate, ShipClass.Frigate, ShipClass.Frigate, ShipClass.Bomber, ShipClass.Carrier, ShipClass.Dreadnought, ShipClass.Support];
        foreach (var (side, from, to) in new[] { (Klaed, a, b), (Nairan, b, a) })
        {
            var (dx, dy) = ((to.X - from.X) / Dist(from, to), (to.Y - from.Y) / Dist(from, to));
            var mid = new Point((a.X + b.X) / 2 - dx * 450, (a.Y + b.Y) / 2 - dy * 450);
            for (var i = 0; i < fleet.Length; i++) SpawnShip(w, side, fleet[i], mid.X - dy * (i - 3) * 90, mid.Y + dx * (i - 3) * 90, Math.Atan2(dy, dx));
            w.Factions[side].AutoAbilities = true;
        }
        SpawnShip(w, Klaed, ShipClass.Scout, b.X + 200, b.Y);
        return w;
    }

    [Fact]
    public void A_replica_kept_up_by_snapshots_matches_the_host_on_all_its_viewer_sees()
    {
        var host = Clash();
        var replica = new World();
        var events = new List<SimEvent>();
        var (foes, remembered) = (0, 0);
        for (var step = 1; step <= 90 * 60; step++)
        {
            Steps.StepWorld(host);
            // The scout has seen the Nairan station: home it comes, and the station is only remembered.
            if (step == 30 && host.Entities.LastOrDefault(e => e.Type == "scout") is { } scout)
            {
                var home = StationOf(host, Klaed)!;
                (scout.X, scout.Y) = (home.X, home.Y + 150);
                Movement.IssueStop(host, [scout.Id]);
            }
            // Twenty snapshots a second, each with the events since the last one.
            if (step % 3 != 0) continue;
            Read(Write(host, Klaed), replica);
            host.Events.Clear();
            events.AddRange(replica.Events);
            replica.Events.Clear();
            if (step % 600 != 0) continue;
            var (f, r) = SameAsHost(host, replica, Klaed);
            (foes, remembered) = (Math.Max(foes, f), Math.Max(remembered, r));
        }
        // The test saw what it is about: foes in sight, structures remembered, events of every common kind.
        Expect.Greater(foes, 10);
        Expect.Greater(remembered, 0);
        Assert.Contains(events, e => e is Hit);
        Assert.Contains(events, e => e is Destroyed);
        Assert.Contains(events, e => e is AbilityUsed);
    }

    [Fact]
    public void What_the_host_loses_the_replica_loses()
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
        var replica = new World();
        void Sync()
        {
            Steps.Run(w, 0.5);
            Read(Write(w, Klaed), replica);
        }
        Sync();
        Assert.True(replica.Entities.Has(doomed.Id) && replica.Entities.Has(foe.Id) && replica.Entities.Has(platform.Id));
        var turret = ChildrenOf(w, foe.Id).First(p => p.Weapons.Count > 0);
        Assert.True(replica.Entities.Has(turret.Id));
        // Destroyed: the frigate, a turret off the dreadnought.
        doomed.Hull = 0;
        turret.Hull = 0;
        Sync();
        Assert.False(replica.Entities.Has(doomed.Id));
        Assert.False(replica.Entities.Has(turret.Id));
        Assert.Contains(replica.Events, e => e is Destroyed d && d.Id == doomed.Id);
        // Out of sight: the dreadnought goes, parts and all; the platform stays, remembered.
        eye.X = 4500;
        (foe.X, foe.Y) = (5800, 3800);
        Movement.IssueStop(w, [eye.Id, foe.Id]);
        Sync();
        Assert.False(replica.Entities.Has(foe.Id));
        Assert.DoesNotContain(replica.Entities, e => e.ParentId == foe.Id);
        Assert.True(replica.Entities.Has(platform.Id));
        Assert.False(Vision.IsVisible(replica, Klaed, replica.Entities.Get(platform.Id)!));
        Assert.True(Vision.WasSeen(replica, Klaed, replica.Entities.Get(platform.Id)!));
        // Destroyed out of sight, remembered no more.
        platform.Hull = 0;
        Sync();
        Assert.False(replica.Entities.Has(platform.Id));
    }

    [Fact]
    public void A_snapshot_of_another_version_is_refused_before_anything_changes()
    {
        var w = Stress.Create();
        var bytes = Write(w, Klaed);
        var replica = new World();
        foreach (var at in new[] { 0, 4 })
        {
            var broken = (byte[])bytes.Clone();
            broken[at]++;
            Assert.Throws<InvalidDataException>(() => Read(broken, replica));
            Assert.Empty(replica.Entities);
            Assert.Empty(replica.Factions);
            Assert.Equal(0, replica.Time);
        }
        // Version 1 reads.
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 4));
        Read(bytes, replica);
        Assert.NotEmpty(replica.Entities);
    }

    [Fact]
    public void Snapshot_sizes_of_a_huge_battle_and_of_a_duel()
    {
        // The stress battle, fleets engaged; a twentieth of a second of events, as a host would send.
        var stress = Stress.Create();
        Steps.Run(stress, 10);
        stress.Events.Clear();
        Steps.Run(stress, 3 * Steps.Step);
        var big = Write(stress, Klaed);
        var all = Write(stress, Klaed, []);
        stress.Fog = false;
        var open = Write(stress, Klaed, []);
        // A duel between AIs at five minutes.
        var duel = Scenario.CreateSkirmish(new SkirmishOptions { Player = Faction.Klaed, Enemies = [Faction.Nairan], EnemyAI = true, Seed = 1 });
        duel.Factions[Klaed].AI = NewAI();
        Steps.Run(duel, 300);
        duel.Events.Clear();
        Steps.Run(duel, 3 * Steps.Step);
        var small = Write(duel, Klaed);
        output.WriteLine($"Stress: {stress.Entities.Count} entities, {stress.Projectiles.Count} shots; snapshot to Kla'ed {big.Length} bytes "
            + $"({all.Length} without events, {open.Length} without fog)");
        output.WriteLine($"Duel at 5 min: {duel.Entities.Count} entities, {duel.Projectiles.Count} shots; snapshot to Kla'ed {small.Length} bytes");
        Expect.Less(big.Length, 200_000);
        Expect.Less(small.Length, 20_000);
    }
}
