// Assault: team 1 holds a fortress (station level 4, three platforms at their top tier, an ion
// cannon, a modest fleet, the income of its station); team 2 comes with a large fleet and a
// strong income, from a light beachhead (a level 3 station, nothing around it). The defenders
// win if one of their stations stands when the clock runs out; the attackers by destroying
// them all. Losing every station loses, for either side, as in the standard rules.
using SandboxShip.Sim.Data;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim;

public static class Assault
{
    public const int Defenders = 1, Attackers = 2;
    /// <summary>Credits per second each attacker gets on top of its station and mines, while its station stands.</summary>
    public const double AttackerIncome = 30;
    /// <summary>A beachhead station that can build the big hulls its income pays for, but has no defenses around it.</summary>
    const int BeachheadLevel = 3;
    /// <summary>The attacking AI sends a wave once this much of its fleet waits, whatever it thinks of the odds.</summary>
    const double WaveSize = 5000;
    /// <summary>Seconds before the end when the attacking AI throws in everything it has.</summary>
    const double AllIn = 180;

    static readonly (ShipClass Cls, int Count)[] Garrison =
        [(ShipClass.Frigate, 4), (ShipClass.Torpedo, 2), (ShipClass.Battlecruiser, 2), (ShipClass.Bulwark, 1)];
    // The opening clash mostly decides an assault between AIs, and it tips sharply: with 6
    // frigates here (12 700 credits), the defense won 79 games of 108; with 8 (14 000), 58; with
    // 6 frigates and a third battlecruiser, 42; with a fourth, 14.
    static readonly (ShipClass Cls, int Count)[] Armada =
    [
        (ShipClass.Frigate, 8), (ShipClass.Torpedo, 4), (ShipClass.Bomber, 4), (ShipClass.Support, 2),
        (ShipClass.Battlecruiser, 2),
    ];

    public static int StartLevel(Slot s) => s.Team == Defenders ? Station.Levels.Count : BeachheadLevel;

    /// <summary>The defenders down the left of the map, the attackers down the right, each in seat order.</summary>
    public static List<Point> Spots(World world, IReadOnlyList<Slot> slots)
    {
        int defenders = slots.Count(s => s.Team == Defenders), attackers = slots.Count - defenders;
        int d = 0, a = 0;
        return slots.Select(s => s.Team == Defenders
            ? new Point(world.Width * 0.15, world.Height * ++d / (defenders + 1))
            : new Point(world.Width * 0.88, world.Height * ++a / (attackers + 1))).ToList();
    }

    public static void Setup(World world, int minutes)
    {
        world.Mode.Deadline = minutes * 60;
        foreach (var f in world.Factions.Values)
        {
            if (StationOf(world, f.Id) is not { } station) continue;
            var toward = Math.Atan2(world.Height / 2 - station.Y, world.Width / 2 - station.X);
            if (f.Team == Defenders) Fortify(world, f.Id, station, toward);
            var fleet = f.Team == Defenders ? Garrison : Armada;
            var ships = new ShipCounts();
            foreach (var (cls, n) in fleet) ships.Increase(cls, n);
            Battle.PlaceShips(world, new BattleSide { Side = f.Id, Ships = ships },
                station.X + Math.Cos(toward) * 600, station.Y + Math.Sin(toward) * 600, toward);
        }
    }

    /// <summary>Three platforms at their top tier in an arc before the station, an ion cannon on a flank.</summary>
    static void Fortify(World world, Side side, Entity station, double toward)
    {
        foreach (var turn in new[] { -0.7, 0, 0.7 })
        {
            var a = toward + turn;
            var p = SpawnStructure(world, side, BuildableType.Platform, station.X + Math.Cos(a) * 330, station.Y + Math.Sin(a) * 330);
            while (p.Level <= Buildables.Defs[BuildableType.Platform].Tiers!.Count) Structures.ApplyTier(world, p);
            p.Shield = p.MaxShield;
        }
        var flank = toward + 1.5;
        SpawnStructure(world, side, BuildableType.IonCannon, station.X + Math.Cos(flank) * 330, station.Y + Math.Sin(flank) * 330);
    }

    public static void Update(World world, double dt)
    {
        foreach (var f in world.Factions.Values)
        {
            if (f.Team != Attackers || f.StationId is null) continue;
            f.Credits += AttackerIncome * dt;
            f.Stats.Earned += AttackerIncome * dt;
        }
        var left = world.Mode.Deadline - world.Time;
        foreach (var warn in new[] { 300.0, 60.0 })
        {
            if (left <= warn && left + dt > warn) world.Events.Add(new ClockWarning(warn));
        }
    }

    /// <summary>Time up: the first defender still holding a station wins with its team. Before, the standard rules hold.</summary>
    public static bool Decide(World world)
    {
        if (world.Time < world.Mode.Deadline) return false;
        var holder = world.Factions.Values.FirstOrDefault(f => f.Team == Defenders && f.StationId is not null);
        if (holder is null) return false;
        Modes.Win(world, holder.Id);
        return true;
    }

    /// <summary>
    /// The defending AI holds (it sends no waves, see Modes.WaveTarget). The attacking AI does not
    /// wait for the odds: a wave at the nearest fortress as soon as enough of its fleet is ready,
    /// and near the end, every ready warship.
    /// </summary>
    public static void Think(World world, FactionState f)
    {
        if (f.Team != Attackers) return;
        // Ready: every warship at home, idle or still on its way to the rally point, as for the
        // all-in past Ai.AllInTime, which an assault never reaches. Big hulls jostling there may
        // never quite get to their spot: waited for as idle, they could sit at home for the rest
        // of the battle, clock and all. Not one holding a spot for a mine.
        var ready = Ai.Warships(world, f).Where(e => e.Order?.Type != OrderType.AttackMove && e.Stance != Stance.Hold && !Modes.Kept(world, e)).ToList();
        if (ready.Count == 0) return;
        if (ready.Sum(e => Ships.UnitValue(e.Cls)) < WaveSize && world.Mode.Deadline - world.Time > AllIn) return;
        var fortress = world.Factions.Values.Where(d => d.Team == Defenders).Select(d => StationOf(world, d.Id)).OfType<Entity>().MinBy(s => Dist(s, ready[0]));
        if (fortress is not null) Ai.Send(world, ready, fortress, Stance.Aggressive);
    }
}
