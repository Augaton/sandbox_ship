// Orders to fleets on the move and rally points, rules of this version only: a fleet on its way
// can be sent elsewhere (turning back mid-lane when shorter) or stopped at its next planet, and
// the ships a planet builds can leave on their own for its rally point.
using System.Text.Json.Nodes;
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.Galaxies;

namespace SandboxShip.Sim.Tests;

public class FleetOrdersTests
{
    static readonly int West = GalaxyMap.Capitals[0];
    static readonly int East = GalaxyMap.Capitals[1];

    /// <summary>The classic galaxy, Kla'ed against a Nairan side that does nothing, without the Errants.</summary>
    static Galaxy Quiet() => CreateGalaxy(Faction.Klaed, [Faction.Nairan], false, errants: false);

    static Fleet Home(Galaxy g) => FleetsAt(g, West, Faction.Klaed)[0];

    /// <summary>Runs the galaxy until the fleet is `progress` of the way along its first lane.</summary>
    static void RunTo(Galaxy g, Fleet f, double progress)
    {
        for (var t = 0.0; t < 120 && f.Progress < progress; t += Step) StepGalaxy(g);
        Assert.Null(f.At);
        Assert.InRange(f.Progress, progress, 0.99);
    }

    /// <summary>Runs the galaxy until the fleet docks somewhere; returns how long that took.</summary>
    static double RunToDock(Galaxy g, Fleet f)
    {
        var t = 0.0;
        for (; t < 300 && f.At is null; t += Step) StepGalaxy(g);
        Assert.NotNull(f.At);
        return t;
    }

    [Fact]
    public void A_fleet_on_its_way_goes_on_through_its_next_planet_when_that_is_shorter()
    {
        var g = Quiet();
        var route = FindPath(g, West, East)!;
        var f = Home(g);
        Assert.True(SendFleet(g, f.Id, route[1]).Ok);
        RunTo(g, f, 0.6);
        var progress = f.Progress;
        Assert.True(RedirectFleet(g, f.Id, East).Ok);
        Assert.Equal(West, f.From);
        Assert.Equal(route[0], f.To);
        Assert.Equal(progress, f.Progress);
        Assert.Equal(route.Skip(1), f.Path);
    }

    [Fact]
    public void A_fleet_turns_back_mid_lane_when_going_back_is_shorter()
    {
        var g = Quiet();
        var route = FindPath(g, West, East)!;
        var f = Home(g);
        Assert.True(SendFleet(g, f.Id, route[1]).Ok);
        RunTo(g, f, 0.3);
        var progress = f.Progress;
        Assert.True(RedirectFleet(g, f.Id, West).Ok);
        // The same spot on the lane, seen from the other end.
        Assert.Equal(route[0], f.From);
        Assert.Equal(West, f.To);
        Assert.Equal(1 - progress, f.Progress, 12);
        Assert.Empty(f.Path);
        RunToDock(g, f);
        Assert.Equal(West, f.At);
    }

    [Fact]
    public void A_redirected_fleet_takes_whichever_way_is_shorter_to_a_planet_behind_it()
    {
        var g = Quiet();
        var first = Neighbours(g, West)[0];
        var other = Neighbours(g, West).First(n => n != first);
        var f = Home(g);
        Assert.True(SendFleet(g, f.Id, first).Ok);
        RunTo(g, f, 0.2);
        var p = f.Progress;
        var lane = Worlds.Dist(g.Planets[West], g.Planets[first]);
        double Length(int from, List<int> path) => path.Aggregate((Sum: 0.0, At: from), (a, id) => (a.Sum + Worlds.Dist(g.Planets[a.At], g.Planets[id]), id)).Sum;
        var back = p * lane + Length(West, FindPath(g, West, other)!);
        var ahead = (1 - p) * lane + Length(first, FindPath(g, first, other)!);
        Assert.Equal(back < ahead, PlanRedirect(g, f, other)!.Value.Back);
        Assert.True(RedirectFleet(g, f.Id, other).Ok);
        Assert.Equal(back < ahead ? West : first, f.To);
        RunToDock(g, f);
        Assert.Equal(other, f.At);
    }

    [Fact]
    public void A_docked_fleet_is_redirected_as_it_is_sent()
    {
        var g = Quiet();
        var route = FindPath(g, West, East)!;
        var f = Home(g);
        Assert.True(RedirectFleet(g, f.Id, route[1]).Ok);
        Assert.Null(f.At);
        Assert.Equal(West, f.From);
        Assert.Equal(route[0], f.To);
        Assert.Equal([route[1]], f.Path);
        Assert.Equal(0, f.Progress);
    }

    [Fact]
    public void Fleets_in_a_battle_take_no_order()
    {
        var g = Quiet();
        var f = Home(g);
        Assert.True(SendFleet(g, f.Id, FindPath(g, West, East)![1]).Ok);
        RunTo(g, f, 0.3);
        f.BattleId = 99;
        Assert.False(RedirectFleet(g, f.Id, West).Ok);
        Assert.False(StopFleet(g, f.Id).Ok);
        Assert.False(RedirectFleet(g, 12345, West).Ok);
    }

