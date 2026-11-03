// What a host and its players say to each other (not in the web version), from the first Hello
// to the end of a battle; the game carries these messages over ENet (Game/scripts/Net), each
// sealed for the other end once their connection's handshake is done (Secure.cs). Each is
// a tag byte, then its fields in order, as commands are (CommandWire.cs); a lobby goes whole, as
// JSON. Reading never trusts the bytes: a malformed message throws InvalidDataException or
// EndOfStreamException.
//
//   player -> host  Hello, Ask, Launch (leader), Order, Pause, Back (leader), Plan, Watch, Ack, Say, Spectate
//   host -> player  Welcome, Bye, Room, Start, State, Refused, Pause, Notice, Back, News, Chart, Map, Tidings, Engage, Over, Said
//
// A player joins: once their channel is sealed (with the password, if the game has one), it says
// Hello with its versions, its name and whether it comes only to watch. The host answers Welcome and sends the Room
// to everyone, or Bye with the reason and closes. Every change to the lobby (Ask, applied
// by LobbyRules) sends the Room again. Start opens the battle: from then on the player sends its
// Orders, and the host a State (a snapshot) twenty times a second, unreliably, and Refused for
// an order it would not apply. The player acknowledges each snapshot it reads (Ack, unreliably):
// the host writes the next ones against it. The events that must not be lost (a victory, a
// mode's news) come apart, reliably, as News. Back returns everyone to the lobby.
//
// A campaign starts the same way. The player sends Plans (galaxy commands), and gets the Chart
// (what never changes) once, reliably, then a Map (a galaxy snapshot) a few times a second,
// unreliably, with its notices as Tidings, reliably.
// When a battle is commanded, Engage opens it on the screen of those who command it, and of
// those who asked to Watch it; State and News follow, as in a battle, until Over.
//
// At any time, in the lobby as in a game, a player's line of chat goes to the host (Say), which
// relays it to everyone, its writer included (Said; Chat.cs).
//
// A spectator joins as a player does, its Hello saying it only watches: it has no seat, and its
// orders are refused. In battle it picks whose eyes it watches through (Spectate). A player who
// left a game under way may come back under its name: the host gives its seat back, and starts
// the game again for it (Start, then a whole snapshot).
using System.Text;
using System.Text.Json;
using SandboxShip.Sim.Data;

namespace SandboxShip.Sim.Net;

public abstract record Message
{
    /// <summary>
    /// A player's first words, once its channel is sealed: the formats it speaks, the game's
    /// version, its name, and whether it comes only to watch.
    /// </summary>
    public sealed record Hello(int Protocol, int Commands, int Snapshots, string Version, string Name, bool Watches = false) : Message;
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
    /// <summary>A battle's events that must reach the player, as Snapshot.Write wrote them apart (`vital`): reliable, unlike State.</summary>
    public sealed record News(byte[] Events) : Message;
    /// <summary>One of the player's galaxy commands.</summary>
    public sealed record Plan(GalaxyCommand Command) : Message;
    /// <summary>The galaxy as the player's team knows it: a snapshot, as GalaxySnapshot.Write wrote it.</summary>
    public sealed record Map(byte[] Snapshot) : Message;
    /// <summary>The galaxy's notices for the player, as GalaxySnapshot.Write wrote them apart (`vital`): reliable, unlike Map.</summary>
    public sealed record Tidings(byte[] Events) : Message;
    /// <summary>A campaign battle opens for the player in the battle view: to command its side, or (Commands false) to watch it.</summary>
    public sealed record Engage(int Battle, bool Commands) : Message;
    /// <summary>The player asks to watch a commanded battle of the campaign, or to stop.</summary>
    public sealed record Watch(int Battle, bool On) : Message;
    /// <summary>The player read this battle snapshot (its number, Snapshot.Read): the host writes the next ones against it. Unreliable, like State.</summary>
    public sealed record Ack(int Snapshot) : Message;
    /// <summary>What never changes in the campaign, as GalaxySnapshot.Chart wrote it: once, reliably, before the first Map.</summary>
    public sealed record Chart(byte[] Bytes) : Message;
    /// <summary>The campaign battle the player commanded or watched is decided, or (no result) left to play on by itself: back to the galaxy.</summary>
    public sealed record Over(int Battle, BattleResult? Result) : Message;
    /// <summary>A line of chat the player writes, for the host to relay.</summary>
    public sealed record Say(string Text) : Message;
    /// <summary>A spectator picks whose eyes it watches the battle through: a side, or (-1) all of them, without fog.</summary>
    public sealed record Spectate(int Side) : Message;
    /// <summary>A line of chat, relayed to everyone: who wrote it (empty: the game itself, to that player alone) and what.</summary>
    public sealed record Said(string Who, string Text) : Message;
}

