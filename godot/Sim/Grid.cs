// Uniform grid over the battle map: neighbour queries without testing every pair. Port of
// src/sim/grid.ts.
namespace SandboxShip.Sim;

public sealed class SpatialGrid
{
    const double Cell = 200;
    readonly Dictionary<int, List<Entity>> cells = [];

    /// <summary>Largest entity radius inserted: queries widen by it to catch big entities near the edge.</summary>
    public double MaxRadius;

    /// <summary>Cell coordinates fit in 10 bits each: maps up to 200 km wide.</summary>
    static int Key(int cx, int cy) => (Math.Max(0, cx) << 10) | Math.Max(0, cy);

    public void Rebuild(EntityStore entities)
    {
        Clear();
        foreach (var e in entities) Add(e);
    }

    /// <summary>Empties the grid; its cells keep their lists for the next fill.</summary>
    public void Clear()
    {
        foreach (var list in cells.Values) list.Clear();
        MaxRadius = 0;
    }

    /// <summary>Adds an entity: within a cell, queries meet entities in the order they were added.</summary>
    public void Add(Entity e)
    {
        var k = Key((int)Math.Floor(e.X / Cell), (int)Math.Floor(e.Y / Cell));
        if (!cells.TryGetValue(k, out var list)) cells[k] = list = [];
        list.Add(e);
        if (e.Radius > MaxRadius) MaxRadius = e.Radius;
    }

    /// <summary>
    /// Every entity whose center may lie within `r` of (x, y), plus the largest radius, cell by
    /// cell (column after column) and in insertion order within a cell. A struct walked by
    /// foreach: no closure and no garbage, however many queries a step makes.
    /// </summary>
    public Query Near(double x, double y, double r) => new(this, x, y, r);

    public struct Query
    {
        readonly Dictionary<int, List<Entity>> cells;
        readonly int x0, x1, y0, y1;
        int cx, cy, index;
        List<Entity>? list;

        internal Query(SpatialGrid grid, double x, double y, double r)
        {
            cells = grid.cells;
            var reach = r + grid.MaxRadius;
            x0 = (int)Math.Floor((x - reach) / Cell);
            x1 = (int)Math.Floor((x + reach) / Cell);
            y0 = (int)Math.Floor((y - reach) / Cell);
            y1 = (int)Math.Floor((y + reach) / Cell);
            cx = x0;
            cy = y0 - 1;
            index = 0;
            list = null;
            Current = null!;
        }

        public Entity Current { get; private set; }

        public readonly Query GetEnumerator() => this;

        public bool MoveNext()
        {
            while (true)
            {
                if (list is not null && index < list.Count)
                {
                    Current = list[index++];
                    return true;
                }
                if (!NextCell()) return false;
            }
        }

        bool NextCell()
        {
            list = null;
            while (true)
            {
                if (++cy > y1)
                {
                    cy = y0;
                    cx++;
                }
                if (cx > x1) return false;
                if (cx < 0 || cy < 0) continue;
                if (cells.TryGetValue(Key(cx, cy), out var l))
                {
                    list = l;
                    index = 0;
                    return true;
                }
            }
        }
    }
}
