// A battle over time (not in the web version): its series stay bounded however long the battle,
// recording it changes nothing in it, a destroyed ship's killer is found and the best ships kept,
// and the history reads back from the network or is refused.
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim.Tests;

public class BattleHistoryTests
{
    static World NewWorld()
    {
        var w = CreateWorld(6000, 4000);
        AddFaction(w, Faction.Klaed, 1000);
        AddFaction(w, Faction.Nairan, 1000);
        return w;
    }

    static World Skirmish(bool recorded)
    {
        var w = Scenario.CreateSkirmish(new SkirmishOptions
        {
            Player = Faction.Klaed, Enemies = [Faction.Nairan, Faction.Nautolan], EnemyAI = true, Seed = 11, Size = MapSize.Small, Mode = BattleMode.Zones,
        });
        if (!recorded) w.History = null;
        foreach (var f in w.Factions.Values)
        {
            f.AI ??= NewAI();
            f.AutoAbilities = true;
        }
        Steps.Run(w, 90);
        return w;
    }

    [Fact]
    public void A_long_battle_weighs_no_more_than_a_short_one()
    {
        var w = NewWorld();
        var h = new BattleHistory();
        for (var t = 0; t <= 4000; t++)
        {
            w.Time = t;
            h.Step(w);
            Assert.InRange(h.Times.Count, 1, BattleHistory.MaxSamples);
            Assert.All(h.Sides.Values, points => Assert.Equal(h.Times.Count, points.Count));
        }
        Assert.Equal(0, h.Times[0]);
        Assert.True(h.Times.Count > BattleHistory.MaxSamples / 2);
        Assert.True(h.Every > BattleHistory.FirstEvery);
        // The pace kept: samples in order, none further apart than the pace now.
        for (var i = 1; i < h.Times.Count; i++) Assert.InRange(h.Times[i] - h.Times[i - 1], 1, h.Every);
        // The end is always sampled, at its winner.
        w.Time = 4000.5;
        w.Winner = w.Factions.Keys.First();
        h.Step(w);
        Assert.True(h.Closed);
        Assert.Equal(4000.5, h.Times[^1]);
        w.Time = 5000;
        h.Step(w);
        Assert.Equal(4000.5, h.Times[^1]);
    }

