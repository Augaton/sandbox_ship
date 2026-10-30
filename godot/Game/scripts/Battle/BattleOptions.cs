// What a battle is: who plays, against whom, on what map; or a battle of the campaign.
using SandboxShip.Sim;
using SandboxShip.Sim.Data;

namespace SandboxShip.Battle;

public enum GameMode { Campaign, Skirmish, Sandbox }

/// <summary>A battle of the galaxy campaign, fought in the battle view; `Title` names it ("Bataille de Vesper").</summary>
public sealed record CampaignBattle(World World, int BattleId, string Title, Faction Attacker, Faction Defender);

public sealed record BattleOptions
{
    public Faction Player { get; init; }
    /// <summary>Everyone else: one or two factions, each against all.</summary>
    public IReadOnlyList<Faction> Enemies { get; init; } = [];
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
}
