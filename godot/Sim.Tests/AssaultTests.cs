// Assault (not in the web version): team 1 holds a fortress against team 2's fleet and income;
// the defenders win if a station of theirs stands when the clock runs out, the attackers by
// destroying them all. Also the lobby's check of every mode's setup.
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim.Tests;

public class AssaultTests
{
    static readonly Side Fourth = new(4);

    static World Assault(int minutes = 15, Difficulty? ai = null) => Scenario.CreateSkirmish(new SkirmishOptions
    {
        Mode = BattleMode.Assault, AssaultMinutes = minutes, Seed = 5, Raid = true,
        Slots = [new(Faction.Klaed, Faction.Klaed, Sim.Assault.Defenders, ai), new(Faction.Nairan, Faction.Nairan, Sim.Assault.Attackers, ai)],
    });

    static int Count(World w, Side side, string type) => w.Entities.Count(e => e.Side == side && e.Type == type);

    static double FleetValue(World w, Side side) =>
        w.Entities.Where(e => e.Side == side && e.Kind == EntityKind.Ship && !e.Launched).Sum(e => Ships.UnitValue(e.Cls));

    [Fact]
    public void The_defenders_hold_a_fortress_and_the_attackers_come_with_a_fleet()
    {
        var w = Assault();
        var fortress = StationOf(w, Faction.Klaed)!;
        var beachhead = StationOf(w, Faction.Nairan)!;
        Assert.Equal((4, 3), (fortress.Level, beachhead.Level));
        Expect.Less(fortress.X, w.Width / 2);
        Expect.Greater(beachhead.X, w.Width / 2);
        var platforms = w.Entities.Where(e => e.Side == Faction.Klaed && e.Type == "platform").ToList();
        Assert.Equal(3, platforms.Count);
        Assert.All(platforms, p => Assert.Equal(3, p.Level));
        Assert.Equal(1, Count(w, Faction.Klaed, "ionCannon"));
        Assert.Equal(0, Count(w, Faction.Nairan, "platform"));
        Expect.Greater(FleetValue(w, Faction.Nairan), FleetValue(w, Faction.Klaed));
        Expect.Greater(FleetValue(w, Faction.Klaed), 5000);
        Assert.Equal(15 * 60, w.Mode.Deadline);
        // No raid in the attackers' way.
        Assert.DoesNotContain(w.Entities, e => e.Type == "colossus");
    }

    [Fact]
    public void The_attackers_earn_more()
    {
        var w = Assault();
        var before = (Klaed: w.Factions[Faction.Klaed].Credits, Nairan: w.Factions[Faction.Nairan].Credits);
        Steps.Run(w, 10);
        var defenders = w.Factions[Faction.Klaed].Credits - before.Klaed;
        var attackers = w.Factions[Faction.Nairan].Credits - before.Nairan;
        Expect.Close(Station.Levels[3].Income * 10, defenders, 0);
        Expect.Close((Station.Levels[2].Income + Sim.Assault.AttackerIncome) * 10, attackers, 0);
    }

    [Fact]
    public void The_defenders_win_when_the_clock_runs_out()
    {
        var w = Assault(minutes: 2);
        Steps.Run(w, 59);
        Assert.Empty(w.Events.OfType<ClockWarning>());
        Steps.Run(w, 2);
        Assert.Equal(60, Assert.Single(w.Events.OfType<ClockWarning>()).Left);
        Steps.Run(w, 58.5);
        Assert.Null(w.Winner);
        Steps.Run(w, 1);
        Assert.Equal(Faction.Klaed, w.Winner);
        Assert.Contains(w.Events, e => e is Victory v && v.Side == Faction.Klaed && v.Team == Sim.Assault.Defenders);
    }

    [Fact]
    public void The_attackers_win_by_destroying_the_fortress()
    {
        var w = Assault();
        Steps.Run(w, 30);
        var fortress = StationOf(w, Faction.Klaed)!;
        foreach (var e in ChildrenOf(w, fortress.Id).Prepend(fortress)) e.Hull = 0;
        Steps.StepWorld(w);
        Assert.Equal(Faction.Nairan, w.Winner);
        Assert.False(Won(w, Faction.Klaed));
    }

    [Fact]
    public void Every_defender_station_must_fall_and_the_defenders_hold_with_any()
    {
        var w = Scenario.CreateSkirmish(new SkirmishOptions
        {
            Mode = BattleMode.Assault, AssaultMinutes = 1, Seed = 5,
            Slots = [new(Faction.Klaed, Faction.Klaed, 1), new(Faction.Nairan, Faction.Nairan, 2), new(Faction.Nautolan, Faction.Nautolan, 1), new(Fourth, Faction.Klaed, 2)],
        });
        Assert.Equal(4, StationOf(w, Faction.Nautolan)!.Level);
        Assert.Equal(3, StationOf(w, Fourth)!.Level);
        var first = StationOf(w, Faction.Klaed)!;
        foreach (var e in ChildrenOf(w, first.Id).Prepend(first)) e.Hull = 0;
        Steps.Run(w, 61);
        Assert.Equal(Faction.Nautolan, w.Winner);
        Assert.True(Won(w, Faction.Klaed));
    }

    [Fact]
    public void The_defending_AI_sends_no_waves()
    {
        var w = Assault(ai: Difficulty.Normal);
        var defenders = w.Factions[Faction.Klaed];
        Assert.Null(Modes.WaveTarget(w, defenders, StationOf(w, Faction.Nairan)));
        Assert.Same(StationOf(w, Faction.Klaed), Modes.WaveTarget(w, w.Factions[Faction.Nairan], StationOf(w, Faction.Klaed)));
    }