    [Fact]
    public void Each_side_has_its_army_earnings_and_spending()
    {
        var w = NewWorld();
        var (klaed, nairan) = (w.Factions.Values.First(), w.Factions.Values.Last());
        SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 500, 500);
        SpawnShip(w, Faction.Klaed, ShipClass.Scout, 600, 500);
        var h = new BattleHistory();
        h.Sample(w);
        klaed.Stats.Earned += 300;
        klaed.Credits = 400;
        w.Time = 10;
        h.Sample(w);
        var p = h.Sides[klaed.Id][^1];
        Assert.Equal((float)(Ships.UnitValue(ShipClass.Frigate) + Ships.UnitValue(ShipClass.Scout)), p.Army);
        Assert.Equal(300, p.Earned);
        // It had 1000, earned 300 and has 400 left.
        Assert.Equal(900, p.Spent);
        Assert.Equal(0, h.Sides[nairan.Id][^1].Army);
        // A side that joins late starts at nothing for the times before it.
        var late = AddFaction(w, Faction.Nautolan, 500);
        w.Time = 20;
        h.Sample(w);
        Assert.Equal(3, h.Sides[late.Id].Count);
        Assert.Equal(new HistoryPoint(), h.Sides[late.Id][0]);
        Assert.Equal(new HistoryPoint(), h.Sides[late.Id][1]);
        Assert.Equal(0, h.Sides[late.Id][2].Spent);
    }

    [Fact]
    public void Recording_a_battle_changes_nothing_in_it()
    {
        var recorded = Skirmish(true);
        var bare = Skirmish(false);
        Assert.Equal(Replays.Digest(bare), Replays.Digest(recorded));
        Assert.True(recorded.History!.Times.Count > 10);
        Assert.Null(bare.History);
        // Three fleets at grips: many killers looked for, the same battle.
        var (fought, alone) = (Stress.Create(), Stress.Create());
        alone.History = null;
        Steps.Run(fought, 10);
        Steps.Run(alone, 10);
        Assert.Equal(Replays.Digest(alone), Replays.Digest(fought));
        Assert.NotEmpty(fought.History!.Aces);
    }

    [Fact]
    public void The_killer_is_the_ship_that_aimed_before_one_that_was_near()
    {
        var w = NewWorld();
        var victim = SpawnShip(w, Faction.Nairan, ShipClass.Scout, 2000, 2000);
        var aiming = SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 2600, 2000);
        aiming.TargetId = victim.Id;
        var near = SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 2100, 2000);
        var far = SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 3200, 2000);
        w.Grid.Rebuild(w.Entities);
        var klaed = w.Factions.Values.First().Id;
        var h = new BattleHistory();
        h.Credit(w, victim, klaed);
        Assert.Equal(1, aiming.Kills);
        Assert.Equal(Ships.UnitValue(ShipClass.Scout), aiming.KilledValue);
        Assert.Equal(0, near.Kills);
        // None aiming: the nearest close by.
        aiming.TargetId = 0;
        h.Credit(w, victim, klaed);
        Assert.Equal(1, near.Kills);
        // None close by: nobody.
        near.Hull = 0;
        aiming.Hull = 0;
        h.Credit(w, victim, klaed);
        Assert.Equal(0, far.Kills);
        // A tie in kills goes to the one that destroyed the most, then to the first built.
        Assert.Equal([aiming.Id, near.Id], h.Aces[klaed].Select(a => a.Id));
        h.Lost(near);
        Assert.Equal([true, false], h.Aces[klaed].Select(a => a.Alive));
    }

    [Fact]
    public void Each_side_keeps_only_its_best_ships()
    {
        var w = NewWorld();
        var klaed = w.Factions.Values.First().Id;
        var h = new BattleHistory();
        var ships = Enumerable.Range(0, BattleHistory.MaxAces + 3).Select(i => SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 500 + i * 200, 500)).ToList();
        for (var i = 0; i < ships.Count; i++)
        {
            var victim = SpawnShip(w, Faction.Nairan, ShipClass.Scout, 500 + i * 200, 600);
            w.Grid.Rebuild(w.Entities);
            // The i-th ship destroys i+1 of them.
            ships[i].TargetId = victim.Id;
            for (var k = 0; k <= i; k++) h.Credit(w, victim, klaed);
        }
        Assert.Equal(ships.AsEnumerable().Reverse().Take(BattleHistory.MaxAces).Select(s => s.Id), h.Aces[klaed].Select(a => a.Id));
        Assert.Equal(ships.Count, h.Aces[klaed][0].Kills);
    }

    [Fact]
    public void A_ship_that_destroys_another_in_battle_is_counted()
    {
        var w = NewWorld();
        var hunter = SpawnShip(w, Faction.Klaed, ShipClass.Battlecruiser, 1000, 1000);
        var victim = SpawnShip(w, Faction.Nairan, ShipClass.Scout, 1300, 1000);
        Steps.Run(w, 30);
        Assert.False(w.Entities.Has(victim.Id));
        var ace = Assert.Single(w.History!.Aces[w.Factions.Values.First().Id]);
        Assert.Equal((hunter.Id, ShipClass.Battlecruiser, 1, true), (ace.Id, ace.Cls, ace.Kills, ace.Alive));
    }

    [Fact]
    public void A_history_reads_back_as_it_was_sent()
    {
        var w = FuzzTests.Battle(5);
        w.History!.Close(w);
        var bytes = BattleHistory.Bytes(w.History);
        var back = BattleHistory.Read(new BinaryReader(new MemoryStream(bytes)));
        Assert.True(back.Closed);
        Assert.Equal(w.History.Times, back.Times);
        Assert.Equal(w.History.Sides.Keys, back.Sides.Keys);
        Assert.Equal(bytes, BattleHistory.Bytes(back));
    }

    static byte[] Written(Action<BinaryWriter> write)
    {
        using var m = new MemoryStream();
        using (var w = new BinaryWriter(m)) write(w);
        return m.ToArray();
    }

    static void Refused(byte[] bytes) =>
        Assert.Throws<InvalidDataException>(() => BattleHistory.Read(new BinaryReader(new MemoryStream(bytes))));

    [Fact]
    public void A_history_that_does_not_hold_is_refused()
    {
        // An ace of a class that does not exist; with a known one it reads.
        byte[] OneAce(byte cls) => Written(w =>
        {
            w.Write(5.0);
            w.Write7BitEncodedInt(0);
            w.Write7BitEncodedInt(0);
            w.Write7BitEncodedInt(1);
            w.Write7BitEncodedInt(1);
            w.Write7BitEncodedInt(1);
            w.Write7BitEncodedInt(42);
            w.Write(cls);
            w.Write(byte.MaxValue);
            w.Write7BitEncodedInt(3);
            w.Write(120.0);
            w.Write(true);
        });
        Assert.Equal(3, BattleHistory.Read(new BinaryReader(new MemoryStream(OneAce((byte)ShipClass.Frigate)))).Aces[new Side(1)][0].Kills);
        Refused(OneAce(250));
        // A pace that is not a number, more samples than are ever kept, a side that does not exist.
        Refused(Written(w => w.Write(double.NaN)));
        Refused(Written(w =>
        {
            w.Write(5.0);
            w.Write7BitEncodedInt(BattleHistory.MaxSamples + 2);
        }));
        Refused(Written(w =>
        {
            w.Write(5.0);
            w.Write7BitEncodedInt(0);
            w.Write7BitEncodedInt(1);
            w.Write7BitEncodedInt(Lobby.MaxSeats + 1);
        }));
    }
}
