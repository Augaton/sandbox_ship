// Survival (not in the web version): every seat on one team against waves of Errants from the
// edges of the map, without end: growing with each wave, a boss every fifth then several, elite
// Errants past the twentieth, a theme now and then. Each wave beaten pays and scores; no station
// left ends it, on each seat's score.
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim.Tests;

public class SurvivalTests
{
    static readonly Side Errants = Faction.Errant;

    static World Survival(int seed = 3, bool ai = false) => Scenario.CreateSkirmish(new SkirmishOptions
    {
        Mode = BattleMode.Survival, Seed = seed, Raid = true,
        Slots = [new(Faction.Klaed, Faction.Klaed, 0, ai ? Difficulty.Normal : null), new(Faction.Nairan, Faction.Nairan, 0, ai ? Difficulty.Normal : null)],
    });

    static List<Entity> ErrantShips(World w) => w.Entities.Where(e => e.Kind == EntityKind.Ship && e.Side == Errants).ToList();

    [Fact]
    public void Every_seat_plays_on_one_team_against_the_Errants()
    {
        var w = Survival();
        Assert.True(Allies(w, Faction.Klaed, Faction.Nairan));
        Assert.True(Foes(w, Faction.Klaed, Errants));
        Assert.True(Foes(w, Faction.Nairan, Errants));
        // No raid among the waves, nor a ship before the first one.
        Assert.Empty(ErrantShips(w));
        Assert.Equal(Sim.Survival.Grace, w.Mode.NextWave);
        Assert.Equal(0, w.Mode.Wave);
    }

    [Fact]
    public void Waves_grow_in_size_and_class_with_a_boss_every_fifth()
    {
        for (var n = 2; n <= 60; n++) Expect.Greater(Sim.Survival.Budget(n, 1), Sim.Survival.Budget(n - 1, 1));
        Expect.Greater(Sim.Survival.Budget(5, 3), Sim.Survival.Budget(5, 1));
        Assert.Equal([ShipClass.Dreadnought, ShipClass.Titan, ShipClass.Leviathan, ShipClass.Colossus],
            new[] { 5, 10, 15, 20 }.Select(n => Sim.Survival.Boss(n)!.Value));
        Assert.Null(Sim.Survival.Boss(4));
        var w = Survival();
        double Value(ShipCounts s) => Ships.All.Sum(c => s.Of(c) * Ships.Defs[c].Cost);
        var first = Sim.Survival.Compose(w, 1);
        var later = Sim.Survival.Compose(w, 12);
        Expect.Greater(Value(later), 4 * Value(first));
        Assert.All(Ships.All.Where(c => first.Of(c) > 0), c => Assert.Equal(SizeClass.Small, Ships.Defs[c].Size));
        Assert.Contains(Ships.All, c => later.Of(c) > 0 && Ships.Defs[c].Size == SizeClass.Large);
        Assert.Equal(1, Sim.Survival.Compose(w, 10).Of(ShipClass.Titan));
    }

    [Fact]
    public void The_lobby_s_waves_strength_scales_every_wave()
    {
        World At(int strength) => Scenario.CreateSkirmish(new SkirmishOptions
        {
            Mode = BattleMode.Survival, Seed = 3, WaveStrength = strength,
            Slots = [new(Faction.Klaed, Faction.Klaed), new(Faction.Nairan, Faction.Nairan)],
        });
        double Value(ShipCounts s) => Ships.All.Sum(c => s.Of(c) * Ships.Defs[c].Cost);
        var (soft, usual, fierce) = (At(70), At(100), At(140));
        Assert.Equal(0.7, Sim.Survival.Strength(soft), 6);
        foreach (var wave in new[] { 3, 8 })
        {
            Expect.Greater(Value(Sim.Survival.Compose(usual, wave)), Value(Sim.Survival.Compose(soft, wave)));
            Expect.Greater(Value(Sim.Survival.Compose(fierce, wave)), Value(Sim.Survival.Compose(usual, wave)));
        }
        // A world not made from options (a player's copy over the network) has the usual waves.
        usual.Origin = null;
        Assert.Equal(1, Sim.Survival.Strength(usual));
        Assert.False(Scenario.Check(new SkirmishOptions { Mode = BattleMode.Survival, WaveStrength = 0, Slots = [new(Faction.Klaed, Faction.Klaed)] }).Ok);
    }

