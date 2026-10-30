// AI-only campaigns at every galaxy size, with two and three factions: how many reach a winner
// within the hour, and how long they take. A measure, not a check, and a long one: it only
// runs when asked,
//   CAMPAIGN=1 dotnet test Sim.Tests --filter CampaignBalance --logger "console;verbosity=detailed"
using SandboxShip.Sim.Data;
using Xunit;
using Xunit.Abstractions;
using static SandboxShip.Sim.Galaxies;

namespace SandboxShip.Sim.Tests;

public class CampaignBalanceTests(ITestOutputHelper output)
{
    const int Seeds = 6;
    const double Limit = 60 * 60;

    [Fact]
    public void Measures_AI_only_campaigns()
    {
        if (Environment.GetEnvironmentVariable("CAMPAIGN") is null) return;
        var runs = (from size in new[] { MapSize.Small, MapSize.Medium, MapSize.Large }
                    from players in new[] { 2, 3 }
                    from seed in Enumerable.Range(1, Seeds)
                    select (Size: size, Players: players, Seed: seed * 7 + 1)).ToList();
        var results = new (Faction? Winner, double Time, int Truces, int Incidents)[runs.Count];
        Parallel.For(0, runs.Count, i =>
        {
            var (size, players, seed) = runs[i];
            Faction[] enemies = players == 2 ? [Faction.Nairan] : [Faction.Nairan, Faction.Nautolan];
            var g = CreateGalaxy(Faction.Klaed, enemies, true, seed, GalaxyGen.GenerateLayout(seed, players, size));
            g.Player = (Faction)99;
            g.Factions[Faction.Klaed].AI = new GalaxyAIState { Difficulty = Difficulty.Normal };
            var truces = 0;
            var incidents = 0;
            while (g.Winner is null && g.Time < Limit)
            {
                RunGalaxy(g, 10);
                truces += g.Events.Count(e => e is TruceSigned);
                incidents += g.Events.Count(e => e is IncidentStarted);
                g.Events.Clear();
            }
            results[i] = (g.Winner, g.Time, truces, incidents);
        });
        var lengths = new List<double>();
        for (var i = 0; i < runs.Count; i++)
        {
            var (size, players, seed) = runs[i];
            var (winner, time, truces, incidents) = results[i];
            output.WriteLine($"{size} à {players}, graine {seed}: {winner?.ToString() ?? "aucun vainqueur"} en {Js.Round(time / 60)} min, "
                + $"{truces} trêve(s), {incidents} événement(s)");
            if (winner is not null) lengths.Add(time);
        }
        lengths.Sort();
        output.WriteLine($"--- terminées : {lengths.Count} / {runs.Count} ---");
        if (lengths.Count > 0)
            output.WriteLine($"durée : médiane {Js.Round(lengths[lengths.Count / 2] / 60)} min, de {Js.Round(lengths[0] / 60)} à {Js.Round(lengths[^1] / 60)} min");
    }
}
