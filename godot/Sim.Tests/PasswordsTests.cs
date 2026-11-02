// A game's password (not in the web version): a key answers one challenge only, never shows the
// password, and a locked game says so in the local network's beacons and in the online list,
// in a way the online server of an older build carries along unread.
using SandboxShip.Sim.Net;
using Xunit;

namespace SandboxShip.Sim.Tests;

public class PasswordsTests
{
    [Fact]
    public void A_key_opens_with_its_password_and_its_challenge_only()
    {
        var challenge = Passwords.Challenge();
        Assert.Equal(Passwords.ChallengeSize, challenge.Length);
        Assert.NotEqual(challenge, Passwords.Challenge());
        var key = Passwords.Key("étoile filante", challenge);
        Assert.True(Passwords.Opens("étoile filante", challenge, key));
        Assert.False(Passwords.Opens("Étoile filante", challenge, key));
        // Seen on the wire, a key opens nothing but the connection it answered.
        Assert.False(Passwords.Opens("étoile filante", Passwords.Challenge(), key));
        Assert.False(Passwords.Opens("étoile filante", challenge, ""));
        Assert.False(Passwords.Opens("étoile filante", challenge, key[..^1]));
        Assert.DoesNotContain("étoile", key);
        Assert.Equal("", Passwords.Key(null, challenge));
        Assert.Equal("", Passwords.Key("", challenge));
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
    public void The_challenge_and_the_answer_read_back()
    {
        var challenge = Passwords.Challenge();
        var c = (Message.Challenge)Protocol.Decode(Protocol.Encode(new Message.Challenge(challenge)));
        Assert.Equal(challenge, c.Nonce);
        var hello = Protocol.Hello("0.5.0", "Bob", Passwords.Key("secret", challenge), true);
        var back = (Message.Hello)Protocol.Decode(Protocol.Encode(hello));
        Assert.Equal(hello, back);
        Assert.True(back.Watches);
        Assert.True(Passwords.Opens("secret", challenge, back.Key));
        // A challenge of no bytes, or of a great many, does not read.
        Assert.Throws<InvalidDataException>(() => Protocol.Decode([25, 0]));
        Assert.Throws<InvalidDataException>(() => Protocol.Decode([25, 200, 1]));
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
