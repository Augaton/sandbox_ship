// The survival lobby's records: the best games played on this machine with as many seats
// (Core/SurvivalScores), read each time the lobby is drawn.
using System.Globalization;
using Godot;
using SandboxShip.Core;
using SandboxShip.Sim;

namespace SandboxShip.Ui;

public partial class Menu
{
    Control RecordsSection(int seats)
    {
        var title = seats > 1 ? $"Meilleurs scores à {seats} joueurs" : "Meilleurs scores en solo";
        var best = SurvivalRecords.Best(SurvivalScores.Load(), seats);
        if (best.Count == 0) return Section(title, Note("Aucune partie encore : le premier score s'inscrira ici."));
        var lines = new List<Control>();
        for (var i = 0; i < Math.Min(5, best.Count); i++)
        {
            var r = best[i];
            var date = DateTimeOffset.FromUnixTimeMilliseconds(r.At).ToLocalTime().ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            lines.Add(Paragraph($"{i + 1}. {HudKit.N(r.Score)} points : vague {r.Wave}, tenu {Names.Duration(r.Held)} ({r.Sides}, le {date})",
                i == 0 ? Palette.Accent : Palette.Text, 14));
        }
        return Section(title, [.. lines]);
    }
}
