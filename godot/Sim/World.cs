// The battle state and its building blocks. Port of src/sim/world.ts: same fields, same
// rules, same order of iteration, so that a seed plays out the same way in both versions.
using SandboxShip.Sim.Data;

namespace SandboxShip.Sim;

public enum EntityKind { Ship, Structure, Hardpoint }

public enum OrderType { Move, AttackMove, Attack }

/// <summary>An order; `Group` is set when several ships got it together, to move as one fleet.</summary>
public sealed record Order(OrderType Type, double X = 0, double Y = 0, int TargetId = 0, MoveGroup? Group = null)
{
    public static Order Move(double x, double y, MoveGroup? group = null) => new(OrderType.Move, x, y, Group: group);
    public static Order AttackMove(double x, double y, MoveGroup? group = null) => new(OrderType.AttackMove, x, y, Group: group);
    public static Order Attack(int targetId, MoveGroup? group = null) => new(OrderType.Attack, TargetId: targetId, Group: group);
}

/// <summary>
/// Ships ordered together travel together: at the pace of the slowest, those left behind
/// catching up at full speed. Not in the web version.
/// </summary>
public sealed class MoveGroup(double pace, double dirX, double dirY)
{
    /// <summary>Cruise speed of the slowest ship.</summary>
    public readonly double Pace = pace;
    /// <summary>Direction of travel, for moves: progress is measured along it.</summary>
    public readonly double DirX = dirX, DirY = dirY;
    /// <summary>Least distance any member has left, as of World.Time == At.</summary>
    public double Lead;
    public double At = double.NaN;
}

public interface IPoint
{
    double X { get; }
    double Y { get; }
}

public readonly record struct Point(double X, double Y) : IPoint;

public sealed class WeaponState(WeaponDef def)
{
    public WeaponDef Def { get; } = def;
    public double Cooldown = def.Cooldown;
}

public sealed class HangarState(HangarDef def)
{
    public HangarDef Def = def;
    public double Timer = 2;
    /// <summary>Index of the next squadron to launch in Def.Squadrons.</summary>
    public int Next;
    public readonly List<int> Launched = [];
}

public sealed class AbilityState(AbilityId id)
{
    public AbilityId Id = id;
    /// <summary>Seconds until it can be used again.</summary>
    public double Cooldown;
    /// <summary>Seconds the effect still lasts.</summary>
    public double Active;
}

/// <summary>What abilities leave on their targets: seconds left of each.</summary>
public sealed class Effects
{
    /// <summary>Takes more damage.</summary>
    public double Marked;
    /// <summary>Cannot move, fire or recharge its shields.</summary>
    public double Stunned;
    /// <summary>Loses hull every second, shields or not.</summary>
    public double Corroded;
    /// <summary>Can only target TauntedBy (not in the web version).</summary>
    public double Taunted;
    public int? TauntedBy;
}

public enum ZoneKind { Command, Dome, Graft }

/// <summary>Areas that help a faction's units inside them, rebuilt every step.</summary>
public sealed record Zone(ZoneKind Kind, Faction Faction, double X, double Y, double R) : IPoint;

/// <summary>`Stellar`: the star lance's blow, a blast as an orbital strike's, far bigger.</summary>
public enum StrikeKind { Orbital, Beam, Stellar }

/// <summary>A blow that lands after a delay: an orbital strike, or a spinal beam charging up.</summary>
public sealed class Strike
{
    public StrikeKind Kind;
    public Faction Faction;
    /// <summary>Center of the blast, or start of the beam.</summary>
    public double X, Y;
    /// <summary>End of the beam; the center again for a blast.</summary>
    public double X2, Y2;
    /// <summary>Blast radius, or half the beam's width.</summary>
    public double Radius;
    public double Damage;
    /// <summary>Seconds before it lands, out of Total.</summary>
    public double Delay, Total;
}

/// <summary>Controls of a ship flown by hand.</summary>
public sealed class Pilot
{
    /// <summary>-1 (reverse) to 1 (full ahead).</summary>
    public double Thrust;
    /// <summary>-1 (port) to 1 (starboard).</summary>
    public double Turn;
    public bool Firing;
    public double AimX, AimY;
}

