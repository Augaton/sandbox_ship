// The levels of the battle AI against each other, the same race and personality on both sides,
// each side taking each start in turn. A measure, not a check, and a long one: it only runs when
// asked,
//   BALANCE=1 dotnet test Sim.Tests --filter AiLevelsBalance --logger "console;verbosity=detailed"
// By default at equal income: what is left is what each level decides. SEEDS=N: N seeds per race
// and pairing instead of 4. RAW=1: with the income each level has in the game. PAIRING=Hard-Normal:
// that pairing only.
using SandboxShip.Sim.Data;
using Xunit;
using Xunit.Abstractions;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim.Tests;

public class AiLevelsBalanceTests(ITestOutputHelper output)
{
    static readonly int Seeds = int.TryParse(Environment.GetEnvironmentVariable("SEEDS"), out var n) && n > 0 ? n : 4;
    static readonly bool Raw = Environment.GetEnvironmentVariable("RAW") is not null;
    const double Limit = 60 * 60;
    const double Dt = 1.0 / 30;

    /// <summary>Plays it out. At equal income, what each AI earned beyond the plain rate of the game is taken back, step by step.</summary>
    static World Play(SkirmishOptions o)
    {
        var w = Scenario.CreateSkirmish(o);
        var earned = w.Factions.Keys.ToDictionary(s => s, _ => 0.0);
        while (w.Winner is null && w.Time < Limit)
        {
            Steps.StepWorld(w, Dt);
            w.Events.Clear();
            if (Raw) continue;
            foreach (var f in w.Factions.Values)
            {
                var income = f.Stats.Earned - f.Bounties;
                f.Credits += (income - earned[f.Id]) * (1 / Difficulties[f.AI!.Difficulty].BattleIncome - 1);
                earned[f.Id] = income;
            }
        }
        return w;
    }

    [Fact]
    public void Measures_the_levels_against_each_other()
    {
        if (Environment.GetEnvironmentVariable("BALANCE") is null) return;
        var pairings = new[] { (Difficulty.Hard, Difficulty.Normal), (Difficulty.Normal, Difficulty.Easy), (Difficulty.Hard, Difficulty.Easy) }
            .Where(p => Environment.GetEnvironmentVariable("PAIRING") is not { } only || only == $"{p.Item1}-{p.Item2}").ToArray();
        var (one, two) = (Lobby.SideOf(0), Lobby.SideOf(1));
        var games = (from p in pairings from race in Factions.All from g in Enumerable.Range(0, Seeds * 2) select (p.Item1, p.Item2, race, g)).AsParallel().AsOrdered().Select(x =>
        {
            var (high, low, race, g) = x;
            var (first, second) = g % 2 == 0 ? (high, low) : (low, high);
            var w = Play(new SkirmishOptions { Slots = [new(one, race, 0, first), new(two, race, 0, second)], Seed = 101 * (g / 2 + 1) });
            Difficulty? won = w.Winner == one ? first : w.Winner == two ? second : null;
            return (High: high, Low: low, Race: race, Won: won, Minutes: w.Time / 60);
        }).ToList();
        foreach (var (high, low) in pairings)
        {
            var mine = games.Where(g => g.High == high && g.Low == low).ToList();
            var byRace = string.Join(", ", Factions.All.Select(r => $"{r} {mine.Count(g => g.Race == r && g.Won == high)} / {mine.Count(g => g.Race == r)}"));
            var lengths = mine.Select(g => g.Minutes).Order().ToList();
            output.WriteLine($"{high} contre {low} : {mine.Count(g => g.Won == high)} / {mine.Count} ({Js.Round(100.0 * mine.Count(g => g.Won == high) / mine.Count)} %), {mine.Count(g => g.Won is null)} nuls ; {byRace} ; durée médiane {Js.Round(lengths[lengths.Count / 2])} min");
        }
    }
}
