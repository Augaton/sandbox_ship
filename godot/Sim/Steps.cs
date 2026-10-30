// The simulation step, in the web version's order. Port of src/sim/step.ts.
namespace SandboxShip.Sim;

public static class Steps
{
    /// <summary>Fixed simulation time step, in seconds.</summary>
    public const double Step = 1.0 / 60;

    public static void StepWorld(World world, double dt = Step)
    {
        world.Time += dt;
        world.Grid.Rebuild(world.Entities);
        Ai.UpdateAI(world, dt);
        Structures.UpdateEconomy(world, dt);
        Structures.UpdateProduction(world, dt);
        Structures.UpdateResearch(world, dt);
        Structures.UpdateConstruction(world, dt);
        Structures.UpdateRebuild(world, dt);
        Structures.UpdateHangars(world, dt);
        Vision.UpdateVision(world, dt);
        Combat.UpdateTargeting(world, dt);
        AbilitySystem.UpdateAbilities(world, dt);
        Movement.UpdateShips(world, dt);
        Movement.Separate(world);
        Worlds.AttachHardpoints(world);
        Combat.UpdateWeapons(world, dt);
        Combat.UpdateProjectiles(world, dt);
        Combat.UpdateRepair(world, dt);
        Combat.UpdateShields(world, dt);
        Worlds.RemoveDead(world);
        Worlds.CheckVictory(world);
    }

    /// <summary>Runs the simulation for `seconds` of game time.</summary>
    public static void Run(World world, double seconds, double dt = Step)
    {
        var steps = Js.Round(seconds / dt);
        for (var i = 0; i < steps; i++) StepWorld(world, dt);
    }
}
