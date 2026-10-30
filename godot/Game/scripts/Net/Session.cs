// A game played over the network, from the lobby to the end of its battles: one node under
// Main, kept across screens, that polls the connection every frame. Three roles: a host who
// plays (its lobby in the menu, its battle in the battle screen), a dedicated server (the
// same, with no seat, no screen and no window: `--server`), and a player who joined either.
// The host keeps the lobby (LobbyRoom) and the battle (BattleHost); a player keeps copies.
// The battle half is in Session.Battle.cs; the messages in Sim/Net/Protocol.cs.
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

    public static Session? Current { get; private set; }
    /// <summary>Bumped at every change from the network that the menu shows: it rebuilds when it sees a new one.</summary>
    public static int Changes { get; private set; }
    /// <summary>Why the last game over the network ended, for the menu to say once; null after a normal leave.</summary>
    public static string? Farewell { get; set; }

    /// <summary>The player's name: --name, or the options'.</summary>
    public static string PlayerName => LobbyRoom.CleanName(Dev.Name ?? Settings.Current.PlayerName);

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

    Transport net = null!;
    BeaconCaller? caller;
    /// <summary>Host: peers connected that have not said Hello yet, and since when.</summary>
    readonly Dictionary<int, double> strangers = [];
    double clock;
    /// <summary>This machine's player's name; for a player, the address it joined.</summary>
    string name = "", target = "";

    /// <summary>Hosts a game of a lobby, playing in its host seat; the reason it cannot, or null.</summary>
    public static string? Host(Lobby lobby, int port)
    {
        var s = new Session { As = Role.Host, Port = port, name = PlayerName };
        s.Game = $"Partie de {s.name}";
        s.Room = new LobbyRoom(lobby) { Members = [new Member(Lobby.Host, s.name)] };
        return s.Listen();
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
        Engine.MaxFps = 60;
        var error = Serve(DevMode, Dev.Name ?? "Serveur Sandbox Ship", Dev.Port);
        if (error is null) return;
        GD.PrintErr(error);
        Main.Instance.QuitGame();
    }

    /// <summary>The lobby of --mode, for --host and --server: a skirmish by default.</summary>
    public static LobbyMode DevMode => Dev.Mode switch
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
        clock += delta;
        net.Poll(Came, Went, Received, Broken);
        if (Current != this) return;
        if (As == Role.Client)
        {
            if (Room is null && net.Status == MultiplayerPeer.ConnectionStatus.Disconnected) Close($"Impossible de joindre {target}.");
        }
        else
        {
            if (battle is null && Room is { } room) caller?.Tick(delta, () => Announce(room));
            foreach (var (peer, since) in strangers.ToList()) if (clock - since > HelloWait) Refuse(peer, "Pas de réponse.");
        }
        BattleFrame(delta);
        DevAuto();
    }

    LanGame Announce(LobbyRoom room)
    {
        var seats = room.Lobby.Seats;
        return new LanGame(Protocol.Version, Ui.Menu.Version, Game, room.Lobby.Mode, seats.Count(s => s.Kind == SeatKind.Human),
            seats.Count(s => s.Kind is SeatKind.Human or SeatKind.Open), Port);
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
        Log($"{member.Name} est parti");
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
            var why = Protocol.Mismatch(hello, Ui.Menu.Version) ?? (battle is not null ? "Une bataille est en cours : reviens à la fin." : room.Join(from, hello.Name));
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
            case Message.Pause pause: SetPause(pause.On, room.NameOf(from)); break;
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
                FromHostInBattle(m);
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

    /// <summary>The game is over for this machine: shown on the menu, back from a battle if one was up.</summary>
    void Close(string why)
    {
        Farewell = why;
        Log(why);
        var inBattle = link is not null;
        End();
        if (inBattle) Main.Instance.ShowMenu();
    }

    void End()
    {
        if (Current != this) return;
        Current = null;
        link = null;
        battle = null;
        net.Close();
        caller?.Close();
        Changes++;
        QueueFree();
    }

    static void Log(string text) => GD.Print($"[réseau] {text}");

    /// <summary>The dev runs (--host, --join): the player is ready once seated, the leader launches once everyone is.</summary>
    bool devReady, devLaunched;

    void DevAuto()
    {
        if (Room is null || link is not null || battle is not null || (!Dev.Host && Dev.Join is null)) return;
        var own = Room.Lobby.Seats.Find(s => s.Kind == SeatKind.Human && s.Peer == Me);
        if (!Hosting && own is { Ready: false } && !devReady)
        {
            devReady = true;
            Edit(new LobbyRequest.SetReady(true));
        }
        if (Leads && !devLaunched && Room.Members.Exists(m => m.Peer != Lobby.Host && m.Peer != Me) && Room.Lobby.Problems().Count == 0)
        {
            devLaunched = true;
            Launch();
        }
    }
}
