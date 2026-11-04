// The Profile screen: the player's statistics on this machine, then every achievement, earned
// ones with their date, the others locked; a hidden one says nothing of itself until earned.
using System.Globalization;
using Godot;
using SandboxShip.Core;
using SandboxShip.Sim;
using SandboxShip.Sim.Data;

namespace SandboxShip.Ui;

public partial class Menu
{
    static readonly (LobbyMode Mode, string Name)[] ProfileModes =
    [
        (LobbyMode.Skirmish, "Escarmouche"), (LobbyMode.Survival, "Survie"), (LobbyMode.Zones, "Contrôle de zone"),
        (LobbyMode.Assault, "Assaut"), (LobbyMode.Regicide, "Régicide"), (LobbyMode.Campaign, "Batailles de campagne"),
        (LobbyMode.Sandbox, "Bac à sable"),
    ];

    void ProfileScreen()
    {
        var p = PlayerProfile.Current;
        var s = p.Stats;
        Titled("Profil", "Tes parties sur cette machine. Échap : retour");
        header.AddChild(ProfileTabs());

        var main = Pairs(
            ("Batailles jouées", HudKit.N(s.Battles)),
            ("Victoires", s.Battles > 0 ? $"{HudKit.N(s.Wins)} ({Math.Round(100.0 * s.Wins / s.Battles)} %)" : "0"),
            ("Vaisseaux détruits", HudKit.N(s.Kills)),
            ("Vaisseaux perdus", HudKit.N(s.Lost)),
            ("Vaisseaux construits", HudKit.N(s.Built)),
            ("Temps de jeu", PlayTime(s.Seconds)),
            ("Meilleure vague en survie", s.BestWave > 0 ? HudKit.N(s.BestWave) : "aucune"),
            ("Campagnes gagnées", $"{HudKit.N(s.CampaignsWon)} sur {HudKit.N(s.Campaigns)}"),
            ("Parties en réseau", HudKit.N(s.NetworkGames)));
        var modes = Pairs([.. ProfileModes.Where(m => s.PlayedIn(m.Mode) > 0)
            // The sandbox has no winner, nor survival, which goes on until everyone falls: their games only.
            .Select(m => (m.Name, m.Mode is LobbyMode.Sandbox or LobbyMode.Survival ? $"{HudKit.N(s.PlayedIn(m.Mode))} parties"
                : $"{HudKit.N(s.WonIn(m.Mode))} victoires sur {HudKit.N(s.PlayedIn(m.Mode))}"))]);
        var races = Pairs([.. new[] { Faction.Klaed, Faction.Nairan, Faction.Nautolan }
            .Where(f => s.PlayedAs.GetValueOrDefault(f.ToString()) > 0)
            .Select(f => (Factions.Defs[f].Name, $"{HudKit.N(s.WonWith(f))} victoires sur {HudKit.N(s.PlayedAs[f.ToString()])}"))]);
        body.AddChild(Section("Statistiques", main));
        if (modes.GetChildCount() > 0) body.AddChild(Section("Par mode", modes));
        if (races.GetChildCount() > 0) body.AddChild(Section("Par race", races));
        if (s.Battles == 0 && s.PlayedIn(LobbyMode.Sandbox) == 0) body.AddChild(Paragraph("Aucune partie encore : tes statistiques se rempliront au fil des batailles.", Palette.Muted, 14));

        var earned = Profiles.All.Count(a => p.Unlocked.ContainsKey(a.Id));
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 8);
        // Earned first, the most recent at the top; then the others in the list's order.
        foreach (var a in Profiles.All.OrderByDescending(a => p.Unlocked.GetValueOrDefault(a.Id, -1)))
            grid.AddChild(AchievementCard(a, p.Unlocked.TryGetValue(a.Id, out var at) ? at : null));
        body.AddChild(Section($"Succès : {earned} sur {Profiles.All.Count}", grid));
        footer.AddChild(Btn("Retour", Back));
    }

    /// <summary>Labels and values side by side, in two columns.</summary>
    static GridContainer Pairs(params (string Label, string Value)[] rows)
    {
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 24);
        grid.AddThemeConstantOverride("v_separation", 4);
        foreach (var (label, value) in rows)
        {
            var l = UiTheme.Text(label, Palette.Muted, 14);
            l.CustomMinimumSize = new Vector2(280, 0);
            grid.AddChild(l);
            grid.AddChild(UiTheme.Text(value, Palette.Text, 14));
        }
        return grid;
    }

    /// <summary>"12 h 05 min", or "5 min" under an hour.</summary>
    static string PlayTime(double seconds)
    {
        var minutes = (long)(seconds / 60);
        return minutes >= 60 ? $"{minutes / 60} h {minutes % 60:00} min" : $"{minutes} min";
    }

    /// <summary>An achievement's card: its icon, its name and what earns it, and the day it was earned; locked ones dimmed.</summary>
    static Control AchievementCard(AchievementDef a, long? at)
    {
        var got = at is not null;
        var secret = a.Hidden && !got;
        var card = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(320, 0) };
        var box = UiTheme.Box(got ? Palette.Raised : Palette.Bg with { A = 0.6f }, got ? Palette.Accent with { A = 0.6f } : Palette.Line, 10);
        card.AddThemeStyleboxOverride("panel", box);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        card.AddChild(row);
        row.AddChild(new TextureRect
        {
            Texture = AchievementIcons.Of(a, got), CustomMinimumSize = new Vector2(48, 48), StretchMode = TextureRect.StretchModeEnum.KeepCentered,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            AccessibilityName = secret ? "Succès caché" : a.Name,
        });
        var col = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 2);
        row.AddChild(col);
        var name = UiTheme.Text(secret ? "Succès caché" : a.Name, got ? Palette.Text : Palette.Muted, 15);
        name.AddThemeFontOverride("font", Bold);
        col.AddChild(name);
        var text = Small(secret ? "Il se révèle une fois obtenu." : a.Text);
        col.AddChild(text);
        if (at is long ms)
        {
            var day = DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            col.AddChild(UiTheme.Text($"Obtenu le {day}", Palette.Accent, 12));
        }
        return card;
    }
}
