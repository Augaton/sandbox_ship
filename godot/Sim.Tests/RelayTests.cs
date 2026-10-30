// The online server (not in the web version), without sockets: its frames read back to the bytes
// they were written to and refuse broken ones; its hub lists the games, carries each game's
// messages between its host and its players only, on their channel and with their reliability,
// tells each end when the other goes, refuses other versions, keeps to its limits and waits no
// longer than it should.
using SandboxShip.Sim.Data;
using SandboxShip.Sim.Net;
using Xunit;

namespace SandboxShip.Sim.Tests;

public class RelayTests
{
    static readonly LanGame Skirmish = new(Protocol.Version, "0.3.0", "Partie d'Alice", LobbyMode.Skirmish, 1, 2, 0);

    static IEnumerable<RelayFrame> Every()
    {
        yield return new RelayFrame.List(Relay.Wire);
        yield return new RelayFrame.Open(Relay.Wire, Skirmish, false, "");
        yield return new RelayFrame.Open(Relay.Wire, Skirmish with { Port = 24680, Dedicated = true }, true, "203.0.113.7");
        yield return new RelayFrame.Update(Skirmish with { Taken = 2 }, true);
        yield return new RelayFrame.Join(Relay.Wire, 12, Protocol.Version, "0.3.0");
        yield return new RelayFrame.ToPlayer(3, Protocol.Encode(new Message.Welcome(3, "Partie d'Alice")));
        yield return new RelayFrame.ToHost(Protocol.Encode(Protocol.Hello("0.3.0", "Bob")));
        yield return new RelayFrame.Kick(3);
        yield return new RelayFrame.Games([new OnlineGame(1, true, Skirmish, false, "", 42), new OnlineGame(2, false, Skirmish with { Dedicated = true }, true, "[2001:db8::1]:24680", -1)]);
        yield return new RelayFrame.Opened(7);
        yield return new RelayFrame.Joined();
        yield return new RelayFrame.Came(2);
        yield return new RelayFrame.Went(2);
        yield return new RelayFrame.FromPlayer(2, [1, 2, 3]);
        yield return new RelayFrame.FromHost([4, 5]);
        yield return new RelayFrame.Closed("L'hôte a quitté la partie.");
    }

    [Fact]
    public void Every_frame_reads_back_to_the_bytes_it_was_written_to()
    {
        var tags = new HashSet<byte>();
        foreach (var f in Every())
        {
            var bytes = Relay.Encode(f);
            var back = Relay.Decode(bytes);
            Assert.Equal(f.GetType(), back.GetType());
            Assert.Equal(bytes, Relay.Encode(back));
            tags.Add(bytes[0]);
        }
        Assert.Equal(15, tags.Count);
        var games = (RelayFrame.Games)Relay.Decode(Relay.Encode(Every().OfType<RelayFrame.Games>().Single()));
        Assert.Equal(Every().OfType<RelayFrame.Games>().Single().Listed, games.Listed);
    }

    [Fact]
    public void A_broken_frame_is_refused()
    {
        static void Broken(byte[] bytes) => Assert.ThrowsAny<Exception>(() => Relay.Decode(bytes));
        Broken([]);
        Broken([200]);
        // Cut short, or followed by more.
        var open = Relay.Encode(new RelayFrame.Open(Relay.Wire, Skirmish, false, ""));
        for (var n = 1; n < open.Length; n++) Broken(open[..n]);
        Broken([.. Relay.Encode(new RelayFrame.Kick(3)), 0]);
        // A game message that is empty, or too large to carry.
        Broken(Relay.Encode(new RelayFrame.ToHost([]))[..1]);
        Broken(Relay.Encode(new RelayFrame.ToHost(new byte[Relay.MaxPayload + 1])));
        // A negative number, a huge text, more players than seats.
        Broken([(byte)5, 0xFF, 0xFF, 0xFF, 0xFF, 0x0F, 1]);
        Broken([(byte)39, 0xFF, 0xFF, 0xFF, 0x7F]);
        var full = Relay.Encode(new RelayFrame.Update(Skirmish with { Taken = 3, Seats = 2 }, false));
        Assert.Throws<InvalidDataException>(() => Relay.Decode(full));
    }

    [Fact]
    public void Beacons_say_whether_a_dedicated_server_calls_and_still_read_without_it()
    {
        var dedicated = Skirmish with { Dedicated = true, Port = 24690 };
        Assert.Equal(dedicated, Protocol.DecodeLanGame(Protocol.Encode(dedicated)));
        // A beacon of a build that did not say it: read as a host who plays.
        var older = Protocol.Encode(Skirmish)[..^1];
        Assert.Equal(Skirmish, Protocol.DecodeLanGame(older));
    }

