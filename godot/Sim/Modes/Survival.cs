// Survival: every seat together, on one team, against waves of Errants coming from the edges of
// the map, without end. Each wave is bigger and heavier than the last: a boss every fifth one,
// several once the colossus has come, elite Errants past the twentieth (tougher wave after wave),
// and now and then a wave with a theme. A wave beaten pays the seats still standing and brings the
// next one sooner. It ends once no seat has a station, on each seat's score.
using SandboxShip.Sim.Data;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim;

/// <summary>A survival wave's theme: its ships drawn from a narrower roster, announced as it comes.</summary>
public enum WaveTheme { None, Swarm, Armada, Carriers }

/// <summary>What a seat has done in a survival game: its score, the Errant warships it brought down, the bosses among them.</summary>
public sealed class SurvivalTally
{
    public int Score, Downed, Bosses;
}

public static class Survival
{
    /// <summary>Seconds before the first wave; then between two, a little shorter each time down to the floor.</summary>
    public const double Grace = 150, Interval = 90, MinInterval = 50;
    /// <summary>Once a wave is beaten, the next one comes after this many seconds at most.</summary>
    public const double Breather = 25;
    /// <summary>A boss every this many waves: a dreadnought, a titan, a leviathan, the colossus; then several at once.</summary>
    public const int BossEvery = 5;
    static readonly ShipClass[] BossClasses = [ShipClass.Dreadnought, ShipClass.Titan, ShipClass.Leviathan, ShipClass.Colossus];
    /// <summary>The last wave of ordinary Errants: past it, their weapons, armor and shields research rise a level every two waves.</summary>
    public const int EliteFrom = 20;
    /// <summary>
    /// A wave's ships at most, its bosses aside (a swarm's more): as its budget grows past what that
    /// many ships could spend, its ships get bigger instead, each worth at least the share of the
    /// budget two caps would leave it.
    /// </summary>
    const int MaxShips = 50, MaxSwarm = 80;
    /// <summary>
    /// The score: a tenth of the value of each Errant warship to the seat that brings it down, and
    /// BossPoints more for a boss; for each wave beaten, its number times WavePoints to every seat
    /// still standing, and AheadPoints for each second it was beaten ahead of the next one.
    /// </summary>
    public const int BossPoints = 1000, WavePoints = 100, AheadPoints = 2;
    /// <summary>Waves come in this far inside the edge, and if they can, this far from every station.</summary>
    const double Inset = 260, Clear = 1500;
    /// <summary>Errants look for a foe this close to the team's structures.</summary>
    const double Help = 1000;

    static readonly Side Errants = Faction.Errant;

    public static void Setup(World world)
    {
        AddFaction(world, Faction.Errant, 0).AutoAbilities = true;
        world.Mode.NextWave = Grace;
    }

    /// <summary>Value of a wave's ships, its bosses aside: it grows faster and faster, without end, and with the number of seats.</summary>
    public static double Budget(int wave, int seats)
    {
        var n = wave - 1;
        return (400 + 250 * n + 35 * n * n) * (1 + 0.4 * (seats - 1));
    }

    /// <summary>Seconds from wave `wave` to the next one, if it is not beaten first.</summary>
    public static double Spacing(int wave) => Math.Max(MinInterval, Interval - 2 * (wave - 1));

    /// <summary>Credits each seat still standing gets for beating wave `wave`.</summary>
    public static int Reward(int wave) => 200 + 40 * wave;

    /// <summary>The Errants' research level for wave `wave`: 0 up to the twentieth, then one more every two waves.</summary>
    public static int Elite(int wave) => wave <= EliteFrom ? 0 : (wave - EliteFrom + 1) / 2;

    /// <summary>
    /// The bosses of a wave, every fifth one: a dreadnought, a titan, a leviathan, the colossus;
    /// after it, the heaviest ones together, two, three, then all four.
    /// </summary>
    public static IReadOnlyList<ShipClass> Bosses(int wave)
    {
        if (wave <= 0 || wave % BossEvery != 0) return [];
        var k = wave / BossEvery;
        if (k <= BossClasses.Length) return [BossClasses[k - 1]];
        var n = Math.Min(BossClasses.Length, k - BossClasses.Length + 1);
        return [.. Enumerable.Range(0, n).Select(i => BossClasses[BossClasses.Length - 1 - i])];
    }

