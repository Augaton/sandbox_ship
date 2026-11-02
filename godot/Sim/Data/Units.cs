// Gameplay definitions only: no Godot, no sprite paths. Port of src/data/units.ts; the
// order of every list matters, the AI draws from them with the shared random generator.
namespace SandboxShip.Sim.Data;

public enum Faction
{
    Klaed, Nairan, Nautolan,
    // Not playable, not in the web version: the neutral guardians of raid sites.
    Errant,
}

/// <summary>Damage multipliers are keyed by the size class of the target.</summary>
public enum SizeClass { Small, Medium, Large, Structure }

/// <summary>
/// The web version's four, then this version's: a beam that holds on its target (no projectile),
/// swarm missiles, and ion bolts that tear down shields.
/// </summary>
public enum WeaponKind { Bullet, Laser, Torpedo, Ray, Beam, Missile, Ion }

public enum ShipClass
{
    Scout, Fighter, Bomber, Support, Torpedo, Frigate, Battlecruiser,
    // Not in the web version: a tank and a carrier.
    Bulwark, Carrier,
    Dreadnought, Titan,
    // Not in the web version: the tier above the titan, and the neutral raid boss nobody builds.
    Leviathan, Colossus,
}

/// <summary>What sets a faction apart. Multipliers apply to everything it fields.</summary>
public sealed record FactionTrait(
    string Name, string Description,
    double Damage = 1, double Shield = 1, double Hull = 1, double Speed = 1,
    // Ship prices.
    double Cost = 1,
    // Share of max hull a ship repairs by itself each second.
    double HullRegen = 0);

public sealed record FactionDef(string Name, uint Color, FactionTrait Trait);

/// <summary>Damage against each size class; 1 where nothing is said.</summary>
public sealed record SizeMult(double Small = 1, double Medium = 1, double Large = 1, double Structure = 1)
{
    public static readonly SizeMult None = new();

    public double Of(SizeClass size) => size switch
    {
        SizeClass.Small => Small,
        SizeClass.Medium => Medium,
        SizeClass.Large => Large,
        _ => Structure,
    };
}

/// <summary>
/// A weapon. A beam's Damage is per second. Not in the web version: a burst of `Burst` shots
/// `BurstGap` seconds apart, Cooldown being the reload after it; point defense, which shoots
/// down torpedoes and missiles in flight; and how hard it hits shields and hulls (ion bolts).
/// </summary>
public sealed record WeaponDef(
    WeaponKind Kind, double Range, double Damage, double Cooldown, double ProjectileSpeed,
    bool Homing = false, SizeMult? Mult = null,
    int Burst = 1, double BurstGap = 0, bool PointDefense = false, double ShieldMult = 1, double HullMult = 1)
{
    public SizeMult Multipliers => Mult ?? SizeMult.None;
}

public sealed record RepairDef(double Range, double Rate);

public sealed record ShipDef(
    ShipClass Id, string Name, SizeClass Size,
    double Hull, double Shield, double ShieldRegen, double Speed, double Accel, double TurnRate, double Radius,
    // Hangar craft are free and have no cost or build time.
    bool FromHangar, double Cost, double BuildTime, int MinStationLevel,
    // Weapons of ships without hardpoints; ships with hardpoints carry theirs on their turrets.
    IReadOnlyList<WeaponDef> Weapons,
    RepairDef? Repair = null,
    // Destructible parts: turrets, engines, shield generator, hangar.
    IReadOnlyList<HardpointSlot>? Hardpoints = null,
    AbilityId? Ability = null,
    // More abilities, for the biggest ships (not in the web version).
    IReadOnlyList<AbilityId>? Extra = null,
    // Ships delivered per order; cost and build time are for the whole squadron.
    int? Squad = null,
    // At most this many at a time per faction, counting those being built.
    int? Limit = null)
{
    public bool Unique => Limit is not null;

    /// <summary>Why one more cannot be ordered, once the limit is reached.</summary>
    public string LimitReason => Limit == 1 ? $"Un seul {Name} à la fois" : $"{Name} : {Limit} au plus à la fois";
}

public static class Factions
{
    /// <summary>The playable factions; the Errants are neutral and only guard raid sites.</summary>
    public static readonly IReadOnlyList<Faction> All = [Faction.Klaed, Faction.Nairan, Faction.Nautolan];

