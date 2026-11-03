// What hosts, players and the multiplayer screen say to the online server (not in the web
// version), and what it answers: a list of games, and a relay between each game's host and its
// players, so that nobody has to open a port. The game's own messages (Protocol.cs) ride inside
// as they are, on the channel and with the reliability their sender gave them. Each frame is a
// tag byte, then its fields in order, as in Protocol.cs. Reading never trusts the bytes: a
// malformed frame throws InvalidDataException or EndOfStreamException.
//
//   menu -> server    List
//   host -> server    Open, Update, ToPlayer, Kick
//   player -> server  Join, ToHost
//   server -> them    Games, Opened, Joined, Came, Went, FromPlayer, FromHost, Closed, Meet
//
// The multiplayer screen asks for the List and gets the Games. A host Opens its game and keeps
// the connection: the game is listed while it lasts, and Updates say how full it is. A player
// Joins one: the server tells the host a player Came, under a number of its own, and from then
// on carries ToHost as FromPlayer and ToPlayer as FromHost. Either leaving tells the other
// (Went, or Closed with the reason); the server says Closed, and why, before it lets anyone go.
// A dedicated server Opens as Public: listed with its own address, joined directly.
//
// The direct link, from relay format 3 on, and only when both ends accept it: a host that offers
// it (Direct in its game) and a player who Joins its game accepting it are each told where the
// other is, as the server sees them (Meet), with a token: each sends a few packets to the other
// from the connection it holds with the server, which opens the way through their boxes, and the
// player then connects straight to its host, the token in its connection's data. Their messages
// then go that way; the relay stays, for when it does not get through. Each then knows the other's
// address: without both consents the server tells nobody where anybody is, and everything stays
// relayed. A machine of an older format is never introduced.
using System.Text;
using SandboxShip.Sim.Data;

namespace SandboxShip.Sim.Net;

public abstract record RelayFrame
{
    /// <summary>The multiplayer screen asks for the games listed, in the relay's format.</summary>
    public sealed record List(int Wire) : RelayFrame;
    /// <summary>
    /// A host opens its game, relayed through the server; or (Public) a dedicated server lists
    /// itself, to be joined at Address (or else where the server sees it from) and Game.Port.
    /// </summary>
    public sealed record Open(int Wire, LanGame Game, bool Public, string Address) : RelayFrame;
    /// <summary>What the list says of the host's game: its players, and whether a battle or a campaign is under way.</summary>
    public sealed record Update(LanGame Game, bool Playing) : RelayFrame;
    /// <summary>A player joins a relayed game, with the formats its game speaks, and whether it accepts the direct link (format 3 on).</summary>
    public sealed record Join(int Wire, int Room, int Protocol, string Version, bool Direct = false) : RelayFrame;
    /// <summary>A host's message for one of its players: a Protocol message, carried as it is.</summary>
    public sealed record ToPlayer(int Player, byte[] Payload) : RelayFrame;
    /// <summary>A player's message for its host.</summary>
    public sealed record ToHost(byte[] Payload) : RelayFrame;
    /// <summary>The host lets one of its players go (after its Bye).</summary>
    public sealed record Kick(int Player) : RelayFrame;

    /// <summary>Every game listed.</summary>
    public sealed record Games(IReadOnlyList<OnlineGame> Listed) : RelayFrame;
    /// <summary>The host's game is listed, under this number.</summary>
    public sealed record Opened(int Room) : RelayFrame;
    /// <summary>The player is in: its host hears of it (Came), and its Hello may go.</summary>
    public sealed record Joined : RelayFrame;
    /// <summary>To a host: a player came, known from now on by this number (2 or more, as ENet numbers them).</summary>
    public sealed record Came(int Player) : RelayFrame;
    /// <summary>To a host: that player is gone.</summary>
    public sealed record Went(int Player) : RelayFrame;
    public sealed record FromPlayer(int Player, byte[] Payload) : RelayFrame;
    public sealed record FromHost(byte[] Payload) : RelayFrame;
    /// <summary>The server lets this connection go, and says why; it closes after it.</summary>
    public sealed record Closed(string Reason) : RelayFrame;
    /// <summary>
    /// To a host: one of its players (Player) is at Address:Port as the server sees it; to a
    /// player: its host (Player 1) is. Token binds the straight connection the player then opens.
    /// </summary>
    public sealed record Meet(int Player, string Address, int Port, int Token) : RelayFrame;
}

