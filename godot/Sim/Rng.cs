namespace SandboxShip.Sim;

/// <summary>
/// The web version's pseudo-random generators, bit for bit: the same seed gives the same
/// maps and the same battles in both versions.
/// </summary>
public static class Rng
{
    /// <summary>mulberry32: battles and generated maps. Advances <paramref name="state"/>.</summary>
    public static double Mulberry32(ref int state)
    {
        unchecked
        {
            state += 0x6D2B79F5;
            uint t = (uint)state;
            t = (t ^ (t >> 15)) * (t | 1);
            t ^= t + (t ^ (t >> 7)) * (t | 61);
            return (t ^ (t >> 14)) / 4294967296.0;
        }
    }

    /// <summary>Park-Miller: the galaxy's own draws. Advances <paramref name="state"/>.</summary>
    public static double ParkMiller(ref long state)
    {
        state = state * 16807 % 2147483647;
        return (state - 1) / 2147483646.0;
    }
}

/// <summary>JavaScript's arithmetic where C# differs.</summary>
public static class Js
{
    /// <summary>Math.round: halves go up (C#'s Math.Round goes to the even neighbour).</summary>
    public static double Round(double x) => Math.Floor(x + 0.5);
}
