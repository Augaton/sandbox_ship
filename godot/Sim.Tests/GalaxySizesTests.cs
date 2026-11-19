// Galaxy sizes (not in the web version): the medium galaxy is the web one, the small and large
// ones about as dense. Also the AI on maps where it once stood still. A class apart from
// GalaxyTests, so that its long campaigns run alongside them.
using System.Text.Json.Nodes;
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.Galaxies;
using static SandboxShip.Sim.Tests.GalaxyTests;

namespace SandboxShip.Sim.Tests;

public class GalaxySizesTests
{
    /// <summary>A campaign the AI plays on every side, watched from a faction that is not on the map.</summary>
    static Galaxy AiOnly(int seed, GalaxyLayout layout)
    {
        Faction[] enemies = layout.Capitals.Count == 2 ? [Faction.Nairan] : [Faction.Nairan, Faction.Nautolan];
        var g = CreateGalaxy(Faction.Klaed, enemies, true, seed, layout);
        g.Player = (Faction)99;
        g.Factions[Faction.Klaed].AI = new GalaxyAIState { Difficulty = Difficulty.Normal };
        return g;
    }

    [Fact]
    public void The_medium_galaxy_is_the_default_one()
    {
        // CrossTests compare the default galaxy with the web version's.
        foreach (var (seed, players) in new[] { (1, 2), (21, 3) })
        {
            var web = GalaxyGen.GenerateLayout(seed, players);
            var medium = GalaxyGen.GenerateLayout(seed, players, MapSize.Medium);
            Assert.Equal(web.Planets, medium.Planets);
            Assert.Equal(web.Lanes, medium.Lanes);
            Assert.Equal(web.Capitals, medium.Capitals);
            Assert.Equal((GalaxyMap.W, GalaxyMap.H), (medium.W, medium.H));
        }
        Assert.Equal((GalaxyMap.W, GalaxyMap.H), (GalaxyGen.Classic.W, GalaxyGen.Classic.H));
    }

    [Theory]
    [InlineData(MapSize.Small, 10, 12, 1300, 850)]
    [InlineData(MapSize.Large, 22, 30, 2200, 1350)]
    public void Small_and_large_galaxies_are_connected_with_capitals_far_apart(MapSize size, int min, int max, double w, double h)
    {
        foreach (var seed in new[] { 1, 2, 3, 10, 21, 55, 99, 12345 })
        {
            foreach (var players in new[] { 2, 3 })
            {
                var layout = GalaxyGen.GenerateLayout(seed, players, size);
                var planets = layout.Planets;
                Assert.InRange(planets.Count, min, max);
                Assert.Equal((w, h), (layout.W, layout.H));
                Assert.All(planets, p => Assert.True(p.X > 0 && p.X < w && p.Y > 0 && p.Y < h));
                Assert.Equal(planets.Count, planets.Select(p => p.Name).Distinct().Count());
                var g = AiOnly(seed, layout);
                Assert.Equal(planets.Count, Reachable(g));
                Assert.Equal(players, layout.Capitals.Distinct().Count());
                // As at medium size: two rivals three lanes apart at least, three rivals two.
                foreach (var a in layout.Capitals)
                {
                    foreach (var b in layout.Capitals.Where(b => b != a)) Expect.GreaterOrEqual(FindPath(g, a, b)!.Count, players == 2 ? 3 : 2);
                }
            }
        }
    }

