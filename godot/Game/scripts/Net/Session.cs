// A game played over the network, from the lobby to the end of its battles: one node under
// Main, kept across screens, that polls the connection every frame. Three roles: a host who
// plays (its lobby in the menu, its battle in the battle screen), a dedicated server (the
// same, with no seat, no screen and no window: `--server`), and a player who joined either.
// The connection is ENet straight between the machines (Transport), or the online server's
// relay (RelayTransport: "Héberger en ligne", and the games it lists); the rest is the same.
// The host keeps the lobby (LobbyRoom) and the battle (BattleHost); a player keeps copies.
// The battle half is in Session.Battle.cs, the campaign half (a host who plays only) in
// Session.Campaign.cs; the messages in Sim/Net/Protocol.cs.
using Godot;
using SandboxShip.Core;
using SandboxShip.Sim;
using SandboxShip.Sim.Net;

namespace SandboxShip.Network;

public sealed partial class Session : Node
{
    public enum Role { Host, Server, Client }

    /// <summary>A peer that does not say Hello in this many seconds is let go.</summary>
    const double HelloWait = 5;
    /// <summary>A dedicated server's frames a second: in battle, one step each; between battles, enough for the lobby and the beacon.</summary>
    const int BattleFps = 60, IdleFps = 15;

    public static Session? Current { get; private set; }
    /// <summary>Bumped at every change from the network that the menu shows: it rebuilds when it sees a new one.</summary>
    public static int Changes { get; private set; }
    /// <summary>Why the last game over the network ended, for the menu to say once; null after a normal leave.</summary>
    public static string? Farewell { get; set; }

    /// <summary>The player's name: --name, or the options'.</summary>
    public static string PlayerName => LobbyRoom.CleanName(Dev.Name ?? Settings.Current.PlayerName);
    /// <summary>The online server's address: --master, or the options'.</summary>
    public static string MasterAddress => Dev.Master ?? Settings.Current.MasterAddress;

    public Role As { get; private set; }
    /// <summary>The lobby and who is in it; for a player, null until the host welcomes it.</summary>
    public LobbyRoom? Room { get; private set; }
    /// <summary>This machine's peer in the lobby: Lobby.Host for the host, the host's number for a player, none (-1) on a server.</summary>
    public int Me { get; private set; } = Lobby.Host;
    /// <summary>The game's name, as the host calls it.</summary>
    public string Game { get; private set; } = "";
    public int Port { get; private set; }
    /// <summary>A player's connection, in words, until the host welcomes it.</summary>
    public string Status { get; private set; } = "";

    /// <summary>This machine has the host's rights over the lobby: the host, or a dedicated server's leader.</summary>
    public bool Leads => Room is not null && Room.Leader == Me;
    public bool Hosting => As != Role.Client;
    /// <summary>Played through the online server: nobody opens a port, and a host's game is listed there while it lasts.</summary>
    public bool Online => relay is not null;

    ITransport net = null!;
    /// <summary>The same connection, when it goes through the online server; a dedicated server's entry there (--public).</summary>
    RelayTransport? relay;
    MasterListing? listing;
    BeaconCaller? caller;
    /// <summary>Host: peers connected that have not said Hello yet, and since when.</summary>
    readonly Dictionary<int, double> strangers = [];
    double clock;
    /// <summary>This machine's player's name; for a player, the address it joined.</summary>
    string name = "", target = "";

    /// <summary>Hosts a game of a lobby, playing in its host seat, on a port or (online) through the online server; the reason it cannot, or null.</summary>
    public static string? Host(Lobby lobby, int port, bool online = false)
    {
        var s = new Session { As = Role.Host, Port = online ? 0 : port, name = PlayerName };
        s.Game = $"Partie de {s.name}";
        s.Room = new LobbyRoom(lobby) { Members = [new Member(Lobby.Host, s.name)] };
        return online ? s.ListenOnline() : s.Listen();
    }

    /// <summary>A dedicated server of a mode: nobody plays on it, the first player in leads.</summary>
    public static string? Serve(LobbyMode mode, string game, int port)
    {
        var lobby = Lobby.Create(mode, Random.Shared.Next(1, 1_000_000));
        lobby.Dedicated = true;
        foreach (var seat in lobby.Seats) lobby.SetKind(seat, SeatKind.Open);
        var s = new Session { As = Role.Server, Port = port, Game = game, Me = -1, Room = new LobbyRoom(lobby) };
        return s.Listen();
    }