public sealed record Rebuild(HardpointRole Role, double X, double Y)
{
    public double Timer = Buildables.RebuildTime;
}

public sealed class Entity : IPoint
{
    public int Id;
    public EntityKind Kind;
    /// <summary>The web version's name of the ship class, structure or part: "scout", "station", "heavyTurret".</summary>
    public string Type = "";
    public Faction Faction;
    public double X { get; set; }
    public double Y { get; set; }
    public double Angle, Speed, Radius = 10;
    public SizeClass Size = SizeClass.Structure;
    public double Hull = 1, MaxHull = 1, Shield, MaxShield, ShieldRegen;
    public List<WeaponState> Weapons = [];
    public double Range;
    public int? TargetId;
    public double Retarget;
    public Order? Order;
    /// <summary>Where an idle ship holds position and returns after a chase.</summary>
    public double GuardX, GuardY;
    /// <summary>0..1; a structure only works once fully built.</summary>
    public double Build = 1;
    public double BuildTime;
    public int Level;
    public int? ParentId;
    public HangarState? Hangar;
    public double Income;
    public int? FieldId;
    /// <summary>For hardpoints: offset from the parent, in the parent's frame (x forward).</summary>
    public double Ox, Oy;
    /// <summary>Engine hardpoints still working, out of MaxEngines; null without engine hardpoints.</summary>
    public int? Engines;
    public int MaxEngines;
    /// <summary>Shield generators still standing; null when the shields need none.</summary>
    public int? ShieldGens;
    /// <summary>Set while the player flies this ship by hand.</summary>
    public Pilot? Pilot;
    /// <summary>Its abilities, its class's own first; big ships have several (not in the web version).</summary>
    public List<AbilityState> Abilities = [];
    /// <summary>The first ability: the web version had a single one.</summary>
    public AbilityState? Ability => Abilities.Count > 0 ? Abilities[0] : null;
    public HeroId? Hero;
    /// <summary>Launched by a hangar: free craft that never leave the battle.</summary>
    public bool Launched;
    /// <summary>
    /// For launched craft: the ship or structure they fly for, and their place beside it, in
    /// its frame (x forward). They escort it and go for what it fights. Not in the web version.
    /// </summary>
    public int? CarrierId;
    public double SlotX, SlotY;
    /// <summary>An AI ship falling back to be repaired.</summary>
    public bool Retreating;
    public Effects Effects = new();
    /// <summary>Seconds before a shield knocked down to nothing starts to come back (not in the web version).</summary>
    public double ShieldDown;
    /// <summary>Seconds left for what an ability summoned (swarm craft, field turrets); null if it stays.</summary>
    public double? Expires;
    /// <summary>Faction that hit it last: the kill is theirs.</summary>
    public Faction? LastHitBy;
    /// <summary>World time of the last hit it took.</summary>
    public double HitAt = double.NegativeInfinity;
    /// <summary>Station parts lost and waiting to be rebuilt.</summary>
    public List<Rebuild> Rebuild = [];

    /// <summary>The ship class, for a ship. Looked up by name once: steering and repairs ask for it every step.</summary>
    public ShipClass Cls => cls ??= Ids.Ship(Type);
    ShipClass? cls;
}

public sealed class Projectile : IPoint
{
    public int Id;
    public Faction Faction;
    public WeaponKind Kind;
    public double X { get; set; }
    public double Y { get; set; }
    public double Vx, Vy, Speed, Damage;
    public SizeMult Mult = SizeMult.None;
    public int TargetId;
    public bool Homing;
    public double Ttl;
}

public sealed class AsteroidField(int id, double x, double y, double radius) : IPoint
{
    public int Id = id;
    public double X { get; set; } = x;
    public double Y { get; set; } = y;
    public double Radius = radius;
    public int? MineId;
}

public enum QueueKind { Ship, Upgrade, Tier, Hero }

public sealed class QueueItem
{
    public QueueKind Kind;
    public ShipClass Cls;
    /// <summary>For a tier: the structure that goes up a level.</summary>
    public int TargetId;
    public HeroId Hero;
    public double Remaining, Total, Cost;
}

