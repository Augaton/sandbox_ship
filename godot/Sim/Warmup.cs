// Runs the simulation's code once, out of sight, while the game loads: a skirmish with every
// AI at work, a showcase fight, a small campaign with battles resolving on their own. The
// first real battle and campaign then find that code compiled already, instead of stalling for
// tens of milliseconds the first time an AI makes each kind of decision.
using SandboxShip.Sim.Data;

namespace SandboxShip.Sim;

public static class Warmup
{
    public static void Run()
    {
        var skirmish = Scenario.CreateSkirmish(new SkirmishOptions
        {
            Player = Faction.Klaed, Enemies = [Faction.Nairan, Faction.Nautolan], EnemyAI = true, Credits = 50000, Seed = 3, Raid = true,
        });
        foreach (var f in skirmish.Factions.Values)
        {
            f.AI ??= Worlds.NewAI();
            f.AutoAbilities = true;
        }
        Steps.Run(skirmish, 30);
        Steps.Run(Showcase.Create(5), 20);
        var g = Galaxies.CreateGalaxy(Faction.Klaed, [Faction.Nairan, Faction.Nautolan], true, 5, GalaxyGen.GenerateLayout(5, 3, MapSize.Small));
        // Watched from off the map: every battle resolves on its own.
        g.Player = Faction.Errant;
        foreach (var f in g.Factions.Values) f.AI ??= new GalaxyAIState { Difficulty = Difficulty.Normal };
        Galaxies.RunGalaxy(g, 240);
    }
}
