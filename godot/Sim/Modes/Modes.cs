// Battle modes beyond the standard one (not in the web version): what each keeps track of,
// what it tells the view, and the hooks the simulation calls. Each mode's rules are in a file
// of their own; in the standard mode every hook does nothing.
using SandboxShip.Sim.Data;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim;

/// <summary>A beacon of the zone control mode: who holds it, and who is taking it.</summary>
public sealed class Beacon(double x, double y) : IPoint
{
    public double X { get; set; } = x;
    public double Y { get; set; } = y;
    /// <summary>Whose it is: it scores for that side's camp. Null: nobody's yet, or no longer.</summary>
    public Side? Holder;
    /// <summary>The side whose ships are taking it, or hold it, and how far they are, 0 to 1.</summary>
    public Side? Claim;
    public double Progress;
    /// <summary>Ships of two camps or more around it: nothing moves until one is left.</summary>
    public bool Contested;
}

/// <summary>
/// What the battle mode keeps track of, as plain data that a network snapshot can carry; the
/// fields of the other modes keep their defaults.
/// </summary>
public sealed class ModeState
{
    public BattleMode Kind;
    /// <summary>Survival: waves sent so far, and the last one beaten (every Errant down).</summary>
    public int Wave, Cleared;
    /// <summary>Survival: world time the next wave comes; never, after the last one.</summary>
    public double NextWave = double.PositiveInfinity;
    /// <summary>Zones: the score that wins.</summary>
    public int ZoneTarget;
    public List<Beacon> Beacons = [];
    /// <summary>Zones: points of each side; a team's score is the sum of its sides'.</summary>
    public Dictionary<Side, double> Scores = [];
    /// <summary>Assault: world time the defenders must hold out until.</summary>
    public double Deadline = double.PositiveInfinity;
    /// <summary>Regicide: each side's flagship still flying, by entity id.</summary>
    public Dictionary<Side, int> Flagships = [];
    /// <summary>Regicide: the ships each AI keeps around its flagship (the AI's own business, not the view's).</summary>
    public Dictionary<Side, List<int>> Escorts = [];
}

/// <summary>Survival: wave `Wave` comes in at (X, Y), with a boss of that class or none.</summary>
public sealed record WaveIncoming(int Wave, double X, double Y, ShipClass? Boss) : SimEvent;
/// <summary>Survival: no Errant left, up to wave `Wave`.</summary>
public sealed record WaveCleared(int Wave) : SimEvent;
/// <summary>Zones: beacon number `Beacon` is `Side`'s now.</summary>
public sealed record BeaconTaken(int Beacon, Side Side) : SimEvent;
/// <summary>Zones: `Side` lost beacon number `Beacon`, which is nobody's now.</summary>
public sealed record BeaconLost(int Beacon, Side Side) : SimEvent;
/// <summary>Regicide: `Side`'s flagship fell at (X, Y); everything of that side blows up with it.</summary>
public sealed record FlagshipLost(Side Side, double X, double Y) : SimEvent;
/// <summary>Assault: `Left` seconds before the defenders have held.</summary>
public sealed record ClockWarning(double Left) : SimEvent;

public static class Modes
{
    /// <summary>
    /// The raid's colossus only in the modes it does not upset: it would sit on a beacon, in
    /// the attackers' way, or among the waves.
    /// </summary>
    public static bool AllowsRaid(BattleMode mode) => mode is BattleMode.Standard or BattleMode.Regicide;

    /// <summary>The level a seat's station starts at.</summary>
    public static int StartLevel(SkirmishOptions o, Slot s) => o.Mode == BattleMode.Assault ? Assault.StartLevel(s) : 1;

