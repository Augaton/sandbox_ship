// The setup of a game before it starts (not in the web version): who takes each seat, the map
// and the rules, for the campaign and every kind of battle. Plain data, so that the menu can
// remember the last one of each mode and the network can later keep it in step between the
// host and the players who join; the game itself comes from Slots, Skirmish and Layout.
using System.Text.Json;
using System.Text.Json.Serialization;
using SandboxShip.Sim.Data;

namespace SandboxShip.Sim;

/// <summary>What a lobby sets up: the galaxy campaign, a battle under one of the BattleModes, or the sandbox.</summary>
public enum LobbyMode { Campaign, Skirmish, Survival, Zones, Assault, Regicide, Sandbox }

/// <summary>Who takes a seat: a human player, the AI, nobody yet (open to a player who joins), or nobody (closed).</summary>
public enum SeatKind { Human, Bot, Open, Closed }

public sealed class Seat
{
    /// <summary>The seat's for the lobby's whole life, wherever it moves; never given twice in a lobby.</summary>
    public int Id { get; set; }
    public SeatKind Kind { get; set; }
    /// <summary>Who sits at a human seat: Lobby.Host (in a local game, the one playing on this machine), or a peer the network numbers.</summary>
    public int Peer { get; set; }
    public Faction Race { get; set; }
    /// <summary>0: alone, at war with everyone; 1 to Lobby.Teams: allied with the rest of that team.</summary>
    public int Team { get; set; }
    /// <summary>A bot's AI.</summary>
    public Difficulty Difficulty { get; set; } = Difficulty.Normal;
    /// <summary>One of Lobby.Colors, no other seat's.</summary>
    public uint Color { get; set; }
    /// <summary>A human seat's player is ready to start: the network's, a local game does not ask.</summary>
    public bool Ready { get; set; }

    /// <summary>Someone plays from this seat: a human or a bot.</summary>
    [JsonIgnore]
    public bool Plays => Kind is SeatKind.Human or SeatKind.Bot;
}

public sealed class Lobby
{
    public const int MaxSeats = 6, Teams = 3;
    /// <summary>The host's peer: in a local game, the one who plays.</summary>
    public const int Host = 0;
    public static readonly IReadOnlyList<int> ZoneTargets = [50, 100, 200];
    public static readonly IReadOnlyList<int> AssaultLengths = [10, 15, 20];
    /// <summary>The colors a seat can take: the races' own, then the players'. Never the Errants' gold.</summary>
    public static readonly IReadOnlyList<uint> Colors = [.. Factions.All.Select(f => Factions.Defs[f].Color), .. Factions.PlayerColors];

    /// <summary>How a lobby reads and writes as JSON: names in camel case, enums by name.</summary>
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public LobbyMode Mode { get; set; }
    /// <summary>In order: the order of the seats that play gives their starting places (see Slots).</summary>
    public List<Seat> Seats { get; set; } = [];
    /// <summary>The id the next seat gets.</summary>
    public int NextId { get; set; }
    /// <summary>The classic map, or galaxy, is for two only (see Fit).</summary>
    public MapKind Map { get; set; }
    public MapSize Size { get; set; } = MapSize.Medium;
    public int Seed { get; set; } = 1;
    /// <summary>A neutral colossus to raid, where the mode has one (HasRaid).</summary>
    public bool Raid { get; set; } = true;
    /// <summary>Fog of war, in the battles that have it (HasFog).</summary>
    public bool Fog { get; set; } = true;
    /// <summary>Zones: the score that wins, one of ZoneTargets.</summary>
    public int ZoneTarget { get; set; } = 100;
    /// <summary>Assault: the minutes the defenders must hold out, one of AssaultLengths.</summary>
    public int AssaultMinutes { get; set; } = 15;

    /// <summary>
    /// A new lobby of a mode: you and one bot, at war; in survival, side by side; in an assault,
    /// you defend (team 1) and the bot attacks (team 2).
    /// </summary>
    public static Lobby Create(LobbyMode mode, int seed)
    {
        var lobby = new Lobby { Mode = mode, Seed = seed };
        lobby.Add(SeatKind.Human);
        lobby.Add(SeatKind.Bot);
        return lobby;
    }

    /// <summary>The fewest seats that must play: one in survival, two in every other mode.</summary>
    public static int MinPlayers(LobbyMode mode) => mode == LobbyMode.Survival ? 1 : 2;

