// The replays on disk (Sim/Replays.cs): one file a battle in user://replays, the newest kept. A
// capture writes none, as it writes no settings and no campaign.
using Godot;
using SandboxShip.Sim;
using SandboxShip.Sim.Data;

namespace SandboxShip.Core;

public static class ReplayStore
{
    /// <summary>Replays kept: past these, the oldest go.</summary>
    public const int Kept = 30;
    /// <summary>A battle left in its first half minute leaves no replay.</summary>
    public const long MinSteps = 30 * 60;
    const string Extension = ".ssreplay";

    /// <summary>A replay on disk: its header, or why it cannot be read.</summary>
    public sealed record Entry(string Path, Replay? Header, string? Problem);

    public static string Folder => Dev.Replays ?? ProjectSettings.GlobalizePath("user://replays");

    /// <summary>Saves a battle's replay, then lets the oldest go; nothing during a capture. Never throws: a battle ends whatever the disk says.</summary>
    public static void Save(Replay r)
    {
        if (Dev.Shot is not null || r.Length < MinSteps) return;
        try
        {
            Directory.CreateDirectory(Folder);
            var when = DateTimeOffset.FromUnixTimeMilliseconds(r.SavedAt).ToLocalTime();
            var path = System.IO.Path.Combine(Folder, $"{when:yyyy-MM-dd_HH-mm-ss}{Extension}");
            // Written whole aside, then put in place: a battle cut short leaves no half file.
            var temp = path + ".tmp";
            using (var file = File.Create(temp)) Replays.Write(file, r);
            File.Move(temp, path, true);
            foreach (var old in Files().Skip(Kept)) File.Delete(old);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"Rediffusion non enregistrée : {e.Message}");
        }
    }

    /// <summary>The replays, newest first, each with its header only.</summary>
    public static List<Entry> List() => [.. Files().Select(path =>
    {
        try
        {
            using var file = File.OpenRead(path);
            return new Entry(path, Replays.Read(file, headerOnly: true), null);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException)
        {
            return new Entry(path, null, e is InvalidDataException ? e.Message : "Fichier illisible.");
        }
    })];

    /// <summary>A whole replay, to watch; null with the reason when it cannot be read.</summary>
    public static Replay? Load(string path, out string? problem)
    {
        problem = null;
        try
        {
            using var file = File.OpenRead(path);
            return Replays.Read(file);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException)
        {
            problem = e is InvalidDataException ? e.Message : "Fichier illisible.";
            return null;
        }
    }

    public static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"Rediffusion non supprimée : {e.Message}");
        }
    }

    static IEnumerable<string> Files() => Directory.Exists(Folder)
        ? Directory.EnumerateFiles(Folder, "*" + Extension).OrderByDescending(f => System.IO.Path.GetFileName(f), StringComparer.Ordinal)
        : [];

    /// <summary>What a replay was: its mode, then who fought whom, teams together ("Escarmouche : Kla'ed et Nairan contre Nautolan").</summary>
    public static string Title(Replay r)
    {
        var mode = r.SandboxTools ? "Bac à sable" : r.Options.Mode switch
        {
            BattleMode.Survival => "Survie",
            BattleMode.Zones => "Contrôle de zone",
            BattleMode.Assault => "Assaut",
            BattleMode.Regicide => "Régicide",
            _ => "Escarmouche",
        };
        var slots = r.Options.Slots;
        // Free for all: each on its own; with teams, each team together.
        var camps = slots.GroupBy(s => s.Team == 0 ? -1 - s.Side.Id : s.Team)
            .Select(g => string.Join(" et ", g.Select(s => Factions.Defs[s.Race].Name)));
        return $"{mode} : {string.Join(r.Options.Mode == BattleMode.Survival ? ", " : " contre ", camps)}";
    }

    /// <summary>When, how long, how it ended: "02/11/2026 14:05, 12 min 30 s, victoire de Kla'ed".</summary>
    public static string Line(Replay r)
    {
        var when = DateTimeOffset.FromUnixTimeMilliseconds(r.SavedAt).ToLocalTime().ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        var end = r.Winner is { } w && r.Options.Slots.FirstOrDefault(s => s.Side == w) is { } winner ? $", victoire de {Factions.Defs[winner.Race].Name}"
            : r.Winner is not null ? ", victoire" : ", sans vainqueur";
        return $"{when}, {Clock(r.Seconds)}{end}";
    }

    /// <summary>A length of battle: "45 s", "12 min 30 s", "1 h 05 min".</summary>
    public static string Clock(double seconds)
    {
        var s = (int)seconds;
        return s < 60 ? $"{s} s" : s < 3600 ? $"{s / 60} min {s % 60:00} s" : $"{s / 3600} h {s % 3600 / 60:00} min";
    }
}
