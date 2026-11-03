// Introductions by the online server (not in the web version): a host that offers the direct link
// and its player who accepts it, both of relay format 3 and seen with their ports, are each told
// where the other is, with the same token. Without both consents, nothing the server sends to
// anyone holds another's address; a machine of an older format is still served, never introduced.
using System.Text;
using SandboxShip.Sim.Data;
using SandboxShip.Sim.Net;
using Xunit;

namespace SandboxShip.Sim.Tests;

public class RelayMeetTests
{
    const int Alice = 1, Bob = 2, Carol = 3, Screen = 9;
    const string AliceAt = "198.51.100.1", BobAt = "203.0.113.9", CarolAt = "192.0.2.33";
    static readonly LanGame Skirmish = new(Protocol.Version, "0.5.0", "Partie d'Alice", LobbyMode.Skirmish, 1, 2, 0);
    static readonly LanGame Offered = Skirmish with { Direct = true };

    sealed class Wire
    {
        public readonly List<(int To, byte[] Bytes)> Sent = [];
        public readonly RelayHub Hub;
        public int Room;

        public Wire() => Hub = new RelayHub((to, bytes, _, _) => Sent.Add((to, bytes)), _ => { }, _ => 10, _ => { });

        public void Say(int conn, RelayFrame f) => Hub.Received(conn, Relay.Encode(f), 0, Relay.Reliable, 0);

        public List<RelayFrame> To(int conn) => [.. Sent.Where(s => s.To == conn).Select(s => Relay.Decode(s.Bytes))];

        public List<RelayFrame.Meet> Meets => [.. Sent.Select(s => Relay.Decode(s.Bytes)).OfType<RelayFrame.Meet>()];

        /// <summary>Alice opens her game, in a relay format, from an address and a port (0: unknown).</summary>
        public void Open(LanGame game, int wire = Relay.Wire, int port = 40001)
        {
            Hub.Connected(Alice, AliceAt, 0, port);
            Say(Alice, new RelayFrame.Open(wire, game, false, ""));
            Room = Assert.IsType<RelayFrame.Opened>(Assert.Single(To(Alice))).Room;
        }

        /// <summary>A player joins Alice's game from its address, accepting the direct link or not.</summary>
        public void Join(int conn, string address, bool direct, int wire = Relay.Wire, int port = 40002)
        {
            Hub.Connected(conn, address, 0, port);
            Say(conn, new RelayFrame.Join(wire, Room, Protocol.Version, "0.5.0", direct));
        }

        /// <summary>Whether any byte the server sent anyone holds this address, in any frame.</summary>
        public bool Told(string address)
        {
            var text = Encoding.UTF8.GetBytes(address);
            return Sent.Any(s => s.Bytes.AsSpan().IndexOf(text) >= 0);
        }
    }

    [Fact]
    public void A_host_and_its_player_who_both_accept_are_each_told_where_the_other_is_with_one_token()
    {
        var w = new Wire();
        w.Open(Offered);
        w.Join(Bob, BobAt, true);
        var toHost = Assert.Single(w.To(Alice).OfType<RelayFrame.Meet>());
        var toPlayer = Assert.Single(w.To(Bob).OfType<RelayFrame.Meet>());
        Assert.Equal((2, BobAt, 40002), (toHost.Player, toHost.Address, toHost.Port));
        Assert.Equal((1, AliceAt, 40001), (toPlayer.Player, toPlayer.Address, toPlayer.Port));
        Assert.Equal(toHost.Token, toPlayer.Token);
        Assert.NotEqual(0, toHost.Token);
        // The player is in as before: joined, and its host told.
        Assert.Contains(w.To(Bob), f => f is RelayFrame.Joined);
        Assert.Contains(w.To(Alice), f => f is RelayFrame.Came { Player: 2 });
    }

    [Fact]
    public void Without_both_consents_nobody_is_told_where_anybody_is()
    {
        foreach (var (game, direct) in new[] { (Skirmish, true), (Offered, false), (Skirmish, false) })
        {
            var w = new Wire();
            w.Open(game);
            w.Join(Bob, BobAt, direct);
            w.Hub.Connected(Screen, CarolAt, 0, 40009);
            w.Say(Screen, new RelayFrame.List(Relay.Wire));
            // Still in, relayed: joined, its host told, the list of games read.
            Assert.Contains(w.To(Bob), f => f is RelayFrame.Joined);
            Assert.Contains(w.To(Alice), f => f is RelayFrame.Came { Player: 2 });
            Assert.Contains(w.To(Screen), f => f is RelayFrame.Games);
            Assert.Empty(w.Meets);
            Assert.False(w.Told(AliceAt), $"{game.Direct}, {direct} : l'adresse de l'hôte est partie");
            Assert.False(w.Told(BobAt), $"{game.Direct}, {direct} : l'adresse du joueur est partie");
        }
    }

