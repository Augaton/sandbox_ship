// Survival: every seat together, on one team, against waves of Errants coming from the edges of
// the map, bigger and heavier each time, a boss every fifth one. Twenty waves beaten win; no
// station left loses.
using SandboxShip.Sim.Data;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim;

public static class Survival
{
    /// <summary>The last wave: beating it wins.</summary>
    public const int Waves = 20;
    /// <summary>Seconds before the first wave, then between two.</summary>
    public const double Grace = 150, Interval = 90;
    /// <summary>A boss every this many waves: a dreadnought, a titan, a leviathan, the colossus.</summary>
    public const int BossEvery = 5;
    static readonly ShipClass[] Bosses = [ShipClass.Dreadnought, ShipClass.Titan, ShipClass.Leviathan, ShipClass.Colossus];
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

    /// <summary>Value of a wave's ships, its boss aside: it grows faster and faster, and with the number of seats.</summary>
    public static double Budget(int wave, int seats)
    {
        var n = wave - 1;
        return (400 + 250 * n + 35 * n * n) * (1 + 0.4 * (seats - 1));
    }

    /// <summary>The boss of a wave, every fifth one: the last kind stays once they run out.</summary>
    public static ShipClass? Boss(int wave) => wave % BossEvery == 0 ? Bosses[Math.Min(wave / BossEvery, Bosses.Length) - 1] : null;

    /// <summary>The classes a wave draws from: bigger hulls join as the waves go on.</summary>
    static ShipClass[] Roster(int wave) => wave switch
    {
        < 3 => [ShipClass.Scout, ShipClass.Bomber],
        < 6 => [ShipClass.Scout, ShipClass.Bomber, ShipClass.Frigate, ShipClass.Torpedo, ShipClass.Support],
        < 9 => [ShipClass.Bomber, ShipClass.Frigate, ShipClass.Torpedo, ShipClass.Support, ShipClass.Battlecruiser, ShipClass.Bulwark],
        _ => [ShipClass.Bomber, ShipClass.Frigate, ShipClass.Torpedo, ShipClass.Battlecruiser, ShipClass.Bulwark, ShipClass.Carrier, ShipClass.Dreadnought],
    };

    static int Seats(World world) => world.Factions.Values.Count(f => f.Race != Faction.Errant);

    /// <summary>A wave's ships, drawn at random from its roster until its budget is spent, its boss included.</summary>
    public static ShipCounts Compose(World world, int wave)
    {
        var roster = Roster(wave);
        var budget = Budget(wave, Seats(world));
        var ships = new ShipCounts();
        while (true)
        {
            var fits = roster.Where(c => Ships.Defs[c].Cost <= budget).ToArray();
            if (fits.Length == 0) break;
            var c = fits[(int)Math.Floor(Rand(world) * fits.Length)];
            ships.Increase(c);
            budget -= Ships.Defs[c].Cost;
        }
        if (Boss(wave) is ShipClass boss) ships.Increase(boss);
        return ships;
    }

    public static void Update(World world, double dt)
    {
        var m = world.Mode;
        if (world.Time >= m.NextWave) Send(world);
        // Once a second, idle Errants set off for the nearest structure of the team.
        else if (Math.Floor(world.Time) != Math.Floor(world.Time - dt)) Steer(world);
        if (m.Wave > m.Cleared && !AnyLeft(world))
        {
            m.Cleared = m.Wave;
            world.Events.Add(new WaveCleared(m.Wave));
        }
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
        m.NextWave = m.Wave < Waves ? world.Time + Interval : double.PositiveInfinity;
        var at = Entry(world);
        var first = world.NextId;
        Battle.PlaceShips(world, new BattleSide { Side = Errants, Ships = Compose(world, m.Wave) }, at.X, at.Y,
            Math.Atan2(world.Height / 2 - at.Y, world.Width / 2 - at.X));
        // A big wave's formation may spill over the edge: it comes in from just inside.
        foreach (var e in world.Entities)
        {
            if (e.Id < first || e.Kind != EntityKind.Ship) continue;
            e.X = e.GuardX = Math.Clamp(e.X, 60, world.Width - 60);
            e.Y = e.GuardY = Math.Clamp(e.Y, 60, world.Height - 60);
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

    /// <summary>The team wins once the last wave is beaten; the Errants, once no seat has a station.</summary>
    public static bool Decide(World world)
    {
        FactionState? standing = null;
        var had = false;
        foreach (var f in world.Factions.Values)
        {
            if (f.Id == Errants) continue;
            had |= f.HadStation;
            if (f.StationId is not null) standing ??= f;
        }
        if (standing is null && had) Modes.Win(world, Errants);
        else if (standing is not null && world.Mode.Cleared >= Waves) Modes.Win(world, standing.Id);
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