    [Fact]
    public void A_stopped_fleet_halts_at_its_next_planet()
    {
        var g = Quiet();
        var route = FindPath(g, West, East)!;
        var f = Home(g);
        Assert.True(SendFleet(g, f.Id, East).Ok);
        Assert.NotEmpty(f.Path);
        RunTo(g, f, 0.5);
        Assert.True(StopFleet(g, f.Id).Ok);
        Assert.Empty(f.Path);
        RunToDock(g, f);
        Assert.Equal(route[0], f.At);
        // Docked, it has no way left to cut short.
        Assert.False(StopFleet(g, f.Id).Ok);
    }

    [Fact]
    public void The_travel_time_is_the_time_the_fleet_takes()
    {
        var g = Quiet();
        var route = FindPath(g, West, East)!;
        var f = Home(g);
        Assert.True(SendFleet(g, f.Id, route[2]).Ok);
        var eta = TravelTime(g, f);
        Assert.InRange(RunToDock(g, f), eta - 2 * Step, eta + 2 * Step);
        Assert.Equal(0, TravelTime(g, f));
    }

    [Fact]
    public void New_ships_leave_for_the_rally_point_and_the_fleet_at_home_stays()
    {
        var g = Quiet();
        var next = Neighbours(g, West)[0];
        g.Factions[Faction.Klaed].Credits = 10000;
        var home = Home(g);
        var before = home.Ships.Clone();
        Assert.True(SetRally(g, Faction.Klaed, West, next).Ok);
        Assert.Equal(next, g.Planets[West].Rally);
        Assert.True(EnqueueBuild(g, Faction.Klaed, West, GalaxyBuild.Ship(ShipClass.Frigate)).Ok);
        for (var t = 0.0; t < 120 && g.Planets[West].Units.Count > 0; t += Step) StepGalaxy(g);
        Assert.Equal(before, home.Ships);
        Assert.Equal(West, home.At);
        var sent = Assert.Single(g.Fleets.Values, f => f.Side == Faction.Klaed && f.Id != home.Id);
        Assert.Equal(1, Battle.CountShips(sent.Ships));
        Assert.Equal(1, sent.Ships.Of(ShipClass.Frigate));
        Assert.Equal(next, sent.To);
        RunToDock(g, sent);
        Assert.Equal(next, sent.At);
    }

    [Fact]
    public void Without_a_rally_point_new_ships_join_the_fleet_at_home()
    {
        var g = Quiet();
        g.Factions[Faction.Klaed].Credits = 10000;
        var home = Home(g);
        var frigates = home.Ships.Of(ShipClass.Frigate);
        Assert.True(EnqueueBuild(g, Faction.Klaed, West, GalaxyBuild.Ship(ShipClass.Frigate)).Ok);
        for (var t = 0.0; t < 120 && g.Planets[West].Units.Count > 0; t += Step) StepGalaxy(g);
        Assert.Equal(frigates + 1, home.Ships.Of(ShipClass.Frigate));
        Assert.Single(g.Fleets.Values, f => f.Side == Faction.Klaed);
    }

    [Fact]
    public void Rally_points_are_set_on_own_planets_and_cleared_on_the_planet_itself_or_by_a_capture()
    {
        var g = Quiet();
        var next = Neighbours(g, West)[0];
        Assert.False(SetRally(g, Faction.Klaed, East, West).Ok);
        Assert.False(SetRally(g, Faction.Klaed, next, West).Ok);
        Assert.True(SetRally(g, Faction.Klaed, West, next).Ok);
        Assert.True(SetRally(g, Faction.Klaed, West, West).Ok);
        Assert.Null(g.Planets[West].Rally);
        Assert.True(SetRally(g, Faction.Klaed, West, null).Ok);
        Assert.Null(g.Planets[West].Rally);

        // A planet taken over loses its rally point with the rest of its orders.
        var p = g.Planets[next];
        p.Owner = Faction.Klaed;
        Assert.True(SetRally(g, Faction.Klaed, next, West).Ok);
        AddShipsAt(g, Faction.Nairan, next, new ShipCounts { [ShipClass.Scout] = 1 });
        for (var t = 0.0; t < 120 && p.Owner == Faction.Klaed; t += Step) StepGalaxy(g);
        Assert.Equal(Faction.Nairan, p.Owner);
        Assert.Null(p.Rally);
    }

    [Fact]
    public void Rally_points_are_saved_and_older_saves_without_them_still_load()
    {
        var g = Quiet();
        var next = Neighbours(g, West)[0];
        Assert.True(SetRally(g, Faction.Klaed, West, next).Ok);
        var text = GalaxySave.Serialize(g, 0);
        var back = GalaxySave.Deserialize(text)!;
        Assert.Equal(next, back.Planets[West].Rally);
        Assert.Equal(text, GalaxySave.Serialize(back, 0));

        var old = JsonNode.Parse(text)!;
        foreach (var planet in old["planets"]!.AsArray()) planet!.AsObject().Remove("rally");
        var loaded = GalaxySave.Deserialize(old.ToJsonString());
        Assert.NotNull(loaded);
        Assert.All(loaded.Planets, p => Assert.Null(p.Rally));
        Assert.Equal(g.Planets.Count, loaded.Planets.Count);
    }
}
