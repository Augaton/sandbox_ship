// Regicide (not in the web version): every side starts with its race's admiral as a flagship;
// losing it takes the side out, all it owns blowing up with it, and the last camp with a
// flagship wins. Stations decide nothing.
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim.Tests;

public class RegicideTests
{
    static readonly Side Fourth = new(4);

    static World Regicide(List<Slot>? slots = null) => Scenario.CreateSkirmish(new SkirmishOptions
    {
        Mode = BattleMode.Regicide, Seed = 9, Size = MapSize.Large, Raid = true,
        Slots = slots ?? [new(Faction.Klaed, Faction.Klaed), new(Faction.Nairan, Faction.Nairan), new(Faction.Nautolan, Faction.Nautolan)],
    });

    static Entity Flagship(World w, Side side) => w.Entities.Get(w.Mode.Flagships[side])!;

    static void Sink(World w, Side side) => Flagship(w, side).Hull = 0;

    [Fact]
    public void Every_side_starts_with_its_admiral_as_flagship()
    {
        var w = Regicide(slots: [new(Faction.Klaed, Faction.Klaed), new(Fourth, Faction.Nairan)]);
        Assert.Equal([Faction.Klaed, Fourth], w.Mode.Flagships.Keys);
        foreach (var (side, race) in new[] { ((Side)Faction.Klaed, Faction.Klaed), (Fourth, Faction.Nairan) })
        {
            var flag = Flagship(w, side);
            Assert.Equal((side, new HeroId(race, HeroRole.Admiral)), (flag.Side, flag.Hero!.Value));
            Assert.True(Modes.Kept(w, flag));
            Expect.Less(Dist(flag, StationOf(w, side)!), 500);
            // Its hero cannot be built a second time while it flies.
            Assert.False(Structures.CanBuildHero(w, side, flag.Hero.Value).Ok);
        }
        // The raid's guardians have none.
        Assert.Contains(w.Entities, e => e.Type == "colossus");
    }

    [Fact]
    public void Losing_the_flagship_takes_the_side_out_and_pays_no_one()
    {
        var w = Regicide();
        var kills = w.Factions.Values.Select(f => f.Stats.Kills).ToList();
        Sink(w, Faction.Klaed);
        Steps.StepWorld(w);
        var lost = Assert.Single(w.Events.OfType<FlagshipLost>());
        Assert.Equal(Faction.Klaed, lost.Side);
        Assert.DoesNotContain((Side)Faction.Klaed, w.Mode.Flagships.Keys);
        Assert.True(Modes.Out(w, Faction.Klaed));
        Steps.StepWorld(w);
        Assert.DoesNotContain(w.Entities, e => e.Side == Faction.Klaed);
        Assert.Null(StationOf(w, Faction.Klaed));
        Assert.Equal(kills, w.Factions.Values.Select(f => f.Stats.Kills));
        // Two flagships left: the battle goes on.
        Assert.Null(w.Winner);
    }

    [Fact]
    public void The_last_flagship_wins_and_stations_decide_nothing()
    {
        var w = Regicide();
        var station = StationOf(w, Faction.Nairan)!;
        foreach (var e in ChildrenOf(w, station.Id).Prepend(station)) e.Hull = 0;
        Steps.Run(w, 1);
        Assert.Null(w.Winner);
        Assert.False(Modes.Out(w, Faction.Nairan));
        Sink(w, Faction.Nautolan);
        Steps.Run(w, 1);
        Assert.Null(w.Winner);
        Sink(w, Faction.Klaed);
        Steps.StepWorld(w);
        // The Nairans win without a station: their flagship is the last.
        Assert.Equal(Faction.Nairan, w.Winner);
        Assert.Contains(w.Events, e => e is Victory v && v.Side == Faction.Nairan);
    }

