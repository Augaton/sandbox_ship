// The new game's screens: the modes as cards, then the lobby of the one picked (its seats, the
// map, the rules) that starts the game. The lobby is Sim's (Lobby): the menu shows it, changes
// it and keeps the last one of each mode in user://lobby.json.
using System.Globalization;
using System.Text.Json;
using Godot;
using SandboxShip.Core;
using SandboxShip.Network;
using SandboxShip.Render;
using SandboxShip.Sim;
using SandboxShip.Sim.Data;
using SandboxShip.Sim.Net;

namespace SandboxShip.Ui;

public partial class Menu
{
    /// <summary>Each mode's card: its name and what it is about, in two lines at most.</summary>
    static readonly (LobbyMode Mode, string Name, string Description)[] Modes =
    [
        (LobbyMode.Campaign, "Campagne galactique", "Conquiers une galaxie planète après planète. Chaque rencontre se joue en bataille, ou se résout seule."),
        (LobbyMode.Skirmish, "Escarmouche", "Une seule bataille : chacun sa station, le dernier à en garder une gagne."),
        (LobbyMode.Survival, "Survie", "Tous ensemble contre des vagues d'Errants, de plus en plus fortes."),
        (LobbyMode.Zones, "Contrôle de zone", "Tiens les balises pour marquer des points : le premier à l'objectif gagne."),
        (LobbyMode.Assault, "Assaut", "L'équipe 1 défend sa forteresse, l'équipe 2 doit la prendre à temps."),
        (LobbyMode.Regicide, "Régicide", "Chacun son vaisseau amiral : le perdre, c'est perdre la partie."),
        (LobbyMode.Sandbox, "Bac à sable", "IA coupée, 50 000 crédits : place ce que tu veux, où tu veux."),
    ];

    /// <summary>The names of Lobby.Colors, in its order: the races' three, then the players' six.</summary>
    static readonly string[] ColorNames = ["Rouge", "Bleu", "Vert", "Violet", "Cyan", "Rose", "Citron", "Argent", "Lavande"];

    const string LobbyPath = "user://lobby.json";

    /// <summary>Each mode's lobby, as last left; the one shown is Current, unless a game over the network is (Menu.Network.cs).</summary>
    readonly Dictionary<LobbyMode, Lobby> lobbies = [];
    LobbyMode mode;
    Lobby Current => Session.Current?.Room?.Lobby ?? lobbies[mode];

    static string Players(LobbyMode m) => $"{Lobby.MinPlayers(m)} à {Lobby.MaxSeats} {(m == LobbyMode.Sandbox ? "camps" : "joueurs")}";

    /// <summary>
    /// The lobbies left last time, each on a new map (a new seed). None in a capture run (--shot),
    /// which leaves the player's files alone; none either from a file that does not hold together.
    /// </summary>
    static List<Lobby> Remembered()
    {
        if (Dev.Shot is not null || !Godot.FileAccess.FileExists(LobbyPath)) return [];
        List<Lobby>? read;
        try
        {
            read = JsonSerializer.Deserialize<List<Lobby>>(Godot.FileAccess.GetFileAsString(LobbyPath), Lobby.Json);
        }
        catch (JsonException)
        {
            return [];
        }
        var sane = (read ?? []).Where(l => l is not null && l.Sane()).ToList();
        foreach (var l in sane)
        {
            l.Seed = NewSeed();
            l.Fit();
        }
        return sane;
    }

    /// <summary>Keeps every mode's lobby for next time; a capture run writes nothing.</summary>
    void Remember()
    {
        if (Dev.Shot is not null) return;
        using var f = Godot.FileAccess.Open(LobbyPath, Godot.FileAccess.ModeFlags.Write);
        // Without a writable user folder, the lobbies last for this session only.
        f?.StoreString(JsonSerializer.Serialize(lobbies.Values.ToList(), Lobby.Json));
    }

    /// <summary>Checking the lobby (--menu=lobby-MODE): its bots added up to --seats, then the --teams in seat order.</summary>
    void DevLobby(LobbyMode m)
    {
        mode = m;
        var l = lobbies[m] = Lobby.Create(m, NewSeed());
        while (l.Seats.Count < Dev.Seats && l.AddBot() is not null) { }
        if (Dev.Terrain is { } terrain) l.Terrain = terrain;
        for (var i = 0; i < Math.Min(Dev.Teams.Length, l.Seats.Count); i++) l.SetTeam(l.Seats[i], Dev.Teams[i]);
    }

