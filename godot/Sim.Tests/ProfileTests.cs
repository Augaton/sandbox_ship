// The player's profile (not in the web version): what each finished game adds to the counts, the
// achievements those counts earn, once each, and a file read whatever it holds.
using System.Text;
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.Profiles;

namespace SandboxShip.Sim.Tests;

public class ProfileTests
{
    const long Now = 1_800_000_000_000;

    static BattleRecap Win(LobbyMode mode = LobbyMode.Skirmish, Faction race = Faction.Klaed, double seconds = 900, int kills = 12, int lost = 3) =>
        new(mode, true, race, seconds, kills, lost, 10);

    static string[] Ids(IEnumerable<AchievementDef> list) => [.. list.Select(a => a.Id)];

    [Fact]
    public void The_achievements_are_about_thirty_with_unique_ids_and_some_hidden()
    {
        Assert.InRange(All.Count, 30, 40);
        Assert.Equal(All.Count, All.Select(a => a.Id).Distinct().Count());
        Assert.Contains(All, a => a.Hidden);
        Assert.All(All, a => Assert.False(string.IsNullOrWhiteSpace(a.Name) || string.IsNullOrWhiteSpace(a.Text) || string.IsNullOrWhiteSpace(a.Icon)));
    }

    [Fact]
    public void A_first_skirmish_won_counts_and_earns_its_achievements_once()
    {
        var p = new Profile();
        var earned = RecordBattle(p, Win(), Now);
        Assert.Equal(["first-win", "skirmish", "klaed"], Ids(earned));
        Assert.Equal((1, 1, 12L, 3L), (p.Stats.PlayedIn(LobbyMode.Skirmish), p.Stats.WonIn(LobbyMode.Skirmish), p.Stats.Kills, p.Stats.Lost));
        Assert.Equal(Now, p.Unlocked["first-win"]);
        // Earned once: a second win earns nothing already earned, and keeps the first date.
        Assert.Empty(RecordBattle(p, Win(), Now + 1000));
        Assert.Equal(Now, p.Unlocked["first-win"]);
    }

    [Fact]
    public void A_defeat_counts_as_played_and_earns_no_win()
    {
        var p = new Profile();
        Assert.Empty(RecordBattle(p, new BattleRecap(LobbyMode.Zones, false, Faction.Nairan, 600, 4, 9, 6), Now));
        Assert.Equal((1, 0, 0), (p.Stats.Battles, p.Stats.Wins, p.Stats.WonWith(Faction.Nairan)));
    }

    [Fact]
    public void The_sandbox_counts_as_played_only()
    {
        var p = new Profile();
        var earned = RecordBattle(p, Win(LobbyMode.Sandbox, kills: 5000), Now);
        Assert.Equal(["sandbox"], Ids(earned));
        Assert.Equal((0L, 0, 0), (p.Stats.Kills, p.Stats.Wins, p.Stats.Battles));
    }

    [Fact]
    public void A_flawless_win_needs_three_minutes_and_a_blitz_is_a_skirmish_under_eight()
    {
        var p = new Profile();
        Assert.DoesNotContain("flawless", Ids(RecordBattle(p, Win(seconds: 120, lost: 0), Now)));
        Assert.Contains("blitz", p.Unlocked.Keys);
        Assert.Contains("flawless", Ids(RecordBattle(p, Win(seconds: FlawlessAfter, lost: 0), Now)));
        // Not a blitz in another mode, nor past eight minutes.
        var q = new Profile();
        RecordBattle(q, Win(LobbyMode.Regicide, seconds: 60), Now);
        RecordBattle(q, Win(seconds: Blitz + 1), Now);
        Assert.Equal(0, q.Stats.QuickWins);
    }

