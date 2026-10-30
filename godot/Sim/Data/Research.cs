// Research: permanent upgrades, one level at a time, bought with credits. Port of
// src/data/research.ts.
namespace SandboxShip.Sim.Data;

public enum TechId
{
    Weapons, Shields, Armor, Engines, Mining, Hangars,
    // Not in the web version.
    Logistics, Repair, Fortification, Energy,
}

public sealed record TechDef(TechId Id, string Name, string Effect,
    // Cost and time of each level, from level 1 to the last.
    IReadOnlyList<double> Costs, IReadOnlyList<double> Times)
{
    public int MaxLevel => Costs.Count;
}

public static class Techs
{
    /// <summary>
    /// The highest level of any tech. The web version stops every tech at 3; here the combat
    /// techs go to 5, most others to 4, and hangars, whose effect would run away, stay at 3.
    /// </summary>
    public const int MaxLevel = 5;

    public static readonly IReadOnlyList<TechId> All = Enum.GetValues<TechId>();

    public static readonly IReadOnlyDictionary<TechId, TechDef> Defs = new Dictionary<TechId, TechDef>
    {
        [TechId.Weapons] = new(TechId.Weapons, "Armement", "+10 % de dégâts par niveau", [600, 1200, 2000, 3500, 5500], [30, 45, 60, 80, 100]),
        [TechId.Shields] = new(TechId.Shields, "Boucliers", "+15 % de bouclier et de recharge par niveau", [600, 1200, 2000, 3500, 5500], [30, 45, 60, 80, 100]),
        [TechId.Armor] = new(TechId.Armor, "Blindage", "+10 % de coque par niveau", [600, 1200, 2000, 3500, 5500], [30, 45, 60, 80, 100]),
        [TechId.Engines] = new(TechId.Engines, "Propulsion", "+10 % de vitesse par niveau", [400, 800, 1400, 2400], [25, 35, 50, 65]),
        [TechId.Mining] = new(TechId.Mining, "Extraction", "+20 % de revenu des mines par niveau", [500, 1000, 1600, 2800], [25, 35, 50, 65]),
        [TechId.Hangars] = new(TechId.Hangars, "Hangars", "+2 appareils par hangar et lancements 15 % plus rapides, par niveau", [500, 1000, 1600], [25, 35, 50]),
        [TechId.Logistics] = new(TechId.Logistics, "Logistique", "Vaisseaux construits 7 % plus vite par niveau", [500, 1000, 1600, 2800], [25, 35, 50, 65]),
        [TechId.Repair] = new(TechId.Repair, "Réparation", "+25 % de réparation par niveau : soutiens, docks, flottes au repos", [400, 800, 1400, 2400], [25, 35, 50, 65]),
        [TechId.Fortification] = new(TechId.Fortification, "Défenses", "+10 % de dégâts des stations et plateformes par niveau", [500, 1000, 1600, 2800], [25, 35, 50, 65]),
        [TechId.Energy] = new(TechId.Energy, "Énergie", "Capacités rechargées 6 % plus vite par niveau", [600, 1200, 2000, 3500], [30, 45, 60, 80]),
    };

    /// <summary>Bonus per level of the techs that scale a stat.</summary>
    public const double WeaponsBonus = 0.1, ShieldsBonus = 0.15, ArmorBonus = 0.1, EnginesBonus = 0.1, MiningBonus = 0.2,
        LogisticsBonus = 0.07, RepairBonus = 0.25, FortificationBonus = 0.1, EnergyBonus = 0.06;

    /// <summary>The station level a research level needs: its own, up to the last station level.</summary>
    public static int StationFor(int level) => Math.Min(level, Station.Levels.Count);
}

/// <summary>Research levels of a faction, 0 to each tech's <see cref="TechDef.MaxLevel"/>.</summary>
public sealed class ResearchLevels
{
    readonly int[] levels = new int[Techs.All.Count];

    public int this[TechId tech]
    {
        get => levels[(int)tech];
        set => levels[(int)tech] = value;
    }

    public ResearchLevels Clone()
    {
        var copy = new ResearchLevels();
        levels.CopyTo(copy.levels, 0);
        return copy;
    }
}

/// <summary>A level being researched or waiting in the queue, already paid for.</summary>
public sealed class ResearchJob
{
    public TechId Tech { get; init; }
    public double Remaining { get; set; }
    public double Total { get; init; }
    public double Cost { get; init; }

    /// <summary>For saves.</summary>
    public ResearchJob() { }

    public ResearchJob(TechId tech, double time, double cost)
    {
        Tech = tech;
        Remaining = Total = time;
        Cost = cost;
    }
}
