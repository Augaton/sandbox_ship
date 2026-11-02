// The menu's side of a game over the network (Network.Session): the multiplayer screen (every game
// within reach in one list, hosting locally or online, joining by address), hosting a lobby, and
// the lobby shared with the players who joined, where everyone sees the same seats live. Every
// change to a lobby goes through Edit, local games included: LobbyRules decides who may make it.
using Godot;
using SandboxShip.Core;
using SandboxShip.Network;
using SandboxShip.Sim;
using SandboxShip.Sim.Net;

namespace SandboxShip.Ui;

public partial class Menu
{
    /// <summary>The modes screen was opened to host a game: its lobby will be open to players; online, through the online server.</summary>
    bool hosting, online;
    string? hostError, joinStatus;
    /// <summary>The lobby shown is a host's this machine joined: when that game ends, the multiplayer screen says why.</summary>
    bool joined;
    BeaconListener? listener;
    MasterList? master;
    IReadOnlyList<(string Address, LanGame Game)> heard = [];
    VBoxContainer? gamesBox;
    Label? masterLine;
    Button? onlineButton, joinButton;
    /// <summary>The multiplayer screen's rows, by game, and the one picked.</summary>
    readonly Dictionary<string, (Button Button, Listed Game)> rows = [];
    string? picked;
    int seenChanges = -1;
    /// <summary>The dev runs (--host, --join) start their game once, not each time the menu comes back.</summary>
    static bool devStarted;
    /// <summary>--join=online: a relayed game to pick on the list, then join; when it was picked.</summary>
    bool devJoining;
    double devPicked = double.NaN;

    static double Now => Time.GetTicksMsec() / 1000.0;

    /// <summary>This machine's peer in the lobby shown: Lobby.Host, unless it joined someone's game.</summary>
    int Me => Session.Current?.Me ?? Lobby.Host;
    /// <summary>The host's rights over the lobby shown: always in a game played alone.</summary>
    bool Leads => Session.Current?.Leads ?? true;
    /// <summary>A seat this machine may change: any for the one who leads, else only its own.</summary>
    bool Mine(Seat seat) => Leads || seat.Kind == SeatKind.Human && seat.Peer == Me;

    /// <summary>A change to the lobby shown: applied at once with every right, or by the game over the network.</summary>
    void Edit(LobbyRequest request)
    {
        if (Session.Current is { Room: not null } net) net.Edit(request);
        else LobbyRules.Apply(Current, Lobby.Host, true, request);
    }

    /// <summary>A change to the map or the rules.</summary>
    void Set(Func<LobbySettings, LobbySettings> change) => Edit(new LobbyRequest.Settings(change(LobbySettings.Of(Current))));

    /// <summary>The modes, for a new game; or, from the multiplayer screen, to host one locally or online.</summary>
    void OpenModes(bool host, bool online = false)
    {
        (hosting, this.online, hostError) = (host, host && online, null);
        if (host) Show(Screen.Modes);
        else Open(Screen.Modes, "new");
    }

    /// <summary>Hosts a game of a mode: you, then a seat open to a player who joins. False, with the reason shown, if it cannot start.</summary>
    bool HostLobby(LobbyMode m)
    {
        var l = Lobby.Create(m, NewSeed());
        l.SetKind(l.Seats[1], SeatKind.Open);
        hostError = Session.Host(l, Dev.Port, online);
        seenChanges = Session.Changes;
        if (hostError is not null)
        {
            if (screen == Screen.Modes) Rebuild($"mode:{m}");
            return false;
        }
        lobbies.TryAdd(m, Lobby.Create(m, NewSeed()));
        return true;
    }

    /// <summary>Hosts the campaign saved over the network again: its lobby keeps each player's seat for them.</summary>
    void HostResume()
    {
        var saved = GalaxySave.Load(Campaign.GalaxyScreen.NetSavePath);
        hostError = saved is null ? "Sauvegarde de la campagne illisible." : Session.Resume(saved, Dev.Port, online);
        seenChanges = Session.Changes;
        if (hostError is not null)
        {
            if (screen == Screen.Modes) Rebuild("resume");
            return;
        }
        mode = LobbyMode.Campaign;
        lobbies.TryAdd(mode, Lobby.Create(mode, NewSeed()));
        if (screen == Screen.Modes) Show(Screen.Lobby);
    }

