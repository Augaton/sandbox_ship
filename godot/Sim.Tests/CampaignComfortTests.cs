// What the player of a campaign is told and spared: a battle's toll, occupations, ships built told
// from other builds, rally points that follow what happens to their planet, a side out of the game
// that sees everything, and the single ship sent with a key. The likely outcome of a battle is in
// OutlookTests.cs.
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.Galaxies;

namespace SandboxShip.Sim.Tests;

public class CampaignComfortTests
{
    static readonly Side Klaed = Faction.Klaed, Nairan = Faction.Nairan, Nautolan = Faction.Nautolan;

    static int Id(string name) => GalaxyMap.Planets.ToList().FindIndex(p => p.Name == name);

    /// <summary>Two sides with no AI, no Errants, no random event; the Kla'ed are the player.</summary>
    static Galaxy Plain()
    {
        var g = CreateGalaxy(Faction.Klaed, [Faction.Nairan], false, errants: false, incidents: false);
        g.Events.Clear();
        return g;
    }

    /// <summary>
    /// A Nairan fleet attacks Kaldra, Kla'ed with a level 2 station, 3 mines, 2 platforms, a dock,
    /// 3 frigates and a hero. The battle waits for the player's choice, its world untouched.
    /// </summary>
    static (Galaxy G, GalaxyBattle B, Planet P) Besieged()
    {
        var g = Plain();
        var p = g.Planets[Id("Kaldra")];
        (p.Owner, p.Station, p.Mines, p.Platforms, p.RepairDock) = (Klaed, 2, 3, 2, 1);
        var hero = Heroes.Of(Faction.Klaed).First().Id;
        AddShipsAt(g, Klaed, p.Id, new ShipCounts { [ShipClass.Frigate] = 3 }, [hero]);
        var attackers = AddShipsAt(g, Nairan, p.Id, new ShipCounts { [ShipClass.Frigate] = 2, [ShipClass.Scout] = 4 })!;
        attackers.ArrivedAt = g.Time + 1;
        attackers.Came = p.Id;
        StepGalaxy(g);
        var b = Assert.Single(g.Battles);
        Assert.Equal(BattleStatus.Pending, b.Status);
        Assert.Equal((Nairan, Klaed), (b.Attacker, b.Defender));
        g.Events.Clear();
        return (g, b, p);
    }

    /// <summary>Takes out of a battle the first `n` things of a side that its picker points at.</summary>
    static void Destroy(GalaxyBattle b, Side side, Func<Entity, bool> which, int n = int.MaxValue)
    {
        foreach (var e in b.World.Entities.Where(e => e.Side == side && which(e)).Take(n).ToList()) e.Hull = 0;
        Worlds.RemoveDead(b.World);
    }

    static bool IsShip(Entity e) => e.Kind == EntityKind.Ship && e.Hero is null;

    [Fact]
    public void A_defense_won_tells_the_ships_the_hero_the_mines_and_the_station_it_cost()
    {
        var (g, b, p) = Besieged();
        var hero = b.Heroes[Klaed][0];
        Destroy(b, Klaed, e => e.Type == "mine", 3);
        Destroy(b, Klaed, e => e.Type == "platform", 1);
        Destroy(b, Klaed, e => e.Type == "repairDock");
        Destroy(b, Klaed, IsShip, 2);
        Destroy(b, Klaed, e => e.Hero is not null);
        Destroy(b, Nairan, e => e.Kind == EntityKind.Ship);
        Worlds.StationOf(b.World, Klaed)!.Level = 1;

        ConcludeBattle(g, b.Id, BattleResult.Defender);

        var toll = Assert.Single(g.Events.OfType<BattleEnded>()).Toll;
        Assert.Equal(2, toll.Defender.Ships.Of(ShipClass.Frigate));
        Assert.Equal([hero], toll.Defender.Heroes);
        Assert.Equal(2, toll.Attacker.Ships.Of(ShipClass.Frigate));
        Assert.Equal(4, toll.Attacker.Ships.Of(ShipClass.Scout));
        Assert.Empty(toll.Attacker.Heroes);
        Assert.Equal((2, 2, 3, 1), (toll.Before!.Station, toll.Before.Platforms, toll.Before.Mines, toll.Before.RepairDock));
        Assert.Equal((1, 1, 0, 0), (toll.After!.Station, toll.After.Platforms, toll.After.Mines, toll.After.RepairDock));
        // What it says is what the planet has now.
        Assert.Equal((1, 1, 0, 0), (p.Station, p.Platforms, p.Mines, p.RepairDock));
        Assert.Equal(Klaed, p.Owner);
    }

