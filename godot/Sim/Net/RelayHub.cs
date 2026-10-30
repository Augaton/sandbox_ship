// The online server's logic (not in the web version), apart from its sockets: who is connected
// and as what, the games listed, and what goes from whom to whom (Relay.cs). The game's relay mode
// (`--relay`) feeds it ENet's events and sends what it asks; the tests feed it by hand.
//
// A connection says what it is with its first frame: the multiplayer screen (List), a host
// (Open), a dedicated server (Open, Public) or a player (Join). A player only ever reaches the
// host of its own game, and a host only its own players. Each game has a budget of bytes a
// second: unreliable packets beyond it are left out (the next snapshot replaces them); a host
// that keeps sending reliable ones far beyond it loses its game. A connection that does not say
// what it is in time, or says something malformed or out of place, is told why and let go.
using SandboxShip.Sim.Data;

namespace SandboxShip.Sim.Net;

/// <summary>What the online server allows, and how long it waits.</summary>
public sealed record RelayLimits
{
    /// <summary>Connections at once, of every kind.</summary>
    public int Connections { get; init; } = 512;
    /// <summary>Games listed at once, relayed and dedicated.</summary>
    public int Rooms { get; init; } = 32;
    /// <summary>Players in one relayed game, as a host listening by itself takes them.</summary>
    public int PlayersPerRoom { get; init; } = Lobby.MaxSeats + 2;
    /// <summary>Bytes a second relayed for one game, both ways, before compression: five players at a host's highest snapshot rate, and room to spare.</summary>
    public int RoomBytesPerSecond { get; init; } = 2 << 20;
    /// <summary>Seconds a new connection has to say what it is.</summary>
    public double HelloWait { get; init; } = 5;
    /// <summary>Seconds the multiplayer screen's connection may go without asking for the list.</summary>
    public double ListWait { get; init; } = 30;
}

public sealed class RelayHub
{
    enum Role { New, Lister, Host, Public, Player }

    sealed class Conn(int id, string address, double now)
    {
        public readonly int Id = id;
        public readonly string Address = address;
        public readonly double Since = now;
        public Role Role;
        public Room? Room;
        /// <summary>A player's number in its game.</summary>
        public int Player;
        public double Listed = double.NegativeInfinity;
    }

    sealed class Room(int id, Conn host, LanGame game, string address)
    {
        public readonly int Id = id;
        public readonly Conn Host = host;
        public LanGame Game = game;
        public bool Playing;
        /// <summary>A dedicated server's "ip:port"; empty for a relayed game.</summary>
        public readonly string Address = address;
        public readonly SortedDictionary<int, Conn> Players = [];
        public int Next = 2;
        /// <summary>The byte budget: filled at the room's rate up to two seconds of it; in debt after reliable packets beyond it.</summary>
        public double Budget, At = double.NaN;
        public bool Relayed => Address.Length == 0;
    }

    readonly Action<int, byte[], int, int> send;
    readonly Action<int> drop;
    readonly Func<int, int> ping;
    readonly Action<string> log;
    readonly RelayLimits limits;
    readonly Dictionary<int, Conn> conns = [];
    readonly SortedDictionary<int, Room> rooms = [];
    int nextRoom = 1;

    /// <summary>Bytes received and sent, frames whole, before compression; unreliable packets left out over a game's budget.</summary>
    public long BytesIn { get; private set; }
    public long BytesOut { get; private set; }
    public long Dropped { get; private set; }
    public RelayLimits Limits => limits;

    /// <summary>
    /// `send(connection, bytes, channel, flags)` and `drop(connection)` (once what was sent to it
    /// has gone) act on the sockets; `ping(connection)` is its round trip in ms; `log` takes a line.
    /// </summary>
    public RelayHub(Action<int, byte[], int, int> send, Action<int> drop, Func<int, int> ping, Action<string> log, RelayLimits? limits = null)
    {
        (this.send, this.drop, this.ping, this.log) = (send, drop, ping, log);
        this.limits = limits ?? new RelayLimits();
    }

    /// <summary>Games listed, relayed ones, players in them, and connections waiting on the list.</summary>
    public (int Rooms, int Relayed, int Players, int Listers) Load =>
        (rooms.Count, rooms.Values.Count(r => r.Relayed), rooms.Values.Sum(r => r.Players.Count), conns.Values.Count(c => c.Role == Role.Lister));

