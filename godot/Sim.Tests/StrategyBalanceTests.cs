// The campaign AI's strategy against the AI without it, face to face: campaigns with the rules of a
// new one (independents, fortified capitals, the opening peace, the fleet cap), every side of the
// same race, personality and level, so that only what it leaves out (GalaxyAIState.Without)
// differs; each takes each start in turn. A measure, not a check, and a long one: it only runs when
// asked, in Release,
//   STRATEGY=1 dotnet test Sim.Tests -c Release --filter StrategyBalance --logger "console;verbosity=detailed"
// NEW and OLD: what each side leaves out, bits of GalaxyAi.Strategy (0 and everything by default);
// SIZES, PLAYERS, SEEDS (of Seeds, in order), THREADS, LEVEL. Alike, both sides play one game per
// map: it is the same from either start. STRATEGY_COST=1 measures what a step costs instead. The
// figures of the last runs are in PLAN.md.
using SandboxShip.Sim.Data;
using Xunit;
using Xunit.Abstractions;
using static SandboxShip.Sim.Galaxies;

namespace SandboxShip.Sim.Tests;

public class StrategyBalanceTests(ITestOutputHelper output)
{
    static readonly Faction[] Races = [Faction.Klaed, Faction.Nairan, Faction.Nautolan];
    const double Limit = 180 * 60;
    /// <summary>Seeds drawing each personality with each race (Personalities.Draw, Races): the first four, one personality each.</summary>
    static readonly int[] Seeds = [15, 10, 5, 1, 4, 8, 6, 3, 11, 9, 19, 2];

    static string? Env(string name) => Environment.GetEnvironmentVariable(name);

    /// <summary>A campaign where seat `fresh` leaves out `freshMask` and every other seat `oldMask`: who won, when, and the personality played.</summary>
    static (bool Fresh, bool Old, double Time, Personality Personality) Game(MapSize size, int players, int seed, int fresh, int freshMask, int oldMask, Difficulty level)
    {
        var race = Races[seed % Races.Length];
        var personality = Personalities.Draw(seed, 0);
        var slots = Enumerable.Range(0, players).Select(i => new Slot(Lobby.SideOf(i), race, 0, level, null, personality)).ToList();
        var g = CreateGalaxy(slots, seed, GalaxyGen.GenerateLayout(seed, players, size), setup: CampaignSetup.Of(10));
        g.Player = (Faction)99;
        for (var i = 0; i < players; i++) g.Factions[Lobby.SideOf(i)].AI!.Without = i == fresh ? freshMask : oldMask;
        while (g.Winner is null && g.Time < Limit)
        {
            StepGalaxy(g);
            g.Events.Clear();
        }
        if (g.Winner is not Side won) return (false, false, g.Time, personality);
        bool Won(int seat) => Lobby.SideOf(seat) == won || Allies(g, Lobby.SideOf(seat), won);
        return (Won(fresh), Enumerable.Range(0, players).Any(i => i != fresh && Won(i)), g.Time, personality);
    }

    /// <summary>
    /// What a campaign step costs with every side leaving out `mask`: the AI's share and the whole
    /// step, in milliseconds, and the bytes the AI allocates, over the first hour.
    /// </summary>
    static (double AiMs, double StepMs, double AiKib) Cost(MapSize size, int players, int seed, int mask)
    {
        var slots = Enumerable.Range(0, players).Select(i => new Slot(Lobby.SideOf(i), Races[i % 3], 0, Difficulty.Normal, null, Personalities.Draw(seed, i))).ToList();
        var g = CreateGalaxy(slots, seed, GalaxyGen.GenerateLayout(seed, players, size), setup: CampaignSetup.Of(10));
        g.Player = (Faction)99;
        foreach (var f in g.Factions.Values) f.AI!.Without = mask;
        var (ai, step) = (new System.Diagnostics.Stopwatch(), new System.Diagnostics.Stopwatch());
        long bytes = 0, steps = 0;
        while (g.Winner is null && g.Time < 3600)
        {
            step.Start();
            g.Time += Step;
            var before = GC.GetAllocatedBytesForCurrentThread();
            ai.Start();
            GalaxyAi.UpdateGalaxyAI(g, Step);
            ai.Stop();
            bytes += GC.GetAllocatedBytesForCurrentThread() - before;
            UpdateGalaxy(g, Step);
            step.Stop();
            g.Events.Clear();
            steps++;
        }
        return (ai.Elapsed.TotalMilliseconds / steps, step.Elapsed.TotalMilliseconds / steps, bytes / 1024.0 / steps);
    }

