// ENet as the online server uses it (native/relay_enet.c over ENet 1.3.18, Godot's own), and
// Godot's compression of its packets: standard zstd frames at level 3, read as Godot reads them.
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SandboxShip.Online;

static unsafe partial class Enet
{
    const string Lib = "sandboxship_enet";

    public enum EventType { None, Connect, Disconnect, Receive }

    /// <summary>ENet's ENET_PEER_STATE_CONNECTED: a peer that may be written to.</summary>
    public const int Connected = 5;

    /// <summary>rn_event: what happened, from which peer, on which channel; a packet's bytes and flags.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Event
    {
        public EventType Type;
        public IntPtr Peer;
        public int Channel;
        public int Flags;
        public IntPtr Packet;
        public byte* Data;
        public nuint Length;
    }

    [LibraryImport(Lib, EntryPoint = "rn_initialize")] public static partial int Initialize();
    [LibraryImport(Lib, EntryPoint = "rn_deinitialize")] public static partial void Deinitialize();
    [LibraryImport(Lib, EntryPoint = "rn_host_create")] public static partial IntPtr HostCreate(ushort port, nuint peers, nuint channels);
    [LibraryImport(Lib, EntryPoint = "rn_host_compress")]
    private static partial int HostCompress(IntPtr host, delegate* unmanaged[Cdecl]<byte*, nuint, byte*, nuint, nuint> compress,
        delegate* unmanaged[Cdecl]<byte*, nuint, byte*, nuint, nuint> decompress);
    [LibraryImport(Lib, EntryPoint = "rn_host_limits")] public static partial void HostLimits(IntPtr host, nuint maximumPacket, nuint maximumWaiting);
    [LibraryImport(Lib, EntryPoint = "rn_host_destroy")] public static partial void HostDestroy(IntPtr host);
    [LibraryImport(Lib, EntryPoint = "rn_host_flush")] public static partial void HostFlush(IntPtr host);
    [LibraryImport(Lib, EntryPoint = "rn_host_pop_totals")] public static partial void HostPopTotals(IntPtr host, out uint received, out uint sent);
    [LibraryImport(Lib, EntryPoint = "rn_host_service")] public static partial int HostService(IntPtr host, Event* e, uint timeout);
    [LibraryImport(Lib, EntryPoint = "rn_packet_destroy")] public static partial void PacketDestroy(IntPtr packet);
    [LibraryImport(Lib, EntryPoint = "rn_peer_send")] public static partial int PeerSend(IntPtr peer, int channel, byte* data, nuint length, int flags);
    [LibraryImport(Lib, EntryPoint = "rn_peer_state")] public static partial int PeerState(IntPtr peer);
    [LibraryImport(Lib, EntryPoint = "rn_peer_round_trip")] public static partial uint PeerRoundTrip(IntPtr peer);
    [LibraryImport(Lib, EntryPoint = "rn_peer_address")] public static partial int PeerAddress(IntPtr peer, byte* text, nuint length);
    [LibraryImport(Lib, EntryPoint = "rn_peer_timeout")] public static partial void PeerTimeout(IntPtr peer, uint limit, uint minimum, uint maximum);
    [LibraryImport(Lib, EntryPoint = "rn_peer_ping_interval")] public static partial void PeerPingInterval(IntPtr peer, uint interval);
    [LibraryImport(Lib, EntryPoint = "rn_peer_disconnect_later")] public static partial void PeerDisconnectLater(IntPtr peer);
    [LibraryImport(Lib, EntryPoint = "rn_peer_disconnect_now")] public static partial void PeerDisconnectNow(IntPtr peer);

    /// <summary>The peer's address as text ("1.2.3.4"), and its port.</summary>
    public static (string Address, int Port) AddressOf(IntPtr peer)
    {
        var text = stackalloc byte[64];
        var port = PeerAddress(peer, text, 64);
        return (Marshal.PtrToStringUTF8((IntPtr)text) ?? "", port);
    }

    /// <summary>The host compresses what it sends, and reads what comes compressed, as a Godot peer does (ENetConnection.Compress(Zstd)).</summary>
    public static bool CompressAsGodot(IntPtr host) => HostCompress(host, &Compress, &Decompress) == 0;

    /// <summary>Godot's level (Compression.zstd_level).</summary>
    const int ZstdLevel = 3;

    // Both run on the thread that services the host, the only one: one of each is enough.
    [ThreadStatic] static ZstdSharp.Compressor? compressor;
    [ThreadStatic] static ZstdSharp.Decompressor? decompressor;

    /// <summary>
    /// A packet's commands, end to end, into a zstd frame of at most `limit` bytes; 0 when it does
    /// not fit, and ENet sends them as they are. Nothing may be thrown back into ENet.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    static nuint Compress(byte* input, nuint length, byte* output, nuint limit)
    {
        try
        {
            compressor ??= new ZstdSharp.Compressor(ZstdLevel);
            return compressor.TryWrap(new ReadOnlySpan<byte>(input, checked((int)length)), new Span<byte>(output, checked((int)limit)), out var written)
                ? (nuint)written : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>A zstd frame back into at most `limit` bytes (ENet's largest packet); 0 when it is not one, and ENet drops the packet.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    static nuint Decompress(byte* input, nuint length, byte* output, nuint limit)
    {
        try
        {
            decompressor ??= new ZstdSharp.Decompressor();
            return decompressor.TryUnwrap(new ReadOnlySpan<byte>(input, checked((int)length)), new Span<byte>(output, checked((int)limit)), out var written)
                ? (nuint)written : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }
}