/// <summary>
/// A host on the local network, as it calls out on the beacon port every second; Dedicated: a
/// server nobody plays on (--server); Locked: joined only with its password. Direct: a game
/// listed by the online server whose host offers the direct link (Relay.cs); never in a beacon.
/// </summary>
public sealed record LanGame(int Protocol, string Version, string Name, LobbyMode Mode, int Taken, int Seats, int Port, bool Dedicated = false, bool Locked = false,
    bool Direct = false);

public static class Protocol
{
    /// <summary>
    /// Layout of the messages below: bumped with any change. Commands and battle snapshots have
    /// their own versions, checked as well; the galaxy snapshot's is part of this one. 2: the campaign.
    /// 3: acknowledged battle snapshots (Ack), the campaign's chart apart (Chart). 4: the chat (Say,
    /// Said), the challenge of a password (Challenge, Hello's key) and players who only watch (Hello's Watches).
    /// 5: every message sealed (Secure.cs), the password in the keys: no more Challenge nor Hello's key.
    /// 6: the lobby's terrain (Settings), with the host's consent to the direct link.
    /// </summary>
    public const int Version = 6;
    /// <summary>Where a host listens for players, unless told otherwise.</summary>
    public const int DefaultPort = 24680;
    /// <summary>Where hosts call out to the local network, and the join screen listens.</summary>
    public const int BeaconPort = 24681;
    /// <summary>Battle snapshots a second to each player.</summary>
    public const double Rate = 20;
    /// <summary>Galaxy snapshots a second to each player: the map moves slowly, fleets glide in between.</summary>
    public const double GalaxyRate = 5;

    const int MaxText = 200, MaxLobby = 64 * 1024, MaxMembers = 64;
    const uint BeaconMagic = 0x43425353;

