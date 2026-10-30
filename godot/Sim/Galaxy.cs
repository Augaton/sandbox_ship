// Galaxy map simulation: planets, fleets travelling along lanes, production, and the battles
// that start when two sides at war meet over a planet. Port of src/sim/galaxy.ts.
using System.Text.Json.Serialization;
using SandboxShip.Sim.Data;

namespace SandboxShip.Sim;

public enum GalaxyBuildKind { Ship, Hero, Station, Mine, Platform, PlatformTier, Lab, RepairDock, IonCannon, StarLance }

/// <summary>Something a planet can build: a ship class, a hero, or a building.</summary>
public readonly record struct GalaxyBuild(GalaxyBuildKind Kind, ShipClass Cls = default, HeroId Hero = default)
{
    public static GalaxyBuild Ship(ShipClass cls) => new(GalaxyBuildKind.Ship, cls);
    public static GalaxyBuild HeroOf(HeroId hero) => new(GalaxyBuildKind.Hero, Hero: hero);
    public static GalaxyBuild Of(GalaxyBuildKind kind) => new(kind);
}

public sealed class GalaxyQueueItem
{
    public GalaxyBuildKind Kind;
    public ShipClass Cls;
    public HeroId Hero;
    public double Remaining, Total, Cost;

    [JsonIgnore]
    public GalaxyBuild Build => new(Kind, Cls, Hero);
}

/// <summary>A lone fleet taking a planet over.</summary>
public sealed class Occupation
{
    /// <summary>Who takes it; "faction" in saves.</summary>
    [JsonPropertyName("faction")]
    public Side Side;
    public double Remaining;
}

public sealed class Planet : IPoint
{
    public int Id;
    public string Name = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Income;
    public int MineSlots;
    public Side? Owner;
    /// <summary>Station level, 0 without a station.</summary>
    public int Station;
    public int Mines, Platforms;
    /// <summary>A lab lets its owner research; it stays on the ground, out of space battles.</summary>
    public int Lab;
    public int RepairDock, IonCannon;
    /// <summary>A star lance stands here (not in the web version), and the seconds before it can fire again.</summary>
    public int StarLance;
    public double LanceCharge;
    /// <summary>Level of the planet's platforms, 1 to 3.</summary>
    public int PlatformTier = 1;
    public PlanetSpecial? Special;
    /// <summary>Two queues run side by side: ships, and everything else.</summary>
    public List<GalaxyQueueItem> Units = [];
    public List<GalaxyQueueItem> Buildings = [];
    /// <summary>Ship classes kept in production: each goes back in the units queue once none of it waits there.</summary>
    public List<ShipClass> Repeat = [];
    public Occupation? Occupation;
    /// <summary>Planet the ships built here leave for, on their own, once done.</summary>
    public int? Rally;
}

public sealed class Fleet
{
    public int Id;
    /// <summary>Who owns it; "faction" in saves.</summary>
    [JsonPropertyName("faction")]
    public Side Side;
    public ShipCounts Ships = [];
    public List<HeroId> Heroes = [];
    /// <summary>Planet the fleet is docked at, null while travelling.</summary>
    public int? At;
    /// <summary>Planet last left (or docked at).</summary>
    public int From;
    /// <summary>Next planet while travelling.</summary>
    public int? To;
    /// <summary>Planets still to visit after To.</summary>
    public List<int> Path = [];
    /// <summary>0..1 along the lane from From to To.</summary>
    public double Progress;
    /// <summary>Planet the fleet came from before docking: where it falls back after a lost battle.</summary>
    public int Came;
    public double ArrivedAt;
    public int? BattleId;
    /// <summary>Share of their hull the ships have left after their battles.</summary>
    public double Condition = 1;

    public Fleet Copy() => (Fleet)MemberwiseClone();
}

/// <summary>A point on a lane: `T` of the way from planet `A` to planet `B`.</summary>
public sealed record LaneSpot(int A, int B, double T);

/// <summary>`Pending` waits for the player to choose; `Tactical` is being played in the battle view.</summary>
public enum BattleStatus { Pending, Auto, Tactical }

public sealed class GalaxyBattle
{
    public int Id;
    /// <summary>The planet fought over; for an interception, the nearest one.</summary>
    public int PlanetId;
    /// <summary>Set for an interception: two fleets met on this lane, and no planet is at stake (not in the web version).</summary>
    public LaneSpot? Lane;
    public Side Attacker, Defender;
    /// <summary>Where the attacker falls back.</summary>
    public int From;
    public required World World;
    public BattleStatus Status;
    public double Elapsed;
    /// <summary>Heroes each side brought, to know who fell.</summary>
    public Dictionary<Side, List<HeroId>> Heroes = [];
    /// <summary>Research each galaxy side came with, to know what the battle taught it: a fallen colossus' free level.</summary>
    public Dictionary<Side, ResearchLevels> Research = [];
}

public sealed partial class GalaxyAIState
{
    public double Timer;
    /// <summary>The ship class the AI saves up for.</summary>
    public ShipClass? Want;
    public Difficulty Difficulty;
}

/// <summary>A side in the campaign: its race, team, color, treasury and research.</summary>
public sealed class GalaxyFaction : IResearcher
{
    public Side Id;
    /// <summary>Its race: traits, ship prices, heroes, looks.</summary>
    public Faction Race;
    /// <summary>Sides of the same team, other than 0, are allies; team 0 is alone, at war with everyone.</summary>
    public int Team;
    /// <summary>What marks its planets and fleets on the map, as 0xRRGGBB.</summary>
    public uint Color;
    public double Credits { get; set; }
    public GalaxyAIState? AI;
    public bool Eliminated;
    public ResearchLevels Research { get; set; } = new();
    public ResearchJob? Researching { get; set; }
    public List<ResearchJob> ResearchQueue { get; set; } = [];
    /// <summary>Galaxy seconds before each fallen hero can be built again.</summary>
    public Dictionary<HeroId, double> HeroCooldowns = [];
}

public abstract record GalaxyEvent;
public sealed record BattleStarted(int BattleId) : GalaxyEvent;
public sealed record BattleEnded(int PlanetId, Side Attacker, Side Defender, BattleResult Result, LaneSpot? Lane = null) : GalaxyEvent;
public sealed record Captured(int PlanetId, Side Side, Side? From) : GalaxyEvent;
public sealed record PlanetBuilt(int PlanetId, Side Side, string What) : GalaxyEvent;
public sealed record TechResearched(Side Side, TechId Tech) : GalaxyEvent;
/// <summary>What a side earned in a battle and brought back: bounties, and the tech a fallen colossus taught.</summary>
public sealed record BountyPaid(int PlanetId, Side Side, double Credits, TechId? Tech, LaneSpot? Lane = null) : GalaxyEvent;
/// <summary>`Side` won, with its team if it has one (0: alone): its allies won with it.</summary>
public sealed record GalaxyVictory(Side Side, int Team) : GalaxyEvent;

public sealed partial class Galaxy
{
    public double Time;
    /// <summary>The local player's side: its battles wait for its choice.</summary>
    public Side Player;
    /// <summary>Extent of the map the planets lie in.</summary>
    public double W = GalaxyMap.W, H = GalaxyMap.H;
    public List<Planet> Planets = [];
    public List<Lane> Lanes = [];
    /// <summary>In insertion order, as a JavaScript Map: merges and battles depend on it.</summary>
    public OrderedDictionary<int, Fleet> Fleets = [];
    public OrderedDictionary<Side, GalaxyFaction> Factions = [];
    public List<GalaxyBattle> Battles = [];
    public List<GalaxyEvent> Events = [];
    public int NextId = 1;
    /// <summary>State of the Park-Miller generator.</summary>
    public long Seed;
    /// <summary>The side that won; its allies won with it (Galaxies.Won).</summary>
    public Side? Winner;
}

public static partial class Galaxies
{
    /// <summary>
    /// Fleets mend while docked at their own planets, faster with a station and fastest with a
    /// repair dock; biomechanical hulls also mend on the way. Shares of the hull per second.
    /// </summary>
    public const double RepairPlanet = 1.0 / 240, RepairStation = 1.0 / 120, RepairDock = 1.0 / 45, RepairRegen = 1.0 / 150;

    const double AutoDt = 1.0 / 30;

