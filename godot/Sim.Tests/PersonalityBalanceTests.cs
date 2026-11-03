// AI personalities against each other: every pair, the same race on both sides so that only the
// personality differs, each side taking each start in turn. A measure, not a check, and a long
// one: it only runs when asked, one series at a time,
//   PERSONALITIES=1 dotnet test Sim.Tests --filter PersonalityBalance --logger "console;verbosity=detailed"
// The figures of the last run are in PLAN.md.
using SandboxShip.Sim.Data;
using Xunit;
using Xunit.Abstractions;
using static SandboxShip.Sim.Galaxies;

namespace SandboxShip.Sim.Tests;

public class PersonalityBalanceTests(ITestOutputHelper output)
{
    static bool Asked => Environment.GetEnvironmentVariable("PERSONALITIES") is not null;

    static readonly Faction[] Races = [Faction.Klaed, Faction.Nairan, Faction.Nautolan];

    /// <summary>Every pair of two different personalities.</summary>
    static IEnumerable<(Personality A, Personality B)> Pairs() =>
        from a in Personalities.All from b in Personalities.All where a < b select (a, b);

    sealed class Tally
    {
        public readonly Dictionary<Personality, int> Wins = Personalities.All.ToDictionary(p => p, _ => 0), Games = Personalities.All.ToDictionary(p => p, _ => 0);
        public readonly List<double> Lengths = [];
        public int Draws;

        public void Add(Personality a, Personality b, Personality? won, double length)
        {
            Games[a]++;
            Games[b]++;
            if (won is Personality w)
            {
                Wins[w]++;
                Lengths.Add(length);
            }
            else Draws++;
        }

        public void Print(ITestOutputHelper output, string unit)
        {
            foreach (var p in Personalities.All) output.WriteLine($"{p}: {Wins[p]} / {Games[p]} ({Js.Round(100.0 * Wins[p] / Math.Max(1, Games[p]))} %)");
            output.WriteLine($"nuls : {Draws}");
            Lengths.Sort();
            if (Lengths.Count > 0) output.WriteLine($"durée : médiane {Js.Round(Lengths[Lengths.Count / 2])} {unit}, de {Js.Round(Lengths[0])} à {Js.Round(Lengths[^1])} {unit}");
        }
    }

    [Fact]
    public void Measures_the_personalities_against_each_other_in_battle()
    {
        if (!Asked) return;
        const double limit = 60 * 60;
        var tally = new Tally();
        foreach (var (a, b) in Pairs())
        {
            for (var game = 0; game < 4; game++)
            {
                var race = Races[game % Races.Length];
                // Each personality takes each start.
                var (first, second) = game % 2 == 0 ? (a, b) : (b, a);
                var (one, two) = (Lobby.SideOf(0), Lobby.SideOf(1));
                var w = Scenario.CreateSkirmish(new SkirmishOptions
                {
                    Slots = [new(one, race, 0, Difficulty.Normal, null, first), new(two, race, 0, Difficulty.Normal, null, second)],
                    Seed = 101 * (game / 2 + 1),
                });
                while (w.Winner is null && w.Time < limit)
                {
                    Steps.StepWorld(w, 1.0 / 30);
                    w.Events.Clear();
                }
                Personality? won = w.Winner == one ? first : w.Winner == two ? second : null;
                tally.Add(a, b, won, w.Time / 60);
                output.WriteLine($"{first} contre {second} ({race}, graine {101 * (game / 2 + 1)}) : {won?.ToString() ?? "nul"} en {Js.Round(w.Time / 60)} min");
            }
        }
        output.WriteLine("--- bataille ---");
        tally.Print(output, "min");
    }

    [Fact]
    public void Measures_the_personalities_against_each_other_in_the_campaign()
    {
        if (!Asked) return;
        var tally = new Tally();
        foreach (var (a, b) in Pairs())
        {
            for (var game = 0; game < 4; game++)
            {
                var race = Races[game % Races.Length];
                var (first, second) = game % 2 == 0 ? (a, b) : (b, a);
                var (one, two) = (Lobby.SideOf(0), Lobby.SideOf(1));
                var seed = 10 * (game / 2 + 1);
                var g = CreateGalaxy([new Slot(one, race, 0, Difficulty.Normal, null, first), new Slot(two, race, 0, Difficulty.Normal, null, second)],
                    seed, GalaxyGen.GenerateLayout(seed, 2, MapSize.Small));
                g.Player = (Faction)99;
                for (var minute = 0; minute < 60 && g.Winner is null; minute++)
                {
                    RunGalaxy(g, 60);
                    g.Events.Clear();
                }
                Personality? won = g.Winner == one ? first : g.Winner == two ? second : null;
                tally.Add(a, b, won, g.Time / 60);
                output.WriteLine($"{first} contre {second} ({race}, graine {seed}) : {won?.ToString() ?? "nul"} en {Js.Round(g.Time / 60)} min");
            }
        }
        output.WriteLine("--- campagne ---");
        tally.Print(output, "min");
    }
}
