// Active abilities: one per ship class and one per hero, used by the player or, when
// automatic, by the faction itself. Port of src/data/abilities.ts.
namespace SandboxShip.Sim.Data;

public enum AbilityId
{
    Sprint, Repair, Salvo, Overcharge, Barrage, Broadside, Spinal,
    // Not in the web version: the bulwark's and the carrier's.
    Taunt, Launch,
    // Heroes only: one each.
    OrbitalStrike, HuntMark, FieldTurret,
    ShieldDome, Cloak, Emp,
    Swarm, Spores, Graft,
    // Not in the web version: the star lance's blow.
    StarLance,
}

/// <summary>What the player picks to use it: nothing, a spot on the map, or an enemy.</summary>
public enum AbilityTarget { Self, Point, Enemy }

public sealed record AbilityDef(
    AbilityId Id, string Name, string Description, double Cooldown,
    // Seconds the effect lasts; 0 for an instant effect.
    double Duration,
    AbilityTarget Target,
    // Color of its marks on the battlefield.
    uint Color,
    // How far from the ship a point or an enemy may be picked.
    double? Range = null,
    // Radius of the area it covers.
    double? Radius = null);

public static class Abilities
{
    const uint Gold = 0xe8a13a;
    const uint Red = 0xe05a4a;
    const uint Green = 0x6fd08c;
    const uint Blue = 0x5aa9e6;
    const uint Star = 0xfff2d0;

    /// <summary>Admirals' passive command: allies this close deal more damage and fly faster.</summary>
    public const double CommandRadius = 500;
    /// <summary>Seconds that abilities' effects last on their targets.</summary>
    public const double Marked = 12, Stunned = 4, Corroded = 8, Taunted = 8;
    /// <summary>Delay before the spinal beam fires, and before an orbital strike lands.</summary>
    public const double SpinalCharge = 2, OrbitalDelay = 2.5;
    /// <summary>The star lance's blow: seconds of warning, seen by all, then its damage over its whole radius.</summary>
    public const double StarLanceDelay = 5, StarLanceDamage = 9000;

    public static readonly IReadOnlyDictionary<AbilityId, AbilityDef> Defs = new Dictionary<AbilityId, AbilityDef>
    {
        [AbilityId.Sprint] = new(AbilityId.Sprint, "Postcombustion", "Vitesse doublée pendant 5 s.", 20, 5, AbilityTarget.Self, Blue),
        [AbilityId.Repair] = new(AbilityId.Repair, "Réparation d'urgence", "Répare de 25 % les alliés proches.", 40, 0, AbilityTarget.Self, Green, Radius: 260),
        [AbilityId.Salvo] = new(AbilityId.Salvo, "Salve", "Cadence de tir triplée pendant 6 s.", 30, 6, AbilityTarget.Self, Gold),
        [AbilityId.Overcharge] = new(AbilityId.Overcharge, "Surcharge des boucliers", "Boucliers pleins, recharge ×4 pendant 8 s.", 35, 8, AbilityTarget.Self, Blue),
        [AbilityId.Barrage] = new(AbilityId.Barrage, "Tir de barrage", "Dégâts +50 % pendant 8 s.", 40, 8, AbilityTarget.Self, Red),
        [AbilityId.Broadside] = new(AbilityId.Broadside, "Bordée", "Toutes les tourelles rechargées, dégâts ×2 pendant 5 s.", 45, 5, AbilityTarget.Self, Red),
        [AbilityId.Spinal] = new(AbilityId.Spinal, "Rayon spinal",
            "Charge 2 s, puis un rayon de 1200 de long frappe tout ce qui est sur sa ligne (900 dégâts).", 50, 0, AbilityTarget.Point, Gold, Range: 1200),
        [AbilityId.Taunt] = new(AbilityId.Taunt, "Provocation",
            "Les ennemis à 650 ou moins le prennent pour cible pendant 8 s (sauf ordre d'attaque explicite).", 30, 8, AbilityTarget.Self, Red, Radius: 650),
        [AbilityId.Launch] = new(AbilityId.Launch, "Catapultes",
            "Ses hangars lancent trois fois plus vite pendant 12 s.", 40, 12, AbilityTarget.Self, Blue),
        [AbilityId.OrbitalStrike] = new(AbilityId.OrbitalStrike, "Bombardement orbital",
            "Après 2,5 s, une zone explose : 650 dégâts à tous les ennemis dedans. Le délai laisse le temps de fuir.", 45, 0, AbilityTarget.Point, Red, Range: 1000, Radius: 220),
        [AbilityId.HuntMark] = new(AbilityId.HuntMark, "Marque du chasseur", "La cible subit +50 % de dégâts de toutes parts pendant 12 s.", 25, 0, AbilityTarget.Enemy, Red, Range: 700),
        [AbilityId.FieldTurret] = new(AbilityId.FieldTurret, "Tourelle de campagne", "Déploie une plateforme de défense armée pendant 45 s.", 50, 0, AbilityTarget.Point, Gold, Range: 300),
        [AbilityId.ShieldDome] = new(AbilityId.ShieldDome, "Dôme de bouclier", "Les alliés sous le dôme subissent 50 % de dégâts en moins pendant 10 s.", 50, 10, AbilityTarget.Self, Blue, Radius: 380),
        [AbilityId.Cloak] = new(AbilityId.Cloak, "Camouflage", "Invisible et intouchable pendant 8 s, dégâts ×2 en embuscade.", 30, 8, AbilityTarget.Self, Blue),
        [AbilityId.Emp] = new(AbilityId.Emp, "Impulsion IEM", "Les ennemis proches perdent leurs boucliers et restent paralysés 4 s.", 45, 0, AbilityTarget.Self, Blue, Radius: 350),
        [AbilityId.Swarm] = new(AbilityId.Swarm, "Essaim", "Lâche 8 chasseurs organiques qui vivent 30 s.", 50, 0, AbilityTarget.Self, Green),
        [AbilityId.Spores] = new(AbilityId.Spores, "Spores corrosives", "Ronge la coque de la cible, à travers les boucliers, pendant 8 s.", 30, 0, AbilityTarget.Enemy, Green, Range: 600),
        [AbilityId.Graft] = new(AbilityId.Graft, "Greffe", "Les alliés proches régénèrent 3 % de leur coque par seconde pendant 10 s.", 50, 10, AbilityTarget.Self, Green, Radius: 420),
        [AbilityId.StarLance] = new(AbilityId.StarLance, "Frappe stellaire",
            "Après 5 s d'alerte visible de tous, un rayon venu des étoiles frappe n'importe quel point de la carte : 9 000 dégâts dans un grand rayon.",
            240, 0, AbilityTarget.Point, Star, Range: 1e6, Radius: 420),
    };
}
