// The player's profile on this machine (Sim's Profiles): fed what the local player does, kept in
// user://profile.json, written whole through a file set aside first. Only the local player
// counts, each machine for its own in a network game; nothing counts in a capture (unless
// --profile names a file of its own), a replay, the menu's backdrop, the tutorial's battle, or
// for a spectator.
using Godot;
using SandboxShip.Battle;
using SandboxShip.Sim;
using SandboxShip.Sim.Data;

namespace SandboxShip.Core;

public static class PlayerProfile
{
    static Profile? current;
    /// <summary>The world whose end was counted last: an end said twice (a defeat, then the winner) counts once.</summary>
    static World? counted;
    /// <summary>Real seconds played not yet in the profile: added a minute at a time, and when a screen goes.</summary>
    static double pending;

    public static Profile Current => current ??= Load();

    static string FilePath => Dev.Profile ?? ProjectSettings.GlobalizePath("user://profile.json");

    /// <summary>Whether anything counts on this run: not a server, not a capture unless it keeps a profile of its own.</summary>
    static bool Enabled => !Dev.Service && (Dev.Shot is null && Dev.Snap is null || Dev.Profile is not null);

    static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    static Profile Load()
    {
        try
        {
            return File.Exists(FilePath) ? Profiles.Parse(File.ReadAllText(FilePath)) : new Profile();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Profile();
        }
    }

    static void Save()
    {
        if (!Enabled || current is null) return;
        try
        {
            var path = FilePath;
            File.WriteAllText(path + ".tmp", Profiles.Serialize(current));
            File.Move(path + ".tmp", path, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"Profil non enregistré : {e.Message}");
        }
    }

    /// <summary>Whether what happens on a battle screen counts for the player on this machine.</summary>
    public static bool Counts(BattleScreen s) =>
        Enabled && !s.Opts.Demo && s.Opts.Replay is null && !s.Opts.Tutorial && s.Opts.Net?.Watching != true
        && Network.Session.Current?.Watching != true && s.World.Factions.ContainsKey(s.Opts.Player);

    /// <summary>What mode a battle is played in, as the lobby names them; a campaign battle's is Campaign.</summary>
    static LobbyMode ModeOf(BattleScreen s) => s.Opts.Campaign is not null ? LobbyMode.Campaign
        : s.Opts.Mode == GameMode.Sandbox ? LobbyMode.Sandbox
        : s.World.Mode.Kind switch
        {
            BattleMode.Survival => LobbyMode.Survival,
            BattleMode.Zones => LobbyMode.Zones,
            BattleMode.Assault => LobbyMode.Assault,
            BattleMode.Regicide => LobbyMode.Regicide,
            _ => LobbyMode.Skirmish,
        };

    /// <summary>Players at the human seats of the network game this machine is in; 1 off the network.</summary>
    static int Humans() => Network.Session.Current?.Room?.Lobby.Seats.Count(s => s.Kind == SeatKind.Human) ?? 1;

    /// <summary>A battle ended for the player on this machine: won or lost, it counts once.</summary>
    public static void BattleEnded(BattleScreen s, bool won)
    {
        if (!Counts(s) || counted == s.World) return;
        counted = s.World;
        var w = s.World;
        var me = w.Factions[s.Opts.Player];
        var foes = w.Factions.Values.Where(f => f.Race != Faction.Errant && Worlds.Foes(w, me.Id, f.Id)).ToList();
        var network = s.Opts.Net is not null && Humans() > 1;
        var recap = new BattleRecap(ModeOf(s), won, me.Race, w.Time, me.Stats.Kills, me.Stats.Lost, me.Stats.Built,
            Enemies: foes.Count, Hardest: foes.Select(f => f.AI?.Difficulty).Max(), Network: network,
            Hosted: network && s.Opts.Net!.Host is not null, Waves: w.Mode.Kind == BattleMode.Survival && FullWaves(s) ? w.Mode.Cleared : 0);
        Announce(Profiles.RecordBattle(Current, recap, Now));
        Flush();
    }

    /// <summary>A campaign ended for the player on this machine.</summary>
    public static void CampaignEnded(bool won)
    {
        if (!Enabled) return;
        Announce(Profiles.RecordCampaign(Current, won, Now));
        Flush();
    }

    /// <summary>Something that earns at once: a titan or colossus in service, the lance fired, the tutorial or a replay to its end.</summary>
    public static void Moment(Profiles.Moment m)
    {
        if (!Enabled) return;
        var earned = Profiles.Note(Current, m, Now);
        Announce(earned);
        Save();
    }

    /// <summary>Survival waves at their usual strength or more: softer ones count toward no wave record, nor its achievements.</summary>
    public static bool FullWaves(BattleScreen s) => s.Opts.WaveStrength >= 100;

    /// <summary>
    /// Every frame of a battle, before its events go: the player's titans and colossi put in
    /// service, the star lance fired, survival waves beaten.
    /// </summary>
    public static void Watch(BattleScreen s)
    {
        if (!Counts(s)) return;
        var w = s.World;
        foreach (var ev in w.Events)
        {
            switch (ev)
            {
                case Delivered d when w.Entities.Get(d.Id) is { Side: var side, Cls: var cls } && side == s.Opts.Player:
                    if (cls == ShipClass.Titan) Moment(Profiles.Moment.Titan);
                    else if (cls == ShipClass.Colossus) Moment(Profiles.Moment.Colossus);
                    break;
                case AbilityUsed { Id: AbilityId.StarLance } a when a.Side == s.Opts.Player:
                    Moment(Profiles.Moment.Lance);
                    break;
                case WaveCleared wc when FullWaves(s):
                    var earned = Profiles.Reached(Current, wc.Wave, Now);
                    if (earned.Count > 0)
                    {
                        Announce(earned);
                        Save();
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Every frame: the time the player spends in a battle or on the galaxy map, as the wall clock
    /// counts it (a battle at ×4 is no longer played for that).
    /// </summary>
    public static void Tick(Node? screen, double delta)
    {
        var playing = screen switch
        {
            BattleScreen b => Counts(b),
            Campaign.GalaxyScreen => Enabled,
            _ => false,
        };
        if (!playing) return;
        pending += delta;
        if (pending >= 60) Flush();
    }

    /// <summary>Puts the time played so far in the profile and saves it: a minute at a time, at a game's end, when a screen goes, on quitting.</summary>
    public static void Flush()
    {
        if (!Enabled) return;
        if (pending > 0) Announce(Profiles.AddTime(Current, pending, Now));
        pending = 0;
        Save();
    }

    static void Announce(List<AchievementDef> earned)
    {
        foreach (var a in earned) Ui.AchievementToast.Show(a);
    }
}