    /// <summary>The games as the multiplayer screen lists them.</summary>
    public IReadOnlyList<OnlineGame> Games =>
        [.. rooms.Values.Select(r => new OnlineGame(r.Id, r.Relayed, r.Game, r.Playing, r.Address, r.Relayed ? ping(r.Host.Id) : -1))];

    public void Connected(int conn, string address, double now)
    {
        var c = new Conn(conn, address, now);
        conns[conn] = c;
        if (conns.Count > limits.Connections) Close(c, "Le serveur en ligne est saturé : réessaie dans un moment.");
    }

    /// <summary>A connection went by itself (or after being let go): its game closes, or its host hears of it.</summary>
    public void Disconnected(int conn)
    {
        if (conns.TryGetValue(conn, out var c)) Gone(c, "L'hôte a quitté la partie.");
    }

    /// <summary>A packet from a connection, on its channel, with ENet's flags.</summary>
    public void Received(int conn, byte[] bytes, int channel, int flags, double now)
    {
        if (!conns.TryGetValue(conn, out var c)) return;
        BytesIn += bytes.Length;
        RelayFrame f;
        try
        {
            f = Relay.Decode(bytes);
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException)
        {
            Close(c, "Message illisible pour le serveur en ligne : versions incompatibles ?");
            return;
        }
        switch (c.Role, f)
        {
            case (Role.New or Role.Lister, RelayFrame.List l):
                if (!SameWire(c, l.Wire)) return;
                c.Role = Role.Lister;
                // A screen asking more than twice a second is answered at that pace.
                if (now - c.Listed < 0.5) return;
                c.Listed = now;
                Send(c, new RelayFrame.Games(Games));
                break;
            case (Role.New or Role.Lister, RelayFrame.Open o): Open(c, o); break;
            case (Role.New or Role.Lister, RelayFrame.Join j): Join(c, j); break;
            case (Role.Host or Role.Public, RelayFrame.Update u):
                c.Room!.Game = u.Game;
                c.Room.Playing = u.Playing;
                break;
            case (Role.Host, RelayFrame.ToPlayer t):
                // A player who just left may still be written to: nothing to carry.
                if (c.Room!.Players.TryGetValue(t.Player, out var to)) Carry(c.Room, to, new RelayFrame.FromHost(t.Payload), channel, flags, now);
                break;
            case (Role.Host, RelayFrame.Kick k):
                if (!c.Room!.Players.Remove(k.Player, out var kicked)) return;
                conns.Remove(kicked.Id);
                drop(kicked.Id);
                Send(c, new RelayFrame.Went(k.Player));
                log($"Joueur {k.Player} renvoyé par l'hôte de la partie {c.Room.Id}");
                break;
            case (Role.Player, RelayFrame.ToHost t): Carry(c.Room!, c.Room!.Host, new RelayFrame.FromPlayer(c.Player, t.Payload), channel, flags, now); break;
            default: Close(c, "Message inattendu pour le serveur en ligne."); break;
        }
    }

    /// <summary>Lets go the connections that never said what they are, and the screens that stopped asking for the list.</summary>
    public void Tick(double now)
    {
        foreach (var c in conns.Values.ToList())
        {
            if (c.Role == Role.New && now - c.Since > limits.HelloWait) Close(c, "Pas de réponse.");
            else if (c.Role == Role.Lister && now - c.Listed > limits.ListWait) Close(c, "Liste inactive.");
        }
    }

    bool SameWire(Conn c, int wire)
    {
        if (wire == Relay.Wire) return true;
        Close(c, $"Serveur en ligne d'une autre version (relais {Relay.Wire}, ton jeu {wire}) : il faut mettre à jour le jeu ou le serveur.");
        return false;
    }

