// Introductions by the online server (not in the web version): a host and its new player, both of
// relay format 2 and seen with their ports, are each told where the other is, with the same
// token; a machine of format 1 is still served, never introduced.
using SandboxShip.Sim.Data;
using SandboxShip.Sim.Net;
using Xunit;

namespace SandboxShip.Sim.Tests;

public class RelayMeetTests
{
    const int Alice = 1, Bob = 2;
    static readonly LanGame Skirmish = new(Protocol.Version, "0.5.0", "Partie d'Alice", LobbyMode.Skirmish, 1, 2, 0);

    sealed class Wire
    {
        public readonly List<(int To, RelayFrame Frame)> Sent = [];
        public readonly RelayHub Hub;

        public Wire() => Hub = new RelayHub((to, bytes, _, _) => Sent.Add((to, Relay.Decode(bytes))), _ => { }, _ => 10, _ => { });

        public void Say(int conn, RelayFrame f) => Hub.Received(conn, Relay.Encode(f), 0, Relay.Reliable, 0);

        public List<RelayFrame> To(int conn) => [.. Sent.Where(s => s.To == conn).Select(s => s.Frame)];

        /// <summary>Alice opens, Bob joins, each in a relay format, from an address and a port (0: unknown).</summary>
        public void Meet(int aliceWire, int bobWire, int alicePort = 40001, int bobPort = 40002)
        {
            Hub.Connected(Alice, "198.51.100.1", 0, alicePort);
            Say(Alice, new RelayFrame.Open(aliceWire, Skirmish, false, ""));
            var room = Assert.IsType<RelayFrame.Opened>(Assert.Single(To(Alice))).Room;
            Hub.Connected(Bob, "203.0.113.9", 0, bobPort);
            Say(Bob, new RelayFrame.Join(bobWire, room, Protocol.Version, "0.5.0"));
        }
    }

    [Fact]
    public void A_host_and_its_player_are_each_told_where_the_other_is_with_one_token()
    {
        var w = new Wire();
        w.Meet(Relay.Wire, Relay.Wire);
        var toHost = Assert.Single(w.To(Alice).OfType<RelayFrame.Meet>());
        var toPlayer = Assert.Single(w.To(Bob).OfType<RelayFrame.Meet>());
        Assert.Equal((2, "203.0.113.9", 40002), (toHost.Player, toHost.Address, toHost.Port));
        Assert.Equal((1, "198.51.100.1", 40001), (toPlayer.Player, toPlayer.Address, toPlayer.Port));
        Assert.Equal(toHost.Token, toPlayer.Token);
        Assert.NotEqual(0, toHost.Token);
        // The player is in as before: joined, and its host told.
        Assert.Contains(w.To(Bob), f => f is RelayFrame.Joined);
        Assert.Contains(w.To(Alice), f => f is RelayFrame.Came { Player: 2 });
    }

    [Fact]
    public void A_machine_of_the_first_format_is_relayed_and_never_introduced()
    {
        var w = new Wire();
        w.Meet(Relay.MinWire, Relay.Wire);
        Assert.Contains(w.To(Bob), f => f is RelayFrame.Joined);
        Assert.DoesNotContain(w.Sent, s => s.Frame is RelayFrame.Meet);
        var unknown = new Wire();
        unknown.Meet(Relay.Wire, Relay.Wire, bobPort: 0);
        Assert.DoesNotContain(unknown.Sent, s => s.Frame is RelayFrame.Meet);
    }

    [Fact]
    public void An_introduction_reads_back()
    {
        var m = new RelayFrame.Meet(3, "2001:db8::7", 51234, 123456789);
        Assert.Equal(m, Relay.Decode(Relay.Encode(m)));
    }
}