    public static readonly IReadOnlyDictionary<Faction, FactionDef> Defs = new Dictionary<Faction, FactionDef>
    {
        [Faction.Klaed] = new("Kla'ed", 0xe0563b, new("Assaut", "Armes +10 %, boucliers −15 %", Damage: 1.1, Shield: 0.85)),
        [Faction.Nairan] = new("Nairan", 0x4fa3e0, new("Technologie", "Boucliers +40 %, coques −5 %", Shield: 1.4, Hull: 0.95)),
        [Faction.Nautolan] = new("Nautolan", 0x7bd36a, new(
            "Biomécanique", "Les coques se régénèrent (1 % par seconde), vaisseaux 10 % moins chers",
            Cost: 0.9, HullRegen: 0.01)),
        [Faction.Errant] = new("Errants", 0xd6b25a, new("Gardiens",
            "Neutres, ils gardent leur site et frappent quiconque s'en approche. Coques et boucliers +20 %, vaisseaux 20 % plus lents",
            Shield: 1.2, Hull: 1.2, Speed: 0.8)),
    };

    public static FactionTrait Trait(Faction f) => Defs[f].Trait;

    /// <summary>
    /// Colors of the sides past the races' own (see Worlds.DefaultColor): violet, cyan, pink,
    /// yellow-green, silver, lavender. Far enough from the races' colors, from each other and
    /// from the dark map to tell apart; none near the Errants' gold.
    /// </summary>
    public static readonly IReadOnlyList<uint> PlayerColors = [0x9a6cf0, 0x3fd6d0, 0xf06aae, 0xd4f05a, 0xdfe3ea, 0xc8a8ff];
}

public static class Weapons
{
    /// <summary>Quick light guns, against small craft; they shoot torpedoes and missiles down too (not in the web version).</summary>
    public static readonly WeaponDef Flak = new(WeaponKind.Bullet, 260, 4, 0.35, 700,
        Mult: new SizeMult(Small: 2, Medium: 0.6, Large: 0.3, Structure: 0.3), PointDefense: true);
    public static readonly WeaponDef Laser = new(WeaponKind.Laser, 400, 16, 1.2, 900, Mult: new SizeMult(Small: 0.4));
    public static readonly WeaponDef Torpedo = new(WeaponKind.Torpedo, 420, 45, 4, 320, Homing: true,
        Mult: new SizeMult(Small: 0.15, Large: 1.6, Structure: 1.6));

    // Not in the web version.

    /// <summary>A beam that holds on its target while it stays in reach: it never misses; damage per second.</summary>
    public static readonly WeaponDef Beam = new(WeaponKind.Beam, 340, 12, 0, 0, Mult: new SizeMult(Small: 0.6, Structure: 0.8));
    /// <summary>A burst cannon: six heavy rounds in half a second, then a long reload.</summary>
    public static readonly WeaponDef Burst = new(WeaponKind.Bullet, 380, 10, 3.4, 850, Burst: 6, BurstGap: 0.1,
        Mult: new SizeMult(Small: 0.5, Medium: 1.2));
    /// <summary>Swarm missiles: a salvo of small homing missiles, deadly to small craft, then a long reload.</summary>
    public static readonly WeaponDef Swarm = new(WeaponKind.Missile, 420, 7, 5, 420, Homing: true, Burst: 6, BurstGap: 0.08,
        Mult: new SizeMult(Small: 2, Medium: 0.8, Large: 0.4, Structure: 0.3));
    /// <summary>Ion bolts: twice their damage to shields, a scratch on the hull; they open the way for the rest.</summary>
    public static readonly WeaponDef Ion = new(WeaponKind.Ion, 440, 20, 1.6, 800, ShieldMult: 2, HullMult: 0.2);

    public static double MaxRange(IEnumerable<WeaponDef> weapons) => weapons.Aggregate(0.0, (m, w) => Math.Max(m, w.Range));
}

public static class Ships
{
    static readonly WeaponDef HeavyLaser = Weapons.Laser with { Range = 450, Damage = 22 };
    static readonly WeaponDef HeavyBurst = Weapons.Burst with { Range = 450, Damage = 14 };
    static readonly WeaponDef LeviathanRay = new(WeaponKind.Ray, 700, 90, 2.5, 1100, Mult: new SizeMult(Small: 0.2));
    static readonly WeaponDef ColossusRay = new(WeaponKind.Ray, 800, 110, 2.5, 1200, Mult: new SizeMult(Small: 0.25));
    /// <summary>A carrier's hangar: fighters and bombers in turn, many and often.</summary>
    static readonly HangarDef CarrierHangar = new([new(ShipClass.Fighter, 4), new(ShipClass.Bomber, 3)], Cap: 10, Interval: 14);

