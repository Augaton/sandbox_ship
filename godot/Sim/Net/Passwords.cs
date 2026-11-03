// A game's password (not in the web version), never sent, not even as a hash: it goes into the
// keys that seal the game's messages (SecureChannel), through a key made of it and the game's
// salt, slow to make on purpose. The password itself is only ever typed, kept in memory while
// the game lasts, and never written in the settings.
using System.Security.Cryptography;
using System.Text;

namespace SandboxShip.Sim.Net;

public static class Passwords
{
    /// <summary>The longest password, in letters.</summary>
    public const int MaxPassword = 64;
    /// <summary>
    /// PBKDF2-SHA256 rounds of a password's key, as OWASP advises (2023): a fraction of a second,
    /// once for a host's game, once for each connection of a player; as much for each password
    /// tried by one who would guess it from a connection it stood in the middle of.
    /// </summary>
    public const int Rounds = 600_000;

    /// <summary>
    /// The key of a password under a game's salt: PBKDF2-SHA256 of the password, its letters in
    /// one normal form (an "é" typed on one system as on another), 32 bytes.
    /// </summary>
    public static byte[] Key(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password.Normalize(NormalizationForm.FormC)), salt, Rounds, HashAlgorithmName.SHA256, 32);

    /// <summary>A password as typed, or null when there is none: spaces at its ends trimmed, at most MaxPassword letters.</summary>
    public static string? Clean(string? password)
    {
        var p = password?.Trim() ?? "";
        if (p.Length > MaxPassword) p = p[..MaxPassword];
        return p.Length > 0 ? p : null;
    }
}
