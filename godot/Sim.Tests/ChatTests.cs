// The chat of a game over the network (not in the web version): lines made clean and bounded,
// kept in a short log, and a pace each player cannot exceed.
using SandboxShip.Sim.Net;
using Xunit;

namespace SandboxShip.Sim.Tests;

public class ChatTests
{
    [Fact]
    public void A_line_is_plain_bounded_text_or_nothing()
    {
        Assert.Equal("bonjour à tous", Chat.Clean("  bonjour\tà\ntous  "));
        Assert.Equal("ab", Chat.Clean("a\u0000\u0007b"));
        Assert.Null(Chat.Clean(" \r\n\t "));
        Assert.Null(Chat.Clean(null));
        // Markup is text like any other: the screens show it as it is.
        Assert.Equal("[color=red]rouge[/color]", Chat.Clean("[color=red]rouge[/color]"));
        Assert.Equal(Chat.MaxText, Chat.Clean(new string('x', 500))!.Length);
        // A letter beyond the basic plane is never cut in two.
        var emoji = new string('x', Chat.MaxText - 1) + "\U0001F680";
        var cut = Chat.Clean(emoji)!;
        Assert.False(char.IsHighSurrogate(cut[^1]));
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(Chat.Clean(new string('世', 500))!) <= Chat.MaxBytes);
    }

    [Fact]
    public void A_player_writes_a_burst_then_at_a_steady_pace_and_others_are_not_held_back()
    {
        var gate = new ChatGate();
        for (var i = 0; i < ChatGate.Burst; i++) Assert.True(gate.Allow(2, 10));
        Assert.False(gate.Allow(2, 10));
        Assert.True(gate.Allow(3, 10));
        Assert.False(gate.Allow(2, 10 + ChatGate.Every * 0.9));
        Assert.True(gate.Allow(2, 10 + ChatGate.Every * 1.1));
        Assert.False(gate.Allow(2, 10 + ChatGate.Every * 1.2));
        // Quiet for long, a player has its whole burst back, no more.
        var later = 100.0;
        for (var i = 0; i < ChatGate.Burst; i++) Assert.True(gate.Allow(2, later));
        Assert.False(gate.Allow(2, later));
        gate.Forget(2);
        Assert.True(gate.Allow(2, later));
    }

    [Fact]
    public void The_log_keeps_the_newest_lines_and_counts_every_one()
    {
        var log = new ChatLog();
        for (var i = 0; i < ChatLog.Kept + 7; i++) log.Add(new ChatLine("Bob", $"ligne {i}", i));
        Assert.Equal(ChatLog.Kept, log.Lines.Count);
        Assert.Equal("ligne 7", log.Lines[0].Text);
        Assert.Equal($"ligne {ChatLog.Kept + 6}", log.Lines[^1].Text);
        Assert.Equal(ChatLog.Kept + 7, log.Version);
    }
}
