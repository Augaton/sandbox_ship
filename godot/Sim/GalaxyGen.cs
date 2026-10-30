// Galaxy layouts: the hand-made classic map, or a random one generated from a seed. Port of
// src/sim/galaxyGen.ts.
using SandboxShip.Sim.Data;

namespace SandboxShip.Sim;

public sealed record GalaxyLayout(IReadOnlyList<PlanetDef> Planets, IReadOnlyList<Lane> Lanes,
    // Index of each side's capital in Planets, the player's first.
    IReadOnlyList<int> Capitals,
    // Extent of the map the planets lie in.
    double W, double H);

public static class GalaxyGen
{
    public static readonly GalaxyLayout Classic = new(GalaxyMap.Planets, GalaxyMap.Lanes, GalaxyMap.Capitals, GalaxyMap.W, GalaxyMap.H);

    static readonly string[] Names =
    [
        "Vesper", "Orin", "Maru", "Kaldra", "Sorel", "Thessa", "Nadir", "Ostra", "Pélion", "Yvra", "Tarsis", "Brume",
        "Ixion", "Calyx", "Aldane", "Bérénice", "Corvée", "Dravon", "Elyne", "Fargo", "Gélise", "Hesper", "Isaure",
        "Joran", "Kestel", "Lunaé", "Mirth", "Nérée", "Opale", "Pyros", "Quarre", "Rhéa", "Solenne", "Tyval", "Ulme",
    ];
    static readonly PlanetSpecial[] SpecialKinds = [PlanetSpecial.Rich, PlanetSpecial.Shipyard, PlanetSpecial.Fortress, PlanetSpecial.Archive];
    const double Margin = 90;
    const double MinGap = 165;

    /// <summary>
    /// Map extent and planet count of each galaxy size, about as dense in all three. Medium is
    /// the web version's galaxy.
    /// </summary>
    static readonly Dictionary<MapSize, (double W, double H, int Min, int Max)> Sizes = new()
    {
        [MapSize.Small] = (1300, 850, 10, 12),
        [MapSize.Medium] = (GalaxyMap.W, GalaxyMap.H, 12, 18),
        [MapSize.Large] = (2200, 1350, 22, 30),
    };

    /// <summary>Deterministic random numbers in [0, 1) (mulberry32).</summary>
    public static Func<double> SeededRandom(int seed)
    {
        var s = seed;
        return () => Rng.Mulberry32(ref s);
    }

    /// <summary>Whether segments ab and cd cross, not counting a shared end.</summary>
    public static bool Crosses(Point a, Point b, Point c, Point d)
    {
        if (a == c || a == d || b == c || b == d) return false;
        static int Orient(Point p, Point q, Point r) => Math.Sign((q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X));
        return Orient(a, b, c) != Orient(a, b, d) && Orient(c, d, a) != Orient(c, d, b);
    }

    static double[] Hops(List<Lane> lanes, int n, int from)
    {
        var dist = Enumerable.Repeat(double.PositiveInfinity, n).ToArray();
        dist[from] = 0;
        var todo = new Queue<int>([from]);
        while (todo.Count > 0)
        {
            var cur = todo.Dequeue();
            foreach (var (a, b) in lanes)
            {
                var next = a == cur ? b : b == cur ? a : -1;
                if (next >= 0 && double.IsPositiveInfinity(dist[next]))
                {
                    dist[next] = dist[cur] + 1;
                    todo.Enqueue(next);
                }
            }
        }
        return dist;
    }

    static double Gap(Point p, Point q) => Worlds.Hypot(p.X - q.X, p.Y - q.Y);

