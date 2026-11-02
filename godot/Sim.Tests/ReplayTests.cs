// Replays, a feature of this version only: a battle recorded from its options and its commands,
// written to a file and read back, plays again to the very same state, AIs included.
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim.Tests;

public class ReplayTests
{
    static readonly Side Player = Faction.Klaed;

    /// <summary>A small battle of `mode` where every side plays on the AI, the player's too, so that a lot happens.</summary>
    static SkirmishOptions Options(BattleMode mode, int seed) => new()
    {
        Mode = mode, Seed = seed, Size = MapSize.Small, Raid = mode != BattleMode.Assault, AssaultMinutes = 2, ZoneTarget = 60,
        Slots = mode switch
        {
            BattleMode.Assault => [new(Faction.Klaed, Faction.Klaed, Assault.Defenders, Difficulty.Normal), new(Faction.Nairan, Faction.Nairan, Assault.Attackers, Difficulty.Hard)],
            BattleMode.Standard => [new(Faction.Klaed, Faction.Klaed, 0, Difficulty.Normal), new(Faction.Nairan, Faction.Nairan, 0, Difficulty.Normal), new(Faction.Nautolan, Faction.Nautolan, 0, Difficulty.Hard)],
            _ => [new(Faction.Klaed, Faction.Klaed, 0, Difficulty.Normal), new(Faction.Nairan, Faction.Nairan, 0, Difficulty.Hard)],
        },
    };

    static int[] Ships(World w) => [.. w.Entities.Where(e => e.Side == Player && e.Kind == EntityKind.Ship).Select(e => e.Id)];

    /// <summary>What the player does on top of its AI, as its screen would send it between two steps.</summary>
    static void Play(World w)
    {
        Command? c = w.StepCount switch
        {
            0 => new Command.SetAutoAbilities(true),
            60 => new Command.EnqueueShip(ShipClass.Frigate, 2),
            120 => w.Fields.OrderBy(f => Dist(f, StationOf(w, Player)!)).Select(f => new Command.PlaceStructure(BuildableType.Mine, f.X, f.Y)).FirstOrDefault(),
            900 => new Command.Move(Ships(w), w.Width / 2, w.Height / 2, true),
            1500 => w.Entities.FirstOrDefault(e => e.Side == Player && e.Abilities.Count > 0) is { } caster
                ? new Command.UseAbility(caster.Id, caster.Abilities[0].Id, new Aim(caster.X + 200, caster.Y)) : null,
            2400 => new Command.Stop(Ships(w)),
            3000 => w.Entities.FirstOrDefault(e => e.Side != Player && e.Kind == EntityKind.Ship && Vision.IsVisible(w, Player, e)) is { } foe
                ? new Command.Attack(Ships(w), foe.Id) : null,
            _ => null,
        };
        if (c is not null) Commands.Apply(w, Player, c);
    }

    /// <summary>Plays `steps` steps with the player's commands, recording, then the replay of it written and read back.</summary>
    static (World Recorded, Replay Replay) Record(SkirmishOptions o, int steps, Action<World>? play = null, bool sandbox = false)
    {
        var w = Scenario.CreateSkirmish(o);
        w.SandboxTools = sandbox;
        Assert.True(Replays.Record(w));
        for (var i = 0; i < steps; i++)
        {
            (play ?? Play)(w);
            Steps.StepWorld(w);
            w.Events.Clear();
        }
        var replay = Replays.Of(w, Player, "test", 1_700_000_000_000)!;
        using var file = new MemoryStream();
        Replays.Write(file, replay);
        file.Position = 0;
        return (w, Replays.Read(file));
    }