    /// <summary>Deterministic random number in [0, 1) (Park-Miller).</summary>
    public static double GalaxyRand(Galaxy g) => Rng.ParkMiller(ref g.Seed);

    /// <summary>
    /// Both capitals start with a level 1 station, a mine and a small fleet. With `errants`, the
    /// neutral Errants hold the most isolated planet (see ErrantSite) for good: a level 3 station,
    /// tier 3 platforms, an ion cannon, a repair dock, and the colossus with its escort. They are no
    /// galaxy faction: they never build, research or move, and do not count for victory.
    /// </summary>
    public static Galaxy CreateGalaxy(Faction player, IReadOnlyList<Faction> enemies, bool enemyAI = true, long seed = 7,
        GalaxyLayout? layout = null, Difficulty difficulty = Difficulty.Normal, bool errants = true, bool incidents = true) =>
        CreateGalaxy([new Slot(player, player), .. enemies.Select(e => new Slot(e, e, 0, enemyAI ? difficulty : null))], seed, layout, errants, incidents);

    /// <summary>
    /// A galaxy for these seats, the local player's first (Player), each on a capital of the
    /// layout in turn: the layout needs one per seat (GalaxyGen.GenerateLayout places 2 to 6).
    /// Side 3 is the Errants': seats take 0 to 2, then 4 and up. Otherwise as above.
    /// </summary>
    public static Galaxy CreateGalaxy(IReadOnlyList<Slot> slots, long seed = 7, GalaxyLayout? layout = null, bool errants = true, bool incidents = true)
    {
        layout ??= GalaxyGen.Classic;
        if (slots.Count > layout.Capitals.Count) throw new ArgumentException("The layout has fewer capitals than seats.");
        if (slots.Select(s => s.Side).Distinct().Count() != slots.Count) throw new ArgumentException("Two slots share a side.");
        // Their site and their war bands (GalaxyIncidents) are theirs.
        if (slots.Any(s => s.Side == Faction.Errant)) throw new ArgumentException("Side 3 is the Errants'.");
        var g = new Galaxy
        {
            Player = slots[0].Side, Seed = seed, W = layout.W, H = layout.H, Incidents = incidents,
            Planets = layout.Planets.Select((p, id) => new Planet
            {
                Id = id, Name = p.Name, X = p.X, Y = p.Y, Income = p.Income, MineSlots = p.MineSlots, Special = p.Special,
            }).ToList(),
            Lanes = [.. layout.Lanes],
        };
        if (!ReferenceEquals(layout, GalaxyGen.Classic)) AddNewSpecials(g, layout.Capitals, seed);
        for (var i = 0; i < slots.Count; i++)
        {
            var (side, race, capital) = (slots[i].Side, slots[i].Race, layout.Capitals[i]);
            g.Factions[side] = new GalaxyFaction
            {
                Id = side, Race = race, Team = race == Faction.Errant ? 0 : slots[i].Team, Color = slots[i].Color ?? Worlds.DefaultColor(side, race),
                Credits = GalaxyRules.StartCredits, AI = slots[i].AI is Difficulty d ? new GalaxyAIState { Difficulty = d } : null,
            };
            var p = g.Planets[capital];
            p.Owner = side;
            p.Station = 1;
            p.Mines = 1;
            AddShipsAt(g, side, capital, new ShipCounts { [ShipClass.Frigate] = 2, [ShipClass.Scout] = 2 });
        }
        if (errants && ErrantSite(g, [.. layout.Capitals.Take(slots.Count)]) is int site)
        {
            var p = g.Planets[site];
            p.Owner = Faction.Errant;
            p.Station = 3;
            p.Platforms = 4;
            p.PlatformTier = 3;
            p.IonCannon = 1;
            p.RepairDock = 1;
            AddShipsAt(g, Faction.Errant, site, new ShipCounts
            {
                [ShipClass.Colossus] = 1, [ShipClass.Dreadnought] = 1, [ShipClass.Battlecruiser] = 2, [ShipClass.Frigate] = 4,
            });
        }
        return g;
    }

    /// <summary>
    /// The Errants' planet: the farthest in lanes from every capital, then the farthest from them
    /// in distance. Never a capital, and next to one only on a map with nothing farther.
    /// </summary>
    public static int? ErrantSite(Galaxy g, IReadOnlyList<int> capitals)
    {
        var hops = Hops(g, capitals);
        return g.Planets.Where(p => !capitals.Contains(p.Id))
            .OrderByDescending(p => hops.GetValueOrDefault(p.Id, int.MaxValue))
            .ThenByDescending(p => capitals.Min(c => Worlds.Dist(p, g.Planets[c])))
            .Select(p => (int?)p.Id).FirstOrDefault();
    }

    /// <summary>
    /// Lanes to cross from each planet to the nearest of `from`, going on only through the planets
    /// `through` lets by (all of them by default). Planets out of reach are left out.
    /// </summary>
    public static Dictionary<int, int> Hops(Galaxy g, IEnumerable<int> from, Func<Planet, bool>? through = null)
    {
        var hops = new Dictionary<int, int>();
        foreach (var id in from) hops.TryAdd(id, 0);
        var frontier = hops.Keys.ToList();
        for (var d = 1; frontier.Count > 0; d++)
        {
            var next = new List<int>();
            foreach (var id in frontier)
            {
                foreach (var n in Neighbours(g, id))
                {
                    if (hops.TryAdd(n, d) && (through is null || through(g.Planets[n]))) next.Add(n);
                }
            }
            frontier = next;
        }
        return hops;
    }

    public static List<int> Neighbours(Galaxy g, int planetId)
    {
        var out_ = new List<int>();
        foreach (var (a, b) in g.Lanes)
        {
            if (a == planetId) out_.Add(b);
            else if (b == planetId) out_.Add(a);
        }
        return out_;
    }

    /// <summary>Shortest route by lane length (Dijkstra). Returns the planets to visit after `from`, or null.</summary>
    public static List<int>? FindPath(Galaxy g, int from, int to)
    {
        var best = new Dictionary<int, double> { [from] = 0 };
        var prev = new Dictionary<int, int>();
        // A list keeps the order planets were opened in, as a JavaScript Set does: ties go the same way.
        var open = new List<int> { from };
        while (open.Count > 0)
        {
            var cur = -1;
            foreach (var id in open) if (cur < 0 || best[id] < best[cur]) cur = id;
            open.Remove(cur);
            if (cur == to) break;
            foreach (var n in Neighbours(g, cur))
            {
                var d = best[cur] + Worlds.Dist(g.Planets[cur], g.Planets[n]);
                if (d < best.GetValueOrDefault(n, double.PositiveInfinity))
                {
                    best[n] = d;
                    prev[n] = cur;
                    if (!open.Contains(n)) open.Add(n);
                }
            }
        }
        if (from == to || !prev.ContainsKey(to)) return null;
        var path = new List<int> { to };
        while (prev[path[0]] != from) path.Insert(0, prev[path[0]]);
        return path;
    }

    /// <summary>Map speed of a fleet: as fast as its slowest ship, faster with engine research.</summary>
    public static double FleetSpeed(ShipCounts ships, int engines = 0, IEnumerable<HeroId>? heroes = null)
    {
        var slowest = double.PositiveInfinity;
        foreach (var c in Data.Ships.All) if (ships.Of(c) > 0) slowest = Math.Min(slowest, Data.Ships.Defs[c].Speed);
        foreach (var h in heroes ?? []) slowest = Math.Min(slowest, Data.Ships.Defs[Data.Heroes.Defs[h].Base].Speed * Data.Heroes.Defs[h].Speed);
        return (30 + slowest * 0.5) * (1 + Techs.EnginesBonus * engines);
    }

    /// <summary>
    /// Planets a side has eyes on, with its allies': their planets, the neighbours of those, and
    /// those their fleets sit at or fly between. Ownership is known everywhere; defenses and
    /// fleets only there.
    /// </summary>
    public static HashSet<int> VisiblePlanets(Galaxy g, Side side)
    {
        var out_ = new HashSet<int>();
        foreach (var p in g.Planets)
        {
            if (p.Owner is not Side o || !Allies(g, o, side)) continue;
            out_.Add(p.Id);
            foreach (var n in Neighbours(g, p.Id)) out_.Add(n);
        }
        foreach (var f in g.Fleets.Values)
        {
            if (!Allies(g, f.Side, side)) continue;
            if (f.At is int at) out_.Add(at);
            out_.Add(f.From);
            if (f.To is int to) out_.Add(to);
        }
        return out_;
    }

