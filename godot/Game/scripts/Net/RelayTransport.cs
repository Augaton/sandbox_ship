// A game played through the online server (Sim/Net/Relay.cs), under the same Session as one
// played directly (Transport): nobody opens a port, each machine only connects out to the server.
// The host opens its game there; the server numbers its players as ENet would (2 and up), so
// each is a peer of its own for the session. A player knows its host as Transport.HostPeer.
// Every message keeps the reliability Transport gives it, on both legs. When the server turns a
// player away or loses it, the player hears it as its host's Bye, with the reason.
using Godot;
using SandboxShip.Sim.Net;

namespace SandboxShip.Network;

public sealed class RelayTransport : ITransport
{
    readonly RelayLink link;
    readonly bool hosting;
    /// <summary>Host: its game as listed, and whether a battle or a campaign is under way.</summary>
    LanGame listed;
    bool playing;
    /// <summary>Player: in its game, its host told; the end already told (Bye), nothing more to say. Closed by its session.</summary>
    bool joined, told, closed;
    string? lost;

    RelayTransport(RelayLink link, bool hosting, LanGame listed) => (this.link, this.hosting, this.listed) = (link, hosting, listed);

    public Dictionary<int, long> Sent { get; } = [];
    /// <summary>Host: its game's number on the server, once listed; 0 until then.</summary>
    public int Room { get; private set; }
    public string Address => link.Address;

    /// <summary>Opens a host's game on the server at `master`; null with the reason when it cannot start.</summary>
    public static RelayTransport? Host(string master, LanGame game, out string? error)
    {
        var link = RelayLink.Open(master, new RelayFrame.Open(Relay.Wire, game, false, ""), out error);
        return link is null ? null : new RelayTransport(link, true, game);
    }

    /// <summary>Joins game `room` through the server at `master`; null with the reason when it cannot start.</summary>
    public static RelayTransport? Join(string master, int room, out string? error)
    {
        var link = RelayLink.Open(master, new RelayFrame.Join(Relay.Wire, room, Protocol.Version, Ui.Menu.Version), out error);
        return link is null ? null : new RelayTransport(link, false, null!);
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
                case RelayFrame.Came c: came(c.Player); break;
                case RelayFrame.Went w: went(w.Player); break;
                case RelayFrame.FromPlayer p: Deliver(p.Player, p.Payload, received, broken); break;
                case RelayFrame.Joined:
                    joined = true;
                    came(Transport.HostPeer);
                    break;
                case RelayFrame.FromHost h: Deliver(Transport.HostPeer, h.Payload, received, broken); break;
                case RelayFrame.Closed c when !hosting: Farewell(c.Reason, received); break;
            }
        });
        if (closed || !link.Down) return;
        var why = link.Problem ?? (Room > 0 || joined ? "Connexion au serveur en ligne perdue." : $"Serveur en ligne injoignable{RelayLink.Shown(link.Address)}.");
        if (hosting) lost ??= why;
        else Farewell(why, received);
    }

    static void Deliver(int from, byte[] payload, Action<int, Message> received, Action<int, Exception> broken)
    {
        Message m;
        try
        {
            m = Protocol.Decode(payload);
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

    public void Send(int to, Message m)
    {
        var bytes = Protocol.Encode(m);
        link.Send(hosting ? new RelayFrame.ToPlayer(to, bytes) : new RelayFrame.ToHost(bytes), Transport.Reliable(m));
        Sent[to] = Sent.GetValueOrDefault(to) + bytes.Length;
    }

    public double PopWireBytes() => link.PopSentBytes();

    public void Drop(int to)
    {
        if (hosting) link.Send(new RelayFrame.Kick(to));
        else Close();
    }

    public void Close()
    {
        closed = true;
        link.Close();
    }
}
