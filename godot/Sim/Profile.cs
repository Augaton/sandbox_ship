// The player's profile on this machine: what was played, won and destroyed, and the achievements
// unlocked. The game keeps it as JSON (user://profile.json) and feeds it what happened; the
// counting rules and the achievements' conditions live here, pure. Reading takes whatever a file
// holds, a damaged or forged one included: unknown keys are left out, counts bounded.
using System.Text.Json;
using System.Text.Json.Serialization;
using SandboxShip.Sim.Data;

namespace SandboxShip.Sim;

/// <summary>
/// How a battle ended for the local player: its mode (Campaign for a battle of the campaign),
/// whether the player's side won, the race played, how long it lasted (world seconds), the
/// player's ships destroyed, lost and built, how many sides fought the player, the hardest AI
/// among them, whether it was played over the network and hosted with another player in it, and
/// for a survival game, the last wave beaten.
/// </summary>
public sealed record BattleRecap(
    LobbyMode Mode, bool Won, Faction Race, double Seconds, int Kills, int Lost, int Built,
    int Enemies = 1, Difficulty? Hardest = null, bool Network = false, bool Hosted = false, int Waves = 0);

/// <summary>Counts over every game played on this machine; what the Profile screen shows.</summary>
public sealed class ProfileStats
{
    /// <summary>Battles played and won, by mode (LobbyMode's names); campaign battles under Campaign.</summary>
    public Dictionary<string, int> Played { get; set; } = [];
    public Dictionary<string, int> Won { get; set; } = [];
    /// <summary>Battles played and won with each race (Faction's names).</summary>
    public Dictionary<string, int> PlayedAs { get; set; } = [];
    public Dictionary<string, int> WonAs { get; set; } = [];
    public long Kills { get; set; }
    public long Lost { get; set; }
    public long Built { get; set; }
    /// <summary>Seconds played, in battle or on the galaxy map, as the clock on the wall counts them (Profiles.AddTime).</summary>
    public double Seconds { get; set; }
    /// <summary>The furthest survival wave beaten.</summary>
    public int BestWave { get; set; }
    public int Campaigns { get; set; }
    public int CampaignsWon { get; set; }
    public int NetworkGames { get; set; }
    public int HostedGames { get; set; }
    /// <summary>Wins without a ship lost; skirmishes won within Blitz; wins against three sides or more; against a hard AI.</summary>
    public int FlawlessWins { get; set; }
    public int QuickWins { get; set; }
    public int WinsOutnumbered { get; set; }
    public int HardWins { get; set; }
    public int Colossi { get; set; }
    public int Titans { get; set; }
    public int Lances { get; set; }
    public int Replays { get; set; }
    public bool Tutorial { get; set; }

    public int PlayedIn(LobbyMode m) => Played.GetValueOrDefault(m.ToString());
    public int WonIn(LobbyMode m) => Won.GetValueOrDefault(m.ToString());
    public int WonWith(Faction f) => WonAs.GetValueOrDefault(f.ToString());
    /// <summary>Battles of every mode but the sandbox, whose rules are the player's own.</summary>
    [JsonIgnore]
    public int Battles => Played.Where(p => p.Key != nameof(LobbyMode.Sandbox)).Sum(p => p.Value);
    [JsonIgnore]
    public int Wins => Won.Where(p => p.Key != nameof(LobbyMode.Sandbox)).Sum(p => p.Value);
}

/// <summary>
/// An achievement: its key in the profile, its name and what earns it (French; the game
/// translates), whether it stays hidden until earned, the icon the game draws for it (see
/// Ui/AchievementIcons), and its condition on the counts.
/// </summary>
public sealed record AchievementDef(string Id, string Name, string Text, string Icon, Func<ProfileStats, bool> Done, bool Hidden = false);

/// <summary>A profile: the counts, and when each achievement was earned (Unix ms).</summary>
public sealed class Profile
{
    public int Version { get; set; } = Profiles.Version;
    public ProfileStats Stats { get; set; } = new();
    public Dictionary<string, long> Unlocked { get; set; } = [];
}

