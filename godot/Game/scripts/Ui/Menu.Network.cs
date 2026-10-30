// The menu's side of a game over the network (Network.Session): hosting a lobby, the join screen
// (the local network's games and an address field), and the lobby shared with the players who
// joined, where everyone sees the same seats live. Every change to a lobby goes through Edit,
// local games included: LobbyRules decides who may make it.
using Godot;
using SandboxShip.Core;
using SandboxShip.Network;
using SandboxShip.Sim;
using SandboxShip.Sim.Net;

namespace SandboxShip.Ui;

public partial class Menu
{
    /// <summary>The modes screen was opened to host a game: its lobby will be open to players.</summary>
    bool hosting;
    string? hostError, joinStatus;
    /// <summary>The lobby shown is a host's this machine joined: when that game ends, the join screen says why.</summary>
    bool joined;
    BeaconListener? listener;
    IReadOnlyList<(string Address, Beacon Beacon)> heard = [];
    VBoxContainer? gamesBox;
    int seenChanges = -1;
    /// <summary>The dev runs (--host, --join) start their game once, not each time the menu comes back.</summary>
    static bool devStarted;

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

    void OpenModes(bool host)
    {
        (hosting, hostError) = (host, null);
        Open(Screen.Modes, host ? "host" : "new");
    }

    /// <summary>Hosts a game of a mode: you, then a seat open to a player who joins. False, with the reason shown, if it cannot listen.</summary>
    bool HostLobby(LobbyMode m)
    {
        var l = Lobby.Create(m, NewSeed());
        l.SetKind(l.Seats[1], SeatKind.Open);
        hostError = Session.Host(l, Dev.Port);
        seenChanges = Session.Changes;
        if (hostError is not null)
        {
            if (screen == Screen.Modes) Rebuild($"mode:{m}");
            return false;
        }
        lobbies.TryAdd(m, Lobby.Create(m, NewSeed()));
        return true;
    }

    /// <summary>
    /// The screen the menu opens on: the lobby of the game under way (back from its battle), the
    /// join screen saying why the last one ended, or the dev runs' game.
    /// </summary>
    Screen NetStart(Screen start)
    {
        seenChanges = Session.Changes;
        if (Session.Current is { } s)
        {
            (hosting, joined, opener) = (s.Hosting, !s.Hosting, s.Hosting ? "host" : "join");
            if (s.Room is null) return Screen.Join;
            mode = s.Room.Lobby.Mode;
            lobbies.TryAdd(mode, Lobby.Create(mode, NewSeed()));
            return Screen.Lobby;
        }
        if (Session.Farewell is { } why)
        {
            (joinStatus, Session.Farewell, opener) = (why, null, "join");
            return Screen.Join;
        }
        if (devStarted || !Dev.Host && Dev.Join is null) return start;
        devStarted = true;
        if (Dev.Host)
        {
            (hosting, opener) = (true, "host");
            if (!HostLobby(Dev.Mode)) return Screen.Modes;
            mode = Dev.Mode;
            return Screen.Lobby;
        }
        opener = "join";
        JoinAt(Dev.Join!);
        return Screen.Join;
    }

