// Zone control: three beacons, the map center and two points mirrored about it. Ships of a
// single camp around a beacon take it in a few seconds (first undoing a foe's hold); ships of
// two camps freeze it. Each beacon held scores for its holder's camp; the first camp to the
// target wins, and destroying every foe station still wins too.
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim;

public static class ZoneControl
{
    /// <summary>How close ships must be to count.</summary>
    public const double Radius = 320;
    /// <summary>Seconds to take a free beacon, or to undo a foe's hold.</summary>
    public const double CaptureTime = 12;
    /// <summary>Points a beacon held gives each second: a camp holding one alone wins 100 in under 17 min.</summary>
    public const double PointsPerSecond = 0.1;
    /// <summary>The beacons off center sit this share of the map's size from it.</summary>
    const double Reach = 0.3;

    public static void Setup(World world, int target)
    {
        var m = world.Mode;
        m.ZoneTarget = target;
        m.Beacons = Place(world);
        foreach (var f in world.Factions.Values) m.Scores[f.Id] = 0;
    }

    /// <summary>The mirrored pair, turned to lie as far from the nearest station as can be, around the center.</summary>
    static List<Beacon> Place(World world)
    {
        var (cx, cy) = (world.Width / 2, world.Height / 2);
        var stations = world.Entities.Where(e => e.Type == "station").ToList();
        (double X, double Y) best = (0, Reach * world.Height);
        var bestD = -1.0;
        // Half a turn: the other half gives the same pairs.
        for (var i = 0; i < 16; i++)
        {
            var a = Math.PI / 2 + i * Math.PI / 16;
            var (dx, dy) = (Math.Cos(a) * Reach * world.Width, Math.Sin(a) * Reach * world.Height);
            var d = stations.Count == 0 ? 0 : stations.Min(s => Math.Min(Hypot(s.X - cx - dx, s.Y - cy - dy), Hypot(s.X - cx + dx, s.Y - cy + dy)));
            if (d > bestD + 1e-6) (best, bestD) = ((dx, dy), d);
        }
        // A is the upper one (or the left one), C the other.
        if (best.Y > 0 || (best.Y == 0 && best.X > 0)) best = (-best.X, -best.Y);
        return [new Beacon(cx + best.X, cy + best.Y), new Beacon(cx, cy), new Beacon(cx - best.X, cy - best.Y)];
    }

    /// <summary>Whether a camp still holds a station: one without cannot score any more.</summary>
    static bool Standing(World world, Side side)
    {
        foreach (var f in world.Factions.Values) if (f.StationId is not null && Allies(world, f.Id, side)) return true;
        return false;
    }

    public static void Update(World world, double dt)
    {
        var m = world.Mode;
        for (var i = 0; i < m.Beacons.Count; i++)
        {
            var b = m.Beacons[i];
            Side? camp = null;
            var contested = false;
            foreach (var e in world.Entities)
            {
                // Hangar craft come with their carrier; the Errants want none of it.
                if (e.Kind != EntityKind.Ship || e.Launched || e.Race == Data.Faction.Errant || Dist(e, b) > Radius) continue;
                if (camp is not Side c) camp = e.Side;
                else if (!Allies(world, c, e.Side)) contested = true;
            }
            b.Contested = contested;
            if (contested || camp is not Side by) continue;
            var step = dt / CaptureTime;
            if (b.Claim is Side claim && !Allies(world, claim, by))
            {
                // A foe's claim or hold comes undone first.
                b.Progress -= step;
                if (b.Progress > 0) continue;
                b.Progress = 0;
                b.Claim = null;
                if (b.Holder is Side lost) world.Events.Add(new BeaconLost(i, lost));
                b.Holder = null;
            }
            else
            {
                b.Claim ??= by;
                b.Progress = Math.Min(1, b.Progress + step);
                if (b.Progress < 1 || b.Holder is not null) continue;
                b.Holder = b.Claim;
                world.Events.Add(new BeaconTaken(i, b.Claim.Value));
            }
        }
        foreach (var b in m.Beacons)
        {
            if (b.Holder is Side h && Standing(world, h)) m.Scores[h] = m.Scores.GetValueOrDefault(h) + PointsPerSecond * dt;
        }
    }

    /// <summary>A camp's score: the sum of its sides'.</summary>
    public static double Score(World world, Side side)
    {
        double sum = 0;
        foreach (var (s, points) in world.Mode.Scores) if (Allies(world, s, side)) sum += points;
        return sum;
    }

    /// <summary>The first side to have joined of a camp at the target wins with it; otherwise the last station standing still may.</summary>
    public static bool Decide(World world)
    {
        foreach (var f in world.Factions.Values)
        {
            if (f.StationId is null || Score(world, f.Id) < world.Mode.ZoneTarget) continue;
            Modes.Win(world, f.Id);
            return true;
        }
        return false;
    }

    /// <summary>
    /// The AI sends its idle warships (those not on a beacon already) to the nearest beacon its
    /// camp does not hold, or else to one of its own under contest.
    /// </summary>
    public static void Think(World world, FactionState f)
    {
        if (StationOf(world, f.Id) is not { } station) return;
        var beacons = world.Mode.Beacons;
        var target = beacons.Where(b => b.Holder is not Side h || !Allies(world, h, f.Id)).MinBy(b => Dist(b, station))
            ?? beacons.Where(b => b.Contested).MinBy(b => Dist(b, station));
        if (target is null) return;
        var idle = Modes.Idle(world, f, e => !beacons.Exists(b => Dist(b, e) <= Radius));
        if (idle.Count > 0) Movement.IssueMove(world, idle, target.X, target.Y, true);
    }
}
