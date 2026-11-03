// Veterans in the campaign, a rule of this version only: a fleet keeps its ships' ranks from one
// battle to the next, its rookies fall first, ships that leave it take its best, and saves from
// before veterans still load.
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.Galaxies;

namespace SandboxShip.Sim.Tests;

public class CampaignVeteransTests
{
    static readonly int West = GalaxyMap.Capitals[0];

    static Galaxy Quiet() => CreateGalaxy(Faction.Klaed, [Faction.Nairan], false, errants: false);

    static Fleet Home(Galaxy g) => FleetsAt(g, West, Faction.Klaed)[0];

    static RankCounts Ranks(ShipClass c, params int[] byRank)
    {
        var r = new RankCounts();
        for (var i = 0; i < byRank.Length; i++) r.Add(c, i + 1, byRank[i]);
        return r;
    }

    [Fact]
    public void Losses_take_rookies_first_and_ships_leaving_take_the_best()
    {
        var r = Ranks(ShipClass.Frigate, 1, 1, 1);
        // Five frigates, three veterans: two lost are rookies.
        r.Fit(new ShipCounts { [ShipClass.Frigate] = 3 });
        Assert.Equal((1, 1, 1), (r.Of(ShipClass.Frigate, 1), r.Of(ShipClass.Frigate, 2), r.Of(ShipClass.Frigate, 3)));
        // One more lost: the lowest veteran.
        r.Fit(new ShipCounts { [ShipClass.Frigate] = 2 });
        Assert.Equal((0, 1, 1), (r.Of(ShipClass.Frigate, 1), r.Of(ShipClass.Frigate, 2), r.Of(ShipClass.Frigate, 3)));
        var taken = r.Take(new ShipCounts { [ShipClass.Frigate] = 1 });
        Assert.Equal(1, taken.Of(ShipClass.Frigate, 3));
        Assert.Equal((0, 1, 0), (r.Of(ShipClass.Frigate, 1), r.Of(ShipClass.Frigate, 2), r.Of(ShipClass.Frigate, 3)));
        // Forged counts read as none.
        var forged = new RankCounts { [ShipClass.Scout] = [-4, 2, 1, 9, 9] };
        Assert.Equal(3, forged.Of(ShipClass.Scout));
        forged.Fit(new ShipCounts());
        Assert.Empty(forged);
    }

    [Fact]
    public void A_fleet_s_veterans_come_into_battle_with_their_rank_the_best_first()
    {
        var side = new BattleSide { Side = Faction.Klaed, Ships = new ShipCounts { [ShipClass.Frigate] = 4 }, Ranks = Ranks(ShipClass.Frigate, 0, 1, 2) };
        var w = Battle.CreatePlanetBattle(side, new BattleSide { Side = Faction.Nairan, Ships = new ShipCounts { [ShipClass.Scout] = 1 } }, PlanetDefense.None);
        var frigates = w.Entities.Where(e => e.Side == Faction.Klaed && e.Kind == EntityKind.Ship).ToList();
        Assert.Equal([3, 3, 2, 0], frigates.Select(e => e.Rank));
        Assert.Empty(w.Events.OfType<Promoted>());
        var back = Veterans.Survivors(w, Faction.Klaed);
        Assert.Equal((0, 1, 2), (back.Of(ShipClass.Frigate, 1), back.Of(ShipClass.Frigate, 2), back.Of(ShipClass.Frigate, 3)));
    }

    [Fact]
    public void Ranks_earned_in_a_campaign_battle_go_back_to_the_fleet()
    {
        var g = Quiet();
        var f = Home(g);
        var cls = f.Ships.Keys.First();
        var battle = new BattleSide { Side = Faction.Klaed, Ships = f.Ships.Clone() };
        var w = Battle.CreatePlanetBattle(battle, new BattleSide { Side = Faction.Nairan }, PlanetDefense.None);
        var veteran = w.Entities.First(e => e.Side == Faction.Klaed && e.Kind == EntityKind.Ship && e.Cls == cls);
        Veterans.Promote(w, veteran, 2);
        var other = g.Planets.First(p => p.Id != West && p.Owner is null).Id;
        f.BattleId = 99;
        g.Battles.Add(new GalaxyBattle { Id = 99, PlanetId = West, Attacker = Faction.Nairan, Defender = Faction.Klaed, From = other, World = w });
        ConcludeBattle(g, 99, BattleResult.Defender);
        var home = Home(g);
        Assert.Equal(1, home.Ranks.Of(cls, 2));
    }

    [Fact]
    public void Ships_sent_away_from_a_fleet_take_its_best_and_fleets_that_meet_pool_theirs()
    {
        var g = Quiet();
        var f = Home(g);
        var cls = f.Ships.Keys.First(c => f.Ships.Of(c) >= 2);
        f.Ranks = Ranks(cls, 1, 0, 1);
        var dest = FindPath(g, West, GalaxyMap.Capitals[1])![1];
        Assert.True(SendFleet(g, f.Id, dest, new ShipCounts { [cls] = 1 }).Ok);
        var gone = g.Fleets.Values.Single(x => x.Side == Faction.Klaed && x.Id != f.Id);
        Assert.Equal(1, gone.Ranks.Of(cls, 3));
        Assert.Equal((1, 0), (f.Ranks.Of(cls, 1), f.Ranks.Of(cls, 3)));
        // Back together, the veterans are one fleet's again.
        AddShipsAt(g, Faction.Klaed, West, gone.Ships, null, 1, gone.Ranks);
        Assert.Equal((1, 1), (f.Ranks.Of(cls, 1), f.Ranks.Of(cls, 3)));
    }

    [Fact]
    public void Veterans_are_saved_and_a_save_from_before_them_still_loads()
    {
        var g = Quiet();
        var f = Home(g);
        var cls = f.Ships.Keys.First();
        f.Ranks = Ranks(cls, 0, 1);
        var text = GalaxySave.Serialize(g, 1);
        var back = GalaxySave.Deserialize(text)!;
        Assert.Equal(1, back.Fleets[f.Id].Ranks.Of(cls, 2));
        // The same save without the field, as older versions wrote it.
        var node = System.Text.Json.Nodes.JsonNode.Parse(text)!;
        foreach (var fleet in node["fleets"]!.AsArray()) Assert.True(fleet!.AsObject().Remove("ranks"));
        var old = GalaxySave.Deserialize(node.ToJsonString())!;
        Assert.Empty(old.Fleets[f.Id].Ranks);
        Assert.Equal(f.Ships.Of(cls), old.Fleets[f.Id].Ships.Of(cls));
    }
}
