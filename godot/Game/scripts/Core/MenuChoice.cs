// What the main menu starts: a campaign, a skirmish or the sandbox, and its setup.
using SandboxShip.Battle;
using SandboxShip.Sim;
using SandboxShip.Sim.Data;

namespace SandboxShip.Core;

public sealed record MenuChoice
{
    /// <summary>The race the player picked: it plays on that race's own side.</summary>
    public Faction Player { get; init; } = Faction.Klaed;
    /// <summary>One or two opponents, every faction against all others.</summary>
    public IReadOnlyList<Faction> Enemies { get; init; } = [Faction.Nairan];
    public GameMode Mode { get; init; } = GameMode.Skirmish;
    /// <summary>Continue the saved campaign instead of starting a new one.</summary>
    public bool Resume { get; init; }
    /// <summary>Map of a new game: generated from the seed, or the hand-made classic one (two players).</summary>
    public MapKind Map { get; init; } = MapKind.Random;
    public int Seed { get; init; } = 1;
    /// <summary>Battle map size for skirmishes and the sandbox; galaxy size for a new campaign.</summary>
    public MapSize Size { get; init; } = MapSize.Medium;
    public Difficulty Difficulty { get; init; } = Difficulty.Normal;
    /// <summary>A neutral colossus to raid, in skirmishes.</summary>
    public bool Raid { get; init; } = true;

    /// <summary>Who plays, the player's seat first: the picks above, one side per race, the enemies at the chosen difficulty.</summary>
    public IReadOnlyList<Slot> Slots => BattleOptions.Versus(Player, Enemies, Difficulty);
}

/// <summary>
/// Launch options for checking a screen without clicking through the menu, after `--` on the
/// command line: --start=menu|battle|sandbox|campaign|gallery (every ship class and platform level, paused)|raid (a battle centered on the colossus)|stress
/// (three large fleets in a fight, one step a frame, frame times logged before a --shot run quits), --skip=SECONDS (battle or galaxy played
/// ahead, the player's side on the AI too), --zoom=Z, --shot=FILE.png and --frames=N (capture, then quit),
/// --menu=home|new|skirmish|controls|credits|options (the menu screen opened first; skirmish:
/// the setup with that mode chosen), --size=small|medium|large (the map, or the galaxy, of --start).
/// </summary>
public static class Dev
{
    public static string Start = "menu";
    public static double Skip;
    public static float? Zoom;
    public static string? Shot;
    public static int Frames = 240;
    public static string? Dump;
    public static Godot.Vector2? At;
    public static string Menu = "home";
    public static MapSize Size = MapSize.Medium;

    public static void Parse(IEnumerable<string> args)
    {
        foreach (var arg in args)
        {
            var (key, value) = arg.Split('=', 2) is [var k, var v] ? (k, v) : (arg, "");
            switch (key)
            {
                case "--start": Start = value; break;
                case "--skip": Skip = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
                case "--zoom": Zoom = float.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
                case "--shot": Shot = value; break;
                case "--frames": Frames = int.Parse(value); break;
                case "--dump": Dump = value; break;
                case "--at" when value.Split(',') is [var ax, var ay]:
                    At = new Godot.Vector2(float.Parse(ax, System.Globalization.CultureInfo.InvariantCulture), float.Parse(ay, System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case "--menu": Menu = value; break;
                case "--size": Size = Enum.Parse<MapSize>(value, true); break;
            }
        }
    }
}