    [JsonIgnore]
    public IEnumerable<Seat> Playing => Seats.Where(s => s.Plays);
    [JsonIgnore]
    public bool CanAdd => Seats.Count < MaxSeats;
    /// <summary>Survival: everyone in one team, against the waves.</summary>
    [JsonIgnore]
    public bool TeamsLocked => Mode == LobbyMode.Survival;
    [JsonIgnore]
    public bool HasRaid => Mode is not (LobbyMode.Campaign or LobbyMode.Sandbox) && Modes.AllowsRaid(Rules);
    /// <summary>The sandbox shows everything; the campaign's battles keep their own fog.</summary>
    [JsonIgnore]
    public bool HasFog => Mode is not (LobbyMode.Campaign or LobbyMode.Sandbox);
    [JsonIgnore]
    public bool ClassicAllowed => Playing.Count() == 2;

    /// <summary>The battle's rules: the mode's own, Standard for the sandbox and the campaign.</summary>
    [JsonIgnore]
    public BattleMode Rules => Mode switch
    {
        LobbyMode.Survival => BattleMode.Survival,
        LobbyMode.Zones => BattleMode.Zones,
        LobbyMode.Assault => BattleMode.Assault,
        LobbyMode.Regicide => BattleMode.Regicide,
        _ => BattleMode.Standard,
    };

    /// <summary>A bot more, at the end; null when all the seats are there. See Add.</summary>
    public Seat? AddBot() => Add(SeatKind.Bot);

    /// <summary>
    /// A seat more, at the end: of the race the fewest seats have, in its race's color if free or
    /// the first free one, in the team its mode puts it in (in an assault, the smaller one).
    /// </summary>
    Seat? Add(SeatKind kind)
    {
        if (!CanAdd) return null;
        var race = Factions.All.OrderBy(r => Seats.Count(s => s.Race == r)).First();
        var color = Factions.Defs[race].Color;
        if (Seats.Exists(s => s.Color == color)) color = Colors.First(c => !Seats.Exists(s => s.Color == c));
        var team = Mode switch
        {
            LobbyMode.Survival => 1,
            LobbyMode.Assault => Seats.Count(s => s.Team == 1) <= Seats.Count(s => s.Team == 2) ? 1 : 2,
            _ => 0,
        };
        var seat = new Seat { Id = NextId++, Kind = kind, Race = race, Color = color, Team = team };
        Seats.Add(seat);
        Fit();
        return seat;
    }

    /// <summary>Takes a seat away; the last one stays.</summary>
    public bool Remove(Seat seat)
    {
        if (Seats.Count <= 1 || !Seats.Remove(seat)) return false;
        Fit();
        return true;
    }

    /// <summary>Who takes a seat; a human seat is the host's unless the network says whose. Nobody is ready after a change.</summary>
    public void SetKind(Seat seat, SeatKind kind, int peer = Host)
    {
        seat.Kind = kind;
        seat.Peer = kind == SeatKind.Human ? peer : Host;
        seat.Ready = false;
        Fit();
    }

    /// <summary>A seat changes race; its color follows when it wore its old race's and the new race's is free.</summary>
    public void SetRace(Seat seat, Faction race)
    {
        if (!Factions.All.Contains(race)) return;
        var follows = seat.Color == Factions.Defs[seat.Race].Color;
        seat.Race = race;
        SetColor(seat, follows ? Factions.Defs[race].Color : seat.Color);
    }

    /// <summary>Whether a seat may take a color: one of Colors, and no other seat's.</summary>
    public bool ColorFree(Seat seat, uint color) => Colors.Contains(color) && !Seats.Exists(s => s != seat && s.Color == color);

    /// <summary>Gives a seat a color, if it is free (ColorFree).</summary>
    public bool SetColor(Seat seat, uint color)
    {
        if (!ColorFree(seat, color)) return false;
        seat.Color = color;
        return true;
    }

    public void SetTeam(Seat seat, int team)
    {
        seat.Team = Math.Clamp(team, 0, Teams);
        Fit();
    }

    /// <summary>
    /// Brings the lobby back within its mode's rules, after a change or once read: the classic
    /// map for two only, everyone in team 1 in survival, every seat in team 1 or 2 in an assault.
    /// </summary>
    public void Fit()
    {
        if (!ClassicAllowed) Map = MapKind.Random;
        foreach (var s in Seats)
        {
            s.Team = Mode switch
            {
                LobbyMode.Survival => 1,
                LobbyMode.Assault => s.Team == 2 ? 2 : 1,
                _ => Math.Clamp(s.Team, 0, Teams),
            };
        }
    }

    /// <summary>The side of the i-th seat that plays: 0 to 2, then 4 and up, as side 3 is the Errants'.</summary>
    public static Side SideOf(int index) => new(index < 3 ? index : index + 1);

