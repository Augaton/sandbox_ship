// What a stranger may do to a server, a host or the game's files (not in the web version): one
// packet of broken bytes, a flood of frames, of connections, of orders or of lobby requests,
// a forged save. Each of these was possible before the security pass; each test proves its fix.
using System.Text;
using SandboxShip.Sim.Data;
using SandboxShip.Sim.Net;
using Xunit;

namespace SandboxShip.Sim.Tests;

public class SecurityTests
{
    static readonly LanGame Game = new(Protocol.Version, "0.5.0", "Partie", LobbyMode.Skirmish, 1, 2, 24680, false, false);

    sealed class Server
    {
        public readonly List<(int To, RelayFrame Frame)> Sent = [];
        public readonly HashSet<int> Dropped = [];
        public readonly RelayHub Hub;

        public Server(RelayLimits? limits = null) =>
            Hub = new RelayHub((to, bytes, _, _) => Sent.Add((to, Relay.Decode(bytes))), c => Dropped.Add(c), _ => -1, _ => { }, limits);

        public void Say(int conn, RelayFrame f, double now = 0, bool reliable = true) =>
            Hub.Received(conn, Relay.Encode(f), 0, reliable ? Relay.Reliable : 0, now);

        public string? ClosedFor(int conn) => Sent.LastOrDefault(s => s.To == conn && s.Frame is RelayFrame.Closed).Frame is RelayFrame.Closed c ? c.Reason : null;
    }

    [Fact]
    public void A_number_that_never_ends_is_refused_and_no_longer_brings_the_online_server_down()
    {
        // The seven bytes that threw FormatException out of the relay's thread.
        var bytes = Convert.FromHexString("01FFFFFFFFFF02");
        Assert.Throws<InvalidDataException>(() => Relay.Decode(bytes));
        var s = new Server();
        s.Hub.Connected(1, "203.0.113.5", 0);
        s.Hub.Received(1, bytes, 0, Relay.Reliable, 0);
        Assert.Contains(1, s.Dropped);
        Assert.NotNull(s.ClosedFor(1));
    }

