// The battle saves on disk (Sim/BattleSaves.cs), in user://battles, apart from the campaign's:
// what the menu lists and loads, what the battle screen writes. A capture writes none, as it
// writes no settings and no campaign, unless it keeps them in a folder of its own (--battle-saves).
using System.Globalization;
using Godot;
using SandboxShip.Sim;

namespace SandboxShip.Core;

public static class BattleStore
{
    public static string Folder => Dev.BattleSaves ?? ProjectSettings.GlobalizePath("user://battles");

    /// <summary>Whether this run writes battle saves: not a capture nor a server, unless the saves are kept apart.</summary>
    public static bool Writes => !Dev.Service && (Dev.Shot is null && Dev.Snap is null || Dev.BattleSaves is not null);

    public static List<BattleSaves.Slot> List() => BattleSaves.List(Folder);

    /// <summary>The most recent save that reads, for "Continuer".</summary>
    public static BattleSaves.Slot? Latest() => List().FirstOrDefault(s => s.Info is not null);

    /// <summary>What a save is called in the list: the automatic one says so, else the player's name for it.</summary>
    public static string Title(BattleSaves.Slot s) => s.IsAuto ? "Sauvegarde automatique" : s.Info is { Name.Length: > 0 } i ? i.Name : "Sauvegarde";

    /// <summary>Where and how long, then when: "carte moyenne, 23 min 10 s, le 19/11/2026 14:05".</summary>
    public static string Line(BattleSaveInfo i)
    {
        var size = Scenario.MapSizes[i.Options.Size].Name.ToLowerInvariant();
        var when = DateTimeOffset.FromUnixTimeMilliseconds(i.SavedAt).ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        return $"carte {size}, {ReplayStore.Clock(i.Seconds)}, le {when}";
    }
}
