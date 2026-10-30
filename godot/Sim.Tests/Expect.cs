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