    void OpenLobby(LobbyMode m)
    {
        if (hosting && !HostLobby(m)) return;
        mode = m;
        if (!lobbies.ContainsKey(m)) lobbies[m] = Lobby.Create(m, NewSeed());
        Show(Screen.Lobby);
    }

    void Launch()
    {
        if (Current.Problems().Count > 0) return;
        if (Session.Current is { } net)
        {
            net.Launch();
            return;
        }
        Remember();
        Main.Instance.Start(Current);
    }

    // The modes.

    void ModeCards()
    {
        // Hosting: every mode but the sandbox, whose tools are for a game played alone; and the
        // campaign saved over the network, to resume with its players.
        if (hosting)
        {
            Titled(online ? "Héberger en ligne" : "Héberger en local", online
                ? "Choisis le mode : ta partie s'affiche chez tous les joueurs, par le serveur en ligne. Échap : retour"
                : "Choisis le mode : les joueurs te rejoignent dans son salon. Échap : retour");
            body.AddChild(PasswordField("Mot de passe de la partie, facultatif", hostPassword, "aucun : qui la voit peut la rejoindre", text => hostPassword = text));
            body.AddChild(ResumeCard() is { } resume ? Section("Campagne", ModeCard(Modes[0]), resume) : Section("Campagne", ModeCard(Modes[0])));
        }
        else
        {
            Titled("Nouvelle partie", "Choisis un mode. Échap : retour");
            body.AddChild(Section("Campagne", ModeCard(Modes[0])));
        }
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 8);
        foreach (var m in Modes.Skip(1).Where(m => !hosting || m.Mode != LobbyMode.Sandbox)) grid.AddChild(ModeCard(m));
        body.AddChild(Section("Bataille", grid));
        if (hosting && hostError is not null) body.AddChild(Paragraph(hostError, Palette.Danger, 14));
        footer.AddChild(Btn("Retour", Back));
    }

    /// <summary>The campaign the host saved over the network, as a card that hosts it again; null without one.</summary>
    Control? ResumeCard()
    {
        if (GalaxySave.Info(Campaign.GalaxyScreen.NetSavePath) is not { } save) return null;
        var when = DateTimeOffset.FromUnixTimeMilliseconds(save.SavedAt).ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        var about = $"{Factions.Defs[save.Race].Name}, {save.Planets} planète{(save.Planets > 1 ? "s" : "")}{(save.Others.Length > 0 ? $", avec {save.Others}" : "")}, le {when}";
        return Tile("resume", false, HostResume, $"Reprendre la campagne : {about}", col =>
        {
            var name = UiTheme.Text("Reprendre la campagne");
            name.AddThemeFontOverride("font", Bold);
            col.AddChild(name);
            var line = Small($"{about}. Chacun reprend sa place en rejoignant sous le même nom.");
            line.CustomMinimumSize = new Vector2(0, 36);
            col.AddChild(line);
        }, Assets.Planet);
    }

    /// <summary>The picture of a mode's card: the galaxy's planet, a ship that says the mode, an asteroid for the sandbox.</summary>
    static string? ModeArt(LobbyMode mode) => mode switch
    {
        LobbyMode.Campaign => Assets.Planet,
        LobbyMode.Skirmish => Assets.ShipSprite(Faction.Klaed, ShipClass.Frigate),
        LobbyMode.Survival => Assets.ShipSprite(Faction.Nautolan, ShipClass.Bomber),
        LobbyMode.Zones => Assets.ShipSprite(Faction.Nairan, ShipClass.Scout),
        LobbyMode.Assault => Assets.ShipSprite(Faction.Klaed, ShipClass.Battlecruiser),
        LobbyMode.Regicide => Assets.ShipSprite(Faction.Nairan, ShipClass.Dreadnought),
        LobbyMode.Sandbox => Assets.Asteroids.FirstOrDefault(),
        _ => null,
    };

    /// <summary>A mode's card: its name and what it is about, and in the bottom right corner, over its picture, how many play it.</summary>
    Control ModeCard((LobbyMode Mode, string Name, string Description) m)
    {
        var tile = Tile($"mode:{m.Mode}", false, () => OpenLobby(m.Mode), $"{m.Name}, {Players(m.Mode)} : {m.Description}", col =>
        {
            var name = UiTheme.Text(m.Name);
            name.AddThemeFontOverride("font", Bold);
            col.AddChild(name);
            var about = Small(m.Description);
            // Two lines at least: the wide card keeps the height of the others.
            about.CustomMinimumSize = new Vector2(0, 36);
            col.AddChild(about);
        }, ModeArt(m.Mode));
        var count = UiTheme.Text(Players(m.Mode), Palette.Accent, 13);
        count.SizeFlagsHorizontal = count.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;
        count.MouseFilter = Control.MouseFilterEnum.Ignore;
        count.AddThemeConstantOverride("outline_size", 5);
        count.AddThemeColorOverride("font_outline_color", Palette.Bg);
        var corner = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        corner.AddThemeConstantOverride("margin_right", 10);
        corner.AddThemeConstantOverride("margin_bottom", 8);
        corner.AddChild(count);
        tile.AddChild(corner);
        return tile;
    }

    // The lobby.

    void LobbyScreen()
    {
        var l = Current;
        var (_, name, description) = Modes.First(m => m.Mode == l.Mode);
        Titled(name, NetTitle() ?? $"{description} Échap : retour");

        body.AddChild(Section($"Joueurs · {l.Seats.Count} sur {Lobby.MaxSeats}", SeatGrid(l), SeatActions(l)));
        if (Session.Current?.Room?.Members.Where(m => m.Watches).Select(m => m.Name).ToList() is { Count: > 0 } watchers)
        {
            body.AddChild(Note($"Spectateurs : {string.Join(", ", watchers)}"));
        }
        // The map and the rules are the host's to set: the others see them. A resumed campaign keeps its own.
        body.AddChild(l.Resumed ? Section("Galaxie", Note("La galaxie, les camps et la partie de la campagne sauvegardée par l'hôte, là où elle s'était arrêtée."))
            : Locked(MapSection(l)));
        if (RulesSection(l) is { } rules) body.AddChild(Locked(rules));
        if (l.Mode == LobbyMode.Survival) body.AddChild(RecordsSection(l.Seats.Count));

        var problems = l.Problems();
        var line = Paragraph(problems.Count > 0 ? problems[0] : Summary(l), problems.Count > 0 ? Palette.Danger : Palette.Muted, 13);
        // A wrapped label needs a width to measure its height by, or it counts one letter a line.
        line.CustomMinimumSize = new Vector2(280, 0);
        line.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        line.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        footer.AddChild(line);
        footer.AddChild(Btn(Session.Current is not { } net ? "Retour" : net.Hosting ? "Fermer la partie" : "Quitter", Back));
        if (ReadyButton(l) is { } ready) footer.AddChild(ready);
        if (!Leads) return;
        var go = Btn("Lancer la partie", Launch, Primary, "act:launch");
        go.Disabled = problems.Count > 0;
        go.TooltipText = string.Join("\n", problems);
        footer.AddChild(go);
    }

    /// <summary>The seats, one row each under the column names: number, who, race, team, color, and the button that takes it away.</summary>
    Control SeatGrid(Lobby l)
    {
        var grid = new GridContainer { Columns = 6, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 6);
        foreach (var head in new[] { "", "Place", "Race", "Équipe", "Couleur", "" }) grid.AddChild(UiTheme.Text(head, Palette.Muted, 13));
        for (var i = 0; i < l.Seats.Count; i++) SeatRow(grid, l, l.Seats[i], i + 1);
        return grid;
    }

    void SeatRow(GridContainer grid, Lobby l, Seat seat, int n)
    {
        var key = $"seat:{seat.Id}";
        var plays = seat.Plays;
        var number = UiTheme.Text(n.ToString(CultureInfo.InvariantCulture), plays ? Palette.Hex(seat.Color) : Palette.Muted, 17);
        number.AddThemeFontOverride("font", Bold);
        number.CustomMinimumSize = new Vector2(16, 0);
        number.HorizontalAlignment = HorizontalAlignment.Center;
        number.VerticalAlignment = VerticalAlignment.Center;
        grid.AddChild(number);

        grid.AddChild(KindDrop(l, seat, key, n));

        var races = Factions.All;
        var race = Drop($"{key}:race", $"Race de la place {n}", races.Select(f => Factions.Defs[f].Name), races.ToList().IndexOf(seat.Race),
            i => Edit(new LobbyRequest.SetRace(seat.Id, races[i])), i => Logos.Emblem(races[i], 1, seat.Color));
        race.Disabled = !plays || !Mine(seat) || l.Resumed;
        grid.AddChild(race);

        // Survival plays as one team; an assault, as the defense (team 1) against the attack (team 2).
        var (teams, labels) = l.Mode switch
        {
            LobbyMode.Survival => (new[] { 1 }, new[] { "Ensemble" }),
            LobbyMode.Assault => (new[] { 1, 2 }, new[] { "Défense", "Attaque" }),
            _ => (new[] { 0, 1, 2, 3 }, new[] { "Aucune", "Équipe 1", "Équipe 2", "Équipe 3" }),
        };
        var team = Drop($"{key}:team", $"Équipe de la place {n}", labels, Math.Max(0, Array.IndexOf(teams, seat.Team)),
            i => Edit(new LobbyRequest.SetTeam(seat.Id, teams[i])));
        team.Disabled = !plays || l.TeamsLocked || !Mine(seat) || l.Resumed;
        if (l.TeamsLocked) team.TooltipText = "En survie, tout le monde fait équipe contre les vagues.";
        grid.AddChild(team);

        var colors = Lobby.Colors;
        var color = Drop($"{key}:color", $"Couleur de la place {n}", ColorNames, colors.ToList().IndexOf(seat.Color),
            i => Edit(new LobbyRequest.SetColor(seat.Id, colors[i])), i => Swatch(colors[i]));
        for (var i = 0; i < colors.Count; i++)
        {
            if (l.ColorFree(seat, colors[i])) continue;
            color.SetItemDisabled(i, true);
            color.SetItemTooltip(i, "Déjà prise");
        }
        color.Disabled = !plays || !Mine(seat) || l.Resumed;
        grid.AddChild(color);

        // Once it is gone, the focus goes to the seat that takes its place, or to the one before.
        var at = l.Seats.IndexOf(seat);
        var next = at + 1 < l.Seats.Count ? l.Seats[at + 1] : at > 0 ? l.Seats[at - 1] : null;
        var remove = Btn("×", () => Pick(next is null ? "act:add" : $"seat:{next.Id}:remove", () => Edit(new LobbyRequest.Remove(seat.Id))), key: $"{key}:remove");
        remove.CustomMinimumSize = new Vector2(40, 40);
        remove.Disabled = l.Seats.Count <= 1 || !Leads || l.Resumed;
        remove.TooltipText = "Retirer la place";
        remove.AccessibilityName = $"Retirer la place {n}";
        grid.AddChild(remove);
    }

    /// <summary>
    /// Who takes the seat: you (leaving your seat open), a bot at one of the difficulties, a
    /// remote player, or nobody. Over the network, a remote player's seat is free until one
    /// sits there, then shows its name, ready or not. Without the host's rights, a player can
    /// only take an open seat.
    /// </summary>
    OptionButton KindDrop(Lobby l, Seat seat, string key, int n)
    {
        var mine = seat.Kind == SeatKind.Human && seat.Peer == Me;
        var remote = seat.Kind == SeatKind.Human && !mine;
        var net = Session.Current?.Room;
        var choices = new List<(string Label, LobbyRequest? Ask, bool On)> { ("Toi", new LobbyRequest.SetKind(seat.Id, SeatKind.Human), mine) };
        foreach (var d in Enum.GetValues<Difficulty>())
        {
            choices.Add(($"Bot · {Worlds.Difficulties[d].Name}", new LobbyRequest.SetKind(seat.Id, SeatKind.Bot, d), seat.Kind == SeatKind.Bot && seat.Difficulty == d));
        }
        var far = choices.Count;
        if (net is not null)
        {
            // A resumed campaign keeps a seat for each player who played it, by name.
            var free = seat.Reserved is { } name ? $"Réservée à {name} · absent" : "Joueur distant · libre";
            var who = !remote ? free : $"{net.NameOf(seat.Peer)} · {(seat.Peer == Lobby.Host ? "hôte" : seat.Ready ? "prêt" : "en attente")}";
            choices.Add((who, new LobbyRequest.SetKind(seat.Id, SeatKind.Open), remote || seat.Kind == SeatKind.Open));
        }
        else
        {
            choices.Add((remote ? $"Joueur distant · {(seat.Ready ? "prêt" : "en attente")}" : "Joueur distant", null, remote));
            choices.Add(("Ouvert", new LobbyRequest.SetKind(seat.Id, SeatKind.Open), seat.Kind == SeatKind.Open));
        }
        if (!l.Resumed) choices.Add(("Fermé", new LobbyRequest.SetKind(seat.Id, SeatKind.Closed), seat.Kind == SeatKind.Closed));
        var drop = Drop($"{key}:kind", $"Place {n}", choices.Select(c => c.Label), choices.FindIndex(c => c.On), i =>
        {
            if (choices[i].Ask is { } ask) Edit(ask);
        });
        var claim = !Leads && seat.Kind == SeatKind.Open && !l.Resumed;
        for (var i = 0; i < choices.Count; i++)
        {
            // Resumed, the host only gives a player's seat to a bot, or keeps it for them.
            var kept = l.Resumed && (seat.Reserved is null || i == 0);
            if (choices[i].Ask is null || !Leads && !(claim && i == 0) || kept) drop.SetItemDisabled(i, !choices[i].On);
        }
        if (net is null) drop.SetItemTooltip(far, "Pour jouer à plusieurs : « Multijoueur », au menu principal.");
        drop.Disabled = !Leads && !claim;
        if (claim) drop.TooltipText = "Choisis « Toi » pour prendre cette place.";
        return drop;
    }

    Control SeatActions(Lobby l)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);
        var add = Btn("Ajouter un bot", () =>
        {
            var count = l.Seats.Count;
            Edit(new LobbyRequest.AddBot());
            var added = l.Seats.Count > count ? l.Seats[^1] : null;
            // Full now: the focus goes to the seat just added.
            Rebuild(l.CanAdd || added is null ? "act:add" : $"seat:{added.Id}:kind");
        }, key: "act:add");
        add.Disabled = !l.CanAdd || !Leads || l.Resumed;
        add.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        row.AddChild(add);
        var remote = Session.Current is null ? "" : " « Joueur distant » : une place libre pour qui rejoint ; un joueur qui part la libère.";
        var note = Note(l.Resumed ? "Campagne reprise : chacun retrouve sa place, sa race et son équipe. Une place réservée attend son joueur, "
            + "qui rejoint sous le même nom ; l'hôte peut la donner à un bot." : SeatsNote(l) + remote);
        note.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        note.CustomMinimumSize = new Vector2(200, 0);
        row.AddChild(note);
        return row;
    }

    static string SeatsNote(Lobby l)
    {
        var note = l.Mode switch
        {
            LobbyMode.Campaign => "L'ordre des places donne les capitales : les deux premières sont les plus éloignées.",
            LobbyMode.Survival => "Tout le monde fait équipe contre les vagues d'Errants.",
            LobbyMode.Assault => "Défense : tenir la forteresse jusqu'au bout. Attaque : la prendre avant.",
            LobbyMode.Sandbox => "IA coupée au départ : le panneau du bac à sable la rallume à la difficulté choisie.",
            _ => "Une équipe : des alliés qui gagnent ensemble. L'ordre des places donne les positions de départ.",
        };
        return l.Seats.Exists(s => !s.Plays) ? $"{note} Les places ouvertes ou fermées ne jouent pas." : note;
    }

    Control MapSection(Lobby l)
    {
        var battle = l.Mode != LobbyMode.Campaign;
        var settings = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        settings.AddThemeConstantOverride("separation", 12);
        settings.AddChild(Labelled("Type", Group("map", [(MapKind.Random, "Aléatoire"), (MapKind.Classic, "Classique")], l.Map,
            v => Set(s => s with { Map = v }), v => v == MapKind.Classic && !l.ClassicAllowed)));
        var classic = l.Map == MapKind.Classic && !battle;
        settings.AddChild(Labelled(battle ? "Taille de la carte" : "Taille de la galaxie",
            Group("size", Scenario.MapSizes.Select(s => (s.Key, s.Value.Name)), l.Size, v => Set(s => s with { Size = v }), _ => classic)));
        if (l.HasTerrain)
        {
            settings.AddChild(Labelled("Terrain", Group("terrain", [(TerrainDensity.None, "Aucun"), (TerrainDensity.Light, "Léger"), (TerrainDensity.Dense, "Dense")],
                l.Terrain, v => Set(s => s with { Terrain = v }))));
        }
        settings.AddChild(SeedRow(l));

        var (mapW, mapH) = battle ? (Scenario.MapSizes[l.Size].W, Scenario.MapSizes[l.Size].H) : (l.Layout().W, l.Layout().H);
        preview = new Control
        {
            CustomMinimumSize = new Vector2(280, (float)Math.Round(280 * mapH / mapW)),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd, SizeFlagsVertical = Control.SizeFlags.ShrinkBegin, ClipContents = true,
            AccessibilityName = battle ? "Aperçu de la carte : chaque place a sa station, à son numéro et à sa couleur"
                : "Aperçu de la galaxie : chaque place a sa capitale, à son numéro et à sa couleur",
        };
        var canvas = preview;
        canvas.Draw += () => DrawPreview(canvas, l);
        var map = new HBoxContainer();
        map.AddThemeConstantOverride("separation", 20);
        map.AddChild(settings);
        map.AddChild(canvas);
        var why = !l.ClassicAllowed ? " La carte classique est faite pour deux." : classic ? " La galaxie classique a sa taille." : "";
        var terrain = l.HasTerrain && l.Terrain != TerrainDensity.None
            ? " Nébuleuses, astéroïdes, mines et puits gravitationnels : chaque place a le même terrain autour d'elle." : "";
        return Section(battle ? "Carte" : "Galaxie", map, Note($"La même graine redonne la même carte.{why + terrain}"));
    }

    Control SeedRow(Lobby l)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var input = new LineEdit
        {
            Text = l.Seed.ToString(CultureInfo.InvariantCulture), MaxLength = 6, Editable = l.Map != MapKind.Classic,
            CustomMinimumSize = new Vector2(120, 40), AccessibilityName = "Graine",
        };
        input.TextChanged += text =>
        {
            var digits = string.Concat(text.Where(char.IsAsciiDigit));
            if (digits != text)
            {
                input.Text = digits;
                input.CaretColumn = digits.Length;
            }
            Set(s => s with { Seed = Math.Max(1, int.TryParse(digits, out var v) ? v : 1) });
            preview?.QueueRedraw();
        };
        row.AddChild(input);
        var reroll = Btn("Nouvelle graine", () => Pick("act:reroll", () => Set(s => s with { Seed = NewSeed(), Map = MapKind.Random })), key: "act:reroll");
        reroll.Disabled = l.Map == MapKind.Classic;
        row.AddChild(reroll);
        return Labelled("Graine", row);
    }

    /// <summary>The mode's own settings, the raid and the fog where they apply; none for the campaign and the sandbox.</summary>
    Control? RulesSection(Lobby l)
    {
        var items = new List<Control>();
        if (l.Mode == LobbyMode.Zones)
        {
            items.Add(Labelled("Objectif", Group("zone", Lobby.ZoneTargets.Select(v => (v, $"{v} points")), l.ZoneTarget, v => Set(s => s with { ZoneTarget = v }))));
        }
        if (l.Mode == LobbyMode.Assault)
        {
            items.Add(Labelled("Durée de l'assaut", Group("assault", Lobby.AssaultLengths.Select(v => (v, $"{v} min")), l.AssaultMinutes,
                v => Set(s => s with { AssaultMinutes = v }))));
        }
        if (l.HasRaid)
        {
            items.Add(Toggle("check:raid", $"Raid : un Colosse neutre garde un coin de la carte ({Raids.Bounty:0} crédits au premier qui l'abat)", l.Raid,
                on => Set(s => s with { Raid = on })));
        }
        if (l.HasFog) items.Add(Toggle("check:fog", "Brouillard de guerre", l.Fog, on => Set(s => s with { Fog = on })));
        // Over the network, a battle may be watched by those without a seat.
        if (Session.Current is { Room: not null } && l.Mode != LobbyMode.Campaign)
        {
            items.Add(Toggle("check:spectators", "Spectateurs : on peut entrer sans place pour regarder la bataille", l.Spectators,
                on => Set(s => s with { Spectators = on })));
        }
        return items.Count == 0 ? null : Section("Règles", [.. items]);
    }

    /// <summary>A check box of the lobby: once toggled, the screen is rebuilt (its summary with it), the focus back on it.</summary>
    CheckBox Toggle(string key, string text, bool on, Action<bool> set)
    {
        var check = HudKit.Check(text, on, v => Callable.From(() => Pick(key, () => set(v))).CallDeferred());
        check.FocusMode = Control.FocusModeEnum.All;
        check.MouseFilter = Control.MouseFilterEnum.Pass;
        check.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        check.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        first ??= check;
        keyed[key] = check;
        return check;
    }

    /// <summary>
    /// A choice among several, as a compact drop-down list: `pick` gets the index chosen, then the
    /// screen is rebuilt with the focus back on it.
    /// </summary>
    OptionButton Drop(string key, string name, IEnumerable<string> items, int selected, Action<int> pick, Func<int, Texture2D>? icon = null)
    {
        var drop = new OptionButton
        {
            CustomMinimumSize = new Vector2(0, 40), FocusMode = Control.FocusModeEnum.All, MouseFilter = Control.MouseFilterEnum.Pass,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, AccessibilityName = name,
        };
        var i = 0;
        foreach (var item in items)
        {
            if (icon is not null) drop.AddIconItem(icon(i), item);
            else drop.AddItem(item);
            i++;
        }
        drop.Selected = selected;
        drop.ItemSelected += at => Callable.From(() => Pick(key, () => pick((int)at))).CallDeferred();
        first ??= drop;
        keyed[key] = drop;
        return drop;
    }

    static readonly Dictionary<uint, Texture2D> swatches = [];

    /// <summary>A small square of a color, with a dark edge, for the color list.</summary>
    static Texture2D Swatch(uint color)
    {
        if (swatches.TryGetValue(color, out var tex)) return tex;
        var img = Image.CreateEmpty(14, 14, false, Image.Format.Rgba8);
        img.Fill(Palette.Bg);
        img.FillRect(new Rect2I(1, 1, 12, 12), Palette.Hex(color));
        return swatches[color] = ImageTexture.CreateFromImage(img);
    }

    /// <summary>The game the lobby makes, in one line beside the launch button.</summary>
    static string Summary(Lobby l)
    {
        var playing = l.Playing.ToList();
        if (l.Resumed) return $"Campagne reprise : {Camps(playing)}";
        static string Count(int n, string word) => $"{n} {word}{(n > 1 ? "s" : "")}";
        var who = l.Mode switch
        {
            LobbyMode.Survival => $"{Count(playing.Count, "joueur")} contre les vagues",
            LobbyMode.Assault => $"{Count(playing.Count(s => s.Team == 1), "défenseur")} contre {Count(playing.Count(s => s.Team == 2), "attaquant")}, {l.AssaultMinutes} min",
            _ => Camps(playing),
        };
        var size = Scenario.MapSizes[l.Size].Name.ToLowerInvariant();
        var where = l.Mode == LobbyMode.Campaign
            ? l.Map == MapKind.Classic ? "galaxie classique" : $"galaxie {size}"
            : l.Map == MapKind.Classic ? $"carte {size} classique" : $"carte {size}";
        var more = new List<string>();
        if (l.Mode == LobbyMode.Zones) more.Add($"objectif {l.ZoneTarget}");
        if (l.HasRaid && l.Raid) more.Add("raid");
        if (l.HasFog && !l.Fog) more.Add("sans brouillard");
        if (l.HasTerrain && l.Terrain != TerrainDensity.Light) more.Add(l.Terrain == TerrainDensity.None ? "sans terrain" : "terrain dense");
        if (l.Mode == LobbyMode.Sandbox) more.Add("IA coupée");
        var name = Modes.First(m => m.Mode == l.Mode).Name;
        return $"{name} : {who}, {where}{string.Concat(more.Select(m => $", {m}"))}";
    }

    /// <summary>The camps at war, by size ("2 contre 2"); everyone alone past two: "4 joueurs, chacun pour soi".</summary>
    static string Camps(List<Seat> playing)
    {
        var sizes = playing.GroupBy(s => s.Team == 0 ? -1 - s.Id : s.Team).Select(g => g.Count()).ToList();
        return sizes.Count > 2 && sizes.TrueForAll(n => n == 1) ? $"{playing.Count} joueurs, chacun pour soi" : string.Join(" contre ", sizes);
    }

    /// <summary>
    /// The map the lobby gives, before launching: the galaxy (planets, lanes, each seat's capital)
    /// or the battlefield (each seat's station, the asteroid fields, the raid's colossus), every
    /// seat at its number and in its color.
    /// </summary>
    static void DrawPreview(Control g, Lobby l)
    {
        var (w, h) = (g.Size.X, g.Size.Y);
        g.DrawRect(new Rect2(0, 0, w, h), Palette.Bg);
        var slots = l.Slots();
        var playing = l.Playing.ToList();
        var marks = new List<(Vector2 At, int Slot, bool Capital)>();
        if (l.Mode == LobbyMode.Campaign)
        {
            var layout = l.Layout();
            var (sx, sy) = (w / layout.W, h / layout.H);
            Vector2 At(PlanetDef p) => new((float)(p.X * sx), (float)(p.Y * sy));
            foreach (var lane in layout.Lanes) g.DrawLine(At(layout.Planets[lane.A]), At(layout.Planets[lane.B]), Palette.Line, 1, true);
            foreach (var p in layout.Planets) g.DrawCircle(At(p), 3.5f, Palette.Muted, true, -1, true);
            for (var k = 0; k < Math.Min(slots.Count, layout.Capitals.Count); k++) marks.Add((At(layout.Planets[layout.Capitals[k]]), k, true));
        }
        else
        {
            // A mode's rules refuse to make a battle that does not hold together yet (a hosted lobby
            // waiting for its second player): no map until it does, as CreateSkirmish would throw.
            var o = l.Skirmish();
            if (o.Mode != BattleMode.Standard && !Scenario.Check(o).Ok)
            {
                g.DrawRect(new Rect2(0.5f, 0.5f, w - 1, h - 1), Palette.Line, false, 1);
                return;
            }
            var world = Scenario.CreateSkirmish(o);
            var (sx, sy) = (w / world.Width, h / world.Height);
            foreach (var f in world.Fields)
            {
                g.DrawCircle(new Vector2((float)(f.X * sx), (float)(f.Y * sy)), (float)Math.Max(2, f.Radius * sx), Palette.Line, true, -1, true);
            }
            Battle.BattleScreen.DrawTerrainMap(g, world.Terrain, sx, sy);
            foreach (var e in world.Entities)
            {
                var at = new Vector2((float)(e.X * sx), (float)(e.Y * sy));
                if (e.Type == "station" && slots.FindIndex(s => s.Side == e.Side) is var k and >= 0) marks.Add((at, k, false));
                // The raid's colossus: a gold diamond.
                if (e.Kind == EntityKind.Ship && e.Side == Faction.Errant && e.Cls == ShipClass.Colossus)
                {
                    g.DrawColoredPolygon([at + new Vector2(0, -6), at + new Vector2(6, 0), at + new Vector2(0, 6), at + new Vector2(-6, 0)], Palette.Of(Faction.Errant));
                }
            }
        }
        foreach (var (at, k, capital) in marks)
        {
            var color = Palette.Hex(slots[k].Color ?? Worlds.DefaultColor(slots[k].Side, slots[k].Race));
            if (capital) g.DrawCircle(at, 6, color, true, -1, true);
            else g.DrawRect(new Rect2(at.X - 5, at.Y - 5, 10, 10), color);
            // The seat's number, as in its row, beside its mark.
            var text = (l.Seats.IndexOf(playing[k]) + 1).ToString(CultureInfo.InvariantCulture);
            var pos = new Vector2(Math.Min(at.X + 8, w - 10), Math.Max(at.Y - 4, 12));
            g.DrawStringOutline(Bold, pos, text, HorizontalAlignment.Left, -1, 13, 4, Palette.Bg);
            g.DrawString(Bold, pos, text, HorizontalAlignment.Left, -1, 13, color);
        }
        g.DrawRect(new Rect2(0.5f, 0.5f, w - 1, h - 1), Palette.Line, false, 1);
    }
}