    void Open(Conn c, RelayFrame.Open o)
    {
        if (!SameWire(c, o.Wire)) return;
        if (rooms.Count >= limits.Rooms)
        {
            Close(c, $"Le serveur en ligne est complet ({limits.Rooms} parties) : réessaie plus tard.");
            return;
        }
        var address = o.Public ? Relay.Endpoint(o.Address.Length > 0 ? o.Address : c.Address, o.Game.Port) : "";
        var room = new Room(nextRoom++, c, o.Game, address);
        rooms[room.Id] = room;
        (c.Role, c.Room) = (o.Public ? Role.Public : Role.Host, room);
        Send(c, new RelayFrame.Opened(room.Id));
        log(o.Public ? $"Serveur dédié {room.Id} « {o.Game.Name} » inscrit à {address} ({o.Game.Mode}, version {o.Game.Version})"
            : $"Partie {room.Id} « {o.Game.Name} » ouverte par {c.Address} ({o.Game.Mode}, {o.Game.Taken}/{o.Game.Seats}, version {o.Game.Version})");
    }

    void Join(Conn c, RelayFrame.Join j)
    {
        if (!SameWire(c, j.Wire)) return;
        var why = !rooms.TryGetValue(j.Room, out var room) ? "Cette partie n'est plus listée."
            : !room.Relayed ? "Ce serveur dédié se rejoint directement, à son adresse."
            : j.Version != room.Game.Version ? $"Version différente : la partie est en {room.Game.Version}, ton jeu en {j.Version}."
            : j.Protocol != room.Game.Protocol ? $"Version du jeu incompatible avec l'hôte (réseau {j.Protocol}, attendu {room.Game.Protocol})."
            : room.Players.Count >= limits.PlayersPerRoom ? "La partie est complète sur le serveur en ligne."
            : null;
        if (why is not null)
        {
            log($"{c.Address} refusé : {why}");
            Close(c, why);
            return;
        }
        var player = room!.Next++;
        room.Players[player] = c;
        (c.Role, c.Room, c.Player) = (Role.Player, room, player);
        Send(c, new RelayFrame.Joined());
        Send(room.Host, new RelayFrame.Came(player));
        log($"Joueur {player} ({c.Address}) entre dans la partie {room.Id} « {room.Game.Name} »");
    }

    /// <summary>
    /// A game's message from one end to the other, on the channel it came on and as reliable as
    /// it came, within the game's budget: an unreliable one beyond it is left out, and a game
    /// several seconds in debt is closed.
    /// </summary>
    void Carry(Room room, Conn to, RelayFrame f, int channel, int flags, double now)
    {
        var bytes = Relay.Encode(f);
        var rate = limits.RoomBytesPerSecond;
        room.Budget = double.IsNaN(room.At) ? 2.0 * rate : Math.Min(2.0 * rate, room.Budget + (now - room.At) * rate);
        room.At = now;
        var reliable = (flags & Relay.Reliable) != 0;
        if (!reliable && room.Budget < bytes.Length)
        {
            Dropped++;
            return;
        }
        room.Budget -= bytes.Length;
        if (room.Budget < -4.0 * rate)
        {
            log($"Partie {room.Id} « {room.Game.Name} » : débit trop élevé");
            Close(room.Host, "Débit trop élevé pour le serveur en ligne : la partie est fermée.");
            return;
        }
        BytesOut += bytes.Length;
        send(to.Id, bytes, channel, Relay.Forwarded(flags));
    }

    void Send(Conn c, RelayFrame f)
    {
        var bytes = Relay.Encode(f);
        BytesOut += bytes.Length;
        send(c.Id, bytes, 0, Relay.Reliable);
    }

    /// <summary>Tells a connection why it goes, then lets it go: a host's game closes with it, for the same reason.</summary>
    void Close(Conn c, string why)
    {
        if (!conns.ContainsKey(c.Id)) return;
        Send(c, new RelayFrame.Closed(why));
        drop(c.Id);
        Gone(c, why);
    }

    /// <summary>A connection is no more: its game closes (each player told why), or its host hears that it went.</summary>
    void Gone(Conn c, string why)
    {
        conns.Remove(c.Id);
        if (c.Room is not { } room) return;
        if (c.Role == Role.Player)
        {
            if (room.Players.Remove(c.Player) && rooms.ContainsKey(room.Id))
            {
                Send(room.Host, new RelayFrame.Went(c.Player));
                log($"Joueur {c.Player} a quitté la partie {room.Id}");
            }
            return;
        }
        if (!rooms.Remove(room.Id)) return;
        foreach (var p in room.Players.Values.ToList()) Close(p, why);
        log($"{(room.Relayed ? "Partie" : "Serveur dédié")} {room.Id} « {room.Game.Name} » fermé{(room.Relayed ? "e" : "")} : {why}");
    }
}
