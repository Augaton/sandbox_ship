// Vitest's comparisons that xUnit lacks, with the values in the failure message.
using Xunit;

namespace SandboxShip.Sim.Tests;

static class Expect
{
    public static void Greater(double actual, double than) => Assert.True(actual > than, $"Expected {actual} > {than}");
    public static void GreaterOrEqual(double actual, double than) => Assert.True(actual >= than, $"Expected {actual} >= {than}");
    public static void Less(double actual, double than) => Assert.True(actual < than, $"Expected {actual} < {than}");

    /// <summary>toBeCloseTo: within half a unit of the last of `digits` decimals.</summary>
    public static void Close(double expected, double actual, int digits = 2) =>
        Assert.True(Math.Abs(expected - actual) < Math.Pow(10, -digits) / 2, $"Expected {actual} to be close to {expected} ({digits} digits)");
}

/// <summary>Ships built go to the reserve: tests that want them on the battlefield call them in.</summary>
static class Landing
{
    /// <summary>Calls the side's whole reserve in round its rally point and runs until the last of it is out of hyperspace.</summary>
    public static void Land(World w, Side side)
    {
        Reserves.CallAll(w, side);
        for (var t = 0.0; t < Reserves.CapitalJumpTime + 1 && w.Jumps.Exists(j => j.Side == side); t += 1.0 / 60) Steps.StepWorld(w);
    }
}
