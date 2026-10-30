// Port of tests/extremes.test.ts: nothing may leave a game stuck with no way to end or to go
// on. The galaxy cases are in GalaxyTests; the sandbox spawn cap (input/selection.ts) belongs
// to the game, not to the simulation.
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.Steps;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim.Tests;

public class ExtremesTests
{
    static void Disarm(World w, Entity e)
    {
        foreach (var part in ChildrenOf(w, e.Id).Prepend(e)) part.Weapons = [];
        e.Range = 0;
    }

    /// <summary>Nothing out of bounds, nothing dead still around, no part without its parent.</summary>
    static void Sane(World w)
    {
        foreach (var e in w.Entities)
        {
            Assert.True(double.IsFinite(e.X) && double.IsFinite(e.Y) && double.IsFinite(e.Hull) && double.IsFinite(e.Shield));
            Expect.Greater(e.Hull, 0);
            if (e.ParentId is int p) Assert.True(w.Entities.Has(p));
        }
        foreach (var f in w.Factions.Values) Assert.True(double.IsFinite(f.Credits) && f.Credits >= 0);
        foreach (var p in w.Projectiles) Assert.True(double.IsFinite(p.X) && double.IsFinite(p.Y));
    }

    // Stations.

    [Fact]
    public void Rebuild_a_lost_shipyard_once_left_alone_so_that_production_can_always_resume()
    {
        var w = CreateWorld(6000, 4000);
        AddFaction(w, Faction.Klaed, 5000);
        AddFaction(w, Faction.Nairan, 0);
        var st = SpawnStation(w, Faction.Klaed, 700, 2000);
        ChildrenOf(w, st.Id).Find(c => c.Type == "shipyard")!.Hull = 0;
        StepWorld(w);
        Assert.Equal(Result.Fail("Chantier naval détruit"), Structures.CanBuildShip(w, Faction.Klaed, ShipClass.Frigate));
        // Under fire, nothing is rebuilt.
        for (double t = 0; t < Buildables.RebuildTime + 10; t += 1)
        {
            st.HitAt = w.Time;
            Run(w, 1);
        }
        Assert.False(Structures.CanBuildShip(w, Faction.Klaed, ShipClass.Frigate).Ok);
        Run(w, Buildables.RebuildQuiet + Buildables.RebuildTime + 1);
        Assert.True(Structures.CanBuildShip(w, Faction.Klaed, ShipClass.Frigate).Ok);
        Assert.Contains(w.Events, e => e is Rebuilt);
    }

    // Planet battles.

    [Fact]
    public void End_even_when_no_side_can_hurt_the_other_the_attacker_falls_back()
    {
        var w = Battle.CreatePlanetBattle(
            new BattleSide { Side = Faction.Klaed, Ships = new() { [ShipClass.Battlecruiser] = 1 } },
            new BattleSide { Side = Faction.Nairan },
            PlanetDefense.None with { Station = 1 });
        foreach (var e in w.Entities) if (e.Kind == EntityKind.Ship || e.Type == "station") Disarm(w, e);
        Run(w, Battle.Stalemate - 5, 1.0 / 30);
        Assert.Null(Battle.Outcome(w, Faction.Klaed, Faction.Nairan));
        Run(w, 10, 1.0 / 30);
        Assert.Equal(BattleResult.Retreat, Battle.Outcome(w, Faction.Klaed, Faction.Nairan));
    }

    // Chaos.

    [Fact]
    public void Three_AIs_random_spawns_abilities_and_kills_the_simulation_stays_sound()
    {
        var w = Scenario.CreateSkirmish(new SkirmishOptions
        {
            Player = Faction.Klaed, Enemies = [Faction.Nairan, Faction.Nautolan], EnemyAI = true, Seed = 31, Size = MapSize.Small,
        });
        Faction[] factions = [Faction.Klaed, Faction.Nairan, Faction.Nautolan];
        foreach (var f in factions) w.Factions[f].AutoAbilities = true;
        T Pick<T>(IReadOnlyList<T> list) => list[(int)Math.Floor(Rand(w) * list.Count)];
        for (var second = 0; second < 240; second++)
        {
            // Every second, something unexpected: a ship anywhere, an ability fired at random,
            // a random entity destroyed outright, a faction emptied of credits.
            var f = Pick(factions);
            SpawnShip(w, f, Pick(Ships.All), Rand(w) * w.Width, Rand(w) * w.Height, Rand(w) * 6);
            var all = w.Entities.ToList();
            var caster = all.Find(e => e.Ability is not null && e.Ability.Cooldown == 0 && Rand(w) < 0.3);
            var victim = Pick(all);
            if (caster is not null) AbilitySystem.UseAbility(w, caster, new Aim(Rand(w) * w.Width, Rand(w) * w.Height, victim?.Id));
            if (victim is not null && Rand(w) < 0.2) victim.Hull = 0;
            if (Rand(w) < 0.05) w.Factions[f].Credits = 0;
            Run(w, 1, 1.0 / 30);
            Sane(w);
            w.Events.Clear();
        }
        Expect.Less(w.Projectiles.Count, 5000);
    }
}