    [Fact]
    public void A_wave_comes_from_an_edge_after_the_grace_period_and_goes_for_the_team()
    {
        var w = Survival();
        Steps.Run(w, Sim.Survival.Grace - 1);
        Assert.Empty(ErrantShips(w));
        Steps.Run(w, 1.5);
        var wave = Assert.Single(w.Events.OfType<WaveIncoming>());
        Assert.Equal((1, null), (wave.Wave, wave.Boss));
        Assert.Equal(1, w.Mode.Wave);
        Assert.Equal(Sim.Survival.Grace + Sim.Survival.Interval, w.Mode.NextWave, 1);
        // Just inside an edge, well away from the stations.
        Assert.Equal(260, Math.Min(Math.Min(wave.X, w.Width - wave.X), Math.Min(wave.Y, w.Height - wave.Y)), 3);
        foreach (var s in w.Entities.Where(e => e.Type == "station")) Expect.Greater(Dist(s, new Point(wave.X, wave.Y)), 1500);
        // Every Errant is on its way to a structure of the team.
        var ours = w.Entities.Where(e => e.Kind == EntityKind.Structure && e.Side != Errants).ToList();
        var ships = ErrantShips(w);
        Assert.NotEmpty(ships);
        Assert.All(ships, e =>
        {
            Assert.Equal(OrderType.AttackMove, e.Order?.Type);
            Expect.Less(ours.Min(s => Hypot(s.X - e.Order!.X, s.Y - e.Order.Y)), 400);
        });
        // The next one comes at the latest an interval later (sooner if the station's guns beat this one first).
        w.Events.Clear();
        Steps.Run(w, Sim.Survival.Interval);
        Assert.Equal(2, w.Events.OfType<WaveIncoming>().First().Wave);
    }

    [Fact]
    public void A_wave_is_beaten_once_every_Errant_is_down()
    {
        var w = Survival();
        Steps.Run(w, Sim.Survival.Grace + 1);
        Assert.Equal(0, w.Mode.Cleared);
        foreach (var e in ErrantShips(w)) e.Hull = 0;
        Steps.Run(w, 0.1);
        Assert.Equal(1, w.Mode.Cleared);
        Assert.Contains(w.Events, e => e is WaveCleared { Wave: 1 });
        Assert.Null(w.Winner);
    }

    /// <summary>As if wave `wave - 1` were over: the next one comes at once.</summary>
    static void Next(World w, int wave)
    {
        w.Mode.Wave = w.Mode.Cleared = wave - 1;
        w.Mode.NextWave = w.Time;
        w.Events.Clear();
        Steps.StepWorld(w);
    }

    [Fact]
    public void Beating_the_twentieth_wave_wins_nothing_the_waves_go_on()
    {
        var w = Survival();
        Next(w, 20);
        Assert.Equal(ShipClass.Colossus, w.Events.OfType<WaveIncoming>().Single().Boss);
        foreach (var e in ErrantShips(w)) e.Hull = 0;
        Steps.Run(w, 0.1);
        Assert.Equal(20, w.Mode.Cleared);
        Assert.Null(w.Winner);
        Expect.Less(w.Mode.NextWave, double.PositiveInfinity);
        // A thirtieth wave comes, heavier still, with three bosses and elite Errants.
        Next(w, 30);
        Assert.Equal(30, w.Mode.Wave);
        Assert.Equal([ShipClass.Colossus, ShipClass.Leviathan, ShipClass.Titan], Sim.Survival.Bosses(30));
        Assert.Equal(3, w.Mode.BossIds.Count);
        Assert.Equal(Sim.Survival.Elite(30), w.Factions[Errants].Research[TechId.Armor]);
        Assert.True(ErrantShips(w).Count > 10);
        Assert.Null(w.Winner);
    }

    [Fact]
    public void Bosses_multiply_once_the_colossus_has_come_and_themes_take_turns()
    {
        Assert.Empty(Sim.Survival.Bosses(24));
        Assert.Equal([ShipClass.Colossus, ShipClass.Leviathan], Sim.Survival.Bosses(25));
        Assert.Equal(4, Sim.Survival.Bosses(35).Count);
        Assert.Equal(4, Sim.Survival.Bosses(80).Count);
        // A theme one wave in four from the seventh, boss waves aside, in turn.
        Assert.Equal(
            [WaveTheme.Swarm, WaveTheme.Armada, WaveTheme.None, WaveTheme.Carriers, WaveTheme.Swarm, WaveTheme.Armada],
            new[] { 7, 11, 15, 19, 23, 27 }.Select(Sim.Survival.Theme));
        Assert.Equal(WaveTheme.None, Sim.Survival.Theme(6));
        Assert.Equal(WaveTheme.None, Sim.Survival.Theme(8));
        var w = Survival();
        var swarm = Sim.Survival.Compose(w, 7);
        Assert.All(Ships.All.Where(c => swarm.Of(c) > 0), c => Assert.Equal(SizeClass.Small, Ships.Defs[c].Size));
        var armada = Sim.Survival.Compose(w, 11);
        Assert.Contains(armada, k => k.Value > 0);
        Assert.All(Ships.All.Where(c => armada.Of(c) > 0), c => Assert.Equal(SizeClass.Large, Ships.Defs[c].Size));
        var carriers = Sim.Survival.Compose(w, 19);
        Expect.Greater(carriers.Of(ShipClass.Carrier), 0);
        Assert.All(Ships.All.Where(c => carriers.Of(c) > 0), c => Assert.Contains(c, new[] { ShipClass.Carrier, ShipClass.Bomber }));
    }