public enum GoalKind { Mine, Outpost, Upgrade, Platform, Structure, Research, Hero, Ship }

/// <summary>What the AI is saving up for.</summary>
public sealed record AIGoal(GoalKind Kind, BuildableType Structure = default, TechId Tech = default, HeroId Hero = default, ShipClass Cls = default);

public enum Difficulty { Easy, Normal, Hard }

public sealed record DifficultyDef(string Name, double Income, double Think);

/// <summary>What an AI remembers of its enemies' forces, fading over time.</summary>
public sealed class Intel
{
    public double Small, Medium, Large, Structures;
    /// <summary>Total cost of the enemy warships last seen.</summary>
    public double Army;
}

public sealed class AIState(Difficulty difficulty)
{
    public double Timer;
    public AIGoal? Goal;
    /// <summary>Army value (sum of ship costs) needed before the next attack wave (3000 in the web version).</summary>
    public double WaveValue = 4500;
    /// <summary>Credits set aside for the next station level, out of reach of other purchases (not in the web version).</summary>
    public double Saved;
    /// <summary>Whether the savings are out of reach now; not while it is outmatched.</summary>
    public bool Guarded;
    public Difficulty Difficulty = difficulty;
    public Intel Intel = new();
}

/// <summary>A faction's record over a battle, for the end screen.</summary>
public sealed class BattleStats
{
    public int Built, Lost, Kills;
    public double Damage, Earned;
}

public sealed class FactionState : IResearcher
{
    public Faction Id;
    public double Credits { get; set; }
    /// <summary>Credits earned by shooting ships down: in a campaign battle, they go back to the galaxy (not in the web version).</summary>
    public double Bounties;
    /// <summary>Ships being built, one at a time.</summary>
    public List<QueueItem> Queue = [];
    /// <summary>Station upgrades and structure levels, one at a time, alongside the ships.</summary>
    public List<QueueItem> BuildQueue = [];
    public ResearchLevels Research { get; set; } = new();
    public ResearchJob? Researching { get; set; }
    public List<ResearchJob> ResearchQueue { get; set; } = [];
    /// <summary>The faction uses its ships' abilities by itself.</summary>
    public bool AutoAbilities;
    /// <summary>Classes kept in production: one goes back in the queue as soon as none is in it.</summary>
    public List<ShipClass> Repeat = [];
    public BattleStats Stats = new();
    /// <summary>Seconds before each fallen hero can be built again.</summary>
    public Dictionary<HeroId, double> HeroCooldowns = [];
    public int? StationId;
    public bool HadStation;
    public double RallyX, RallyY;
    public AIState? AI;
}

public abstract record SimEvent;
public sealed record Destroyed(int Id, EntityKind Kind, string EntityType, Faction Faction, double X, double Y, double Radius) : SimEvent;
/// <summary>A shot landed on TargetId; ShieldId is whose shield took it, if one did.</summary>
public sealed record Hit(double X, double Y, WeaponKind Kind, int TargetId, Faction Faction, int? ShieldId) : SimEvent;
public sealed record Built(int Id) : SimEvent;
/// <summary>A ship left the shipyard (not in the web version: the view greets big ones).</summary>
public sealed record Delivered(int Id) : SimEvent;
public sealed record Researched(Faction Faction, TechId Tech) : SimEvent;
/// <summary>An ability was used, from (X, Y), aimed at (Tx, Ty).</summary>
public sealed record AbilityUsed(AbilityId Id, Faction Faction, int EntityId, double X, double Y, double Tx, double Ty) : SimEvent;
public sealed record StrikeLanded(Strike Strike) : SimEvent;
public sealed record Rebuilt(int Id, Faction Faction, HardpointRole Role) : SimEvent;
public sealed record Victory(Faction Faction) : SimEvent;
/// <summary>A raid boss fell to `Faction`: its bounty, and the tech that gained a level, if any.</summary>
public sealed record RaidWon(Faction Faction, double Credits, TechId? Tech) : SimEvent;

public readonly record struct Result(bool Ok, string Reason = "")
{
    public static readonly Result OK = new(true);
    public static Result Fail(string reason) => new(false, reason);
}