    /// <summary>
    /// A random galaxy: planets spread out (12 to 18 at medium size), lanes between near
    /// neighbours that never cross, everything connected, and the capitals as far apart as the
    /// lanes allow.
    /// </summary>
    public static GalaxyLayout GenerateLayout(int seed, int players = 2, MapSize size = MapSize.Medium)
    {
        var (w, h, min, max) = Sizes[size];
        var rnd = SeededRandom(seed);
        var count = min + (int)Math.Floor(rnd() * (max - min + 1));
        var points = new List<Point>();
        for (var tries = 0; points.Count < count && tries < 5000; tries++)
        {
            var px = Margin + rnd() * (w - 2 * Margin);
            var py = Margin + rnd() * (h - 2 * Margin);
            var p = new Point(px, py);
            if (points.TrueForAll(q => Gap(p, q) >= MinGap)) points.Add(new Point(Js.Round(p.X), Js.Round(p.Y)));
        }
        var names = Names.ToArray();
        for (var i = names.Length - 1; i > 0; i--)
        {
            var j = (int)Math.Floor(rnd() * (i + 1));
            (names[i], names[j]) = (names[j], names[i]);
        }
        var planets = points.Select((p, i) =>
        {
            var income = 2 + Math.Floor(rnd() * 5);
            PlanetSpecial? special = rnd() < 0.3 ? SpecialKinds[(int)Math.Floor(rnd() * SpecialKinds.Length)] : null;
            return new PlanetDef(names[i], p.X, p.Y, income, (int)Math.Min(3, 1 + Math.Floor(income / 2)), special);
        }).ToList();

        // Shortest lanes first, each to one of a planet's nearest neighbours, as long as none cross.
        var lanes = new List<Lane>();
        var degree = new int[points.Count];
        var candidates = new List<(int A, int B, double D)>();
        for (var i = 0; i < points.Count; i++)
        {
            var near = points.Select((q, j) => (J: j, D: Gap(points[i], q))).Where(x => x.J != i).OrderBy(x => x.D).Take(4);
            foreach (var (j, d) in near) candidates.Add((Math.Min(i, j), Math.Max(i, j), d));
        }
        candidates = candidates.OrderBy(c => c.D).ToList();
        bool Has(int a, int b) => lanes.Exists(l => (l.A == a && l.B == b) || (l.A == b && l.B == a));
        bool Free(int a, int b) => lanes.TrueForAll(l => !Crosses(points[a], points[b], points[l.A], points[l.B]));
        foreach (var (a, b, _) in candidates)
        {
            if (Has(a, b) || degree[a] >= 4 || degree[b] >= 4 || !Free(a, b)) continue;
            lanes.Add(new Lane(a, b));
            degree[a]++;
            degree[b]++;
        }
        // Join separate groups with their shortest crossing-free link (any link as a last resort).
        for (;;)
        {
            var reach = Hops(lanes, points.Count, 0);
            var out_ = Enumerable.Range(0, points.Count).Where(i => double.IsPositiveInfinity(reach[i])).ToList();
            if (out_.Count == 0) break;
            var links = new List<(int A, int B, double D)>();
            for (var a = 0; a < points.Count; a++)
            {
                if (double.IsPositiveInfinity(reach[a])) continue;
                foreach (var b in out_) links.Add((a, b, Gap(points[a], points[b])));
            }
            links = links.OrderBy(l => l.D).ToList();
            var link = links.FirstOrDefault(l => Free(l.A, l.B), links[0]);
            lanes.Add(new Lane(link.A, link.B));
        }

        // Capitals: the pair furthest apart in lanes (then in distance), then each further one as
        // far as possible from those already picked.
        var allHops = Enumerable.Range(0, points.Count).Select(i => Hops(lanes, points.Count, i)).ToList();
        double Apart(int a, int b) => allHops[a][b] * 10000 + Gap(points[a], points[b]);
        var capitals = new List<int> { 0, 1 };
        var best = -1.0;
        for (var a = 0; a < points.Count; a++)
        {
            for (var b = 0; b < points.Count; b++)
            {
                if (b != a && Apart(a, b) > best)
                {
                    best = Apart(a, b);
                    capitals = points[a].X <= points[b].X ? [a, b] : [b, a];
                }
            }
        }
        while (capitals.Count < players)
        {
            var next = Enumerable.Range(0, points.Count).Where(i => !capitals.Contains(i))
                .OrderByDescending(i => capitals.Min(c => Apart(i, c))).First();
            capitals.Add(next);
        }
        foreach (var c in capitals) planets[c] = planets[c] with { Income = 6, MineSlots = 3, Special = null };
        return new GalaxyLayout(planets, lanes, capitals, w, h);
    }
}
