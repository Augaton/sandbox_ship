// The difficulty levels against each other in the campaign, at the same income: the same race and
// personality on both sides, each side taking each start in turn, so that only what each level
// decides (and how often it thinks) differs. A measure, not a check, and a long one: it only runs
// when asked,
//   DIFFICULTY=1 dotnet test Sim.Tests --filter DifficultyBalance --logger "console;verbosity=detailed"
// The figures of the last run are in PLAN.md.
using SandboxShip.Sim.Data;
using Xunit;
using Xunit.Abstractions;
using static SandboxShip.Sim.Galaxies;

namespace SandboxShip.Sim.Tests;

public class DifficultyBalanceTests(ITestOutputHelper output)
{
    const int Seeds = 8;
    const double Limit = 60 * 60;
    static readonly Faction[] Races = [Faction.Klaed, Faction.Nairan, Faction.Nautolan];

    /// <summary>The game of two levels on a map; the level that won, if one did.</summary>
    static Difficulty? Duel(Difficulty first, Difficulty second, MapSize? size, int seed)
    {
        var (one, two) = (Lobby.SideOf(0), Lobby.SideOf(1));
        var race = Races[seed % Races.Length];
        var personality = Personalities.Draw(seed, 0);
        var g = CreateGalaxy([new Slot(one, race, 0, first, null, personality), new Slot(two, race, 0, second, null, personality)], seed,
            size is MapSize m ? GalaxyGen.GenerateLayout(seed, 2, m) : GalaxyGen.Classic);
        g.Player = (Faction)99;
        while (g.Winner is null && g.Time < Limit)
        {
            StepGalaxy(g);
            g.Events.Clear();
            // What the level's income factor took or added is given back: the same income for both.
            foreach (var f in g.Factions.Values)
            {
                if (f.AI is { } ai && !f.Eliminated) f.Credits += IncomeOf(g, f.Id) * (1 - Worlds.Difficulties[ai.Difficulty].Income) * Step;
            }
        }
        return g.Winner == one ? first : g.Winner == two ? second : null;
    }

    [Fact]
    public void Measures_the_levels_against_each_other_at_the_same_income()
    {
        if (Environment.GetEnvironmentVariable("DIFFICULTY") is null) return;
        (Difficulty Strong, Difficulty Weak)[] pairs = [(Difficulty.Hard, Difficulty.Normal), (Difficulty.Normal, Difficulty.Easy), (Difficulty.Hard, Difficulty.Easy)];
        var runs = (from pair in pairs
                    from size in new MapSize?[] { null, MapSize.Small, MapSize.Medium }
                    from swap in new[] { false, true }
                    from seed in Enumerable.Range(1, Seeds)
                    select (Pair: pair, Size: size, Swap: swap, Seed: seed * 17 + 2)).ToList();
        var won = new Difficulty?[runs.Count];
        Parallel.For(0, runs.Count, i =>
        {
            var (pair, size, swap, seed) = runs[i];
            won[i] = swap ? Duel(pair.Weak, pair.Strong, size, seed) : Duel(pair.Strong, pair.Weak, size, seed);
        });
        foreach (var pair in pairs)
        {
            var games = Enumerable.Range(0, runs.Count).Where(i => runs[i].Pair == pair).ToList();
            var (strong, weak) = (games.Count(i => won[i] == pair.Strong), games.Count(i => won[i] == pair.Weak));
            output.WriteLine($"{pair.Strong} contre {pair.Weak} : {strong} - {weak}, {games.Count - strong - weak} nuls, sur {games.Count}");
        }
    }
}