    /// <summary>
    /// Whether a side knows what guards a planet: it or an ally has eyes on it, or it is the Errants'
    /// site, known to all (their guard never leaves it, and changes only in the battles fought there).
    /// </summary>
    public static bool Known(Galaxy g, Side side, Planet planet, HashSet<int>? seen = null) =>
        planet.Owner == Faction.Errant || (seen ?? VisiblePlanets(g, side)).Contains(planet.Id);

    /// <summary>Whether a side sees a fleet: its own or an ally's, or one where it has eyes, out of nebulae.</summary>
    public static bool FleetVisible(Galaxy g, Side side, Fleet fleet, HashSet<int>? seen = null)
    {
        if (Allies(g, fleet.Side, side)) return true;
        if (InNebula(g, fleet)) return false;
        seen ??= VisiblePlanets(g, side);
        return fleet.At is int at ? seen.Contains(at) : seen.Contains(fleet.From) || (fleet.To is int to && seen.Contains(to));
    }

    public static List<Fleet> FleetsAt(Galaxy g, int planetId, Side? side = null) =>
        g.Fleets.Values.Where(f => f.At == planetId && f.BattleId is null && (side is null || f.Side == side)).ToList();

    /// <summary>Whether a battle is fought over this planet; fleets fighting on a lane nearby do not count.</summary>
    public static bool BattleAt(Galaxy g, int planetId) => g.Battles.Exists(b => b.PlanetId == planetId && b.Lane is null);

    /// <summary>What a battle is called: after its planet, or after the lane where two fleets met.</summary>
    public static string BattleName(Galaxy g, int planetId, LaneSpot? lane) => lane is { } l
        ? $"Interception entre {g.Planets[l.A].Name} et {g.Planets[l.B].Name}"
        : $"Bataille de {g.Planets[planetId].Name}";

    /// <summary>Strength of a fleet for estimates: its value, less the damage it carries.</summary>
    public static double FleetStrength(Fleet f) =>
        Battle.FleetValue(f.Ships, f.Heroes) * f.Condition;

    /// <summary>Adds ships to a fleet; its condition becomes the average of both, weighted by value.</summary>
    static void MergeInto(Fleet into, ShipCounts ships, IEnumerable<HeroId> heroes, double condition)
    {
        var a = Battle.FleetValue(into.Ships);
        var b = Battle.FleetValue(ships);
        if (a + b > 0) into.Condition = (into.Condition * a + condition * b) / (a + b);
        foreach (var c in Data.Ships.All) if (ships.Of(c) > 0) into.Ships.Increase(c, ships.Of(c));
        into.Heroes.AddRange(heroes);
    }

    public static Fleet? AddShipsAt(Galaxy g, Side side, int planetId, ShipCounts ships, IReadOnlyList<HeroId>? heroes = null, double condition = 1)
    {
        heroes ??= [];
        if (Battle.CountShips(ships) == 0 && heroes.Count == 0) return null;
        var fleet = FleetsAt(g, planetId, side).FirstOrDefault();
        if (fleet is not null)
        {
            MergeInto(fleet, ships, heroes, condition);
            return fleet;
        }
        var f = new Fleet
        {
            Id = g.NextId++, Side = side, Ships = ships.Clone(), Heroes = [.. heroes], At = planetId, From = planetId,
            Came = planetId, ArrivedAt = g.Time, Condition = condition,
        };
        g.Fleets[f.Id] = f;
        return f;
    }

    /// <summary>
    /// Sends `ships` and `heroes` (the whole fleet by default) towards `dest`; a partial order
    /// splits the fleet, and heroes left out of it stay behind.
    /// </summary>
    public static Result SendFleet(Galaxy g, int fleetId, int dest, ShipCounts? ships = null, IReadOnlyList<HeroId>? heroes = null)
    {
        heroes ??= [];
        if (!g.Fleets.TryGetValue(fleetId, out var fleet) || fleet.At is not int at || fleet.BattleId is not null) return Result.Fail("Flotte indisponible");
        if (g.Planets[dest].Owner is Side partner && AtPeace(g, fleet.Side, partner)) return Result.Fail("Trêve en cours");
        var path = FindPath(g, at, dest);
        if (path is null) return Result.Fail("Aucune route");
        var moving = fleet;
        if (ships is not null)
        {
            foreach (var c in Data.Ships.All) if (ships.Of(c) > fleet.Ships.Of(c)) return Result.Fail("Vaisseaux insuffisants");
            if (heroes.Any(h => !fleet.Heroes.Contains(h))) return Result.Fail("Héros absent");
            if (Battle.CountShips(ships) == 0 && heroes.Count == 0) return Result.Fail("Aucun vaisseau choisi");
            var rest = new ShipCounts();
            foreach (var c in Data.Ships.All)
            {
                var left = fleet.Ships.Of(c) - ships.Of(c);
                if (left > 0) rest[c] = left;
            }
            var restHeroes = fleet.Heroes.Where(h => !heroes.Contains(h)).ToList();
            if (Battle.CountShips(rest) > 0 || restHeroes.Count > 0)
            {
                fleet.Ships = rest;
                fleet.Heroes = restHeroes;
                moving = fleet.Copy();
                moving.Id = g.NextId++;
                moving.Ships = ships.Clone();
                moving.Heroes = [.. heroes];
                moving.Path = [];
                g.Fleets[moving.Id] = moving;
            }
        }
        moving.From = moving.At!.Value;
        moving.At = null;
        moving.To = path[0];
        moving.Path = path.Skip(1).ToList();
        moving.Progress = 0;
        return Result.OK;
    }

    /// <summary>Planets to visit after `from` on the way to `dest`: none when already there, null without a route.</summary>
    static List<int>? RouteFrom(Galaxy g, int from, int dest) => from == dest ? [] : FindPath(g, from, dest);

    /// <summary>Length of a route: from `from`, through the planets of `path` in turn.</summary>
    static double RouteLength(Galaxy g, int from, IEnumerable<int> path)
    {
        double d = 0;
        foreach (var id in path)
        {
            d += Worlds.Dist(g.Planets[from], g.Planets[id]);
            from = id;
        }
        return d;
    }

    /// <summary>
    /// Sends a fleet towards `dest`, wherever it is: a docked one as SendFleet does, whole; one
    /// on the move goes on through its next planet, or turns back mid-lane when that way is
    /// shorter (not in the web version). A fleet in a battle takes no order.
    /// </summary>
    public static Result RedirectFleet(Galaxy g, int fleetId, int dest)
    {
        if (!g.Fleets.TryGetValue(fleetId, out var fleet) || fleet.BattleId is not null) return Result.Fail("Flotte indisponible");
        if (fleet.At is not null) return SendFleet(g, fleetId, dest);
        if (fleet.To is not int to) return Result.Fail("Flotte indisponible");
        if (g.Planets[dest].Owner is Side partner && AtPeace(g, fleet.Side, partner)) return Result.Fail("Trêve en cours");
        if (PlanRedirect(g, fleet, dest) is not { } plan) return Result.Fail("Aucune route");
        var (back, path) = plan;
        if (back)
        {
            // As after a stalemate on a lane: the same spot, seen from the other end.
            (fleet.From, fleet.To) = (to, fleet.From);
            fleet.Progress = 1 - fleet.Progress;
        }
        fleet.Path = path;
        return Result.OK;
    }

    /// <summary>
    /// The shorter way to `dest` for a fleet on the move: on through its next planet, or back
    /// (`Back`) through the one it left; `Path` is what follows that planet. Null without a route.
    /// </summary>
    public static (bool Back, List<int> Path)? PlanRedirect(Galaxy g, Fleet fleet, int dest)
    {
        if (fleet.To is not int to) return null;
        var lane = Worlds.Dist(g.Planets[fleet.From], g.Planets[to]);
        var ahead = RouteFrom(g, to, dest);
        var back = RouteFrom(g, fleet.From, dest);
        var viaTo = ahead is null ? double.PositiveInfinity : (1 - fleet.Progress) * lane + RouteLength(g, to, ahead);
        var viaFrom = back is null ? double.PositiveInfinity : fleet.Progress * lane + RouteLength(g, fleet.From, back);
        if (ahead is null && back is null) return null;
        return viaFrom < viaTo ? (true, back!) : (false, ahead!);
    }