    /// <summary>
    /// The screen the menu opens on: the lobby of the game under way (back from its battle), the
    /// multiplayer screen saying why the last one ended, or the dev runs' game.
    /// </summary>
    Screen NetStart(Screen start)
    {
        seenChanges = Session.Changes;
        if (Session.Current is { } s)
        {
            (hosting, online, joined, opener) = (s.Hosting, s.Hosting && s.Online, !s.Hosting, "multi");
            if (s.Room is null) return Screen.Multiplayer;
            mode = s.Room.Lobby.Mode;
            lobbies.TryAdd(mode, Lobby.Create(mode, NewSeed()));
            return Screen.Lobby;
        }
        if (Session.Farewell is { } why)
        {
            (joinStatus, Session.Farewell, opener) = (why, null, "multi");
            return Screen.Multiplayer;
        }
        if (devStarted || !Dev.Host && Dev.Join is null) return start;
        devStarted = true;
        opener = "multi";
        if (Dev.Host)
        {
            (hosting, online) = (true, Dev.Online);
            if (Dev.Resume)
            {
                HostResume();
                return hostError is null ? Screen.Lobby : Screen.Modes;
            }
            if (!HostLobby(Session.DevMode)) return Screen.Modes;
            mode = Session.DevMode;
            return Screen.Lobby;
        }
        if (Dev.Join == "online") devJoining = true;
        else JoinAt(Dev.Join!);
        return Screen.Multiplayer;
    }

    /// <summary>Every frame: the games heard and listed, and what the game over the network changed.</summary>
    void NetTick()
    {
        if (screen != Screen.Multiplayer)
        {
            listener?.Close();
            master?.Close();
            (listener, master) = (null, null);
        }
        else
        {
            var now = Now;
            // What the local network says changes the list for sure (its note too); a list from the online server, maybe only its pings.
            var heardNew = listener is not null && listener.Poll(now, out heard);
            var listed = master is not null && master.Poll(now);
            if (listed) ShowMaster();
            if (heardNew || listed) FillGames(heardNew);
            DevJoinOnline(now);
        }
        if (Session.Changes == seenChanges) return;
        seenChanges = Session.Changes;
        var s = Session.Current;
        if (s is null && Session.Farewell is { } why) (joinStatus, Session.Farewell) = (why, null);
        switch (screen)
        {
            case Screen.Multiplayer when s?.Room is not null:
                mode = s.Room.Lobby.Mode;
                lobbies.TryAdd(mode, Lobby.Create(mode, NewSeed()));
                joined = true;
                Show(Screen.Lobby);
                break;
            case Screen.Multiplayer:
                Rebuild(lastKey);
                break;
            // The game ended under the lobby: the host left, or the online server let this host's game go.
            case Screen.Lobby when (joined || online) && s?.Room is null:
                (joined, hosting, online) = (false, false, false);
                Show(Screen.Multiplayer);
                break;
            case Screen.Lobby when s?.Room is not null:
                Rebuild(lastKey);
                break;
        }
    }

    /// <summary>Esc or "Retour" from a screen of the network: leaving the game shown, or the join under way.</summary>
    bool NetBack()
    {
        switch (screen)
        {
            case Screen.Multiplayer:
                Session.Current?.Leave();
                (joinStatus, devJoining) = (null, false);
                return false;
            case Screen.Modes when hosting:
                var from = online ? "act:host-online" : "act:host-local";
                (hosting, online, hostError) = (false, false, null);
                Show(Screen.Multiplayer);
                Settle(keyed.GetValueOrDefault(from), 0);
                return true;
            case Screen.Lobby when Session.Current is { } s:
                s.Leave();
                (joined, seenChanges) = (false, Session.Changes);
                if (!s.Hosting)
                {
                    Show(Screen.Multiplayer);
                    return true;
                }
                Show(Screen.Modes);
                Settle(keyed.GetValueOrDefault($"mode:{mode}"), 0);
                return true;
        }
        return false;
    }