    /// <summary>Every frame: the games heard on the local network, and what the game over the network changed.</summary>
    void NetTick()
    {
        if (listener is not null)
        {
            if (screen != Screen.Join)
            {
                listener.Close();
                listener = null;
            }
            else if (listener.Poll(Time.GetTicksMsec() / 1000.0, out var found))
            {
                heard = found;
                FillGames();
            }
        }
        if (Session.Changes == seenChanges) return;
        seenChanges = Session.Changes;
        var s = Session.Current;
        if (s is null && Session.Farewell is { } why) (joinStatus, Session.Farewell) = (why, null);
        switch (screen)
        {
            case Screen.Join when s?.Room is not null:
                mode = s.Room.Lobby.Mode;
                lobbies.TryAdd(mode, Lobby.Create(mode, NewSeed()));
                joined = true;
                Show(Screen.Lobby);
                break;
            case Screen.Join:
                Rebuild(lastKey);
                break;
            case Screen.Lobby when joined && s?.Room is null:
                joined = false;
                Show(Screen.Join);
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
            case Screen.Join:
                Session.Current?.Leave();
                joinStatus = null;
                return false;
            case Screen.Modes when hosting:
                (hosting, hostError) = (false, null);
                return false;
            case Screen.Lobby when Session.Current is { } s:
                s.Leave();
                (joined, seenChanges) = (false, Session.Changes);
                if (!s.Hosting)
                {
                    Show(Screen.Join);
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
        var ips = IP.GetLocalAddresses().Where(a => a.Contains('.') && !a.StartsWith("127.", StringComparison.Ordinal) && !a.StartsWith("169.254.", StringComparison.Ordinal)).Take(2).ToList();
        var at = ips.Count > 0 ? $" à l'adresse {string.Join(" ou ", ips.Select(ip => $"{ip}:{s.Port}"))}" : $" sur le port {s.Port}";
        return $"Partie hébergée{at} : les joueurs la trouvent dans « Rejoindre ». Échap : fermer la partie";
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

    /// <summary>Without the host's rights, a section shows its settings, none of which can be changed.</summary>
    Control Locked(Control section)
    {
        if (!Leads) Lock(section);
        return section;
        static void Lock(Node n)
        {
            if (n is BaseButton b) b.Disabled = true;
            if (n is LineEdit e) e.Editable = false;
            foreach (var child in n.GetChildren()) Lock(child);
        }
    }

    // The join screen.

    void JoinScreen()
    {
        Titled("Rejoindre une partie", "Une partie du réseau local, ou l'adresse de son hôte. Échap : retour");
        listener ??= new BeaconListener();
        gamesBox = new VBoxContainer();
        gamesBox.AddThemeConstantOverride("separation", 8);
        FillGames();
        body.AddChild(Section("Sur le réseau local", gamesBox));

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var input = new LineEdit
        {
            Text = Settings.Current.JoinAddress, PlaceholderText = $"adresse:{Protocol.DefaultPort}", CustomMinimumSize = new Vector2(300, 40),
            AccessibilityName = "Adresse de l'hôte",
        };
        input.TextSubmitted += text => JoinAt(text);
        row.AddChild(input);
        row.AddChild(Btn("Rejoindre", () => JoinAt(input.Text), Primary, "act:join"));
        body.AddChild(Section("Par adresse", row, Note($"Le port est {Protocol.DefaultPort} si l'hôte n'en a pas choisi d'autre. Sur Internet, l'hôte "
            + "doit ouvrir ce port (UDP) vers sa machine.")));
        body.AddChild(Section("Ton nom", Note($"Les autres joueurs te voient sous le nom « {Session.PlayerName} » : il se change dans les options.")));

        var connecting = Session.Current is { Room: null } s ? s.Status : null;
        var line = Paragraph(connecting ?? joinStatus ?? "", connecting is not null ? Palette.Muted : Palette.Danger, 13);
        line.CustomMinimumSize = new Vector2(280, 0);
        line.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        line.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        footer.AddChild(line);
        footer.AddChild(Btn("Retour", Back));
    }

    /// <summary>The games heard on the local network, one card each; those of another version or full show why they cannot be joined.</summary>
    void FillGames()
    {
        if (gamesBox is null || !IsInstanceValid(gamesBox)) return;
        foreach (var child in gamesBox.GetChildren())
        {
            gamesBox.RemoveChild(child);
            child.QueueFree();
        }
        if (listener?.Problem is { } problem)
        {
            gamesBox.AddChild(Note(problem));
            return;
        }
        if (heard.Count == 0)
        {
            gamesBox.AddChild(Note("Aucune partie trouvée pour l'instant : celles du réseau local apparaissent ici d'elles-mêmes."));
            return;
        }
        foreach (var (address, b) in heard)
        {
            var modeName = Modes.First(m => m.Mode == b.Mode).Name;
            var why = b.Protocol != Protocol.Version || b.Version != Version ? $"Version {b.Version} : il faut la même que la tienne ({Version})."
                : b.Taken >= b.Seats ? "Complète." : null;
            var tile = Tile($"game:{address}", false, () => JoinAt(address), $"{b.Name}, {modeName}, {b.Taken} joueurs sur {b.Seats}, {address}", col =>
            {
                var top = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
                top.AddThemeConstantOverride("separation", 10);
                var name = UiTheme.Text(b.Name);
                name.AddThemeFontOverride("font", Bold);
                name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                top.AddChild(name);
                top.AddChild(UiTheme.Text($"{b.Taken} / {b.Seats} joueurs", Palette.Accent, 13));
                col.AddChild(top);
                col.AddChild(Small($"{modeName} · {address}"));
                if (why is not null) col.AddChild(UiTheme.Text(why, Palette.Danger, 13));
            });
            ((Button)tile.GetChild(0)).Disabled = why is not null;
            gamesBox.AddChild(tile);
        }
    }

    void JoinAt(string text)
    {
        var (host, port) = Address(text.Trim());
        if (host.Length == 0)
        {
            joinStatus = "Indique l'adresse de l'hôte.";
            Rebuild("act:join");
            return;
        }
        Settings.Current.JoinAddress = text.Trim();
        Settings.Current.Save();
        joinStatus = Session.Join(host, port);
        // The screen shows the connection under way, or why it could not start.
        seenChanges = -1;
    }

    /// <summary>"host", "host:port" or "[ipv6]:port"; without a port, the default one.</summary>
    static (string Host, int Port) Address(string text)
    {
        static int? PortOf(string s) => int.TryParse(s, out var p) && p is > 0 and < 65536 ? p : null;
        if (text.StartsWith('[') && text.IndexOf(']') is var end and > 0)
        {
            return (text[1..end], text.Length > end + 2 && text[end + 1] == ':' && PortOf(text[(end + 2)..]) is int v6 ? v6 : Protocol.DefaultPort);
        }
        var colon = text.LastIndexOf(':');
        if (colon > 0 && text.IndexOf(':') == colon && PortOf(text[(colon + 1)..]) is int port) return (text[..colon], port);
        return (text, Protocol.DefaultPort);
    }

    public override void _ExitTree() => listener?.Close();
}