public static class Profiles
{
    public const int Version = 1;
    /// <summary>A file larger than this is no profile: it is not read.</summary>
    const int MaxBytes = 256 * 1024;
    /// <summary>No count goes past this, whatever a file says: years of play.</summary>
    const long MaxCount = 100_000_000;
    /// <summary>A skirmish won within this many seconds is a blitz.</summary>
    public const double Blitz = 8 * 60;
    /// <summary>A win without a ship lost counts only if the battle lasted this long.</summary>
    public const double FlawlessAfter = 3 * 60;

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    static readonly LobbyMode[] BattleModes = [LobbyMode.Skirmish, LobbyMode.Zones, LobbyMode.Assault, LobbyMode.Regicide];
    static readonly Faction[] Races = [Faction.Klaed, Faction.Nairan, Faction.Nautolan];

    public static readonly IReadOnlyList<AchievementDef> All =
    [
        new("tutorial", "Premier pas", "Finir le didacticiel.", "book", s => s.Tutorial),
        new("first-win", "Première victoire", "Gagner une bataille.", "star", s => s.Wins >= 1),
        new("skirmish", "Escarmoucheur", "Gagner une escarmouche.", "sword", s => s.WonIn(LobbyMode.Skirmish) >= 1),
        new("zones", "Maître des balises", "Gagner une partie de contrôle de zone.", "beacon", s => s.WonIn(LobbyMode.Zones) >= 1),
        new("assault", "Forteresse", "Gagner un assaut, en défense ou en attaque.", "shield", s => s.WonIn(LobbyMode.Assault) >= 1),
        new("regicide", "Régicide", "Gagner une partie de régicide.", "crown", s => s.WonIn(LobbyMode.Regicide) >= 1),
        new("survival-10", "Tenir la ligne", "Repousser 10 vagues en survie.", "wave", s => s.BestWave >= 10),
        new("survival-20", "Rempart vivant", "Repousser 20 vagues en survie.", "wave", s => s.BestWave >= 20),
        new("survival-30", "Dernier bastion", "Repousser 30 vagues en survie.", "wave", s => s.BestWave >= 30),
        new("survival-50", "Légende de l'Errance", "Repousser 50 vagues en survie.", "wave", s => s.BestWave >= 50),
        new("all-modes", "Stratège complet", "Gagner en escarmouche, en zones, en assaut et en régicide, et repousser 10 vagues en survie.", "medal",
            s => BattleModes.All(m => s.WonIn(m) >= 1) && s.BestWave >= 10),
        new("conquest", "Première conquête", "Gagner une bataille de la campagne.", "flag", s => s.WonIn(LobbyMode.Campaign) >= 1),
        new("campaign", "Maître de la galaxie", "Gagner une campagne galactique.", "galaxy", s => s.CampaignsWon >= 1),
        new("klaed", "Fer de lance", "Gagner une bataille avec les Kla'ed.", "race:Klaed", s => s.WonWith(Faction.Klaed) >= 1),
        new("nairan", "Bouclier d'azur", "Gagner une bataille avec les Nairan.", "race:Nairan", s => s.WonWith(Faction.Nairan) >= 1),
        new("nautolan", "Marée verte", "Gagner une bataille avec les Nautolan.", "race:Nautolan", s => s.WonWith(Faction.Nautolan) >= 1),
        new("all-races", "Trois bannières", "Gagner une bataille avec chacune des trois races.", "medal", s => Races.All(r => s.WonWith(r) >= 1)),
        new("kills-100", "Chasseur", "Détruire 100 vaisseaux ennemis.", "target", s => s.Kills >= 100),
        new("kills-1000", "Fléau", "Détruire 1 000 vaisseaux ennemis.", "target", s => s.Kills >= 1000),
        new("kills-10000", "Apocalypse", "Détruire 10 000 vaisseaux ennemis.", "skull", s => s.Kills >= 10000),
        new("flawless", "Sans une égratignure", "Gagner une bataille d'au moins 3 minutes sans perdre un vaisseau.", "shield", s => s.FlawlessWins >= 1),
        new("blitz", "Blitz", "Gagner une escarmouche en moins de 8 minutes.", "bolt", s => s.QuickWins >= 1),
        new("outnumbered", "Seul contre tous", "Gagner face à trois camps ennemis ou plus.", "sword", s => s.WinsOutnumbered >= 1),
        new("hard", "Contre les meilleurs", "Gagner contre une IA difficile.", "crown", s => s.HardWins >= 1),
        new("titan", "Titan", "Mettre un Titan en service.", "ship", s => s.Titans >= 1),
        new("colossus", "Colosse", "Mettre un Colosse en service.", "ship", s => s.Colossi >= 1),
        new("lance", "Lance stellaire", "Tirer la Lance stellaire.", "lance", s => s.Lances >= 1),
        new("network", "En réseau", "Finir une partie à plusieurs, en réseau.", "network", s => s.NetworkGames >= 1),
        new("host", "Hôte", "Héberger une partie avec au moins un autre joueur, jusqu'à sa fin.", "antenna", s => s.HostedGames >= 1),
        new("games-10", "Habitué", "Jouer 10 batailles.", "clock", s => s.Battles >= 10),
        new("games-100", "Vétéran", "Jouer 100 batailles.", "medal", s => s.Battles >= 100),
        new("hours-10", "Vie de commandant", "Jouer 10 heures.", "clock", s => s.Seconds >= 10 * 3600),
        new("sandbox", "Démiurge", "Jouer une partie de bac à sable.", "cube", s => s.PlayedIn(LobbyMode.Sandbox) >= 1, Hidden: true),
        new("replay", "Cinéphile", "Regarder une rediffusion jusqu'au bout.", "film", s => s.Replays >= 1, Hidden: true),
        new("losses", "Pertes acceptables", "Perdre 1 000 vaisseaux.", "skull", s => s.Lost >= 1000, Hidden: true),
        new("stubborn", "Têtu", "Perdre 10 batailles.", "flag", s => s.Battles - s.Wins >= 10, Hidden: true),
    ];