    /// <summary>A fleet on the move stops at the next planet on its way, and goes no farther.</summary>
    public static Result StopFleet(Galaxy g, int fleetId)
    {
        if (!g.Fleets.TryGetValue(fleetId, out var fleet) || fleet.BattleId is not null || fleet.To is null) return Result.Fail("Flotte indisponible");
        fleet.Path = [];
        return Result.OK;
    }

    /// <summary>
    /// Seconds a fleet on the move needs to reach the end of its route, lane by lane (relays and
    /// storms change the pace), as things stand now.
    /// </summary>
    public static double TravelTime(Galaxy g, Fleet f)
    {
        if (f.To is not int to) return 0;
        var engines = g.Factions.TryGetValue(f.Side, out var gf) ? gf.Research[TechId.Engines] : 0;
        var speed = FleetSpeed(f.Ships, engines, f.Heroes);
        var t = (1 - f.Progress) * Worlds.Dist(g.Planets[f.From], g.Planets[to]) / (speed * LaneSpeed(g, f));
        foreach (var next in f.Path)
        {
            t += Worlds.Dist(g.Planets[to], g.Planets[next]) / (speed * LaneSpeed(g, new Fleet { Side = f.Side, From = to, To = next }));
            to = next;
        }
        return t;
    }

    /// <summary>
    /// Where the ships built at a planet leave for once done (not in the web version); null, or
    /// the planet itself, clears it.
    /// </summary>
    public static Result SetRally(Galaxy g, Side side, int planetId, int? target)
    {
        var planet = g.Planets[planetId];
        if (planet.Owner != side) return Result.Fail("Planète non contrôlée");
        if (target is int t && t != planetId && FindPath(g, planetId, t) is null) return Result.Fail("Aucune route");
        planet.Rally = target == planetId ? null : target;
        return Result.OK;
    }

    static bool HostileTo(Galaxy g, Planet planet, Side side)
    {
        // Allies and truce partners let the fleet by, their planets and their fleets.
        var owned = planet.Owner is Side o && AtWar(g, o, side);
        return owned || FleetsAt(g, planet.Id).Exists(f => AtWar(g, f.Side, side));
    }

    /// <summary>
    /// Fleets pass through friendly and neutral planets, and stop at enemy ones. Fleets at war
    /// that meet head-on along a lane stop there and fight first (not in the web version).
    /// </summary>
    static void MoveFleets(Galaxy g, double dt)
    {
        var moving = new List<(Fleet F, double Before)>();
        foreach (var f in g.Fleets.Values)
        {
            if (f.To is not int to || f.BattleId is not null) continue;
            var engines = g.Factions.TryGetValue(f.Side, out var gf) ? gf.Research[TechId.Engines] : 0;
            moving.Add((f, f.Progress));
            f.Progress += FleetSpeed(f.Ships, engines, f.Heroes) * LaneSpeed(g, f) * dt / Worlds.Dist(g.Planets[f.From], g.Planets[to]);
        }
        Intercept(g, moving);
        foreach (var (f, _) in moving)
        {
            if (f.BattleId is not null || f.Progress < 1) continue;
            var arrived = f.To!.Value;
            var b = g.Planets[arrived];
            f.Came = f.From;
            f.From = arrived;
            f.Progress = 0;
            if (f.Path.Count > 0 && !HostileTo(g, b, f.Side))
            {
                f.To = f.Path[0];
                f.Path.RemoveAt(0);
                continue;
            }
            f.To = null;
            f.Path = [];
            f.At = arrived;
            f.ArrivedAt = g.Time;
        }
    }

    /// <summary>
    /// Two fleets at war on the same lane, heading towards each other, that passed each other
    /// during this step: they go back to where they met, and fight there. No fleet slips past
    /// another to strike behind its lines.
    /// </summary>
    static void Intercept(Galaxy g, List<(Fleet F, double Before)> moving)
    {
        for (var i = 0; i < moving.Count; i++)
        {
            var (a, a0) = moving[i];
            for (var j = i + 1; j < moving.Count && a.BattleId is null; j++)
            {
                var (b, b0) = moving[j];
                if (b.BattleId is not null || a.From != b.To || a.To != b.From || !AtWar(g, a.Side, b.Side)) continue;
                // Along the lane, the two progresses add up to 1 where the fleets meet.
                if (a0 + b0 >= 1 || a.Progress + b.Progress < 1) continue;
                var share = (1 - a0 - b0) / (a.Progress - a0 + b.Progress - b0);
                a.Progress = a0 + share * (a.Progress - a0);
                // A hair short of each other: a game saved during the fight has them meet again once loaded.
                b.Progress = Math.Max(0, 1 - a.Progress - 1e-6);
                var near = a.Progress < 0.5 ? a.From : a.To!.Value;
                Open(g, near, new LaneSpot(a.From, a.To!.Value, a.Progress), a.Side, [a], b.Side, [b], PlanetDefense.None, a.From);
            }
        }
    }

    static void RepairFleets(Galaxy g, double dt)
    {
        foreach (var f in g.Fleets.Values)
        {
            if (f.Condition >= 1 || f.BattleId is not null) continue;
            var p = f.At is int at ? g.Planets[at] : null;
            var home = p?.Owner == f.Side ? p : null;
            var rate = (home is null ? 0 : home.RepairDock > 0 ? RepairDock : home.Station > 0 ? RepairStation : RepairPlanet)
                + (Factions.Trait(RaceOf(g, f.Side)).HullRegen > 0 && Supplied(g, f) ? RepairRegen : 0);
            if (g.Factions.TryGetValue(f.Side, out var gf)) rate *= 1 + Techs.RepairBonus * gf.Research[TechId.Repair];
            f.Condition = Math.Min(1, f.Condition + rate * dt);
        }
    }

    static void MergeFleets(Galaxy g)
    {
        var seen = new Dictionary<(int, Side), Fleet>();
        foreach (var f in g.Fleets.Values.OrderBy(f => f.ArrivedAt).ToList())
        {
            if (f.At is not int at || f.BattleId is not null) continue;
            if (!seen.TryGetValue((at, f.Side), out var into))
            {
                seen[(at, f.Side)] = f;
                continue;
            }
            MergeInto(into, f.Ships, f.Heroes, f.Condition);
            g.Fleets.Remove(f.Id);
        }
    }

    static void Capture(Galaxy g, Planet planet, Side side)
    {
        var from = planet.Owner;
        planet.Owner = side;
        // Space structures are lost; mines and labs on the ground change hands.
        planet.Station = 0;
        planet.Platforms = 0;
        planet.PlatformTier = 1;
        planet.RepairDock = 0;
        planet.IonCannon = 0;
        planet.StarLance = 0;
        planet.LanceCharge = 0;
        planet.Units = [];
        planet.Buildings = [];
        planet.Repeat = [];
        planet.Occupation = null;
        planet.Rally = null;
        g.Events.Add(new Captured(planet.Id, side, from));
    }

    static PlanetDefense DefenseOf(Planet p) => new()
    {
        Station = p.Station, Platforms = p.Platforms, Mines = p.Mines, RepairDock = p.RepairDock, IonCannon = p.IonCannon, PlatformTier = p.PlatformTier,
    };

    /// <summary>Whether the planet has anything in orbit that fights back.</summary>
    static bool Fortified(Planet p) => p.Station > 0 || p.Platforms > 0 || p.IonCannon > 0 || p.RepairDock > 0;

    /// <summary>Strength a planet opposes to an attacker: its defenses plus the fleets docked there.</summary>
    public static double DefenseValue(Galaxy g, Planet planet)
    {
        var fleets = planet.Owner is Side owner ? FleetsAt(g, planet.Id, owner) : [];
        var levels = Station.Levels.Take(planet.Station).Sum(l => l.Hull + l.Shield);
        return levels / 2 + planet.Platforms * Buildables.Defs[BuildableType.Platform].Cost + planet.IonCannon * Buildables.Defs[BuildableType.IonCannon].Cost
            + fleets.Sum(FleetStrength);
    }

