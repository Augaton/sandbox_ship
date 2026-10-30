// The spatial grid (not in the web version as an array): a query meets exactly the entities of
// the cells in reach, column after column and in insertion order within a cell, as the web
// version's grid does; places off the map fall in its first column or row.
using Xunit;

namespace SandboxShip.Sim.Tests;

public class GridTests
{
    /// <summary>What a query must meet, the plain way: each entity's cell, those in reach, in that order.</summary>
    static List<Entity> Expected(List<Entity> all, double x, double y, double r)
    {
        const double Cell = 200;
        var reach = r + all.Max(e => e.Radius);
        var (x0, x1) = ((int)Math.Floor((x - reach) / Cell), (int)Math.Floor((x + reach) / Cell));
        var (y0, y1) = ((int)Math.Floor((y - reach) / Cell), (int)Math.Floor((y + reach) / Cell));
        (int, int) CellOf(Entity e) => (Math.Max(0, (int)Math.Floor(e.X / Cell)), Math.Max(0, (int)Math.Floor(e.Y / Cell)));
        return [.. all.Select((e, i) => (e, i, c: CellOf(e)))
            .Where(p => p.c.Item1 >= x0 && p.c.Item1 <= x1 && p.c.Item2 >= y0 && p.c.Item2 <= y1)
            .OrderBy(p => p.c.Item1).ThenBy(p => p.c.Item2).ThenBy(p => p.i).Select(p => p.e)];
    }

    [Fact]
    public void A_query_meets_the_entities_in_reach_in_the_grids_order()
    {
        var rng = new Random(5);
        var all = new List<Entity>();
        for (var i = 0; i < 600; i++)
        {
            // Mostly on a large map, some off its edges.
            all.Add(new Entity { Id = i + 1, X = rng.NextDouble() * 8600 - 300, Y = rng.NextDouble() * 6000 - 300, Radius = 5 + rng.NextDouble() * 60 });
        }
        var grid = new SpatialGrid();
        foreach (var e in all) grid.Add(e);
        for (var q = 0; q < 300; q++)
        {
            var (x, y, r) = (rng.NextDouble() * 9000 - 500, rng.NextDouble() * 6400 - 500, rng.NextDouble() * 900);
            var met = new List<Entity>();
            foreach (var e in grid.Near(x, y, r)) met.Add(e);
            Assert.Equal(Expected(all, x, y, r), met);
        }
        // Refilled, it holds the new places only; empty, it meets nothing.
        foreach (var e in all) e.X = 8000 - e.X;
        var refill = new EntityStore();
        foreach (var e in all) refill.Add(e);
        grid.Rebuild(refill);
        var near = new List<Entity>();
        foreach (var e in grid.Near(4000, 2700, 500)) near.Add(e);
        Assert.Equal(Expected(all, 4000, 2700, 500), near);
        var none = new List<Entity>();
        foreach (var e in new SpatialGrid().Near(100, 100, 1000)) none.Add(e);
        Assert.Empty(none);
    }
}