    /// <summary>`--server`: a dedicated server of --mode on --port, named --name. It quits if it cannot listen.</summary>
    public static void RunServer()
    {
        // Sixty frames a second are plenty for steps of 1/60 s; without a cap, an idle server spins a core.
        Engine.MaxFps = BattleFps;
        // The campaign needs a host who plays: its galaxy waits for its players' choices.
        var error = Dev.Campaign ? "La campagne se joue sur la machine d'un joueur : « Multijoueur », puis héberger."
            : Serve(DevMode, Dev.Name ?? "Serveur Sandbox Ship", Dev.Port);
        if (error is null)
        {
            // --public: listed by the online server too, joined at this machine's address.
            if (Dev.Public is { } address) Current!.listing = new MasterListing(MasterAddress, address);
            return;
        }
        GD.PrintErr(error);
        Main.Instance.QuitGame();
    }

    /// <summary>The lobby of --mode, for --host and --server: a skirmish by default.</summary>
    public static LobbyMode DevMode => Dev.Campaign ? LobbyMode.Campaign : Dev.Mode switch
    {
        BattleMode.Survival => LobbyMode.Survival,
        BattleMode.Zones => LobbyMode.Zones,
        BattleMode.Assault => LobbyMode.Assault,
        BattleMode.Regicide => LobbyMode.Regicide,
        _ => LobbyMode.Skirmish,
    };

    string? Listen()
    {
        net = Transport.Listen(Port, Lobby.MaxSeats + 2, out var error)!;
        if (error is not null) return error;
        caller = new BeaconCaller();
        Begin();
        Log($"« {Game} » à l'écoute sur le port {Port}");
        return null;
    }

    /// <summary>A host's game listed on the online server: it opens there, and its players come through it.</summary>
    string? ListenOnline()
    {
        relay = RelayTransport.Host(MasterAddress, Announce(Room!), out var error);
        if (relay is null) return error;
        net = relay;
        Status = $"Inscription au serveur en ligne {MasterAddress}…";
        Begin();
        Log($"« {Game} » ouverte par le serveur en ligne {MasterAddress}");
        return null;
    }

    /// <summary>Joins a game the online server lists, through it; the menu follows the connection as for a host joined at its address.</summary>
    public static string? JoinOnline(int room, string game)
    {
        var s = new Session { As = Role.Client, name = PlayerName, Me = -1, target = $"« {game} » par le serveur en ligne" };
        s.Status = $"Connexion à {s.target}…";
        s.relay = RelayTransport.Join(MasterAddress, room, out var error);
        if (s.relay is null) return error;
        s.net = s.relay;
        s.Begin();
        return null;
    }

    /// <summary>Joins a host at an address; the menu follows the connection through Status, then the lobby.</summary>
    public static string? Join(string address, int port)
    {
        var s = new Session { As = Role.Client, Port = port, name = PlayerName, Me = -1, target = $"{address}:{port}" };
        s.Status = $"Connexion à {s.target}…";
        s.net = Transport.Join(address, port, out var error)!;
        if (error is not null) return error;
        s.Begin();
        return null;
    }

    void Begin()
    {
        Current?.Leave();
        Current = this;
        Farewell = null;
        Main.Instance.AddChild(this);
        Changes++;
    }

    public override void _Process(double delta)
    {
        // Left this frame (the game quitting before this node's turn): its connection is closed.
        if (Current != this) return;
        clock += delta;
        net.Poll(Came, Went, Received, Broken);
        if (Current != this) return;
        if (As == Role.Server) Engine.MaxFps = battle is null ? IdleFps : BattleFps;
        if (As == Role.Client)
        {
            if (Room is null && net.Status == MultiplayerPeer.ConnectionStatus.Disconnected) Close($"Impossible de joindre {target}.");
        }
        else
        {
            if (net.Lost is { } lost)
            {
                Close(lost);
                return;
            }
            if (battle is null && campaign is null && Room is { } room) caller?.Tick(delta, () => Announce(room));
            if (Room is { } listed)
            {
                relay?.Advertise(Announce(listed), InBattle || InCampaign);
                listing?.Tick(clock, Announce(listed), InBattle);
            }
            if (relay is { Room: > 0 } && Status.Length > 0)
            {
                Status = "";
                Log($"Partie inscrite au serveur en ligne (n° {relay.Room})");
                Changes++;
            }
            foreach (var (peer, since) in strangers.ToList()) if (clock - since > HelloWait) Refuse(peer, "Pas de réponse.");
        }
        BattleFrame(delta);
        CampaignFrame(delta);
        DevAuto();
    }

