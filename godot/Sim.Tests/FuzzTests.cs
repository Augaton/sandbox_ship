// Bytes that come from the network or from a file, broken or forged (not in the web version):
// every reader refuses them with InvalidDataException or EndOfStreamException, never another
// exception that would bring a server or the game down, never in more than a moment, and never
// asking for memory far beyond what came. The cases are drawn from fixed seeds: the same each run.
using System.Diagnostics;
using System.Text;
using SandboxShip.Sim.Data;
using SandboxShip.Sim.Net;
using Xunit;

namespace SandboxShip.Sim.Tests;

public class FuzzTests
{
    /// <summary>Variations of each seed, per reader.</summary>
    const int Cases = 400;

    /// <summary>
    /// Variations of a valid input: cut short, bytes flipped, bytes put in, numbers made malformed
    /// (five bytes that never end a 7-bit number) or huge, and plain noise.
    /// </summary>
    internal static IEnumerable<byte[]> Mutations(byte[] seed, int salt)
    {
        var rng = new Random(seed.Length * 7919 + salt);
        for (var i = 0; i < Cases; i++)
        {
            var b = seed.ToList();
            switch (i % 6)
            {
                case 0:
                    b = [.. seed.Take(rng.Next(seed.Length + 1))];
                    break;
                case 1:
                    for (var k = rng.Next(1, 4); k > 0 && b.Count > 0; k--) b[rng.Next(b.Count)] = (byte)rng.Next(256);
                    break;
                case 2:
                    b.InsertRange(rng.Next(b.Count + 1), [.. Enumerable.Range(0, rng.Next(1, 8)).Select(_ => (byte)rng.Next(256))]);
                    break;
                case 3:
                    b.InsertRange(rng.Next(b.Count + 1), [0xFF, 0xFF, 0xFF, 0xFF, 0xFF]);
                    break;
                case 4:
                    b.InsertRange(rng.Next(b.Count + 1), [0xFF, 0xFF, 0xFF, 0x7F]);
                    break;
                default:
                    b = [.. Enumerable.Range(0, rng.Next(0, 64)).Select(_ => (byte)rng.Next(256))];
                    if (seed.Length > 0 && b.Count > 0) b[0] = seed[0];
                    break;
            }
            yield return [.. b];
        }
    }

    /// <summary>
    /// Reads every variation of every seed: a refusal is fine; any other exception, a read of more
    /// than a second, or tens of megabytes allocated fail, with the input that did it.
    /// </summary>
    static void Survives(string what, IEnumerable<byte[]> seeds, Action<byte[]> read, Func<Exception, bool>? refusal = null)
    {
        refusal ??= e => e is InvalidDataException or EndOfStreamException;
        var salt = 0;
        foreach (var seed in seeds)
        {
            foreach (var input in Mutations(seed, salt++))
            {
                var clock = Stopwatch.StartNew();
                var allocated = GC.GetAllocatedBytesForCurrentThread();
                try
                {
                    read(input);
                }
                catch (Exception e) when (refusal(e))
                {
                }
                catch (Exception e)
                {
                    Assert.Fail($"{what} : {e.GetType().Name} ({e.Message}) sur {Convert.ToHexString(input)}");
                }
                Assert.True(clock.Elapsed.TotalSeconds < 1, $"{what} : {clock.Elapsed.TotalSeconds:F1} s sur {Convert.ToHexString(input)}");
                var used = GC.GetAllocatedBytesForCurrentThread() - allocated;
                Assert.True(used < 64L << 20, $"{what} : {used >> 20} Mio alloués sur {input.Length} octets");
            }
        }
    }

    static readonly LanGame Game = new(Protocol.Version, "0.5.0", "Partie d'Aurélien", LobbyMode.Zones, 2, 6, 24680, false, true);

    static IEnumerable<RelayFrame> RelayFrames()
    {
        yield return new RelayFrame.List(Relay.Wire);
        yield return new RelayFrame.Open(Relay.Wire, Game, false, "");
        yield return new RelayFrame.Open(Relay.Wire, Game with { Dedicated = true }, true, "203.0.113.7");
        yield return new RelayFrame.Update(Game, true);
        yield return new RelayFrame.Join(Relay.Wire, 3, Protocol.Version, "0.5.0");
        yield return new RelayFrame.ToPlayer(2, [1, 2, 3, 4]);
        yield return new RelayFrame.ToHost([9, 8, 7]);
        yield return new RelayFrame.Kick(4);
        yield return new RelayFrame.Games([new OnlineGame(1, true, Game, false, "", 42), new OnlineGame(2, false, Game, true, "203.0.113.7:24680", -1)]);
        yield return new RelayFrame.Opened(7);
        yield return new RelayFrame.Joined();
        yield return new RelayFrame.Came(2);
        yield return new RelayFrame.Went(2);
        yield return new RelayFrame.FromPlayer(2, [5, 6]);
        yield return new RelayFrame.FromHost([5, 6, 7]);
        yield return new RelayFrame.Closed("Le serveur en ligne est complet.");
        yield return new RelayFrame.Meet(2, "198.51.100.4", 50123, 123456);
    }

