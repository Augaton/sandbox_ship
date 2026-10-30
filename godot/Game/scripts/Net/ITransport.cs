// What a Session plays over: ENet straight between the machines (Transport), or the online
// server's relay (RelayTransport). Either way a host knows each player by a peer number of 2 or
// more, a player knows its host as Transport.HostPeer, and every message keeps its reliability.
using Godot;
using SandboxShip.Sim.Net;

namespace SandboxShip.Network;

public interface ITransport
{
    MultiplayerPeer.ConnectionStatus Status { get; }
    /// <summary>Under a host: why its connection went down (the online server lost or refusing it); null while it holds.</summary>
    string? Lost { get; }
    /// <summary>Bytes of messages sent to each peer so far, before compression.</summary>
    Dictionary<int, long> Sent { get; }
    /// <summary>Bytes that left on the wire since the last call, compressed: ENet's own count.</summary>
    double PopWireBytes();
    /// <summary>
    /// Takes what came since the last call, in order: peers that came and went, their messages.
    /// A message that does not read is `broken`'s: a host drops that player.
    /// </summary>
    void Poll(Action<int> came, Action<int> went, Action<int, Message> received, Action<int, Exception> broken);
    void Send(int to, Message m);
    /// <summary>Lets a peer go: a player sent off by its host, or the host left by its player.</summary>
    void Drop(int to);
    void Close();
}