    /// <summary>The lobby's line under its title, over the network: where the host can be joined, or whose game this is.</summary>
    string? NetTitle()
    {
        if (Session.Current is not { Room: not null } s) return null;
        if (!s.Hosting) return $"{s.Game}{(s.Leads ? " : tu as la main sur le salon" : ", réglée par l'hôte")}. Échap : quitter";
        if (s.Online)
        {
            return s.Status.Length > 0 ? $"{s.Status} Échap : fermer la partie"
                : "Partie en ligne : tous les joueurs la trouvent dans « Multijoueur », sans rien ouvrir. Échap : fermer la partie";
        }
        // The home network's addresses first: 172.16 to 172.31 are as often a container's bridge.
        static int Rank(string a) => a.StartsWith("192.168.", StringComparison.Ordinal) ? 0 : a.StartsWith("10.", StringComparison.Ordinal) ? 1 : a.StartsWith("172.", StringComparison.Ordinal) ? 3 : 2;
        var ips = IP.GetLocalAddresses().Where(a => a.Contains('.') && !a.StartsWith("127.", StringComparison.Ordinal) && !a.StartsWith("169.254.", StringComparison.Ordinal))
            .OrderBy(Rank).Take(2).ToList();
        var at = ips.Count > 0 ? $" à l'adresse {string.Join(" ou ", ips.Select(ip => $"{ip}:{s.Port}"))}" : $" sur le port {s.Port}";
        return $"Partie hébergée{at} : les joueurs la trouvent dans « Multijoueur ». Échap : fermer la partie";
    }

    /// <summary>A player's "ready", once seated in a host's lobby.</summary>
    Button? ReadyButton(Lobby l)
    {
        if (Session.Current is not { Hosting: false } s || l.Seats.Find(x => x.Kind == SeatKind.Human && x.Peer == s.Me) is not { } own) return null;
        var b = Btn(own.Ready ? "Prêt · annuler" : "Je suis prêt", () => Pick("act:ready", () => Edit(new LobbyRequest.SetReady(!own.Ready))),
            own.Ready ? On : Leads ? null : Primary, "act:ready");
        b.TooltipText = own.Ready ? "Tu n'es plus prêt" : "La partie démarre quand tous les joueurs sont prêts";
        return b;
    }

    /// <summary>
    /// Without the host's rights, a section shows its settings, none of which can be changed:
    /// its buttons take no pointer nor focus, but keep their look, so that the choices made show.
    /// </summary>
    Control Locked(Control section)
    {
        if (!Leads) Lock(section);
        return section;
        static void Lock(Node n)
        {
            if (n is BaseButton b)
            {
                b.MouseFilter = Control.MouseFilterEnum.Ignore;
                b.FocusMode = Control.FocusModeEnum.None;
            }
            if (n is LineEdit e)
            {
                e.Editable = false;
                e.FocusMode = Control.FocusModeEnum.None;
            }
            foreach (var child in n.GetChildren()) Lock(child);
        }
    }

    // The multiplayer screen.

    /// <summary>Where a game was found, which says how it is joined: each in its own color on the list.</summary>
    enum Found { Lan, Online, Dedicated }

    /// <summary>A game on the list: joined at Address (local network, dedicated server), or through the online server (Room). Ping in ms, -1 unknown.</summary>
    sealed record Listed(string Key, Found Kind, LanGame Game, bool Playing, string Address, int Room, int Ping);

    static (string Name, Color Color) KindOf(Found f) => f switch
    {
        Found.Lan => ("Réseau local", Palette.Ok),
        Found.Online => ("En ligne", Palette.Shield),
        _ => ("Serveur dédié", Palette.Hex(0xc792ea)),
    };

    /// <summary>The list's columns after the game's name: mode, players, kind, ping.</summary>
    static readonly float[] ColumnWidths = [170, 80, 130, 70];