/// <summary>
/// A game as the online server lists it: relayed (joined through the server, Ping its host's
/// round trip to it, in ms) or a dedicated server (joined directly at Address, "ip:port"; Ping -1).
/// </summary>
public sealed record OnlineGame(int Room, bool Relayed, LanGame Game, bool Playing, string Address, int Ping);

public static class Relay
{
    /// <summary>
    /// Layout of the frames above: bumped with any change. The games carried keep their own
    /// versions, which the server does not need to know. 2: Meet. 3: the direct link only by
    /// consent (Direct in a game, in a Join). A server takes MinWire on.
    /// </summary>
    public const int Wire = 3, MinWire = 1;
    /// <summary>Where the online server listens, unless told otherwise.</summary>
    public const int DefaultPort = 27029;
    /// <summary>
    /// Seconds before a link's round trip means something: ENet starts it at 500 ms and brings it
    /// down an eighth of the way each ping; at one ping every 100 ms, it is within a few ms by then.
    /// The game's links and both online servers (the game's --relay and godot/Relay) wait as long.
    /// </summary>
    public const double PingSettle = 4;
    /// <summary>As Godot's own ENet peer: reliable messages on channel 0, unreliable ones on 1.</summary>
    public const int Channels = 2;
    /// <summary>ENet's packet flags, as Godot names them (ENetPacketPeer.Flag*).</summary>
    public const int Reliable = 1, Unsequenced = 2, UnreliableFragment = 8;
    /// <summary>The largest game message carried: far above the largest snapshot or lobby, far below what would hurt the server.</summary>
    public const int MaxPayload = 1 << 20;
    /// <summary>The largest frame, a payload and its header.</summary>
    public const int MaxFrame = MaxPayload + 16;

    const int MaxGames = 1024, MaxSeats = 64;

    enum Tag : byte { List = 1, Open, Update, Join, ToPlayer, ToHost, Kick, Games = 32, Opened, Joined, Came, Went, FromPlayer, FromHost, Closed, Meet }