    /// <summary>
    /// The game's seats, in order, from the seats that play: the i-th on side SideOf(i). The order
    /// gives the places: around the skirmish map in turn; in the galaxy, the first two get the
    /// capitals farthest apart. Bots play at their own difficulty; in the sandbox, once its panel
    /// turns the AI on.
    /// </summary>
    public List<Slot> Slots() =>
        [.. Playing.Select((s, i) => new Slot(SideOf(i), s.Race, s.Team, s.Kind == SeatKind.Bot ? s.Difficulty : null, s.Color))];

    /// <summary>The side a peer plays, by default the host's: null when that peer has no seat.</summary>
    public Side? SideOfPeer(int peer = Host)
    {
        var i = Playing.ToList().FindIndex(s => s.Kind == SeatKind.Human && s.Peer == peer);
        return i < 0 ? null : SideOf(i);
    }

    /// <summary>A battle's options (not the campaign's). The sandbox's credits, and its AI off at the start, are the battle screen's.</summary>
    public SkirmishOptions Skirmish() => new()
    {
        Mode = Rules, ZoneTarget = ZoneTarget, AssaultMinutes = AssaultMinutes, Slots = Slots(), Seed = Seed, Map = Map, Size = Size,
        Fog = HasFog && Fog, Raid = HasRaid && Raid,
    };

    /// <summary>The campaign's galaxy, as GalaxyScreen makes it: the classic one for two, or one drawn from the seed with a capital per seat.</summary>
    public GalaxyLayout Layout() =>
        Map == MapKind.Classic && ClassicAllowed ? GalaxyGen.Classic : GalaxyGen.GenerateLayout(Seed, Math.Max(2, Playing.Count()), Size);

    /// <summary>
    /// Why the game cannot start yet, in the words of the menu; empty when it can. Fit keeps the
    /// mode's own team rules; this checks what is left to the players' choices.
    /// </summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        var playing = Playing.ToList();
        var you = playing.Count(s => s.Kind == SeatKind.Human && s.Peer == Host);
        if (you == 0) problems.Add("Il faut une place « Toi ».");
        if (you > 1) problems.Add("Une seule place peut être « Toi ».");
        if (playing.Count < MinPlayers(Mode))
        {
            problems.Add(Mode == LobbyMode.Survival ? "Il faut au moins une place occupée." : "Il faut au moins deux places occupées.");
        }
        else if (Mode == LobbyMode.Assault)
        {
            if (!playing.Exists(s => s.Team == 1)) problems.Add("L'assaut demande au moins un défenseur (équipe 1).");
            if (!playing.Exists(s => s.Team == 2)) problems.Add("L'assaut demande au moins un attaquant (équipe 2).");
        }
        else if (Mode != LobbyMode.Survival && playing.Select(s => s.Team == 0 ? -1 - s.Id : s.Team).Distinct().Count() < 2)
        {
            problems.Add("Tout le monde est dans la même équipe : il faut au moins deux camps ennemis.");
        }
        if (playing.Exists(s => s.Kind == SeatKind.Human && s.Peer != Host && !s.Ready)) problems.Add("Un joueur n'est pas prêt.");
        // Then the rules' own check, for whatever the lobby's checks above leave through.
        if (problems.Count == 0 && Mode != LobbyMode.Campaign && Scenario.Check(Skirmish()) is { Ok: false } r) problems.Add(r.Reason);
        return problems;
    }

    /// <summary>
    /// Whether a lobby read from a file (or, later, the network) holds together: known modes,
    /// races and settings, 1 to MaxSeats seats with ids and colors of their own, teams 0 to
    /// Teams. Fit and Problems take it from there.
    /// </summary>
    public bool Sane() =>
        Enum.IsDefined(Mode) && Enum.IsDefined(Map) && Enum.IsDefined(Size) && Seed >= 1
        && ZoneTargets.Contains(ZoneTarget) && AssaultLengths.Contains(AssaultMinutes)
        && Seats is { Count: >= 1 and <= MaxSeats }
        && Seats.TrueForAll(s => s is not null && Enum.IsDefined(s.Kind) && Enum.IsDefined(s.Difficulty) && Factions.All.Contains(s.Race)
            && s.Team is >= 0 and <= Teams && Colors.Contains(s.Color) && s.Id >= 0 && s.Id < NextId)
        && Seats.Select(s => s.Id).Distinct().Count() == Seats.Count
        && Seats.Select(s => s.Color).Distinct().Count() == Seats.Count;
}
