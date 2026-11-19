// Raids, a rule of this version only: a neutral colossus far from every station, deadly up
// close, and a bounty for whoever brings it down; the AI goes for it once it can, and keeps clear
// of it until then.
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

    // The AI hunts it once it can bring it down, and only then.

    static readonly Side Player = Lobby.SideOf(0), Bot = Lobby.SideOf(1);

    /// <summary>
    /// About 10 000 credits of what AIs build, in the shares of a measure between AIs: siege guns,
    /// a cruiser, frigates, bombers, torpedo boats, scouts and corvettes.
    /// </summary>
    static readonly (ShipClass Cls, int N)[] Squadron =
    [
        (ShipClass.Artillery, 2), (ShipClass.Battlecruiser, 1), (ShipClass.Frigate, 3), (ShipClass.Bomber, 9),
        (ShipClass.Torpedo, 2), (ShipClass.Scout, 3), (ShipClass.Corvette, 2),
    ];

    /// <summary>
    /// A passive player and an AI with that many squadrons waiting by its station, and no wave in
    /// mind: only the colossus to go for. `threatened`: it saw 60 000 credits of enemy cruisers
    /// first, beside its station, far enough from it and its platforms not to count as a threat,
    /// holding fire and too tough to fall.
    /// </summary>
    static (World W, FactionState F, Entity Colossus, List<Entity> Fleet) Hunters(int squadrons,
        Difficulty level = Difficulty.Normal, Personality personality = Personality.Balanced, bool threatened = false)
    {
        var w = Scenario.CreateSkirmish(new SkirmishOptions
        {
            Slots = [new(Player, Faction.Klaed), new(Bot, Faction.Klaed, 0, level, null, personality)], Seed = 5, Raid = true,
        });
        var f = w.Factions[Bot];
        f.AI!.WaveValue = 1e9;
        var st = StationOf(w, Bot)!;
        var a = Math.Atan2(w.Height / 2 - st.Y, w.Width / 2 - st.X);
        if (threatened)
        {
            var side = a + Math.PI / 2;
            if (st.X + Math.Cos(side) * 1000 < 300 || st.X + Math.Cos(side) * 1000 > w.Width - 300 || st.Y + Math.Sin(side) * 1000 < 300 || st.Y + Math.Sin(side) * 1000 > w.Height - 300) side = a - Math.PI / 2;
            for (var i = 0; i < 30; i++)
            {
                var e = SpawnShip(w, Player, ShipClass.Battlecruiser, st.X + Math.Cos(side) * 1000 + (i % 6 - 2.5) * 50, st.Y + Math.Sin(side) * 1000 + (i / 6 - 2) * 50);
                e.Stance = Stance.Passive;
                e.Hull = e.MaxHull = 1e6;
            }
            Run(w, 3, 1.0 / 30);
        }
        var fleet = new List<Entity>();
        foreach (var (cls, n) in Squadron)
        {
            for (var k = 0; k < n * squadrons; k++)
            {
                var i = fleet.Count;
                var (along, across) = (150 + i / 10 * 50, (i % 10 - 4.5) * 50);
                fleet.Add(SpawnShip(w, Bot, cls, st.X + Math.Cos(a) * along - Math.Sin(a) * across, st.Y + Math.Sin(a) * along + Math.Cos(a) * across, a));
            }
        }
        return (w, f, Colossus(w), fleet);
    }

    /// <summary>The nearest any of these ships came to the colossus in that time.</summary>
    static double Watch(World w, List<Entity> fleet, Entity colossus, double seconds)
    {
        var closest = double.PositiveInfinity;
        for (var t = 0; t < seconds; t++)
        {
            Run(w, 1, 1.0 / 30);
            foreach (var e in fleet) if (w.Entities.Has(e.Id)) closest = Math.Min(closest, Dist(e, colossus));
        }
        return closest;
    }

    [Fact]
    public void An_AI_with_the_fleet_for_it_hunts_the_colossus_down_and_wins_the_bounty()
    {
        var (w, f, colossus, _) = Hunters(6);
        for (var t = 0; t < 420 && w.Entities.Has(colossus.Id); t++) Run(w, 1, 1.0 / 30);
        Assert.False(w.Entities.Has(colossus.Id), "the colossus should have fallen");
        Assert.Contains(w.Events, e => e is RaidWon r && r.Side == Bot);
        Expect.Greater(f.Bounties, Raids.Bounty - 1);
    }

    [Theory]
    // Too weak: 30 000 credits of ships against the colossus would most often be lost to the last.
    [InlineData(3, Difficulty.Normal, Personality.Balanced, false)]
    [InlineData(3, Difficulty.Hard, Personality.Balanced, false)]
    // Easy weighs nothing, so tries nothing; the defensive AI waits for a wider edge.
    [InlineData(6, Difficulty.Easy, Personality.Balanced, false)]
    [InlineData(6, Difficulty.Normal, Personality.Defensive, false)]
    // Strong enough, but the enemy army it saw would find its home bare.
    [InlineData(6, Difficulty.Normal, Personality.Balanced, true)]
    public void An_AI_short_of_the_fleet_for_it_or_needed_at_home_leaves_the_colossus_alone(int squadrons, Difficulty level, Personality personality, bool threatened)
    {
        var (w, _, colossus, fleet) = Hunters(squadrons, level, personality, threatened);
        var closest = Watch(w, fleet, colossus, 120);
        // None went near it, and nothing scratched it.
        Expect.Greater(closest, 1600);
        Assert.Equal(colossus.MaxHull, colossus.Hull);
        Assert.Equal(colossus.MaxShield, colossus.Shield);
    }

    [Fact]
    public void An_AI_builds_no_mine_in_the_colossus_corner()
    {
        var w = Skirmish();
        var c = Colossus(w);
        var f = w.Factions[Faction.Nairan];
        f.Credits = 5000;
        // Every field taken but one in the colossus's corner and one far from it.
        var haunted = w.Fields.OrderBy(fl => Dist(fl, c)).First();
        var free = w.Fields.Where(fl => Dist(fl, c) > 2500).OrderBy(fl => Dist(fl, StationOf(w, Faction.Nairan)!)).First();
        foreach (var fl in w.Fields) if (fl != haunted && fl != free) fl.MineId = -1;
        Expect.Less(Dist(haunted, c), 1500);
        Run(w, 90, 1.0 / 30);
        Assert.Null(haunted.MineId);
        Assert.DoesNotContain(w.Entities, e => e.Side == Faction.Nairan && Dist(e, c) < 1500);
    }
}
