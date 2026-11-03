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
