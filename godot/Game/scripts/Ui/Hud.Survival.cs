// Survival on screen (not in the web version): the mode panel's lines (the wave, when the next
// one comes and what it brings, the scores), what a wave's toasts say, and the end of a game: the
// scoreboard of every seat, with the records kept on this machine.
using System.Globalization;
using Godot;
using SandboxShip.Core;
using SandboxShip.Sim;
using SandboxShip.Sim.Data;
using static SandboxShip.Ui.HudKit;

namespace SandboxShip.Ui;

public partial class Hud
{
    /// <summary>A theme as the panel names it.</summary>
    static string ThemeName(WaveTheme theme) => theme switch
    {
        WaveTheme.Swarm => "essaim",
        WaveTheme.Armada => "armada lourde",
        WaveTheme.Carriers => "porte-avions",
        _ => "",
    };

    /// <summary>"Colosse, Léviathan": a wave's bosses.</summary>
    static string BossNames(int wave) => string.Join(", ", Survival.Bosses(wave).Select(c => Ships.Defs[c].Name));

    /// <summary>The mode panel in survival: the wave and the Errants left, when the next comes and what it brings, the scores.</summary>
    List<Part> SurvivalParts(double now)
    {
        var m = World.Mode;
        var errants = World.Entities.Count(e => e.Kind == EntityKind.Ship && e.Side == (Side)Faction.Errant && !e.Launched);
        var next = m.Wave + 1;
        var parts = new List<Part>
        {
            Heading("Survie", $"score d'équipe {N(Survival.TeamScore(World))}"),
            SpansPart(16, ($"Vague {m.Wave}", Palette.Text, true), ($"Errants : {errants}", Palette.Muted, false)),
            TextPart($"{(m.Wave == 0 ? "Premiers Errants" : $"Vague {next}")} dans {Clock(m.NextWave - now)}", Palette.Muted, 14, wrap: false),
        };
        if (Survival.Theme(next) is var theme && theme != WaveTheme.None) parts.Add(TextPart($"Vague à thème : {ThemeName(theme)}", Palette.Accent, 14, wrap: false));
        else if (Survival.Bosses(next).Count > 0) parts.Add(TextPart($"Boss : {BossNames(next)}", Palette.Danger, 14, wrap: false));
        if (Survival.Elite(next) > 0) parts.Add(TextPart($"Errants d'élite, niveau {Survival.Elite(next)}", Palette.Danger, 14, wrap: false));
        if (m.Tallies.TryGetValue(Player, out var mine)) parts.Add(TextPart($"Ton score : {N(mine.Score)}", Palette.Muted, 14, wrap: false));
        return parts;
    }

    /// <summary>What the alert of an incoming wave says: its theme, or its bosses.</summary>
    public static string WaveNews(int wave) => Survival.Theme(wave) switch
    {
        WaveTheme.Swarm => $"Vague {wave} : un essaim d'Errants arrive",
        WaveTheme.Armada => $"Vague {wave} : une armada lourde arrive",
        WaveTheme.Carriers => $"Vague {wave} : des porte-avions arrivent",
        _ when Survival.Bosses(wave).Count > 0 => $"Vague {wave} : les Errants arrivent, boss : {BossNames(wave)}",
        _ => $"Vague {wave} : les Errants arrivent",
    };

    /// <summary>A wave beaten: what it pays, to a seat still standing.</summary>
    public void WaveBeaten(WaveCleared wc)
    {
        var standing = World.Factions.TryGetValue(Player, out var f) && f.StationId is not null;
        var points = Survival.WavePoints * wc.Wave + Survival.AheadPoints * wc.Ahead;
        Toast(standing ? $"Vague {wc.Wave} repoussée : +{N(Survival.Reward(wc.Wave))} crédits, +{N(points)} points" : $"Vague {wc.Wave} repoussée");
    }

    /// <summary>
    /// The end of a survival game: kept among this machine's records if it makes them, then the
    /// team's score, every seat's line, and the best games played with as many seats.
    /// </summary>
    Control SurvivalEnd()
    {
        var m = World.Mode;
        var seats = World.Factions.Values.Where(f => f.Race != Faction.Errant).ToList();
        var team = Survival.TeamScore(World);
        var held = m.Held > 0 ? m.Held : World.Time;
        var game = new SurvivalRecord(team, m.Wave, held, seats.Count, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            string.Join(", ", seats.Select(s => Factions.Defs[s.Race].Name)));
        var (rank, best) = SurvivalScores.Add(game);

        var box = Column(12);
        box.AddChild(Text($"Score d'équipe : {N(team)}", Palette.Accent, 22, bold: true));
        if (rank == 0) box.AddChild(Text("Nouveau record", Palette.Ok, 16, bold: true));
        else if (rank > 0) box.AddChild(Text($"{rank + 1}e meilleur score", Palette.Text, 15));
        var rows = seats.OrderByDescending(s => m.Tallies.GetValueOrDefault(s.Id)?.Score ?? 0).Select(s =>
        {
            var t = m.Tallies.GetValueOrDefault(s.Id) ?? new SurvivalTally();
            var color = s.Id == Player ? Palette.Accent : Palette.Text;
            return (Who(s.Id, color), color, (IEnumerable<string>)[N(t.Score), N(t.Downed), N(t.Bosses), N(Js.Round(s.Stats.Damage)), N(s.Stats.Lost)]);
        });
        box.AddChild(Table(["Joueur", "Score", "Errants abattus", "Boss", "Dégâts", "Pertes"], rows));
        if (best.Count > 0)
        {
            box.AddChild(Text(seats.Count > 1 ? $"Meilleurs scores à {seats.Count} joueurs" : "Meilleurs scores en solo", Palette.Muted, 14, bold: true));
            box.AddChild(Table(["", "Score", "Vague", "Tenu", "Date"], best.Take(5).Select((r, i) =>
            {
                var color = r == game ? Palette.Accent : Palette.Text;
                var date = DateTimeOffset.FromUnixTimeMilliseconds(r.At).ToLocalTime().ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
                return ((Control)Text($"{i + 1}", color, 14), color, (IEnumerable<string>)[N(r.Score), N(r.Wave), Names.Duration(r.Held), date]);
            })));
        }
        return box;
    }
}