    static readonly Dictionary<string, AchievementDef> ById = All.ToDictionary(a => a.Id);

    public static AchievementDef? Find(string id) => ById.GetValueOrDefault(id);

    /// <summary>Counts a battle that ended; returns the achievements it earned.</summary>
    public static List<AchievementDef> RecordBattle(Profile p, BattleRecap r, long now)
    {
        var s = p.Stats;
        var mode = r.Mode.ToString();
        s.Played[mode] = Bounded(s.Played.GetValueOrDefault(mode) + 1);
        // The sandbox's rules are the player's own: it counts as played, nothing more.
        if (r.Mode != LobbyMode.Sandbox)
        {
            var race = r.Race.ToString();
            s.PlayedAs[race] = Bounded(s.PlayedAs.GetValueOrDefault(race) + 1);
            s.Kills = Math.Min(MaxCount, s.Kills + Math.Max(0, r.Kills));
            s.Lost = Math.Min(MaxCount, s.Lost + Math.Max(0, r.Lost));
            s.Built = Math.Min(MaxCount, s.Built + Math.Max(0, r.Built));
            if (r.Mode == LobbyMode.Survival) s.BestWave = Math.Max(s.BestWave, Math.Clamp(r.Waves, 0, 100_000));
            if (r.Network) s.NetworkGames = Bounded(s.NetworkGames + 1);
            if (r.Hosted) s.HostedGames = Bounded(s.HostedGames + 1);
            if (r.Won)
            {
                s.Won[mode] = Bounded(s.Won.GetValueOrDefault(mode) + 1);
                s.WonAs[race] = Bounded(s.WonAs.GetValueOrDefault(race) + 1);
                if (r.Lost == 0 && r.Seconds >= FlawlessAfter) s.FlawlessWins = Bounded(s.FlawlessWins + 1);
                if (r.Mode == LobbyMode.Skirmish && r.Seconds < Blitz) s.QuickWins = Bounded(s.QuickWins + 1);
                if (r.Enemies >= 3) s.WinsOutnumbered = Bounded(s.WinsOutnumbered + 1);
                if (r.Hardest == Difficulty.Hard) s.HardWins = Bounded(s.HardWins + 1);
            }
        }
        return Unlock(p, now);
    }

    /// <summary>Counts a campaign that ended, won or lost.</summary>
    public static List<AchievementDef> RecordCampaign(Profile p, bool won, long now)
    {
        var s = p.Stats;
        s.Campaigns = Bounded(s.Campaigns + 1);
        if (won) s.CampaignsWon = Bounded(s.CampaignsWon + 1);
        return Unlock(p, now);
    }

    /// <summary>
    /// Real seconds played, in battle or on the galaxy map, whatever the game's speed and whether
    /// the game ended or was left: the game adds them as they pass. Returns what they earned.
    /// </summary>
    public static List<AchievementDef> AddTime(Profile p, double seconds, long now)
    {
        p.Stats.Seconds = Math.Min(MaxCount, p.Stats.Seconds + Seconds(seconds));
        return Unlock(p, now);
    }

