// The battle's terrain in the tooltip (Sim/Terrain.cs): over a piece with nothing under the
// pointer, what it is and what it does. Not in the web version.
using SandboxShip.Core;
using SandboxShip.Sim;
using static SandboxShip.Ui.HudKit;

namespace SandboxShip.Ui;

public partial class Hud
{
    public static string TerrainName(TerrainKind kind) => kind switch
    {
        TerrainKind.Nebula => "Nébuleuse",
        TerrainKind.Belt => "Ceinture d'astéroïdes",
        TerrainKind.Minefield => "Champ de mines",
        _ => "Puits gravitationnel",
    };

    static List<Part> TerrainTip(TerrainPatch p)
    {
        const int size = 14;
        var lines = new List<Part> { TextPart(TerrainName(p.Kind), Palette.Text, size, bold: true, wrap: false) };
        IEnumerable<string> effects = p.Kind switch
        {
            TerrainKind.Nebula =>
            [
                $"Un vaisseau dedans n'est vu qu'à {N(Terrains.NebulaSight)} de distance",
                $"Boucliers et coques s'y régénèrent à {Percent(Terrains.NebulaRepair)} de leur rythme",
            ],
            TerrainKind.Belt => ["Infranchissable : les vaisseaux la contournent", "Arrête les tirs, sauf les rayons lourds et les faisceaux"],
            TerrainKind.Minefield =>
            [
                p.Armed ? $"{N(p.Mines)} mines sur {N(p.MaxMines)}" : "Déminé",
                "Chaque seconde, une mine saute sous chaque vaisseau dedans",
                "Il se vide quand on le traverse, ou sous une frappe",
            ],
            _ => [$"Au cœur, les vaisseaux vont à {Percent(Terrains.WellSlow)} de leur vitesse", "Attire les vaisseaux et dévie les tirs qui passent"],
        };
        foreach (var line in effects) lines.Add(TextPart(line, Palette.Muted, size, wrap: false));
        return lines;
    }
}