    [Theory]
    [InlineData(BattleMode.Standard, 7)]
    [InlineData(BattleMode.Standard, 42)]
    [InlineData(BattleMode.Survival, 3)]
    [InlineData(BattleMode.Zones, 11)]
    [InlineData(BattleMode.Assault, 5)]
    [InlineData(BattleMode.Regicide, 9)]
    public void A_battle_played_again_from_its_file_ends_exactly_as_recorded(BattleMode mode, int seed)
    {
        var (recorded, replay) = Record(Options(mode, seed), 4800);
        Assert.NotEmpty(replay.Commands);
        var player = new ReplayPlayer(replay);
        while (!player.Over) player.Step();
        Assert.True(player.Faithful);
        Assert.Equal(Replays.Digest(recorded), Replays.Digest(player.World));
        Assert.Equal(recorded.Entities.Count, player.World.Entities.Count);
        Assert.Equal(recorded.Winner, player.World.Winner);
    }

    [Fact]
    public void The_sandbox_s_tools_are_played_again_too()
    {
        var o = new SkirmishOptions { Seed = 21, Size = MapSize.Small, Credits = 50000, Fog = false, Slots = [new(Faction.Klaed, Faction.Klaed), new(Faction.Nairan, Faction.Nairan)] };
        void Play(World w)
        {
            Command? c = w.StepCount switch
            {
                0 => new Command.Spawn(Faction.Nairan, "dreadnought", 1500, 1500, 0),
                10 => new Command.Spawn(Faction.Klaed, "battlecruiser", 1700, 1500, Math.PI),
                20 => new Command.SetAI(Faction.Nairan, Difficulty.Hard),
                400 => new Command.GrantCredits(5000),
                900 => new Command.Destroy([.. w.Entities.Where(e => e.Side == Faction.Nairan && e.Kind == EntityKind.Ship).Take(1).Select(e => e.Id)]),
                _ => null,
            };
            if (c is not null) Commands.Apply(w, Player, c);
        }
        var (recorded, replay) = Record(o, 1800, Play, sandbox: true);
        Assert.True(replay.SandboxTools);
        var player = new ReplayPlayer(replay);
        player.SkipTo(long.MaxValue);
        Assert.True(player.Faithful);
        Assert.Equal(Replays.Digest(recorded), Replays.Digest(player.World));
    }

    [Fact]
    public void A_replay_that_no_longer_plays_the_same_says_so()
    {
        var (_, replay) = Record(Options(BattleMode.Standard, 7), 2400);
        // A command left out, as a change of rules would lose one: the end is no longer the one recorded.
        var altered = new Replay
        {
            Options = replay.Options, Player = replay.Player, Length = replay.Length, Digest = replay.Digest,
            Commands = [.. replay.Commands.Where(c => c.Command is not Command.EnqueueShip)],
        };
        var player = new ReplayPlayer(altered);
        player.SkipTo(long.MaxValue);
        Assert.False(player.Faithful);
    }

    [Fact]
    public void Its_header_tells_what_the_list_shows_without_reading_the_commands()
    {
        var (recorded, replay) = Record(Options(BattleMode.Zones, 11), 600);
        using var file = new MemoryStream();
        Replays.Write(file, replay);
        file.Position = 0;
        var header = Replays.Read(file, headerOnly: true);
        Assert.Empty(header.Commands);
        Assert.Equal(replay.Commands.Count, header.CommandCount);
        Assert.Equal((600L, "test", BattleMode.Zones, 11, 60), (header.Length, header.Version, header.Options.Mode, header.Options.Seed, header.Options.ZoneTarget));
        Assert.Equal(recorded.Origin!.Slots, header.Options.Slots);
        Assert.Equal(10, header.Seconds, 6);
    }

    [Fact]
    public void Only_a_world_made_from_options_is_recorded_and_a_stranger_file_is_refused()
    {
        Assert.False(Replays.Record(CreateWorld(2000, 2000)));
        Assert.Throws<InvalidDataException>(() => Replays.Read(new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12])));
        var (_, replay) = Record(Options(BattleMode.Standard, 7), 60);
        using var file = new MemoryStream();
        Replays.Write(file, replay);
        var bytes = file.ToArray();
        // Another layout of the file: the format's version, right after the four first bytes.
        bytes[4]++;
        Assert.Throws<InvalidDataException>(() => Replays.Read(new MemoryStream(bytes)));
    }
}
