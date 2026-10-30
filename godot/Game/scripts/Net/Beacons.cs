// Games on the local network: a host calls out every second, by UDP broadcast on the beacon
// port (Protocol.BeaconPort, or --beacon), with its name, mode, seats and game port; the
// multiplayer screen listens there and lists what it hears. A firewall may stop them: joining by
// address still works.
using Godot;
using SandboxShip.Core;
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
        udp.SetDestAddress("255.255.255.255", Dev.Beacon);
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

/// <summary>The multiplayer screen's ear on the beacon port: the games heard in the last few seconds.</summary>
public sealed class BeaconListener
{
    /// <summary>A game not heard of for this long is gone.</summary>
    const double Forget = 3.5;

    readonly PacketPeerUdp udp = new();
    readonly Dictionary<string, (LanGame Game, double Heard)> games = [];
    /// <summary>When to try the port again, while another game on this machine holds it.</summary>
    double retry;

    /// <summary>Why nothing can be heard (the port taken by another game on this machine), or null.</summary>
    public string? Problem { get; private set; }

    public BeaconListener() => Listen(0);

    void Listen(double now)
    {
        var ok = udp.Bind(Dev.Beacon, "0.0.0.0") == Error.Ok;
        Problem = ok ? null : $"Recherche impossible pour l'instant : le port {Dev.Beacon} est pris par un autre jeu de cette machine.";
        retry = now + 1;
    }

    /// <summary>The games heard of lately, by address ("ip:port"); true when the list changed (or the port came free).</summary>
    public bool Poll(double now, out IReadOnlyList<(string Address, LanGame Game)> found)
    {
        var changed = false;
        if (!udp.IsBound() && now >= retry)
        {
            Listen(now);
            changed = Problem is null;
        }
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
