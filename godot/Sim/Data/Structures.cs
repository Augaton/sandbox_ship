// Stations, their parts, and what can be built around them. Port of src/data/structures.ts.
namespace SandboxShip.Sim.Data;

public enum HardpointRole { Turret, HeavyTurret, Hangar, Shield, Shipyard, Engine }

public enum BuildableType
{
    Platform, Mine, Outpost, RepairDock, IonCannon,
    // Not in the web version: research needs one; the endgame weapon.
    Lab, StarLance,
}

public sealed record Squadron(ShipClass Cls, int Size);

/// <summary>Squadrons launched in turn, one per interval, up to a cap of craft alive at once.</summary>
public sealed record HangarDef(IReadOnlyList<Squadron> Squadrons, int Cap, double Interval);

public sealed record HardpointDef(HardpointRole Role, string Name, double Hull, double Radius, IReadOnlyList<WeaponDef> Weapons, HangarDef? Hangar = null);

/// <summary>
/// Where a part sits on its parent, with the parent at angle 0: x forward, y to starboard.
/// The overrides are for the smaller parts of ships.
/// </summary>
public sealed record HardpointSlot(
    HardpointRole Role, double X, double Y,
    double? Hull = null, double? Radius = null, IReadOnlyList<WeaponDef>? Weapons = null, HangarDef? Hangar = null);

public sealed record StationLevel(
    double Income, double Hull, double Shield, double ShieldRegen,
    // Cost and time to reach this level (unused for level 1).
    double UpgradeCost, double UpgradeTime,
    // Hardpoints added when this level is reached.
    IReadOnlyList<HardpointSlot> Hardpoints);

/// <summary>What a platform or an outpost becomes at its next level.</summary>
public sealed record TierDef(double Cost, double Time, double Hull, double Shield,
    IReadOnlyList<WeaponDef>? Weapons = null, HangarDef? Hangar = null, double? Zone = null);

public sealed record BuildableDef(
    BuildableType Id, string Name, double Hull, double Shield, double ShieldRegen, double Radius,
    double Cost, double BuildTime, int MinStationLevel, IReadOnlyList<WeaponDef> Weapons,
    // Credits per second once built.
    double Income = 0,
    HangarDef? Hangar = null,
    // Repairs every friendly ship in range.
    RepairDef? Repair = null,
    // Can also be built next to one of the faction's mines, not only near its station.
    bool NearMines = false,
    // Construction radius around it, like the station's: an outpost opens a new build area.
    double? Zone = null,
    // Levels 2 and 3, bought one after the other.
    IReadOnlyList<TierDef>? Tiers = null,
    // An ability of its own, charging from the moment it is built.
    AbilityId? Ability = null);

public static class Hardpoints
{
    public static readonly IReadOnlyDictionary<HardpointRole, HardpointDef> Defs = new Dictionary<HardpointRole, HardpointDef>
    {
        [HardpointRole.Turret] = new(HardpointRole.Turret, "Tourelle", 400, 14, [Weapons.Laser with { Range = 420, Damage = 12, Cooldown = 1 }]),
        [HardpointRole.HeavyTurret] = new(HardpointRole.HeavyTurret, "Tourelle lourde", 700, 18, [Weapons.Laser with { Range = 520, Damage = 30, Cooldown = 2 }]),
        [HardpointRole.Hangar] = new(HardpointRole.Hangar, "Hangar", 600, 20, [],
            new([new(ShipClass.Fighter, 3), new(ShipClass.Bomber, 2)], Cap: 8, Interval: 20)),
        // While one stands, the station or ship it belongs to has shields.
        [HardpointRole.Shield] = new(HardpointRole.Shield, "Générateur de bouclier", 500, 16, []),
        // While alive, the faction can build ships.
        [HardpointRole.Shipyard] = new(HardpointRole.Shipyard, "Chantier naval", 800, 22, []),
        // Each engine lost slows its ship down.
        [HardpointRole.Engine] = new(HardpointRole.Engine, "Réacteur", 300, 9, []),
    };
}

public static class Station
{
    public const string Name = "Station spatiale";
    public const double Radius = 90;
    public const double ConstructionRadius = 650;
    public static readonly IReadOnlyList<WeaponDef> Weapons = [Data.Weapons.Flak];

    public static readonly IReadOnlyList<StationLevel> Levels =
    [
        new(Income: 6, Hull: 4500, Shield: 1500, ShieldRegen: 5, UpgradeCost: 0, UpgradeTime: 0, Hardpoints:
        [
            new(HardpointRole.Shipyard, 0, -100),
            new(HardpointRole.Hangar, 0, 100),
            new(HardpointRole.Shield, -70, 0),
            new(HardpointRole.Turret, 75, -70),
            new(HardpointRole.Turret, 75, 70),
        ]),
        // Upgrades cost far more than in the web version, for longer games where each level is
        // an event, and every level is sturdier so that a station holds while its owner saves up.
        // Level 4 (not in the web version) unlocks the leviathan.
        new(Income: 8, Hull: 6500, Shield: 2200, ShieldRegen: 7, UpgradeCost: 4000, UpgradeTime: 60, Hardpoints:
        [
            new(HardpointRole.Turret, -75, -70),
            new(HardpointRole.Turret, -75, 70),
            new(HardpointRole.Hangar, 105, 0),
        ]),
        new(Income: 10, Hull: 9000, Shield: 3000, ShieldRegen: 10, UpgradeCost: 9000, UpgradeTime: 90, Hardpoints:
        [
            new(HardpointRole.HeavyTurret, 0, -150),
            new(HardpointRole.HeavyTurret, 0, 150),
        ]),
        new(Income: 14, Hull: 13000, Shield: 4200, ShieldRegen: 14, UpgradeCost: 18000, UpgradeTime: 120, Hardpoints:
        [
            new(HardpointRole.HeavyTurret, -150, 0),
            new(HardpointRole.HeavyTurret, 150, 0),
            new(HardpointRole.Shield, 70, 0),
        ]),
    ];
}