    enum Tag : byte { Hello = 1, Welcome, Bye, Room, Ask, Launch, Start, Order, State, Refused, Pause, Notice, Back, News, Plan, Map, Tidings, Engage, Watch, Over, Ack, Chart, Say, Said, Spectate = 26 }

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
                w.Write(h.Watches);
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
                    w.Write(member.Watches);
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
            case Message.News n: w.Write((byte)Tag.News); w.Write(n.Events); break;
            case Message.Plan p: w.Write((byte)Tag.Plan); Commands.Write(w, p.Command); break;
            case Message.Map map: w.Write((byte)Tag.Map); w.Write(map.Snapshot); break;
            case Message.Tidings t: w.Write((byte)Tag.Tidings); w.Write(t.Events); break;
            case Message.Engage e: w.Write((byte)Tag.Engage); w.Write7BitEncodedInt(e.Battle); w.Write(e.Commands); break;
            case Message.Watch watch: w.Write((byte)Tag.Watch); w.Write7BitEncodedInt(watch.Battle); w.Write(watch.On); break;
            case Message.Ack a: w.Write((byte)Tag.Ack); w.Write7BitEncodedInt(a.Snapshot); break;
            case Message.Chart c: w.Write((byte)Tag.Chart); w.Write(c.Bytes); break;
            case Message.Over o:
                w.Write((byte)Tag.Over);
                w.Write7BitEncodedInt(o.Battle);
                w.Write(o.Result is BattleResult result ? (byte)result : byte.MaxValue);
                break;
            case Message.Say s: w.Write((byte)Tag.Say); w.Write(s.Text); break;
            case Message.Spectate s: w.Write((byte)Tag.Spectate); w.Write7BitEncodedInt(s.Side + 1); break;
            case Message.Said s: w.Write((byte)Tag.Said); w.Write(s.Who); w.Write(s.Text); break;
            default: throw new ArgumentException($"Message sans format : {m.GetType().Name}");
        }
    }

    public static Message Read(BinaryReader r) => (Tag)r.ReadByte() switch
    {
        Tag.Hello => new Message.Hello(r.ReadVarint(), r.ReadVarint(), r.ReadVarint(), Text(r), Text(r), r.ReadBoolean()),
        Tag.Welcome => new Message.Welcome(r.ReadVarint(), Text(r)),
        Tag.Bye => new Message.Bye(Text(r)),
        Tag.Room => new Message.Room(ReadLobby(r), ReadMembers(r)),
        Tag.Ask => new Message.Ask(ReadRequest(r)),
        Tag.Launch => new Message.Launch(),
        Tag.Start => new Message.Start(ReadLobby(r)),
        Tag.Order => new Message.Order(Commands.ReadCommand(r)),
        Tag.State => new Message.State(Rest(r)),
        Tag.Refused => new Message.Refused(Text(r)),
        Tag.Pause => new Message.Pause(r.ReadBoolean(), Text(r)),
        Tag.Notice => new Message.Notice(Text(r)),
        Tag.Back => new Message.Back(),
        Tag.News => new Message.News(Rest(r)),
        Tag.Plan => new Message.Plan(Commands.ReadGalaxyCommand(r)),
        Tag.Map => new Message.Map(Rest(r)),
        Tag.Tidings => new Message.Tidings(Rest(r)),
        Tag.Engage => new Message.Engage(r.ReadVarint(), r.ReadBoolean()),
        Tag.Watch => new Message.Watch(r.ReadVarint(), r.ReadBoolean()),
        Tag.Ack => new Message.Ack(r.ReadVarint()),
        Tag.Chart => new Message.Chart(Rest(r)),
        Tag.Over => new Message.Over(r.ReadVarint(), r.ReadByte() is var o && o != byte.MaxValue
            ? Enum.IsDefined((BattleResult)o) ? (BattleResult)o : throw new InvalidDataException($"Issue inconnue : {o}") : null),
        Tag.Say => new Message.Say(Text(r, Chat.MaxBytes)),
        Tag.Spectate => new Message.Spectate(r.ReadVarint() - 1),
        Tag.Said => new Message.Said(Text(r), Text(r, Chat.MaxBytes)),
        var tag => throw new InvalidDataException($"Message inconnu : {tag}"),
    };


    /// <summary>What is left of a message: a snapshot or events, read by their own reader.</summary>
    static byte[] Rest(BinaryReader r) => r.ReadBytes((int)(r.BaseStream.Length - r.BaseStream.Position));

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

    /// <summary>The Hello of this build of the game, and whether it only watches.</summary>
    public static Message.Hello Hello(string version, string name, bool watch = false) =>
        new(Version, Sim.Commands.WireVersion, Snapshot.Version, version, name, watch);

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
        var n = r.ReadVarint();
        if (n is < 0 or > MaxMembers) throw new InvalidDataException($"{n} joueurs");
        var members = new List<Member>(n);
        for (var i = 0; i < n; i++) members.Add(new Member(r.ReadVarint(), Text(r), r.ReadBoolean()));
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
                w.Write(v.Spectators);
                w.Write(v.Direct);
                w.Write((byte)v.Terrain);
                break;
            default: throw new ArgumentException($"Demande sans format : {q.GetType().Name}");
        }
    }

    static LobbyRequest ReadRequest(BinaryReader r) => r.ReadByte() switch
    {
        1 => new LobbyRequest.SetKind(r.ReadVarint(), r.ReadEnum<SeatKind>(), r.ReadEnum<Difficulty>()),
        2 => new LobbyRequest.SetRace(r.ReadVarint(), r.ReadEnum<Faction>()),
        3 => new LobbyRequest.SetTeam(r.ReadVarint(), r.ReadVarint()),
        4 => new LobbyRequest.SetColor(r.ReadVarint(), r.ReadUInt32()),
        5 => new LobbyRequest.SetReady(r.ReadBoolean()),
        6 => new LobbyRequest.AddBot(),
        7 => new LobbyRequest.Remove(r.ReadVarint()),
        8 => new LobbyRequest.Settings(new LobbySettings(r.ReadEnum<MapKind>(), r.ReadEnum<MapSize>(), r.ReadVarint(), r.ReadBoolean(),
            r.ReadBoolean(), r.ReadVarint(), r.ReadVarint(), r.ReadBoolean(), r.ReadBoolean(), r.ReadEnum<TerrainDensity>())),
        var tag => throw new InvalidDataException($"Demande inconnue : {tag}"),
    };

    /// <summary>A string of at most `max` UTF-8 bytes: a broken message must not ask for a huge one.</summary>
    internal static string Text(BinaryReader r, int max = MaxText)
    {
        var n = r.ReadVarint();
        if (n < 0 || n > max) throw new InvalidDataException($"Texte de {n} octets");
        var bytes = r.ReadBytes(n);
        if (bytes.Length != n) throw new EndOfStreamException();
        return Encoding.UTF8.GetString(bytes);
    }

    public static byte[] Encode(LanGame b)
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
            // Last, so that a beacon without them (an older build) still reads.
            w.Write(b.Dedicated);
            w.Write(b.Locked);
        }
        return ms.ToArray();
    }

    /// <summary>A beacon heard on the local network; null for anything else that came on that port.</summary>
    public static LanGame? DecodeLanGame(byte[] bytes)
    {
        try
        {
            using var r = new BinaryReader(new MemoryStream(bytes));
            if (r.ReadUInt32() != BeaconMagic) return null;
            var b = new LanGame(r.ReadVarint(), Text(r), Text(r), (LobbyMode)r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadUInt16(),
                r.BaseStream.Position < bytes.Length && r.ReadBoolean(), r.BaseStream.Position < bytes.Length && r.ReadBoolean());
            return Enum.IsDefined(b.Mode) ? b : null;
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException or FormatException)
        {
            return null;
        }
    }
}