    [Fact]
    public void The_immense_galaxy_has_regions_joined_by_choke_points()
    {
        foreach (var seed in new[] { 1, 2, 3, 10, 21, 55, 99, 12345 })
        {
            foreach (var players in new[] { 2, 3, 6 })
            {
                var layout = GalaxyGen.GenerateLayout(seed, players, MapSize.Immense);
                var planets = layout.Planets;
                Assert.InRange(planets.Count, 54, 64);
                Assert.Equal((3400.0, 2100.0), (layout.W, layout.H));
                Assert.All(planets, p => Assert.True(p.X > 0 && p.X < 3400 && p.Y > 0 && p.Y < 2100));
                Assert.Equal(planets.Count, planets.Select(p => p.Name).Distinct().Count());
                Assert.All(layout.Lanes, l => Assert.All(layout.Lanes, m => Assert.False(GalaxyGen.Crosses(
                    new Point(planets[l.A].X, planets[l.A].Y), new Point(planets[l.B].X, planets[l.B].Y),
                    new Point(planets[m.A].X, planets[m.A].Y), new Point(planets[m.B].X, planets[m.B].Y)))));
                var slots = Enumerable.Range(0, players).Select(i => new Slot(Lobby.SideOf(i), (Faction)(i % 3))).ToList();
                var g = CreateGalaxy(slots, seed, layout);
                Assert.Equal(planets.Count, Reachable(g));
                Assert.Equal(players, layout.Capitals.Distinct().Count());
                // Far apart: two rivals six lanes apart at least, six rivals three.
                foreach (var a in layout.Capitals)
                {
                    foreach (var b in layout.Capitals.Where(b => b != a)) Expect.GreaterOrEqual(FindPath(g, a, b)!.Count, players == 2 ? 6 : 3);
                }
                Assert.Equal(2, g.Planets.Count(p => p.Owner == Faction.Errant));
            }
        }
    }

    [Fact]
    public void Saves_keep_the_galaxy_size_and_older_saves_load_as_medium()
    {
        var large = CreateGalaxy(Faction.Klaed, [Faction.Nairan], true, 7, GalaxyGen.GenerateLayout(7, 2, MapSize.Large));
        var back = GalaxySave.Deserialize(GalaxySave.Serialize(large))!;
        Assert.Equal((2200.0, 1350.0), (back.W, back.H));
        var old = JsonNode.Parse(GalaxySave.Serialize(CreateGalaxy(Faction.Klaed, [Faction.Nairan])))!.AsObject();
        Assert.True(old.Remove("w") && old.Remove("h"));
        var loaded = GalaxySave.Deserialize(old.ToJsonString())!;
        Assert.Equal((GalaxyMap.W, GalaxyMap.H), (loaded.W, loaded.H));
    }

    [Theory]
    [InlineData(MapSize.Small, 2, 30)]
    [InlineData(MapSize.Small, 3, 10)]
    [InlineData(MapSize.Large, 2, 30)]
    [InlineData(MapSize.Large, 3, 30)]
    public void AIs_alone_play_a_small_or_large_galaxy_to_the_end(MapSize size, int players, int seed)
    {
        var g = AiOnly(seed, GalaxyGen.GenerateLayout(seed, players, size));
        // An hour at most, two in a large galaxy: the AI waits for its edge and regroups between offensives
        // (GalaxyAi.Regroup), and under the fleet cap a fortified planet holds out longer. Large, two AIs, seeds 30
        // to 34: 99, 54, 28, 50 and 42 min under the cap; 37, 27, 139, past 150 and 73 min without it.
        var hours = size == MapSize.Large ? 2 : 1;
        for (var minute = 0; minute < 60 * hours && g.Winner is null; minute++) RunGalaxy(g, 60);
        Assert.NotNull(g.Winner);
    }

    [Fact]
    public void AIs_walled_off_by_the_Errants_break_through_their_planet()
    {
        // In this galaxy, the Errants hold the only way between the two capitals.
        var layout = GalaxyGen.GenerateLayout(10, 2, MapSize.Small);
        var g = AiOnly(10, layout);
        var site = ErrantPlanet(g);
        Assert.DoesNotContain(layout.Capitals[1], Hops(g, [layout.Capitals[0]], p => p.Id != site.Id).Keys);
        Assert.DoesNotContain(site.Id, Neighbours(g, layout.Capitals[0]));
        // Far stronger than their guard, the fleet leaves home to go through them.
        AddShipsAt(g, Faction.Klaed, layout.Capitals[0], new ShipCounts { [ShipClass.Leviathan] = 6 });
        RunGalaxy(g, 300);
        Assert.Contains(g.Events, e => e is BattleEnded b && b.Attacker == Faction.Klaed && b.Defender == Faction.Errant);
    }

    [Fact]
    public void AIs_take_the_free_planets_behind_their_capital()
    {
        // In this galaxy, a capital's first free neighbour lies away from the front: the scout
        // sent there used to be called back to the front before it could take the planet.
        var g = AiOnly(30, GalaxyGen.GenerateLayout(30));
        RunGalaxy(g, 120);
        foreach (var f in g.Factions.Keys) Expect.Greater(g.Planets.Count(p => p.Owner == f), 1);
    }
}
