// The star lance in the campaign, a rule of this version only: a planet building that strikes
// an enemy planet up to three lanes away every five minutes.
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.Galaxies;

namespace SandboxShip.Sim.Tests;

public class GalaxyLanceTests
{
    static readonly GalaxyBuild Lance = GalaxyBuild.Of(GalaxyBuildKind.StarLance);

    /// <summary>The player's capital with a level 4 station and the credits for a lance; no AI.</summary>
    static (Galaxy G, Planet Home) Rich()
    {
        var g = CreateGalaxy(Faction.Klaed, [Faction.Nairan], false, errants: false);
        var home = g.Planets.First(p => p.Owner == Faction.Klaed);
        home.Station = Station.Levels.Count;
        g.Factions[Faction.Klaed].Credits = 100000;
        return (g, home);
    }

    /// <summary>An enemy planet `lanes` away from the capital, given to the Nairan with defenses and a fleet.</summary>
    static Planet Enemy(Galaxy g, Planet home, int lanes)
    {
        var p = g.Planets.First(p => p.Owner is null && LanesBetween(g, home.Id, p.Id) == lanes);
        p.Owner = Faction.Nairan;
        p.Station = 3;
        p.Platforms = 2;
        p.IonCannon = 1;
        p.Mines = 2;
        AddShipsAt(g, Faction.Nairan, p.Id, new ShipCounts { [ShipClass.Frigate] = 6, [ShipClass.Battlecruiser] = 3 });
        return p;
    }

    [Fact]
    public void It_needs_a_level_4_station_comes_one_per_faction_and_charges_once_built()
    {
        var (g, home) = Rich();
        home.Station = 3;
        Assert.Equal("Station niveau 4 requise", CanBuild(g, Faction.Klaed, home.Id, Lance).Reason);
        home.Station = 4;
        Assert.True(EnqueueBuild(g, Faction.Klaed, home.Id, Lance).Ok);
        Assert.Equal(100000 - Buildables.Defs[BuildableType.StarLance].Cost, g.Factions[Faction.Klaed].Credits);
        var other = g.Planets.First(p => p.Owner is null);
        other.Owner = Faction.Klaed;
        other.Station = 4;
        Assert.Equal("Une seule Lance stellaire", CanBuild(g, Faction.Klaed, other.Id, Lance).Reason);
        RunGalaxy(g, Buildables.Defs[BuildableType.StarLance].BuildTime + 1);
        Assert.Equal(1, home.StarLance);
        Assert.InRange(home.LanceCharge, LanceRecharge - 2, LanceRecharge);
    }

    [Fact]
    public void A_strike_breaks_what_is_in_orbit_and_halves_the_fleets_there()
    {
        var (g, home) = Rich();
        home.StarLance = 1;
        var target = Enemy(g, home, 2);
        home.LanceCharge = 10;
        Assert.StartsWith("En charge", CanFireLance(g, Faction.Klaed, home.Id, target.Id).Reason);
        home.LanceCharge = 0;
        Assert.Equal("Choisis une planète ennemie", CanFireLance(g, Faction.Klaed, home.Id, home.Id).Reason);
        Assert.True(FireLance(g, Faction.Klaed, home.Id, target.Id).Ok);
        Assert.Equal((0, 0, 2, 1), (target.Platforms, target.IonCannon, target.Station, target.Mines));
        var fleet = FleetsAt(g, target.Id, Faction.Nairan)[0];
        Assert.Equal(3, fleet.Ships.Of(ShipClass.Frigate));
        Assert.Equal(1, fleet.Ships.Of(ShipClass.Battlecruiser));
        Assert.Equal(LanceRecharge, home.LanceCharge);
        Assert.Contains(g.Events, e => e is LanceFired f && f.Target == target.Id);
    }

    [Fact]
    public void It_reaches_three_lanes_and_spares_a_faction_at_truce()
    {
        var (g, home) = Rich();
        home.StarLance = 1;
        var far = g.Planets.FirstOrDefault(p => p.Owner is null && LanesBetween(g, home.Id, p.Id) == LanceReach + 1);
        if (far is not null)
        {
            far.Owner = Faction.Nairan;
            Assert.StartsWith("Hors de portée", CanFireLance(g, Faction.Klaed, home.Id, far.Id).Reason);
        }
        var near = Enemy(g, home, 1);
        g.Truces.Add(new Truce { A = Faction.Klaed, B = Faction.Nairan, Remaining = 600 });
        Assert.Equal("Trêve en cours", CanFireLance(g, Faction.Klaed, home.Id, near.Id).Reason);
    }

    [Fact]
    public void An_ai_builds_one_once_rich_and_fires_it_as_soon_as_it_is_charged()
    {
        var g = CreateGalaxy(Faction.Klaed, [Faction.Nairan], true, errants: false);
        g.Player = Faction.Nautolan;
        var theirs = g.Planets.First(p => p.Owner == Faction.Nairan);
        theirs.Station = 4;
        g.Factions[Faction.Nairan].Credits = 200000;
        var target = g.Planets.Where(p => p.Owner is null).OrderBy(p => LanesBetween(g, theirs.Id, p.Id)).First();
        target.Owner = Faction.Klaed;
        target.Station = 2;
        RunGalaxy(g, 1);
        Assert.Contains(theirs.Buildings, q => q.Kind == GalaxyBuildKind.StarLance);
        theirs.Buildings.Clear();
        theirs.StarLance = 1;
        theirs.LanceCharge = 0;
        var fired = new List<LanceFired>();
        for (var t = 0.0; t < 2 && fired.Count == 0; t += Step)
        {
            StepGalaxy(g);
            fired.AddRange(g.Events.OfType<LanceFired>());
        }
        var shot = Assert.Single(fired);
        Assert.Equal(Faction.Nairan, shot.Side);
        Assert.Equal(Faction.Klaed, g.Planets[shot.Target].Owner);
        Assert.InRange(LanesBetween(g, theirs.Id, shot.Target), 1, LanceReach);
    }

    [Fact]
    public void Taking_the_planet_takes_the_lance_down()
    {
        var (g, home) = Rich();
        var p = g.Planets.First(x => x.Owner is null);
        p.Owner = Faction.Nairan;
        p.StarLance = 1;
        p.LanceCharge = 40;
        AddShipsAt(g, Faction.Klaed, p.Id, new ShipCounts { [ShipClass.Scout] = 3 });
        RunGalaxy(g, GalaxyRules.CaptureTime + 1);
        Assert.Equal(Faction.Klaed, p.Owner);
        Assert.Equal(0, p.StarLance);
    }
}
