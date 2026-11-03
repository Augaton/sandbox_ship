// The best survival games played on this machine (Sim's SurvivalRecords), kept in user://:
// read whenever they are shown, written when a game makes the list. A capture writes nothing, and
// a file that cannot be read counts as none.
using Godot;
using SandboxShip.Sim;

namespace SandboxShip.Core;

public static class SurvivalScores
{
    static string FilePath => ProjectSettings.GlobalizePath("user://survival-records.json");

    public static List<SurvivalRecord> Load()
    {
        try
        {
            return File.Exists(FilePath) ? SurvivalRecords.Parse(File.ReadAllText(FilePath)) : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Adds a game that just ended: its rank among the games of its number of seats (0, the best;
    /// -1, off the list), and the best of them. Saved whole, through a file set aside first.
    /// </summary>
    public static (int Rank, List<SurvivalRecord> Best) Add(SurvivalRecord game)
    {
        var all = Load();
        var rank = SurvivalRecords.Insert(all, game);
        if (rank >= 0 && Dev.Shot is null) Save(all);
        return (rank, SurvivalRecords.Best(all, game.Seats));
    }

    static void Save(List<SurvivalRecord> records)
    {
        try
        {
            var path = FilePath;
            File.WriteAllText(path + ".tmp", SurvivalRecords.Serialize(records));
            File.Move(path + ".tmp", path, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"Records de survie non enregistrés : {e.Message}");
        }
    }
}
