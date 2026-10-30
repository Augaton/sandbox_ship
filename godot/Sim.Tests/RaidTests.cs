// Raids, a rule of this version only: a neutral colossus far from every station, deadly up
// close, and a bounty for whoever brings it down.
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.Steps;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim.Tests;

public class RaidTests
{
    static World Skirmish() => Scenario.CreateSkirmish(new SkirmishOptions
    {
        Player = Faction.Klaed, Enemies = [Faction.Nairan], EnemyAI = true, Seed = 5, Raid = true,
    });

    static Entity Colossus(World w) => w.Entities.First(e => e.Type == "colossus");

    [Fact]
    public void The_colossus_guards_the_spot_farthest_from_every_station_and_nobody_builds_one()
    {
        var w = Skirmish();
        var c = Colossus(w);
        Assert.Equal(Faction.Errant, c.Side);
        foreach (var st in w.Entities.Where(e => e.Type == "station")) Expect.Greater(Dist(c, st), 2000);
        Assert.False(Structures.CanBuildShip(w, Faction.Klaed, ShipClass.Colossus).Ok);
        Assert.DoesNotContain(Faction.Errant, Factions.All);
    }

    [Fact]
    public void It_strikes_ships_that_come_near_and_stays_on_its_site()
    {
        var w = Skirmish();
        var c = Colossus(w);
        var (gx, gy) = (c.X, c.Y);
        var intruder = SpawnShip(w, Faction.Klaed, ShipClass.Frigate, c.X + 600, c.Y);
        intruder.Order = Order.Move(intruder.X, intruder.Y);
        Run(w, 20);
        Assert.True(intruder.Hull <= 0 || !w.Entities.Has(intruder.Id), "the frigate should not survive");
        Expect.Less(Hypot(c.X - gx, c.Y - gy), 800);
    }

    [Fact]
    public void Bringing_it_down_pays_a_bounty_and_a_research_level()
    {
        var w = Skirmish();
        var c = Colossus(w);
        var f = w.Factions[Faction.Nairan];
        var credits = f.Credits;
        var levels = Techs.All.Sum(t => f.Research[t]);
        c.Hull = 0;
        c.LastHitBy = Faction.Nairan;
        RemoveDead(w);
        Assert.Equal(credits + Raids.Bounty, f.Credits);
        Assert.Equal(levels + 1, Techs.All.Sum(t => f.Research[t]));
        Assert.Contains(w.Events, e => e is RaidWon r && r.Side == Faction.Nairan);
        // Neutral guardians never count for victory.
        CheckVictory(w);
        Assert.Null(w.Winner);
    }

    [Fact]
    public void A_shield_knocked_down_stays_down_a_while_longer_on_bigger_hulls()
    {
        var w = CreateWorld(4000, 4000);
        AddFaction(w, Faction.Klaed, 0);
        var scout = SpawnShip(w, Faction.Klaed, ShipClass.Scout, 500, 500);
        var cruiser = SpawnShip(w, Faction.Klaed, ShipClass.Battlecruiser, 1500, 500);
        foreach (var e in new[] { scout, cruiser }) ApplyDamage(e, e.Shield + 1);
        Run(w, ShieldBreak(SizeClass.Small) - 0.5);
        Assert.Equal(0, scout.Shield);
        Run(w, 1);
        Expect.Greater(scout.Shield, 0);
        Assert.Equal(0, cruiser.Shield);
        Run(w, ShieldBreak(SizeClass.Large) - ShieldBreak(SizeClass.Small));
        Expect.Greater(cruiser.Shield, 0);
    }

    [Fact]
    public void Shooting_a_ship_down_pays_a_share_of_its_value()
    {
        var w = CreateWorld(4000, 4000);
        AddFaction(w, Faction.Klaed, 0);
        AddFaction(w, Faction.Nairan, 0);
        var frigate = SpawnShip(w, Faction.Nairan, ShipClass.Frigate, 500, 500);
        frigate.Hull = 0;
        frigate.LastHitBy = Faction.Klaed;
        RemoveDead(w);
        var f = w.Factions[Faction.Klaed];
        Assert.Equal(Js.Round(KillShare * Ships.UnitValue(ShipClass.Frigate)), f.Credits);
        Assert.Equal(f.Credits, f.Bounties);
    }
}