    LanGame Announce(LobbyRoom room)
    {
        var seats = room.Lobby.Seats;
        return new LanGame(Protocol.Version, Ui.Menu.Version, Game, room.Lobby.Mode, seats.Count(s => s.Kind == SeatKind.Human),
            seats.Count(s => s.Kind is SeatKind.Human or SeatKind.Open), Port, As == Role.Server);
    }

    public void Send(Message m)
    {
        if (As == Role.Client) net.Send(Transport.HostPeer, m);
    }

    /// <summary>Host: a message to every remote player.</summary>
    void Broadcast(Message m)
    {
        if (Room is null) return;
        foreach (var member in Room.Members) if (member.Peer != Lobby.Host) net.Send(member.Peer, m);
    }

    void Came(int peer)
    {
        if (Hosting) strangers[peer] = clock;
        else if (peer == Transport.HostPeer)
        {
            net.Send(peer, Protocol.Hello(Ui.Menu.Version, name));
            Status = "Connecté, en attente de l'hôte…";
            Changes++;
        }
    }

    void Went(int peer)
    {
        if (!Hosting)
        {
            if (peer == Transport.HostPeer) Close("L'hôte a quitté la partie.");
            return;
        }
        strangers.Remove(peer);
        if (Room?.Members.Find(m => m.Peer == peer) is not { } member) return;
        Room.Leave(peer);
        Log($"{member.Name} a quitté la partie");
        PlayerLeft(peer, member.Name);
        Broadcast(new Message.Room(Room.Lobby, Room.Members));
        Changes++;
    }

    void Broken(int peer, Exception e)
    {
        GD.PushWarning($"Message illisible de {peer} : {e.Message}");
        if (Hosting) Refuse(peer, "Message illisible : versions incompatibles ?");
    }

    void Received(int from, Message m)
    {
        if (Hosting) FromPlayer(from, m);
        else if (from == Transport.HostPeer) FromHost(m);
    }

    void FromPlayer(int from, Message m)
    {
        var room = Room!;
        if (m is Message.Hello hello)
        {
            if (!strangers.Remove(from)) return;
            var why = Protocol.Mismatch(hello, Ui.Menu.Version) ?? (battle is not null ? "Une bataille est en cours : reviens à la fin."
                : campaign is not null ? "Une campagne est en cours : elle ne se rejoint pas en route." : room.Join(from, hello.Name));
            if (why is not null)
            {
                Refuse(from, why);
                return;
            }
            net.Send(from, new Message.Welcome(from, Game));
            Log($"{room.NameOf(from)} a rejoint la partie");
            AfterChange();
            Changes++;
            return;
        }
        if (!room.Members.Exists(member => member.Peer == from)) return;
        switch (m)
        {
            case Message.Ask ask:
                if (room.Apply(from, ask.Request))
                {
                    AfterChange();
                    Changes++;
                }
                // Refused: the player's own copy took the change; the lobby as it is puts it back.
                else net.Send(from, new Message.Room(room.Lobby, room.Members));
                break;
            case Message.Launch when from == room.Leader: Launch(); break;
            case Message.Back when from == room.Leader: BackToLobby(); break;
            case Message.Order order: Ordered(from, order.Command); break;
            case Message.Ack ack: (battle ?? fight)?.Acked(from, ack.Snapshot); break;
            case Message.Pause pause: SetPause(pause.On, room.NameOf(from)); break;
            case Message.Plan plan: Planned(from, plan.Command); break;
            case Message.Watch watch: Watched(from, watch); break;
        }
    }