    [Fact]
    public void A_planet_taken_keeps_only_what_is_on_the_ground_and_the_toll_says_so()
    {
        var (g, b, p) = Besieged();
        Destroy(b, Klaed, e => e.Type == "mine", 1);
        Destroy(b, Klaed, e => e.Kind == EntityKind.Ship);
        Worlds.StationOf(b.World, Klaed)!.Hull = 0;
        Worlds.RemoveDead(b.World);

        // Its end screen asks before anything changes.
        var asked = TollOf(g, b, BattleResult.Attacker);
        Assert.Equal(Klaed, p.Owner);
        Assert.Equal(2, p.Station);

        ConcludeBattle(g, b.Id, BattleResult.Attacker);

        var toll = Assert.Single(g.Events.OfType<BattleEnded>()).Toll;
        Assert.Equal((asked.Before, asked.After, asked.Defender.Heroes.Count), (toll.Before, toll.After, toll.Defender.Heroes.Count));
        Assert.Equal(Nairan, p.Owner);
        Assert.Equal((p.Station, p.Platforms, p.Mines, p.RepairDock, p.IonCannon, p.PlatformTier),
            (toll.After!.Station, toll.After.Platforms, toll.After.Mines, toll.After.RepairDock, toll.After.IonCannon, toll.After.PlatformTier));
        Assert.Equal(2, p.Mines);
        Assert.Equal(0, p.Station);
        Assert.Equal(3, toll.Defender.Ships.Of(ShipClass.Frigate));
        Assert.Single(toll.Defender.Heroes);
    }

    [Fact]
    public void A_battle_over_a_neutral_planet_or_a_lane_has_no_defenses_to_tell()
    {
        var g = Plain();
        var p = g.Planets[Id("Kaldra")];
        AddShipsAt(g, Klaed, p.Id, new ShipCounts { [ShipClass.Frigate] = 1 });
        AddShipsAt(g, Nairan, p.Id, new ShipCounts { [ShipClass.Frigate] = 1 })!.ArrivedAt = g.Time + 1;
        StepGalaxy(g);
        var b = Assert.Single(g.Battles);
        ConcludeBattle(g, b.Id, BattleResult.Retreat);
        var toll = Assert.Single(g.Events.OfType<BattleEnded>()).Toll;
        Assert.Null(toll.Before);
        Assert.Null(toll.After);
        Assert.Empty(toll.Attacker.Ships);
        Assert.Empty(toll.Defender.Ships);
    }