    [Fact]
    public void The_pace_quickens_to_a_floor_and_elite_Errants_come_past_the_twentieth()
    {
        Assert.Equal(Sim.Survival.Interval, Sim.Survival.Spacing(1));
        for (var n = 2; n <= 60; n++) Assert.True(Sim.Survival.Spacing(n) <= Sim.Survival.Spacing(n - 1));
        Assert.Equal(Sim.Survival.MinInterval, Sim.Survival.Spacing(60));
        Assert.Equal(0, Sim.Survival.Elite(20));
        Assert.Equal([1, 1, 2, 2, 3], new[] { 21, 22, 23, 24, 25 }.Select(Sim.Survival.Elite));
        // A wave past the twentieth is tougher than the same ships before it.
        var w = Survival();
        Next(w, 2);
        var plain = ErrantShips(w).First(e => e.Cls == ShipClass.Bomber);
        var w2 = Survival();
        Next(w2, 26);
        var elite = ErrantShips(w2).First(e => e.Cls == ShipClass.Frigate);
        Expect.Greater(elite.MaxHull, Ships.Defs[ShipClass.Frigate].Hull * HullMult(w2, Errants) / (1 + 0.1 * 3) * 1.25);
        Expect.Greater(HullMult(w2, Errants), HullMult(w, Errants));
        Assert.NotNull(plain);
    }

    [Fact]
    public void A_wave_beaten_early_pays_the_seats_standing_scores_and_brings_the_next_one_sooner()
    {
        var w = Survival();
        Steps.Run(w, Sim.Survival.Grace + 1);
        var credits = w.Factions[Faction.Klaed].Credits;
        foreach (var e in ErrantShips(w)) e.Hull = 0;
        Steps.Run(w, 0.1);
        var cleared = Assert.Single(w.Events.OfType<WaveCleared>());
        Assert.Equal(1, cleared.Wave);
        // Due 89 s later, it comes after a breather: the 64 s saved count.
        Assert.Equal(w.Time + Sim.Survival.Breather, w.Mode.NextWave, 0.2);
        Assert.InRange(cleared.Ahead, 62, 66);
        // The reward, and the station's income meanwhile.
        Assert.Equal(credits + Sim.Survival.Reward(1), w.Factions[Faction.Klaed].Credits, 2.0);
        var score = Sim.Survival.TallyOf(w, Faction.Klaed).Score;
        Assert.InRange(score, Sim.Survival.WavePoints + Sim.Survival.AheadPoints * cleared.Ahead - 2, Sim.Survival.WavePoints + Sim.Survival.AheadPoints * cleared.Ahead + 2);
        Assert.Equal(Sim.Survival.TallyOf(w, Faction.Nairan).Score, score);
        Assert.Equal(2 * score, Sim.Survival.TeamScore(w));
    }

    [Fact]
    public void An_Errant_downed_scores_a_tenth_of_its_value_for_whoever_downed_it_and_a_boss_more()
    {
        var w = Survival();
        Next(w, 5);
        var boss = w.Entities.Get(Assert.Single(w.Mode.BossIds))!;
        Assert.Equal(ShipClass.Dreadnought, boss.Cls);
        var frigate = SpawnShip(w, Errants, ShipClass.Frigate, 300, 300);
        frigate.LastHitBy = Faction.Nairan;
        frigate.Hull = 0;
        boss.LastHitBy = Faction.Klaed;
        boss.Hull = 0;
        Steps.StepWorld(w);
        var nairan = Sim.Survival.TallyOf(w, Faction.Nairan);
        var klaed = Sim.Survival.TallyOf(w, Faction.Klaed);
        Assert.Equal((1, 0, 65), (nairan.Downed, nairan.Bosses, nairan.Score));
        Assert.Equal((1, 1), (klaed.Downed, klaed.Bosses));
        Assert.Equal(Sim.Survival.BossPoints + (int)Math.Round(Ships.UnitValue(ShipClass.Dreadnought) / 10), klaed.Score);
        Assert.Empty(w.Mode.BossIds);
    }

