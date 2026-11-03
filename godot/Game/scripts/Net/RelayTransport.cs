// A game played through the online server (Sim/Net/Relay.cs), under the same Session as one
// played directly (Transport): nobody opens a port, each machine only connects out to the server.
// The host opens its game there; the server numbers its players as ENet would (2 and up), so
// each is a peer of its own for the session. A player knows its host as Transport.HostPeer.
// Every message keeps the reliability Transport gives it, on both legs, and goes sealed as over
// Transport (Sim/Net/Secure.cs): the server carries bytes it cannot read, a host's players count
// for its session once their channel is sealed. When the server turns a player away or loses
// it, the player hears it as its host's Bye, with the reason.
//
// When both accepted the direct link (the host offering it in its lobby, the player on the
// multiplayer screen), the server introduces them to each other (Meet): the host sends a few
// packets toward the player from the socket the server sees, which opens the way in through its
// box for that machine, and the player connects to that socket from one of its own, the server's
// token in its connection's data. Once that connection is up, their messages take it; the relay
// stays, and carries them again if it drops. Through boxes that will not let it pass (one that
// lets in only the very port it sent to, or that changes ports for each address), nothing gets
// through in a few seconds: the game stays relayed, as it always was. An introduction that comes
// though this end did not accept it (from a server that does not keep to it) is ignored.
using Godot;
using SandboxShip.Sim;
using SandboxShip.Sim.Net;

namespace SandboxShip.Network;

public sealed class RelayTransport : ITransport
{
    /// <summary>How long an introduction is tried for, and how often the way is opened meanwhile, in milliseconds.</summary>
    const ulong TryMs = 6000, PunchMs = 200;

    readonly RelayLink link;
    readonly bool hosting;
    /// <summary>Host: its game as listed, and whether a battle or a campaign is under way.</summary>
    LanGame listed;
    bool playing;
    /// <summary>Player: in its game, its host told; the end already told (Bye), nothing more to say. Closed by its session.</summary>
    bool joined, told, closed;
    /// <summary>Player: it accepts the direct link (a host offers it in its game as listed).</summary>
    readonly bool direct;
    string? lost;
    /// <summary>Each peer's sealed channel, whichever way its packets go.</summary>
    readonly SecureLinks links;

    /// <summary>Straight connections up, by peer as the session knows it (a player's number; for a player, its host).</summary>
    readonly Dictionary<int, ENetPacketPeer> straight = [];
    /// <summary>Host: the player each straight connection is, by its peer's instance.</summary>
    readonly Dictionary<ulong, int> players = [];
    /// <summary>Introductions under way: where the other is, the token, until when to try, when to open the way next.</summary>
    readonly Dictionary<int, (string Address, int Port, int Token, ulong Until, ulong Next)> meetings = [];
    /// <summary>Player: its own connection to its host (Dial), and the host on it.</summary>
    ENetConnection? dialer;
    ENetPacketPeer? dialed;

    RelayTransport(RelayLink link, SecureLinks links, LanGame listed, bool direct = false) =>
        (this.link, this.links, hosting, this.listed, this.direct) = (link, links, links.Hosts, listed, direct);

    static double Now => Time.GetTicksMsec() / 1000.0;

    public Dictionary<int, long> Sent { get; } = [];
    /// <summary>Host: its game's number on the server, once listed; 0 until then.</summary>
    public int Room { get; private set; }
    public string Address => link.Address;
    /// <summary>The straight connections up: for a host, to so many players; for a player, 1 with its host.</summary>
    public int StraightCount => straight.Count;

    /// <summary>Whether a peer is reached straight, not through the server.</summary>
    public bool Straight(int peer) => straight.ContainsKey(peer);

    /// <summary>Opens a host's game on the server at `master`, locked by `password` (null: open); null with the reason when it cannot start.</summary>
    public static RelayTransport? Host(string master, LanGame game, string? password, out string? error)
    {
        if ((error = SecureChannel.Missing) is not null) return null;
        var link = RelayLink.Open(master, new RelayFrame.Open(Relay.Wire, game, false, ""), out error, Lobby.MaxSeats + 2);
        return link is null ? null : new RelayTransport(link, SecureLinks.Hosting(GameLock.For(password)), game);
    }

    /// <summary>
    /// Joins game `room` through the server at `master`, accepting the direct link if `direct`,
    /// with the game's password (null: none); null with the reason when it cannot start.
    /// </summary>
    public static RelayTransport? Join(string master, int room, bool direct, string? password, out string? error)
    {
        if ((error = SecureChannel.Missing) is not null) return null;
        var link = RelayLink.Open(master, new RelayFrame.Join(Relay.Wire, room, Protocol.Version, Ui.Menu.Version, direct), out error);
        return link is null ? null : new RelayTransport(link, SecureLinks.Joining(password), null!, direct);
    }

