// Port of tests/combat.test.ts.
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.Steps;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim.Tests;

public class CombatTests
{
    static World NewWorld()
    {
        var w = CreateWorld(6000, 4000);
        AddFaction(w, Faction.Klaed, 0);
        AddFaction(w, Faction.Nairan, 0);
        return w;
    }

    [Fact]
    public void Drains_shields_before_the_hull()
    {
        var w = NewWorld();
        var s = SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 0, 0);
        var maxShield = s.MaxShield;
        var maxHull = s.MaxHull;
        ApplyDamage(s, 100);
        Assert.Equal(maxShield - 100, s.Shield);
        Assert.Equal(maxHull, s.Hull);
        ApplyDamage(s, maxShield);
        Assert.Equal(0, s.Shield);
        Assert.Equal(maxHull - 100, s.Hull);
    }

    [Fact]
    public void Fires_at_enemies_in_range_and_destroys_them()
    {
        var w = NewWorld();
        SpawnShip(w, Faction.Klaed, ShipClass.Battlecruiser, 1000, 1000);
        var victim = SpawnShip(w, Faction.Nairan, ShipClass.Scout, 1300, 1000);
        Run(w, 30);
        Assert.False(w.Entities.Has(victim.Id));
        Assert.Contains(w.Events, e => e is Destroyed d && d.Id == victim.Id);
    }

    [Fact]
    public void Applies_damage_multipliers_by_target_size()
    {
        // Fighters' guns barely scratch structures.
        var w = NewWorld();
        var station = SpawnStation(w, Faction.Nairan, 3000, 2000);
        foreach (var c in ChildrenOf(w, station.Id)) c.Hull = -1;
        StepWorld(w);
        station.Shield = 0;
        station.MaxShield = 0;
        var f = SpawnShip(w, Faction.Klaed, ShipClass.Fighter, 3000, 2000 - station.Radius - 60);
        Movement.IssueAttack(w, [f.Id], station.Id);
        var before = station.Hull;
        Run(w, 5);
        var dealt = before - station.Hull;
        // 3 dmg x 0.2 per shot, ~3.3 shots/s over 5 s: well under 25.
        Expect.Greater(dealt, 0);
        Expect.Less(dealt, 25);
    }

    [Fact]
    public void Destroying_a_station_takes_its_hardpoints_with_it()
    {
        var w = NewWorld();
        var station = SpawnStation(w, Faction.Nairan, 3000, 2000);
        Assert.NotEmpty(ChildrenOf(w, station.Id));
        station.Hull = 0;
        StepWorld(w);
        Assert.False(w.Entities.Has(station.Id));
        Assert.Empty(ChildrenOf(w, station.Id));
        Assert.Null(w.Factions[Faction.Nairan].StationId);
    }

    [Fact]
    public void The_station_loses_its_shields_with_its_shield_generator()
    {
        var w = NewWorld();
        var station = SpawnStation(w, Faction.Nairan, 3000, 2000);
        ChildrenOf(w, station.Id).Find(c => c.Type == "shield")!.Hull = 0;
        StepWorld(w);
        StepWorld(w);
        Assert.Equal(0, station.Shield);
    }

    [Fact]
    public void Support_ships_repair_damaged_allies()
    {
        var w = NewWorld();
        var hurt = SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 1000, 1000);
        SpawnShip(w, Faction.Klaed, ShipClass.Support, 1100, 1000);
        hurt.Hull = 100;
        Run(w, 5);
        Expect.Greater(hurt.Hull, 140);
    }

    [Fact]
    public void Declares_the_last_faction_with_a_station_the_winner()
    {
        var w = NewWorld();
        SpawnStation(w, Faction.Klaed, 700, 2000);
        var enemy = SpawnStation(w, Faction.Nairan, 5300, 2000);
        StepWorld(w);
        Assert.Null(w.Winner);
        enemy.Hull = 0;
        StepWorld(w);
        Assert.Equal(Faction.Klaed, w.Winner);
    }
}

/// <summary>Focus fire, a rule of this version only: a ship's turrets follow its attack order.</summary>
public class FocusFireTests
{
    [Fact]
    public void A_ships_turrets_fire_at_its_ordered_target_once_in_reach_not_at_the_nearest_foe()
    {
        var w = CreateWorld(6000, 4000);
        AddFaction(w, Faction.Klaed, 0);
        AddFaction(w, Faction.Nairan, 0);
        var titan = SpawnShip(w, Faction.Klaed, ShipClass.Titan, 2000, 2000);
        var near = SpawnShip(w, Faction.Nairan, ShipClass.Battlecruiser, 2220, 2000);
        var ordered = SpawnShip(w, Faction.Nairan, ShipClass.Battlecruiser, 2000, 2380);
        var turrets = ChildrenOf(w, titan.Id).Where(p => p.Weapons.Count > 0).Select(p => p.Id).ToHashSet();
        Assert.NotEmpty(turrets);
        Movement.IssueAttack(w, [titan.Id], ordered.Id);
        var aimed = new Dictionary<int, int>();
        for (var i = 0; i < 120; i++)
        {
            var before = w.Projectiles.Select(p => p.Id).ToHashSet();
            StepWorld(w);
            foreach (var p in w.Projectiles.Where(p => p.Side == titan.Side && !before.Contains(p.Id)))
            {
                aimed[p.TargetId] = aimed.GetValueOrDefault(p.TargetId) + 1;
            }
        }
        // Every turret shot goes to the ordered target, though the other cruiser is closer.
        Assert.True(aimed.GetValueOrDefault(ordered.Id) > 0);
        Assert.Equal(0, aimed.GetValueOrDefault(near.Id));
    }
}
