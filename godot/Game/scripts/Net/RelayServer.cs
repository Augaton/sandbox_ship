// The online server (`--relay`), headless and without a window: the list of games and the relay
// between their hosts and their players (Sim/Net/RelayHub.cs), on ENet as the game itself. It
// serves on a thread of its own that looks for packets every millisecond while they flow (every
// ten when all is quiet), so that what it carries waits for no frame; the main loop only idles. (ENet's own wait does not sleep in Godot: it spins
// a core for its whole timeout, and wakes for no packet.) It writes who opens, joins and leaves,
// and every ten seconds its load: games, players, bytes a second before and after compression,
// processor time.
using System.Diagnostics;
using Godot;
using SandboxShip.Core;
using SandboxShip.Sim.Net;

namespace SandboxShip.Network;

public sealed partial class RelayServer : Node
{
    /// <summary>Seconds between two lines on the load.</summary>
    const double LoadEvery = 10;

    readonly ENetConnection host = new();
    readonly Dictionary<ulong, int> ids = [];
    readonly Dictionary<int, ENetPacketPeer> peers = [];
    /// <summary>When each peer came, on the server's clock: its round trip is listed once settled.</summary>
    readonly Dictionary<int, double> came = [];
    readonly Stopwatch clock = Stopwatch.StartNew();
    RelayHub hub = null!;
    System.Threading.Thread? thread;
    volatile bool stop;
    int next;

    /// <summary>`--relay`: listens on --port (27029 by default) and serves until stopped; quits if it cannot listen.</summary>
    public static void Run()
    {
        // Nothing to do on the main loop: a few frames a second are plenty.
        Engine.MaxFps = 10;
        var s = new RelayServer();
        var limits = new RelayLimits();
        var err = s.host.CreateHostBound("*", Dev.Port, limits.Connections, Relay.Channels);
        if (err != Error.Ok)
        {
            GD.PrintErr(err == Error.AlreadyInUse ? $"Le port {Dev.Port} est déjà pris." : $"Impossible d'écouter sur le port {Dev.Port} ({err}).");
            Main.Instance.QuitGame();
            return;
        }
        s.host.Compress(ENetConnection.CompressionMode.Zstd);
        s.hub = new RelayHub(s.SendTo, s.DropPeer, s.PingOf, Log, limits);
        Main.Instance.AddChild(s);
        Log($"Serveur en ligne (version {Ui.Menu.Version}, relais {Relay.Wire}) à l'écoute sur le port {Dev.Port} : {limits.Rooms} parties au plus, "
            + $"{limits.PlayersPerRoom} joueurs chacune, {limits.RoomBytesPerSecond >> 10} Kio/s par partie");
        s.thread = new System.Threading.Thread(s.Serve) { IsBackground = true, Name = "Relais" };
        s.thread.Start();
    }

    public override void _ExitTree()
    {
        stop = true;
        thread?.Join(1000);
    }

    void Serve()
    {
        var (loadAt, cpuAt, bytesIn, bytesOut, dropped) = (0.0, Process.GetCurrentProcess().TotalProcessorTime, 0L, 0L, 0L);
        var (heard, ticked) = (double.NegativeInfinity, 0.0);
        while (!stop)
        {
            for (var e = host.Service(); !stop; e = host.Service())
            {
                var type = (ENetConnection.EventType)e[0].AsInt32();
                if (type == ENetConnection.EventType.None) break;
                if (type == ENetConnection.EventType.Error)
                {
                    GD.PushWarning("Erreur d'ENet sur le serveur en ligne");
                    System.Threading.Thread.Sleep(10);
                    break;
                }
                heard = clock.Elapsed.TotalSeconds;
                Handle(type, e[1].As<ENetPacketPeer>(), e[3].AsInt32(), heard);
            }
            host.Flush();
            var now = clock.Elapsed.TotalSeconds;
            if (now - ticked >= 0.1)
            {
                hub.Tick(now);
                ticked = now;
            }
            // A millisecond's sleep while packets flow: what comes meanwhile waits that long at most. Ten when all is quiet.
            System.Threading.Thread.Sleep(now - heard < 0.5 ? 1 : 10);
            if (now - loadAt < LoadEvery) continue;
            var span = now - loadAt;
            var cpu = Process.GetCurrentProcess().TotalProcessorTime;
            var (rooms, relayed, players, listers) = hub.Load;
            var wireIn = host.PopStatistic(ENetConnection.HostStatistic.ReceivedData);
            var wireOut = host.PopStatistic(ENetConnection.HostStatistic.SentData);
            Log($"Charge : {rooms} parties ({relayed} relayées), {players} joueurs, {listers} en liste ; {(hub.BytesIn - bytesIn) / span / 1024:0.0} Kio/s reçus, "
                + $"{(hub.BytesOut - bytesOut) / span / 1024:0.0} envoyés ({wireIn / span / 1024:0.0} et {wireOut / span / 1024:0.0} compressés) ; "
                + $"{hub.Dropped - dropped} paquets écartés ; processeur {(cpu - cpuAt).TotalSeconds / span * 100:0.0} %");
            (loadAt, cpuAt, bytesIn, bytesOut, dropped) = (now, cpu, hub.BytesIn, hub.BytesOut, hub.Dropped);
        }
        // Stopped: everyone hears at once that the server is gone.
        foreach (var p in peers.Values) if (p.IsActive()) p.PeerDisconnectNow();
        host.Flush();
        host.Destroy();
    }

    void Handle(ENetConnection.EventType type, ENetPacketPeer peer, int channel, double now)
    {
        switch (type)
        {
            case ENetConnection.EventType.Connect:
                var id = ++next;
                (ids[peer.GetInstanceId()], peers[id], came[id]) = (id, peer, now);
                // A peer that stops answering is let go within 15 s; a host's round trip, listed, settles in seconds.
                peer.SetTimeout(32, 5000, 15000);
                peer.PingInterval(100);
                hub.Connected(id, peer.GetRemoteAddress(), now);
                break;
            case ENetConnection.EventType.Disconnect:
                if (!ids.Remove(peer.GetInstanceId(), out var gone)) return;
                peers.Remove(gone);
                came.Remove(gone);
                hub.Disconnected(gone);
                break;
            case ENetConnection.EventType.Receive:
                // The flags first: they are those of the packet about to be taken.
                var flags = peer.GetPacketFlags();
                var bytes = peer.GetPacket();
                if (ids.TryGetValue(peer.GetInstanceId(), out var from)) hub.Received(from, bytes, channel, flags, now);
                break;
        }
    }

    /// <summary>A peer still connected: one that went in the same batch of events may already be reset, before its own event comes.</summary>
    ENetPacketPeer? Connected(int conn) => peers.TryGetValue(conn, out var p) && p.IsActive() && p.GetState() == ENetPacketPeer.PeerState.Connected ? p : null;

    void SendTo(int conn, byte[] bytes, int channel, int flags) => Connected(conn)?.Send(channel, bytes, flags);

    /// <summary>Lets a peer go once what was sent to it has gone (the reason it is let go, last).</summary>
    void DropPeer(int conn) => Connected(conn)?.PeerDisconnectLater();

    int PingOf(int conn) => Connected(conn) is { } p && clock.Elapsed.TotalSeconds - came[conn] >= RelayLink.Settle
        ? (int)p.GetStatistic(ENetPacketPeer.PeerStatistic.RoundTripTime) : -1;

    static void Log(string text) => GD.Print($"[relais] {text}");
}