    public MultiplayerPeer.ConnectionStatus Status => link.Down ? MultiplayerPeer.ConnectionStatus.Disconnected
        : (hosting ? Room > 0 : joined) ? MultiplayerPeer.ConnectionStatus.Connected : MultiplayerPeer.ConnectionStatus.Connecting;

    public string? Lost => hosting ? lost : null;

    /// <summary>Host: what the list says of its game, sent when it changes.</summary>
    public void Advertise(LanGame game, bool playing)
    {
        if (Room == 0 || game == listed && playing == this.playing) return;
        (listed, this.playing) = (game, playing);
        link.Send(new RelayFrame.Update(game, playing));
    }

    public void Poll(Action<int> came, Action<int> went, Action<int, Message> received, Action<int, Exception> broken)
    {
        link.Poll(f =>
        {
            switch (f)
            {
                case RelayFrame.Opened o: Room = o.Room; break;
                case RelayFrame.Came c:
                    if (links.Connected(c.Player, Now) is { } offer) Put(c.Player, offer, true);
                    break;
                case RelayFrame.Went w:
                    Forget(w.Player);
                    if (links.Gone(w.Player)) went(w.Player);
                    break;
                case RelayFrame.FromPlayer p: Deliver(p.Player, p.Payload, came, received, broken); break;
                case RelayFrame.Joined:
                    joined = true;
                    links.Connected(Transport.HostPeer, Now);
                    break;
                case RelayFrame.FromHost h: Deliver(Transport.HostPeer, h.Payload, came, received, broken); break;
                case RelayFrame.Closed c when !hosting: Farewell(c.Reason, received); break;
                case RelayFrame.Meet m: Meet(m); break;
            }
        }, Reached, (peer, payload) =>
        {
            if (Whose(peer) is int from) Deliver(from, payload, came, received, broken);
        }, peer =>
        {
            if (Whose(peer) is int from) Forget(from);
        });
        OpenWays();
        PollDialed(came, received, broken);
        foreach (var p in links.Overdue(Now))
        {
            if (hosting) Drop(p);
            else Farewell("L'hôte ne répond pas.", received);
        }
        if (closed || !link.Down) return;
        var why = link.Problem ?? (Room > 0 || joined ? "Connexion au serveur en ligne perdue." : $"Serveur en ligne injoignable{RelayLink.Shown(link.Address)}.");
        if (hosting) lost ??= why;
        else Farewell(why, received);
    }

    /// <summary>The server introduced the other end: the way gets opened toward it for a few seconds, if this end accepted the direct link.</summary>
    void Meet(RelayFrame.Meet m)
    {
        if (!(hosting ? listed.Direct : direct)) return;
        var now = Time.GetTicksMsec();
        meetings[hosting ? m.Player : Transport.HostPeer] = (m.Address, m.Port, m.Token, now + TryMs, now);
    }

