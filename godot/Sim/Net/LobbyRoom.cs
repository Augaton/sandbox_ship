// A lobby shared over the network (not in the web version): who may change what, and who is in
// it. Every change to a lobby, a local one included, is a request that LobbyRules applies for
// the one asking; the host applies those of its players and sends the lobby back to all of
// them, whole, after each change. A player who asks also applies its request at once to its own
// copy, by the same rules, so that the menu answers before the host does.
using SandboxShip.Sim.Data;

namespace SandboxShip.Sim.Net;

/// <summary>A change to a lobby, as the menu asks for it; seats by id.</summary>
public abstract record LobbyRequest
{
    /// <summary>Who takes a seat. Human: the one asking sits there, leaving its old seat open; a bot plays at `Difficulty`.</summary>
    public sealed record SetKind(int Seat, SeatKind Kind, Difficulty Difficulty = Difficulty.Normal) : LobbyRequest;
    public sealed record SetRace(int Seat, Faction Race) : LobbyRequest;
    /// <summary>How a bot plays; null: drawn at the start.</summary>
    public sealed record SetPersonality(int Seat, Personality? Personality) : LobbyRequest;
    public sealed record SetTeam(int Seat, int Team) : LobbyRequest;
    public sealed record SetColor(int Seat, uint Color) : LobbyRequest;
    /// <summary>The one asking is ready to start, or no longer.</summary>
    public sealed record SetReady(bool Ready) : LobbyRequest;
    public sealed record AddBot : LobbyRequest;
    public sealed record Remove(int Seat) : LobbyRequest;
    /// <summary>The map and the rules, all at once.</summary>
    public sealed record Settings(LobbySettings Values) : LobbyRequest;
}

/// <summary>A lobby's map and rules: everything but its mode and its seats; whether it takes spectators, offers the direct link, and its terrain.</summary>
public sealed record LobbySettings(MapKind Map, MapSize Size, int Seed, bool Raid, bool Fog, int ZoneTarget, int AssaultMinutes, bool Spectators = true,
    bool Direct = false, TerrainDensity Terrain = TerrainDensity.Light)
{
    public static LobbySettings Of(Lobby l) => new(l.Map, l.Size, l.Seed, l.Raid, l.Fog, l.ZoneTarget, l.AssaultMinutes, l.Spectators, l.Direct, l.Terrain);

    /// <summary>Known values only: a map, a size, a terrain, a seed from 1, a zone target and an assault length the lobby offers.</summary>
    public bool Sane => Enum.IsDefined(Map) && Enum.IsDefined(Size) && Enum.IsDefined(Terrain) && Seed >= 1
        && Lobby.ZoneTargets.Contains(ZoneTarget) && Lobby.AssaultLengths.Contains(AssaultMinutes);
}

