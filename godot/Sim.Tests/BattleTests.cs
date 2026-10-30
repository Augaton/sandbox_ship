// Port of tests/battle.test.ts.
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.Battle;
using static SandboxShip.Sim.Steps;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim.Tests;

public class BattleTests
{
    static BattleSide Side(Faction faction, ShipCounts ships) => new() { Faction = faction, Ships = ships };

    // Planet battles.

    [Fact]
    public void Lay_out_the_defenses_of_the_planet_and_both_fleets()
    {
        var w = CreatePlanetBattle(
            Side(Faction.Klaed, new() { [ShipClass.Frigate] = 3 }),
            Side(Faction.Nairan, new() { [ShipClass.Scout] = 2 }),
            PlanetDefense.None with { Station = 2, Platforms = 2, Mines = 1, RepairDock = 1, PlatformTier = 2 });
        var station = StationOf(w, Faction.Nairan)!;
        Assert.Equal(2, station.Level);
        Assert.Equal(Station.Levels[0].Hardpoints.Count + Station.Levels[1].Hardpoints.Count, ChildrenOf(w, station.Id).Count);
        Assert.Equal(new PlanetDefense { Station = 2, Platforms = 2, Mines = 1, RepairDock = 1, IonCannon = 0, PlatformTier = 2 }, DefenseLeft(w, Faction.Nairan));
        Assert.Equal(new ShipCounts { [ShipClass.Frigate] = 3 }, Survivors(w, Faction.Klaed));
        Assert.Equal(new ShipCounts { [ShipClass.Scout] = 2 }, Survivors(w, Faction.Nairan));
    }

    [Fact]
    public void End_when_one_side_has_nothing_left()
    {
        var w = CreatePlanetBattle(Side(Faction.Klaed, new() { [ShipClass.Frigate] = 1 }), Side(Faction.Nairan, []), PlanetDefense.None with { Station = 1 });
        Assert.Null(Outcome(w, Faction.Klaed, Faction.Nairan));
        StationOf(w, Faction.Nairan)!.Hull = 0;
        Run(w, 0.1);
        Assert.Equal(BattleResult.Attacker, Outcome(w, Faction.Klaed, Faction.Nairan));
        foreach (var e in w.Entities) if (e.Faction == Faction.Klaed) e.Hull = 0;
        Run(w, 0.1);
        Assert.Equal(BattleResult.Defender, Outcome(w, Faction.Klaed, Faction.Nairan));
    }

    [Fact]
    public void Do_not_bring_hangar_craft_back_to_the_map()
    {
        var w = CreatePlanetBattle(Side(Faction.Klaed, new() { [ShipClass.Frigate] = 1 }), Side(Faction.Nairan, []), PlanetDefense.None);
        SpawnShip(w, Faction.Klaed, ShipClass.Fighter, 100, 100).Launched = true;
        Assert.Equal(new ShipCounts { [ShipClass.Frigate] = 1 }, Survivors(w, Faction.Klaed));
        // Fighters built at a shipyard (not in the web version) go back like any warship.
        SpawnShip(w, Faction.Klaed, ShipClass.Fighter, 120, 100);
        Assert.Equal(new ShipCounts { [ShipClass.Frigate] = 1, [ShipClass.Fighter] = 1 }, Survivors(w, Faction.Klaed));
    }

    [Fact]
    public void The_battle_AI_sends_idle_fleets_at_each_other_until_one_side_wins()
    {
        var w = CreatePlanetBattle(
            Side(Faction.Klaed, new() { [ShipClass.Battlecruiser] = 2, [ShipClass.Frigate] = 4 }),
            Side(Faction.Nairan, new() { [ShipClass.Scout] = 3 }),
            PlanetDefense.None);
        BattleResult? result = null;
        for (double t = 0; t < 240 && result is null; t += 1.0 / 30)
        {
            Run(w, 1.0 / 30, 1.0 / 30);
            UpdateBattleAI(w, [Faction.Klaed, Faction.Nairan], 1.0 / 30);
            result = Outcome(w, Faction.Klaed, Faction.Nairan);
        }
        Assert.Equal(BattleResult.Attacker, result);
        Expect.Greater(FleetValue(Survivors(w, Faction.Klaed)), 0);
    }
}