    [Fact]
    public void Measures_what_the_strategy_costs()
    {
        if (Env("STRATEGY_COST") is null) return;
        var mask = int.TryParse(Env("NEW"), out var n) ? n : 0;
        foreach (var (size, players, seed) in new[] { (MapSize.Large, 2, 31), (MapSize.Immense, 3, 1), (MapSize.Immense, 6, 2) })
        {
            foreach (var m in new[] { -1, mask })
            {
                var (aiMs, stepMs, kib) = Cost(size, players, seed, m);
                output.WriteLine($"{size} à {players}, graine {seed}, sans {m} : IA {aiMs:F3} ms et {kib:F1} Kio par pas, pas entier {stepMs:F3} ms");
            }
        }
    }

    [Fact]
    public void Measures_the_strategy_against_the_AI_without_it()
    {
        if (Env("STRATEGY") is null) return;
        var freshMask = int.TryParse(Env("NEW"), out var n) ? n : 0;
        var oldMask = int.TryParse(Env("OLD"), out var o) ? o : -1;
        var seeds = int.TryParse(Env("SEEDS"), out var s) && s > 0 ? s : 4;
        var threads = int.TryParse(Env("THREADS"), out var t) && t > 0 ? t : 2;
        var level = Enum.TryParse<Difficulty>(Env("LEVEL"), out var l) ? l : Difficulty.Normal;
        var sizes = (Env("SIZES") ?? "Medium,Large,Immense").Split(',').Select(Enum.Parse<MapSize>).ToList();
        var counts = (Env("PLAYERS") ?? "2,3").Split(',').Select(int.Parse).ToList();
        var runs = (from size in sizes
                    from players in counts
                    from seed in Seeds.Take(seeds)
                    from fresh in Enumerable.Range(0, freshMask == oldMask ? 1 : players)
                    select (Size: size, Players: players, Seed: seed, Fresh: fresh)).ToList();
        var results = new (bool Fresh, bool Old, double Time, Personality Personality)[runs.Count];
        Parallel.For(0, runs.Count, new ParallelOptions { MaxDegreeOfParallelism = threads }, i =>
        {
            var (size, players, seed, fresh) = runs[i];
            results[i] = Game(size, players, seed, fresh, freshMask, oldMask, level);
        });
        output.WriteLine($"NEW={freshMask} OLD={oldMask} LEVEL={level}");
        foreach (var group in Enumerable.Range(0, runs.Count).GroupBy(i => (runs[i].Size, runs[i].Players)))
        {
            var list = group.ToList();
            var fresh = list.Count(i => results[i].Fresh && !results[i].Old);
            var old = list.Count(i => results[i].Old && !results[i].Fresh);
            var shared = list.Count(i => results[i].Old && results[i].Fresh);
            var times = list.Where(i => results[i].Fresh || results[i].Old).Select(i => results[i].Time / 60).OrderBy(x => x).ToList();
            output.WriteLine($"{group.Key.Size} à {group.Key.Players} : nouvelle {fresh}, ancienne {old}, partagées {shared}, nuls {list.Count - fresh - old - shared} sur {list.Count}"
                + (times.Count > 0 ? $", durée médiane {Js.Round(times[times.Count / 2])} min ({Js.Round(times[0])} à {Js.Round(times[^1])})" : ""));
            foreach (var i in list)
            {
                var r = results[i];
                output.WriteLine($"  graine {runs[i].Seed} place {runs[i].Fresh} {r.Personality} : {(r.Fresh && r.Old ? "partagée" : r.Fresh ? "nouvelle" : r.Old ? "ancienne" : "nul")} en {Js.Round(r.Time / 60)} min");
            }
        }
        var all = Enumerable.Range(0, runs.Count).ToList();
        output.WriteLine($"TOTAL : nouvelle {all.Count(i => results[i].Fresh && !results[i].Old)}, ancienne {all.Count(i => results[i].Old && !results[i].Fresh)}, "
            + $"partagées {all.Count(i => results[i].Old && results[i].Fresh)}, nuls {all.Count(i => !results[i].Old && !results[i].Fresh)} sur {all.Count}");
        foreach (var p in Personalities.Drawn)
        {
            var mine = all.Where(i => results[i].Personality == p).ToList();
            if (mine.Count > 0) output.WriteLine($"  {p} : nouvelle {mine.Count(i => results[i].Fresh && !results[i].Old)} sur {mine.Count}");
        }
        // Most maps favour a start whoever takes it: only the maps one AI wins from every start tell them apart.
        var maps = all.GroupBy(i => (runs[i].Size, runs[i].Players, runs[i].Seed)).ToList();
        output.WriteLine($"cartes gagnées de toutes les places : nouvelle {maps.Count(m => m.All(i => results[i].Fresh && !results[i].Old))}, "
            + $"ancienne {maps.Count(m => m.All(i => results[i].Old && !results[i].Fresh))}, sur {maps.Count}");
    }
}
