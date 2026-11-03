// AI personalities (not in the web version): how a personality is chosen, and that each one
// plays the way it says against the same opponent.
using SandboxShip.Sim.Data;
using SandboxShip.Sim.Net;
using Xunit;
using Xunit.Abstractions;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim.Tests;

public class PersonalityTests(ITestOutputHelper output)
{
    [Fact]
    public void Balanced_plays_as_the_AI_always_did()
    {
        var d = Personalities.Defs[Personality.Balanced];
        Assert.Equal((1.0, 1.2, 1800.0, 0.0), (d.Wave, d.WaveGrowth, d.AllInTime, d.Counter));
        Assert.Equal((1, 0.25, 2, 0.15, 0.15, 0.2), (d.Platforms, d.PlatformOdds, d.Outposts, d.OutpostOdds, d.LabOdds, d.ResearchOdds));
        Assert.Equal((0.5, 8, 0.25, 1.0), (d.SaveShare, d.LanceFleet, d.LanceOdds, d.Edge));
        Assert.All(new[] { SizeClass.Small, SizeClass.Medium, SizeClass.Large }, s => Assert.Equal(1, d.Lean(s)));
        Assert.Equal(4500, NewAI().WaveValue);
        Assert.Equal(Personality.Balanced, NewAI().Personality);
        Assert.Equal(Personalities.All.Count, Personalities.Defs.Count);
    }

    [Fact]
    public void A_random_draw_is_the_same_everywhere_and_gives_each_of_the_four()
    {
        var drawn = new Dictionary<Personality, int>();
        for (var seed = 1; seed <= 300; seed++)
        {
            for (var seat = 0; seat < 6; seat++)
            {
                var p = Personalities.Draw(seed, seat);
                Assert.Equal(p, Personalities.Draw(seed, seat));
                drawn[p] = drawn.GetValueOrDefault(p) + 1;
            }
        }
        Assert.False(drawn.ContainsKey(Personality.Balanced));
        // 1800 draws: each of the four near a quarter.
        Assert.All(Personalities.Drawn, p => Assert.InRange(drawn[p], 350, 550));
        // Seats of one game do not all get the same.
        Assert.True(Enumerable.Range(1, 50).Count(seed => Enumerable.Range(0, 4).Select(s => Personalities.Draw(seed, s)).Distinct().Count() > 1) > 45);
    }

    [Fact]
    public void A_lobby_draws_a_random_bot_from_its_seed_and_keeps_a_chosen_one()
    {
        var lobby = Lobby.Create(LobbyMode.Skirmish, 42);
        var bot = lobby.Seats[1];
        Assert.Null(bot.Personality);
        Assert.Equal(Personalities.Draw(42, bot.Id), lobby.Slots()[1].Personality);
        Assert.Equal(Personality.Balanced, lobby.Slots()[0].Personality);
        bot.Personality = Personality.Defensive;
        Assert.Equal(Personality.Defensive, lobby.Slots()[1].Personality);
        Assert.Equal(Personality.Defensive, lobby.Skirmish().Slots[1].Personality);
        Assert.True(lobby.Sane());
        bot.Personality = (Personality)99;
        Assert.False(lobby.Sane());
    }

    [Fact]
    public void Only_the_leader_sets_a_bot_s_personality()
    {
        var l = Lobby.Create(LobbyMode.Skirmish, 5);
        var (human, bot) = (l.Seats[0], l.Seats[1]);
        Assert.False(LobbyRules.Apply(l, 7, false, new LobbyRequest.SetPersonality(bot.Id, Personality.Economic)));
        Assert.False(LobbyRules.Apply(l, Lobby.Host, true, new LobbyRequest.SetPersonality(human.Id, Personality.Economic)));
        Assert.False(LobbyRules.Apply(l, Lobby.Host, true, new LobbyRequest.SetPersonality(bot.Id, (Personality)42)));
        Assert.True(LobbyRules.Apply(l, Lobby.Host, true, new LobbyRequest.SetPersonality(bot.Id, Personality.Economic)));
        Assert.Equal(Personality.Economic, bot.Personality);
        Assert.True(LobbyRules.Apply(l, Lobby.Host, true, new LobbyRequest.SetPersonality(bot.Id, null)));
        Assert.Null(bot.Personality);
        // Over the network the lobby travels whole: the personality with it.
        bot.Personality = Personality.Defensive;
        var copy = System.Text.Json.JsonSerializer.Deserialize<Lobby>(System.Text.Json.JsonSerializer.Serialize(l, Lobby.Json), Lobby.Json)!;
        Assert.Equal(Personality.Defensive, copy.Seats[1].Personality);
        Assert.Null(copy.Seats[0].Personality);
    }

    [Fact]
    public void A_resumed_campaign_keeps_its_bots_personalities()
    {
        var slots = new List<Slot>
        {
            new(Faction.Klaed, Faction.Klaed),
            new(Faction.Nairan, Faction.Nairan, 0, Difficulty.Normal, null, Personality.Defensive),
        };
        var g = Galaxies.CreateGalaxy(slots, 3);
        var lobby = Lobby.Resume(g, 3)!;
        Assert.Equal(Personality.Defensive, lobby.Seats[1].Personality);
        g.Factions[Faction.Nairan].AI = null;
        lobby.Seat(g, _ => "moi");
        Assert.Equal(Personality.Defensive, g.Factions[Faction.Nairan].AI!.Personality);
    }