    /// <summary>The first boss of a wave, or none.</summary>
    public static ShipClass? Boss(int wave) => Bosses(wave) is [var b, ..] ? b : null;

    static readonly WaveTheme[] Themes = [WaveTheme.Swarm, WaveTheme.Armada, WaveTheme.Carriers];

    /// <summary>One wave in four has a theme from the seventh, boss waves aside: 7, 11, 19, 23, 27...</summary>
    static bool Themed(int wave) => wave >= 7 && wave % 4 == 3 && wave % BossEvery != 0;

    /// <summary>A wave's theme: the themed waves take a swarm, a heavy armada and carriers in turn.</summary>
    public static WaveTheme Theme(int wave)
    {
        if (!Themed(wave)) return WaveTheme.None;
        var k = 0;
        for (var w = 7; w < wave; w++) if (Themed(w)) k++;
        return Themes[k % Themes.Length];
    }

    /// <summary>The classes a wave draws from: bigger hulls join as the waves go on, titans from the sixteenth, leviathans from the twenty-sixth.</summary>
    static ShipClass[] Roster(int wave) => wave switch
    {
        < 3 => [ShipClass.Scout, ShipClass.Bomber],
        < 6 => [ShipClass.Scout, ShipClass.Bomber, ShipClass.Frigate, ShipClass.Torpedo, ShipClass.Support],
        < 9 => [ShipClass.Bomber, ShipClass.Frigate, ShipClass.Torpedo, ShipClass.Support, ShipClass.Battlecruiser, ShipClass.Bulwark],
        < 16 => [ShipClass.Bomber, ShipClass.Frigate, ShipClass.Torpedo, ShipClass.Battlecruiser, ShipClass.Bulwark, ShipClass.Carrier, ShipClass.Dreadnought],
        < 26 => [ShipClass.Bomber, ShipClass.Frigate, ShipClass.Torpedo, ShipClass.Battlecruiser, ShipClass.Bulwark, ShipClass.Carrier, ShipClass.Dreadnought, ShipClass.Titan],
        _ => [ShipClass.Frigate, ShipClass.Torpedo, ShipClass.Battlecruiser, ShipClass.Bulwark, ShipClass.Carrier, ShipClass.Dreadnought, ShipClass.Titan, ShipClass.Leviathan],
    };

    /// <summary>A themed wave's roster: small craft only, the wave's big hulls only, or carriers and bombers.</summary>
    static ShipClass[] Roster(int wave, WaveTheme theme) => theme switch
    {
        WaveTheme.Swarm => [ShipClass.Scout, ShipClass.Fighter, ShipClass.Bomber],
        WaveTheme.Armada => [.. Roster(wave).Where(c => Ships.Defs[c].Size == SizeClass.Large)],
        WaveTheme.Carriers => [ShipClass.Carrier, ShipClass.Bomber],
        _ => Roster(wave),
    };

    static int Seats(World world) => world.Factions.Values.Count(f => f.Race != Faction.Errant);

    /// <summary>
    /// A wave's ships, drawn at random from its roster until its budget is spent or its cap is
    /// reached, its bosses included; a swarm has a quarter more to spend.
    /// </summary>
    public static ShipCounts Compose(World world, int wave)
    {
        var theme = Theme(wave);
        var budget = Budget(wave, Seats(world)) * (theme == WaveTheme.Swarm ? 1.25 : 1);
        var cap = theme == WaveTheme.Swarm ? MaxSwarm : MaxShips;
        var roster = Roster(wave, theme);
        // A theme with nothing it can afford yet falls back on the wave's roster.
        if (!roster.Any(c => Ships.Defs[c].Cost <= budget)) roster = Roster(wave);
        var floor = budget / (2.0 * cap);
        var pool = roster.Where(c => Ships.Defs[c].Cost >= floor).ToArray();
        if (pool.Length == 0) pool = roster;
        var ships = new ShipCounts();
        for (var n = 0; n < cap; n++)
        {
            var fits = pool.Where(c => Ships.Defs[c].Cost <= budget).ToArray();
            if (fits.Length == 0) break;
            var c = fits[(int)Math.Floor(Rand(world) * fits.Length)];
            ships.Increase(c);
            budget -= Ships.Defs[c].Cost;
        }
        foreach (var boss in Bosses(wave)) ships.Increase(boss);
        return ships;
    }

