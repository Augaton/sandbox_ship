// Port of tests/qol.test.ts. Alerts (ui), sounds (audio) and faction colors (render) are not
// part of the simulation and stay in the web version's tests.
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.Steps;
using static SandboxShip.Sim.Structures;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim.Tests;

public class QolTests
{
    // Looped production.

    [Fact]
    public void Puts_the_class_back_in_the_queue_as_each_one_comes_out_until_stopped()
    {
        var w = CreateWorld(6000, 4000);
        var f = AddFaction(w, Faction.Klaed, 10000);
        AddFaction(w, Faction.Nairan, 0);
        SpawnStation(w, Faction.Klaed, 700, 2000);
        f.Repeat = [ShipClass.Frigate];
        StepWorld(w);
        Assert.Single(f.Queue);
        Run(w, Ships.Defs[ShipClass.Frigate].BuildTime * 2 + 1);
        int Frigates() => w.Entities.Count(e => e.Type == "frigate" && e.Side == Faction.Klaed);
        Assert.Equal(2, Frigates());
        Assert.Single(f.Queue);
        f.Repeat = [];
        Run(w, Ships.Defs[ShipClass.Frigate].BuildTime + 1);
        Assert.Equal(3, Frigates());
        Assert.Empty(f.Queue);
    }

    [Fact]
    public void Looped_production_waits_when_credits_run_short()
    {
        var w = CreateWorld(6000, 4000);
        var f = AddFaction(w, Faction.Klaed, 0);
        AddFaction(w, Faction.Nairan, 0);
        SpawnStation(w, Faction.Klaed, 700, 2000);
        f.Repeat = [ShipClass.Frigate];
        StepWorld(w);
        Assert.Empty(f.Queue);
        f.Credits = 1000;
        StepWorld(w);
        Assert.Single(f.Queue);
    }

    // Hits.

    [Fact]
    public void Hits_say_what_was_hit_and_whose_shield_took_it()
    {
        var w = CreateWorld(6000, 4000);
        AddFaction(w, Faction.Klaed, 0);
        AddFaction(w, Faction.Nairan, 0);
        SpawnShip(w, Faction.Klaed, ShipClass.Scout, 1000, 1000);
        var target = SpawnShip(w, Faction.Nairan, ShipClass.Frigate, 1150, 1000);
        // Unarmed, its turret's beam too: only the scout's hits count.
        target.Weapons = [];
        foreach (var part in ChildrenOf(w, target.Id)) part.Weapons = [];
        Hit? hit = null;
        for (var i = 0; i < 600 && hit is null; i++)
        {
            StepWorld(w);
            hit = w.Events.OfType<Hit>().FirstOrDefault();
            w.Events.Clear();
        }
        Assert.Equal(Faction.Nairan, hit?.Side);
        // On the frigate or one of its parts, the frigate's shield takes it.
        var struck = w.Entities.Get(hit!.TargetId)!;
        Assert.True(struck == target || struck.ParentId == target.Id);
        Assert.Equal(target.Id, hit.ShieldId);
    }

    // Battle statistics.

    [Fact]
    public void Statistics_count_ships_built_and_lost_kills_damage_and_credits()
    {
        var w = CreateWorld(6000, 4000);
        var k = AddFaction(w, Faction.Klaed, 1000);
        var n = AddFaction(w, Faction.Nairan, 0);
        SpawnStation(w, Faction.Klaed, 700, 2000);
        EnqueueShip(w, Faction.Klaed, ShipClass.Scout);
        Run(w, Ships.Defs[ShipClass.Scout].BuildTime + 0.5);
        Assert.Equal(3, k.Stats.Built);
        Expect.Greater(k.Stats.Earned, 0);
        // A fight far from the station's guns.
        SpawnShip(w, Faction.Klaed, ShipClass.Scout, 4000, 2000);
        SpawnShip(w, Faction.Nairan, ShipClass.Frigate, 4150, 2000);
        for (var i = 0; i < 60 * 60 && k.Stats.Lost == 0; i++) StepWorld(w);
        Expect.Greater(k.Stats.Lost, 0);
        Assert.Equal(k.Stats.Lost, n.Stats.Kills);
        Expect.Greater(n.Stats.Damage, 0);
    }
}