    [Fact]
    public void Addresses_read_with_or_without_their_port()
    {
        Assert.Equal(("51.91.214.135", 27029), Relay.Address("51.91.214.135:27029", 1));
        Assert.Equal(("example.org", Relay.DefaultPort), Relay.Address(" example.org ", Relay.DefaultPort));
        Assert.Equal(("::1", 27030), Relay.Address("[::1]:27030", 1));
        Assert.Equal(("", 5), Relay.Address("", 5));
        Assert.Equal(("host:99999", 5), Relay.Address("host:99999", 5));
        Assert.Equal("[::1]:27030", Relay.Endpoint("::1", 27030));
        Assert.Equal("10.0.0.2:24680", Relay.Endpoint("10.0.0.2", 24680));
    }

    [Fact]
    public void Unreliable_packets_stay_unreliable_and_fragment_so()
    {
        Assert.Equal(Relay.Reliable, Relay.Forwarded(Relay.Reliable));
        Assert.Equal(Relay.UnreliableFragment, Relay.Forwarded(0));
        Assert.Equal(Relay.UnreliableFragment, Relay.Forwarded(Relay.UnreliableFragment));
        Assert.Equal(Relay.Unsequenced | Relay.UnreliableFragment, Relay.Forwarded(Relay.Unsequenced));
    }

    /// <summary>The hub, wired to lists instead of sockets.</summary>
    sealed class Wire
    {
        public readonly List<(int To, RelayFrame Frame, int Channel, int Flags)> Sent = [];
        public readonly List<int> Dropped = [];
        public readonly List<string> Log = [];
        public readonly RelayHub Hub;
        public double Now;

        public Wire(RelayLimits? limits = null) => Hub = new RelayHub((to, bytes, ch, flags) => Sent.Add((to, Relay.Decode(bytes), ch, flags)), Dropped.Add,
            conn => 10 + conn, Log.Add, limits);

        public void Connect(int conn, string address = "198.51.100.1") => Hub.Connected(conn, address, Now);

        public void Say(int conn, RelayFrame f, int channel = 0, int flags = Relay.Reliable) => Hub.Received(conn, Relay.Encode(f), channel, flags, Now);

        /// <summary>What was sent to a connection since the last call, frames only.</summary>
        public List<RelayFrame> To(int conn)
        {
            var mine = Sent.Where(s => s.To == conn).Select(s => s.Frame).ToList();
            Sent.RemoveAll(s => s.To == conn);
            return mine;
        }

        /// <summary>A host's game opened on connection `host`: its number.</summary>
        public int Open(int host, LanGame? game = null)
        {
            Connect(host);
            Say(host, new RelayFrame.Open(Relay.Wire, game ?? Skirmish, false, ""));
            return Assert.IsType<RelayFrame.Opened>(Assert.Single(To(host))).Room;
        }

        /// <summary>A player on connection `conn` joins game `room`: its number for the host.</summary>
        public int Join(int conn, int room, int host)
        {
            Connect(conn, $"198.51.100.{conn}");
            Say(conn, new RelayFrame.Join(Relay.Wire, room, Protocol.Version, "0.3.0"));
            Assert.IsType<RelayFrame.Joined>(Assert.Single(To(conn)));
            return Assert.IsType<RelayFrame.Came>(Assert.Single(To(host))).Player;
        }

        public IReadOnlyList<OnlineGame> List(int conn)
        {
            Now += 1;
            Say(conn, new RelayFrame.List(Relay.Wire));
            return Assert.IsType<RelayFrame.Games>(Assert.Single(To(conn))).Listed;
        }
    }

    const int Alice = 1, Bob = 2, Eve = 3, Carol = 4, Screen = 9;

    [Fact]
    public void A_hosted_game_is_listed_while_its_host_stays_and_gone_when_it_leaves()
    {
        var w = new Wire();
        w.Connect(Screen);
        Assert.Empty(w.List(Screen));
        var room = w.Open(Alice);
        var game = Assert.Single(w.List(Screen));
        Assert.Equal(new OnlineGame(room, true, Skirmish, false, "", 10 + Alice), game);
        // The host says how full its game is, and when a battle starts.
        w.Say(Alice, new RelayFrame.Update(Skirmish with { Taken = 2 }, true));
        Assert.Equal((2, true), (w.List(Screen)[0].Game.Taken, w.List(Screen)[0].Playing));
        w.Hub.Disconnected(Alice);
        Assert.Empty(w.List(Screen));
        Assert.Contains(w.Log, l => l.Contains("ouverte par 198.51.100.1"));
        Assert.Contains(w.Log, l => l.Contains("fermée : L'hôte a quitté la partie."));
    }