/// <summary>
/// Entities by id, iterated in insertion order like a JavaScript Map: entities added while a
/// loop runs are visited by that loop, as they are in the web version.
/// </summary>
public sealed class EntityStore : IEnumerable<Entity>
{
    readonly List<Entity> order = [];
    readonly Dictionary<int, Entity> byId = [];

    public int Count => order.Count;

    public void Add(Entity e)
    {
        byId[e.Id] = e;
        order.Add(e);
    }

    public bool Remove(int id)
    {
        if (!byId.Remove(id, out var e)) return false;
        order.Remove(e);
        return true;
    }

    public Entity? Get(int? id) => id is int i && byId.TryGetValue(i, out var e) ? e : null;

    public bool Has(int id) => byId.ContainsKey(id);

    public Enumerator GetEnumerator() => new(order);
    IEnumerator<Entity> IEnumerable<Entity>.GetEnumerator() => GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Walks the live list by index: it sees what is appended meanwhile.</summary>
    public struct Enumerator(List<Entity> list) : IEnumerator<Entity>
    {
        int index = -1;
        public readonly Entity Current => list[index];
        readonly object System.Collections.IEnumerator.Current => Current;
        public bool MoveNext() => ++index < list.Count;
        public void Reset() => index = -1;
        public readonly void Dispose() { }
    }
}

public sealed class World
{
    public double Time;
    public double Width, Height;
    public readonly EntityStore Entities = new();
    public List<Projectile> Projectiles = [];
    /// <summary>Factions in the order they joined.</summary>
    public readonly Dictionary<Faction, FactionState> Factions = [];
    public readonly List<AsteroidField> Fields = [];
    public readonly List<SimEvent> Events = [];
    public int NextId = 1;
    /// <summary>State of the mulberry32 generator.</summary>
    public int Seed;
    public Faction? Winner;
    public List<Zone> Zones = [];
    public List<Strike> Strikes = [];
    /// <summary>World time of the last damage anyone dealt: a long quiet means a battle that cannot end.</summary>
    public double LastDamage;
    /// <summary>Fog of war: each faction only sees and targets what its own units see.</summary>
    public bool Fog;
    /// <summary>Enemy entities each faction sees right now.</summary>
    public readonly Dictionary<Faction, HashSet<int>> Visible = [];
    /// <summary>Enemy structures each faction has seen at least once.</summary>
    public readonly Dictionary<Faction, HashSet<int>> Seen = [];
    public double VisionTimer;
    /// <summary>Neighbour index, rebuilt every step.</summary>
    public readonly SpatialGrid Grid = new();
    /// <summary>Separation's own grids: ships and structures, and what small craft are pushed by (see Movement.Separate).</summary>
    public readonly SpatialGrid Bodies = new(), Heavies = new();
}

/// <summary>The web version's names of ship classes, structures and parts.</summary>
public static class Ids
{
    static string Camel(string s) => char.ToLowerInvariant(s[0]) + s[1..];

    static readonly Dictionary<string, ShipClass> ships = Enum.GetValues<ShipClass>().ToDictionary(c => Camel(c.ToString()));
    static readonly Dictionary<string, HardpointRole> roles = Enum.GetValues<HardpointRole>().ToDictionary(c => Camel(c.ToString()));
    static readonly Dictionary<string, BuildableType> buildables = Enum.GetValues<BuildableType>().ToDictionary(c => Camel(c.ToString()));

    public static string Of(ShipClass c) => Names<ShipClass>.Of[(int)c];
    public static string Of(HardpointRole r) => Names<HardpointRole>.Of[(int)r];
    public static string Of(BuildableType b) => Names<BuildableType>.Of[(int)b];

    public static ShipClass Ship(string type) => ships[type];
    public static bool IsShip(string type) => ships.ContainsKey(type);
    public static HardpointRole Role(string type) => roles[type];
    public static bool IsBuildable(string type, out BuildableType b) => buildables.TryGetValue(type, out b);

    static class Names<T> where T : struct, Enum
    {
        public static readonly string[] Of = Enum.GetValues<T>().Select(v => Camel(v.ToString())).ToArray();
    }
}