    void MultiplayerScreen()
    {
        Titled("Multijoueur", "Rejoins une partie de la liste, ou héberge la tienne. Échap : retour");
        listener ??= new BeaconListener();
        master ??= new MasterList(Session.MasterAddress, Now);

        var status = new HBoxContainer();
        status.AddThemeConstantOverride("separation", 12);
        masterLine = Paragraph("", Palette.Muted, 14);
        masterLine.CustomMinimumSize = new Vector2(280, 0);
        masterLine.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        masterLine.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        status.AddChild(masterLine);
        var refresh = Btn("Actualiser", () =>
        {
            master?.Refresh(Now);
            ShowMaster();
        }, key: "act:refresh");
        refresh.TooltipText = "Demande la liste au serveur en ligne ; elle se met aussi à jour d'elle-même";
        status.AddChild(refresh);

        gamesBox = new VBoxContainer();
        gamesBox.AddThemeConstantOverride("separation", 4);
        body.AddChild(Section("Parties", status, Legend(), Columns(), gamesBox));

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var input = new LineEdit
        {
            Text = Settings.Current.JoinAddress, PlaceholderText = $"adresse:{Protocol.DefaultPort}", CustomMinimumSize = new Vector2(280, 40),
            AccessibilityName = "Adresse de l'hôte",
        };
        input.TextSubmitted += text => JoinAt(text);
        row.AddChild(input);
        row.AddChild(Btn("Rejoindre", () => JoinAt(input.Text), key: "act:address"));
        body.AddChild(Section("Par adresse", row));

        var hosts = new HBoxContainer();
        hosts.AddThemeConstantOverride("separation", 8);
        hosts.AddChild(Btn("Héberger en local", () => OpenModes(true), key: "act:host-local"));
        onlineButton = Btn("Héberger en ligne", () => OpenModes(true, true), key: "act:host-online");
        hosts.AddChild(onlineButton);
        body.AddChild(Section("Héberger", hosts, Note($"En local : les joueurs de ton réseau te trouvent dans cette liste ; par Internet, ouvre le port {Protocol.DefaultPort} "
            + $"(UDP) vers ta machine. En ligne : ta partie passe par le serveur en ligne, rien à ouvrir. Tu joues sous le nom « {Session.PlayerName} » : "
            + $"il se change dans les options{(OS.IsDebugBuild() ? ", comme l'adresse du serveur en ligne" : "")}.")));

        var connecting = Session.Current is { Room: null } s ? s.Status : null;
        var line = Paragraph(connecting ?? joinStatus ?? "", connecting is not null ? Palette.Muted : Palette.Danger, 13);
        line.CustomMinimumSize = new Vector2(280, 0);
        line.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        line.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        footer.AddChild(line);
        footer.AddChild(Btn("Retour", Back));
        joinButton = Btn("Rejoindre la partie", JoinPicked, Primary, "act:join");
        footer.AddChild(joinButton);
        FillGames(true);
        ShowMaster();
    }

    /// <summary>The three kinds of games, in their colors.</summary>
    static HBoxContainer Legend()
    {
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", 18);
        foreach (var f in new[] { Found.Lan, Found.Online, Found.Dedicated })
        {
            var (name, color) = KindOf(f);
            var item = new HBoxContainer();
            item.AddThemeConstantOverride("separation", 6);
            item.AddChild(new ColorRect { Color = color, CustomMinimumSize = new Vector2(12, 12), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
            item.AddChild(UiTheme.Text(name, Palette.Muted, 13));
            box.AddChild(item);
        }
        return box;
    }

    /// <summary>The list's heading: the columns' names, over the rows'.</summary>
    static Control Columns()
    {
        var pad = new MarginContainer();
        pad.AddThemeConstantOverride("margin_left", 18);
        pad.AddThemeConstantOverride("margin_right", 12);
        pad.AddChild(Cells(["Partie", "Mode", "Joueurs", "Type", "Ping"], Palette.Muted, 13));
        return pad;
    }

    /// <summary>A row of the list: the name taking what is left, the other columns at their width.</summary>
    static HBoxContainer Cells(string[] texts, Color color, int size, Color? kind = null)
    {
        var cells = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        cells.AddThemeConstantOverride("separation", 10);
        for (var i = 0; i < texts.Length; i++)
        {
            var cell = UiTheme.Text(texts[i], i == 3 && kind is { } k ? k : color, size);
            cell.ClipText = true;
            cell.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            if (i == 0) cell.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            else cell.CustomMinimumSize = new Vector2(ColumnWidths[i - 1], 0);
            if (i == 4) cell.HorizontalAlignment = HorizontalAlignment.Right;
            cells.AddChild(cell);
        }
        return cells;
    }

    /// <summary>The online server's line: whether it answers, at what round trip; hosting online waits for it.</summary>
    void ShowMaster()
    {
        if (masterLine is null || !IsInstanceValid(masterLine)) return;
        var (text, color) = master switch
        {
            { Up: true } m => ($"● Serveur en ligne joignable{(OS.IsDebugBuild() ? $" · {m.Address}" : "")}{(m.Ping >= 0 ? $" · {m.Ping} ms" : "")}", Palette.Ok),
            { Problem: { } why } => ($"● {why} Seules les parties du réseau local s'affichent.", Palette.Danger),
            { } m => ($"● Serveur en ligne{RelayLink.Shown(m.Address)} : connexion…", Palette.Muted),
            _ => ("", Palette.Muted),
        };
        masterLine.Text = text;
        masterLine.AddThemeColorOverride("font_color", color);
        if (onlineButton is null || !IsInstanceValid(onlineButton)) return;
        onlineButton.Disabled = master is not { Up: true };
        onlineButton.TooltipText = onlineButton.Disabled ? "Il faut que le serveur en ligne réponde" : "Ta partie s'affiche chez tous les joueurs, sans port à ouvrir";
    }

    /// <summary>Every game within reach: heard on the local network, listed by the online server. A dedicated server found both ways shows once, at its local address.</summary>
    List<Listed> Games()
    {
        var all = heard.Select(h => new Listed($"lan:{h.Address}", h.Game.Dedicated ? Found.Dedicated : Found.Lan, h.Game, false, h.Address, 0, -1)).ToList();
        foreach (var g in master?.Games ?? [])
        {
            if (g.Relayed) all.Add(new($"online:{g.Room}", Found.Online, g.Game, g.Playing, "", g.Room, g.Ping >= 0 && master!.Ping >= 0 ? g.Ping + master.Ping : -1));
            else if (!heard.Any(h => h.Game.Dedicated && h.Game.Name == g.Game.Name && h.Game.Port == g.Game.Port))
            {
                all.Add(new($"server:{g.Address}", Found.Dedicated, g.Game, g.Playing, g.Address, 0, -1));
            }
        }
        return [.. all.OrderBy(l => l.Kind).ThenBy(l => l.Game.Name, StringComparer.CurrentCulture)];
    }

    /// <summary>Why a game cannot be joined, in a few words; null when it can.</summary>
    string? Unjoinable(Listed l) => l.Game.Protocol != Protocol.Version || l.Game.Version != Version ? $"version {l.Game.Version}"
        : l.Playing ? "en partie" : l.Game.Taken >= l.Game.Seats ? "complète" : null;

    static string ModeName(LobbyMode m) => Modes.FirstOrDefault(x => x.Mode == m).Name ?? "Mode inconnu";

    /// <summary>The list, one row a game, the one picked outlined; the focus stays on its game. Left as it is when its rows would show the same.</summary>
    void FillGames(bool force = false)
    {
        if (gamesBox is null || !IsInstanceValid(gamesBox)) return;
        var games = Games();
        if (!force && games.SequenceEqual(rows.Values.Select(r => r.Game)) && (games.Count > 0 || gamesBox.GetChildCount() > 0)) return;
        var focused = rows.FirstOrDefault(r => IsInstanceValid(r.Value.Button) && r.Value.Button.HasFocus()).Key;
        rows.Clear();
        foreach (var child in gamesBox.GetChildren())
        {
            gamesBox.RemoveChild(child);
            child.QueueFree();
        }
        if (picked is not null && games.All(g => g.Key != picked)) picked = null;
        if (games.Count == 0)
        {
            gamesBox.AddChild(Note(listener?.Problem ?? "Aucune partie pour l'instant : celles du réseau local et du serveur en ligne apparaissent ici d'elles-mêmes."));
        }
        foreach (var g in games) gamesBox.AddChild(Row(g));
        if (focused is not null && rows.TryGetValue(focused, out var again)) again.Button.GrabFocus(!viaKeys);
        Picked();
    }

    Control Row(Listed l)
    {
        var why = Unjoinable(l);
        var (kind, color) = KindOf(l.Kind);
        var mode = ModeName(l.Game.Mode);
        var tile = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var button = Btn("", () => Choose(l.Key), key: l.Key);
        button.CustomMinimumSize = new Vector2(0, 38);
        button.Disabled = why is not null;
        button.AccessibilityName = $"{l.Game.Name}, {mode}, {l.Game.Taken} joueurs sur {l.Game.Seats}, {kind}{(l.Ping >= 0 ? $", {l.Ping} ms" : "")}{(why is not null ? $", {why}" : "")}";
        button.TooltipText = l.Kind == Found.Online ? "Par le serveur en ligne" : l.Address;
        // A double click joins at once.
        button.GuiInput += e =>
        {
            if (e is InputEventMouseButton { DoubleClick: true, ButtonIndex: MouseButton.Left } && why is null) Callable.From(() => JoinListed(l)).CallDeferred();
        };
        tile.AddChild(button);
        var line = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        line.AddThemeConstantOverride("separation", 0);
        line.AddChild(new ColorRect { Color = color, CustomMinimumSize = new Vector2(4, 0), MouseFilter = Control.MouseFilterEnum.Ignore });
        var pad = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        pad.AddThemeConstantOverride("margin_left", 14);
        pad.AddThemeConstantOverride("margin_right", 12);
        var name = why is null ? l.Game.Name : $"{l.Game.Name} · {why}";
        pad.AddChild(Cells([name, mode, $"{l.Game.Taken} / {l.Game.Seats}", kind, l.Ping >= 0 ? $"{l.Ping} ms" : "—"], why is null ? Palette.Text : Palette.Muted, 15, color));
        line.AddChild(pad);
        tile.AddChild(line);
        rows[l.Key] = (button, l);
        return tile;
    }

    /// <summary>A row clicked picks its game; Enter on the game picked joins it.</summary>
    void Choose(string key)
    {
        if (picked == key && viaKeys && rows.TryGetValue(key, out var row))
        {
            JoinListed(row.Game);
            return;
        }
        picked = key;
        Picked();
    }

    /// <summary>The game picked, outlined; "Rejoindre la partie" lit for it.</summary>
    void Picked()
    {
        foreach (var (key, (button, _)) in rows) button.ThemeTypeVariation = key == picked ? On : "";
        if (joinButton is null || !IsInstanceValid(joinButton)) return;
        joinButton.Disabled = picked is null;
        joinButton.TooltipText = picked is null ? "Choisis d'abord une partie dans la liste (un double clic la rejoint aussi)" : "";
    }

    void JoinPicked()
    {
        if (picked is not null && rows.TryGetValue(picked, out var row)) JoinListed(row.Game);
    }

    /// <summary>Joins a listed game: through the online server, or straight at its address.</summary>
    void JoinListed(Listed l)
    {
        if (Unjoinable(l) is not null || Session.Current is not null) return;
        if (l.Kind == Found.Online) joinStatus = Session.JoinOnline(l.Room, l.Game.Name);
        else
        {
            var (host, port) = Relay.Address(l.Address, Protocol.DefaultPort);
            joinStatus = Session.Join(host, port);
        }
        // The screen shows the connection under way, or why it could not start.
        seenChanges = -1;
    }

    /// <summary>--join=online: the first game the online server relays, picked on the list (and captured so), then joined.</summary>
    void DevJoinOnline(double now)
    {
        if (!devJoining || Session.Current is not null) return;
        if (double.IsNaN(devPicked))
        {
            if (rows.Values.FirstOrDefault(r => r.Game.Kind == Found.Online && Unjoinable(r.Game) is null).Game is not { } game) return;
            devPicked = now;
            Choose(game.Key);
            Main.Instance.Snap("liste", 30);
            return;
        }
        if (now - devPicked < 1.5) return;
        devJoining = false;
        JoinPicked();
    }

    void JoinAt(string text)
    {
        var (host, port) = Relay.Address(text, Protocol.DefaultPort);
        if (host.Length == 0)
        {
            joinStatus = "Indique l'adresse de l'hôte.";
            Rebuild("act:address");
            return;
        }
        Settings.Current.JoinAddress = text.Trim();
        Settings.Current.Save();
        joinStatus = Session.Join(host, port);
        // The screen shows the connection under way, or why it could not start.
        seenChanges = -1;
    }

    public override void _ExitTree()
    {
        listener?.Close();
        master?.Close();
    }
}