    static void StartBattle(Galaxy g, Planet planet, Side attacker, Side defender, int from) =>
        Open(g, planet.Id, null, attacker, FleetsAt(g, planet.Id, attacker), defender, FleetsAt(g, planet.Id, defender),
            planet.Owner == defender ? DefenseOf(planet) : PlanetDefense.None, from);

    /// <summary>Opens a battle between these fleets, over a planet or on a lane.</summary>
    static void Open(Galaxy g, int planetId, LaneSpot? lane, Side attacker, List<Fleet> attackers, Side defender, List<Fleet> defenders,
        PlanetDefense defense, int from)
    {
        var id = g.NextId++;
        BattleSide SideOf(Side side, List<Fleet> fleets)
        {
            var ships = new ShipCounts();
            var heroes = new List<HeroId>();
            double value = 0, wear = 0;
            foreach (var f in fleets)
            {
                f.BattleId = id;
                foreach (var c in Data.Ships.All) if (f.Ships.Of(c) > 0) ships.Increase(c, f.Ships.Of(c));
                heroes.AddRange(f.Heroes);
                var v = Battle.FleetValue(f.Ships);
                value += v;
                wear += v * f.Condition;
            }
            return new BattleSide
            {
                Side = side, Race = RaceOf(g, side), Color = g.Factions.TryGetValue(side, out var gf) ? gf.Color : null,
                Ships = ships, Heroes = heroes, Research = gf?.Research,
                Condition = value > 0 ? wear / value : 1,
            };
        }
        var att = SideOf(attacker, attackers);
        var def = SideOf(defender, defenders);
        var world = Battle.CreatePlanetBattle(att, def, defense, (int)Math.Floor(GalaxyRand(g) * 2147483646) + 1);
        var status = attacker == g.Player || defender == g.Player ? BattleStatus.Pending : BattleStatus.Auto;
        g.Battles.Add(new GalaxyBattle
        {
            Id = id, PlanetId = planetId, Lane = lane, Attacker = attacker, Defender = defender, From = from, World = world, Status = status,
            Heroes = new() { [attacker] = att.Heroes, [defender] = def.Heroes },
            Research = new[] { att, def }.Where(x => x.Research is not null).ToDictionary(x => x.Side, x => x.Research!.Clone()),
        });
        g.Events.Add(new BattleStarted(id));
    }

    /// <summary>
    /// A lone fleet occupies an undefended planet, and takes it once the occupation runs out.
    /// Two sides at war present (fleets, or an owner with a station or platforms) fight: the last
    /// fleet to arrive attacks. Allies and sides bound by a truce do not fight, and do not take
    /// each other's planets: on a neutral one, the first to come takes it while the other waits
    /// by. Errant war bands never take a planet.
    /// </summary>
    static void DetectConflicts(Galaxy g)
    {
        foreach (var planet in g.Planets)
        {
            if (BattleAt(g, planet.Id)) continue;
            var here = FleetsAt(g, planet.Id).OrderBy(f => f.ArrivedAt).ToList();
            var present = here.Select(f => f.Side).Distinct().ToList();
            var owner = planet.Owner;
            var defended = owner is Side o && (Fortified(planet) || present.Contains(o));
            var clash = Clash(g, here, defended ? owner : null);
            var takers = present.Where(f => f != Faction.Errant && (owner is not Side o2 || f == o2 || AtWar(g, f, o2))).ToList();
            if (!defended && clash is null && takers.Count > 0)
            {
                var f = planet.Occupation is { } going && takers.Contains(going.Side) ? going.Side : takers[0];
                if (owner == f) planet.Occupation = null;
                else if (planet.Occupation?.Side != f)
                {
                    var time = owner is not null ? GalaxyRules.CaptureTime : GalaxyRules.NeutralCaptureTime;
                    planet.Occupation = new Occupation { Side = f, Remaining = time };
                }
                continue;
            }
            planet.Occupation = null;
            if (clash is { } c) StartBattle(g, planet, c.Attacker.Side, c.Defender, c.Attacker.Came);
        }
    }

    /// <summary>
    /// Ends a battle: survivors go back on the map as fleets, and the planet may change hands.
    /// After an interception, they take up their way again; both turn back after a stalemate.
    /// </summary>
    public static void ConcludeBattle(Galaxy g, int battleId, BattleResult result)
    {
        var b = g.Battles.Find(x => x.Id == battleId);
        if (b is null) return;
        g.Battles.Remove(b);
        var w = b.World;
        var att = Battle.Survivors(w, b.Attacker);
        var def = Battle.Survivors(w, b.Defender);
        var attCondition = Battle.SurvivorCondition(w, b.Attacker);
        var defCondition = Battle.SurvivorCondition(w, b.Defender);
        var attHeroes = Battle.HeroSurvivors(w, b.Attacker);
        var defHeroes = Battle.HeroSurvivors(w, b.Defender);
        // Fallen heroes can be built again after a while.
        foreach (var (side, alive) in new[] { (b.Attacker, attHeroes), (b.Defender, defHeroes) })
        {
            if (!g.Factions.TryGetValue(side, out var f)) continue;
            foreach (var h in b.Heroes.GetValueOrDefault(side) ?? []) if (!alive.Contains(h)) f.HeroCooldowns[h] = Data.Heroes.Defs[h].Respawn;
        }
        if (b.Lane is not null)
        {
            foreach (var f in g.Fleets.Values.Where(f => f.BattleId == b.Id).ToList())
            {
                var attacking = f.Side == b.Attacker;
                var ships = attacking ? att : def;
                var heroes = attacking ? attHeroes : defHeroes;
                if (Battle.CountShips(ships) == 0 && heroes.Count == 0)
                {
                    g.Fleets.Remove(f.Id);
                    continue;
                }
                f.Ships = ships;
                f.Heroes = [.. heroes];
                f.Condition = attacking ? attCondition : defCondition;
                f.BattleId = null;
                if (result != BattleResult.Retreat) continue;
                (f.From, f.To) = (f.To!.Value, f.From);
                f.Progress = 1 - f.Progress;
                f.Path = [];
            }
        }
        else Settle(g, b, result, att, def, attCondition, defCondition, attHeroes, defHeroes);
        g.Events.Add(new BattleEnded(b.PlanetId, b.Attacker, b.Defender, result, b.Lane));
        // Each galaxy side brings back what it earned: the bounties of the ships it shot down
        // (a colossus' included), and the free tech level a fallen colossus teaches. The Errants,
        // no galaxy faction, keep nothing.
        foreach (var side in new[] { b.Attacker, b.Defender })
        {
            if (!g.Factions.TryGetValue(side, out var gf) || !w.Factions.TryGetValue(side, out var fs)) continue;
            gf.Credits += fs.Bounties;
            var before = b.Research.GetValueOrDefault(side) ?? fs.Research;
            TechId? taught = null;
            foreach (var t in Techs.All)
            {
                var gained = fs.Research[t] - before[t];
                if (gained <= 0) continue;
                gf.Research[t] = Math.Min(Techs.Defs[t].MaxLevel, gf.Research[t] + gained);
                taught = t;
            }
            if (fs.Bounties > 0 || taught is not null) g.Events.Add(new BountyPaid(b.PlanetId, side, fs.Bounties, taught, b.Lane));
        }
    }

    /// <summary>The end of a battle over a planet: what is left of its defenses, who holds it, where the survivors go.</summary>
    static void Settle(Galaxy g, GalaxyBattle b, BattleResult result, ShipCounts att, ShipCounts def, double attCondition, double defCondition,
        IReadOnlyList<HeroId> attHeroes, IReadOnlyList<HeroId> defHeroes)
    {
        foreach (var f in g.Fleets.Values.ToList()) if (f.BattleId == b.Id) g.Fleets.Remove(f.Id);
        var planet = g.Planets[b.PlanetId];
        if (planet.Owner == b.Defender)
        {
            var left = Battle.DefenseLeft(b.World, b.Defender);
            planet.Station = left.Station;
            planet.Platforms = left.Platforms;
            planet.Mines = left.Mines;
            planet.RepairDock = left.RepairDock;
            planet.IonCannon = left.IonCannon;
            planet.PlatformTier = left.PlatformTier;
        }
        if (result == BattleResult.Attacker)
        {
            // An Errant war band pillages the planet and leaves it (GalaxyIncidents): it never holds one.
            if (RaceOf(g, b.Attacker) != Faction.Errant) Capture(g, planet, b.Attacker);
            AddShipsAt(g, b.Attacker, planet.Id, att, attHeroes, attCondition);
        }
        else
        {
            AddShipsAt(g, b.Defender, planet.Id, def, defHeroes, defCondition);
            if (result == BattleResult.Retreat)
            {
                var back = AddShipsAt(g, b.Attacker, planet.Id, att, attHeroes, attCondition);
                if (back is not null) SendFleet(g, back.Id, b.From);
            }
        }
    }

