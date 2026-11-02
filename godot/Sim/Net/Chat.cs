// What players write to each other over the network (not in the web version): lines of plain
// text, bounded, under the name of who wrote them. A player sends its line to the host (Say);
// the host keeps the pace of each player within ChatGate, and relays every line to everyone,
// the one who wrote it included (Said), so that all see the same lines in the same order.
namespace SandboxShip.Sim.Net;

/// <summary>A line as the host relayed it: who wrote it (empty: the game itself), its text, and when it came, in the session's clock.</summary>
public sealed record ChatLine(string Who, string Text, double At);

public static class Chat
{
    /// <summary>The longest line, in letters.</summary>
    public const int MaxText = 160;
    /// <summary>The most a line weighs on the wire, in UTF-8 bytes: four for a letter at worst.</summary>
    public const int MaxBytes = MaxText * 4;

    /// <summary>
    /// A line as everyone sees it: no control characters (tabs and line breaks become spaces),
    /// spaces trimmed, at most MaxText letters; null when nothing is left. Whatever it holds
    /// is shown as text, never read as markup.
    /// </summary>
    public static string? Clean(string? text)
    {
        if (text is null) return null;
        var chars = text.Select(c => char.IsWhiteSpace(c) ? ' ' : c).Where(c => !char.IsControl(c)).ToArray();
        var clean = new string(chars).Trim();
        if (clean.Length > MaxText)
        {
            // Never cut a letter in two: a surrogate pair stays whole or goes.
            var end = char.IsHighSurrogate(clean[MaxText - 1]) ? MaxText - 1 : MaxText;
            clean = clean[..end].TrimEnd();
        }
        return clean.Length > 0 ? clean : null;
    }
}

/// <summary>The lines kept for the screens, newest last, at most Kept; Version changes with each new one.</summary>
public sealed class ChatLog
{
    public const int Kept = 50;

    readonly List<ChatLine> lines = [];

    public IReadOnlyList<ChatLine> Lines => lines;
    public int Version { get; private set; }

    public void Add(ChatLine line)
    {
        lines.Add(line);
        if (lines.Count > Kept) lines.RemoveAt(0);
        Version++;
    }
}

/// <summary>
/// How fast each player may write, on the host: Burst lines at once, then one more every Every
/// seconds. A line beyond it is not relayed; its writer is told to wait.
/// </summary>
public sealed class ChatGate
{
    public const int Burst = 4;
    public const double Every = 1.5;

    readonly Dictionary<int, (double Tokens, double At)> buckets = [];

    /// <summary>Whether `peer` may write a line at `now` (seconds); if so, the line is counted.</summary>
    public bool Allow(int peer, double now)
    {
        var (tokens, at) = buckets.TryGetValue(peer, out var b) ? b : (Burst, now);
        tokens = Math.Min(Burst, tokens + (now - at) / Every);
        if (tokens < 1)
        {
            buckets[peer] = (tokens, now);
            return false;
        }
        buckets[peer] = (tokens - 1, now);
        return true;
    }

    /// <summary>A player gone: its pace is forgotten.</summary>
    public void Forget(int peer) => buckets.Remove(peer);
}