    public static byte[] Encode(RelayFrame f)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms)) Write(w, f);
        return ms.ToArray();
    }

    public static RelayFrame Decode(byte[] bytes)
    {
        if (bytes.Length > MaxFrame) throw new InvalidDataException($"Message de {bytes.Length} octets");
        using var r = new BinaryReader(new MemoryStream(bytes));
        var f = Read(r);
        if (r.BaseStream.Position != bytes.Length) throw new InvalidDataException("Message trop long");
        return f;
    }

    static void Write(BinaryWriter w, RelayFrame f)
    {
        switch (f)
        {
            case RelayFrame.List l: w.Write((byte)Tag.List); w.Write7BitEncodedInt(l.Wire); break;
            case RelayFrame.Open o:
                w.Write((byte)Tag.Open);
                w.Write7BitEncodedInt(o.Wire);
                WriteGame(w, o.Game);
                w.Write(o.Public);
                w.Write(o.Address);
                break;
            case RelayFrame.Update u: w.Write((byte)Tag.Update); WriteGame(w, u.Game); w.Write(u.Playing); break;
            case RelayFrame.Join j:
                w.Write((byte)Tag.Join);
                w.Write7BitEncodedInt(j.Wire);
                w.Write7BitEncodedInt(j.Room);
                w.Write7BitEncodedInt(j.Protocol);
                w.Write(j.Version);
                if (j.Wire >= 3) w.Write(j.Direct);
                break;
            case RelayFrame.ToPlayer t: w.Write((byte)Tag.ToPlayer); w.Write7BitEncodedInt(t.Player); w.Write(t.Payload); break;
            case RelayFrame.ToHost t: w.Write((byte)Tag.ToHost); w.Write(t.Payload); break;
            case RelayFrame.Kick k: w.Write((byte)Tag.Kick); w.Write7BitEncodedInt(k.Player); break;
            case RelayFrame.Games g:
                w.Write((byte)Tag.Games);
                w.Write7BitEncodedInt(g.Listed.Count);
                foreach (var game in g.Listed)
                {
                    w.Write7BitEncodedInt(game.Room);
                    w.Write(game.Relayed);
                    WriteGame(w, game.Game);
                    w.Write(game.Playing);
                    w.Write(game.Address);
                    w.Write7BitEncodedInt(game.Ping + 1);
                }
                break;
            case RelayFrame.Opened o: w.Write((byte)Tag.Opened); w.Write7BitEncodedInt(o.Room); break;
            case RelayFrame.Joined: w.Write((byte)Tag.Joined); break;
            case RelayFrame.Came c: w.Write((byte)Tag.Came); w.Write7BitEncodedInt(c.Player); break;
            case RelayFrame.Went g: w.Write((byte)Tag.Went); w.Write7BitEncodedInt(g.Player); break;
            case RelayFrame.FromPlayer p: w.Write((byte)Tag.FromPlayer); w.Write7BitEncodedInt(p.Player); w.Write(p.Payload); break;
            case RelayFrame.FromHost h: w.Write((byte)Tag.FromHost); w.Write(h.Payload); break;
            case RelayFrame.Closed c: w.Write((byte)Tag.Closed); w.Write(c.Reason); break;
            case RelayFrame.Meet m:
                w.Write((byte)Tag.Meet);
                w.Write7BitEncodedInt(m.Player);
                w.Write(m.Address);
                w.Write((ushort)m.Port);
                w.Write(m.Token);
                break;
            default: throw new ArgumentException($"Message sans format : {f.GetType().Name}");
        }
    }

    static RelayFrame Read(BinaryReader r) => (Tag)r.ReadByte() switch
    {
        Tag.List => new RelayFrame.List(Count(r)),
        Tag.Open => new RelayFrame.Open(Count(r), ReadGame(r), r.ReadBoolean(), Protocol.Text(r)),
        Tag.Update => new RelayFrame.Update(ReadGame(r), r.ReadBoolean()),
        Tag.Join => ReadJoin(r),
        Tag.ToPlayer => new RelayFrame.ToPlayer(Count(r), Payload(r)),
        Tag.ToHost => new RelayFrame.ToHost(Payload(r)),
        Tag.Kick => new RelayFrame.Kick(Count(r)),
        Tag.Games => new RelayFrame.Games(ReadGames(r)),
        Tag.Opened => new RelayFrame.Opened(Count(r)),
        Tag.Joined => new RelayFrame.Joined(),
        Tag.Came => new RelayFrame.Came(Count(r)),
        Tag.Went => new RelayFrame.Went(Count(r)),
        Tag.FromPlayer => new RelayFrame.FromPlayer(Count(r), Payload(r)),
        Tag.FromHost => new RelayFrame.FromHost(Payload(r)),
        Tag.Closed => new RelayFrame.Closed(Protocol.Text(r)),
        Tag.Meet => new RelayFrame.Meet(Count(r), Protocol.Text(r), r.ReadUInt16(), r.ReadInt32()),
        var tag => throw new InvalidDataException($"Message inconnu : {tag}"),
    };

    /// <summary>A Join: the consent to the direct link only from format 3 on, an older game never giving it.</summary>
    static RelayFrame.Join ReadJoin(BinaryReader r)
    {
        var (wire, room, protocol, version) = (Count(r), Count(r), Count(r), Protocol.Text(r));
        return new RelayFrame.Join(wire, room, protocol, version, wire >= 3 && r.ReadBoolean());
    }

    /// <summary>A number that cannot be negative: a version, a game, a player.</summary>
    static int Count(BinaryReader r)
    {
        var n = r.ReadVarint();
        return n >= 0 ? n : throw new InvalidDataException($"Nombre négatif : {n}");
    }

    /// <summary>A game message: the rest of the frame, never empty.</summary>
    static byte[] Payload(BinaryReader r)
    {
        var n = r.BaseStream.Length - r.BaseStream.Position;
        if (n is < 1 or > MaxPayload) throw new InvalidDataException($"Message de jeu de {n} octets");
        return r.ReadBytes((int)n);
    }

    /// <summary>
    /// The bits of a game's mode byte that say it is locked, and that its host offers the direct
    /// link: a server of an older build carries them along with the mode, unread.
    /// </summary>
    const int LockedBit = 0x80, DirectBit = 0x40;

    /// <summary>A game as its host says it, the mode left as it is: a newer game's mode is still listed, as of another version.</summary>
    static void WriteGame(BinaryWriter w, LanGame g)
    {
        w.Write7BitEncodedInt(g.Protocol);
        w.Write(g.Version);
        w.Write(g.Name);
        w.Write((byte)((int)g.Mode | (g.Locked ? LockedBit : 0) | (g.Direct ? DirectBit : 0)));
        w.Write((byte)g.Taken);
        w.Write((byte)g.Seats);
        w.Write((ushort)g.Port);
        w.Write(g.Dedicated);
    }

    static LanGame ReadGame(BinaryReader r)
    {
        var (protocol, version, name, mode) = (Count(r), Protocol.Text(r), Protocol.Text(r), r.ReadByte());
        var g = new LanGame(protocol, version, name, (LobbyMode)(mode & ~(LockedBit | DirectBit)), r.ReadByte(), r.ReadByte(), r.ReadUInt16(), r.ReadBoolean(),
            (mode & LockedBit) != 0, (mode & DirectBit) != 0);
        if (g.Seats > MaxSeats || g.Taken > g.Seats) throw new InvalidDataException($"{g.Taken} joueurs sur {g.Seats}");
        return g;
    }

    static List<OnlineGame> ReadGames(BinaryReader r)
    {
        var n = Count(r);
        if (n > MaxGames) throw new InvalidDataException($"{n} parties");
        var games = new List<OnlineGame>(n);
        for (var i = 0; i < n; i++) games.Add(new OnlineGame(Count(r), r.ReadBoolean(), ReadGame(r), r.ReadBoolean(), Protocol.Text(r), Count(r) - 1));
        return games;
    }

    /// <summary>
    /// The flags a relayed packet leaves with: reliable stays reliable; the rest stays unreliable
    /// (unsequenced if it came so), fragmented unreliably as Godot's ENet peer sends it. Without
    /// that flag ENet would send a large unreliable packet as reliable fragments.
    /// </summary>
    public static int Forwarded(int flags) => (flags & Reliable) != 0 ? Reliable : (flags & Unsequenced) | UnreliableFragment;

    /// <summary>"host", "host:port" or "[ipv6]:port"; without a port, `port`. An empty host when there is none.</summary>
    public static (string Host, int Port) Address(string text, int port)
    {
        text = text.Trim();
        static int? PortOf(string s) => int.TryParse(s, out var p) && p is > 0 and < 65536 ? p : null;
        if (text.StartsWith('[') && text.IndexOf(']') is var end and > 0)
        {
            return (text[1..end], text.Length > end + 2 && text[end + 1] == ':' && PortOf(text[(end + 2)..]) is int v6 ? v6 : port);
        }
        var colon = text.LastIndexOf(':');
        if (colon > 0 && text.IndexOf(':') == colon && PortOf(text[(colon + 1)..]) is int p) return (text[..colon], p);
        return (text, port);
    }

    /// <summary>An address and a port as one types them: "[ipv6]:port" for an IPv6 address.</summary>
    public static string Endpoint(string host, int port) => host.Contains(':') ? $"[{host}]:{port}" : $"{host}:{port}";
}
