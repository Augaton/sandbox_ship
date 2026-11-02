// A game's password (not in the web version), never sent as it is: the host gives each player
// that connects a challenge of its own (Message.Challenge), and the player answers in its Hello
// with a key made of the password and that challenge. Seen on the wire, a key opens nothing but
// that one connection, already let in; the password itself is only ever typed, kept in memory
// while the game lasts, and never written in the settings.
using System.Security.Cryptography;
using System.Text;

namespace SandboxShip.Sim.Net;

public static class Passwords
{
    /// <summary>Bytes of a challenge.</summary>
    public const int ChallengeSize = 16;
    /// <summary>The longest password, in letters.</summary>
    public const int MaxPassword = 64;

    /// <summary>A new challenge, for one connection.</summary>
    public static byte[] Challenge() => RandomNumberGenerator.GetBytes(ChallengeSize);

    /// <summary>The key a player answers a challenge with: HMAC-SHA256 of the challenge under the password, in hex; empty without a password.</summary>
    public static string Key(string? password, byte[] challenge) =>
        string.IsNullOrEmpty(password) ? "" : Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(password), challenge));

    /// <summary>Whether a player's key answers the challenge it was given for this password; compared in constant time.</summary>
    public static bool Opens(string password, byte[] challenge, string key)
    {
        var expected = Encoding.ASCII.GetBytes(Key(password, challenge));
        var given = Encoding.ASCII.GetBytes(key);
        return given.Length == expected.Length && CryptographicOperations.FixedTimeEquals(expected, given);
    }

    /// <summary>A password as typed, or null when there is none: spaces at its ends trimmed, at most MaxPassword letters.</summary>
    public static string? Clean(string? password)
    {
        var p = password?.Trim() ?? "";
        if (p.Length > MaxPassword) p = p[..MaxPassword];
        return p.Length > 0 ? p : null;
    }
}
