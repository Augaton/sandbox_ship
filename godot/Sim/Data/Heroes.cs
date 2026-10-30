// Heroes: unique, expensive, much stronger than the ship they are built on, each with a power
// of their own. Each faction has three. Port of src/data/heroes.ts.
namespace SandboxShip.Sim.Data;

public enum HeroRole { Admiral, Ace, Engineer }

/// <summary>A hero is its faction and its role; written "klaed-admiral" as in the web saves.</summary>
public readonly record struct HeroId(Faction Faction, HeroRole Role)
{
    public override string ToString() => $"{Faction.ToString().ToLowerInvariant()}-{Role.ToString().ToLowerInvariant()}";
}

public sealed record HeroDef(
    HeroId Id, string Name, string Title, string Description,
    // Ship class the hero flies: sprite, size, weapons and hardpoints.
    ShipClass Base,
    // Multipliers over the base class.
    double Hull, double Shield, double Damage, double Speed,
    AbilityId Ability, double Cost, double BuildTime, int MinStationLevel,
    // Seconds before a fallen hero can be built again.
    double Respawn);

public static class Heroes
{
    static readonly IReadOnlyDictionary<HeroRole, HeroDef> Roles = new Dictionary<HeroRole, HeroDef>
    {
        [HeroRole.Admiral] = Role(HeroRole.Admiral, ShipClass.Dreadnought, hull: 1.6, shield: 1.5, damage: 1.4, speed: 1.1, cost: 5000, buildTime: 60, minLevel: 3, respawn: 180),
        [HeroRole.Ace] = Role(HeroRole.Ace, ShipClass.Scout, hull: 12, shield: 15, damage: 5, speed: 1.3, cost: 1500, buildTime: 25, minLevel: 1, respawn: 90),
        [HeroRole.Engineer] = Role(HeroRole.Engineer, ShipClass.Support, hull: 3.5, shield: 4, damage: 2, speed: 1.2, cost: 1800, buildTime: 30, minLevel: 2, respawn: 120),
    };

    static HeroDef Role(HeroRole role, ShipClass baseClass, double hull, double shield, double damage, double speed, double cost, double buildTime, int minLevel, double respawn) =>
        new(new HeroId(Faction.Klaed, role), "", "", "", baseClass, hull, shield, damage, speed, AbilityId.Sprint, cost, buildTime, minLevel, respawn);

    static HeroDef Hero(Faction faction, HeroRole role, AbilityId ability, string name, string title, string description, Func<HeroDef, HeroDef>? tweak = null)
    {
        var def = Roles[role] with { Id = new HeroId(faction, role), Ability = ability, Name = name, Title = title, Description = description };
        return tweak?.Invoke(def) ?? def;
    }

    // Admirals also command: allies around them deal more damage and fly faster.
    public static readonly IReadOnlyList<HeroDef> All =
    [
        Hero(Faction.Klaed, HeroRole.Admiral, AbilityId.OrbitalStrike, "Vorrak", "Amiral", "Cuirassé de commandement aux canons renforcés. Appelle un bombardement orbital.", d => d with { Damage = 1.7 }),
        Hero(Faction.Klaed, HeroRole.Ace, AbilityId.HuntMark, "Kesh", "As", "Éclaireur d'élite. Marque une proie que toute la flotte achève."),
        Hero(Faction.Klaed, HeroRole.Engineer, AbilityId.FieldTurret, "Ondra", "Ingénieure", "Répare en continu et déploie des tourelles en pleine bataille."),
        Hero(Faction.Nairan, HeroRole.Admiral, AbilityId.ShieldDome, "Selis Vaun", "Amirale", "Cuirassé aux boucliers presque impénétrables. Couvre sa flotte d'un dôme.", d => d with { Shield = 2.2 }),
        Hero(Faction.Nairan, HeroRole.Ace, AbilityId.Cloak, "Ilo Fenn", "As", "Pilote de chasse plus rapide que tout. Disparaît pour frapper par surprise.", d => d with { Speed = 1.5 }),
        Hero(Faction.Nairan, HeroRole.Engineer, AbilityId.Emp, "Tavi Qor", "Ingénieur", "Spécialiste des boucliers : son impulsion paralyse les vaisseaux ennemis."),
        Hero(Faction.Nautolan, HeroRole.Admiral, AbilityId.Swarm, "Ysh'ra", "Mère-nef", "Cuirassé vivant, à la coque immense, qui enfante des essaims.", d => d with { Hull = 2.2 }),
        Hero(Faction.Nautolan, HeroRole.Ace, AbilityId.Spores, "Kael", "Rôdeur", "Chasseur organique et tenace. Ses spores rongent les coques.", d => d with { Hull = 16 }),
        Hero(Faction.Nautolan, HeroRole.Engineer, AbilityId.Graft, "Om", "Greffeur", "Fait repousser les coques de toute une flotte."),
    ];

    public static readonly IReadOnlyDictionary<HeroId, HeroDef> Defs = All.ToDictionary(h => h.Id);

    public static IEnumerable<HeroDef> Of(Faction faction) => All.Where(h => h.Id.Faction == faction);
}