/// <summary>
/// Who may change what. The leader (the host, or on a dedicated server the first player in) has
/// every right: the settings and every seat. Any other player only its own seat (race, team,
/// color, ready) or an open seat to move to. A remote player always keeps a seat: it cannot
/// leave its own, and when the leader takes it away, LobbyRoom.Seatless names it to be sent off.
/// </summary>
public static class LobbyRules
{
    /// <summary>Applies `peer`'s request if its rights allow it; false when refused or meaningless.</summary>
    public static bool Apply(Lobby l, int peer, bool leads, LobbyRequest request)
    {
        if (l.Resumed) return Resumed(l, peer, leads, request);
        var own = l.Seats.Find(s => s.Kind == SeatKind.Human && s.Peer == peer);
        Seat? At(int id) => l.Seats.Find(s => s.Id == id);
        bool Mine(Seat seat) => leads || seat == own;
        switch (request)
        {
            case LobbyRequest.SetKind { Kind: SeatKind.Human } k:
            {
                // Sitting down: on an open seat, or for the leader any seat no other player holds.
                if (At(k.Seat) is not { } seat || seat == own || seat.Kind == SeatKind.Human) return false;
                if (seat.Kind != SeatKind.Open && !leads) return false;
                l.SetKind(seat, SeatKind.Human, peer);
                if (own is not null) l.SetKind(own, SeatKind.Open);
                return true;
            }
            case LobbyRequest.SetKind k:
            {
                if (!leads || !Enum.IsDefined(k.Kind) || !Enum.IsDefined(k.Difficulty) || At(k.Seat) is not { } seat) return false;
                if (seat == own && peer != Lobby.Host) return false;
                l.SetKind(seat, k.Kind);
                if (k.Kind == SeatKind.Bot) seat.Difficulty = k.Difficulty;
                return true;
            }
            case LobbyRequest.SetPersonality p:
            {
                if (!leads || At(p.Seat) is not { Kind: SeatKind.Bot } seat || p.Personality is { } known && !Enum.IsDefined(known)) return false;
                seat.Personality = p.Personality;
                return true;
            }
            case LobbyRequest.SetRace r:
            {
                if (At(r.Seat) is not { } seat || !Mine(seat) || !Factions.All.Contains(r.Race)) return false;
                l.SetRace(seat, r.Race);
                return true;
            }
            case LobbyRequest.SetTeam t:
            {
                if (At(t.Seat) is not { } seat || !Mine(seat) || l.TeamsLocked) return false;
                l.SetTeam(seat, t.Team);
                return true;
            }
            case LobbyRequest.SetColor c:
                return At(c.Seat) is { } colored && Mine(colored) && l.SetColor(colored, c.Color);
            case LobbyRequest.SetReady r:
                if (own is null) return false;
                own.Ready = r.Ready;
                return true;
            case LobbyRequest.AddBot:
                return leads && l.AddBot() is not null;
            case LobbyRequest.Remove r:
            {
                if (!leads || At(r.Seat) is not { } seat || seat == own && peer != Lobby.Host) return false;
                return l.Remove(seat);
            }
            case LobbyRequest.Settings { Values: var v }:
                if (!leads || !v.Sane) return false;
                (l.Map, l.Size, l.Seed, l.Raid, l.Fog, l.ZoneTarget, l.AssaultMinutes, l.Spectators, l.Terrain) =
                    (v.Map, v.Size, v.Seed, v.Raid, v.Fog, v.ZoneTarget, v.AssaultMinutes, v.Spectators, v.Terrain);
                // The direct link shows the host's address: its own consent, never a player's, leader or not.
                if (peer == Lobby.Host) l.Direct = v.Direct;
                l.Fit();
                return true;
        }
        return false;
    }

    /// <summary>
    /// A resumed campaign keeps the save's seats, races, teams, colors and map: players only say
    /// they are ready, and the leader gives a seat waiting for its player to a bot, or opens it
    /// again for them. Players sit at the seats kept for them when they join (LobbyRoom.Join).
    /// </summary>
    static bool Resumed(Lobby l, int peer, bool leads, LobbyRequest request)
    {
        switch (request)
        {
            case LobbyRequest.SetReady r when l.Seats.Find(s => s.Kind == SeatKind.Human && s.Peer == peer) is { } own:
                own.Ready = r.Ready;
                return true;
            case LobbyRequest.SetKind { Kind: SeatKind.Bot or SeatKind.Open } k when leads && Enum.IsDefined(k.Difficulty):
            {
                if (l.Seats.Find(s => s.Id == k.Seat) is not { Reserved: not null } seat || seat.Kind == SeatKind.Human) return false;
                l.SetKind(seat, k.Kind);
                if (k.Kind == SeatKind.Bot) seat.Difficulty = k.Difficulty;
                return true;
            }
        }
        return false;
    }
}

/// <summary>
/// Someone in a lobby played over the network: its peer (Lobby.Host for the host's own player),
/// its name, and whether it only watches: a spectator has no seat, and gives no order.
/// </summary>
public sealed record Member(int Peer, string Name, bool Watches = false);

/// <summary>
/// A lobby with the players in it, as the host keeps it: who joined, in what order, under what
/// name, and who leads. On a dedicated server nobody plays on the host: the first player in
/// leads, and the next one when it leaves.
/// </summary>
public sealed class LobbyRoom(Lobby lobby)
{
    public const int MaxName = 20;

    public Lobby Lobby { get; set; } = lobby;
    /// <summary>In the order they came: the host's own player first, unless the host is a dedicated server.</summary>
    public List<Member> Members { get; set; } = [];

    /// <summary>The peer with the host's rights; -1 on a dedicated server no player has joined (a spectator never leads).</summary>
    public int Leader => !Lobby.Dedicated ? Lobby.Host : Members.Find(m => !m.Watches)?.Peer ?? -1;

    /// <summary>Whether a peer only watches.</summary>
    public bool Watches(int peer) => Members.Find(m => m.Peer == peer)?.Watches == true;