    [Fact]
    public void Frames_for_and_from_the_online_server()
    {
        Survives("Relay.Decode", RelayFrames().Select(Relay.Encode), bytes => Relay.Decode(bytes));
    }

    [Fact]
    public void The_online_server_lets_go_of_any_frame_and_never_throws()
    {
        var sent = new List<(int To, byte[] Bytes)>();
        var dropped = new HashSet<int>();
        var hub = new RelayHub((to, bytes, _, _) => sent.Add((to, bytes)), c => dropped.Add(c), _ => -1, _ => { });
        var now = 0.0;
        var conn = 0;
        // A host with its game, a player in it, then every broken frame from a host, a player and a newcomer.
        hub.Connected(++conn, "203.0.113.1", now, 40000);
        hub.Received(conn, Relay.Encode(new RelayFrame.Open(Relay.Wire, Game, false, "")), 0, Relay.Reliable, now);
        var host = conn;
        hub.Connected(++conn, "203.0.113.2", now, 40001);
        hub.Received(conn, Relay.Encode(new RelayFrame.Join(Relay.Wire, 1, Game.Protocol, Game.Version)), 0, Relay.Reliable, now);
        var player = conn;
        foreach (var from in new[] { host, player, -1 })
        {
            var salt = from;
            foreach (var seed in RelayFrames().Select(Relay.Encode))
            {
                foreach (var input in Mutations(seed, salt++))
                {
                    var c = from;
                    if (c < 0)
                    {
                        c = ++conn;
                        hub.Connected(c, "198.51.100.9", now, 1);
                    }
                    now += 0.01;
                    hub.Received(c, input, input.Length % 2, input.Length % 3 == 0 ? Relay.Reliable : 0, now);
                    if (now % 1 < 0.01) hub.Tick(now);
                }
            }
        }
        Assert.NotEmpty(sent);
    }

    [Fact]
    public void Game_messages_between_a_host_and_its_players()
    {
        Survives("Protocol.Decode", ProtocolTests.Every().Select(Protocol.Encode), bytes => Protocol.Decode(bytes));
        Survives("Protocol.DecodeLanGame", [Protocol.Encode(Game)], bytes => Protocol.DecodeLanGame(bytes));
    }

    internal static World Battle(int seed)
    {
        var w = Scenario.CreateSkirmish(new SkirmishOptions
        {
            Player = Faction.Klaed, Enemies = [Faction.Nairan, Faction.Nautolan], EnemyAI = true, Seed = seed, Size = MapSize.Small, Mode = BattleMode.Zones,
        });
        foreach (var f in w.Factions.Values)
        {
            f.AI ??= Worlds.NewAI();
            f.AutoAbilities = true;
        }
        Steps.Run(w, 40);
        return w;
    }

