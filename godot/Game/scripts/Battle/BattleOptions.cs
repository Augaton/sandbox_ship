// What a battle is: who plays, against whom, on what map; or a battle of the campaign.
using SandboxShip.Sim;
using SandboxShip.Sim.Data;

namespace SandboxShip.Battle;

public enum GameMode { Campaign, Skirmish, Sandbox }

/// <summary>A battle of the galaxy campaign, fought in the battle view; `Title` names it ("Bataille de Vesper").</summary>
public sealed record CampaignBattle(World World, int BattleId, string Title, Side Attacker, Side Defender);

public sealed record BattleOptions
{
    /// <summary>The local player's side.</summary>
    public Side Player { get; init; }
    /// <summary>Who plays a skirmish or the sandbox, the player's seat among them; a campaign battle and the demo bring their own.</summary>
    public IReadOnlyList<Slot> Slots { get; init; } = [];
    public GameMode Mode { get; init; } = GameMode.Skirmish;
    public CampaignBattle? Campaign { get; init; }
    /// <summary>Map of a skirmish or sandbox game; drawn at random when null.</summary>
    public int? Seed { get; init; }
    public MapKind Map { get; init; } = MapKind.Random;
    public MapSize Size { get; init; } = MapSize.Medium;
    public Difficulty Difficulty { get; init; } = Difficulty.Normal;
    /// <summary>A neutral colossus guards the far side of the map, with a bounty on it.</summary>
    public bool Raid { get; init; }
    /// <summary>The main menu's backdrop: a showcase fight under a drifting camera, without HUD, input or sound.</summary>
    public bool Demo { get; init; }

    /// <summary>The player's race against one or two others, each on its race's own side, every one at war with all; the enemies' AI at `difficulty`.</summary>
    public static IReadOnlyList<Slot> Versus(Faction player, IReadOnlyList<Faction> enemies, Difficulty difficulty = Difficulty.Normal) =>
        [new Slot(player, player), .. enemies.Select(e => new Slot(e, e, 0, difficulty))];
}
