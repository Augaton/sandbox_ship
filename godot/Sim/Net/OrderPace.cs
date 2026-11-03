// How fast a player's orders are taken by a host (not in the web version): far faster than hands
// give them, but a flood (thousands of moves a second, each over a whole fleet) is left out
// rather than taking the host's processor. Hand flying, sent as the mouse moves, is not counted:
// it only sets a few numbers.
namespace SandboxShip.Sim.Net;

public sealed class OrderPace
{
    /// <summary>Orders a second, and at once after a pause.</summary>
    public const double PerSecond = 20, AtOnce = 40;
    /// <summary>Orders, hand flying included, waiting for the next step at most.</summary>
    public const int MostPending = 256;
    /// <summary>Seconds between two notices to a player whose orders are left out.</summary>
    const double NoticeEvery = 2;
    /// <summary>What the player is told, at most every NoticeEvery seconds.</summary>
    public const string TooMany = "Trop d'ordres d'un coup : les derniers sont ignorés.";

    double allowance = AtOnce, notice;
    bool dropped;

    /// <summary>Whether this order is taken; `pending`: the player's orders already waiting.</summary>
    public bool Take(bool free, int pending)
    {
        if (pending >= MostPending || !free && allowance < 1)
        {
            dropped = true;
            return false;
        }
        if (!free) allowance -= 1;
        return true;
    }

    /// <summary>Time goes by: the allowance fills again. True when the player should hear that orders were left out.</summary>
    public bool Refill(double dt)
    {
        allowance = Math.Min(AtOnce, allowance + PerSecond * dt);
        notice -= dt;
        if (!dropped || notice > 0) return false;
        (dropped, notice) = (false, NoticeEvery);
        return true;
    }
}