    /// <summary>The player leaves a battle they were commanding: it plays on by itself.</summary>
    public static void DelegateBattle(Galaxy g, int battleId)
    {
        var b = g.Battles.Find(x => x.Id == battleId);
        if (b?.Status == BattleStatus.Tactical) b.Status = BattleStatus.Auto;
    }

    public static void ChooseBattle(Galaxy g, int battleId, BattleStatus mode)
    {
        var b = g.Battles.Find(x => x.Id == battleId);
        if (b?.Status == BattleStatus.Pending) b.Status = mode;
    }

    static void AdvanceAutoBattles(Galaxy g, double dt)
    {
        foreach (var b in g.Battles.ToList())
        {
            if (b.Status != BattleStatus.Auto) continue;
            BattleResult? result = null;
            for (var t = 0.0; t < GalaxyRules.AutoSpeed * dt && result is null; t += AutoDt)
            {
                Steps.StepWorld(b.World, AutoDt);
                Battle.UpdateBattleAI(b.World, [b.Attacker, b.Defender], AutoDt);
                b.Elapsed += AutoDt;
                result = Battle.Outcome(b.World, b.Attacker, b.Defender);
                if (result is null && b.Elapsed >= GalaxyRules.AutoTimeout) result = BattleResult.Retreat;
            }
            b.World.Events.Clear();
            if (result is BattleResult r) ConcludeBattle(g, b.Id, r);
        }
    }

    static void UpdateOccupations(Galaxy g, double dt)
    {
        foreach (var planet in g.Planets)
        {
            var o = planet.Occupation;
            if (o is null) continue;
            o.Remaining -= dt;
            if (o.Remaining <= 0) Capture(g, planet, o.Side);
        }
    }

    /// <summary>Galaxy income per second for a side: planets, plus mines (more on rich deposits, more with research).</summary>
    public static double IncomeOf(Galaxy g, Side side)
    {
        double s = 0;
        foreach (var p in g.Planets)
        {
            if (p.Owner == side) s += PlanetIncome(g, p);
        }
        return s;
    }

    /// <summary>What one planet pays its owner per second, mining research included.</summary>
    public static double PlanetIncome(Galaxy g, Planet p)
    {
        var mining = 1 + Techs.MiningBonus * (p.Owner is Side o && g.Factions.TryGetValue(o, out var f) ? f.Research[TechId.Mining] : 0);
        return (p.Income + p.Station * GalaxyRules.StationIncome + p.Mines * GalaxyRules.MineIncome * mining * (p.Special == PlanetSpecial.Rich ? 2 : 1)) * BoomOf(g, p);
    }

    static readonly Dictionary<GalaxyBuildKind, string> BuildingNames = new()
    {
        [GalaxyBuildKind.Station] = Station.Name, [GalaxyBuildKind.Mine] = Buildables.Defs[BuildableType.Mine].Name,
        [GalaxyBuildKind.Platform] = Buildables.Defs[BuildableType.Platform].Name, [GalaxyBuildKind.PlatformTier] = "Renforcer les plateformes",
        [GalaxyBuildKind.Lab] = "Laboratoire", [GalaxyBuildKind.RepairDock] = Buildables.Defs[BuildableType.RepairDock].Name,
        [GalaxyBuildKind.IonCannon] = Buildables.Defs[BuildableType.IonCannon].Name,
        [GalaxyBuildKind.StarLance] = Buildables.Defs[BuildableType.StarLance].Name,
    };