    [Fact]
    public void A_dedicated_server_is_listed_at_its_own_address_and_not_relayed()
    {
        var w = new Wire();
        w.Connect(Alice, "203.0.113.7");
        w.Say(Alice, new RelayFrame.Open(Relay.Wire, Skirmish with { Port = 24690, Dedicated = true }, true, ""));
        var room = Assert.IsType<RelayFrame.Opened>(Assert.Single(w.To(Alice))).Room;
        w.Connect(Carol, "203.0.113.8");
        w.Say(Carol, new RelayFrame.Open(Relay.Wire, Skirmish with { Port = 24691, Dedicated = true }, true, "play.example.org"));
        w.Connect(Screen);
        var games = w.List(Screen);
        Assert.Equal(["203.0.113.7:24690", "play.example.org:24691"], games.Select(g => g.Address));
        Assert.All(games, g => Assert.False(g.Relayed));
        Assert.All(games, g => Assert.Equal(-1, g.Ping));
        // Joined directly: the server refuses to carry it.
        w.Connect(Bob);
        w.Say(Bob, new RelayFrame.Join(Relay.Wire, room, Protocol.Version, "0.3.0"));
        Assert.Equal("Ce serveur dédié se rejoint directement, à son adresse.", Assert.IsType<RelayFrame.Closed>(Assert.Single(w.To(Bob))).Reason);
        Assert.Contains(Bob, w.Dropped);
    }

    [Fact]
    public void Messages_go_between_a_host_and_its_players_with_their_channel_and_reliability()
    {
        var w = new Wire();
        var room = w.Open(Alice);
        var bob = w.Join(Bob, room, Alice);
        var eve = w.Join(Eve, room, Alice);
        Assert.Equal([2, 3], new[] { bob, eve });
        byte[] hello = Protocol.Encode(Protocol.Hello("0.3.0", "Bob")), state = Protocol.Encode(new Message.State([9, 9, 9]));
        w.Say(Bob, new RelayFrame.ToHost(hello));
        var (to, frame, channel, flags) = Assert.Single(w.Sent);
        Assert.Equal((Alice, 0, Relay.Reliable), (to, channel, flags));
        Assert.Equal(hello, Assert.IsType<RelayFrame.FromPlayer>(frame) is { Player: 2 } p ? p.Payload : null);
        w.Sent.Clear();
        // A snapshot to Eve alone, unreliable on channel 1: it stays so.
        w.Say(Alice, new RelayFrame.ToPlayer(eve, state), 1, Relay.UnreliableFragment);
        (to, frame, channel, flags) = Assert.Single(w.Sent);
        Assert.Equal((Eve, 1, Relay.UnreliableFragment), (to, channel, flags));
        Assert.Equal(state, Assert.IsType<RelayFrame.FromHost>(frame).Payload);
        w.Sent.Clear();
        // Unsequenced stays unsequenced.
        w.Say(Alice, new RelayFrame.ToPlayer(bob, state), 1, Relay.Unsequenced);
        Assert.Equal(Relay.Unsequenced | Relay.UnreliableFragment, Assert.Single(w.Sent).Flags);
    }

    [Fact]
    public void Nobody_reaches_another_game()
    {
        var w = new Wire();
        var first = w.Open(Alice);
        var second = w.Open(Carol);
        var bob = w.Join(Bob, first, Alice);
        var eve = w.Join(Eve, second, Carol);
        Assert.Equal(bob, eve);
        // Carol's player number 2 is Eve, not Bob: a host reaches its own players only.
        w.Say(Carol, new RelayFrame.ToPlayer(2, [1]));
        Assert.Equal(Eve, Assert.Single(w.Sent).To);
        w.Sent.Clear();
        // A number nobody holds in this game: nothing goes.
        w.Say(Alice, new RelayFrame.ToPlayer(7, [1]));
        Assert.Empty(w.Sent);
        // A player speaks to its own host only, and cannot act as one.
        w.Say(Bob, new RelayFrame.ToHost([1]));
        Assert.Equal(Alice, Assert.Single(w.Sent).To);
        w.Sent.Clear();
        w.Say(Bob, new RelayFrame.ToPlayer(eve, [1]));
        Assert.IsType<RelayFrame.Closed>(Assert.Single(w.To(Bob)));
        Assert.Contains(Bob, w.Dropped);
        Assert.IsType<RelayFrame.Went>(Assert.Single(w.To(Alice)));
        Assert.Empty(w.To(Eve));
        Assert.Empty(w.To(Carol));
    }

