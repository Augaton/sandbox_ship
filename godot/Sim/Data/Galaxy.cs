// The galaxy map: planets and hyperspace lanes. The east half mirrors the west half
// (x -> W - x, y -> H - y) so both capitals start on equal terms. Port of src/data/galaxy.ts.
namespace SandboxShip.Sim.Data;

public enum PlanetSpecial
{
    Rich, Shipyard, Fortress, Archive,
    // Not in the web version: generated galaxies only, drawn apart from the web's (Galaxies.AddNewSpecials).
    Nebula, Relay, Ruins,
}

public sealed record SpecialDef(string Name, string Description);

public sealed record PlanetDef(string Name, double X, double Y,
    // Credits per second while owned.
    double Income, int MineSlots, PlanetSpecial? Special = null);

/// <summary>A hyperspace lane between two planets, by index.</summary>
public readonly record struct Lane(int A, int B);

public static class GalaxyMap
{
    public const double W = 1600;
    public const double H = 1000;

    public static readonly IReadOnlyDictionary<PlanetSpecial, SpecialDef> Specials = new Dictionary<PlanetSpecial, SpecialDef>
    {
        [PlanetSpecial.Rich] = new("Gisement riche", "Les mines rapportent le double."),
        [PlanetSpecial.Shipyard] = new("Chantier orbital", "Les vaisseaux sont construits 30 % plus vite."),
        [PlanetSpecial.Fortress] = new("Forteresse", "Deux plateformes de défense de plus."),
        [PlanetSpecial.Archive] = new("Archives anciennes", "Tant que la planète est à toi, la recherche va 25 % plus vite."),
        [PlanetSpecial.Nebula] = new("Nébuleuse", "Les flottes qui s'y trouvent sont invisibles pour leurs ennemis."),
        [PlanetSpecial.Relay] = new("Relais hyperspatial", "Les flottes de son propriétaire en repartent 50 % plus vite, jusqu'à la planète suivante."),
        [PlanetSpecial.Ruins] = new("Ruines anciennes", "La première faction à prendre la planète y trouve 1 500 crédits et un niveau de recherche."),
    };

    static readonly PlanetDef[] West =
    [
        new("Vesper", 150, 500, 6, 3),
        new("Orin", 330, 260, 3, 2, PlanetSpecial.Archive),
        new("Maru", 330, 740, 3, 2),
        new("Kaldra", 520, 470, 4, 2, PlanetSpecial.Shipyard),
        new("Sorel", 600, 150, 2, 1, PlanetSpecial.Fortress),
        new("Thessa", 640, 820, 2, 1),
        new("Nadir", 800, 330, 5, 3, PlanetSpecial.Rich),
    ];
    static readonly string[] EastNames = ["Ostra", "Pélion", "Yvra", "Tarsis", "Brume", "Ixion", "Calyx"];

    /// <summary>Planet i of the west half mirrors planet i + 7. Nadir and Calyx mirror each other.</summary>
    public static readonly IReadOnlyList<PlanetDef> Planets =
        [.. West, .. West.Select((p, i) => p with { Name = EastNames[i], X = W - p.X, Y = H - p.Y })];

    static readonly Lane[] WestLanes =
    [
        new(0, 1), new(0, 2), new(1, 3), new(2, 3), new(1, 4), new(2, 5), new(3, 6), new(3, 13), new(4, 6), new(5, 13), new(6, 13), new(4, 12),
    ];

    static int Mirror(int i) => (i + 7) % 14;

    /// <summary>Each west lane and its mirror image, without duplicates.</summary>
    public static readonly IReadOnlyList<Lane> Lanes = BuildLanes();

    static List<Lane> BuildLanes()
    {
        var seen = new HashSet<(int, int)>();
        var out_ = new List<Lane>();
        foreach (var l in WestLanes.SelectMany(l => new[] { l, new Lane(Mirror(l.A), Mirror(l.B)) }))
        {
            if (seen.Add(l.A < l.B ? (l.A, l.B) : (l.B, l.A))) out_.Add(l);
        }
        return out_;
    }

    /// <summary>Index of each side's capital in Planets.</summary>
    public static readonly IReadOnlyList<int> Capitals = [0, 7];
}

public static class GalaxyRules
{
    public const double StartCredits = 2000;
    /// <summary>Galaxy income of one mine, per second.</summary>
    public const double MineIncome = 3;
    /// <summary>Galaxy income of each station level, per second.</summary>
    public const double StationIncome = 1;
    public const double StationCost = 1500;
    public const double StationTime = 40;
    /// <summary>A lab is needed to research; it stays on the ground, out of space battles.</summary>
    public const double LabCost = 800;
    public const double LabTime = 30;
    /// <summary>Seconds a lone fleet must hold an undefended enemy planet before taking it.</summary>
    public const double CaptureTime = 15;
    public const double NeutralCaptureTime = 5;
    /// <summary>Battles resolved automatically run this many times faster than the galaxy clock.</summary>
    public const double AutoSpeed = 15;
    /// <summary>An automatic battle still undecided after this many seconds ends with the attacker falling back.</summary>
    public const double AutoTimeout = 600;
}