    /// <summary>Last of a skirmish's making, once stations, labs and raid are in place.</summary>
    public static void Setup(World world, SkirmishOptions o)
    {
        world.Mode.Kind = o.Mode;
        switch (o.Mode)
        {
            case BattleMode.Survival: Survival.Setup(world); break;
            case BattleMode.Zones: ZoneControl.Setup(world, o.ZoneTarget); break;
            case BattleMode.Assault: Assault.Setup(world, o.AssaultMinutes); break;
            case BattleMode.Regicide: Regicide.Setup(world); break;
        }
    }

    /// <summary>Every step, once the dead are removed and before the victory check; nothing more once the battle is decided.</summary>
    public static void Update(World world, double dt)
    {
        if (world.Winner is not null) return;
        switch (world.Mode.Kind)
        {
            case BattleMode.Survival: Survival.Update(world, dt); break;
            case BattleMode.Zones: ZoneControl.Update(world, dt); break;
            case BattleMode.Assault: Assault.Update(world, dt); break;
            case BattleMode.Regicide: Regicide.Update(world); break;
        }
    }

    /// <summary>
    /// Asked first by the victory check: true when the mode decided the battle, or has rules
    /// of its own in place of the last station standing (survival, regicide).
    /// </summary>
    public static bool Decide(World world) => world.Mode.Kind switch
    {
        BattleMode.Survival => Survival.Decide(world),
        BattleMode.Zones => ZoneControl.Decide(world),
        BattleMode.Assault => Assault.Decide(world),
        BattleMode.Regicide => Regicide.Decide(world),
        _ => false,
    };

    /// <summary>
    /// Whether a side and all its allies are out of the battle, before it is over: no station
    /// left among them, or in regicide no flagship.
    /// </summary>
    public static bool Out(World world, Side side)
    {
        if (world.Mode.Kind == BattleMode.Regicide) return !world.Mode.Flagships.Keys.Any(s => Allies(world, s, side));
        foreach (var f in world.Factions.Values) if (f.StationId is not null && Allies(world, f.Id, side)) return false;
        return true;
    }

    internal static void Win(World world, Side side)
    {
        world.Winner = side;
        world.Events.Add(new Victory(side, TeamOf(world, side)));
    }

    /// <summary>
    /// What the skirmish AI sends its waves at, given the nearest foe station: none for assault
    /// defenders, who hold; in regicide, the nearest foe flagship once no foe station is left or
    /// the battle drags on.
    /// </summary>
    public static Entity? WaveTarget(World world, FactionState f, Entity? enemy) => world.Mode.Kind switch
    {
        BattleMode.Regicide => world.Time >= Regicide.AllIn ? Regicide.Prey(world, f) ?? enemy : enemy ?? Regicide.Prey(world, f),
        BattleMode.Assault when f.Team == Assault.Defenders => null,
        _ => enemy,
    };

    /// <summary>Ships the mode's AI keeps for itself, out of the standard AI's errands (waves, escorts): a flagship and its escort.</summary>
    public static bool Kept(World world, Entity e)
    {
        var m = world.Mode;
        return m.Kind == BattleMode.Regicide
            && ((m.Flagships.TryGetValue(e.Side, out var id) && id == e.Id) || (m.Escorts.TryGetValue(e.Side, out var escort) && escort.Contains(e.Id)));
    }

    /// <summary>After each of its thoughts, what a skirmish AI does for the mode.</summary>
    public static void Think(World world, FactionState f)
    {
        switch (world.Mode.Kind)
        {
            case BattleMode.Survival: Survival.Think(world, f); break;
            case BattleMode.Zones: ZoneControl.Think(world, f); break;
            case BattleMode.Assault: Assault.Think(world, f); break;
            case BattleMode.Regicide: Regicide.Think(world, f); break;
        }
    }

    /// <summary>The idle warships an AI may send for the mode.</summary>
    internal static List<int> Idle(World world, FactionState f, Func<Entity, bool>? keep = null) =>
        Ai.Warships(world, f).Where(e => e.Order is null && (keep is null || keep(e))).Select(e => e.Id).ToList();
}
