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
    // Not in the web version either: a siege gun against defenses, an escort against craft and
    // missiles, a jammer, and a cruiser the enemy only sees up close or when it fires. Last, so
    // that the classes before them keep their numbers.
    Artillery, Corvette, Jammer, Stealth,
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
    int? Limit = null,
    // Half the width of a long hull, a capsule along its heading (Radius long either side of its
    // middle); 0 for a round one. Not in the web version.
    double Beam = 0)
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
        // +15 % and −10 % (+10 % and −15 % in the web version): the Kla'ed won a quarter of the
        // zone control games and a third of the regicides, where holding on decides.
        [Faction.Klaed] = new("Kla'ed", 0xe0563b, new("Assaut", "Armes +15 %, boucliers −10 %", Damage: 1.15, Shield: 0.9)),
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
    // 9 damage and 4 s (7 and 5 at first): at equal cost a titan did no better against small
    // craft with its swarms than with the lasers they replaced, and far worse against the rest.
    public static readonly WeaponDef Swarm = new(WeaponKind.Missile, 420, 9, 4, 420, Homing: true, Burst: 6, BurstGap: 0.08,
        Mult: new SizeMult(Small: 2, Medium: 0.8, Large: 0.4, Structure: 0.3));
    /// <summary>Ion bolts: twice their damage to shields, a scratch on the hull; they open the way for the rest.</summary>
    public static readonly WeaponDef Ion = new(WeaponKind.Ion, 440, 20, 1.6, 800, ShieldMult: 2, HullMult: 0.2);

    public static double MaxRange(IEnumerable<WeaponDef> weapons) => weapons.Aggregate(0.0, (m, w) => Math.Max(m, w.Range));

    /// <summary>
    /// Bullets faster than this come from a burst cannon (850), slower ones from flak and light guns
    /// (700 at most): the view tells their sound apart by it, all a player over the network knows of
    /// a shot. A test keeps the weapons on either side of it.
    /// </summary>
    public const double BurstRoundSpeed = 800;
}