    [Fact]
    public void It_ends_once_no_seat_has_a_station_and_not_before()
    {
        var w = Survival();
        Steps.Run(w, 30);
        foreach (var side in new Side[] { Faction.Klaed, Faction.Nairan })
        {
            Assert.Null(w.Winner);
            Assert.Equal(0, w.Mode.Held);
            Assert.False(Modes.Out(w, Faction.Klaed));
            var station = StationOf(w, side)!;
            foreach (var e in ChildrenOf(w, station.Id).Prepend(station)) e.Hull = 0;
            Steps.StepWorld(w);
        }
        Assert.Equal(Errants, w.Winner);
        Assert.Equal(w.Time, w.Mode.Held, 0.1);
        Assert.False(Won(w, Faction.Klaed));
        Assert.True(Modes.Out(w, Faction.Nairan));
    }

    [Fact]
    public void A_survival_game_s_score_crosses_the_network()
    {
        var w = Survival();
        Next(w, 5);
        Sim.Survival.TallyOf(w, Faction.Klaed).Score = 1234;
        Sim.Survival.TallyOf(w, Faction.Nairan).Bosses = 2;
        w.Mode.Held = 321.5;
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms)) Snapshot.Write(w, Faction.Klaed, bw, null);
        var replica = new World();
        Snapshot.Read(new BinaryReader(new MemoryStream(ms.ToArray())), replica);
        // The bosses are the host's business.
        Assert.Empty(replica.Mode.BossIds);
        Assert.Equal(5, replica.Mode.Wave);
        Assert.Equal(321.5, replica.Mode.Held);
        Assert.Equal(1234, replica.Mode.Tallies[Faction.Klaed].Score);
        Assert.Equal(2, replica.Mode.Tallies[Faction.Nairan].Bosses);
    }

    [Fact]
    public void The_best_games_are_kept_ten_for_each_number_of_seats_and_a_damaged_file_reads_as_none()
    {
        SurvivalRecord Game(int score, int seats = 1, int at = 0) => new(score, score / 100, score, seats, at, "Kla'ed");
        var list = new List<SurvivalRecord>();
        for (var i = 1; i <= 12; i++) SurvivalRecords.Insert(list, Game(i * 100, 1, i));
        Assert.Equal(SurvivalRecords.Kept, SurvivalRecords.Best(list, 1).Count);
        Assert.Equal(1200, SurvivalRecords.Best(list, 1)[0].Score);
        Assert.Equal(-1, SurvivalRecords.Insert(list, Game(50)));
        Assert.Equal(-1, SurvivalRecords.Insert([], Game(0)));
        Assert.Equal(0, SurvivalRecords.Insert(list, Game(5000)));
        // At the same score, the older game keeps its place.
        Assert.Equal(4, SurvivalRecords.Insert(list, Game(1000, 1, 99)));
        // Two seats keep their own ten.
        Assert.Equal(0, SurvivalRecords.Insert(list, Game(10, 2)));
        Assert.Single(SurvivalRecords.Best(list, 2));
        var back = SurvivalRecords.Parse(SurvivalRecords.Serialize(list));
        Assert.Equal(list, back);
        Assert.Empty(SurvivalRecords.Parse("{ ce n'est pas du JSON"));
        Assert.Empty(SurvivalRecords.Parse("{\"Score\": 3}"));
        Assert.Empty(SurvivalRecords.Parse(null));
        Assert.Empty(SurvivalRecords.Parse(new string('[', 300_000)));
        // What cannot be a record is left out; the rest is kept, a long players' line cut short.
        var forged = "[null, {\"Score\": -5, \"Wave\": 1, \"Held\": 1, \"Seats\": 1, \"At\": 0, \"Sides\": \"x\"}, "
            + "{\"Score\": 9, \"Wave\": 1, \"Held\": 1, \"Seats\": 99, \"At\": 0, \"Sides\": \"x\"}, "
            + "{\"Score\": 7, \"Wave\": 2, \"Held\": 3, \"Seats\": 2, \"At\": 4, \"Sides\": \"" + new string('a', 500) + "\"}]";
        var kept = Assert.Single(SurvivalRecords.Parse(forged));
        Assert.Equal((7, 2), (kept.Score, kept.Seats));
        Assert.Equal(120, kept.Sides.Length);
    }

    [Fact]
    public void Between_AIs_it_ends()
    {
        var w = Scenario.CreateSkirmish(new SkirmishOptions
        {
            Mode = BattleMode.Survival, Seed = 7, Size = MapSize.Small, Slots = [new(Faction.Nautolan, Faction.Nautolan, 0, Difficulty.Easy)],
        });
        while (w.Winner is null && w.Time < 45 * 60)
        {
            Steps.StepWorld(w, 1.0 / 30);
            w.Events.Clear();
        }
        Assert.NotNull(w.Winner);
        Expect.GreaterOrEqual(w.Mode.Wave, 3);
    }
}