    [Fact]
    public void Between_AIs_it_ends_by_the_clock_at_the_latest()
    {
        var w = Assault(minutes: 10, ai: Difficulty.Normal);
        while (w.Winner is null && w.Time < 11 * 60)
        {
            Steps.StepWorld(w, 1.0 / 30);
            w.Events.Clear();
        }
        Assert.NotNull(w.Winner);
        Expect.Less(w.Time, 10 * 60 + 1);
        // The attackers went for the fortress.
        Expect.Greater(w.Factions[Faction.Nairan].Stats.Kills + w.Factions[Faction.Klaed].Stats.Kills, 5);
    }

    public static TheoryData<string, SkirmishOptions, string?> Setups => new()
    {
        { "standard à deux", new() { Player = Faction.Klaed, Enemies = [Faction.Nairan] }, null },
        { "un seul joueur", new() { Slots = [new(Faction.Klaed, Faction.Klaed)] }, "De 2 à 6 joueurs" },
        { "même camp", new() { Slots = [new(Faction.Klaed, Faction.Klaed), new(Faction.Klaed, Faction.Nairan)] }, "Deux places jouent le même camp" },
        { "Errants", new() { Slots = [new(Faction.Klaed, Faction.Klaed), new(Fourth, Faction.Errant)] }, "Les Errants ne sont pas jouables" },
        { "raid sur le camp 3", new() { Raid = true, Slots = [new(Faction.Klaed, Faction.Klaed), new(Faction.Errant, Faction.Nairan)] }, "Le camp 3 revient aux Errants" },
        { "une seule équipe", new() { Slots = [new(Faction.Klaed, Faction.Klaed, 1), new(Faction.Nairan, Faction.Nairan, 1)] }, "Il faut au moins deux camps adverses" },
        { "survie seul", new() { Mode = BattleMode.Survival, Slots = [new(Faction.Klaed, Faction.Klaed)] }, null },
        { "survie en équipe", new() { Mode = BattleMode.Survival, Slots = [new(Faction.Klaed, Faction.Klaed, 1), new(Faction.Nairan, Faction.Nairan, 2)] }, null },
        { "survie sur le camp 3", new() { Mode = BattleMode.Survival, Slots = [new(Faction.Errant, Faction.Klaed)] }, "Le camp 3 revient aux Errants" },
        { "zones", new() { Mode = BattleMode.Zones, Raid = true, Slots = [new(Faction.Klaed, Faction.Klaed), new(Faction.Errant, Faction.Nairan)] }, null },
        { "zones sans score", new() { Mode = BattleMode.Zones, ZoneTarget = 0, Player = Faction.Klaed, Enemies = [Faction.Nairan] }, "Le score à atteindre doit être d'au moins 1" },
        { "assaut", new() { Mode = BattleMode.Assault, Slots = [new(Faction.Klaed, Faction.Klaed, 1), new(Faction.Nairan, Faction.Nairan, 2), new(Fourth, Faction.Klaed, 2)] }, null },
        { "assaut sans équipes", new() { Mode = BattleMode.Assault, Player = Faction.Klaed, Enemies = [Faction.Nairan] }, "En assaut, chaque joueur défend (équipe 1) ou attaque (équipe 2)" },
        { "assaut sans attaquant", new() { Mode = BattleMode.Assault, Slots = [new(Faction.Klaed, Faction.Klaed, 1), new(Faction.Nairan, Faction.Nairan, 1)] }, "L'assaut demande au moins un attaquant (équipe 2)" },
        { "assaut sans défenseur", new() { Mode = BattleMode.Assault, Slots = [new(Faction.Klaed, Faction.Klaed, 2), new(Faction.Nairan, Faction.Nairan, 2)] }, "L'assaut demande au moins un défenseur (équipe 1)" },
        { "assaut éclair", new() { Mode = BattleMode.Assault, AssaultMinutes = 0, Slots = [new(Faction.Klaed, Faction.Klaed, 1), new(Faction.Nairan, Faction.Nairan, 2)] }, "L'assaut doit durer au moins 1 min" },
        { "régicide à trois", new() { Mode = BattleMode.Regicide, Slots = [new(Faction.Klaed, Faction.Klaed), new(Faction.Nairan, Faction.Nairan), new(Faction.Nautolan, Faction.Nautolan)] }, null },
        { "régicide sept", new() { Mode = BattleMode.Regicide, Slots = [.. Enumerable.Range(4, 7).Select(i => new Slot(new Side(i), Faction.Klaed))] }, "De 2 à 6 joueurs" },
    };

    [Theory]
    [MemberData(nameof(Setups))]
    public void Check_tells_the_lobby_what_is_wrong_with_a_setup(string name, SkirmishOptions o, string? reason)
    {
        var r = Scenario.Check(o);
        Assert.True(r.Ok == (reason is null), name);
        Assert.Equal(reason ?? "", r.Reason);
        // A mode's world is only made from a setup that passes.
        if (o.Mode == BattleMode.Standard) return;
        if (reason is null) Assert.Equal(o.Mode, Scenario.CreateSkirmish(o).Mode.Kind);
        else Assert.Throws<ArgumentException>(() => Scenario.CreateSkirmish(o));
    }
}
