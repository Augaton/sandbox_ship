// What a host and its players say to each other (not in the web version), from the handshake
// to the end of a battle; the game carries these messages over ENet (Game/scripts/Net). Each is
// a tag byte, then its fields in order, as commands are (CommandWire.cs); a lobby goes whole, as
// JSON. Reading never trusts the bytes: a malformed message throws InvalidDataException or
// EndOfStreamException.
//
//   player -> host  Hello, Ask, Launch (leader), Order, Pause, Back (leader)
//   host -> player  Welcome, Bye, Room, Start, State, Refused, Pause, Notice, Back
//
// A player joins: Hello with its versions and name; the host answers Welcome and sends the
// Room to everyone, or Bye with the reason and closes. Every change to the lobby (Ask, applied
// by LobbyRules) sends the Room again. Start opens the battle: from then on the player sends its
// Orders, and the host a State (a snapshot) twenty times a second, unreliably, and Refused for
// an order it would not apply. Back returns everyone to the lobby.
using System.Text;
using System.Text.Json;
using SandboxShip.Sim.Data;

namespace SandboxShip.Sim.Net;

public abstract record Message
{
    /// <summary>A player's first words: the formats it speaks, the game's version, its name.</summary>
    public sealed record Hello(int Protocol, int Commands, int Snapshots, string Version, string Name) : Message;
    /// <summary>The host lets the player in: the peer it is known by, and the game's name.</summary>
    public sealed record Welcome(int Peer, string Game) : Message;
    /// <summary>The host turns a player away, or sends it off, and says why; the connection closes after it.</summary>
    public sealed record Bye(string Reason) : Message;
    /// <summary>The lobby as the host has it, after every change, and who is in it in the order they came.</summary>
    public sealed record Room(Lobby Lobby, IReadOnlyList<Member> Members) : Message;
    /// <summary>A player asks for a change to the lobby.</summary>
    public sealed record Ask(LobbyRequest Request) : Message;
    /// <summary>The leader of a dedicated server's lobby starts the battle.</summary>
    public sealed record Launch : Message;
    /// <summary>The battle starts, from this lobby.</summary>
    public sealed record Start(Lobby Lobby) : Message;
    /// <summary>One of the player's battle commands.</summary>
    public sealed record Order(Command Command) : Message;
    /// <summary>The battle as the player's team sees it: a snapshot, as Snapshot.Write wrote it.</summary>
    public sealed record State(byte[] Snapshot) : Message;
    /// <summary>One of the player's orders that the host would not apply, and why.</summary>
    public sealed record Refused(string Reason) : Message;
    /// <summary>A player asks for the game to stop or go on (Who empty); the host tells everyone, with who did it.</summary>
    public sealed record Pause(bool On, string Who) : Message;
    /// <summary>A line every player sees: someone left, the AI took over.</summary>
    public sealed record Notice(string Text) : Message;
    /// <summary>The battle is over, back to the lobby: asked by the leader, then told by the host.</summary>
    public sealed record Back : Message;
}

/// <summary>A host on the local network, as it calls out on the beacon port every second.</summary>
public sealed record Beacon(int Protocol, string Version, string Name, LobbyMode Mode, int Taken, int Seats, int Port);

public static class Protocol
{
    /// <summary>Layout of the messages below: bumped with any change. Commands and snapshots have their own versions, checked as well.</summary>
    public const int Version = 1;
    /// <summary>Where a host listens for players, unless told otherwise.</summary>
    public const int DefaultPort = 24680;
    /// <summary>Where hosts call out to the local network, and the join screen listens.</summary>
    public const int BeaconPort = 24681;
    /// <summary>Snapshots a second to each player.</summary>
    public const double Rate = 20;

    const int MaxText = 200, MaxLobby = 64 * 1024, MaxMembers = 64;
    const uint BeaconMagic = 0x43425353;

    enum Tag : byte { Hello = 1, Welcome, Bye, Room, Ask, Launch, Start, Order, State, Refused, Pause, Notice, Back }

