// Uniform grid over the battle map: neighbour queries without testing every pair. Port of
// src/sim/grid.ts.
namespace SandboxShip.Sim;

public sealed class SpatialGrid
{
    const double Cell = 200;
    /// <summary>
    /// Cells column after column, `rows` to a column: a flat array, which the many queries of a
    /// step walk far faster than they would ask a dictionary. It grows to the farthest cell
    /// filled, once per map. Places left of or above the map fall in its first column or row.
    /// </summary>
    List<Entity>?[] cells = [];
    int columns, rows;

    /// <summary>Largest entity radius inserted: queries widen by it to catch big entities near the edge.</summary>
    public double MaxRadius;

    public void Rebuild(EntityStore entities)
    {
        Clear();
        foreach (var e in entities) Add(e);
    }

    /// <summary>Empties the grid; its cells keep their lists for the next fill.</summary>
    public void Clear()
    {
        foreach (var list in cells) list?.Clear();
        MaxRadius = 0;
    }

    /// <summary>Adds an entity: within a cell, queries meet entities in the order they were added.</summary>
    public void Add(Entity e)
    {
        var cx = Math.Max(0, (int)Math.Floor(e.X / Cell));
        var cy = Math.Max(0, (int)Math.Floor(e.Y / Cell));
        if (cx >= columns || cy >= rows) Grow(cx, cy);
        var list = cells[cx * rows + cy] ??= [];
        list.Add(e);
        if (e.Radius > MaxRadius) MaxRadius = e.Radius;
    }

    /// <summary>Room up to cell (cx, cy) and a little beyond, the lists kept in their cells.</summary>
    void Grow(int cx, int cy)
    {
        var (c, r) = (Math.Max(columns, cx + 2), Math.Max(rows, cy + 2));
        var grown = new List<Entity>?[c * r];
        for (var x = 0; x < columns; x++)
        {
            for (var y = 0; y < rows; y++) grown[x * r + y] = cells[x * rows + y];
        }
        (cells, columns, rows) = (grown, c, r);
    }

    /// <summary>
    /// Every entity whose center may lie within `r` of (x, y), plus the largest radius, cell by
    /// cell (column after column) and in insertion order within a cell. A struct walked by
    /// foreach: no closure and no garbage, however many queries a step makes.
    /// </summary>
    public Query Near(double x, double y, double r) => new(this, x, y, r);

    public struct Query
    {
        readonly List<Entity>?[] cells;
        readonly int columns, rows;
        readonly int x0, x1, y0, y1;
        int cx, cy, index;
        List<Entity>? list;

        internal Query(SpatialGrid grid, double x, double y, double r)
        {
            (cells, columns, rows) = (grid.cells, grid.columns, grid.rows);
            var reach = r + grid.MaxRadius;
            x0 = (int)Math.Floor((x - reach) / Cell);
            x1 = Math.Min(columns - 1, (int)Math.Floor((x + reach) / Cell));
            y0 = (int)Math.Floor((y - reach) / Cell);
            y1 = Math.Min(rows - 1, (int)Math.Floor((y + reach) / Cell));
            cx = Math.Max(0, x0);
            cy = Math.Max(0, y0) - 1;
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
            while (true)
            {
                if (++cy > y1)
                {
                    cy = Math.Max(0, y0);
                    cx++;
                }
                // Past the last column, or no row of the map in reach.
                if (cx > x1 || cy > y1) return false;
                list = cells[cx * rows + cy];
                if (list is not null)
                {
                    index = 0;
                    return true;
                }
            }
        }
    }
}