    [Fact]
    public void A_team_holds_while_one_of_its_flagships_flies()
    {
        var w = Regicide(slots:
        [
            new(Faction.Klaed, Faction.Klaed, 1), new(Faction.Nairan, Faction.Nairan, 2),
            new(Faction.Nautolan, Faction.Nautolan, 1), new(Fourth, Faction.Klaed, 2),
        ]);
        Sink(w, Faction.Klaed);
        Sink(w, Faction.Nairan);
        Steps.Run(w, 1);
        Assert.Null(w.Winner);
        Assert.False(Modes.Out(w, Faction.Klaed));
        Sink(w, Fourth);
        Steps.StepWorld(w);
        Assert.Equal(Faction.Nautolan, w.Winner);
        Assert.True(Won(w, Faction.Klaed));
        Assert.False(Won(w, Faction.Nairan));
    }

    [Fact]
    public void The_last_two_falling_together_leave_a_winner()
    {
        var w = Regicide(slots: [new(Faction.Klaed, Faction.Klaed), new(Faction.Nairan, Faction.Nairan)]);
        Sink(w, Faction.Klaed);
        Sink(w, Faction.Nairan);
        Steps.StepWorld(w);
        Assert.Equal(Faction.Klaed, w.Winner);
        Assert.Equal(2, w.Events.OfType<FlagshipLost>().Count());
    }

    [Fact]
    public void The_AI_keeps_its_flagship_back_with_an_escort()
    {
        var w = Regicide(slots: [new(Faction.Klaed, Faction.Klaed, 0, Difficulty.Normal), new(Faction.Nairan, Faction.Nairan, 0, Difficulty.Normal)]);
        Steps.Run(w, 6 * 60, 1.0 / 30);
        foreach (var side in new Side[] { Faction.Klaed, Faction.Nairan })
        {
            var flag = Flagship(w, side);
            var home = StationOf(w, side)!;
            Expect.Less(Dist(flag, home), 1300);
            var foe = StationOf(w, side == Faction.Klaed ? Faction.Nairan : Faction.Klaed)!;
            Expect.Greater(Dist(flag, foe), Dist(home, foe));
            var escort = w.Mode.Escorts[side];
            Expect.GreaterOrEqual(escort.Count, 2);
            Assert.All(escort, id => Assert.True(Modes.Kept(w, w.Entities.Get(id)!)));
        }
    }

    [Fact]
    public void Between_AIs_it_ends()
    {
        var w = Scenario.CreateSkirmish(new SkirmishOptions
        {
            Mode = BattleMode.Regicide, Seed = 101,
            Slots = [new(Faction.Nairan, Faction.Nairan, 0, Difficulty.Normal), new(Faction.Klaed, Faction.Klaed, 0, Difficulty.Normal)],
        });
        while (w.Winner is null && w.Time < 45 * 60)
        {
            Steps.StepWorld(w, 1.0 / 30);
            w.Events.Clear();
        }
        Assert.NotNull(w.Winner);
        Assert.Single(w.Mode.Flagships);
    }

    [Fact]
    public void Sudden_death_strips_the_flagships_shields_and_stops_every_repair()
    {
        var w = Regicide();
        var id = w.Mode.Flagships[Faction.Klaed];
        var flag = w.Entities.Get(id)!;
        // A support ship beside it keeps mending it, until sudden death.
        SpawnShip(w, Faction.Klaed, ShipClass.Support, flag.X + 60, flag.Y);
        w.Time = SandboxShip.Sim.Regicide.SuddenDeath - SandboxShip.Sim.Regicide.Warning - 1;
        var warnings = new List<double>();
        for (var t = 0.0; t < SandboxShip.Sim.Regicide.Warning + 5; t += Steps.Step)
        {
            if (w.Time < SandboxShip.Sim.Regicide.SuddenDeath) flag.Hull = flag.MaxHull * 0.5;
            Steps.StepWorld(w);
            warnings.AddRange(w.Events.OfType<ClockWarning>().Select(c => c.Left));
            w.Events.Clear();
        }
        Assert.Equal([SandboxShip.Sim.Regicide.Warning, 0.0], warnings);
        Assert.Equal(0, flag.Shield);
        var held = flag.Hull;
        Steps.Run(w, 5);
        Assert.Equal(0, flag.Shield);
        Assert.True(flag.Hull <= held);
    }
}
