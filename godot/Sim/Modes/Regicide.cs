// Regicide: every side starts with a flagship, its race's admiral in front of its station,
// marked and never rebuilt. Losing it loses the battle: everything else of that side blows up
// with it. The last camp with a flagship wins; stations do not decide anything here.
using SandboxShip.Sim.Data;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim;

public static class Regicide
{
    /// <summary>A side's flagship: its race's admiral, the hero it could otherwise build at station level 3.</summary>
    public static HeroId Flagship(Faction race) => new(race, HeroRole.Admiral);

    /// <summary>
    /// The AI keeps its flagship this far from its station, on the flank away from its foes:
    /// under the station's guns, a flagship with its hero's powers made every base out of reach
    /// and AI battles never ended. Its escort stays this close to it.
    /// </summary>
    const double Back = 900, Guard = 350;
    /// <summary>Ships on a wave this close to a foe flagship they see go straight for it.</summary>
    const double Dive = 1600;
    /// <summary>
    /// After this many seconds, the AI goes all in: its escort too, and every wave, at the
    /// nearest foe flagship. A base guarded by its flagship holds against waves of its size, and
    /// without it two AIs would watch each other for an hour.
    /// </summary>
    public const double AllIn = 1200;
    /// <summary>
    /// Sudden death, after this many seconds: flagships lose their shields and nothing mends their
    /// hull any more, so that a battle between flagships that heal faster than they are hurt still ends.
    /// A warning comes two minutes before.
    /// </summary>
    public const double SuddenDeath = 1500, Warning = 120;

    public static void Setup(World world)
    {
        foreach (var f in world.Factions.Values)
        {
            if (f.Race == Faction.Errant || StationOf(world, f.Id) is not { } s) continue;
            var a = Math.Atan2(world.Height / 2 - s.Y, world.Width / 2 - s.X);
            world.Mode.Flagships[f.Id] = SpawnHero(world, f.Id, Flagship(f.Race), s.X + Math.Cos(a) * 330, s.Y + Math.Sin(a) * 330, a).Id;
        }
    }

    /// <summary>
    /// A flagship gone takes its side out: all it owns self-destructs, and no one is paid for
    /// it. Should the last ones fall together, the first of them to have joined wins.
    /// </summary>
    public static void Update(World world, double dt)
    {
        var m = world.Mode;
        foreach (var at in new[] { SuddenDeath - Warning, SuddenDeath })
        {
            if (world.Time >= at && world.Time - dt < at) world.Events.Add(new ClockWarning(SuddenDeath - at));
        }
        // Run last in the step, after every repair and regeneration: what they gave back is taken again.
        if (world.Time >= SuddenDeath)
        {
            foreach (var id in m.Flagships.Values)
            {
                if (world.Entities.Get(id) is not { } e) continue;
                e.Shield = 0;
                e.Hull = m.Worn[id] = Math.Min(e.Hull, m.Worn.GetValueOrDefault(id, e.Hull));
            }
        }
        List<Side>? fallen = null;
        foreach (var (side, id) in m.Flagships) if (!world.Entities.Has(id)) (fallen ??= []).Add(side);
        if (fallen is null) return;
        foreach (var side in fallen)
        {
            var id = m.Flagships[side];
            m.Flagships.Remove(side);
            m.Escorts.Remove(side);
            var wreck = world.Events.FindLast(ev => ev is Destroyed d && d.Id == id) as Destroyed;
            world.Events.Add(new FlagshipLost(side, wreck?.X ?? 0, wreck?.Y ?? 0));
            foreach (var e in world.Entities)
            {
                if (e.Side != side) continue;
                e.Hull = 0;
                e.LastHitBy = null;
            }
        }
        if (m.Flagships.Count == 0 && world.Winner is null) Modes.Win(world, fallen[0]);
    }

    /// <summary>The last camp with a flagship wins, with the first of its sides to have joined.</summary>
    public static bool Decide(World world)
    {
        Side? first = null;
        foreach (var side in world.Mode.Flagships.Keys)
        {
            if (first is not Side f) first = side;
            else if (!Allies(world, f, side)) return true;
        }
        if (first is Side won) Modes.Win(world, won);
        return true;
    }

    /// <summary>The foe flagship nearest to `from`: everyone knows where flagships are.</summary>
    static Entity? NearestFoe(World world, Side side, IPoint from)
    {
        Entity? best = null;
        foreach (var (s, id) in world.Mode.Flagships)
        {
            if (!Foes(world, side, s) || world.Entities.Get(id) is not { } e) continue;
            if (best is null || Dist(e, from) < Dist(best, from)) best = e;
        }
        return best;
    }

