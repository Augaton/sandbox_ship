// A connection to the online server (Sim/Net/Relay.cs): ENet, compressed as a game's, polled by
// hand. Its first frame goes as soon as it is up: List for the multiplayer screen (MasterList),
// Open for a host (RelayTransport) or a dedicated server (MasterListing), Join for a player.
using Godot;
using SandboxShip.Sim.Net;

namespace SandboxShip.Network;

public sealed class RelayLink
{
    readonly ENetConnection host;
    readonly ENetPacketPeer server;
    readonly RelayFrame hello;
    bool closed;

    /// <summary>The server's address, as given.</summary>
    public string Address { get; }
    /// <summary>Connected to the server.</summary>
    public bool Up { get; private set; }
    /// <summary>The connection failed, broke, or was closed: it does not come back.</summary>
    public bool Down { get; private set; }
    /// <summary>What the server said last on closing it, or why its words could not be read.</summary>
    public string? Problem { get; private set; }
    /// <summary>
    /// Seconds before a link's round trip means something: ENet starts it at 500 ms and brings it
    /// down an eighth of the way each ping; at one ping every 100 ms, it is within a few ms by then.
    /// </summary>
    public const double Settle = 4;

    ulong upAt;

    /// <summary>The round trip to the server, in ms, as ENet measures it; -1 until it settles.</summary>
    public int Ping => Up && Time.GetTicksMsec() - upAt >= Settle * 1000 ? (int)server.GetStatistic(ENetPacketPeer.PeerStatistic.RoundTripTime) : -1;

    /// <summary>
    /// " (address)" to put the server's address in a sentence, only in a developer build: a
    /// player's build never shows where the online server is.
    /// </summary>
    public static string Shown(string address) => OS.IsDebugBuild() ? $" ({address})" : "";

    RelayLink(ENetConnection host, ENetPacketPeer server, string address, RelayFrame hello) => (this.host, this.server, Address, this.hello) = (host, server, address, hello);

    /// <summary>Starts connecting to the server at "host:port", saying `hello` once up; null with the reason when it cannot start.</summary>
    public static RelayLink? Open(string address, RelayFrame hello, out string? error)
    {
        var (name, port) = Relay.Address(address, Relay.DefaultPort);
        error = name.Length == 0 ? "Aucun serveur en ligne : son adresse se règle dans les options." : null;
        if (error is not null) return null;
        var host = new ENetConnection();
        var err = host.CreateHost(1, Relay.Channels);
        if (err != Error.Ok)
        {
            error = $"Connexion impossible ({err}).";
            return null;
        }
        host.Compress(ENetConnection.CompressionMode.Zstd);
        var server = host.ConnectToHost(name, port, Relay.Channels);
        if (server is null)
        {
            host.Destroy();
            error = $"Adresse du serveur en ligne introuvable{Shown(address)}.";
            return null;
        }
        // A server that does not answer is said so within seconds, not after ENet's half minute; an
        // idle link pings often enough for its round trip to settle in seconds (ENet starts it at 500 ms).
        server.SetTimeout(32, 3000, 8000);
        server.PingInterval(100);
        return new RelayLink(host, server, address, hello);
    }

    /// <summary>Takes what came, in order, until nothing waits or the link is closed under it.</summary>
    public void Poll(Action<RelayFrame> got)
    {
        while (!closed && !Down)
        {
            var e = host.Service();
            switch ((ENetConnection.EventType)e[0].AsInt32())
            {
                case ENetConnection.EventType.None: return;
                case ENetConnection.EventType.Connect:
                    (Up, upAt) = (true, Time.GetTicksMsec());
                    Send(hello);
                    break;
                case ENetConnection.EventType.Receive:
                    var bytes = server.GetPacket();
                    RelayFrame f;
                    try
                    {
                        f = Relay.Decode(bytes);
                    }
                    catch (Exception x) when (x is InvalidDataException or EndOfStreamException)
                    {
                        Problem = $"Réponse illisible du serveur en ligne : {x.Message}";
                        Close();
                        return;
                    }
                    if (f is RelayFrame.Closed c) Problem = c.Reason;
                    got(f);
                    break;
                default:
                    (Up, Down) = (false, true);
                    return;
            }
        }
    }

    /// <summary>A frame to the server: reliable on channel 0, or unreliable on channel 1, as Godot's ENet peer sends a game's messages.</summary>
    public void Send(RelayFrame f, bool reliable = true)
    {
        if (!Up || closed) return;
        server.Send(reliable ? 0 : 1, Relay.Encode(f), reliable ? Relay.Reliable : Relay.UnreliableFragment);
    }

    public double PopSentBytes() => closed ? 0 : host.PopStatistic(ENetConnection.HostStatistic.SentData);

    /// <summary>Sends what waits, says goodbye at once, and lets the connection go.</summary>
    public void Close()
    {
        if (closed) return;
        closed = true;
        host.Flush();
        if (server.IsActive()) server.PeerDisconnectNow();
        host.Destroy();
        (Up, Down) = (false, true);
    }
}