    static BuildableType BuildableOf(GalaxyBuildKind kind) => kind switch
    {
        GalaxyBuildKind.Mine => BuildableType.Mine,
        GalaxyBuildKind.Platform => BuildableType.Platform,
        GalaxyBuildKind.RepairDock => BuildableType.RepairDock,
        GalaxyBuildKind.IonCannon => BuildableType.IonCannon,
        GalaxyBuildKind.StarLance => BuildableType.StarLance,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static string BuildName(Planet planet, GalaxyBuild build) => build.Kind switch
    {
        GalaxyBuildKind.Ship => Data.Ships.OrderName(build.Cls),
        GalaxyBuildKind.Hero => $"{Data.Heroes.Defs[build.Hero].Name} ({Data.Heroes.Defs[build.Hero].Title})",
        GalaxyBuildKind.Station when planet.Station > 0 => "Améliorer la station",
        GalaxyBuildKind.PlatformTier => $"Plateformes niveau {Math.Min(3, planet.PlatformTier + 1)}",
        _ => BuildingNames[build.Kind],
    };

    /// <summary>Price and time at the planet, whose station is taken at level `station`; `logistics`: its owner's research.</summary>
    static (double Cost, double Time) BuildCost(Galaxy g, Planet planet, GalaxyBuild build, int station, int logistics = 0)
    {
        var yard = planet.Special == PlanetSpecial.Shipyard ? 0.7 : 1;
        switch (build.Kind)
        {
            case GalaxyBuildKind.Hero:
            {
                var def = Data.Heroes.Defs[build.Hero];
                return (def.Cost, def.BuildTime * yard);
            }
            case GalaxyBuildKind.Ship:
                return (Data.Ships.Cost(build.Cls, planet.Owner is Side owner ? RaceOf(g, owner) : Faction.Klaed),
                    Data.Ships.Defs[build.Cls].BuildTime * yard * (1 - Techs.LogisticsBonus * logistics));
            case GalaxyBuildKind.Station:
                if (station == 0) return (GalaxyRules.StationCost, GalaxyRules.StationTime);
                // At the top level there is no next one: nothing to pay (CanBuild refuses it anyway).
                return station < Station.Levels.Count ? (Station.Levels[station].UpgradeCost, Station.Levels[station].UpgradeTime) : (0, 0);
            case GalaxyBuildKind.Lab:
                return (GalaxyRules.LabCost, GalaxyRules.LabTime);
            case GalaxyBuildKind.PlatformTier:
            {
                var tier = Buildables.Defs[BuildableType.Platform].Tiers![Math.Min(planet.PlatformTier, 2) - 1];
                return (tier.Cost, tier.Time);
            }
            default:
            {
                var def = Buildables.Defs[BuildableOf(build.Kind)];
                return (def.Cost, def.BuildTime);
            }
        }
    }

    public static double BuildPrice(Galaxy g, Planet planet, GalaxyBuild build) => BuildCost(g, planet, build, planet.Station).Cost;

    public static double BuildTime(Galaxy g, Planet planet, GalaxyBuild build) => BuildCost(g, planet, build, planet.Station, Logistics(g, planet)).Time;

    /// <summary>The logistics research of the planet's owner: its ships are built faster.</summary>
    static int Logistics(Galaxy g, Planet planet) =>
        planet.Owner is Side owner && g.Factions.TryGetValue(owner, out var f) ? f.Research[TechId.Logistics] : 0;

    static List<GalaxyQueueItem> QueueFor(Planet planet, GalaxyBuild build) =>
        build.Kind is GalaxyBuildKind.Ship or GalaxyBuildKind.Hero ? planet.Units : planet.Buildings;

    /// <summary>Whether a hero is out there already: in a fleet, in a battle, or being built.</summary>
    static bool HeroInService(Galaxy g, HeroId hero) =>
        g.Fleets.Values.Any(f => f.Heroes.Contains(hero))
        || g.Battles.Exists(b => b.Heroes.Values.Any(list => list.Contains(hero)))
        || g.Planets.Exists(p => p.Units.Exists(q => q.Kind == GalaxyBuildKind.Hero && q.Hero == hero));

    /// <summary>How many ships of this class the side has anywhere: in fleets, battles and queues.</summary>
    public static int ClassCount(Galaxy g, Side side, ShipClass cls)
    {
        var type = Ids.Of(cls);
        return g.Fleets.Values.Where(f => f.Side == side).Sum(f => f.Ships.Of(cls))
            + g.Battles.Sum(b => b.World.Entities.Count(e => e.Side == side && e.Kind == EntityKind.Ship && e.Type == type))
            + g.Planets.Where(p => p.Owner == side).Sum(p => p.Units.Count(q => q.Kind == GalaxyBuildKind.Ship && q.Cls == cls));
    }

    public static int PlatformCap(Planet planet) => planet.Station * 2 + (planet.Special == PlanetSpecial.Fortress ? 2 : 0);

    /// <summary>Whether the side can queue this at the planet right now, and if not, why.</summary>
    public static Result CanBuild(Galaxy g, Side side, int planetId, GalaxyBuild build)
    {
        var planet = g.Planets[planetId];
        if (!g.Factions.TryGetValue(side, out var f) || planet.Owner != side) return Result.Fail("Planète non contrôlée");
        if (BattleAt(g, planetId)) return Result.Fail("Combat en cours");
        if (QueueFor(planet, build).Count >= Buildables.MaxQueue) return Result.Fail("File d'attente pleine");
        int Queued(GalaxyBuildKind kind) => planet.Buildings.Count(q => q.Kind == kind);
        // Queued station work counts: a station being built or upgraded unlocks what comes after it.
        int Have(GalaxyBuildKind kind) => Queued(kind) + kind switch
        {
            GalaxyBuildKind.Station => planet.Station,
            GalaxyBuildKind.Mine => planet.Mines,
            GalaxyBuildKind.Platform => planet.Platforms,
            GalaxyBuildKind.Lab => planet.Lab,
            GalaxyBuildKind.RepairDock => planet.RepairDock,
            GalaxyBuildKind.IonCannon => planet.IonCannon,
            _ => 0,
        };
        switch (build.Kind)
        {
            case GalaxyBuildKind.Station:
                if (Have(GalaxyBuildKind.Station) >= Station.Levels.Count) return Result.Fail("Niveau maximum");
                break;
            case GalaxyBuildKind.Mine:
                if (Have(GalaxyBuildKind.Mine) >= planet.MineSlots) return Result.Fail("Aucun emplacement");
                break;
            case GalaxyBuildKind.Lab:
                if (Have(GalaxyBuildKind.Lab) >= 1) return Result.Fail("Déjà construit");
                break;
            case GalaxyBuildKind.PlatformTier:
                if (planet.PlatformTier >= 3) return Result.Fail("Niveau maximum");
                if (Queued(GalaxyBuildKind.PlatformTier) > 0) return Result.Fail("Déjà en cours");
                if (planet.Platforms == 0) return Result.Fail("Plateforme requise");
                if (planet.Station < planet.PlatformTier + 1) return Result.Fail($"Station niveau {planet.PlatformTier + 1} requise");
                break;
            case GalaxyBuildKind.Platform:
                if (planet.Station == 0) return Result.Fail("Station requise");
                if (Have(GalaxyBuildKind.Platform) >= PlatformCap(planet)) return Result.Fail($"Maximum {PlatformCap(planet)}");
                break;
            case GalaxyBuildKind.RepairDock:
            case GalaxyBuildKind.IonCannon:
            {
                var need = build.Kind == GalaxyBuildKind.IonCannon ? 2 : 1;
                if (planet.Station < need) return Result.Fail(need == 1 ? "Station requise" : $"Station niveau {need} requise");
                if (Have(build.Kind) >= 1) return Result.Fail("Déjà construit");
                break;
            }
            case GalaxyBuildKind.StarLance:
                if (planet.Station < Station.Levels.Count) return Result.Fail($"Station niveau {Station.Levels.Count} requise");
                if (HasLance(g, side)) return Result.Fail("Une seule Lance stellaire");
                break;
            case GalaxyBuildKind.Hero:
            {
                var def = Data.Heroes.Defs[build.Hero];
                if (def.Id.Faction != f.Race) return Result.Fail("Héros d'une autre faction");
                if (planet.Station < def.MinStationLevel) return Result.Fail($"Station niveau {def.MinStationLevel} requise");
                if (HeroInService(g, build.Hero)) return Result.Fail("Déjà en service");
                var wait = f.HeroCooldowns.GetValueOrDefault(build.Hero);
                if (wait > 0) return Result.Fail($"Disponible dans {Math.Ceiling(wait)} s");
                break;
            }
            case GalaxyBuildKind.Ship:
            {
                var yard = Shipyard(planet, build.Cls);
                if (!yard.Ok) return yard;
                var def = Data.Ships.Defs[build.Cls];
                if (def.Limit is int limit && ClassCount(g, side, build.Cls) >= limit) return Result.Fail(def.LimitReason);
                break;
            }
        }
        if (f.Credits < BuildCost(g, planet, build, planet.Station).Cost) return Result.Fail("Crédits insuffisants");
        return Result.OK;
    }

    /// <summary>Whether the planet's station can build this ship class at all.</summary>
    static Result Shipyard(Planet planet, ShipClass cls)
    {
        var def = Data.Ships.Defs[cls];
        if (def.FromHangar) return Result.Fail("Lancé par les hangars");
        if (planet.Station == 0) return Result.Fail("Station requise");
        if (planet.Station < def.MinStationLevel) return Result.Fail($"Station niveau {def.MinStationLevel} requise");
        return Result.OK;
    }

    /// <summary>
    /// Whether the side can keep this class in production at the planet: its station must
    /// build it. Credits, a full queue or a class at its limit only make the loop wait.
    /// </summary>
    public static Result CanLoop(Galaxy g, Side side, int planetId, ShipClass cls)
    {
        var planet = g.Planets[planetId];
        if (!g.Factions.ContainsKey(side) || planet.Owner != side) return Result.Fail("Planète non contrôlée");
        return Shipyard(planet, cls);
    }

    /// <summary>Starts or stops a planet's production loop of a ship class. Heroes are one of a kind: they never loop.</summary>
    public static Result SetLoop(Galaxy g, Side side, int planetId, ShipClass cls, bool on)
    {
        var planet = g.Planets[planetId];
        if (!on)
        {
            if (planet.Owner != side) return Result.Fail("Planète non contrôlée");
            planet.Repeat.Remove(cls);
            return Result.OK;
        }
        var r = CanLoop(g, side, planetId, cls);
        if (r.Ok && !planet.Repeat.Contains(cls)) planet.Repeat.Add(cls);
        return r;
    }

    public static Result EnqueueBuild(Galaxy g, Side side, int planetId, GalaxyBuild build)
    {
        var r = CanBuild(g, side, planetId, build);
        if (!r.Ok) return r;
        var planet = g.Planets[planetId];
        // A station upgrade queued behind another one is priced at the level it will reach.
        var ahead = planet.Station + planet.Buildings.Count(q => q.Kind == GalaxyBuildKind.Station);
        var (cost, time) = BuildCost(g, planet, build, ahead, Logistics(g, planet));
        var f = g.Factions[side];
        if (f.Credits < cost) return Result.Fail("Crédits insuffisants");
        f.Credits -= cost;
        QueueFor(planet, build).Add(new GalaxyQueueItem { Kind = build.Kind, Cls = build.Cls, Hero = build.Hero, Remaining = time, Total = time, Cost = cost });
        return Result.OK;
    }

    public static void CancelBuild(Galaxy g, int planetId, int index, QueueSide which = QueueSide.Buildings)
    {
        var planet = g.Planets[planetId];
        var queue = which == QueueSide.Units ? planet.Units : planet.Buildings;
        if (index < 0 || index >= queue.Count || planet.Owner is not Side owner) return;
        var item = queue[index];
        queue.RemoveAt(index);
        g.Factions[owner].Credits += item.Cost;
    }

    static void Finish(Galaxy g, Planet planet, GalaxyQueueItem item)
    {
        var owner = planet.Owner!.Value;
        string what;
        switch (item.Kind)
        {
            case GalaxyBuildKind.Ship:
                Deliver(g, planet, new ShipCounts { [item.Cls] = Data.Ships.Defs[item.Cls].Squad ?? 1 }, []);
                what = Data.Ships.OrderName(item.Cls);
                break;
            case GalaxyBuildKind.Hero:
                Deliver(g, planet, [], [item.Hero]);
                what = Data.Heroes.Defs[item.Hero].Name;
                break;
            case GalaxyBuildKind.Station:
                planet.Station++;
                what = planet.Station == 1 ? Station.Name : $"{Station.Name} niveau {planet.Station}";
                break;
            default:
                switch (item.Kind)
                {
                    case GalaxyBuildKind.Mine: planet.Mines++; break;
                    case GalaxyBuildKind.Platform: planet.Platforms++; break;
                    case GalaxyBuildKind.PlatformTier: planet.PlatformTier++; break;
                    case GalaxyBuildKind.Lab: planet.Lab++; break;
                    case GalaxyBuildKind.RepairDock: planet.RepairDock++; break;
                    case GalaxyBuildKind.IonCannon: planet.IonCannon++; break;
                    case GalaxyBuildKind.StarLance:
                        planet.StarLance = 1;
                        planet.LanceCharge = LanceRecharge;
                        break;
                }
                what = BuildingNames[item.Kind];
                break;
        }
        g.Events.Add(new PlanetBuilt(planet.Id, owner, what));
    }

    /// <summary>
    /// New ships join the fleet waiting at their planet; with a rally point, they leave for it
    /// on their own, as a fleet of their own, and the fleet waiting there stays.
    /// </summary>
    static void Deliver(Galaxy g, Planet planet, ShipCounts ships, IReadOnlyList<HeroId> heroes)
    {
        var owner = planet.Owner!.Value;
        if (planet.Rally is not int rally || FindPath(g, planet.Id, rally) is null)
        {
            AddShipsAt(g, owner, planet.Id, ships, heroes);
            return;
        }
        var f = new Fleet
        {
            Id = g.NextId++, Side = owner, Ships = ships.Clone(), Heroes = [.. heroes], At = planet.Id, From = planet.Id,
            Came = planet.Id, ArrivedAt = g.Time,
        };
        g.Fleets[f.Id] = f;
        SendFleet(g, f.Id, rally);
    }

    /// <summary>
    /// The front of each queue progresses; nothing is built on a planet under attack. Classes in
    /// a loop go back in the queue once none of them waits there; short of credits, they wait.
    /// </summary>
    static void UpdateProduction(Galaxy g, double dt)
    {
        foreach (var planet in g.Planets)
        {
            if (planet.Owner is not Side owner || BattleAt(g, planet.Id)) continue;
            foreach (var queue in new[] { planet.Buildings, planet.Units })
            {
                var item = queue.Count > 0 ? queue[0] : null;
                if (item is null || (item.Remaining -= dt) > 0) continue;
                queue.RemoveAt(0);
                Finish(g, planet, item);
            }
            foreach (var cls in planet.Repeat)
            {
                if (!planet.Units.Exists(q => q.Kind == GalaxyBuildKind.Ship && q.Cls == cls)) EnqueueBuild(g, owner, planet.Id, GalaxyBuild.Ship(cls));
            }
        }
    }

    /// <summary>
    /// Whether the next level of this tech can join the queue: judged on the level it would reach.
    /// Research needs a lab, and a station of the level about to be reached.
    /// </summary>
    public static Result CanResearchTech(Galaxy g, Side side, TechId tech)
    {
        if (!g.Factions.TryGetValue(side, out var f)) return Result.Fail("Faction inconnue");
        var own = g.Planets.Where(p => p.Owner == side).ToList();
        var level = ResearchQueue.PlannedLevel(f, tech);
        if (!own.Exists(p => p.Lab > 0)) return Result.Fail("Laboratoire requis");
        if (level >= Techs.Defs[tech].MaxLevel) return Result.Fail("Niveau maximum");
        if (ResearchQueue.Jobs(f).Count >= Buildables.MaxQueue) return Result.Fail("File d'attente pleine");
        var station = Techs.StationFor(level + 1);
        if (own.Select(p => p.Station).Append(0).Max() < station) return Result.Fail($"Station niveau {station} requise");
        if (f.Credits < Techs.Defs[tech].Costs[level]) return Result.Fail("Crédits insuffisants");
        return Result.OK;
    }

    public static Result StartTech(Galaxy g, Side side, TechId tech)
    {
        var r = CanResearchTech(g, side, tech);
        if (!r.Ok) return r;
        ResearchQueue.Queue(g.Factions[side], tech);
        return Result.OK;
    }

    /// <summary>Cancels a research job, 0 being the running one; later levels of the same tech go too.</summary>
    public static void CancelTech(Galaxy g, Side side, int index = 0)
    {
        if (g.Factions.TryGetValue(side, out var f)) ResearchQueue.CancelAt(f, index);
    }

    /// <summary>Owning ancient archives speeds research up.</summary>
    public static double ResearchSpeed(Galaxy g, Side side) =>
        g.Planets.Exists(p => p.Owner == side && p.Special == PlanetSpecial.Archive) ? 1 / 0.75 : 1;

    static void UpdateResearch(Galaxy g, double dt)
    {
        foreach (var f in g.Factions.Values)
        {
            if (ResearchQueue.Advance(f, dt * ResearchSpeed(g, f.Id)) is TechId done) g.Events.Add(new TechResearched(f.Id, done));
        }
    }

    /// <summary>
    /// A side without a planet is out, unless it is taking one over or fighting for one: its
    /// fleets have nowhere left to go. The last camp standing wins, a team together or a side
    /// alone; the Errants, no galaxy faction, are never out and never in the way.
    /// </summary>
    static void CheckElimination(Galaxy g)
    {
        foreach (var f in g.Factions.Values)
        {
            if (f.Eliminated) continue;
            var planets = g.Planets.Exists(p => p.Owner == f.Id || p.Occupation?.Side == f.Id);
            var fighting = g.Battles.Exists(b => b.Attacker == f.Id || b.Defender == f.Id);
            if (planets || fighting) continue;
            f.Eliminated = true;
            foreach (var fl in g.Fleets.Values.ToList()) if (fl.Side == f.Id) g.Fleets.Remove(fl.Id);
        }
        var alive = g.Factions.Values.Where(f => !f.Eliminated).ToList();
        if (g.Winner is null && alive.Count > 0 && alive.TrueForAll(f => Allies(g, f.Id, alive[0].Id)))
        {
            g.Winner = alive[0].Id;
            g.Events.Add(new GalaxyVictory(alive[0].Id, alive[0].Team));
        }
    }

    /// <summary>The galaxy waits while the player has a battle to choose or to fight.</summary>
    public static bool WaitingForPlayer(Galaxy g) => g.Battles.Exists(b => b.Status != BattleStatus.Auto);

    public static void UpdateGalaxy(Galaxy g, double dt)
    {
        foreach (var f in g.Factions.Values)
        {
            if (!f.Eliminated) f.Credits += IncomeOf(g, f.Id) * (f.AI is not null ? Worlds.Difficulties[f.AI.Difficulty].Income : 1) * dt;
            foreach (var h in f.HeroCooldowns.Keys.ToList()) f.HeroCooldowns[h] = Math.Max(0, f.HeroCooldowns[h] - dt);
        }
        UpdateProduction(g, dt);
        UpdateLances(g, dt);
        UpdateResearch(g, dt);
        UpdateDiplomacy(g, dt);
        MoveFleets(g, dt);
        RepairFleets(g, dt);
        WearUnsupplied(g, dt);
        MergeFleets(g);
        DetectConflicts(g);
        UpdateOccupations(g, dt);
        ExploreRuins(g);
        AdvanceAutoBattles(g, dt);
        UpdateIncidents(g, dt);
        CheckElimination(g);
    }

    /// <summary>Fixed galaxy time step, in seconds.</summary>
    public const double Step = 0.1;

    /// <summary>Advances the galaxy, unless the game is over or a battle waits for the player.</summary>
    public static void StepGalaxy(Galaxy g, double dt = Step)
    {
        if (g.Winner is not null || WaitingForPlayer(g)) return;
        g.Time += dt;
        GalaxyAi.UpdateGalaxyAI(g, dt);
        UpdateGalaxy(g, dt);
    }

    public static void RunGalaxy(Galaxy g, double seconds, double dt = Step)
    {
        var steps = Js.Round(seconds / dt);
        for (var i = 0; i < steps; i++) StepGalaxy(g, dt);
    }
}