    public static void Update(World world, double dt)
    {
        var m = world.Mode;
        if (world.Time >= m.NextWave) Send(world);
        // Once a second, idle Errants set off for the nearest structure of the team.
        else if (Math.Floor(world.Time) != Math.Floor(world.Time - dt)) Steer(world);
        if (m.Wave > m.Cleared && !AnyLeft(world)) Beaten(world);
    }

    /// <summary>A seat's tally, made when it first scores.</summary>
    public static SurvivalTally TallyOf(World world, Side side)
    {
        var tallies = world.Mode.Tallies;
        if (!tallies.TryGetValue(side, out var t)) tallies[side] = t = new SurvivalTally();
        return t;
    }

    /// <summary>The team's score: every seat's together.</summary>
    public static int TeamScore(World world) => world.Mode.Tallies.Where(t => t.Key != Errants).Sum(t => t.Value.Score);

    /// <summary>
    /// The last wave sent is beaten: each seat still standing is paid and scores, and the next
    /// wave comes after a breather if it was due later, the seconds saved counting too.
    /// </summary>
    static void Beaten(World world)
    {
        var m = world.Mode;
        m.Cleared = m.Wave;
        var ahead = Math.Max(0, m.NextWave - (world.Time + Breather));
        if (ahead > 0) m.NextWave = world.Time + Breather;
        var points = WavePoints * m.Cleared + (int)Math.Round(AheadPoints * ahead);
        var reward = Reward(m.Cleared);
        foreach (var f in world.Factions.Values)
        {
            if (f.Id == Errants || f.StationId is null) continue;
            f.Credits += reward;
            f.Stats.Earned += reward;
            TallyOf(world, f.Id).Score += points;
        }
        world.Events.Add(new WaveCleared(m.Cleared, (int)Math.Round(ahead)));
    }

    /// <summary>
    /// An Errant warship down: a tenth of its value to the seat that brought it down, and a boss's
    /// points if it was one; a boss lost to no seat is just gone.
    /// </summary>
    public static void Downed(World world, Entity e)
    {
        if (e.Side != Errants || e.Kind != EntityKind.Ship) return;
        var boss = world.Mode.BossIds.Remove(e.Id);
        if (e.Launched || e.LastHitBy is not Side by || by == Errants || !world.Factions.ContainsKey(by)) return;
        var t = TallyOf(world, by);
        t.Downed++;
        t.Score += (int)Math.Round(Ships.UnitValue(e.Cls) / 10);
        if (!boss) return;
        t.Bosses++;
        t.Score += BossPoints;
    }

    /// <summary>Errant warships still flying; hangar craft left behind do not hold a wave up.</summary>
    static bool AnyLeft(World world)
    {
        foreach (var e in world.Entities) if (e.Kind == EntityKind.Ship && e.Side == Errants && !e.Launched) return true;
        return false;
    }