    /// <summary>The foe flagship nearest the AI's station (or its flagship, without one): once no foe station is left, its waves go for it.</summary>
    public static Entity? Prey(World world, FactionState f)
    {
        var from = StationOf(world, f.Id) ?? (world.Mode.Flagships.TryGetValue(f.Id, out var id) ? world.Entities.Get(id) : null);
        return from is null ? null : NearestFoe(world, f.Id, from);
    }

    /// <summary>
    /// The AI keeps its flagship back (see Back) and an escort of a quarter of its fleet (2 to 6
    /// ships) at its side, whatever the standard AI wanted of them (Modes.Kept keeps them out of
    /// its waves). Its waves go for foe stations as usual, then, once a foe has none left or the
    /// battle drags on (AllIn), for the nearest foe flagship (Modes.WaveTarget); any wave that
    /// sees a foe flagship close by dives at it, and idle warships nearer a foe flagship than
    /// home keep on hunting. Past AllIn, the escort and every idle warship go too.
    /// </summary>
    public static void Think(World world, FactionState f)
    {
        var m = world.Mode;
        if (!m.Flagships.TryGetValue(f.Id, out var id) || world.Entities.Get(id) is not { } flag) return;
        var station = StationOf(world, f.Id);
        if (station is not null && !flag.Retreating)
        {
            var home = Haven(world, f.Id, station);
            var homeward = flag.Order is { Type: OrderType.Move } o && Hypot(o.X - home.X, o.Y - home.Y) < Guard;
            if (!homeward && (flag.Order is not null || Dist(flag, home) > Guard)) Movement.IssueMove(world, [flag.Id], home.X, home.Y);
        }
        if (!m.Escorts.TryGetValue(f.Id, out var escort)) m.Escorts[f.Id] = escort = [];
        escort.RemoveAll(e => !world.Entities.Has(e));
        var allIn = world.Time >= AllIn;
        if (allIn) escort.Clear();
        var ships = Ai.Warships(world, f).Where(e => e.Id != flag.Id).ToList();
        var quota = allIn ? 0 : Math.Clamp(ships.Count / 4, 2, 6);
        foreach (var e in ships.Where(e => e.Order is null && !escort.Contains(e.Id)).OrderBy(e => Dist(e, flag)))
        {
            if (escort.Count >= quota) break;
            escort.Add(e.Id);
        }
        // Escorts sent elsewhere, or left behind, come back in front of it: a point on a ship would never be reached.
        var front = Math.Atan2(world.Height / 2 - flag.Y, world.Width / 2 - flag.X);
        var (gx, gy) = (flag.X + Math.Cos(front) * 160, flag.Y + Math.Sin(front) * 160);
        var stray = escort.Where(i => world.Entities.Get(i) is { Retreating: false } e
            && (e.Order is { } o ? Hypot(o.X - gx, o.Y - gy) > Guard : Dist(e, flag) > Guard)).ToList();
        if (stray.Count > 0) Movement.IssueMove(world, stray, gx, gy, true);
        foreach (var (side, fid) in m.Flagships)
        {
            if (!Foes(world, f.Id, side) || world.Entities.Get(fid) is not { } foe || !Vision.IsVisible(world, f.Id, foe)) continue;
            // Each on its own: a fleet's pace would hold the first ones back for the last.
            foreach (var e in ships)
            {
                if (e.Order?.Type == OrderType.AttackMove && !escort.Contains(e.Id) && Dist(e, foe) < Dive) Movement.IssueAttack(world, [e.Id], fid);
            }
        }
        if (station is null || Prey(world, f) is not { } prey) return;
        var hunters = ships.Where(e => e.Order is null && !escort.Contains(e.Id) && (allIn || Dist(e, prey) < Dist(e, station))).Select(e => e.Id).ToList();
        if (hunters.Count > 0) Movement.IssueMove(world, hunters, prey.X, prey.Y, true);
    }

    /// <summary>Where the AI keeps its flagship: Back from its station, on the flank (of the two) farther from every foe station.</summary>
    static Point Haven(World world, Side side, Entity station)
    {
        var away = Math.Atan2(station.Y - world.Height / 2, station.X - world.Width / 2);
        var foes = world.Factions.Values.Where(o => Foes(world, o.Id, side)).Select(o => StationOf(world, o.Id)).OfType<Entity>().ToList();
        return new[] { away - 1.1, away + 1.1 }
            .Select(a => new Point(Math.Clamp(station.X + Math.Cos(a) * Back, 250, world.Width - 250), Math.Clamp(station.Y + Math.Sin(a) * Back, 250, world.Height - 250)))
            .MaxBy(p => foes.Count == 0 ? 0 : foes.Min(s => Dist(s, p)));
    }
}
