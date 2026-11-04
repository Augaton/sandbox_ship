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
    /// <summary>The battle's terrain (Sim/Terrain.cs): none, light or dense.</summary>
    public TerrainDensity Terrain { get; init; }
    /// <summary>Fog of war in a skirmish; the sandbox goes without.</summary>
    public bool Fog { get; init; } = true;
    /// <summary>The rules of a skirmish, and their settings.</summary>
    public BattleMode Rules { get; init; } = BattleMode.Standard;
    public int ZoneTarget { get; init; } = 100;
    public int AssaultMinutes { get; init; } = 15;
    /// <summary>What each side starts with, as the lobby chose (Lobby.Credits); the sandbox and the tutorial have their own.</summary>
    public double Credits { get; init; } = Scenario.StartCredits;
    /// <summary>Survival: the waves' strength, in percent.</summary>
    public int WaveStrength { get; init; } = 100;
    /// <summary>The main menu's backdrop: a showcase fight under a drifting camera, without HUD, input or sound.</summary>
    public bool Demo { get; init; }
    /// <summary>A battle over the network: the host's world, or a player's replica of it (Network.NetBattle).</summary>
    public Network.NetBattle? Net { get; init; }
    /// <summary>A recorded battle played again: its world and its commands, no order of the player's (Sim/Replays.cs).</summary>
    public Replay? Replay { get; init; }
    /// <summary>The guided first battle (Ui/Tutorial.cs): a few ships to start with, a foe that does nothing but defend.</summary>
    public bool Tutorial { get; init; }

    /// <summary>The options to watch a replay: its battle as it was set up, through the eyes of whoever recorded it.</summary>
    public static BattleOptions Watch(Replay r) => new()
    {
        Player = r.Player, Slots = r.Options.Slots, Mode = r.SandboxTools ? GameMode.Sandbox : GameMode.Skirmish, Seed = r.Options.Seed,
        Map = r.Options.Map, Size = r.Options.Size, Raid = r.Options.Raid, Terrain = r.Options.Terrain, Fog = r.Options.Fog, Rules = r.Options.Mode,
        ZoneTarget = r.Options.ZoneTarget, AssaultMinutes = r.Options.AssaultMinutes, Credits = r.Options.Credits,
        WaveStrength = r.Options.WaveStrength, Replay = r,
    };

    /// <summary>The player's race against one or two others, each on its race's own side, every one at war with all; the enemies' AI at `difficulty`.</summary>
    public static IReadOnlyList<Slot> Versus(Faction player, IReadOnlyList<Faction> enemies, Difficulty difficulty = Difficulty.Normal) =>
        [new Slot(player, player), .. enemies.Select(e => new Slot(e, e, 0, difficulty))];
}