    static void Send(World world)
    {
        var m = world.Mode;
        m.Wave++;
        m.NextWave = world.Time + Spacing(m.Wave);
        // Elite Errants: what the new wave brings in is built with their research of the moment
        // (hull and shields); their weapons hit harder at once, the ships already out included.
        if (world.Factions.TryGetValue(Errants, out var errants))
        {
            var level = Elite(m.Wave);
            errants.Research[TechId.Weapons] = errants.Research[TechId.Armor] = errants.Research[TechId.Shields] = level;
        }
        var at = Entry(world);
        var first = world.NextId;
        Battle.PlaceShips(world, new BattleSide { Side = Errants, Ships = Compose(world, m.Wave) }, at.X, at.Y,
            Math.Atan2(world.Height / 2 - at.Y, world.Width / 2 - at.X));
        // The wave's bosses, as many of each class as it has: a roster ship of the same class is as good a boss.
        var bosses = Bosses(m.Wave).GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());
        foreach (var e in world.Entities)
        {
            if (e.Id < first || e.Kind != EntityKind.Ship) continue;
            // A big wave's formation may spill over the edge: it comes in from just inside.
            e.X = e.GuardX = Math.Clamp(e.X, 60, world.Width - 60);
            e.Y = e.GuardY = Math.Clamp(e.Y, 60, world.Height - 60);
            if (bosses.TryGetValue(e.Cls, out var left) && left > 0)
            {
                bosses[e.Cls] = left - 1;
                m.BossIds.Add(e.Id);
            }
        }
        world.Events.Add(new WaveIncoming(m.Wave, at.X, at.Y, Boss(m.Wave)));
        Steer(world);
    }

    /// <summary>A point along one of the edges, drawn at random until one lies well away from every station (or the farthest of a few).</summary>
    static Point Entry(World world)
    {
        var (w, h) = (world.Width, world.Height);
        var stations = world.Entities.Where(e => e.Type == "station").ToList();
        var best = new Point(Inset, Inset);
        var bestD = -1.0;
        for (var i = 0; i < 8 && bestD < Clear; i++)
        {
            var edge = (int)Math.Floor(Rand(world) * 4);
            var t = Rand(world);
            var p = edge switch
            {
                0 => new Point(Inset + t * (w - 2 * Inset), Inset),
                1 => new Point(w - Inset, Inset + t * (h - 2 * Inset)),
                2 => new Point(Inset + t * (w - 2 * Inset), h - Inset),
                _ => new Point(Inset, Inset + t * (h - 2 * Inset)),
            };
            var d = stations.Count == 0 ? Clear : stations.Min(s => Dist(s, p));
            if (d > bestD) (best, bestD) = (p, d);
        }
        return best;
    }

    /// <summary>
    /// Idle Errants attack-move to the structure of the team nearest to each; those going for
    /// the same one fly together.
    /// </summary>
    static void Steer(World world)
    {
        var targets = new List<Entity>();
        foreach (var e in world.Entities) if (e.Kind == EntityKind.Structure && Foes(world, Errants, e.Side)) targets.Add(e);
        if (targets.Count == 0) return;
        var groups = new Dictionary<int, List<int>>();
        foreach (var e in world.Entities)
        {
            if (e.Kind != EntityKind.Ship || e.Side != Errants || e.Order is not null) continue;
            // Hangar craft fly with their carrier while it lives.
            if (Movement.CarrierOf(world, e) is not null) continue;
            var t = targets.MinBy(s => Dist(s, e))!;
            if (!groups.TryGetValue(t.Id, out var g)) groups[t.Id] = g = [];
            g.Add(e.Id);
        }
        foreach (var (id, ships) in groups)
        {
            var t = world.Entities.Get(id)!;
            Movement.IssueMove(world, ships, t.X, t.Y, true);
        }
    }

    /// <summary>There is no beating them: the Errants win once no seat has a station, and the team's time is up.</summary>
    public static bool Decide(World world)
    {
        bool had = false, standing = false;
        foreach (var f in world.Factions.Values)
        {
            if (f.Id == Errants) continue;
            had |= f.HadStation;
            standing |= f.StationId is not null;
        }
        if (!standing && had)
        {
            world.Mode.Held = world.Time;
            Modes.Win(world, Errants);
        }
        return true;
    }

    /// <summary>
    /// The AI defends the team: idle warships go for the foe nearest its station among those
    /// near any structure of the team, its allies' too.
    /// </summary>
    public static void Think(World world, FactionState f)
    {
        if (StationOf(world, f.Id) is not { } station) return;
        var ours = new List<Entity>();
        foreach (var e in world.Entities) if (e.Kind == EntityKind.Structure && Allies(world, e.Side, f.Id)) ours.Add(e);
        Entity? threat = null;
        foreach (var e in world.Entities)
        {
            if (e.Kind != EntityKind.Ship || !Foes(world, f.Id, e.Side) || !Vision.IsVisible(world, f.Id, e)) continue;
            if (threat is not null && Dist(e, station) >= Dist(threat, station)) continue;
            if (ours.Exists(s => Dist(s, e) < Help)) threat = e;
        }
        if (threat is null) return;
        var idle = Modes.Idle(world, f);
        if (idle.Count > 0) Movement.IssueMove(world, idle, threat.X, threat.Y, true);
    }
}
