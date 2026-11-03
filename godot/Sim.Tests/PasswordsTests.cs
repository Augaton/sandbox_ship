// A game's password (not in the web version): its key depends on the game's salt, never shows
// the password, and is the same for an "é" typed one way or the other; a locked game says so in
// the local network's beacons and in the online list, in a way the online server of an older
// build carries along unread. The handshake that uses it: SecureTests.
using System.Text;
using SandboxShip.Sim.Net;
using Xunit;

namespace SandboxShip.Sim.Tests;

public class PasswordsTests
{
    [Fact]
    public void A_password_key_depends_on_its_salt_and_its_letters_not_on_how_they_were_typed()
    {
        var salt = new byte[GameLock.SaltSize];
        var key = Passwords.Key("étoile filante", salt);
        Assert.Equal(32, key.Length);
        Assert.Equal(key, Passwords.Key("étoile filante", salt));
        // "é" as one letter, and as "e" with its accent after it.
        Assert.Equal(key, Passwords.Key("étoile filante", salt));
        Assert.NotEqual(key, Passwords.Key("Étoile filante", salt));
        Assert.NotEqual(key, Passwords.Key("étoile filante", [1, .. salt[1..]]));
        Assert.DoesNotContain("étoile", Encoding.UTF8.GetString(key));
    }

    [Fact]
    public void A_password_is_trimmed_bounded_or_none()
    {
        Assert.Null(Passwords.Clean(null));
        Assert.Null(Passwords.Clean("   "));
        Assert.Equal("mot de passe", Passwords.Clean("  mot de passe "));
        Assert.Equal(Passwords.MaxPassword, Passwords.Clean(new string('a', 200))!.Length);
    }

    [Fact]
    public void A_locked_game_says_so_on_the_local_network_and_on_the_online_list()
    {
        var game = new LanGame(Protocol.Version, "0.5.0", "Partie de Bob", LobbyMode.Regicide, 1, 4, 24690, false, true);
        Assert.Equal(game, Protocol.DecodeLanGame(Protocol.Encode(game)));
        var open = game with { Locked = false };
        Assert.Equal(open, Protocol.DecodeLanGame(Protocol.Encode(open)));
        var list = new RelayFrame.Games([new OnlineGame(3, true, game, false, "", 25), new OnlineGame(4, true, open, true, "", 40)]);
        var read = (RelayFrame.Games)Relay.Decode(Relay.Encode(list));
        Assert.Equal(game, read.Listed[0].Game);
        Assert.Equal(open, read.Listed[1].Game);
        // The lock rides in the mode's byte: a server of an older build, which lists any mode as it
        // came, hands it on as it is.
        var opened = Relay.Encode(new RelayFrame.Open(Relay.Wire, game, false, ""));
        Assert.Equal(Relay.Encode(new RelayFrame.Open(Relay.Wire, open, false, "")).Length, opened.Length);
    }
}