    [Fact]
    public void Survival_waves_climb_during_the_game_and_at_its_end()
    {
        var p = new Profile();
        Assert.Empty(Reached(p, 9, Now));
        Assert.Equal(["survival-10"], Ids(Reached(p, 10, Now)));
        Assert.Equal(["survival-20", "survival-30"], Ids(RecordBattle(p, new BattleRecap(LobbyMode.Survival, false, Faction.Nautolan, 3000, 50, 80, 90, Waves: 31), Now)));
        // A worse game never lowers the best wave.
        RecordBattle(p, new BattleRecap(LobbyMode.Survival, false, Faction.Nautolan, 300, 10, 10, 10, Waves: 4), Now);
        Assert.Equal(31, p.Stats.BestWave);
    }

    [Fact]
    public void Every_mode_and_every_race_lead_to_their_own_achievements()
    {
        var p = new Profile();
        RecordBattle(p, Win(LobbyMode.Skirmish, Faction.Klaed), Now);
        RecordBattle(p, Win(LobbyMode.Zones, Faction.Nairan), Now);
        RecordBattle(p, Win(LobbyMode.Assault, Faction.Nautolan), Now);
        Assert.Contains("all-races", p.Unlocked.Keys);
        RecordBattle(p, Win(LobbyMode.Regicide), Now);
        Assert.DoesNotContain("all-modes", p.Unlocked.Keys);
        Assert.Equal(["survival-10", "all-modes"], Ids(Reached(p, 10, Now)));
    }

    [Fact]
    public void Campaigns_moments_and_kills_earn_theirs()
    {
        var p = new Profile();
        Assert.Equal(["first-win", "conquest", "klaed"], Ids(RecordBattle(p, Win(LobbyMode.Campaign), Now)));
        Assert.Empty(RecordCampaign(p, false, Now));
        Assert.Equal(["campaign"], Ids(RecordCampaign(p, true, Now)));
        Assert.Equal(["titan"], Ids(Note(p, Moment.Titan, Now)));
        Assert.Equal(["colossus"], Ids(Note(p, Moment.Colossus, Now)));
        Assert.Equal(["lance"], Ids(Note(p, Moment.Lance, Now)));
        Assert.Equal(["tutorial"], Ids(Note(p, Moment.Tutorial, Now)));
        Assert.Equal(["replay"], Ids(Note(p, Moment.Replay, Now)));
        Assert.Equal(["kills-100"], Ids(RecordBattle(p, Win(LobbyMode.Campaign, kills: 90), Now)));
    }

    [Fact]
    public void Network_hosting_numbers_hard_ais_and_time_earn_theirs()
    {
        var p = new Profile();
        var earned = Ids(RecordBattle(p, Win() with { Network = true, Hosted = true, Enemies = 3, Hardest = Difficulty.Hard }, Now));
        Assert.Contains("network", earned);
        Assert.Contains("host", earned);
        Assert.Contains("outnumbered", earned);
        Assert.Contains("hard", earned);
        Assert.Empty(AddTime(p, 9 * 3600, Now));
        Assert.Equal(["hours-10"], Ids(AddTime(p, 3600, Now)));
        // Whatever a clock gives: nothing negative, nothing endless.
        AddTime(p, double.NaN, Now);
        AddTime(p, -50, Now);
        Assert.Equal(10 * 3600.0, p.Stats.Seconds);
    }

    [Fact]
    public void Every_achievement_can_be_earned()
    {
        var p = new Profile();
        for (var i = 0; i < 100; i++) RecordBattle(p, Win(LobbyMode.Skirmish, (Faction)(i % 3), seconds: 300, kills: 100, lost: 0) with
            { Network = true, Hosted = true, Enemies = 3, Hardest = Difficulty.Hard }, Now);
        foreach (var m in new[] { LobbyMode.Zones, LobbyMode.Assault, LobbyMode.Regicide, LobbyMode.Campaign, LobbyMode.Sandbox })
            RecordBattle(p, Win(m), Now);
        for (var i = 0; i < 10; i++) RecordBattle(p, new BattleRecap(LobbyMode.Skirmish, false, Faction.Klaed, 300, 0, 100, 0), Now);
        RecordBattle(p, new BattleRecap(LobbyMode.Survival, false, Faction.Klaed, 36000, 0, 0, 0, Waves: 50), Now);
        AddTime(p, 36000, Now);
        RecordCampaign(p, true, Now);
        foreach (var m in Enum.GetValues<Moment>()) Note(p, m, Now);
        Assert.Equal(All.Count, p.Unlocked.Count);
    }

