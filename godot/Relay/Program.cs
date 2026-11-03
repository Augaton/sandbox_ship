// The online server on its own (`sandbox-ship-relay --port=27029`): the list of games and the
// relay between their hosts and their players, as the game's `--relay` serves them (the same
// RelayHub, the same ENet, Godot's compression), without loading Godot or the game. One thread
// services ENet, waiting on its socket while all is quiet; it writes who opens, joins and leaves,
// and every ten seconds its load. Ctrl+C or a panel's stop: everyone connected hears at once that
// the server goes, then it quits.
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using SandboxShip.Online;
using SandboxShip.Sim.Net;

var port = Relay.DefaultPort;
foreach (var arg in args)
{
    if (arg.StartsWith("--port=", StringComparison.Ordinal) && int.TryParse(arg["--port=".Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var p) && p is > 0 and < 65536)
    {
        port = p;
    }
    else if (arg is "--help" or "-h")
    {
        Console.WriteLine("sandbox-ship-relay [--port=PORT] : le serveur en ligne de Sandbox Ship (liste des parties et relais), "
            + $"sur le port UDP {Relay.DefaultPort} par défaut.");
        return 0;
    }
    else if (arg != "--relay")
    {
        Console.Error.WriteLine($"Option inconnue : {arg} (--port=PORT, --help)");
        return 2;
    }
}
return OnlineServer.Run(port);

namespace SandboxShip.Online
{
    sealed unsafe class OnlineServer
    {
        /// <summary>Seconds between two lines on the load.</summary>
        const double LoadEvery = 10;
        /// <summary>The longest wait on the socket for a packet, in ms: how soon a stop is heard, and the hub's clock ticks.</summary>
        const uint Wait = 10;

        readonly IntPtr host;
        readonly RelayHub hub;
        readonly Dictionary<IntPtr, int> ids = [];
        readonly Dictionary<int, IntPtr> peers = [];
        /// <summary>When each peer came, on the server's clock: its round trip is listed once settled.</summary>
        readonly Dictionary<int, double> came = [];
        readonly Stopwatch clock = Stopwatch.StartNew();
        volatile bool stop;
        int next;

        OnlineServer(IntPtr host, RelayLimits limits)
        {
            this.host = host;
            hub = new RelayHub(SendTo, DropPeer, PingOf, Log, limits);
        }

        public static int Run(int port)
        {
            if (Enet.Initialize() != 0)
            {
                Console.Error.WriteLine("ENet ne démarre pas.");
                return 1;
            }
            var limits = new RelayLimits();
            var host = Enet.HostCreate((ushort)port, (nuint)limits.Connections, (nuint)Relay.Channels);
            if (host == IntPtr.Zero)
            {
                // ENet says only that it could not create its host: most often, the port is taken.
                Console.Error.WriteLine($"Impossible d'écouter sur le port {port} : déjà pris par un autre programme ?");
                Enet.Deinitialize();
                return 1;
            }
            if (!Enet.CompressAsGodot(host))
            {
                Console.Error.WriteLine("Compression indisponible.");
                Enet.HostDestroy(host);
                Enet.Deinitialize();
                return 1;
            }
            var s = new OnlineServer(host, limits);
            var version = typeof(OnlineServer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";
            Log($"Serveur en ligne léger ({version}, relais {Relay.Wire}) à l'écoute sur le port {port} : {limits.Rooms} parties au plus, "
                + $"{limits.PlayersPerRoom} joueurs chacune, {limits.RoomBytesPerSecond >> 10} Kio/s par partie");
            void Stopping(PosixSignalContext context)
            {
                context.Cancel = true;
                Log("Arrêt demandé");
                s.stop = true;
            }
            using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, Stopping);
            using var terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Stopping);
            s.Serve();
            Enet.Deinitialize();
            return 0;
        }

        void Serve()
        {
            var (loadAt, cpuAt, bytesIn, bytesOut, dropped) = (0.0, Process.GetCurrentProcess().TotalProcessorTime, 0L, 0L, 0L);
            var ticked = 0.0;
            Enet.Event e;
            while (!stop)
            {
                // Waits on the socket for the first event, then takes those that came with it.
                for (var wait = Wait; !stop; wait = 0)
                {
                    var got = Enet.HostService(host, &e, wait);
                    if (got == 0) break;
                    if (got < 0)
                    {
                        Log("Erreur d'ENet");
                        Thread.Sleep(10);
                        break;
                    }
                    Handle(ref e, clock.Elapsed.TotalSeconds);
                }
                Enet.HostFlush(host);
                var now = clock.Elapsed.TotalSeconds;
                if (now - ticked >= 0.1)
                {
                    hub.Tick(now);
                    ticked = now;
                }
                if (now - loadAt < LoadEvery) continue;
                var span = now - loadAt;
                var cpu = Process.GetCurrentProcess().TotalProcessorTime;
                var (rooms, relayed, players, listers) = hub.Load;
                Enet.HostPopTotals(host, out var wireIn, out var wireOut);
                Log($"Charge : {rooms} parties ({relayed} relayées), {players} joueurs, {listers} en liste ; {(hub.BytesIn - bytesIn) / span / 1024:0.0} Kio/s reçus, "
                    + $"{(hub.BytesOut - bytesOut) / span / 1024:0.0} envoyés ({wireIn / span / 1024:0.0} et {wireOut / span / 1024:0.0} compressés) ; "
                    + $"{hub.Dropped - dropped} paquets écartés ; processeur {(cpu - cpuAt).TotalSeconds / span * 100:0.0} % ; "
                    + $"mémoire {Environment.WorkingSet >> 20} Mio");
                (loadAt, cpuAt, bytesIn, bytesOut, dropped) = (now, cpu, hub.BytesIn, hub.BytesOut, hub.Dropped);
            }
            // Stopped: everyone hears at once that the server is gone.
            foreach (var peer in peers.Values) if (Enet.PeerState(peer) == Enet.Connected) Enet.PeerDisconnectNow(peer);
            Enet.HostFlush(host);
            Enet.HostDestroy(host);
            Log($"Arrêté : {peers.Count} connexions fermées");
        }

        void Handle(ref Enet.Event e, double now)
        {
            switch (e.Type)
            {
                case Enet.EventType.Connect:
                    var id = ++next;
                    (ids[e.Peer], peers[id], came[id]) = (id, e.Peer, now);
                    // A peer that stops answering is let go within 15 s; a host's round trip, listed, settles in seconds.
                    Enet.PeerTimeout(e.Peer, 32, 5000, 15000);
                    Enet.PeerPingInterval(e.Peer, 100);
                    var (address, remotePort) = Enet.AddressOf(e.Peer);
                    hub.Connected(id, address, now, remotePort);
                    break;
                case Enet.EventType.Disconnect:
                    if (!ids.Remove(e.Peer, out var gone)) return;
                    peers.Remove(gone);
                    came.Remove(gone);
                    hub.Disconnected(gone);
                    break;
                case Enet.EventType.Receive:
                    // Copied out before ENet's packet goes: the hub keeps what it carries.
                    var bytes = new ReadOnlySpan<byte>(e.Data, checked((int)e.Length)).ToArray();
                    var (flags, channel) = (e.Flags, e.Channel);
                    Enet.PacketDestroy(e.Packet);
                    if (ids.TryGetValue(e.Peer, out var from)) hub.Received(from, bytes, channel, flags, now);
                    break;
            }
        }

        /// <summary>A peer still connected: one that went in the same batch of events may already be reset, before its own event comes.</summary>
        IntPtr Connected(int conn) => peers.TryGetValue(conn, out var p) && Enet.PeerState(p) == Enet.Connected ? p : IntPtr.Zero;

        void SendTo(int conn, byte[] bytes, int channel, int flags)
        {
            var peer = Connected(conn);
            if (peer == IntPtr.Zero) return;
            fixed (byte* data = bytes) Enet.PeerSend(peer, channel, data, (nuint)bytes.Length, flags);
        }

        /// <summary>Lets a peer go once what was sent to it has gone (the reason it is let go, last).</summary>
        void DropPeer(int conn)
        {
            var peer = Connected(conn);
            if (peer != IntPtr.Zero) Enet.PeerDisconnectLater(peer);
        }

        int PingOf(int conn) => Connected(conn) is var p && p != IntPtr.Zero && clock.Elapsed.TotalSeconds - came[conn] >= Relay.PingSettle
            ? (int)Enet.PeerRoundTrip(p) : -1;

        static void Log(string text) => Console.WriteLine($"[relais] {text}");
    }
}
