// Port of tests/ai.test.ts.
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.Steps;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim.Tests;

public class AiTests
{
    static World Skirmish(int seed) =>
        Scenario.CreateSkirmish(new SkirmishOptions { Player = Faction.Klaed, Enemies = [Faction.Nairan], EnemyAI = true, Seed = seed });

    [Fact]
    public void Counters_what_it_has_seen_flak_frigates_against_swarms_torpedoes_against_big_hulls()
    {
        var w = Skirmish(5);
        var f = w.Factions[Faction.Nairan];
        var station = StationOf(w, Faction.Nairan)!;
        station.Level = 3;
        double Share(ShipClass cls)
        {
            var n = 0;
            for (var i = 0; i < 2000; i++) if (Ai.PickShip(w, f, station) == cls) n++;
            return n / 2000.0;
        }
        var frigates = Share(ShipClass.Frigate);
        var torpedoes = Share(ShipClass.Torpedo);
        f.AI!.Intel.Small = 10;
        Expect.Greater(Share(ShipClass.Frigate), frigates + 0.1);
        f.AI!.Intel.Small = 0;
        f.AI!.Intel.Large = 3;
        Expect.Greater(Share(ShipClass.Torpedo), torpedoes + 0.1);
    }

    [Fact]
    public void Builds_an_economy_and_a_fleet()
    {
        var w = Skirmish(7);
        // Nine minutes, not three as in the web version: upgrades cost far more here, and the AI
        // gets there in 6 to 8 minutes.
        Run(w, 540, 1.0 / 30);
        var own = w.Entities.Where(e => e.Faction == Faction.Nairan).ToList();
        Assert.True(own.Count(e => e.Type == "mine") >= 2);
        Expect.Greater(w.Entities.Get(w.Factions[Faction.Nairan].StationId)!.Level, 1);
        Expect.Greater(own.Count(e => e.Kind == EntityKind.Ship && e.Type != "fighter" && e.Type != "bomber"), 0);
    }

    [Fact]
    public void Destroys_a_passive_opponent()
    {
        var w = Skirmish(1);
        for (var minute = 0; minute < 25 && w.Winner is null; minute++) Run(w, 60, 1.0 / 30);
        Assert.Equal(Faction.Nairan, w.Winner);
    }

    [Fact]
    public void Two_AIs_fight_without_breaking_the_simulation()
    {
        var w = Skirmish(3);
        w.Factions[Faction.Klaed].AI = NewAI();
        Run(w, 600, 1.0 / 30);
        foreach (var e in w.Entities)
        {
            Assert.True(double.IsFinite(e.X) && double.IsFinite(e.Y) && double.IsFinite(e.Hull));
        }
        Expect.Greater(w.Events.OfType<Destroyed>().Count(), 10);
        Expect.Less(w.Entities.Count, 400);
    }
}
