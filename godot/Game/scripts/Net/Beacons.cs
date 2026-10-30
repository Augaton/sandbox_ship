// Games on the local network: a host calls out every second, by UDP broadcast on the beacon
// port (Protocol.BeaconPort), with its name, mode, seats and game port; the join screen
// listens there and lists what it hears. A firewall may stop them: joining by address still works.
using Godot;
using SandboxShip.Sim.Net;

namespace SandboxShip.Network;

/// <summary>A host's call to the local network, every second.</summary>
public sealed class BeaconCaller
{
    readonly PacketPeerUdp udp = new();
    double wait;

    public BeaconCaller()
    {
        udp.SetBroadcastEnabled(true);
        udp.SetDestAddress("255.255.255.255", Protocol.BeaconPort);
    }

    public void Tick(double dt, Func<LanGame> game)
    {
        wait -= dt;
        if (wait > 0) return;
        wait = 1;
        udp.PutPacket(Protocol.Encode(game()));
    }

    public void Close() => udp.Close();
}

/// <summary>The join screen's ear on the beacon port: the games heard in the last few seconds.</summary>
public sealed class BeaconListener
{
    /// <summary>A game not heard of for this long is gone.</summary>
    const double Forget = 3.5;

    readonly PacketPeerUdp udp = new();
    readonly Dictionary<string, (LanGame Game, double Heard)> games = [];

    /// <summary>Why nothing can be heard (the port taken by another game on this machine), or null.</summary>
    public string? Problem { get; }

    public BeaconListener()
    {
        if (udp.Bind(Protocol.BeaconPort, "0.0.0.0") != Error.Ok) Problem = $"Recherche impossible : le port {Protocol.BeaconPort} est pris.";
    }

    /// <summary>The games heard of lately, by address ("ip:port"); true when the list changed.</summary>
    public bool Poll(double now, out IReadOnlyList<(string Address, LanGame Game)> found)
    {
        var changed = false;
        while (udp.GetAvailablePacketCount() > 0)
        {
            var bytes = udp.GetPacket();
            if (Protocol.DecodeLanGame(bytes) is not { } b) continue;
            var address = $"{udp.GetPacketIP()}:{b.Port}";
            changed |= !games.TryGetValue(address, out var was) || was.Game != b;
            games[address] = (b, now);
        }
        foreach (var (address, g) in games.ToList())
        {
            if (now - g.Heard < Forget) continue;
            games.Remove(address);
            changed = true;
        }
        found = [.. games.OrderBy(g => g.Key).Select(g => (g.Key, g.Value.Game))];
        return changed;
    }

    public void Close() => udp.Close();
}