    public string NameOf(int peer) => Members.Find(m => m.Peer == peer)?.Name ?? "Joueur";

    /// <summary>
    /// A player comes in and takes the first open seat, under its name made clean and unique;
    /// the reason it cannot, or null.
    /// </summary>
    public string? Join(int peer, string name)
    {
        if (Members.Exists(m => m.Peer == peer)) return "Déjà dans la partie.";
        var clean = CleanName(name);
        // A resumed campaign takes back only those who played it, each at the seat kept for them.
        var seat = Lobby.Resumed ? Lobby.Seats.Find(s => s.Kind == SeatKind.Open && s.Reserved == clean) : Lobby.Seats.Find(s => s.Kind == SeatKind.Open);
        if (seat is null && Lobby.Resumed)
        {
            var waiting = Lobby.Seats.Where(s => s.Kind == SeatKind.Open && s.Reserved is not null).Select(s => s.Reserved).ToList();
            return waiting.Count == 0 ? "La campagne reprise n'attend plus personne."
                : $"Cette partie reprend une campagne sauvegardée : elle attend {string.Join(", ", waiting)}. Rejoins-la sous ce nom.";
        }
        if (seat is null) return "La partie est complète.";
        Members.Add(new Member(peer, Unique(clean)));
        Lobby.SetKind(seat, SeatKind.Human, peer);
        return null;
    }

    /// <summary>A spectator comes in, without a seat, under its name made clean and unique; the reason it cannot, or null.</summary>
    public string? Watch(int peer, string name)
    {
        if (Members.Exists(m => m.Peer == peer)) return "Déjà dans la partie.";
        if (!Lobby.Spectators) return "L'hôte ne prend pas de spectateurs.";
        if (Members.Count(m => m.Watches) >= MaxSpectators) return "Il y a déjà assez de spectateurs.";
        Members.Add(new Member(peer, Unique(CleanName(name)), true));
        return null;
    }

    /// <summary>
    /// A player who left a game under way comes back, under its old name, to the seat it had
    /// (seat id); the reason it cannot, or null.
    /// </summary>
    public string? Rejoin(int peer, string name, int seatId)
    {
        if (Members.Exists(m => m.Peer == peer)) return "Déjà dans la partie.";
        if (Lobby.Seats.Find(s => s.Id == seatId) is not { Kind: SeatKind.Open } seat) return "Ta place n'est plus libre.";
        if (Members.Exists(m => m.Name == name)) return "Un joueur de ce nom est déjà dans la partie.";
        Members.Add(new Member(peer, name));
        Lobby.SetKind(seat, SeatKind.Human, peer);
        return null;
    }

    /// <summary>Spectators at once, at most: each costs the host a snapshot stream.</summary>
    public const int MaxSpectators = 4;

    string Unique(string clean)
    {
        var unique = clean;
        for (var n = 2; Members.Exists(m => m.Name == unique); n++) unique = $"{clean} {n}";
        return unique;
    }

    /// <summary>A player goes: its seat opens again.</summary>
    public void Leave(int peer)
    {
        Members.RemoveAll(m => m.Peer == peer);
        foreach (var s in Lobby.Seats.Where(s => s.Kind == SeatKind.Human && s.Peer == peer).ToList()) Lobby.SetKind(s, SeatKind.Open);
    }

    /// <summary>
    /// Applies a member's request to the lobby. A spectator asks for nothing: it has no seat to
    /// change, and must not take an open one from the players (it would hold it while watching).
    /// </summary>
    public bool Apply(int peer, LobbyRequest request) =>
        Members.Exists(m => m.Peer == peer && !m.Watches) && LobbyRules.Apply(Lobby, peer, peer == Leader, request);

    /// <summary>Remote players the leader moved out of their seat: they must go. Spectators have none to lose.</summary>
    public List<int> Seatless() =>
        [.. Members.Where(m => m.Peer != Lobby.Host && !m.Watches && !Lobby.Seats.Exists(s => s.Kind == SeatKind.Human && s.Peer == m.Peer)).Select(m => m.Peer)];

    /// <summary>A name as players see it: no control characters, trimmed, at most MaxName letters; "Joueur" when nothing is left.</summary>
    public static string CleanName(string? name)
    {
        var clean = new string((name ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (clean.Length > MaxName) clean = clean[..MaxName].TrimEnd();
        return clean.Length > 0 ? clean : "Joueur";
    }
}