    [Fact]
    public void Each_end_hears_when_the_other_goes()
    {
        var w = new Wire();
        var room = w.Open(Alice);
        var bob = w.Join(Bob, room, Alice);
        var eve = w.Join(Eve, room, Alice);
        // A player leaves: its host hears of it.
        w.Hub.Disconnected(Bob);
        Assert.Equal(bob, Assert.IsType<RelayFrame.Went>(Assert.Single(w.To(Alice))).Player);
        // The host sends one off: it goes after what was sent to it, and the host hears of that too.
        w.Say(Alice, new RelayFrame.Kick(eve));
        Assert.Contains(Eve, w.Dropped);
        Assert.Equal(eve, Assert.IsType<RelayFrame.Went>(Assert.Single(w.To(Alice))).Player);
        Assert.Empty(w.To(Eve));
        w.Say(Eve, new RelayFrame.ToHost([1]));
        Assert.Empty(w.Sent);
        // The host leaves: every player is told why and let go; the game is no longer listed.
        var carol = w.Join(Carol, room, Alice);
        Assert.Equal(4, carol);
        w.Hub.Disconnected(Alice);
        Assert.Equal("L'hôte a quitté la partie.", Assert.IsType<RelayFrame.Closed>(Assert.Single(w.To(Carol))).Reason);
        Assert.Contains(Carol, w.Dropped);
        w.Connect(Screen);
        Assert.Empty(w.List(Screen));
        // What was let go is no more: a late disconnection changes nothing.
        w.Hub.Disconnected(Carol);
        Assert.Empty(w.Sent);
    }

    [Fact]
    public void Another_version_is_told_why()
    {
        var w = new Wire();
        var room = w.Open(Alice);
        w.Connect(Bob);
        w.Say(Bob, new RelayFrame.Join(Relay.Wire, room, Protocol.Version, "0.2.0"));
        Assert.Equal("Version différente : la partie est en 0.3.0, ton jeu en 0.2.0.", Assert.IsType<RelayFrame.Closed>(Assert.Single(w.To(Bob))).Reason);
        w.Connect(Eve);
        w.Say(Eve, new RelayFrame.Join(Relay.Wire, room, Protocol.Version + 1, "0.3.0"));
        Assert.StartsWith("Version du jeu incompatible", Assert.IsType<RelayFrame.Closed>(Assert.Single(w.To(Eve))).Reason);
        w.Connect(Carol);
        w.Say(Carol, new RelayFrame.List(Relay.Wire + 1));
        Assert.StartsWith("Serveur en ligne d'une autre version", Assert.IsType<RelayFrame.Closed>(Assert.Single(w.To(Carol))).Reason);
        w.Connect(Screen);
        w.Say(Screen, new RelayFrame.Join(Relay.Wire, 99, Protocol.Version, "0.3.0"));
        Assert.Equal("Cette partie n'est plus listée.", Assert.IsType<RelayFrame.Closed>(Assert.Single(w.To(Screen))).Reason);
        // The host never heard of any of them.
        Assert.Empty(w.To(Alice));
        Assert.Equal([Bob, Eve, Carol, Screen], w.Dropped);
    }

    [Fact]
    public void Broken_or_misplaced_frames_end_the_connection()
    {
        var w = new Wire();
        w.Connect(Bob);
        w.Hub.Received(Bob, [200, 1, 2], 0, Relay.Reliable, 0);
        Assert.StartsWith("Message illisible", Assert.IsType<RelayFrame.Closed>(Assert.Single(w.To(Bob))).Reason);
        // A player's frame before joining anything, a host's before opening.
        w.Connect(Eve);
        w.Say(Eve, new RelayFrame.ToHost([1]));
        Assert.Equal("Message inattendu pour le serveur en ligne.", Assert.IsType<RelayFrame.Closed>(Assert.Single(w.To(Eve))).Reason);
        w.Connect(Carol);
        w.Say(Carol, new RelayFrame.Update(Skirmish, false));
        Assert.IsType<RelayFrame.Closed>(Assert.Single(w.To(Carol)));
        // A host that tries to open a second game, or to join one.
        var room = w.Open(Alice);
        w.Say(Alice, new RelayFrame.Join(Relay.Wire, room, Protocol.Version, "0.3.0"));
        Assert.IsType<RelayFrame.Closed>(Assert.Single(w.To(Alice)));
        w.Connect(Screen);
        Assert.Empty(w.List(Screen));
    }