    public static byte[] Encode(Message m)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms)) Write(w, m);
        return ms.ToArray();
    }

    public static Message Decode(byte[] bytes)
    {
        using var r = new BinaryReader(new MemoryStream(bytes));
        var m = Read(r);
        if (r.BaseStream.Position != bytes.Length) throw new InvalidDataException("Message trop long");
        return m;
    }

    public static void Write(BinaryWriter w, Message m)
    {
        switch (m)
        {
            case Message.Hello h:
                w.Write((byte)Tag.Hello);
                w.Write7BitEncodedInt(h.Protocol);
                w.Write7BitEncodedInt(h.Commands);
                w.Write7BitEncodedInt(h.Snapshots);
                w.Write(h.Version);
                w.Write(h.Name);
                break;
            case Message.Welcome welcome:
                w.Write((byte)Tag.Welcome);
                w.Write7BitEncodedInt(welcome.Peer);
                w.Write(welcome.Game);
                break;
            case Message.Bye b: w.Write((byte)Tag.Bye); w.Write(b.Reason); break;
            case Message.Room room:
                w.Write((byte)Tag.Room);
                WriteLobby(w, room.Lobby);
                w.Write7BitEncodedInt(room.Members.Count);
                foreach (var member in room.Members)
                {
                    w.Write7BitEncodedInt(member.Peer);
                    w.Write(member.Name);
                }
                break;
            case Message.Ask a: w.Write((byte)Tag.Ask); WriteRequest(w, a.Request); break;
            case Message.Launch: w.Write((byte)Tag.Launch); break;
            case Message.Start s: w.Write((byte)Tag.Start); WriteLobby(w, s.Lobby); break;
            case Message.Order o: w.Write((byte)Tag.Order); Commands.Write(w, o.Command); break;
            case Message.State s: w.Write((byte)Tag.State); w.Write(s.Snapshot); break;
            case Message.Refused r: w.Write((byte)Tag.Refused); w.Write(r.Reason); break;
            case Message.Pause p: w.Write((byte)Tag.Pause); w.Write(p.On); w.Write(p.Who); break;
            case Message.Notice n: w.Write((byte)Tag.Notice); w.Write(n.Text); break;
            case Message.Back: w.Write((byte)Tag.Back); break;
            default: throw new ArgumentException($"Message sans format : {m.GetType().Name}");
        }
    }

    public static Message Read(BinaryReader r) => (Tag)r.ReadByte() switch
    {
        Tag.Hello => new Message.Hello(r.Read7BitEncodedInt(), r.Read7BitEncodedInt(), r.Read7BitEncodedInt(), Text(r), Text(r)),
        Tag.Welcome => new Message.Welcome(r.Read7BitEncodedInt(), Text(r)),
        Tag.Bye => new Message.Bye(Text(r)),
        Tag.Room => new Message.Room(ReadLobby(r), ReadMembers(r)),
        Tag.Ask => new Message.Ask(ReadRequest(r)),
        Tag.Launch => new Message.Launch(),
        Tag.Start => new Message.Start(ReadLobby(r)),
        Tag.Order => new Message.Order(Commands.ReadCommand(r)),
        Tag.State => new Message.State(r.ReadBytes((int)(r.BaseStream.Length - r.BaseStream.Position))),
        Tag.Refused => new Message.Refused(Text(r)),
        Tag.Pause => new Message.Pause(r.ReadBoolean(), Text(r)),
        Tag.Notice => new Message.Notice(Text(r)),
        Tag.Back => new Message.Back(),
        var tag => throw new InvalidDataException($"Message inconnu : {tag}"),
    };

    /// <summary>
    /// Why a player's Hello cannot join this game, in words for that player; null when it can.
    /// The game's version speaks to players; the formats behind it are checked as well.
    /// </summary>
    public static string? Mismatch(Message.Hello h, string version)
    {
        if (h.Version != version) return $"Version différente : la partie est en {version}, ton jeu en {h.Version}.";
        if (h.Protocol != Version || h.Commands != Sim.Commands.WireVersion || h.Snapshots != Snapshot.Version)
        {
            return $"Version du jeu incompatible avec l'hôte (formats {h.Protocol}.{h.Commands}.{h.Snapshots}, "
                + $"attendus {Version}.{Sim.Commands.WireVersion}.{Snapshot.Version}).";
        }
        return null;
    }

    /// <summary>The Hello of this build of the game.</summary>
    public static Message.Hello Hello(string version, string name) => new(Version, Sim.Commands.WireVersion, Snapshot.Version, version, name);

    static void WriteLobby(BinaryWriter w, Lobby l) => w.Write(JsonSerializer.Serialize(l, Lobby.Json));

    /// <summary>A lobby from the host, held to the same checks as one read from a file: Sane, then Fit.</summary>
    static Lobby ReadLobby(BinaryReader r)
    {
        Lobby? l;
        try
        {
            l = JsonSerializer.Deserialize<Lobby>(Text(r, MaxLobby), Lobby.Json);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"Salon illisible : {e.Message}");
        }
        if (l is null || !l.Sane()) throw new InvalidDataException("Salon incohérent");
        l.Fit();
        return l;
    }

    static List<Member> ReadMembers(BinaryReader r)
    {
        var n = r.Read7BitEncodedInt();
        if (n is < 0 or > MaxMembers) throw new InvalidDataException($"{n} joueurs");
        var members = new List<Member>(n);
        for (var i = 0; i < n; i++) members.Add(new Member(r.Read7BitEncodedInt(), Text(r)));
        return members;
    }

    static void WriteRequest(BinaryWriter w, LobbyRequest q)
    {
        switch (q)
        {
            case LobbyRequest.SetKind k: w.Write((byte)1); w.Write7BitEncodedInt(k.Seat); w.Write((byte)k.Kind); w.Write((byte)k.Difficulty); break;
            case LobbyRequest.SetRace s: w.Write((byte)2); w.Write7BitEncodedInt(s.Seat); w.Write((byte)s.Race); break;
            case LobbyRequest.SetTeam t: w.Write((byte)3); w.Write7BitEncodedInt(t.Seat); w.Write7BitEncodedInt(t.Team); break;
            case LobbyRequest.SetColor c: w.Write((byte)4); w.Write7BitEncodedInt(c.Seat); w.Write(c.Color); break;
            case LobbyRequest.SetReady s: w.Write((byte)5); w.Write(s.Ready); break;
            case LobbyRequest.AddBot: w.Write((byte)6); break;
            case LobbyRequest.Remove x: w.Write((byte)7); w.Write7BitEncodedInt(x.Seat); break;
            case LobbyRequest.Settings { Values: var v }:
                w.Write((byte)8);
                w.Write((byte)v.Map);
                w.Write((byte)v.Size);
                w.Write7BitEncodedInt(v.Seed);
                w.Write(v.Raid);
                w.Write(v.Fog);
                w.Write7BitEncodedInt(v.ZoneTarget);
                w.Write7BitEncodedInt(v.AssaultMinutes);
                break;
            default: throw new ArgumentException($"Demande sans format : {q.GetType().Name}");
        }
    }

    static LobbyRequest ReadRequest(BinaryReader r) => r.ReadByte() switch
    {
        1 => new LobbyRequest.SetKind(r.Read7BitEncodedInt(), (SeatKind)r.ReadByte(), (Difficulty)r.ReadByte()),
        2 => new LobbyRequest.SetRace(r.Read7BitEncodedInt(), (Faction)r.ReadByte()),
        3 => new LobbyRequest.SetTeam(r.Read7BitEncodedInt(), r.Read7BitEncodedInt()),
        4 => new LobbyRequest.SetColor(r.Read7BitEncodedInt(), r.ReadUInt32()),
        5 => new LobbyRequest.SetReady(r.ReadBoolean()),
        6 => new LobbyRequest.AddBot(),
        7 => new LobbyRequest.Remove(r.Read7BitEncodedInt()),
        8 => new LobbyRequest.Settings(new LobbySettings((MapKind)r.ReadByte(), (MapSize)r.ReadByte(), r.Read7BitEncodedInt(), r.ReadBoolean(),
            r.ReadBoolean(), r.Read7BitEncodedInt(), r.Read7BitEncodedInt())),
        var tag => throw new InvalidDataException($"Demande inconnue : {tag}"),
    };

    /// <summary>A string of at most `max` UTF-8 bytes: a broken message must not ask for a huge one.</summary>
    static string Text(BinaryReader r, int max = MaxText)
    {
        var n = r.Read7BitEncodedInt();
        if (n < 0 || n > max) throw new InvalidDataException($"Texte de {n} octets");
        var bytes = r.ReadBytes(n);
        if (bytes.Length != n) throw new EndOfStreamException();
        return Encoding.UTF8.GetString(bytes);
    }

    public static byte[] Encode(Beacon b)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms))
        {
            w.Write(BeaconMagic);
            w.Write7BitEncodedInt(b.Protocol);
            w.Write(b.Version);
            w.Write(b.Name);
            w.Write((byte)b.Mode);
            w.Write((byte)b.Taken);
            w.Write((byte)b.Seats);
            w.Write((ushort)b.Port);
        }
        return ms.ToArray();
    }

    /// <summary>A beacon heard on the local network; null for anything else that came on that port.</summary>
    public static Beacon? DecodeBeacon(byte[] bytes)
    {
        try
        {
            using var r = new BinaryReader(new MemoryStream(bytes));
            if (r.ReadUInt32() != BeaconMagic) return null;
            var b = new Beacon(r.Read7BitEncodedInt(), Text(r), Text(r), (LobbyMode)r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadUInt16());
            return Enum.IsDefined(b.Mode) ? b : null;
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException or FormatException)
        {
            return null;
        }
    }
}
