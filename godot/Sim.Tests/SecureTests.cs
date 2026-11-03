// The game's messages sealed end to end (not in the web version): a host and its player agree
// keys of their own, the password in them; a packet changed on its way or played again is left
// out; a wrong password, a player without one, or one in the middle who does not know it gets
// nowhere. What it does not hold against is shown too: an open game, with one in the middle.
using System.Text;
using SandboxShip.Sim.Net;
using Xunit;

namespace SandboxShip.Sim.Tests;

public class SecureTests
{
    const string Secret = "comète bleue";
    /// <summary>Made once: each costs Passwords.Rounds of PBKDF2.</summary>
    static readonly GameLock Open = GameLock.For(null), Locked = GameLock.For(Secret);

    /// <summary>A handshake carried as it is: both ends, and the refusal one of them heard, if any.</summary>
    static (SecureChannel Host, SecureChannel Player, string? Refused) Shake(GameLock game, string? password)
    {
        var host = SecureChannel.Host(game, out var offer);
        var player = SecureChannel.Player(password);
        var answer = player.Take(offer);
        if (answer.Refused is { } early) return (host, player, early);
        var accept = host.Take(answer.Reply!);
        var last = player.Take(accept.Reply!);
        return (host, player, last.Refused ?? accept.Refused);
    }

