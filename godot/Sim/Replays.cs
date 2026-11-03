// Replays of battles (not in the web version): what made a battle's world, its skirmish options,
// and every command applied to it with the number of steps run before it came. The simulation
// being deterministic, the same world given the same commands after the same steps plays the
// same battle again. A digest of the last state checks it: another version of the game, with
// other rules, would not, and the replay says so rather than show another battle as this one.
using System.Text;
using SandboxShip.Sim.Data;

namespace SandboxShip.Sim;

/// <summary>A command as it was applied: after how many steps, for which side.</summary>
public readonly record struct LoggedCommand(long Step, Side Side, Command Command);

/// <summary>The commands applied to a world while it plays, in order (World.Log).</summary>
public sealed class ReplayLog
{
    public readonly List<LoggedCommand> Commands = [];

    public void Add(long step, Side side, Command c) => Commands.Add(new(step, side, c));
}

/// <summary>A battle as recorded: enough to play it again, and what the list of replays shows of it.</summary>
public sealed class Replay
{
    public required SkirmishOptions Options { get; init; }
    /// <summary>The sandbox's tools were open: its commands are played again too.</summary>
    public bool SandboxTools { get; init; }
    /// <summary>Whose battle it was: the side whose eyes the replay starts with.</summary>
    public Side Player { get; init; }
    /// <summary>The version of the game that recorded it.</summary>
    public string Version { get; init; } = "";
    /// <summary>When it was saved, in Unix milliseconds.</summary>
    public long SavedAt { get; init; }
    /// <summary>Steps played: the replay's length.</summary>
    public long Length { get; init; }
    public Side? Winner { get; init; }
    /// <summary>Replays.Digest of the world at the end.</summary>
    public uint Digest { get; init; }
    /// <summary>How many commands it holds, known from its header alone.</summary>
    public int CommandCount { get; init; }
    /// <summary>Empty when only the header was read.</summary>
    public IReadOnlyList<LoggedCommand> Commands { get; init; } = [];

    public double Seconds => Length * Steps.Step;
}

public static class Replays
{
    /// <summary>
    /// Layout of the file below: a replay of another layout is not read. Bumped with any change, as
    /// Commands.WireVersion with the commands'. 2: the terrain. 3: each seat's personality.
    /// </summary>
    public const int FormatVersion = 3;
    /// <summary>"SSRP", the file's first four bytes.</summary>
    const uint Magic = 0x50525353;

    /// <summary>Starts keeping the commands applied to a world made by Scenario.CreateSkirmish; false for any other, which a replay could not make again.</summary>
    public static bool Record(World w)
    {
        if (w.Origin is null) return false;
        w.Log ??= new ReplayLog();
        return true;
    }

    /// <summary>The replay of a recorded world as it stands; null if it was not recorded.</summary>
    public static Replay? Of(World w, Side player, string version, long savedAt) => w.Origin is null || w.Log is null ? null : new Replay
    {
        Options = w.Origin, SandboxTools = w.SandboxTools, Player = player, Version = version, SavedAt = savedAt,
        Length = w.StepCount, Winner = w.Winner, Digest = Digest(w), CommandCount = w.Log.Commands.Count, Commands = [.. w.Log.Commands],
    };

    /// <summary>The recorded battle's world as it started.</summary>
    public static World Start(Replay r)
    {
        var w = Scenario.CreateSkirmish(r.Options);
        w.SandboxTools = r.SandboxTools;
        return w;
    }

    /// <summary>
    /// A digest of what the battle has come to: steps, time, the generator's state, every entity's
    /// place, heading and health, every side's credits. FNV-1a, the same in every process.
    /// </summary>
    public static uint Digest(World w)
    {
        var h = 2166136261u;
        void Add(long v)
        {
            for (var i = 0; i < 8; i++) h = (h ^ (uint)((v >> (i * 8)) & 0xff)) * 16777619u;
        }
        void Real(double d) => Add(BitConverter.DoubleToInt64Bits(d));
        Add(w.StepCount);
        Real(w.Time);
        Add(w.NextId);
        Add(w.Seed);
        Add(w.Winner?.Id ?? -1);
        foreach (var e in w.Entities)
        {
            Add(e.Id);
            Real(e.X);
            Real(e.Y);
            Real(e.Angle);
            Real(e.Hull);
            Real(e.Shield);
            Real(e.Xp);
            Add(e.Rank | (int)e.Stance << 8);
        }
        foreach (var f in w.Factions.Values)
        {
            Add(f.Id.Id);
            Real(f.Credits);
        }
        Add(w.Projectiles.Count);
        return h;
    }