    [Fact]
    public void Every_AI_plays_its_seat_s_personality_in_battle_and_in_the_campaign()
    {
        var slots = new List<Slot>
        {
            new(Faction.Klaed, Faction.Klaed),
            new(Faction.Nairan, Faction.Nairan, 0, Difficulty.Normal, null, Personality.Aggressive),
            new(Faction.Nautolan, Faction.Nautolan, 0, Difficulty.Hard, null, Personality.Technological),
        };
        var w = Scenario.CreateSkirmish(new SkirmishOptions { Slots = slots, Seed = 3 });
        Assert.Null(w.Factions[Faction.Klaed].AI);
        Assert.Equal(Personality.Aggressive, w.Factions[Faction.Nairan].AI!.Personality);
        Assert.Equal(4500 * 0.6, w.Factions[Faction.Nairan].AI!.WaveValue, 6);
        Assert.Equal(Personality.Technological, w.Factions[Faction.Nautolan].AI!.Personality);
        var g = Galaxies.CreateGalaxy(slots, 3, GalaxyGen.GenerateLayout(3, 3, MapSize.Small));
        Assert.Equal(Personality.Aggressive, g.Factions[Faction.Nairan].AI!.Personality);
        Assert.Equal(Personality.Technological, g.Factions[Faction.Nautolan].AI!.Personality);
    }

    [Fact]
    public void A_replay_keeps_each_seat_s_personality()
    {
        var o = new SkirmishOptions
        {
            Slots = [new(Faction.Klaed, Faction.Klaed, 0, Difficulty.Normal, null, Personality.Economic), new(Faction.Nairan, Faction.Nairan, 0, Difficulty.Normal, null, Personality.Defensive)],
            Seed = 9,
        };
        var w = Scenario.CreateSkirmish(o);
        Assert.True(Replays.Record(w));
        Steps.Run(w, 30);
        using var file = new MemoryStream();
        Replays.Write(file, Replays.Of(w, Faction.Klaed, "test", 0)!);
        file.Position = 0;
        var back = Replays.Read(file);
        // Played again, each AI plays as it did.
        var player = new ReplayPlayer(back);
        while (!player.Over) player.Step();
        Assert.True(player.Faithful);
        Assert.Equal(Replays.Digest(w), Replays.Digest(player.World));
        Assert.Equal([Personality.Economic, Personality.Defensive], back.Options.Slots.Select(s => s.Personality));
    }

    /// <summary>What an AI did over its first minutes against a player who does nothing.</summary>
    sealed record Played(double FirstStrike, int Platforms, int Outposts, int Research, double LargeShare);

    /// <summary>The Nairan AI with this personality against a passive Kla'ed player, for `minutes`.</summary>
    static Played Play(Personality personality, int seed, double minutes)
    {
        var w = Scenario.CreateSkirmish(new SkirmishOptions
        {
            Slots = [new(Faction.Klaed, Faction.Klaed), new(Faction.Nairan, Faction.Nairan, 0, Difficulty.Normal, null, personality)],
            Seed = seed,
        });
        var target = StationOf(w, Faction.Klaed)!;
        var strike = double.PositiveInfinity;
        var built = new HashSet<int>();
        var (large, ships) = (0, 0);
        for (var t = 0; t < minutes * 60 && w.Winner is null; t++)
        {
            Steps.Run(w, 1);
            foreach (var e in w.Entities)
            {
                if (e.Side != Faction.Nairan || e.Kind != EntityKind.Ship || e.Launched) continue;
                if (double.IsInfinity(strike) && Dist(e, target) < 2500) strike = w.Time;
                if (!built.Add(e.Id)) continue;
                ships++;
                if (Ships.Defs[e.Cls].Size == SizeClass.Large) large++;
            }
            w.Events.Clear();
        }
        var own = w.Entities.Where(e => e.Side == Faction.Nairan).ToList();
        var research = Techs.All.Sum(tech => w.Factions[Faction.Nairan].Research[tech]);
        return new Played(strike, own.Count(e => e.Type == "platform"), own.Count(e => e.Type == "outpost"), research, ships == 0 ? 0 : (double)large / ships);
    }

    [Fact]
    public void Each_personality_plays_the_way_it_says()
    {
        // Two seeds each, a quarter of an hour against a player who does nothing.
        var runs = Personalities.All.ToDictionary(p => p, p => new[] { Play(p, 4, 15), Play(p, 8, 15) });
        double Mean(Personality p, Func<Played, double> of) => runs[p].Average(of);
        foreach (var (p, r) in runs) output.WriteLine($"{p}: {string.Join(" | ", r.Select(x => x.ToString()))}");
        // The aggressive AI strikes first.
        Expect.Less(Mean(Personality.Aggressive, r => r.FirstStrike), Mean(Personality.Balanced, r => r.FirstStrike));
        Expect.Less(Mean(Personality.Aggressive, r => r.FirstStrike), Mean(Personality.Economic, r => r.FirstStrike));
        // The defensive one stands behind the most platforms, the aggressive one behind the fewest.
        foreach (var other in Personalities.All.Where(p => p != Personality.Defensive))
            Expect.Greater(Mean(Personality.Defensive, r => r.Platforms), Mean(other, r => r.Platforms));
        Expect.Less(Mean(Personality.Aggressive, r => r.Platforms), Mean(Personality.Balanced, r => r.Platforms) + 0.01);
        // The thrifty one expands the most.
        Expect.Greater(Mean(Personality.Economic, r => r.Outposts), Mean(Personality.Balanced, r => r.Outposts));
        Expect.Greater(Mean(Personality.Economic, r => r.Outposts), Mean(Personality.Aggressive, r => r.Outposts));
        // The technological one researches the most, with the biggest share of large hulls.
        foreach (var other in Personalities.All.Where(p => p != Personality.Technological))
            Expect.Greater(Mean(Personality.Technological, r => r.Research), Mean(other, r => r.Research));
        Expect.Greater(Mean(Personality.Technological, r => r.LargeShare), Mean(Personality.Aggressive, r => r.LargeShare));
    }
}