    [Fact]
    public void A_machine_of_an_older_format_or_an_unknown_port_is_relayed_and_never_introduced()
    {
        foreach (var (hostWire, playerWire) in new[] { (2, Relay.Wire), (Relay.Wire, 2), (Relay.MinWire, Relay.Wire) })
        {
            var w = new Wire();
            w.Open(Offered, hostWire);
            // A Join of format 2 has no consent to give: it cannot accept.
            w.Join(Bob, BobAt, true, playerWire);
            Assert.Contains(w.To(Bob), f => f is RelayFrame.Joined);
            Assert.Empty(w.Meets);
            Assert.False(w.Told(AliceAt) || w.Told(BobAt));
        }
        var unknown = new Wire();
        unknown.Open(Offered);
        unknown.Join(Bob, BobAt, true, port: 0);
        Assert.Empty(unknown.Meets);
    }

    [Fact]
    public void A_host_that_offers_the_link_later_meets_only_the_players_who_accept_it_and_once()
    {
        var w = new Wire();
        w.Open(Skirmish);
        w.Join(Bob, BobAt, true);
        w.Join(Carol, CarolAt, false, port: 40003);
        Assert.Empty(w.Meets);
        w.Say(Alice, new RelayFrame.Update(Offered with { Taken = 3, Seats = 3 }, false));
        var met = Assert.Single(w.To(Bob).OfType<RelayFrame.Meet>());
        Assert.Equal(AliceAt, met.Address);
        Assert.Empty(w.To(Carol).OfType<RelayFrame.Meet>());
        Assert.False(w.Told(CarolAt));
        // Withdrawn, then offered again: Bob was introduced already, Carol still does not accept.
        w.Say(Alice, new RelayFrame.Update(Skirmish with { Taken = 3, Seats = 3 }, false));
        w.Say(Alice, new RelayFrame.Update(Offered with { Taken = 3, Seats = 3 }, false));
        Assert.Equal(2, w.Meets.Count);
        Assert.False(w.Told(CarolAt));
    }

    [Fact]
    public void An_introduction_a_consent_and_an_offer_read_back()
    {
        var m = new RelayFrame.Meet(3, "2001:db8::7", 51234, 123456789);
        Assert.Equal(m, Relay.Decode(Relay.Encode(m)));
        foreach (var direct in new[] { false, true })
        {
            var j = new RelayFrame.Join(Relay.Wire, 4, Protocol.Version, "0.5.0", direct);
            Assert.Equal(j, Relay.Decode(Relay.Encode(j)));
        }
        // A Join of format 2 carries no consent, and reads as none.
        var older = new RelayFrame.Join(2, 4, Protocol.Version, "0.5.0", true);
        Assert.Equal(older with { Direct = false }, Relay.Decode(Relay.Encode(older)));
        var listed = new RelayFrame.Games([new OnlineGame(1, true, Offered with { Locked = true, Mode = LobbyMode.Assault }, false, "", 30)]);
        Assert.Equal(listed.Listed[0], ((RelayFrame.Games)Relay.Decode(Relay.Encode(listed))).Listed[0]);
    }

    [Fact]
    public void Only_the_host_turns_the_direct_link_on_or_off()
    {
        var l = Lobby.Create(LobbyMode.Skirmish, 3);
        var on = LobbySettings.Of(l) with { Direct = true };
        Assert.Equal(on, ((LobbyRequest.Settings)((Message.Ask)Protocol.Decode(Protocol.Encode(new Message.Ask(new LobbyRequest.Settings(on))))).Request).Values);
        Assert.False(l.Direct);
        // A dedicated server's leader is a player: the host's address is not its to give.
        Assert.True(LobbyRules.Apply(l, 1004, true, new LobbyRequest.Settings(on)));
        Assert.False(l.Direct);
        Assert.True(LobbyRules.Apply(l, Lobby.Host, true, new LobbyRequest.Settings(on)));
        Assert.True(l.Direct);
    }
}