    [Fact]
    public void The_server_keeps_to_its_limits()
    {
        var w = new Wire(new RelayLimits { Rooms = 2, PlayersPerRoom = 1, Connections = 5 });
        var room = w.Open(Alice);
        w.Open(Carol);
        w.Connect(5);
        w.Say(5, new RelayFrame.Open(Relay.Wire, Skirmish, false, ""));
        Assert.StartsWith("Le serveur en ligne est complet (2 parties)", Assert.IsType<RelayFrame.Closed>(Assert.Single(w.To(5))).Reason);
        w.Join(Bob, room, Alice);
        w.Connect(Eve);
        w.Say(Eve, new RelayFrame.Join(Relay.Wire, room, Protocol.Version, "0.3.0"));
        Assert.Equal("La partie est complète sur le serveur en ligne.", Assert.IsType<RelayFrame.Closed>(Assert.Single(w.To(Eve))).Reason);
        // Three connections left: two more fill the server, one more is too many.
        w.Connect(6);
        w.Connect(7);
        Assert.Empty(w.To(7));
        w.Connect(8);
        Assert.StartsWith("Le serveur en ligne est saturé", Assert.IsType<RelayFrame.Closed>(Assert.Single(w.To(8))).Reason);
    }

    [Fact]
    public void A_game_keeps_to_its_budget_of_bytes()
    {
        const int Rate = 10_000;
        var w = new Wire(new RelayLimits { RoomBytesPerSecond = Rate });
        var room = w.Open(Alice);
        var bob = w.Join(Bob, room, Alice);
        var big = new byte[1000];
        // Two seconds of budget at first: snapshots beyond it are left out, not the reliable messages.
        for (var i = 0; i < 30; i++) w.Say(Alice, new RelayFrame.ToPlayer(bob, big), 1, Relay.UnreliableFragment);
        var through = w.To(Bob).Count;
        Assert.InRange(through, 19, 20);
        Assert.Equal(30 - through, w.Hub.Dropped);
        w.Say(Alice, new RelayFrame.ToPlayer(bob, big));
        Assert.Single(w.To(Bob));
        // A second later, a second's worth goes again.
        w.Now += 1;
        for (var i = 0; i < 30; i++) w.Say(Alice, new RelayFrame.ToPlayer(bob, big), 1, Relay.UnreliableFragment);
        Assert.InRange(w.To(Bob).Count, 8, 10);
        // Reliable messages far beyond it: the game is closed for everyone, with the reason.
        for (var i = 0; i < 60 && w.Hub.Load.Rooms > 0; i++) w.Say(Alice, new RelayFrame.ToPlayer(bob, big));
        Assert.Equal(0, w.Hub.Load.Rooms);
        Assert.Equal("Débit trop élevé pour le serveur en ligne : la partie est fermée.", Assert.IsType<RelayFrame.Closed>(w.To(Bob).Last()).Reason);
        Assert.Contains(Alice, w.Dropped);
        Assert.Contains(Bob, w.Dropped);
    }

    [Fact]
    public void Silent_connections_are_let_go_in_time()
    {
        var w = new Wire(new RelayLimits { HelloWait = 5, ListWait = 30 });
        w.Connect(Bob);
        w.Connect(Screen);
        w.Say(Screen, new RelayFrame.List(Relay.Wire));
        w.To(Screen);
        var room = w.Open(Alice);
        w.Now = 4;
        w.Hub.Tick(w.Now);
        Assert.Empty(w.Dropped);
        w.Now = 6;
        w.Hub.Tick(w.Now);
        Assert.Equal("Pas de réponse.", Assert.IsType<RelayFrame.Closed>(Assert.Single(w.To(Bob))).Reason);
        // The screen that keeps asking stays; once it stops, it goes. A host waiting for players stays.
        w.Now = 20;
        Assert.Single(w.List(Screen));
        w.Now = 50;
        w.Hub.Tick(w.Now);
        Assert.DoesNotContain(Screen, w.Dropped);
        w.Now = 52;
        w.Hub.Tick(w.Now);
        Assert.Contains(Screen, w.Dropped);
        Assert.DoesNotContain(Alice, w.Dropped);
        Assert.Equal(1, w.Hub.Load.Rooms);
        Assert.Equal(room, w.Hub.Games.Single().Room);
    }

    [Fact]
    public void A_screen_asking_too_often_is_answered_at_its_pace()
    {
        var w = new Wire();
        w.Connect(Screen);
        for (var i = 0; i < 10; i++) w.Say(Screen, new RelayFrame.List(Relay.Wire));
        Assert.Single(w.To(Screen));
        w.Now += 0.6;
        w.Say(Screen, new RelayFrame.List(Relay.Wire));
        Assert.Single(w.To(Screen));
    }
}