    [Fact]
    public void Battle_snapshots_whole_and_in_parts()
    {
        var w = Battle(5);
        var events = w.Events.ToList();
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, true)) Snapshot.Write(w, Faction.Klaed, bw, events);
        var whole = ms.ToArray();
        var parts = Snapshot.WriteParts(w, Faction.Klaed, events, null, new SnapshotFrames());
        Survives("Snapshot.Read", [whole], bytes => Snapshot.Read(new BinaryReader(new MemoryStream(bytes)), new World()));
        var replica = new World();
        Snapshot.Read(new BinaryReader(new MemoryStream(whole)), replica);
        Survives("Snapshot.Read (réplique déjà remplie)", [whole], bytes => Snapshot.Read(new BinaryReader(new MemoryStream(bytes)), replica));
        Survives("Snapshot.ReadPart", parts.Take(4), bytes => Snapshot.ReadPart(new BinaryReader(new MemoryStream(bytes)), new World(), new SnapshotFrames()));
    }

    internal static Galaxy Campaign(int seed)
    {
        var g = Galaxies.CreateGalaxy(Faction.Klaed, [Faction.Nairan, Faction.Nautolan], true, seed, GalaxyGen.GenerateLayout(seed, 3, MapSize.Small));
        g.Player = Faction.Errant;
        foreach (var f in g.Factions.Values) f.AI ??= new GalaxyAIState { Difficulty = Difficulty.Normal };
        Galaxies.RunGalaxy(g, 120);
        g.Player = Faction.Klaed;
        return g;
    }

    [Fact]
    public void Galaxy_snapshots_and_charts()
    {
        var g = Campaign(5);
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, true)) GalaxySnapshot.Write(g, Faction.Klaed, w, g.Events.ToList(), null, default);
        var snapshot = ms.ToArray();
        var chart = GalaxySnapshot.Chart(g);
        Survives("GalaxySnapshot.ReadChart", [chart], bytes => GalaxySnapshot.ReadChart(new BinaryReader(new MemoryStream(bytes)), new Galaxy()));
        var replica = new Galaxy();
        GalaxySnapshot.ReadChart(new BinaryReader(new MemoryStream(chart)), replica);
        Survives("GalaxySnapshot.Read", [snapshot], bytes => GalaxySnapshot.Read(new BinaryReader(new MemoryStream(bytes)), replica));
    }

    [Fact]
    public void Commands_from_players()
    {
        var w = Battle(6);
        var ids = w.Entities.Where(e => e.Side == Faction.Klaed).Select(e => e.Id).Take(5).ToArray();
        Command[] commands =
        [
            new Command.Move(ids, 100.5, 200.25, true), new Command.Attack(ids, ids[0]), new Command.Stop(ids),
            new Command.EnqueueShip(ShipClass.Frigate, 5), new Command.PlaceStructure(BuildableType.Mine, 300, 400),
            new Command.UseAbility(ids[0], AbilityId.Cloak, new Aim(1, 2, 3)), new Command.SetAutoAbilities(true),
        ];
        Survives("Commands.ReadCommand", commands.Select(c => Bytes(bw => Commands.Write(bw, c))), bytes => Commands.ReadCommand(new BinaryReader(new MemoryStream(bytes))));
    }

    static byte[] Bytes(Action<BinaryWriter> write)
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, true)) write(bw);
        return ms.ToArray();
    }

    [Fact]
    public void Replay_files()
    {
        var w = Scenario.CreateSkirmish(new SkirmishOptions { Player = Faction.Klaed, Enemies = [Faction.Nairan], EnemyAI = true, Seed = 3, Size = MapSize.Small });
        Assert.True(Replays.Record(w));
        for (var i = 0; i < 600; i++)
        {
            if (i == 30) Commands.Apply(w, Faction.Klaed, new Command.EnqueueShip(ShipClass.Frigate, 1));
            Steps.StepWorld(w);
            w.Events.Clear();
        }
        var bytes = Bytes(bw => Replays.Write(bw.BaseStream, Replays.Of(w, Faction.Klaed, "0.5.0", 1_700_000_000_000)!));
        Survives("Replays.Read", [bytes], b => Replays.Read(new MemoryStream(b)));
        Survives("Replays.Read (en-tête)", [bytes], b => Replays.Read(new MemoryStream(b), headerOnly: true));
    }

    [Fact]
    public void Campaign_saves()
    {
        var g = Campaign(7);
        var dir = Directory.CreateTempSubdirectory("fuzz-saves").FullName;
        try
        {
            var path = Path.Combine(dir, "save.json");
            Assert.True(GalaxySave.Save(g, path));
            var text = File.ReadAllBytes(path);
            // A save is read from disk: Info for the list, Load to play it; a broken one is left out (null).
            Survives("GalaxySave", [text], bytes =>
            {
                File.WriteAllBytes(path, bytes);
                GalaxySave.Info(path);
                GalaxySave.Load(path);
            }, _ => false);
            // A save of this version with its lists missing or holding nothing: refused, not a crash of the list of saves.
            string[] hollow =
            [
                """{"version":1,"planets":null,"factions":null,"humans":null,"fleets":null,"lanes":null}""",
                """{"version":1,"planets":[null],"factions":[null],"fleets":[null],"lanes":[null],"truces":[null],"offers":[null],"ongoing":[null]}""",
                """{"version":1,"planets":[{}],"factions":[{}],"humans":{"0":null}}""",
            ];
            foreach (var json in hollow)
            {
                File.WriteAllText(path, json);
                Assert.Null(GalaxySave.Load(path));
                GalaxySave.Info(path);
            }
            Survives("GalaxySave (champs vides)", hollow.Select(Encoding.UTF8.GetBytes), bytes =>
            {
                File.WriteAllBytes(path, bytes);
                GalaxySave.Info(path);
                GalaxySave.Load(path);
            }, _ => false);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