    void FromHost(Message m)
    {
        switch (m)
        {
            case Message.Welcome w:
                (Me, Game, Status) = (w.Peer, w.Game, "");
                break;
            case Message.Bye bye:
                Close(bye.Reason);
                break;
            case Message.Room r:
                var same = Room is not null && Same(Room.Lobby, r.Lobby) && Room.Members.SequenceEqual(r.Members);
                Room = new LobbyRoom(r.Lobby) { Members = [.. r.Members] };
                // Its own change coming back as it was guessed: nothing to show again (a field being typed in keeps its caret).
                if (!same) Changes++;
                break;
            default:
                if (!FromHostInCampaign(m)) FromHostInBattle(m);
                break;
        }
    }

    static bool Same(Lobby a, Lobby b) =>
        System.Text.Json.JsonSerializer.Serialize(a, Lobby.Json) == System.Text.Json.JsonSerializer.Serialize(b, Lobby.Json);

    /// <summary>A change to the lobby from this machine's menu: applied here, and by the host if this is a player.</summary>
    public void Edit(LobbyRequest r)
    {
        if (Room is null) return;
        if (Hosting)
        {
            if (Room.Apply(Me, r)) AfterChange();
            return;
        }
        // Guessed here at once, by the rules the host applies; the host's lobby follows.
        LobbyRules.Apply(Room.Lobby, Me, Leads, r);
        Send(new Message.Ask(r));
    }

    /// <summary>Host, after a change: players moved out of their seat are sent off, everyone gets the lobby.</summary>
    void AfterChange()
    {
        foreach (var peer in Room!.Seatless())
        {
            Room.Leave(peer);
            Refuse(peer, "L'hôte t'a retiré ta place.");
        }
        Broadcast(new Message.Room(Room.Lobby, Room.Members));
    }

    /// <summary>Host: turns a player away or sends it off, telling it why.</summary>
    void Refuse(int peer, string why)
    {
        strangers.Remove(peer);
        net.Send(peer, new Message.Bye(why));
        net.Drop(peer);
        Log($"Joueur {peer} renvoyé : {why}");
    }

    /// <summary>Leaves the game: a player goes (its host hands its side to the AI), a host closes it for everyone.</summary>
    public void Leave()
    {
        if (Hosting) Broadcast(new Message.Bye("L'hôte a quitté la partie."));
        else net.Drop(Transport.HostPeer);
        End();
    }

    /// <summary>The game is over for this machine: shown on the menu, back from a battle or the galaxy if one was up.</summary>
    void Close(string why)
    {
        Farewell = why;
        Log(why);
        Main.Instance.Snap("depart", 90);
        var inGame = link is not null || map is not null;
        End();
        if (inGame) Main.Instance.ShowMenu();
    }

    void End()
    {
        if (Current != this) return;
        Current = null;
        link = null;
        battle = null;
        (campaign, map, fight, fought, front, frontOpts) = (null, null, null, null, null, null);
        net.Close();
        caller?.Close();
        listing?.Close();
        Changes++;
        QueueFree();
    }

    static void Log(string text) => GD.Print($"[réseau] {text}");

    /// <summary>
    /// The dev runs (--host, --join): the player is ready once seated; the leader launches once
    /// everyone has been ready for three seconds, time for a capture of the full lobby.
    /// </summary>
    bool devReady, devLaunched, devWaiting;
    double devAllReady = double.NaN;

    void DevAuto()
    {
        if (Room is null || link is not null || battle is not null || InCampaign || (!Dev.Host && Dev.Join is null)) return;
        var own = Room.Lobby.Seats.Find(s => s.Kind == SeatKind.Human && s.Peer == Me);
        // A resumed campaign's lobby, its players' seats still waiting for them.
        if (Hosting && Room.Lobby.Resumed && !devWaiting)
        {
            devWaiting = true;
            Main.Instance.Snap("salon-repris", 60);
        }
        if (!Hosting && own is { Ready: false } && !devReady)
        {
            devReady = true;
            Edit(new LobbyRequest.SetReady(true));
            Main.Instance.Snap("salon", 60);
        }
        if (!Leads || devLaunched) return;
        var all = Room.Members.Exists(m => m.Peer != Lobby.Host && m.Peer != Me) && Room.Lobby.Problems().Count == 0;
        if (!all) devAllReady = double.NaN;
        else if (double.IsNaN(devAllReady))
        {
            devAllReady = clock;
            Main.Instance.Snap("salon", 60);
        }
        else if (clock - devAllReady >= 3)
        {
            devLaunched = true;
            Launch();
        }
    }
}
