// The connection under a game played over the network: Godot's ENet, used as a plain packet
// peer (no RPC), polled by its Session every frame. A host listens for players; a player joins
// a host, which is its peer 1. Every message goes whole (Sim/Net/Protocol.cs): reliable and in
// order, but for snapshots, which the next one replaces; the events a snapshot must not lose go
// apart, reliably (News, Tidings). Packets are compressed (zstd) by ENet.
using Godot;
using SandboxShip.Sim.Net;

namespace SandboxShip.Network;

public sealed class Transport : ITransport
{
    /// <summary>A player's peer number for its host.</summary>
    public const int HostPeer = 1;

    readonly ENetMultiplayerPeer peer = new();
    readonly Queue<(int Peer, bool Up)> changes = [];

    /// <summary>Bytes of messages sent to each peer so far, before compression.</summary>
    public Dictionary<int, long> Sent { get; } = [];
    /// <summary>A host listening by itself holds as long as this machine does.</summary>
    public string? Lost => null;
    /// <summary>Bytes that left on the wire since the last call, compressed: ENet's own count.</summary>
    public double PopWireBytes() => Open ? peer.Host.PopStatistic(ENetConnection.HostStatistic.SentData) : 0;

    /// <summary>Still listening, or connected (or connecting) to the host.</summary>
    bool Open => peer.GetConnectionStatus() != MultiplayerPeer.ConnectionStatus.Disconnected;

    Transport()
    {
        peer.PeerConnected += id => changes.Enqueue(((int)id, true));
        peer.PeerDisconnected += id => changes.Enqueue(((int)id, false));
    }

    /// <summary>Listens for players on a port of every interface; null with the reason when it cannot.</summary>
    public static Transport? Listen(int port, int players, out string? error)
    {
        var t = new Transport();
        var err = t.peer.CreateServer(port, players);
        error = err == Error.Ok ? null : err == Error.AlreadyInUse ? $"Le port {port} est déjà pris." : $"Impossible d'écouter sur le port {port} ({err}).";
        if (error is not null) return null;
        t.peer.Host.Compress(ENetConnection.CompressionMode.Zstd);
        return t;
    }

    /// <summary>Starts joining a host; the connection is up when the host shows as connected (Poll).</summary>
    public static Transport? Join(string address, int port, out string? error)
    {
        var t = new Transport();
        var err = t.peer.CreateClient(address, port);
        error = err == Error.Ok ? null : $"Adresse introuvable : {address}:{port} ({err}).";
        if (error is not null) return null;
        t.peer.Host.Compress(ENetConnection.CompressionMode.Zstd);
        return t;
    }

    /// <summary>This end's peer number: 1 for a host, the one the host knows a player by.</summary>
    public int Id => peer.GetUniqueId();

    public MultiplayerPeer.ConnectionStatus Status => peer.GetConnectionStatus();

    /// <summary>
    /// Takes what came since the last call: peers that came, their messages in order, then
    /// peers that went, so that the last words of one that left (a host's Bye and its reason)
    /// are read before its departure. A message that does not read is `broken`'s: a host drops that player.
    /// </summary>
    public void Poll(Action<int> came, Action<int> went, Action<int, Message> received, Action<int, Exception> broken)
    {
        peer.Poll();
        List<int>? gone = null;
        while (changes.TryDequeue(out var c))
        {
            if (c.Up) came(c.Peer);
            else (gone ??= []).Add(c.Peer);
        }
        Receive(received, broken);
        foreach (var p in gone ?? []) went(p);
    }

    void Receive(Action<int, Message> received, Action<int, Exception> broken)
    {
        while (peer.GetAvailablePacketCount() > 0)
        {
            var from = peer.GetPacketPeer();
            var bytes = peer.GetPacket();
            Message m;
            try
            {
                m = Protocol.Decode(bytes);
            }
            catch (Exception e) when (e is InvalidDataException or EndOfStreamException or ArgumentException)
            {
                broken(from, e);
                continue;
            }
            received(from, m);
        }
    }

    public void Send(int to, Message m)
    {
        var bytes = Protocol.Encode(m);
        peer.SetTargetPeer(to);
        peer.TransferMode = Reliable(m) ? MultiplayerPeer.TransferModeEnum.Reliable : MultiplayerPeer.TransferModeEnum.UnreliableOrdered;
        peer.PutPacket(bytes);
        Sent[to] = Sent.GetValueOrDefault(to) + bytes.Length;
    }

    /// <summary>
    /// Snapshots, battle and galaxy, are replaced by the next one, and so are the acknowledgements
    /// of battle snapshots: unreliable (and in order). Everything else, the events that must not
    /// be lost and the galaxy's chart included, is reliable. Over the online server too (RelayTransport).
    /// </summary>
    public static bool Reliable(Message m) => m is not (Message.State or Message.Map or Message.Ack);

    /// <summary>Sends what waits now, before a connection closes on it.</summary>
    public void Flush()
    {
        if (Open) peer.Host.Flush();
    }

    /// <summary>Lets a peer go: a player sent off by its host, or the host left by its player.</summary>
    public void Drop(int to)
    {
        if (!Open) return;
        Flush();
        peer.DisconnectPeer(to);
    }

    public void Close()
    {
        Flush();
        peer.Close();
    }
}
