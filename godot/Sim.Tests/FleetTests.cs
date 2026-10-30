// Rules of this version only (not in the web version): craft escort their carrier and go for
// what it fights; ships ordered together move as one fleet, toughest hulls in front.
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.Steps;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim.Tests;

public class FleetTests
{
    static World Open()
    {
        var w = CreateWorld(8000, 4000);
        AddFaction(w, Faction.Klaed, 0);
        AddFaction(w, Faction.Nairan, 0);
        return w;
    }

    static List<Entity> CraftOf(World w, Entity carrier) => w.Entities.Where(e => e.CarrierId == carrier.Id).ToList();

    [Fact]
    public void Craft_launched_by_a_carrier_go_for_what_it_attacks()
    {
        var w = Open();
        var d = SpawnShip(w, Faction.Klaed, ShipClass.Dreadnought, 1000, 2000);
        Run(w, 3);
        var craft = CraftOf(w, d);
        Assert.NotEmpty(craft);
        // A decoy right by the craft, the dreadnought's target further off.
        SpawnShip(w, Faction.Nairan, ShipClass.Scout, craft[0].X, craft[0].Y + 60);
        var target = SpawnShip(w, Faction.Nairan, ShipClass.Frigate, 1500, 2300);
        Movement.IssueAttack(w, [d.Id], target.Id);
        Run(w, 1);
        foreach (var c in craft.Where(c => w.Entities.Has(c.Id))) Assert.Equal(target.Id, c.TargetId);
        var before = craft.Average(c => Dist(c, target));
        Run(w, 2);
        Expect.Less(craft.Where(c => w.Entities.Has(c.Id)).Average(c => Dist(c, target)), before);
    }

    [Fact]
    public void Craft_follow_their_carrier_when_it_moves()
    {
        var w = Open();
        var d = SpawnShip(w, Faction.Klaed, ShipClass.Dreadnought, 1000, 2000);
        Run(w, 3);
        var craft = CraftOf(w, d);
        Assert.NotEmpty(craft);
        Movement.IssueMove(w, [d.Id], 4000, 2000);
        Run(w, 30);
        Expect.Greater(d.X, 1600);
        foreach (var c in craft) Expect.Less(Dist(c, d), 300);
    }

    [Fact]
    public void A_fleet_moves_at_the_pace_of_its_slowest_ship_with_the_toughest_in_front()
    {
        var w = Open();
        var d = SpawnShip(w, Faction.Klaed, ShipClass.Dreadnought, 1000, 2000);
        var scouts = Enumerable.Range(0, 3).Select(i => SpawnShip(w, Faction.Klaed, ShipClass.Scout, 900, 1900 + i * 100)).ToList();
        Movement.IssueMove(w, [.. scouts.Select(s => s.Id), d.Id], 5000, 2000);
        // Heading east: the dreadnought's place is ahead of the scouts'.
        foreach (var s in scouts) Expect.Greater(d.Order!.X, s.Order!.X);
        Run(w, 20);
        foreach (var s in scouts) Expect.Less(s.X, d.X + 150);
        Run(w, 120);
        Assert.Null(d.Order);
        foreach (var s in scouts)
        {
            Assert.Null(s.Order);
            Expect.Less(s.X, d.X);
        }
    }

    [Fact]
    public void Stragglers_catch_up_at_full_speed_then_keep_the_pace()
    {
        var w = Open();
        var frigates = Enumerable.Range(0, 2).Select(i => SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 1000, 1900 + i * 200)).ToList();
        var scout = SpawnShip(w, Faction.Klaed, ShipClass.Scout, 0, 2000);
        Movement.IssueMove(w, [.. frigates.Select(f => f.Id), scout.Id], 7000, 2000);
        Run(w, 15);
        // Unpaced, the scout would be a thousand pixels ahead by now: it caught up, then kept the pace.
        Expect.Less(Math.Abs(scout.X - frigates[0].X), 400);
    }
}