    [Fact]
    public void Numbers_and_texts_read_as_BinaryReader_wrote_them_and_broken_ones_are_refused()
    {
        var rng = new Random(11);
        for (var i = 0; i < 2000; i++)
        {
            var n = i < 10 ? new[] { 0, 1, 127, 128, 16383, 16384, int.MaxValue, -1, int.MinValue, 300 }[i] : rng.Next(int.MinValue, int.MaxValue);
            var l = (long)n * rng.Next(1, 1 << 20);
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, Encoding.UTF8, true))
            {
                w.Write7BitEncodedInt(n);
                w.Write7BitEncodedInt64(l);
                w.Write($"texte {n} é");
            }
            ms.Position = 0;
            using var r = new BinaryReader(ms);
            Assert.Equal(n, r.ReadVarint());
            Assert.Equal(l, r.ReadVarint64());
            Assert.Equal($"texte {n} é", r.ReadText());
        }
        Assert.Throws<InvalidDataException>(() => new BinaryReader(new MemoryStream([0xFF, 0xFF, 0xFF, 0xFF, 0x7F])).ReadVarint());
        Assert.Throws<InvalidDataException>(() => new BinaryReader(new MemoryStream([.. Enumerable.Repeat((byte)0xFF, 9), 0x02])).ReadVarint64());
        // A text announced at a gigabyte is refused before anything is allocated for it.
        Assert.Throws<InvalidDataException>(() => new BinaryReader(new MemoryStream([0xFF, 0xFF, 0xFF, 0xFF, 0x03])).ReadText());
    }

    [Fact]
    public void One_address_holds_a_few_connections_and_games_at_most()
    {
        var s = new Server(new RelayLimits { ConnectionsPerAddress = 16, RoomsPerAddress = 4 });
        for (var c = 1; c <= 17; c++) s.Hub.Connected(c, "198.51.100.7", 0);
        Assert.Equal("Trop de connexions depuis ton adresse.", s.ClosedFor(17));
        Assert.Null(s.ClosedFor(16));
        // Another address is not counted with it.
        s.Hub.Connected(18, "198.51.100.8", 0);
        Assert.Null(s.ClosedFor(18));

        var t = new Server(new RelayLimits { RoomsPerAddress = 4 });
        for (var c = 1; c <= 5; c++)
        {
            t.Hub.Connected(c, "198.51.100.9", 0);
            t.Say(c, new RelayFrame.Open(Relay.Wire, Game, false, ""));
        }
        Assert.Equal(4, t.Hub.Load.Rooms);
        Assert.Equal("Trop de parties ouvertes depuis ton adresse.", t.ClosedFor(5));
    }

    [Fact]
    public void A_flood_of_frames_is_left_out_beyond_the_pace_of_a_connection()
    {
        var s = new Server(new RelayLimits { FramesPerSecond = 100 });
        s.Hub.Connected(1, "203.0.113.1", 0);
        s.Say(1, new RelayFrame.Open(Relay.Wire, Game, false, ""));
        // Ten thousand updates in the same instant: two seconds' worth go through, the rest is dropped.
        for (var i = 0; i < 10000; i++) s.Say(1, new RelayFrame.Update(Game, false), 0.001);
        Assert.InRange(s.Hub.Dropped, 9700, 9900);
        Assert.Null(s.ClosedFor(1));
        // A second later, it may speak again.
        var before = s.Hub.Dropped;
        s.Say(1, new RelayFrame.Update(Game, true), 1.5);
        Assert.Equal(before, s.Hub.Dropped);
    }

    [Fact]
    public void A_player_who_floods_its_host_is_let_go_and_the_game_goes_on()
    {
        var s = new Server(new RelayLimits { PlayerBytesPerSecond = 10_000, FramesPerSecond = 1e9 });
        s.Hub.Connected(1, "203.0.113.1", 0);
        s.Say(1, new RelayFrame.Open(Relay.Wire, Game, false, ""));
        s.Hub.Connected(2, "203.0.113.2", 0);
        s.Say(2, new RelayFrame.Join(Relay.Wire, 1, Game.Protocol, Game.Version));
        // Reliable messages far beyond the player's budget, all at once.
        for (var i = 0; i < 100; i++) s.Say(2, new RelayFrame.ToHost(new byte[1000]), 0.01);
        Assert.Equal("Débit trop élevé pour le serveur en ligne.", s.ClosedFor(2));
        Assert.Null(s.ClosedFor(1));
        Assert.Equal(1, s.Hub.Load.Rooms);
        Assert.Contains(s.Sent, x => x.To == 1 && x.Frame is RelayFrame.Went { Player: 2 });
    }

    static World Duel()
    {
        var w = Scenario.CreateSkirmish(new SkirmishOptions { Player = Faction.Klaed, Enemies = [Faction.Nairan], Seed = 4, Size = MapSize.Small });
        w.Fog = false;
        return w;
    }

    [Fact]
    public void A_host_takes_a_players_orders_at_the_pace_of_hands_and_hand_flying_freely()
    {
        var w = Duel();
        var sent = new List<(int To, Message M)>();
        var host = new BattleHost(w, (to, m) => sent.Add((to, m)));
        host.Add(2, Faction.Nairan);
        var ships = w.Entities.Where(e => e.Side == Faction.Nairan && e.Kind == EntityKind.Ship).Select(e => e.Id).ToArray();
        var logged = 0;
        w.Log = new ReplayLog();
        for (var i = 0; i < 1000; i++) host.Receive(2, new Command.Move(ships, 100 + i, 100, false));
        host.BeforeStep();
        logged = w.Log.Commands.Count;
        Assert.Equal((int)OrderPace.AtOnce, logged);
        // Hand flying is not counted, but no more than MostPending wait at once.
        for (var i = 0; i < 1000; i++) host.Receive(2, new Command.PilotInput(ships.FirstOrDefault(), 1, 0, i, 0, false));
        host.BeforeStep();
        Assert.Equal(logged + OrderPace.MostPending, w.Log.Commands.Count);
        // The player hears once that orders were left out, not once per order.
        host.Tick(0.016);
        host.Tick(0.016);
        Assert.Single(sent, s => s.M is Message.Refused { Reason: OrderPace.TooMany });
        // A second later, orders are taken again.
        host.Tick(1);
        host.Receive(2, new Command.Stop(ships));
        host.BeforeStep();
        Assert.IsType<Command.Stop>(w.Log.Commands[^1].Command);
    }

    [Fact]
    public void Lobby_requests_are_paced_per_player()
    {
        var gate = new ChatGate(ChatGate.AskBurst, ChatGate.AskEvery);
        var taken = Enumerable.Range(0, 1000).Count(_ => gate.Allow(7, 0));
        Assert.Equal(ChatGate.AskBurst, taken);
        Assert.True(gate.Allow(7, 1));
        // Another player has its own pace.
        Assert.True(gate.Allow(8, 0));
    }

    [Fact]
    public void A_save_cannot_name_a_file_outside_its_folder_nor_bring_a_race_by_number()
    {
        var g = Galaxies.CreateGalaxy(Faction.Klaed, [Faction.Nairan], false, 3, GalaxyGen.GenerateLayout(3, 2, MapSize.Small));
        var root = Directory.CreateTempSubdirectory("saves-security").FullName;
        try
        {
            var dir = Path.Combine(root, "saves");
            var id = SaveSlots.SaveAs(g, dir, "../../../tmp/evil")!;
            Assert.DoesNotContain('/', id);
            Assert.DoesNotContain('.', id);
            Assert.True(File.Exists(SaveSlots.PathOf(dir, id)));
            Assert.Single(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories));
            // A save whose race is a number (one that does not exist) is not a save.
            var path = SaveSlots.PathOf(dir, id);
            var text = File.ReadAllText(path);
            var forged = System.Text.RegularExpressions.Regex.Replace(text, "\"race\":\"[a-z]+\"", "\"race\":99");
            Assert.NotEqual(text, forged);
            File.WriteAllText(path, forged);
            Assert.Null(GalaxySave.Load(path));
            Assert.Null(GalaxySave.Info(path));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}