    /// <summary>A replica of `g` as `viewer` sees it, from a snapshot that carries `events`.</summary>
    static Galaxy Replica(Galaxy g, Side viewer, IReadOnlyList<GalaxyEvent> events)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms)) GalaxySnapshot.Write(g, viewer, w, events);
        var replica = new Galaxy();
        GalaxySnapshot.ReadChart(new BinaryReader(new MemoryStream(GalaxySnapshot.Chart(g))), replica);
        Assert.NotNull(GalaxySnapshot.Read(new BinaryReader(new MemoryStream(ms.ToArray())), replica));
        return replica;
    }

    [Fact]
    public void A_battles_toll_reaches_a_replica_whole()
    {
        var (g, b, p) = Besieged();
        Destroy(b, Klaed, e => e.Type == "mine", 2);
        Destroy(b, Klaed, IsShip, 1);
        Destroy(b, Klaed, e => e.Hero is not null);
        Destroy(b, Nairan, e => e.Kind == EntityKind.Ship);
        var hero = b.Heroes[Klaed][0];
        ConcludeBattle(g, b.Id, BattleResult.Defender);
        var sent = Assert.Single(g.Events.OfType<BattleEnded>());

        var got = Assert.Single(Replica(g, Klaed, g.Events).Events.OfType<BattleEnded>());

        Assert.Equal(1, got.Toll.Defender.Ships.Of(ShipClass.Frigate));
        Assert.Equal([hero], got.Toll.Defender.Heroes);
        Assert.Equal(sent.Toll.Attacker.Ships.Of(ShipClass.Scout), got.Toll.Attacker.Ships.Of(ShipClass.Scout));
        Assert.Equal(sent.Toll.Before, got.Toll.Before);
        Assert.Equal(sent.Toll.After, got.Toll.After);
        Assert.Equal(1, got.Toll.After!.Mines);
        Assert.Equal(p.Id, got.PlanetId);
    }

    [Fact]
    public void A_toll_with_no_planet_at_stake_reaches_a_replica_too()
    {
        var g = Plain();
        var lost = new BattleLosses(new ShipCounts { [ShipClass.Scout] = 2 }, []);
        var none = new BattleLosses(new ShipCounts(), []);
        var spot = new LaneSpot(0, Neighbours(g, 0)[0], 0.5f);
        List<GalaxyEvent> events = [new BattleEnded(0, Klaed, Nairan, BattleResult.Attacker, spot, new BattleToll(none, lost, null, null))];
        var got = Assert.Single(Replica(g, Klaed, events).Events.OfType<BattleEnded>());
        Assert.Null(got.Toll.Before);
        Assert.Equal(spot, got.Lane);
        Assert.Equal(2, got.Toll.Defender.Ships.Of(ShipClass.Scout));
    }

    [Fact]
    public void An_occupation_of_a_side_planet_is_told_once_and_to_its_team_only()
    {
        var g = Plain();
        var p = g.Planets[Id("Kaldra")];
        p.Owner = Klaed;
        AddShipsAt(g, Nairan, p.Id, new ShipCounts { [ShipClass.Scout] = 1 });
        RunGalaxy(g, 3);
        var begun = Assert.Single(g.Events.OfType<OccupationBegun>());
        Assert.Equal((p.Id, Nairan, Klaed), (begun.PlanetId, begun.Side, begun.From));
        Assert.Contains(Replica(g, Klaed, g.Events).Events, e => e is OccupationBegun);
        Assert.DoesNotContain(Replica(g, Nairan, g.Events).Events, e => e is OccupationBegun);
    }

    [Fact]
    public void Taking_a_neutral_planet_is_no_news_of_an_occupation()
    {
        var g = Plain();
        var p = g.Planets[Id("Kaldra")];
        AddShipsAt(g, Nairan, p.Id, new ShipCounts { [ShipClass.Scout] = 1 });
        RunGalaxy(g, 3);
        Assert.NotNull(p.Occupation);
        Assert.DoesNotContain(g.Events, e => e is OccupationBegun);
    }

    [Fact]
    public void Ships_built_are_told_from_other_builds_even_on_a_replica()
    {
        var g = Plain();
        var home = g.Planets.First(p => p.Owner == Klaed);
        g.Factions[Klaed].Credits = 100000;
        Assert.True(EnqueueBuild(g, Klaed, home.Id, GalaxyBuild.Ship(ShipClass.Scout)).Ok);
        Assert.True(EnqueueBuild(g, Klaed, home.Id, GalaxyBuild.Of(GalaxyBuildKind.Mine)).Ok);
        home.Units[0].Remaining = 0.1;
        home.Buildings[0].Remaining = 0.1;
        StepGalaxy(g);
        var built = g.Events.OfType<PlanetBuilt>().ToList();
        Assert.Equal(2, built.Count);
        Assert.Single(built, b => b.Ship);
        var heard = Replica(g, Klaed, g.Events).Events.OfType<PlanetBuilt>().ToList();
        Assert.Equal(built.Select(b => (b.What, b.Ship)).OrderBy(x => x.What), heard.Select(b => (b.What, b.Ship)).OrderBy(x => x.What));
    }

    [Fact]
    public void A_side_out_of_the_game_sees_everything()
    {
        var g = Plain();
        var far = g.Planets.First(p => !VisiblePlanets(g, Klaed).Contains(p.Id));
        AddShipsAt(g, Nairan, far.Id, new ShipCounts { [ShipClass.Scout] = 1 });
        var fleet = FleetsAt(g, far.Id, Nairan)[0];
        Assert.False(Known(g, Klaed, far));
        Assert.False(FleetVisible(g, Klaed, fleet));

        g.Factions[Klaed].Eliminated = true;

        Assert.Equal(g.Planets.Count, VisiblePlanets(g, Klaed).Count);
        Assert.True(Known(g, Klaed, far));
        Assert.True(FleetVisible(g, Klaed, fleet));
        // A replica's host tells it as much.
        Assert.Contains(Replica(g, Klaed, []).Fleets.Values, f => f.Id == fleet.Id);
    }

    [Fact]
    public void Being_out_comes_with_a_flag_a_replica_can_read()
    {
        var g = Plain();
        foreach (var p in g.Planets.Where(p => p.Owner == Klaed)) p.Owner = null;
        foreach (var f in g.Fleets.Values.Where(f => f.Side == Klaed).ToList()) g.Fleets.Remove(f.Id);
        var third = g.Planets.First(p => p.Owner is null);
        third.Owner = Nairan;
        StepGalaxy(g);
        // Alone against one side, it is over at once; a third side would have kept it going.
        Assert.True(g.Factions[Klaed].Eliminated);
        Assert.True(Replica(g, Klaed, []).Factions[Klaed].Eliminated);
    }

    [Fact]
    public void A_rally_point_goes_when_its_planet_falls_and_the_ships_built_then_stay()
    {
        var g = Plain();
        var home = g.Planets.First(p => p.Owner == Klaed);
        var kaldra = g.Planets[Neighbours(g, home.Id)[0]];
        kaldra.Owner = Klaed;
        Assert.True(SetRally(g, Klaed, home.Id, kaldra.Id).Ok);
        kaldra.Owner = Nairan;
        g.Factions[Klaed].Credits = 100000;
        Assert.True(EnqueueBuild(g, Klaed, home.Id, GalaxyBuild.Ship(ShipClass.Scout)).Ok);
        home.Units[0].Remaining = 0.05;
        var before = FleetsAt(g, home.Id, Klaed)[0].Ships.Of(ShipClass.Scout);

        StepGalaxy(g);

        Assert.Null(home.Rally);
        var told = Assert.Single(g.Events.OfType<RallyCancelled>());
        Assert.Equal((home.Id, kaldra.Id, Klaed), (told.PlanetId, told.Target, told.Side));
        // The ship waits at home: no fleet of one flies out to start a battle.
        Assert.Equal(before + Ships.Defs[ShipClass.Scout].Squad, FleetsAt(g, home.Id, Klaed)[0].Ships.Of(ShipClass.Scout));
        Assert.DoesNotContain(g.Fleets.Values, f => f.Side == Klaed && f.To is not null);
        // Told once, and to the side it concerns.
        Assert.Contains(Replica(g, Klaed, g.Events).Events, e => e is RallyCancelled);
        Assert.DoesNotContain(Replica(g, Nairan, g.Events).Events, e => e is RallyCancelled);
        g.Events.Clear();
        StepGalaxy(g);
        Assert.DoesNotContain(g.Events, e => e is RallyCancelled);
    }

    [Fact]
    public void A_rally_point_holds_on_an_ally_until_the_alliance_ends()
    {
        // A third side: two allies alone would have won together.
        var g = CreateGalaxy(Faction.Klaed, [Faction.Nairan, Faction.Nautolan], false, 3, GalaxyGen.GenerateLayout(3, 3, MapSize.Small), errants: false, incidents: false);
        var home = g.Planets.First(p => p.Owner == Klaed);
        var kaldra = g.Planets[Neighbours(g, home.Id)[0]];
        kaldra.Owner = Nairan;
        var pact = new Pact { Kind = DealKind.Alliance, A = Klaed, B = Nairan };
        g.Pacts.Add(pact);
        Assert.True(SetRally(g, Klaed, home.Id, kaldra.Id).Ok);
        RunGalaxy(g, 1);
        Assert.Equal(kaldra.Id, home.Rally);
        g.Pacts.Remove(pact);
        StepGalaxy(g);
        Assert.Null(home.Rally);
        Assert.Contains(g.Events, e => e is RallyCancelled);
    }

    [Fact]
    public void A_neutral_planet_may_be_rallied_to_until_someone_else_takes_it()
    {
        var g = Plain();
        var home = g.Planets.First(p => p.Owner == Klaed);
        var kaldra = g.Planets[Neighbours(g, home.Id)[0]];
        Assert.Null(kaldra.Owner);
        Assert.True(SetRally(g, Klaed, home.Id, kaldra.Id).Ok);
        RunGalaxy(g, 1);
        Assert.Equal(kaldra.Id, home.Rally);
        kaldra.Owner = Nairan;
        StepGalaxy(g);
        Assert.Null(home.Rally);
    }

    [Fact]
    public void A_rally_point_is_refused_on_an_enemy_planet()
    {
        var g = Plain();
        var home = g.Planets.First(p => p.Owner == Klaed);
        var theirs = g.Planets.First(p => p.Owner == Nairan);
        var r = SetRally(g, Klaed, home.Id, theirs.Id);
        Assert.False(r.Ok);
        Assert.Equal("Planète ennemie", r.Reason);
        Assert.Null(home.Rally);
    }

    [Fact]
    public void One_ship_is_the_cheapest_of_the_fleet_and_leaves_the_rest_where_it_was()
    {
        var g = Plain();
        var home = g.Planets.First(p => p.Owner == Klaed);
        var hero = Heroes.Of(Faction.Klaed).First().Id;
        AddShipsAt(g, Klaed, home.Id, new ShipCounts { [ShipClass.Battlecruiser] = 1 }, [hero]);
        var fleet = FleetsAt(g, home.Id, Klaed)[0];
        var (ships, heroes) = OneShip(fleet);
        var cheapest = Ships.All.Where(c => fleet.Ships.Of(c) > 0).MinBy(Ships.UnitValue);
        Assert.Equal(1, ships.Of(cheapest));
        Assert.Equal(1, Battle.CountShips(ships));
        Assert.Empty(heroes);
        var before = Battle.CountShips(fleet.Ships);

        Assert.True(SendFleet(g, fleet.Id, Neighbours(g, home.Id)[0], ships, heroes).Ok);

        Assert.Equal(before - 1, Battle.CountShips(fleet.Ships));
        Assert.Equal([hero], fleet.Heroes);
        Assert.Equal(home.Id, fleet.At);
        var gone = Assert.Single(g.Fleets.Values, f => f.Side == Klaed && f.To is not null);
        Assert.Equal(1, Battle.CountShips(gone.Ships));
    }

    [Fact]
    public void One_ship_from_a_fleet_of_heroes_is_a_hero_and_from_nothing_is_nothing()
    {
        var hero = Heroes.Of(Faction.Klaed).First().Id;
        var (ships, heroes) = OneShip(new Fleet { Side = Klaed, Heroes = [hero] });
        Assert.Equal(0, Battle.CountShips(ships));
        Assert.Equal([hero], heroes);
        var (none, nobody) = OneShip(new Fleet { Side = Klaed });
        Assert.Equal(0, Battle.CountShips(none));
        Assert.Empty(nobody);
    }
}