    public static void Write(Stream s, Replay r)
    {
        using var w = new BinaryWriter(s, Encoding.UTF8, leaveOpen: true);
        w.Write(Magic);
        w.Write(FormatVersion);
        w.Write(Commands.WireVersion);
        w.Write(r.Version);
        w.Write(r.SavedAt);
        w.Write(r.Length);
        w.Write(r.Digest);
        w.Write(r.Player.Id);
        w.Write(r.Winner?.Id ?? -1);
        w.Write(r.SandboxTools);
        WriteOptions(w, r.Options);
        w.Write(r.Commands.Count);
        long last = 0;
        foreach (var c in r.Commands)
        {
            w.Write7BitEncodedInt64(c.Step - last);
            last = c.Step;
            w.Write7BitEncodedInt(c.Side.Id);
            Commands.Write(w, c.Command);
        }
    }

    /// <summary>
    /// Reads a replay, or only its header (for the list). Never trusts the bytes: a file that is
    /// not a replay, or of another layout, throws InvalidDataException with the reason in French.
    /// </summary>
    public static Replay Read(Stream s, bool headerOnly = false)
    {
        using var r = new BinaryReader(s, Encoding.UTF8, leaveOpen: true);
        if (r.ReadUInt32() != Magic) throw new InvalidDataException("Ce fichier n'est pas une rediffusion.");
        var format = r.ReadInt32();
        var wire = r.ReadInt32();
        // Formats 1 and 2 still read: their battles had no terrain (1), their AIs all played Balanced.
        // So do commands 1: those of 2 but the new tags.
        if (format is not (1 or 2 or FormatVersion) || wire is not (1 or Commands.WireVersion)) throw new InvalidDataException("Rediffusion d'une autre version du jeu, illisible par celle-ci.");
        var version = r.ReadText();
        var savedAt = r.ReadInt64();
        var length = r.ReadInt64();
        var digest = r.ReadUInt32();
        var player = new Side(r.ReadInt32());
        var winner = r.ReadInt32();
        var sandbox = r.ReadBoolean();
        var options = ReadOptions(r, format);
        var count = r.ReadInt32();
        if (length < 0 || count < 0) throw new InvalidDataException("Rediffusion abîmée.");
        var commands = new List<LoggedCommand>(headerOnly ? 0 : Math.Min(count, 1 << 16));
        long step = 0;
        for (var i = 0; i < count && !headerOnly; i++)
        {
            step += r.ReadVarint64();
            commands.Add(new(step, new Side(r.ReadVarint()), Commands.ReadCommand(r)));
        }
        return new Replay
        {
            Options = options, SandboxTools = sandbox, Player = player, Version = version, SavedAt = savedAt, Length = length,
            Winner = winner >= 0 ? new Side(winner) : null, Digest = digest, CommandCount = count, Commands = commands,
        };
    }

    static void WriteOptions(BinaryWriter w, SkirmishOptions o)
    {
        w.Write((byte)o.Mode);
        w.Write(o.ZoneTarget);
        w.Write(o.AssaultMinutes);
        w.Write(o.Slots.Count);
        foreach (var s in o.Slots)
        {
            w.Write(s.Side.Id);
            w.Write((byte)s.Race);
            w.Write(s.Team);
            w.Write(s.AI is Difficulty ai ? (byte)ai : byte.MaxValue);
            w.Write(s.Color is not null);
            w.Write(s.Color ?? 0);
            w.Write((byte)s.Personality);
        }
        w.Write((byte)o.Player);
        w.Write(o.Enemies.Count);
        foreach (var e in o.Enemies) w.Write((byte)e);
        w.Write(o.EnemyAI);
        w.Write(o.Credits);
        w.Write(o.Seed);
        w.Write((byte)o.Map);
        w.Write((byte)o.Size);
        w.Write(o.Fog);
        w.Write((byte)o.Difficulty);
        w.Write(o.Raid);
        w.Write((byte)o.Terrain);
    }

