// Port of tests/hardpoints.test.ts.
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.Movement;
using static SandboxShip.Sim.Steps;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim.Tests;

public class HardpointsTests
{
    static World NewWorld()
    {
        var w = CreateWorld(6000, 4000);
        AddFaction(w, Faction.Klaed, 0);
        AddFaction(w, Faction.Nairan, 0);
        return w;
    }

    static Entity Part(World w, int shipId, string role) => ChildrenOf(w, shipId).Find(c => c.Type == role)!;

    // Ship hardpoints.

    [Fact]
    public void Capital_ships_carry_their_parts_and_their_weapons_on_their_turrets()
    {
        var w = NewWorld();
        var bc = SpawnShip(w, Faction.Klaed, ShipClass.Battlecruiser, 1000, 1000);
        var parts = ChildrenOf(w, bc.Id);
        Assert.Equal(new[] { "engine", "engine", "shield", "turret", "turret", "turret" }, parts.Select(p => p.Type).Order(StringComparer.Ordinal));
        Assert.Empty(bc.Weapons);
        Assert.Equal(450, bc.Range);
        Assert.Equal(2, bc.Engines);
        Assert.Equal(2, bc.MaxEngines);
        Assert.Equal(1, bc.ShieldGens);
        var support = SpawnShip(w, Faction.Klaed, ShipClass.Support, 0, 0);
        Assert.Null(support.Engines);
        Assert.Null(support.ShieldGens);
        var frigate = SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 200, 0);
        Assert.Null(frigate.Engines);
        Assert.Equal(1, frigate.ShieldGens);
    }

    [Fact]
    public void Parts_follow_their_ship_as_it_moves_and_turns()
    {
        var w = NewWorld();
        var bc = SpawnShip(w, Faction.Klaed, ShipClass.Battlecruiser, 1000, 1000);
        var engine = Part(w, bc.Id, "engine");
        IssueMove(w, [bc.Id], 1000, 2000);
        Run(w, 20);
        // The ship now points down (+y): its engines trail behind it, above.
        Expect.Less(engine.Y, bc.Y);
        Expect.Close(Hypot(engine.Ox, engine.Oy), Dist(engine, bc), 5);
    }

    [Fact]
    public void Lost_engines_slow_the_ship_down()
    {
        var w = NewWorld();
        var intact = SpawnShip(w, Faction.Klaed, ShipClass.Battlecruiser, 1000, 1000);
        var crippled = SpawnShip(w, Faction.Klaed, ShipClass.Battlecruiser, 1000, 2000);
        foreach (var c in ChildrenOf(w, crippled.Id)) if (c.Type == "engine") c.Hull = 0;
        StepWorld(w);
        Assert.Equal(0, crippled.Engines);
        IssueMove(w, [intact.Id], 3000, 1000);
        IssueMove(w, [crippled.Id], 3000, 2000);
        Run(w, 10);
        Expect.Greater(intact.X - 1000, (crippled.X - 1000) * 3);
    }

    [Fact]
    public void The_shields_go_down_with_the_shield_generator_and_cover_the_parts_until_then()
    {
        var w = NewWorld();
        var bc = SpawnShip(w, Faction.Klaed, ShipClass.Battlecruiser, 1000, 1000);
        var turret = Part(w, bc.Id, "turret");
        // A torpedo bomber's shot lands on a turret: the ship's shields take it.
        var bomber = SpawnShip(w, Faction.Nairan, ShipClass.Torpedo, 1300, 1000, Math.PI);
        bomber.Weapons[0].Cooldown = 0;
        bomber.TargetId = turret.Id;
        bomber.Order = Order.Attack(turret.Id);
        Run(w, 2);
        Assert.Equal(turret.MaxHull, turret.Hull);
        Expect.Less(bc.Shield, bc.MaxShield);
        Part(w, bc.Id, "shield").Hull = 0;
        Run(w, 0.1);
        Assert.Equal(0, bc.Shield);
    }

    [Fact]
    public void A_ship_whose_turrets_are_gone_cannot_fire()
    {
        var w = NewWorld();
        var bc = SpawnShip(w, Faction.Klaed, ShipClass.Battlecruiser, 1000, 1000);
        foreach (var c in ChildrenOf(w, bc.Id)) if (c.Type == "turret") c.Hull = 0;
        var scout = SpawnShip(w, Faction.Nairan, ShipClass.Scout, 1250, 1000);
        Run(w, 10);
        Assert.True(w.Entities.Has(scout.Id));
        Assert.DoesNotContain(w.Projectiles, p => p.Faction == Faction.Klaed);
    }

    [Fact]
    public void Parts_die_with_their_ship()
    {
        var w = NewWorld();
        var dn = SpawnShip(w, Faction.Klaed, ShipClass.Dreadnought, 1000, 1000);
        Assert.Equal(Ships.Defs[ShipClass.Dreadnought].Hardpoints!.Count, ChildrenOf(w, dn.Id).Count);
        dn.Hull = 0;
        StepWorld(w);
        Assert.Empty(ChildrenOf(w, dn.Id));
    }

    // Piloting.

    [Fact]
    public void Flies_where_the_pilot_steers()
    {
        var w = NewWorld();
        var s = SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 1000, 1000);
        s.Pilot = new Pilot { Thrust = 1 };
        Run(w, 3);
        Expect.Greater(s.X, 1100);
        Expect.Less(Math.Abs(s.Y - 1000), 1);
        s.Pilot.Turn = 1;
        Run(w, 1);
        Expect.Greater(s.Angle, 0.5);
    }

    [Fact]
    public void Fires_only_on_the_trigger_at_the_enemy_nearest_the_aim()
    {
        var w = NewWorld();
        var s = SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 1000, 1000);
        var near = SpawnShip(w, Faction.Nairan, ShipClass.Scout, 1200, 950);
        var aimed = SpawnShip(w, Faction.Nairan, ShipClass.Scout, 1200, 1100);
        foreach (var e in new[] { near, aimed }) e.Pilot = new Pilot();
        s.Pilot = new Pilot { AimX = 1200, AimY = 1100 };
        Run(w, 2);
        Assert.DoesNotContain(w.Projectiles, p => p.Faction == Faction.Klaed);
        s.Pilot.Firing = true;
        Run(w, 0.5);
        var shots = w.Projectiles.Where(p => p.Faction == Faction.Klaed).ToList();
        Assert.NotEmpty(shots);
        Assert.All(shots, p => Assert.Equal(aimed.Id, p.TargetId));
    }
}