    static byte[] Text(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void A_host_and_its_player_agree_keys_and_their_messages_go_both_ways()
    {
        foreach (var (game, password) in new[] { (Open, (string?)null), (Locked, Secret) })
        {
            var (host, player, refused) = Shake(game, password);
            Assert.Null(refused);
            Assert.True(host.Ready && player.Ready);
            foreach (var reliable in new[] { true, false })
            {
                Assert.Equal(Text("Bonjour"), player.Open(host.Seal(Text("Bonjour"), reliable)));
                Assert.Equal(Text("Salut"), host.Open(player.Seal(Text("Salut"), reliable)));
            }
            // A snapshot-like message goes compressed, and comes back whole; random bytes go as they are.
            var large = Enumerable.Range(0, 20_000).Select(i => (byte)(i % 7 * 31 + i / 500)).ToArray();
            var packed = host.Seal(large, false);
            Assert.True(packed.Length < large.Length / 2, $"{packed.Length} octets pour {large.Length}");
            Assert.Equal(large, player.Open(packed));
            var noise = new byte[5000];
            new Random(3).NextBytes(noise);
            Assert.Equal(noise.Length + SecureChannel.Overhead, host.Seal(noise, true).Length);
            Assert.Equal(0, host.Rejected + player.Rejected);
        }
    }

    [Fact]
    public void A_packet_changed_on_its_way_is_left_out_and_the_channel_goes_on()
    {
        var (host, player, _) = Shake(Open, null);
        var packet = host.Seal(Text("Ordre d'attaque"), true);
        // Every byte in turn: the kind, the counter, the message, the tag.
        for (var i = 0; i < packet.Length; i++)
        {
            var changed = (byte[])packet.Clone();
            changed[i] ^= 0x20;
            Assert.Null(player.Open(changed));
        }
        Assert.Null(player.Open(packet[..^1]));
        Assert.Null(player.Open([.. packet, 0]));
        Assert.Equal(packet.Length + 2, player.Rejected);
        // None of them moved the window: the true packet still opens.
        Assert.Equal(Text("Ordre d'attaque"), player.Open(packet));
    }

    [Fact]
    public void A_packet_played_again_or_too_old_is_left_out_one_late_but_new_is_taken()
    {
        var (host, player, _) = Shake(Open, null);
        var first = host.Seal(Text("1"), false);
        var packets = Enumerable.Range(2, 70).Select(i => host.Seal(Text($"{i}"), false)).ToList();
        Assert.NotNull(player.Open(packets[^1]));
        Assert.Null(player.Open(packets[^1]));
        // Late, within 64 of the newest: taken once. Further behind: left out.
        Assert.Equal(Text("40"), player.Open(packets[38]));
        Assert.Null(player.Open(packets[38]));
        Assert.Null(player.Open(first));
        // Each channel counts apart: a reliable message after them all is not "played again".
        Assert.Equal(Text("fiable"), player.Open(host.Seal(Text("fiable"), true)));
        // Nor does a packet go back to its sender, nor from one channel to another.
        var mine = player.Seal(Text("à l'hôte"), true);
        Assert.Null(player.Open(mine));
        var reliable = host.Seal(Text("x"), true);
        reliable[0] ^= 1;
        Assert.Null(player.Open(reliable));
    }

    [Fact]
    public void No_two_messages_share_a_nonce()
    {
        var (host, player, _) = Shake(Open, null);
        var seen = new HashSet<string>();
        foreach (var end in new[] { host, player })
        {
            for (var i = 0; i < 500; i++)
            {
                var p = end.Seal(Text("même message"), i % 2 == 0);
                // The nonce is who seals, the channel (in the kind) and the counter.
                Assert.True(seen.Add($"{end == host}:{p[0] & 1}:{Convert.ToHexString(p, 1, 8)}"));
            }
        }
        // Two seals of one message never give the same bytes.
        Assert.NotEqual(host.Seal(Text("a"), true), host.Seal(Text("a"), true));
    }

    [Fact]
    public void A_wrong_password_or_none_is_refused_and_so_is_a_password_for_an_open_game()
    {
        var (host, player, refused) = Shake(Locked, "comète rouge");
        Assert.Equal("Mot de passe incorrect.", refused);
        Assert.False(host.Ready || player.Ready);
        Assert.StartsWith("Cette partie est protégée", Shake(Locked, null).Refused);
        // A player with a password takes only a game locked by it: a host that says it has none could be anyone.
        Assert.StartsWith("Cette partie ne demande pas de mot de passe", Shake(Open, Secret).Refused);
        // Nothing is ever sealed before the channel is ready.
        Assert.Throws<InvalidOperationException>(() => host.Seal(Text("x"), true));
    }

    [Fact]
    public void A_relay_in_the_middle_of_a_locked_game_can_neither_read_nor_pass_for_the_host()
    {
        // In the host's place, as an open game: the player, who has the password, refuses at once.
        SecureChannel.Host(Open, out var openOffer);
        Assert.StartsWith("Cette partie ne demande pas de mot de passe", SecureChannel.Player(Secret).Take(openOffer).Refused);

        // As a locked game, of a password it guessed: the player answers, then refuses the proofs the relay can give.
        var guessed = SecureChannel.Host(GameLock.For("1234"), out var fakeOffer);
        var player = SecureChannel.Player(Secret);
        var answer = player.Take(fakeOffer).Reply!;
        // The player's proof does not open the guess, so the relay's own host end has nothing to accept with.
        Assert.Equal("Mot de passe incorrect.", guessed.Take(answer).Refused);
        Assert.StartsWith("L'hôte n'a pas prouvé", player.Take([0xE3, .. new byte[32]]).Refused);
        Assert.False(player.Ready);
        Assert.Throws<InvalidOperationException>(() => player.Seal(Text("Hello"), true));

        // Toward the true host, as the player: its own key put in the player's answer, the player's proof kept.
        var host = SecureChannel.Host(Locked, out var trueOffer);
        var trueAnswer = SecureChannel.Player(Secret).Take(trueOffer).Reply!;
        var relayKey = SecureChannel.Player(null).Take(openOffer).Reply!;
        byte[] spliced = [trueAnswer[0], .. relayKey[1..66], .. trueAnswer[66..]];
        Assert.Equal("Mot de passe incorrect.", host.Take(spliced).Refused);
        Assert.False(host.Ready);
    }

    [Fact]
    public void A_relay_that_only_carries_the_packets_sees_nothing_it_can_read()
    {
        var host = SecureChannel.Host(Locked, out var offer);
        var player = SecureChannel.Player(Secret);
        var carried = new List<byte[]> { offer };
        var answer = player.Take(offer).Reply!;
        carried.Add(answer);
        var accept = host.Take(answer).Reply!;
        carried.Add(accept);
        player.Take(accept);
        var words = Text("Rendez-vous au nord");
        carried.Add(host.Seal(words, true));
        carried.Add(player.Seal(words, false));
        foreach (var bytes in carried)
        {
            Assert.True(bytes.AsSpan().IndexOf(words) < 0);
            Assert.True(bytes.AsSpan().IndexOf(Text(Secret)) < 0);
        }
        // Played again to the host, the player's answer opens nothing more: the host's channel is ready, the answer is no message.
        Assert.Equal(default, host.Take(answer));
        Assert.Null(host.Open(answer));
    }

    [Fact]
    public void An_open_game_does_not_hold_against_one_in_the_middle()
    {
        // The limit, shown: without a password, nothing tells the player its host from one that answers in its place.
        var (middleAsHost, player, refused) = Shake(Open, null);
        Assert.Null(refused);
        Assert.Equal(Text("lu au passage"), middleAsHost.Open(player.Seal(Text("lu au passage"), true)));
    }

    [Fact]
    public void A_key_that_is_no_point_of_the_curve_is_refused_cleanly()
    {
        SecureChannel.Host(Open, out var offer);
        var bad = (byte[])offer.Clone();
        bad[^1] ^= 1;
        Assert.NotNull(SecureChannel.Player(null).Take(bad).Refused);
        var host = SecureChannel.Host(Open, out offer);
        var answer = SecureChannel.Player(null).Take(offer).Reply!;
        answer[40] ^= 1;
        Assert.Equal("Échange illisible.", host.Take(answer).Refused);
    }

    [Fact]
    public void A_host_knows_a_player_once_sealed_and_lets_go_one_that_takes_too_long_or_was_refused()
    {
        var host = SecureLinks.Hosting(Locked);
        var player = SecureLinks.Joining(Secret);
        var offer = host.Connected(2, 0)!;
        Assert.Null(player.Connected(Transport_HostPeer, 0));
        Assert.Null(host.Seal(2, Text("trop tôt"), true));
        var answer = player.Received(Transport_HostPeer, offer, 0.1);
        Assert.False(answer.Came);
        var accept = host.Received(2, answer.Reply!, 0.2);
        Assert.True(accept.Came && host.Ready(2));
        Assert.True(player.Received(Transport_HostPeer, accept.Reply!, 0.3).Came);
        Assert.Equal(Text("Hello"), host.Received(2, player.Seal(Transport_HostPeer, Text("Hello"), true)!, 0.4).Message);

        // A peer that never answers goes after Wait; one refused goes a moment later, unknown to the session.
        host.Connected(3, 1);
        var wrong = SecureLinks.Joining("faux");
        wrong.Connected(Transport_HostPeer, 1);
        var refused = host.Received(4, wrong.Received(Transport_HostPeer, host.Connected(4, 1)!, 1).Reply!, 1);
        Assert.Equal("Mot de passe incorrect.", refused.Refused);
        Assert.NotNull(refused.Reply);
        Assert.Empty(host.Overdue(1.2));
        Assert.Equal([4], host.Overdue(1 + SecureLinks.Grace + 0.1));
        Assert.Equal([3], host.Overdue(1 + SecureLinks.Wait + 0.1));
        Assert.False(host.Gone(3));
        Assert.True(host.Gone(2));
    }

    /// <summary>As Game/scripts/Net/Transport.HostPeer: how a player knows its host.</summary>
    const int Transport_HostPeer = 1;
}
