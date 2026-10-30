// Interceptions, a rule of this version only: enemy fleets that meet head-on along a lane stop
// and fight there, instead of slipping past each other to strike behind the lines.
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.Galaxies;

namespace SandboxShip.Sim.Tests;

public class InterceptTests
{
    static readonly int West = GalaxyMap.Capitals[0];

    /// <summary>
    /// A Kla'ed fleet leaves its capital for a free neighbour while a Nairan fleet leaves that
    /// neighbour for the capital. `player`: who plays (Nautolan, off the map, only watches).
    /// </summary>
    static (Galaxy G, Fleet Klaed, Fleet Nairan, int Next) Crossing(Faction player, ShipCounts nairan)
    {
        var g = CreateGalaxy(Faction.Klaed, [Faction.Nairan], false, errants: false);
        g.Player = player;
        var next = Neighbours(g, West).First(p => g.Planets[p].Owner is null);
        var klaed = FleetsAt(g, West, Faction.Klaed)[0];
        var them = AddShipsAt(g, Faction.Nairan, next, nairan)!;
        Assert.True(SendFleet(g, klaed.Id, next).Ok);
        Assert.True(SendFleet(g, them.Id, West).Ok);
        return (g, klaed, them, next);
    }

    static GalaxyBattle RunToBattle(Galaxy g)
    {
        for (var t = 0.0; t < 120 && g.Battles.Count == 0; t += Step) StepGalaxy(g);
        return Assert.Single(g.Battles);
    }

    [Fact]
    public void Fleets_heading_at_each_other_stop_where_they_meet_and_fight_there()
    {
        var (g, klaed, nairan, next) = Crossing(Faction.Klaed, new ShipCounts { [ShipClass.Frigate] = 3 });
        var b = RunToBattle(g);
        Assert.Equal(new LaneSpot(West, next, klaed.Progress), b.Lane);
        Assert.Equal(BattleStatus.Pending, b.Status);
        Assert.Equal(b.Id, klaed.BattleId);
        Assert.Equal(b.Id, nairan.BattleId);
        // Both stand at the same point of the lane, neither docked anywhere.
        Assert.Null(klaed.At);
        Assert.Null(nairan.At);
        Assert.Equal(1, klaed.Progress + nairan.Progress, 4);
        Assert.InRange(klaed.Progress, 0.01, 0.99);
        // No planet is fought over: both ends keep building.
        Assert.False(BattleAt(g, West));
        Assert.False(BattleAt(g, next));
        Assert.StartsWith("Interception entre", BattleName(g, b.PlanetId, b.Lane));
    }

    [Fact]
    public void The_winner_takes_up_its_way_again_and_the_loser_never_gets_behind_the_lines()
    {
        var (g, klaed, nairan, next) = Crossing(Faction.Nautolan, new ShipCounts { [ShipClass.Scout] = 1 });
        var ended = new List<BattleEnded>();
        for (var t = 0.0; t < 180 && klaed.At is null; t += Step)
        {
            StepGalaxy(g);
            ended.AddRange(g.Events.OfType<BattleEnded>());
            g.Events.Clear();
        }
        var end = Assert.Single(ended);
        Assert.Equal(BattleResult.Attacker, end.Result);
        Assert.NotNull(end.Lane);
        Assert.False(g.Fleets.ContainsKey(nairan.Id));
        Assert.Equal(next, klaed.At);
        Assert.Null(klaed.BattleId);
        Assert.Equal(Faction.Klaed, g.Planets[West].Owner);
    }

    [Fact]
    public void After_a_stalemate_both_fleets_turn_back_and_do_not_meet_again()
    {
        var (g, klaed, nairan, next) = Crossing(Faction.Klaed, new ShipCounts { [ShipClass.Frigate] = 3 });
        var b = RunToBattle(g);
        ConcludeBattle(g, b.Id, BattleResult.Retreat);
        Assert.Equal(West, klaed.To);
        Assert.Equal(next, nairan.To);
        Assert.Null(klaed.BattleId);
        for (var t = 0.0; t < 120 && (klaed.At is null || nairan.At is null); t += Step) StepGalaxy(g);
        Assert.Empty(g.Battles);
        Assert.Equal(West, klaed.At);
        Assert.Equal(next, nairan.At);
    }

    [Fact]
    public void Fleets_of_one_faction_pass_each_other()
    {
        var g = CreateGalaxy(Faction.Klaed, [Faction.Nairan], false, errants: false);
        var next = Neighbours(g, West).First(p => g.Planets[p].Owner is null);
        var home = FleetsAt(g, West, Faction.Klaed)[0];
        var other = AddShipsAt(g, Faction.Klaed, next, new ShipCounts { [ShipClass.Frigate] = 2 })!;
        SendFleet(g, home.Id, next);
        SendFleet(g, other.Id, West);
        RunGalaxy(g, 60);
        Assert.Empty(g.Battles);
        Assert.True(FleetsAt(g, next, Faction.Klaed).Count > 0);
        Assert.True(FleetsAt(g, West, Faction.Klaed).Count > 0);
    }

    [Fact]
    public void A_game_saved_during_an_interception_meets_again_once_loaded()
    {
        var (g, _, _, next) = Crossing(Faction.Klaed, new ShipCounts { [ShipClass.Frigate] = 3 });
        RunToBattle(g);
        var loaded = GalaxySave.Deserialize(GalaxySave.Serialize(g))!;
        Assert.Empty(loaded.Battles);
        var b = RunToBattle(loaded);
        Assert.Equal(West, b.Lane!.A);
        Assert.Equal(next, b.Lane.B);
    }
}
