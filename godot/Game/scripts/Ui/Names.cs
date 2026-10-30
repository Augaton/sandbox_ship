// Names shown to the player, and the alert rule. Port of typeName and entityName
// (src/ui/hud.ts) and of src/ui/alerts.ts.
using SandboxShip.Sim;
using SandboxShip.Sim.Data;

namespace SandboxShip.Ui;

public static class Names
{
    /// <summary>Name of a ship class, structure or part from its type.</summary>
    public static string TypeName(string type)
    {
        if (type == "station") return Station.Name;
        if (Enum.TryParse<HardpointRole>(type, true, out var role) && Hardpoints.Defs.TryGetValue(role, out var hp)) return hp.Name;
        if (Ids.IsShip(type)) return Ships.Defs[Ids.Ship(type)].Name;
        return Ids.IsBuildable(type, out var b) ? Buildables.Defs[b].Name : type;
    }

    public static string TypeName(HardpointRole role) => Hardpoints.Defs[role].Name;

    public static string EntityName(Entity e) => e.Hero is HeroId h ? Heroes.Defs[h].Name : TypeName(e.Type);

    /// <summary>"12 min 05 s"</summary>
    public static string Duration(double seconds)
    {
        var m = (int)Math.Floor(seconds / 60);
        var s = (int)Math.Floor(seconds % 60);
        return m > 0 ? $"{m} min {s:00} s" : $"{s} s";
    }
}

/// <summary>What happened to the player out of sight: an attack or a loss, and where.</summary>
public sealed record Alert(string Text, double X, double Y, double Time);

public static class Alerts
{
    /// <summary>Within this long (ms) and this close to a reported spot, more of the same is old news.</summary>
    public const double Gap = 15000;
    public const double Radius = 900;

    /// <summary>Whether an event here is worth an alert: nothing was reported near it lately.</summary>
    public static bool IsNews(IEnumerable<Alert> alerts, double x, double y, double now) =>
        !alerts.Any(a => now - a.Time < Gap && Worlds.Hypot(a.X - x, a.Y - y) < Radius);
}
