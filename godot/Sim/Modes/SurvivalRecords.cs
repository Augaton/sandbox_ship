// The best survival games played on this machine: the ten best scores for each number of seats,
// kept as JSON by the game (user://). Reading takes whatever a file holds, a damaged or forged one
// included: what cannot be a record is left out, and nothing in it can make the list grow past
// its bounds.
using System.Text.Json;

namespace SandboxShip.Sim;

/// <summary>
/// A survival game as kept among the best: the team's score, the wave it reached and how long it
/// held, its number of seats, when it ended (Unix ms), and who played (the races, as shown).
/// </summary>
public sealed record SurvivalRecord(int Score, int Wave, double Held, int Seats, long At, string Sides);

public static class SurvivalRecords
{
    /// <summary>Records kept for each number of seats.</summary>
    public const int Kept = 10;
    /// <summary>A file larger than this is no list of records: it is not read.</summary>
    const int MaxBytes = 256 * 1024;
    /// <summary>The players' line of a record, at most this long.</summary>
    const int MaxSides = 120;

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Better first: the higher score, then the further wave, then the earlier game.</summary>
    static int Order(SurvivalRecord a, SurvivalRecord b) =>
        b.Score != a.Score ? b.Score.CompareTo(a.Score) : b.Wave != a.Wave ? b.Wave.CompareTo(a.Wave) : a.At.CompareTo(b.At);

    static bool Valid(SurvivalRecord? r) =>
        r is not null && r.Score >= 0 && r.Wave >= 0 && r.Seats is >= 1 and <= Lobby.MaxSeats
        && double.IsFinite(r.Held) && r.Held >= 0 && r.At >= 0 && r.Sides is not null;

    /// <summary>The records of a file's text: none from an empty, damaged or foreign one; the valid ones of a list, the best Kept of each number of seats.</summary>
    public static List<SurvivalRecord> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxBytes) return [];
        List<SurvivalRecord?>? read;
        try
        {
            read = JsonSerializer.Deserialize<List<SurvivalRecord?>>(text, Json);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException)
        {
            return [];
        }
        var records = new List<SurvivalRecord>();
        foreach (var r in read ?? [])
        {
            if (Valid(r)) records.Add(r! with { Sides = r!.Sides.Length > MaxSides ? r.Sides[..MaxSides] : r.Sides });
        }
        return Trim(records);
    }

    public static string Serialize(IEnumerable<SurvivalRecord> records) => JsonSerializer.Serialize(Trim([.. records]), Json);

    /// <summary>The best Kept of each number of seats, best first within each.</summary>
    static List<SurvivalRecord> Trim(List<SurvivalRecord> records) =>
        [.. records.GroupBy(r => r.Seats).OrderBy(g => g.Key).SelectMany(g => g.Order(Comparer<SurvivalRecord>.Create(Order)).Take(Kept))];

    /// <summary>The records of games with that many seats, best first.</summary>
    public static List<SurvivalRecord> Best(IEnumerable<SurvivalRecord> records, int seats) =>
        [.. records.Where(r => r.Seats == seats).Order(Comparer<SurvivalRecord>.Create(Order)).Take(Kept)];

    /// <summary>
    /// Adds a game to the list, which keeps the best Kept of each number of seats: its rank among
    /// those of its number of seats (0, the best), or -1 when it does not make them.
    /// </summary>
    public static int Insert(List<SurvivalRecord> records, SurvivalRecord game)
    {
        if (!Valid(game)) return -1;
        records.Add(game);
        var kept = Trim(records);
        records.Clear();
        records.AddRange(kept);
        return Best(records, game.Seats).IndexOf(game);
    }
}