public static class Ships
{
    static readonly WeaponDef HeavyLaser = Weapons.Laser with { Range = 450, Damage = 22 };
    static readonly WeaponDef HeavyBurst = Weapons.Burst with { Range = 450, Damage = 14 };
    static readonly WeaponDef LeviathanRay = new(WeaponKind.Ray, 700, 90, 2.5, 1100, Mult: new SizeMult(Small: 0.2));
    static readonly WeaponDef ColossusRay = new(WeaponKind.Ray, 800, 110, 2.5, 1200, Mult: new SizeMult(Small: 0.25));
    /// <summary>A carrier's hangar: fighters and bombers in turn, many and often.</summary>
    static readonly HangarDef CarrierHangar = new([new(ShipClass.Fighter, 4), new(ShipClass.Bomber, 3)], Cap: 10, Interval: 14);
    /// <summary>Siege shells: slow, long-reaching, shot down by point defense; they crack defenses and barely touch small craft.</summary>
    static readonly WeaponDef SiegeShell = new(WeaponKind.Torpedo, 950, 120, 6, 380,
        Mult: new SizeMult(Small: 0.1, Medium: 0.5, Large: 1, Structure: 2.5));


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
            Hull: 3200, Shield: 1800, ShieldRegen: 12, Speed: 55, Accel: 40, TurnRate: 0.6, Radius: 50,
            FromHangar: false, Cost: 2600, BuildTime: 40, MinStationLevel: 2,
            Weapons: [Weapons.Flak],
            Ability: AbilityId.Taunt),
        // A carrier: next to no guns, but two big hangars that keep launching.
        [ShipClass.Carrier] = new(ShipClass.Carrier, "Transporteur", SizeClass.Large,
            Hull: 1600, Shield: 700, ShieldRegen: 6, Speed: 50, Accel: 40, TurnRate: 0.55, Radius: 52,
            FromHangar: false, Cost: 3600, BuildTime: 50, MinStationLevel: 2,
            Weapons: [],
            // Flak on the flight deck's bow, the hangars' mouths at the bows of the two hangar hulls,
            // the shield generator on the island.
            Hardpoints: [
                new(HardpointRole.Turret, 50, 0, Hull: 250, Radius: 8, Weapons: [Weapons.Flak]),
                new(HardpointRole.Hangar, 32, -28, Hull: 450, Radius: 10, Hangar: CarrierHangar),
                new(HardpointRole.Hangar, 32, 28, Hull: 450, Radius: 10, Hangar: CarrierHangar),
                new(HardpointRole.Shield, -35, 0, Hull: 350, Radius: 8),
                new(HardpointRole.Engine, -55, -28),
                new(HardpointRole.Engine, -55, 28),
            ],
            Ability: AbilityId.Launch),
        // A fifth bigger than in the web version (radius 56), its parts moved and grown with it.
        [ShipClass.Dreadnought] = new(ShipClass.Dreadnought, "Cuirassé", SizeClass.Large,
            Hull: 2800, Shield: 1500, ShieldRegen: 8, Speed: 45, Accel: 35, TurnRate: 0.45, Radius: 68,
            FromHangar: false, Cost: 4800, BuildTime: 60, MinStationLevel: 3,
            Weapons: [],
            Hardpoints: [
                new(HardpointRole.HeavyTurret, 41, 0, Hull: 400, Radius: 13,
                    Weapons: [new(WeaponKind.Ray, 560, 60, 2.5, 1100, Mult: new SizeMult(Small: 0.2))]),
                new(HardpointRole.Turret, 12, -29, Hull: 250, Radius: 10, Weapons: [HeavyLaser]),
                // An ion cannon in this version, a second heavy laser in the web one.
                new(HardpointRole.Turret, 12, 29, Hull: 250, Radius: 10, Weapons: [Weapons.Ion]),
                new(HardpointRole.Turret, -19, -27, Hull: 200, Radius: 8.5, Weapons: [Weapons.Flak]),
                new(HardpointRole.Turret, -19, 27, Hull: 200, Radius: 8.5, Weapons: [Weapons.Flak]),
                new(HardpointRole.Hangar, -10, 0, Hull: 400, Radius: 12,
                    Hangar: new([new(ShipClass.Fighter, 3)], Cap: 6, Interval: 25)),
                new(HardpointRole.Shield, -39, 0, Hull: 350, Radius: 11),
                new(HardpointRole.Engine, -70, -19),
                new(HardpointRole.Engine, -70, 19),
            ],
            Ability: AbilityId.Broadside, Extra: [AbilityId.Overcharge]),
        [ShipClass.Titan] = new(ShipClass.Titan, "Titan", SizeClass.Large,
            Hull: 6000, Shield: 3500, ShieldRegen: 12, Speed: 32, Accel: 25, TurnRate: 0.3, Radius: 152,
            FromHangar: false, Cost: 14000, BuildTime: 120, MinStationLevel: 3,
            // Flak in the hull: fewer parts on screen, the hull's drawing shows through.
            Weapons: [Weapons.Flak, Weapons.Flak],
            // Long and narrow in this version (a capsule, radius 84 and round in the web one): the
            // two rays down the keel forward, lasers and swarm missiles on the side hulls aft.
            Hardpoints: [
                new(HardpointRole.HeavyTurret, 134, 0, Hull: 500, Radius: 12,
                    Weapons: [new(WeaponKind.Ray, 600, 70, 2.5, 1100, Mult: new SizeMult(Small: 0.2))]),
                new(HardpointRole.HeavyTurret, 83, 0, Hull: 500, Radius: 12,
                    Weapons: [new(WeaponKind.Ray, 600, 70, 2.5, 1100, Mult: new SizeMult(Small: 0.2))]),
                new(HardpointRole.Turret, -15, -33, Hull: 300, Radius: 9, Weapons: [Weapons.Laser with { Range = 470, Damage = 24 }]),
                new(HardpointRole.Turret, -15, 33, Hull: 300, Radius: 9, Weapons: [Weapons.Laser with { Range = 470, Damage = 24 }]),
                new(HardpointRole.Turret, -119, -33, Hull: 300, Radius: 9, Weapons: [Weapons.Swarm]),
                new(HardpointRole.Turret, -119, 33, Hull: 300, Radius: 9, Weapons: [Weapons.Swarm]),
                new(HardpointRole.Hangar, -100, 0, Hull: 500, Radius: 11,
                    Hangar: new([new(ShipClass.Fighter, 3), new(ShipClass.Bomber, 2)], Cap: 8, Interval: 20)),
                new(HardpointRole.Shield, -140, -12, Hull: 400, Radius: 9),
                new(HardpointRole.Shield, -140, 12, Hull: 400, Radius: 9),
                new(HardpointRole.Engine, -169, -33),
                new(HardpointRole.Engine, -183, 0),
                new(HardpointRole.Engine, -169, 33),
            ],
            Ability: AbilityId.Spinal, Extra: [AbilityId.Broadside, AbilityId.Launch], Limit: 2, Beam: 40),
        // The tier above the titan: three dreadnought hulls in one, heavy guns, two hangars,
        // and a shield dome over the fleet around it.
        // A true capital ship: a long, narrow hull bristling with guns along its whole length, a
        // spinal battery forward, broadsides down both flanks, slow to get going and slower to turn.
        [ShipClass.Leviathan] = new(ShipClass.Leviathan, "Léviathan", SizeClass.Large,
            Hull: 12000, Shield: 7000, ShieldRegen: 20, Speed: 19, Accel: 11, TurnRate: 0.11, Radius: 208,
            FromHangar: false, Cost: 32000, BuildTime: 200, MinStationLevel: 4,
            Weapons: [Weapons.Flak, Weapons.Flak, Weapons.Flak],
            Hardpoints: [
                // The spinal battery, down the keel; one more astern of the bridge.
                new(HardpointRole.HeavyTurret, 179, 0, Hull: 700, Radius: 13, Weapons: [LeviathanRay]),
                new(HardpointRole.HeavyTurret, 124, 0, Hull: 700, Radius: 13, Weapons: [LeviathanRay]),
                new(HardpointRole.HeavyTurret, 68, 0, Hull: 700, Radius: 13, Weapons: [LeviathanRay]),
                new(HardpointRole.HeavyTurret, -134, 0, Hull: 700, Radius: 13, Weapons: [LeviathanRay]),
                // The broadsides, on the flank hulls aft of the bow: lasers forward, burst cannons aft.
                new(HardpointRole.Turret, 41, -62, Hull: 300, Radius: 9, Weapons: [Weapons.Laser with { Range = 480, Damage = 22 }]),
                new(HardpointRole.Turret, 41, 62, Hull: 300, Radius: 9, Weapons: [Weapons.Laser with { Range = 480, Damage = 22 }]),
                new(HardpointRole.Turret, -5, -62, Hull: 300, Radius: 9, Weapons: [Weapons.Laser with { Range = 480, Damage = 22 }]),
                new(HardpointRole.Turret, -5, 62, Hull: 300, Radius: 9, Weapons: [Weapons.Laser with { Range = 480, Damage = 22 }]),
                new(HardpointRole.Turret, -74, -62, Hull: 300, Radius: 9, Weapons: [HeavyBurst]),
                new(HardpointRole.Turret, -74, 62, Hull: 300, Radius: 9, Weapons: [HeavyBurst]),
                new(HardpointRole.Turret, -124, -62, Hull: 300, Radius: 9, Weapons: [HeavyBurst]),
                new(HardpointRole.Turret, -124, 62, Hull: 300, Radius: 9, Weapons: [HeavyBurst]),
                // Flak at both ends, against craft.
                new(HardpointRole.Turret, 221, -22, Hull: 220, Radius: 8, Weapons: [Weapons.Flak]),
                new(HardpointRole.Turret, 221, 22, Hull: 220, Radius: 8, Weapons: [Weapons.Flak]),
                new(HardpointRole.Turret, -177, -62, Hull: 220, Radius: 8, Weapons: [Weapons.Flak]),
                new(HardpointRole.Turret, -177, 62, Hull: 220, Radius: 8, Weapons: [Weapons.Flak]),
                new(HardpointRole.Hangar, -40, -35, Hull: 600, Radius: 11, Hangar: CarrierHangar with { Interval = 16 }),
                new(HardpointRole.Hangar, -40, 35, Hull: 600, Radius: 11, Hangar: CarrierHangar with { Interval = 16 }),
                new(HardpointRole.Shield, 22, 0, Hull: 500, Radius: 10),
                new(HardpointRole.Shield, -189, -26, Hull: 500, Radius: 10),
                new(HardpointRole.Shield, -189, 26, Hull: 500, Radius: 10),
                new(HardpointRole.Engine, -257, 0),
                new(HardpointRole.Engine, -221, -62),
                new(HardpointRole.Engine, -221, 62),
            ],
            Ability: AbilityId.ShieldDome, Extra: [AbilityId.OrbitalStrike, AbilityId.Emp, AbilityId.Launch], Limit: 1, Beam: 70),
        // The ultimate ship, at the price of a whole fleet; the Errants' one is a raid boss,
        // guarding a corner of the map for whoever can bring it down. Its four shield generators
        // are the way in: with them gone, its shields are gone.
        [ShipClass.Colossus] = new(ShipClass.Colossus, "Colosse", SizeClass.Large,
            Hull: 30000, Shield: 16000, ShieldRegen: 25, Speed: 16, Accel: 12, TurnRate: 0.15, Radius: 248,
            FromHangar: false, Cost: 60000, BuildTime: 300, MinStationLevel: 4,
            Weapons: [Weapons.Flak, Weapons.Flak, Weapons.Flak, Weapons.Flak],
            // A trimaran: a keel between two outer hulls. Rays on the fork of its bow, on its keel and
            // on the outer hulls, lasers and flak down them, hangars by the keel, its four shield
            // generators fore and aft (the raid boss's way in).
            Hardpoints: [
                new(HardpointRole.HeavyTurret, 232, -45, Hull: 900, Radius: 14, Weapons: [ColossusRay]),
                new(HardpointRole.HeavyTurret, 232, 45, Hull: 900, Radius: 14, Weapons: [ColossusRay]),
                new(HardpointRole.HeavyTurret, 154, 0, Hull: 900, Radius: 14, Weapons: [ColossusRay]),
                new(HardpointRole.HeavyTurret, 14, -80, Hull: 900, Radius: 14, Weapons: [ColossusRay]),
                new(HardpointRole.HeavyTurret, 14, 80, Hull: 900, Radius: 14, Weapons: [ColossusRay]),
                new(HardpointRole.Turret, 87, -80, Hull: 450, Radius: 10, Weapons: [Weapons.Laser with { Range = 520, Damage = 30 }]),
                new(HardpointRole.Turret, 87, 80, Hull: 450, Radius: 10, Weapons: [Weapons.Laser with { Range = 520, Damage = 30 }]),
                new(HardpointRole.Turret, -79, -80, Hull: 450, Radius: 10, Weapons: [Weapons.Laser with { Range = 520, Damage = 30 }]),
                new(HardpointRole.Turret, -79, 80, Hull: 450, Radius: 10, Weapons: [Weapons.Laser with { Range = 520, Damage = 30 }]),
                new(HardpointRole.Turret, -212, -80, Hull: 350, Radius: 9, Weapons: [Weapons.Flak]),
                new(HardpointRole.Turret, -212, 80, Hull: 350, Radius: 9, Weapons: [Weapons.Flak]),
                new(HardpointRole.Hangar, -139, -27, Hull: 800, Radius: 12, Hangar: CarrierHangar with { Cap = 12, Interval = 18 }),
                new(HardpointRole.Hangar, -139, 27, Hull: 800, Radius: 12, Hangar: CarrierHangar with { Cap = 12, Interval = 18 }),
                new(HardpointRole.Shield, -252, -24, Hull: 1200, Radius: 12),
                new(HardpointRole.Shield, -252, 24, Hull: 1200, Radius: 12),
                new(HardpointRole.Shield, 67, -24, Hull: 1200, Radius: 12),
                new(HardpointRole.Shield, 67, 24, Hull: 1200, Radius: 12),
                new(HardpointRole.Engine, -271, -80),
                new(HardpointRole.Engine, -300, -16),
                new(HardpointRole.Engine, -300, 16),
                new(HardpointRole.Engine, -271, 80),
            ],
            Ability: AbilityId.Emp, Extra: [AbilityId.OrbitalStrike, AbilityId.ShieldDome, AbilityId.Launch], Limit: 1, Beam: 78),
        // The four classes added together (not in the web version).
        // Outranges every defense but the ion cannon, at the price of being helpless up close.
        [ShipClass.Artillery] = new(ShipClass.Artillery, "Artillerie", SizeClass.Medium,
            Hull: 500, Shield: 150, ShieldRegen: 3, Speed: 60, Accel: 50, TurnRate: 0.9, Radius: 26,
            FromHangar: false, Cost: 1400, BuildTime: 28, MinStationLevel: 2,
            Weapons: [SiegeShell], Ability: AbilityId.Barrage),
        // Two to an order, quick, flak and point defense: the fleet's screen against craft, torpedoes and shells.
        [ShipClass.Corvette] = new(ShipClass.Corvette, "Corvette", SizeClass.Small,
            Hull: 220, Shield: 80, ShieldRegen: 3, Speed: 190, Accel: 220, TurnRate: 3, Radius: 14,
            FromHangar: false, Cost: 450, BuildTime: 14, MinStationLevel: 1,
            Weapons: [Weapons.Flak with { Range = 300, Damage = 5 }, Weapons.Flak with { Range = 300, Damage = 5 }],
            Ability: AbilityId.Salvo, Squad: 2),
        // Hardly armed: its jamming cuts the enemy's reach and stops its shields recharging.
        [ShipClass.Jammer] = new(ShipClass.Jammer, "Brouilleur", SizeClass.Medium,
            Hull: 320, Shield: 180, ShieldRegen: 4, Speed: 115, Accel: 110, TurnRate: 1.8, Radius: 20,
            FromHangar: false, Cost: 900, BuildTime: 22, MinStationLevel: 2,
            Weapons: [Weapons.Ion with { Range = 400, Damage = 10 }], Ability: AbilityId.Jam),
        // Unseen by the enemy beyond a short reach until it fires (Vision.cs); it hunts mines and outposts.
        [ShipClass.Stealth] = new(ShipClass.Stealth, "Croiseur furtif", SizeClass.Medium,
            Hull: 550, Shield: 250, ShieldRegen: 4, Speed: 140, Accel: 120, TurnRate: 1.4, Radius: 24,
            FromHangar: false, Cost: 1300, BuildTime: 26, MinStationLevel: 2,
            Weapons: [Weapons.Burst with { Range = 360, Damage = 12, Mult = new SizeMult(Small: 0.5, Medium: 1.2, Structure: 1.6) }],
            Ability: AbilityId.Cloak),
    };

    /// <summary>Price of one order (a ship, or a squadron) for a faction, after its trait.</summary>
    public static double Cost(ShipClass cls, Faction faction) => Js.Round(Defs[cls].Cost * Factions.Trait(faction).Cost);

    /// <summary>Value of a single ship, for strength estimates.</summary>
    public static double UnitValue(ShipClass cls) => Defs[cls].Cost / (Defs[cls].Squad ?? 1);

    /// <summary>Display name of an order: "3× Éclaireur" for a squadron.</summary>
    public static string OrderName(ShipClass cls) =>
        Defs[cls].Squad is int squad ? $"{squad}× {Defs[cls].Name}" : Defs[cls].Name;
}
