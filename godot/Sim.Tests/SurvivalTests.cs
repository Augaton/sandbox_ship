// Survival (not in the web version): every seat on one team against waves of Errants from the
// edges of the map, growing with each wave, a boss every fifth; the last wave beaten wins, no
// station left loses.
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
        for (var n = 2; n <= Sim.Survival.Waves; n++) Expect.Greater(Sim.Survival.Budget(n, 1), Sim.Survival.Budget(n - 1, 1));
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
        // The next one comes an interval later, bigger.
        w.Events.Clear();
        Steps.Run(w, Sim.Survival.Interval);
        Assert.Equal(2, Assert.Single(w.Events.OfType<WaveIncoming>()).Wave);
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

    [Fact]
    public void Beating_the_last_wave_wins()
    {
        var w = Survival();
        // As if the nineteenth were over and the last one on the map.
        w.Mode.Wave = w.Mode.Cleared = Sim.Survival.Waves - 1;
        w.Mode.NextWave = w.Time;
        Steps.StepWorld(w);
        Assert.Equal(Sim.Survival.Waves, w.Mode.Wave);
        Assert.Equal(ShipClass.Colossus, w.Events.OfType<WaveIncoming>().Single().Boss);
        Assert.Equal(double.PositiveInfinity, w.Mode.NextWave);
        Assert.Null(w.Winner);
        foreach (var e in ErrantShips(w)) e.Hull = 0;
        Steps.Run(w, 0.1);
        Assert.Equal(Faction.Klaed, w.Winner);
        Assert.True(Won(w, Faction.Nairan));
        Assert.False(Won(w, Errants));
    }

    [Fact]
    public void The_team_loses_once_no_seat_has_a_station()
    {
        var w = Survival();
        foreach (var side in new Side[] { Faction.Klaed, Faction.Nairan })
        {
            Assert.Null(w.Winner);
            Assert.False(Modes.Out(w, Faction.Klaed));
            var station = StationOf(w, side)!;
            foreach (var e in ChildrenOf(w, station.Id).Prepend(station)) e.Hull = 0;
            Steps.StepWorld(w);
        }
        Assert.Equal(Errants, w.Winner);
        Assert.False(Won(w, Faction.Klaed));
        Assert.True(Modes.Out(w, Faction.Nairan));
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