    static SkirmishOptions ReadOptions(BinaryReader r, int format)
    {
        var o = new SkirmishOptions { Mode = Named<BattleMode>(r.ReadByte()), ZoneTarget = r.ReadInt32(), AssaultMinutes = r.ReadInt32() };
        var slots = r.ReadInt32();
        if (slots is < 0 or > 16) throw new InvalidDataException("Rediffusion abîmée.");
        for (var i = 0; i < slots; i++)
        {
            var side = new Side(r.ReadInt32());
            var race = Named<Faction>(r.ReadByte());
            var team = r.ReadInt32();
            var ai = r.ReadByte();
            var hasColor = r.ReadBoolean();
            var color = r.ReadUInt32();
            var personality = format >= 3 ? Named<Personality>(r.ReadByte()) : Personality.Balanced;
            o.Slots.Add(new Slot(side, race, team, ai == byte.MaxValue ? null : Named<Difficulty>(ai), hasColor ? color : null, personality));
        }
        o.Player = Named<Faction>(r.ReadByte());
        var enemies = r.ReadInt32();
        if (enemies is < 0 or > 16) throw new InvalidDataException("Rediffusion abîmée.");
        for (var i = 0; i < enemies; i++) o.Enemies.Add(Named<Faction>(r.ReadByte()));
        o.EnemyAI = r.ReadBoolean();
        o.Credits = r.ReadDouble();
        o.Seed = r.ReadInt32();
        o.Map = Named<MapKind>(r.ReadByte());
        o.Size = Named<MapSize>(r.ReadByte());
        o.Fog = r.ReadBoolean();
        o.Difficulty = Named<Difficulty>(r.ReadByte());
        o.Raid = r.ReadBoolean();
        if (format >= 2) o.Terrain = Named<TerrainDensity>(r.ReadByte());
        return o;
    }

    /// <summary>A known value of an enum, whatever its underlying type, or the file is refused.</summary>
    static T Named<T>(byte b) where T : struct, Enum =>
        Enum.ToObject(typeof(T), b) is T v && Enum.IsDefined(v) ? v : throw new InvalidDataException("Rediffusion abîmée.");
}

/// <summary>
/// A replay played again: its world stepped as it was, each recorded command applied after as
/// many steps as it first came. Faithful says, at the end, whether it came out as recorded.
/// </summary>
public sealed class ReplayPlayer
{
    readonly Replay replay;
    int next;

    public ReplayPlayer(Replay replay)
    {
        this.replay = replay;
        World = Replays.Start(replay);
        if (Over) End();
    }

    public World World { get; }
    public Replay Replay => replay;
    public bool Over => World.StepCount >= replay.Length;
    /// <summary>Whether the end came out as recorded; null until the end.</summary>
    public bool? Faithful { get; private set; }

    /// <summary>One step, after the commands that came before it.</summary>
    public void Step()
    {
        if (Over) return;
        Due();
        Steps.StepWorld(World);
        if (Over) End();
    }

    /// <summary>Steps until `step` (or the end), the world's events let go as they come: a jump ahead shows none of them.</summary>
    public void SkipTo(long step)
    {
        while (!Over && World.StepCount < step)
        {
            Step();
            World.Events.Clear();
        }
    }

    /// <summary>The commands that came after the last step, then the check against the recording.</summary>
    void End()
    {
        Due();
        Faithful = Replays.Digest(World) == replay.Digest;
    }

    void Due()
    {
        var commands = replay.Commands;
        while (next < commands.Count && commands[next].Step <= World.StepCount)
        {
            var c = commands[next++];
            Commands.Apply(World, c.Side, c.Command);
        }
    }
}