    /// <summary>Every frame, for each introduction: a few bytes toward the other end; a player also connects to its host, once. Past the time, it stays relayed.</summary>
    void OpenWays()
    {
        if (meetings.Count == 0 || closed) return;
        var now = Time.GetTicksMsec();
        foreach (var (peer, m) in meetings.ToList())
        {
            if (now > m.Until)
            {
                meetings.Remove(peer);
                if (!hosting && !straight.ContainsKey(peer)) Hang();
                GD.Print($"[réseau] Pas de liaison directe avec {(hosting ? $"le joueur {peer}" : "l'hôte")} : elle reste relayée");
                continue;
            }
            if (now < m.Next) continue;
            link.Punch(m.Address, m.Port);
            meetings[peer] = m with { Next = now + PunchMs };
            if (!hosting && dialer is null) Dial(m.Address, m.Port, m.Token);
        }
    }

    /// <summary>
    /// Host: a straight connection came up on the socket the server sees: bound to the player
    /// whose token it carries, refused without one.
    /// </summary>
    void Reached(ENetPacketPeer peer, int token)
    {
        if (!hosting) return;
        var match = meetings.FirstOrDefault(m => m.Value.Token == token);
        if (token == 0 || match.Value.Token != token)
        {
            peer.PeerDisconnect();
            return;
        }
        meetings.Remove(match.Key);
        straight[match.Key] = peer;
        players[peer.GetInstanceId()] = match.Key;
        GD.Print($"[réseau] Liaison directe avec le joueur {match.Key}");
    }

    /// <summary>
    /// Player: connects to its host from a socket of its own, Godot's ENet connecting out only
    /// once a socket (the link's is the server's). Its box opens the way out as it calls; the
    /// host's lets it in having sent toward this machine (Punch), unless it also holds to ports.
    /// </summary>
    void Dial(string address, int port, int token)
    {
        dialer = new ENetConnection();
        if (dialer.CreateHost(1, Relay.Channels) != Error.Ok)
        {
            dialer = null;
            return;
        }
        dialer.Compress(ENetConnection.CompressionMode.Zstd);
        dialed = dialer.ConnectToHost(address, port, Relay.Channels, token);
        if (dialed is null) Hang();
    }

    /// <summary>Player: its own connection to the host, polled as the link is.</summary>
    void PollDialed(Action<int> came, Action<int, Message> received, Action<int, Exception> broken)
    {
        while (dialer is not null && dialed is not null)
        {
            var e = dialer.Service();
            switch ((ENetConnection.EventType)e[0].AsInt32())
            {
                case ENetConnection.EventType.None: return;
                case ENetConnection.EventType.Connect:
                    meetings.Remove(Transport.HostPeer);
                    straight[Transport.HostPeer] = dialed;
                    GD.Print("[réseau] Liaison directe avec l'hôte");
                    break;
                case ENetConnection.EventType.Receive:
                    Deliver(Transport.HostPeer, dialed.GetPacket(), came, received, broken);
                    break;
                default:
                    Forget(Transport.HostPeer);
                    Hang();
                    return;
            }
        }
    }

    /// <summary>Player: lets its own connection to the host go.</summary>
    void Hang()
    {
        dialer?.Destroy();
        (dialer, dialed) = (null, null);
    }

    /// <summary>The peer a straight connection is, for the session; null for one it does not know.</summary>
    int? Whose(ENetPacketPeer peer)
    {
        return hosting && players.TryGetValue(peer.GetInstanceId(), out var p) ? p : null;
    }

    /// <summary>A straight connection gone, or its player: the relay carries their messages again.</summary>
    void Forget(int peer)
    {
        meetings.Remove(peer);
        if (!straight.Remove(peer, out var p)) return;
        players.Remove(p.GetInstanceId());
        GD.Print($"[réseau] Liaison directe avec {(hosting ? $"le joueur {peer}" : "l'hôte")} perdue : relayée de nouveau");
    }

    /// <summary>
    /// A packet from a peer, whichever way it came: its handshake goes on (a host's players
    /// come once sealed, a player hears a refusal as its host's Bye), or its message is opened.
    /// </summary>
    void Deliver(int from, byte[] payload, Action<int> came, Action<int, Message> received, Action<int, Exception> broken)
    {
        var a = links.Received(from, payload, Now);
        if (a.Reply is { } reply) Put(from, reply, true);
        if (a.Refused is { } why)
        {
            if (hosting) GD.Print($"[réseau] Joueur {from} refusé : {why}");
            else Farewell(why, received);
            return;
        }
        if (a.Came) came(from);
        if (a.Message is not { } bytes) return;
        Message m;
        try
        {
            m = Protocol.Decode(bytes);
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException or ArgumentException)
        {
            broken(from, e);
            return;
        }
        received(from, m);
    }

    /// <summary>Player: the end of the game as its host's own Bye, once.</summary>
    void Farewell(string why, Action<int, Message> received)
    {
        if (told) return;
        told = true;
        received(Transport.HostPeer, new Message.Bye(why));
    }

    /// <summary>A message, sealed, as reliable as Transport sends it; nothing while its channel is not.</summary>
    public void Send(int to, Message m)
    {
        var bytes = Protocol.Encode(m);
        var reliable = Transport.Reliable(m);
        var key = hosting ? to : Transport.HostPeer;
        if (links.Seal(key, bytes, reliable) is not { } sealedBytes) return;
        Put(key, sealedBytes, reliable);
        Sent[to] = Sent.GetValueOrDefault(to) + bytes.Length;
    }

    /// <summary>Bytes as they are to a peer: straight when that connection is up, else through the server; as reliable either way.</summary>
    void Put(int key, byte[] bytes, bool reliable)
    {
        if (straight.TryGetValue(key, out var p) && p.GetState() == ENetPacketPeer.PeerState.Connected)
        {
            p.Send(reliable ? 0 : 1, bytes, reliable ? Relay.Reliable : Relay.UnreliableFragment);
        }
        else link.Send(hosting ? new RelayFrame.ToPlayer(key, bytes) : new RelayFrame.ToHost(bytes), reliable);
    }

    public double PopWireBytes() => link.PopSentBytes();

    public void Drop(int to)
    {
        if (!hosting)
        {
            Close();
            return;
        }
        if (straight.TryGetValue(to, out var p)) p.PeerDisconnectLater();
        Forget(to);
        link.Send(new RelayFrame.Kick(to));
    }

    public void Close()
    {
        closed = true;
        dialer?.Flush();
        Hang();
        link.Close();
    }
}
