// Port of tests/balance/duel.ts: every pair of factions fights many skirmishes, both AIs on
// normal difficulty, each side taking each start in turn. Prints win rates, game lengths and
// what got built. A measure, not a check, and a long one: it only runs when asked,
//   BALANCE=1 dotnet test Sim.Tests --filter Balance --logger "console;verbosity=detailed"
using SandboxShip.Sim.Data;
using Xunit;
using Xunit.Abstractions;

namespace SandboxShip.Sim.Tests;

public class BalanceTests(ITestOutputHelper output)
{
    const int Seeds = 6;
    // An hour: dearer station upgrades make games longer than in the web version (30 min there).
    const double Limit = 60 * 60;
    const double Dt = 1.0 / 30;

    [Fact]
    public void Measures_the_factions_against_each_other()
    {
        if (Environment.GetEnvironmentVariable("BALANCE") is null) return;
        var wins = Factions.All.ToDictionary(f => f, _ => 0);
        var games = Factions.All.ToDictionary(f => f, _ => 0);
        var lengths = new List<double>();
        var built = new Dictionary<string, int>();
        var draws = 0;
        foreach (var a in Factions.All)
        {
            foreach (var b in Factions.All)
            {
                if (a == b) continue;
                for (var seed = 1; seed <= Seeds; seed++)
                {
                    var w = Scenario.CreateSkirmish(new SkirmishOptions { Player = a, Enemies = [b], EnemyAI = true, Seed = seed * 101 });
                    w.Factions[a].AI = Worlds.NewAI();
                    w.Factions[a].AutoAbilities = true;
                    var seen = new HashSet<int>();
                    while (w.Winner is null && w.Time < Limit)
                    {
                        Steps.StepWorld(w, Dt);
                        foreach (var e in w.Entities)
                        {
                            if (e.Kind != EntityKind.Ship || e.Launched || !seen.Add(e.Id)) continue;
                            var k = e.Hero is HeroId h ? $"héros {h}" : e.Type;
                            built[k] = built.GetValueOrDefault(k) + 1;
                        }
                        w.Events.Clear();
                    }
                    games[a]++;
                    games[b]++;
                    if (w.Winner is Faction winner)
                    {
                        wins[winner]++;
                        lengths.Add(w.Time);
                    }
                    else draws++;
                    output.WriteLine($"{a} contre {b}, graine {seed}: {w.Winner?.ToString() ?? "nul"} en {Js.Round(w.Time / 60)} min");
                }
            }
        }
        output.WriteLine("--- victoires ---");
        foreach (var f in Factions.All) output.WriteLine($"{f}: {wins[f]} / {games[f]} ({Js.Round(100.0 * wins[f] / games[f])} %)");
        output.WriteLine($"nuls ({Limit / 60} min sans vainqueur): {draws}");
        lengths.Sort();
        if (lengths.Count > 0)
            output.WriteLine($"durée: médiane {Js.Round(lengths[lengths.Count / 2] / 60)} min, de {Js.Round(lengths[0] / 60)} à {Js.Round(lengths[^1] / 60)} min");
        output.WriteLine("construits: " + string.Join(", ", built.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}")));
    }
}