public static class Buildables
{
    /// <summary>Radius around a built mine where platforms and repair docks may go.</summary>
    public const double MineZone = 350;
    /// <summary>Outside every build area, a mine or an outpost needs one of the faction's ships this close...</summary>
    public const double HoldRange = 400;
    /// <summary>...and no enemy ship or gun this close.</summary>
    public const double DangerRange = 600;
    public const int MaxQueue = 8;
    /// <summary>A station's lost parts come back on their own: seconds to rebuild one, once left alone this long.</summary>
    public const double RebuildTime = 90;
    public const double RebuildQuiet = 20;
    /// <summary>Defense platforms each build area holds at most (not in the web version).</summary>
    public const int StationPlatforms = 3, OutpostPlatforms = 2, MinePlatforms = 1;

    public static readonly IReadOnlyList<BuildableType> All = Enum.GetValues<BuildableType>();

    public static readonly IReadOnlyDictionary<BuildableType, BuildableDef> Defs = new Dictionary<BuildableType, BuildableDef>
    {
        [BuildableType.Platform] = new(BuildableType.Platform, "Plateforme de défense", Hull: 900, Shield: 200, ShieldRegen: 5, Radius: 22,
            Cost: 400, BuildTime: 15, MinStationLevel: 1,
            Weapons: [Weapons.Laser with { Range = 450, Damage = 12 }, Weapons.Flak],
            NearMines: true,
            Tiers:
            [
                new(400, 20, 1400, 350, [Weapons.Laser with { Range = 470, Damage = 18 }, Weapons.Flak]),
                new(700, 30, 2000, 500, [Weapons.Laser with { Range = 500, Damage = 22 }, Weapons.Laser with { Range = 500, Damage = 22 }, Weapons.Flak]),
            ]),
        [BuildableType.Mine] = new(BuildableType.Mine, "Mine d'astéroïdes", Hull: 700, Shield: 0, ShieldRegen: 0, Radius: 26,
            Cost: 500, BuildTime: 20, MinStationLevel: 1, Weapons: [], Income: 8),
        [BuildableType.Outpost] = new(BuildableType.Outpost, "Avant-poste", Hull: 1000, Shield: 300, ShieldRegen: 5, Radius: 30,
            Cost: 700, BuildTime: 25, MinStationLevel: 1, Weapons: [Weapons.Flak], Zone: 450,
            Tiers:
            [
                new(500, 20, 1500, 450, [Weapons.Flak, Weapons.Laser with { Range = 450, Damage = 14 }], Zone: 600),
                new(800, 30, 2200, 650, [Weapons.Flak, Weapons.Laser with { Range = 480, Damage = 18 }, Weapons.Laser with { Range = 480, Damage = 18 }], Zone: 750),
            ]),
        [BuildableType.RepairDock] = new(BuildableType.RepairDock, "Dock de réparation", Hull: 900, Shield: 200, ShieldRegen: 5, Radius: 30,
            Cost: 600, BuildTime: 20, MinStationLevel: 1, Weapons: [], Repair: new(280, 15), NearMines: true),
        [BuildableType.IonCannon] = new(BuildableType.IonCannon, "Canon ionique", Hull: 1400, Shield: 400, ShieldRegen: 8, Radius: 26,
            Cost: 1500, BuildTime: 35, MinStationLevel: 3,
            Weapons: [new(WeaponKind.Ray, 900, 90, 5, 1200, Mult: new SizeMult(Small: 0.1, Medium: 0.8))]),
        // Research goes on only while one stands; a skirmish gives one beside each station.
        [BuildableType.Lab] = new(BuildableType.Lab, "Laboratoire", Hull: 1200, Shield: 400, ShieldRegen: 5, Radius: 34,
            Cost: 800, BuildTime: 30, MinStationLevel: 1, Weapons: []),
        // The endgame weapon (not in the web version): one per faction, at the price of a fleet,
        // striking anywhere on the map every four minutes. Bring it down to stop it.
        [BuildableType.StarLance] = new(BuildableType.StarLance, "Lance stellaire", Hull: 8000, Shield: 3000, ShieldRegen: 15, Radius: 52,
            Cost: 30000, BuildTime: 180, MinStationLevel: 4, Weapons: [], Ability: AbilityId.StarLance),
    };
}
