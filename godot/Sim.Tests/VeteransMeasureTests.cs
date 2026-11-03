// What veterans change, AI against AI: the same skirmishes played twice, once as the rules are,
// once with every rank and all experience taken away as soon as earned. Prints win rates, game
// lengths and how far ships rose. A measure, not a check, and a long one: it only runs when asked,
//   VETERANS=1 dotnet test Sim.Tests --filter VeteransMeasure --logger "console;verbosity=detailed"
// VETERANS=FILE: the lines go to that file as well, as they come.
using SandboxShip.Sim.Data;
using Xunit;
using Xunit.Abstractions;

namespace SandboxShip.Sim.Tests;

public class VeteransMeasureTests(ITestOutputHelper output)
{
    const int Seeds = 3;
    const double Limit = 60 * 60;
    const double Dt = 1.0 / 30;

    [Fact]
    public void Measures_what_veterans_change()
    {
        if (Environment.GetEnvironmentVariable("VETERANS") is null) return;
        Series(true);
        Series(false);
    }

    void Series(bool veterans)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var wins = Factions.All.ToDictionary(f => f, _ => 0);
        var games = Factions.All.ToDictionary(f => f, _ => 0);
        var lengths = new List<double>();
        // Ships built, and the highest rank each reached.
        var reached = new int[Veterans.MaxRank + 1];
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
                    var best = new Dictionary<int, int>();
                    while (w.Winner is null && w.Time < Limit)
                    {
                        Steps.StepWorld(w, Dt);
                        foreach (var e in w.Entities)
                        {
                            if (e.Kind != EntityKind.Ship || e.Launched) continue;
                            if (!veterans && (e.Rank > 0 || e.Xp > 0))
                            {
                                Veterans.Promote(w, e, 0);
                                e.Xp = 0;
                            }
                            best[e.Id] = Math.Max(best.GetValueOrDefault(e.Id), e.Rank);
                        }
                        w.Events.Clear();
                    }
                    foreach (var rank in best.Values) reached[rank]++;
                    games[a]++;
                    games[b]++;
                    if (w.Winner is Side won)
                    {
                        wins[Worlds.RaceOf(w, won)]++;
                        lengths.Add(w.Time);
                    }
                    else draws++;
                    Say($"{a} contre {b}, graine {seed}: {(w.Winner is Side s ? Worlds.RaceOf(w, s).ToString() : "nul")} en {Js.Round(w.Time / 60)} min");
                }
            }
        }
        Say($"--- {(veterans ? "avec" : "sans")} vétérans ({Js.Round(clock.Elapsed.TotalMinutes)} min de calcul) ---");
        foreach (var f in Factions.All) Say($"{f}: {wins[f]} / {games[f]} ({Js.Round(100.0 * wins[f] / games[f])} %)");
        Say($"nuls ({Limit / 60} min sans vainqueur): {draws}");
        lengths.Sort();
        if (lengths.Count > 0)
            Say($"durée: médiane {Js.Round(lengths[lengths.Count / 2] / 60)} min, de {Js.Round(lengths[0] / 60)} à {Js.Round(lengths[^1] / 60)} min");
        var built = reached.Sum();
        Say($"vaisseaux: {built}, rang atteint: " + string.Join(", ", reached.Select((n, r) => $"{r}: {n} ({Js.Round(1000.0 * n / Math.Max(1, built)) / 10} %)")));
    }

    void Say(string line)
    {
        output.WriteLine(line);
        if (Environment.GetEnvironmentVariable("VETERANS") is { } file && file != "1") File.AppendAllText(file, line + "\n");
    }
}