/// <summary>The last pass before 1.0.0: what came since the first (stances, patrols, guards, queues, deals, personalities).</summary>
public class FinalSecurityTests
{
    static World Battle()
    {
        var w = Scenario.CreateSkirmish(new SkirmishOptions { Player = Faction.Klaed, Enemies = [Faction.Nairan], Seed = 5, Size = MapSize.Small, Credits = 20000 });
        w.Factions[Faction.Nairan].AI = null;
        return w;
    }

    [Fact]
    public void A_rally_point_off_the_map_is_brought_onto_it()
    {
        var w = Battle();
        // Finite, but beyond any map: a float of the snapshot cannot even hold it.
        Assert.True(Commands.Apply(w, Faction.Klaed, new Command.SetRally(1e300, -1e300)).Ok);
        Assert.True(Commands.Apply(w, Faction.Klaed, new Command.EnqueueShip(ShipClass.Fighter, 1)).Ok);
        Entity? built = null;
        for (var i = 0; i < 60 * 60 && built is null; i++)
        {
            w.Events.Clear();
            Steps.StepWorld(w);
            if (w.Events.OfType<Delivered>().FirstOrDefault() is { } d) built = w.Entities.Get(d.Id);
        }
        Assert.NotNull(built);
        // Ships come out spread around the rally point, 160 wide.
        Assert.InRange(built.Order!.X, -80, w.Width + 80);
        Assert.InRange(built.Order.Y, -80, w.Height + 80);
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, true)) Snapshot.Write(w, Faction.Klaed, bw);
        var replica = new World();
        Snapshot.Read(new BinaryReader(new MemoryStream(ms.ToArray())), replica);
        Assert.True(double.IsFinite(replica.Factions[Faction.Klaed].RallyX) && double.IsFinite(replica.Factions[Faction.Klaed].RallyY));
    }

    [Fact]
    public void Something_put_down_off_the_map_by_the_sandbox_or_a_replay_lands_on_it()
    {
        var w = Battle();
        w.SandboxTools = true;
        // A replay is a file from anyone: its sandbox orders are played as they are written.
        foreach (var (x, y) in new[] { (1e30, 1e30), (-1e30, 5.0), (5.0, 1e300) })
        {
            foreach (var what in new[] { "fighter", "station", "admiral" }) Commands.Apply(w, Faction.Nautolan, new Command.Spawn(Faction.Nautolan, what, x, y, 0));
        }
        Assert.False(Commands.Apply(w, Faction.Klaed, new Command.PlaceStructure(BuildableType.Platform, 1e30, 1e30)).Ok);
        Steps.Run(w, 2);
        // A station's parts stand around it: at the edge, a little past it.
        const double Edge = 500;
        Assert.All(w.Entities, e => Assert.True(e.X >= -Edge && e.X <= w.Width + Edge && e.Y >= -Edge && e.Y <= w.Height + Edge, $"{e.Type} à ({e.X}, {e.Y})"));
    }

    /// <summary>A replay of these options, written as a file would hold it, with one order of an unknown side.</summary>
    static byte[] Forged(SkirmishOptions o)
    {
        using var ms = new MemoryStream();
        Replays.Write(ms, new Replay
        {
            Options = o, Player = Faction.Klaed, Version = "1.0.0", Length = 120,
            Commands = [new LoggedCommand(10, new Side(Lobby.MaxSeats), new Command.EnqueueShip(ShipClass.Frigate, 1))], CommandCount = 1,
        });
        return ms.ToArray();
    }

    [Fact]
    public void A_replay_forged_to_a_battle_that_cannot_be_is_refused_as_it_is_read()
    {
        SkirmishOptions Two(Action<SkirmishOptions>? change = null, BattleMode mode = BattleMode.Standard)
        {
            var o = new SkirmishOptions
            {
                Mode = mode, Seed = 3, Size = MapSize.Small,
                Slots = [new Slot(Faction.Klaed, Faction.Klaed, mode == BattleMode.Assault ? 1 : 0), new Slot(Faction.Nairan, Faction.Nairan, mode == BattleMode.Assault ? 2 : 0, Difficulty.Normal)],
            };
            change?.Invoke(o);
            return o;
        }
        var played = new ReplayPlayer(Replays.Read(new MemoryStream(Forged(Two()))));
        while (!played.Over) played.Step();
        SkirmishOptions[] forged =
        [
            Two(o => o.Slots = [new Slot(Faction.Klaed, Faction.Klaed), new Slot(Faction.Klaed, Faction.Nairan)]),
            Two(o => o.Slots = [new Slot(Faction.Klaed, Faction.Klaed), new Slot(new Side(-1), Faction.Nairan)]),
            Two(o => o.Slots = [new Slot(Faction.Klaed, Faction.Klaed), new Slot(new Side(1000), Faction.Nairan)]),
            Two(o => o.Slots = [.. Enumerable.Range(0, 16).Select(i => new Slot(new Side(i), Faction.Nairan))]),
            Two(o => o.Slots = [new Slot(Faction.Klaed, Faction.Klaed)]),
            Two(o => o.Slots = [new Slot(Faction.Klaed, Faction.Klaed), new Slot(Faction.Nairan, Faction.Errant)]),
            Two(o => o.Credits = double.NaN),
            Two(o => o.Credits = double.PositiveInfinity),
            Two(o => o.Credits = -5),
            Two(o => o.ZoneTarget = 0, BattleMode.Zones),
            Two(o => o.AssaultMinutes = -3, BattleMode.Assault),
            Two(o => o.WaveStrength = 0, BattleMode.Survival),
            Two(o => o.WaveStrength = 100_000, BattleMode.Survival),
        ];
        foreach (var o in forged)
        {
            Assert.Throws<InvalidDataException>(() => Replays.Read(new MemoryStream(Forged(o))));
        }
    }

    static byte[] Release(string tag) => Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string> { ["tag_name"] = tag }));

    [Fact]
    public void The_release_page_opened_is_built_only_from_a_tag_of_plain_digits()
    {
        Assert.Equal("v1.2.3", Releases.Tag(Release("v1.2.3")));
        Assert.True(Releases.IsNewer("v1.0.1", "1.0.0"));
        Assert.False(Releases.IsNewer("v1.0.0", "1.0.0"));
        // A line break after it, digits of another script, a path, a query: no answer.
        foreach (var tag in new[] { "v9.9.9\n", "v٩.٩.٩", "v9.9.9/../../evil", "v9.9.9?x=1", "9.9.9 ", "" })
        {
            Assert.Null(Releases.Tag(Release(tag)));
        }
        Assert.Null(Releases.Tag(Encoding.UTF8.GetBytes("""{"tag_name": 9}""")));
        Assert.Null(Releases.Tag(Encoding.UTF8.GetBytes("[1, 2]")));
        Assert.Null(Releases.Tag([0xFF, 0x00, 0x7B]));
    }
}