    public static readonly IReadOnlyList<ShipClass> All = Enum.GetValues<ShipClass>();

    public static readonly IReadOnlyDictionary<ShipClass, ShipDef> Defs = new Dictionary<ShipClass, ShipDef>
    {
        [ShipClass.Scout] = new(ShipClass.Scout, "Éclaireur", SizeClass.Small,
            Hull: 60, Shield: 20, ShieldRegen: 2, Speed: 230, Accel: 300, TurnRate: 4, Radius: 10,
            FromHangar: false, Cost: 400, BuildTime: 10, MinStationLevel: 1,
            Weapons: [new(WeaponKind.Bullet, 220, 4, 0.4, 650)],
            Ability: AbilityId.Sprint, Squad: 3),
        // Hangars launch them, and here (not in the web version) the shipyard builds them too.
        [ShipClass.Fighter] = new(ShipClass.Fighter, "Chasseur", SizeClass.Small,
            Hull: 40, Shield: 0, ShieldRegen: 0, Speed: 260, Accel: 360, TurnRate: 5, Radius: 9,
            FromHangar: false, Cost: 300, BuildTime: 10, MinStationLevel: 1, Squad: 4,
            Weapons: [new(WeaponKind.Bullet, 200, 3, 0.3, 700, Mult: new SizeMult(Small: 1.5, Medium: 0.5, Large: 0.3, Structure: 0.2))]),
        [ShipClass.Bomber] = new(ShipClass.Bomber, "Bombardier", SizeClass.Small,
            Hull: 70, Shield: 0, ShieldRegen: 0, Speed: 190, Accel: 260, TurnRate: 3.5, Radius: 12,
            FromHangar: false, Cost: 500, BuildTime: 15, MinStationLevel: 1,
            Weapons: [Weapons.Torpedo with { Range = 260, Damage = 30, Cooldown = 3, Mult = new SizeMult(Small: 0.2, Large: 1.5, Structure: 1.8) }],
            Squad: 3),
        [ShipClass.Support] = new(ShipClass.Support, "Soutien", SizeClass.Medium,
            Hull: 220, Shield: 80, ShieldRegen: 4, Speed: 120, Accel: 120, TurnRate: 2, Radius: 18,
            FromHangar: false, Cost: 400, BuildTime: 12, MinStationLevel: 1,
            Weapons: [Weapons.Flak with { Damage = 3 }],
            Repair: new(220, 10), Ability: AbilityId.Repair),
        [ShipClass.Torpedo] = new(ShipClass.Torpedo, "Torpilleur", SizeClass.Medium,
            Hull: 300, Shield: 100, ShieldRegen: 4, Speed: 110, Accel: 110, TurnRate: 1.8, Radius: 18,
            FromHangar: false, Cost: 500, BuildTime: 14, MinStationLevel: 2,
            Weapons: [Weapons.Torpedo], Ability: AbilityId.Salvo),
        [ShipClass.Frigate] = new(ShipClass.Frigate, "Frégate", SizeClass.Medium,
            Hull: 600, Shield: 250, ShieldRegen: 3, Speed: 90, Accel: 80, TurnRate: 1.2, Radius: 26,
            FromHangar: false, Cost: 650, BuildTime: 16, MinStationLevel: 1,
            Weapons: [Weapons.Flak],
            Hardpoints: [
                // A beam in this version, a laser in the web one.
                new(HardpointRole.Turret, 10, 0, Hull: 200, Radius: 7, Weapons: [Weapons.Beam]),
                new(HardpointRole.Shield, -12, 0, Hull: 220, Radius: 7),
            ],
            Ability: AbilityId.Overcharge),
        [ShipClass.Battlecruiser] = new(ShipClass.Battlecruiser, "Croiseur", SizeClass.Large,
            Hull: 1100, Shield: 600, ShieldRegen: 5, Speed: 65, Accel: 50, TurnRate: 0.7, Radius: 40,
            FromHangar: false, Cost: 2000, BuildTime: 34, MinStationLevel: 2,
            Weapons: [],
            Hardpoints: [
                new(HardpointRole.Turret, 14, -18, Hull: 250, Radius: 8, Weapons: [HeavyLaser]),
                // A burst cannon in this version, a second heavy laser in the web one.
                new(HardpointRole.Turret, 14, 18, Hull: 250, Radius: 8, Weapons: [Weapons.Burst]),
                new(HardpointRole.Turret, -8, 0, Hull: 200, Radius: 7, Weapons: [Weapons.Flak]),
                new(HardpointRole.Shield, -24, 0, Hull: 350, Radius: 8),
                new(HardpointRole.Engine, -42, -12),
                new(HardpointRole.Engine, -42, 12),
            ],
            Ability: AbilityId.Barrage),
        // A tank: as tough as a dreadnought for less, one light gun, and it draws the enemy's fire.
        [ShipClass.Bulwark] = new(ShipClass.Bulwark, "Rempart", SizeClass.Large,
            Hull: 3200, Shield: 1800, ShieldRegen: 12, Speed: 55, Accel: 40, TurnRate: 0.6, Radius: 44,
            FromHangar: false, Cost: 2600, BuildTime: 40, MinStationLevel: 2,
            Weapons: [Weapons.Flak],
            Ability: AbilityId.Taunt),
        // A carrier: next to no guns, but two big hangars that keep launching.
        [ShipClass.Carrier] = new(ShipClass.Carrier, "Transporteur", SizeClass.Large,
            Hull: 1600, Shield: 700, ShieldRegen: 6, Speed: 50, Accel: 40, TurnRate: 0.55, Radius: 52,
            FromHangar: false, Cost: 3600, BuildTime: 50, MinStationLevel: 2,
            Weapons: [],
            Hardpoints: [
                new(HardpointRole.Turret, 24, 0, Hull: 250, Radius: 8, Weapons: [Weapons.Flak]),
                new(HardpointRole.Hangar, -6, -22, Hull: 450, Radius: 10, Hangar: CarrierHangar),
                new(HardpointRole.Hangar, -6, 22, Hull: 450, Radius: 10, Hangar: CarrierHangar),
                new(HardpointRole.Shield, -26, 0, Hull: 350, Radius: 8),
                new(HardpointRole.Engine, -50, -14),
                new(HardpointRole.Engine, -50, 14),
            ],
            Ability: AbilityId.Launch),
        [ShipClass.Dreadnought] = new(ShipClass.Dreadnought, "Cuirassé", SizeClass.Large,
            Hull: 2800, Shield: 1500, ShieldRegen: 8, Speed: 45, Accel: 35, TurnRate: 0.45, Radius: 56,
            FromHangar: false, Cost: 4800, BuildTime: 60, MinStationLevel: 3,
            Weapons: [],
            Hardpoints: [
                new(HardpointRole.HeavyTurret, 34, 0, Hull: 400, Radius: 11,
                    Weapons: [new(WeaponKind.Ray, 560, 60, 2.5, 1100, Mult: new SizeMult(Small: 0.2))]),
                new(HardpointRole.Turret, 10, -24, Hull: 250, Radius: 8, Weapons: [HeavyLaser]),
                // An ion cannon in this version, a second heavy laser in the web one.
                new(HardpointRole.Turret, 10, 24, Hull: 250, Radius: 8, Weapons: [Weapons.Ion]),
                new(HardpointRole.Turret, -16, -22, Hull: 200, Radius: 7, Weapons: [Weapons.Flak]),
                new(HardpointRole.Turret, -16, 22, Hull: 200, Radius: 7, Weapons: [Weapons.Flak]),
                new(HardpointRole.Hangar, -8, 0, Hull: 400, Radius: 10,
                    Hangar: new([new(ShipClass.Fighter, 3)], Cap: 6, Interval: 25)),
                new(HardpointRole.Shield, -32, 0, Hull: 350, Radius: 9),
                new(HardpointRole.Engine, -58, -16),
                new(HardpointRole.Engine, -58, 16),
            ],
            Ability: AbilityId.Broadside, Extra: [AbilityId.Overcharge]),
        [ShipClass.Titan] = new(ShipClass.Titan, "Titan", SizeClass.Large,
            Hull: 6000, Shield: 3500, ShieldRegen: 12, Speed: 32, Accel: 25, TurnRate: 0.3, Radius: 84,
            FromHangar: false, Cost: 14000, BuildTime: 120, MinStationLevel: 3,
            // Flak in the hull: fewer parts on screen, the hull's drawing shows through.
            Weapons: [Weapons.Flak, Weapons.Flak],
            Hardpoints: [
                new(HardpointRole.HeavyTurret, 52, -16, Hull: 500, Radius: 12,
                    Weapons: [new(WeaponKind.Ray, 600, 70, 2.5, 1100, Mult: new SizeMult(Small: 0.2))]),
                new(HardpointRole.HeavyTurret, 52, 16, Hull: 500, Radius: 12,
                    Weapons: [new(WeaponKind.Ray, 600, 70, 2.5, 1100, Mult: new SizeMult(Small: 0.2))]),
                new(HardpointRole.Turret, 20, -46, Hull: 300, Radius: 9, Weapons: [Weapons.Laser with { Range = 470, Damage = 24 }]),
                new(HardpointRole.Turret, 20, 46, Hull: 300, Radius: 9, Weapons: [Weapons.Laser with { Range = 470, Damage = 24 }]),
                new(HardpointRole.Turret, -22, -52, Hull: 300, Radius: 9, Weapons: [Weapons.Swarm]),
                new(HardpointRole.Turret, -22, 52, Hull: 300, Radius: 9, Weapons: [Weapons.Swarm]),
                new(HardpointRole.Hangar, -14, 0, Hull: 500, Radius: 11,
                    Hangar: new([new(ShipClass.Fighter, 3), new(ShipClass.Bomber, 2)], Cap: 8, Interval: 20)),
                new(HardpointRole.Shield, -40, -14, Hull: 400, Radius: 9),
                new(HardpointRole.Shield, -40, 14, Hull: 400, Radius: 9),
                new(HardpointRole.Engine, -76, -38),
                new(HardpointRole.Engine, -80, 0),
                new(HardpointRole.Engine, -76, 38),
            ],
            Ability: AbilityId.Spinal, Extra: [AbilityId.Broadside, AbilityId.Launch], Limit: 2),
        // The tier above the titan: three dreadnought hulls in one, heavy guns, two hangars,
        // and a shield dome over the fleet around it.
        [ShipClass.Leviathan] = new(ShipClass.Leviathan, "Léviathan", SizeClass.Large,
            Hull: 12000, Shield: 7000, ShieldRegen: 20, Speed: 24, Accel: 18, TurnRate: 0.2, Radius: 130,
            FromHangar: false, Cost: 32000, BuildTime: 200, MinStationLevel: 4,
            Weapons: [Weapons.Flak, Weapons.Flak, Weapons.Flak],
            Hardpoints: [
                new(HardpointRole.HeavyTurret, 80, -28, Hull: 700, Radius: 13, Weapons: [LeviathanRay]),
                new(HardpointRole.HeavyTurret, 80, 28, Hull: 700, Radius: 13, Weapons: [LeviathanRay]),
                new(HardpointRole.HeavyTurret, 40, 0, Hull: 700, Radius: 13, Weapons: [LeviathanRay]),
                new(HardpointRole.Turret, 30, -72, Hull: 350, Radius: 9, Weapons: [Weapons.Laser with { Range = 480, Damage = 26 }]),
                new(HardpointRole.Turret, 30, 72, Hull: 350, Radius: 9, Weapons: [Weapons.Laser with { Range = 480, Damage = 26 }]),
                new(HardpointRole.Turret, -34, -84, Hull: 350, Radius: 9, Weapons: [HeavyBurst]),
                new(HardpointRole.Turret, -34, 84, Hull: 350, Radius: 9, Weapons: [HeavyBurst]),
                new(HardpointRole.Turret, -70, -62, Hull: 250, Radius: 8, Weapons: [Weapons.Flak]),
                new(HardpointRole.Turret, -70, 62, Hull: 250, Radius: 8, Weapons: [Weapons.Flak]),
                new(HardpointRole.Hangar, -10, -36, Hull: 600, Radius: 11, Hangar: CarrierHangar with { Interval = 16 }),
                new(HardpointRole.Hangar, -10, 36, Hull: 600, Radius: 11, Hangar: CarrierHangar with { Interval = 16 }),
                new(HardpointRole.Shield, -50, -22, Hull: 500, Radius: 10),
                new(HardpointRole.Shield, -50, 22, Hull: 500, Radius: 10),
                new(HardpointRole.Shield, 10, 0, Hull: 500, Radius: 10),
                new(HardpointRole.Engine, -124, -48),
                new(HardpointRole.Engine, -130, 0),
                new(HardpointRole.Engine, -124, 48),
            ],
            Ability: AbilityId.ShieldDome, Extra: [AbilityId.OrbitalStrike, AbilityId.Emp, AbilityId.Launch], Limit: 1),
        // The ultimate ship, at the price of a whole fleet; the Errants' one is a raid boss,
        // guarding a corner of the map for whoever can bring it down. Its four shield generators
        // are the way in: with them gone, its shields are gone.
        [ShipClass.Colossus] = new(ShipClass.Colossus, "Colosse", SizeClass.Large,
            Hull: 30000, Shield: 16000, ShieldRegen: 25, Speed: 16, Accel: 12, TurnRate: 0.15, Radius: 170,
            FromHangar: false, Cost: 60000, BuildTime: 300, MinStationLevel: 4,
            Weapons: [Weapons.Flak, Weapons.Flak, Weapons.Flak, Weapons.Flak],
            Hardpoints: [
                new(HardpointRole.HeavyTurret, 120, -40, Hull: 900, Radius: 14, Weapons: [ColossusRay]),
                new(HardpointRole.HeavyTurret, 120, 40, Hull: 900, Radius: 14, Weapons: [ColossusRay]),
                new(HardpointRole.HeavyTurret, 60, 0, Hull: 900, Radius: 14, Weapons: [ColossusRay]),
                new(HardpointRole.HeavyTurret, -20, -100, Hull: 900, Radius: 14, Weapons: [ColossusRay]),
                new(HardpointRole.HeavyTurret, -20, 100, Hull: 900, Radius: 14, Weapons: [ColossusRay]),
                new(HardpointRole.Turret, 40, -110, Hull: 450, Radius: 10, Weapons: [Weapons.Laser with { Range = 520, Damage = 30 }]),
                new(HardpointRole.Turret, 40, 110, Hull: 450, Radius: 10, Weapons: [Weapons.Laser with { Range = 520, Damage = 30 }]),
                new(HardpointRole.Turret, -70, -125, Hull: 450, Radius: 10, Weapons: [Weapons.Laser with { Range = 520, Damage = 30 }]),
                new(HardpointRole.Turret, -70, 125, Hull: 450, Radius: 10, Weapons: [Weapons.Laser with { Range = 520, Damage = 30 }]),
                new(HardpointRole.Turret, -110, -70, Hull: 350, Radius: 9, Weapons: [Weapons.Flak]),
                new(HardpointRole.Turret, -110, 70, Hull: 350, Radius: 9, Weapons: [Weapons.Flak]),
                new(HardpointRole.Hangar, -40, -50, Hull: 800, Radius: 12, Hangar: CarrierHangar with { Cap = 12, Interval = 18 }),
                new(HardpointRole.Hangar, -40, 50, Hull: 800, Radius: 12, Hangar: CarrierHangar with { Cap = 12, Interval = 18 }),
                new(HardpointRole.Shield, -130, -30, Hull: 1200, Radius: 12),
                new(HardpointRole.Shield, -130, 30, Hull: 1200, Radius: 12),
                new(HardpointRole.Shield, 10, -45, Hull: 1200, Radius: 12),
                new(HardpointRole.Shield, 10, 45, Hull: 1200, Radius: 12),
                new(HardpointRole.Engine, -165, -60),
                new(HardpointRole.Engine, -170, -20),
                new(HardpointRole.Engine, -170, 20),
                new(HardpointRole.Engine, -165, 60),
            ],
            Ability: AbilityId.Emp, Extra: [AbilityId.OrbitalStrike, AbilityId.ShieldDome, AbilityId.Launch], Limit: 1),
    };

    /// <summary>Price of one order (a ship, or a squadron) for a faction, after its trait.</summary>
    public static double Cost(ShipClass cls, Faction faction) => Js.Round(Defs[cls].Cost * Factions.Trait(faction).Cost);

    /// <summary>Value of a single ship, for strength estimates.</summary>
    public static double UnitValue(ShipClass cls) => Defs[cls].Cost / (Defs[cls].Squad ?? 1);

    /// <summary>Display name of an order: "3× Éclaireur" for a squadron.</summary>
    public static string OrderName(ShipClass cls) =>
        Defs[cls].Squad is int squad ? $"{squad}× {Defs[cls].Name}" : Defs[cls].Name;
}