    [Fact]
    public void A_profile_comes_back_as_it_was_saved()
    {
        var p = new Profile();
        RecordBattle(p, Win() with { Hardest = Difficulty.Hard }, Now);
        RecordCampaign(p, true, Now);
        AddTime(p, 1234.5, Now);
        Note(p, Moment.Colossus, Now);
        var back = Parse(Serialize(p));
        Assert.Equal(Serialize(p), Serialize(back));
        Assert.Equal(p.Unlocked, back.Unlocked);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("[1, 2, 3]")]
    [InlineData("{\"Stats\": 7}")]
    [InlineData("{\"Stats\": {\"Kills\": 1e400}}")]
    [InlineData("null")]
    public void A_damaged_file_reads_as_an_empty_profile(string? text)
    {
        var p = Parse(text);
        Assert.Equal(0, p.Stats.Battles);
        Assert.Empty(p.Unlocked);
    }

    [Fact]
    public void A_forged_file_keeps_only_known_keys_and_bounded_counts()
    {
        var text = """
            {
              "Stats": {
                "Played": { "Skirmish": 4, "Nonsense": 9, "skirmish": 3, "1": 5, "Zones": -8 },
                "WonAs": { "Klaed": 2147483647, "Errant": 1, "999": 1 },
                "Kills": -50, "Lost": 99999999999, "Seconds": -1, "BestWave": 2000000000,
                "Played2": 3
              },
              "Unlocked": { "first-win": 1700000000000, "made-up": 1, "skirmish": -5 }
            }
            """;
        var p = Parse(text);
        Assert.Equal(new Dictionary<string, int> { ["Skirmish"] = 4, ["Zones"] = 0 }, p.Stats.Played);
        Assert.Equal(["Klaed", "Errant"], p.Stats.WonAs.Keys.ToArray());
        Assert.Equal(100_000_000, p.Stats.WonAs["Klaed"]);
        Assert.Equal((0L, 100_000_000L, 0.0, 100_000), (p.Stats.Kills, p.Stats.Lost, p.Stats.Seconds, p.Stats.BestWave));
        Assert.Equal(new Dictionary<string, long> { ["first-win"] = 1700000000000, ["skirmish"] = 0 }, p.Unlocked);
    }

    [Fact]
    public void A_file_too_large_is_not_read()
    {
        var huge = "{\"Stats\": {\"Kills\": 5}, \"Pad\": \"" + new string('x', 300 * 1024) + "\"}";
        Assert.Equal(0, Parse(huge).Stats.Kills);
    }

    /// <summary>Mutations of a real profile's text, from a fixed seed: never an exception, never a count out of bounds.</summary>
    [Fact]
    public void Broken_profiles_never_throw()
    {
        var p = new Profile();
        RecordBattle(p, Win() with { Network = true }, Now);
        Reached(p, 25, Now);
        var seed = Encoding.UTF8.GetBytes(Serialize(p));
        var rng = new Random(4242);
        for (var i = 0; i < 2000; i++)
        {
            var b = seed.ToArray();
            for (var k = rng.Next(1, 6); k > 0; k--) b[rng.Next(b.Length)] = (byte)rng.Next(32, 127);
            var text = Encoding.UTF8.GetString(b, 0, rng.Next(b.Length + 1));
            var read = Parse(text);
            Assert.InRange(read.Stats.Kills, 0, 100_000_000);
            Assert.InRange(read.Stats.BestWave, 0, 100_000);
            Assert.All(read.Unlocked.Keys, id => Assert.NotNull(Find(id)));
        }
    }
}