    /// <summary>What happens in the middle of a game and earns something at once.</summary>
    public enum Moment { Titan, Colossus, Lance, Tutorial, Replay }

    public static List<AchievementDef> Note(Profile p, Moment m, long now)
    {
        var s = p.Stats;
        switch (m)
        {
            case Moment.Titan: s.Titans = Bounded(s.Titans + 1); break;
            case Moment.Colossus: s.Colossi = Bounded(s.Colossi + 1); break;
            case Moment.Lance: s.Lances = Bounded(s.Lances + 1); break;
            case Moment.Tutorial: s.Tutorial = true; break;
            case Moment.Replay: s.Replays = Bounded(s.Replays + 1); break;
        }
        return Unlock(p, now);
    }

    /// <summary>A survival wave just beaten: the best wave climbs during the game, not only at its end.</summary>
    public static List<AchievementDef> Reached(Profile p, int wave, long now)
    {
        p.Stats.BestWave = Math.Max(p.Stats.BestWave, Math.Clamp(wave, 0, 100_000));
        return Unlock(p, now);
    }

    /// <summary>Marks every achievement whose condition now holds; returns those newly earned, in the list's order.</summary>
    static List<AchievementDef> Unlock(Profile p, long now)
    {
        var earned = new List<AchievementDef>();
        foreach (var a in All)
        {
            if (p.Unlocked.ContainsKey(a.Id) || !a.Done(p.Stats)) continue;
            p.Unlocked[a.Id] = Math.Max(0, now);
            earned.Add(a);
        }
        return earned;
    }

    static int Bounded(long n) => (int)Math.Clamp(n, 0, MaxCount);

    static double Seconds(double s) => double.IsFinite(s) ? Math.Clamp(s, 0, 7 * 24 * 3600) : 0;

    /// <summary>A profile from a file's text: an empty one from an empty, damaged or foreign file; else its known keys, bounded.</summary>
    public static Profile Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxBytes) return new();
        Profile? read;
        try
        {
            read = JsonSerializer.Deserialize<Profile>(text, Json);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            return new();
        }
        if (read is null) return new();
        var s = read.Stats ?? new ProfileStats();
        var clean = new ProfileStats
        {
            Played = Known<LobbyMode>(s.Played), Won = Known<LobbyMode>(s.Won),
            PlayedAs = Known<Faction>(s.PlayedAs), WonAs = Known<Faction>(s.WonAs),
            Kills = Math.Clamp(s.Kills, 0, MaxCount), Lost = Math.Clamp(s.Lost, 0, MaxCount), Built = Math.Clamp(s.Built, 0, MaxCount),
            Seconds = double.IsFinite(s.Seconds) ? Math.Clamp(s.Seconds, 0, MaxCount) : 0,
            BestWave = Math.Clamp(s.BestWave, 0, 100_000),
            Campaigns = Bounded(s.Campaigns), CampaignsWon = Bounded(s.CampaignsWon),
            NetworkGames = Bounded(s.NetworkGames), HostedGames = Bounded(s.HostedGames),
            FlawlessWins = Bounded(s.FlawlessWins), QuickWins = Bounded(s.QuickWins),
            WinsOutnumbered = Bounded(s.WinsOutnumbered), HardWins = Bounded(s.HardWins),
            Colossi = Bounded(s.Colossi), Titans = Bounded(s.Titans), Lances = Bounded(s.Lances), Replays = Bounded(s.Replays),
            Tutorial = s.Tutorial,
        };
        var unlocked = new Dictionary<string, long>();
        foreach (var (id, at) in read.Unlocked ?? [])
        {
            if (id is not null && ById.ContainsKey(id)) unlocked[id] = Math.Max(0, at);
        }
        return new Profile { Stats = clean, Unlocked = unlocked };
    }

    /// <summary>The counts of an enum's names only, each bounded: a forged file adds no key and no absurd count.</summary>
    static Dictionary<string, int> Known<T>(Dictionary<string, int>? counts) where T : struct, Enum
    {
        var known = new Dictionary<string, int>();
        foreach (var (key, n) in counts ?? [])
        {
            if (key is not null && Enum.TryParse<T>(key, false, out var v) && Enum.IsDefined(v) && v.ToString() == key) known[key] = Bounded(n);
        }
        return known;
    }

    public static string Serialize(Profile p) => JsonSerializer.Serialize(p, Json);
}
