// The battle modes played out between AIs on normal difficulty, every pair of races, a few
// seeds each: who wins, how, and how long it takes. A measure, not a check, and a long one: it
// only runs when asked,
//   BALANCE=1 dotnet test Sim.Tests --filter ModeBalance --logger "console;verbosity=detailed"
// TERRAIN=light or dense: on that battle terrain (BalanceTests.Terrain). SEEDS=N: N seeds per
// pair of races instead of 4, best for one series alone, e.g. the assault on 108 games:
//   BALANCE=1 SEEDS=18 dotnet test Sim.Tests --filter Measures_assault --logger "console;verbosity=detailed"
using SandboxShip.Sim.Data;
using Xunit;
using Xunit.Abstractions;

namespace SandboxShip.Sim.Tests;

public class ModeBalanceTests(ITestOutputHelper output)
{
    static readonly int Seeds = int.TryParse(Environment.GetEnvironmentVariable("SEEDS"), out var n) && n > 0 ? n : 4;
    const double Limit = 60 * 60;
    const double Dt = 1.0 / 30;

    /// <summary>Plays a skirmish out between AIs; returns the world at its end, or at the time limit.</summary>
    static World Play(SkirmishOptions o)
    {
        o.Terrain = BalanceTests.Terrain;
        var w = Scenario.CreateSkirmish(o);
        while (w.Winner is null && w.Time < Limit)
        {
            Steps.StepWorld(w, Dt);
            w.Events.Clear();
        }
        return w;
    }

    static Slot Seat(Side side, Faction race, int team = 0) => new(side, race, team, Difficulty.Normal);

    void Report(string mode, List<(string Game, World World, string Outcome)> games)
    {
        foreach (var (game, w, outcome) in games) output.WriteLine($"{mode} {game}: {outcome} en {Js.Round(w.Time / 60)} min");
        var lengths = games.Select(g => g.World.Time / 60).Order().ToList();
        output.WriteLine($"{mode}: médiane {Js.Round(lengths[lengths.Count / 2])} min, de {Js.Round(lengths[0])} à {Js.Round(lengths[^1])} min");
    }

    static List<(Faction A, Faction B, int Seed)> Pairs() =>
        [.. from a in Factions.All from b in Factions.All where a != b from s in Enumerable.Range(1, Seeds) select (a, b, s)];

    [Fact]
    public void Measures_survival()
    {
        if (Environment.GetEnvironmentVariable("BALANCE") is null) return;
        var games = Factions.All.SelectMany(a => Enumerable.Range(1, Seeds).SelectMany(s => new[]
        {
            ($"{a} seul, graine {s}", new SkirmishOptions { Mode = BattleMode.Survival, Slots = [Seat(a, a)], Seed = s * 101 }),
            ($"{a} et {(Faction)(((int)a + 1) % 3)}, graine {s}", new SkirmishOptions
            {
                Mode = BattleMode.Survival, Slots = [Seat(a, a), Seat((Faction)(((int)a + 1) % 3), (Faction)(((int)a + 1) % 3))], Seed = s * 101,
            }),
        })).AsParallel().AsOrdered().Select(g =>
        {
            var w = Play(g.Item2);
            var m = w.Mode;
            // Endless since this version: how far the team got, and its score.
            return (g.Item1, w, $"vague {m.Wave}, {m.Cleared} repoussées, score {Survival.TeamScore(w)}");
        }).ToList();
        Report("survie", games);
    }

    [Fact]
    public void Measures_zones()
    {
        if (Environment.GetEnvironmentVariable("BALANCE") is null) return;
        var games = Pairs().AsParallel().AsOrdered().Select(p =>
        {
            var w = Play(new SkirmishOptions { Mode = BattleMode.Zones, Slots = [Seat(p.A, p.A), Seat(p.B, p.B)], Seed = p.Seed * 101 });
            var scores = string.Join(" / ", w.Mode.Scores.Values.Select(v => Js.Round(v)));
            var how = w.Winner is Side s ? $"{Worlds.RaceOf(w, s)} {(ZoneControl.Score(w, s) >= w.Mode.ZoneTarget ? "aux points" : "par destruction")}" : "nul";
            return ($"{p.A} contre {p.B}, graine {p.Seed}", w, $"{how} ({scores})");
        }).ToList();
        Report("zones", games);
    }

    [Fact]
    public void Measures_assault()
    {
        if (Environment.GetEnvironmentVariable("BALANCE") is null) return;
        var games = Pairs().AsParallel().AsOrdered().Select(p =>
        {
            var w = Play(new SkirmishOptions { Mode = BattleMode.Assault, Slots = [Seat(p.A, p.A, 1), Seat(p.B, p.B, 2)], Seed = p.Seed * 101 });
            var how = w.Winner is Side s ? Worlds.TeamOf(w, s) == Assault.Defenders ? "défense" : "attaque" : "nul";
            // How close a hold was: what is left of the fortress's hull.
            var left = how == "défense" && Worlds.StationOf(w, p.A) is { } fortress ? $" (station à {Js.Round(100 * fortress.Hull / fortress.MaxHull)} %)" : "";
            return (Game: $"{p.A} défend contre {p.B}, graine {p.Seed}", World: w, Outcome: how + left, Defender: p.A, Attacker: p.B, Held: how == "défense");
        }).ToList();
        Report("assaut", [.. games.Select(g => (g.Game, g.World, g.Outcome))]);
        output.WriteLine($"défense {games.Count(g => g.Held)}, attaque {games.Count(g => g.World.Winner is not null && !g.Held)} sur {games.Count}");
        // Each race's wins in both roles, as in PLAN.md's table.
        foreach (var f in Factions.All)
        {
            int held = games.Count(g => g.Defender == f && g.Held), taken = games.Count(g => g.Attacker == f && g.World.Winner is not null && !g.Held);
            int asDefender = games.Count(g => g.Defender == f), asAttacker = games.Count(g => g.Attacker == f);
            output.WriteLine($"{f}: {Js.Round(100.0 * (held + taken) / (asDefender + asAttacker))} % (en défense {held} / {asDefender}, en attaque {taken} / {asAttacker})");
        }
    }

    [Fact]
    public void Measures_regicide()
    {
        if (Environment.GetEnvironmentVariable("BALANCE") is null) return;
        var games = Pairs().Where(p => p.Seed <= 2).Select(p => (Game: $"{p.A} contre {p.B}, graine {p.Seed}", Slots: new List<Slot> { Seat(p.A, p.A), Seat(p.B, p.B) }, p.Seed))
            .Concat(Enumerable.Range(1, 3).Select(s => (Game: $"trois, graine {s}", Slots: Factions.All.Select(f => Seat(f, f)).ToList(), Seed: s)))
            .AsParallel().AsOrdered().Select(g =>
            {
                var w = Play(new SkirmishOptions { Mode = BattleMode.Regicide, Slots = g.Slots, Seed = g.Seed * 101 });
                return (g.Game, w, w.Winner is Side s ? Worlds.RaceOf(w, s).ToString() : "nul");
            }).ToList();
        Report("régicide", games);
    }
}
