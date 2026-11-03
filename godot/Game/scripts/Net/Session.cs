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
    /// <summary>
    /// The online server's address: --master, else the options' in a developer build (only it
    /// shows the field) and the default in a player's.
    /// </summary>
    public static string MasterAddress => Dev.Master ?? (OS.IsDebugBuild() ? Settings.Current.MasterAddress : Settings.DefaultMaster);

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
    /// <summary>The lines of chat of this game, from the lobby on (Sim/Net/Chat.cs).</summary>
    public ChatLog Chat { get; } = new();
    /// <summary>Seconds since the session began: what a line of chat's time is counted in.</summary>
    public double Clock => clock;

    /// <summary>This machine has the host's rights over the lobby: the host, or a dedicated server's leader.</summary>
    public bool Leads => Room is not null && Room.Leader == Me;
    public bool Hosting => As != Role.Client;
    /// <summary>Played through the online server: nobody opens a port, and a host's game is listed there while it lasts.</summary>
    public bool Online => relay is not null;
    /// <summary>Host: players join only with the game's password.</summary>
    public bool Locked => Hosting && password is not null;

    /// <summary>
    /// Through the online server, how the messages go, in words: a player's with its host,
    /// straight or relayed; a host's, how many players it reaches straight. Null otherwise.
    /// </summary>
    public string? Path => relay is null ? null
        : As == Role.Client ? relay.Straight(Transport.HostPeer) ? "liaison directe avec l'hôte" : "liaison par le serveur en ligne"
        : relay.StraightCount switch { 0 => null, 1 => "un joueur en liaison directe", var n => $"{n} joueurs en liaison directe" };
    int straightSeen;

    ITransport net = null!;
    /// <summary>The same connection, when it goes through the online server; a dedicated server's entry there (--public).</summary>
    RelayTransport? relay;
    MasterListing? listing;
    BeaconCaller? caller;
    /// <summary>Host: peers connected that have not said Hello yet, and since when.</summary>
    readonly Dictionary<int, double> strangers = [];
    /// <summary>Host: how fast each player writes in the chat.</summary>
    readonly ChatGate gate = new();
    /// <summary>How fast each player's lobby requests are taken: each one taken goes to every member, a flood would go to all.</summary>
    readonly ChatGate asks = new(ChatGate.AskBurst, ChatGate.AskEvery);
    /// <summary>Host: the challenge given to each peer that has not said Hello yet (Passwords.cs).</summary>
    readonly Dictionary<int, byte[]> challenges = [];
    /// <summary>Host: peers sent off, and when their connection is cut: a moment after their Bye, so that they read it first.</summary>
    readonly Dictionary<int, double> sentOff = [];
    const double ByeGrace = 0.5;
    /// <summary>The game's password, only in memory: the host's lock, or the one a player joins with; null without one.</summary>
    string? password;
    /// <summary>Host: players who left the game under way, by name, and the seat kept for them until it ends.</summary>
    readonly Dictionary<string, int> away = [];
    /// <summary>A player: it came only to watch.</summary>
    bool watching;

    /// <summary>This machine's player only watches: no seat, no order.</summary>
    public bool Watching => watching;
    /// <summary>The last game joined, to join again after the connection broke (a player back in a game under way takes its seat again).</summary>
    public static Func<string?>? Rejoin { get; private set; }
    /// <summary>The last game joined, in words.</summary>
    public static string RejoinName { get; private set; } = "";
    /// <summary>Lines of chat one who joins gets, the newest.</summary>
    const int History = 12;
    double clock;
    /// <summary>This machine's player's name; for a player, the address it joined.</summary>
    string name = "", target = "";

    /// <summary>
    /// Hosts a game of a lobby, playing in its host seat, on a port or (online) through the online
    /// server, locked with a password if one is given; the reason it cannot, or null.
    /// </summary>
    public static string? Host(Lobby lobby, int port, bool online = false, string? password = null)
    {
        var s = new Session { As = Role.Host, Port = online ? 0 : port, name = PlayerName, password = Passwords.Clean(password) };
        s.Game = $"Partie de {s.name}";
        s.Room = new LobbyRoom(lobby) { Members = [new Member(Lobby.Host, s.name)] };
        return online ? s.ListenOnline() : s.Listen();
    }

    /// <summary>A dedicated server of a mode, locked with a password if one is given: nobody plays on it, the first player in leads.</summary>
    public static string? Serve(LobbyMode mode, string game, int port, string? password = null)
    {
        var lobby = Lobby.Create(mode, Random.Shared.Next(1, 1_000_000));
        lobby.Dedicated = true;
        foreach (var seat in lobby.Seats) lobby.SetKind(seat, SeatKind.Open);
        var s = new Session { As = Role.Server, Port = port, Game = game, Me = -1, Room = new LobbyRoom(lobby), password = Passwords.Clean(password) };
        return s.Listen();
    }

    /// <summary>`--server`: a dedicated server of --mode on --port, named --name. It quits if it cannot listen.</summary>
    public static void RunServer()
    {
        // Sixty frames a second are plenty for steps of 1/60 s; without a cap, an idle server spins a core.
        Engine.MaxFps = BattleFps;
        // The campaign needs a host who plays: its galaxy waits for its players' choices.
        var error = Dev.Campaign ? "La campagne se joue sur la machine d'un joueur : « Multijoueur », puis héberger."
            : Serve(DevMode, Dev.Name ?? "Serveur Sandbox Ship", Dev.Port, Dev.Password);
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
        Log($"« {Game} » à l'écoute sur le port {Port}{(password is not null ? ", avec un mot de passe" : "")}");
        return null;
    }

    /// <summary>A host's game listed on the online server: it opens there, and its players come through it.</summary>
    string? ListenOnline()
    {
        // --direct: a dev run's game offers the direct link from the start.
        if (Dev.Direct) Room!.Lobby.Direct = true;
        relay = RelayTransport.Host(MasterAddress, Announce(Room!), out var error);
        if (relay is null) return error;
        net = relay;
        Status = $"Inscription au serveur en ligne{RelayLink.Shown(MasterAddress)}…";
        Begin();
        Log($"« {Game} » ouverte par le serveur en ligne {MasterAddress}");
        return null;
    }

    /// <summary>
    /// Joins a game the online server lists, through it, with its password if it has one, to play
    /// or only to `watch`, accepting the direct link if `direct`; the menu follows the connection
    /// as for a host joined at its address.
    /// </summary>
    public static string? JoinOnline(int room, string game, string? password = null, bool watch = false, bool direct = false)
    {
        var s = new Session { As = Role.Client, name = PlayerName, Me = -1, target = $"« {game} » par le serveur en ligne", password = Passwords.Clean(password), watching = watch };
        (Rejoin, RejoinName) = (() => JoinOnline(room, game, password, watch, direct), $"« {game} »");
        s.Status = $"Connexion à {s.target}…";
        s.relay = RelayTransport.Join(MasterAddress, room, direct, out var error);
        if (s.relay is null) return error;
        s.net = s.relay;
        s.Begin();
        return null;
    }

    /// <summary>
    /// Joins a host at an address, with the game's password if it has one, to play or only to
    /// `watch`; the menu follows the connection through Status, then the lobby.
    /// </summary>
    public static string? Join(string address, int port, string? password = null, bool watch = false)
    {
        var s = new Session { As = Role.Client, Port = port, name = PlayerName, Me = -1, target = $"{address}:{port}", password = Passwords.Clean(password), watching = watch };
        (Rejoin, RejoinName) = (() => Join(address, port, password, watch), $"{address}:{port}");
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
        // A straight connection up or down: the lobby says how the messages go.
        if (relay is not null && relay.StraightCount != straightSeen)
        {
            straightSeen = relay.StraightCount;
            Changes++;
        }
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
            foreach (var (peer, at) in sentOff.ToList())
            {
                if (clock < at) continue;
                sentOff.Remove(peer);
                net.Drop(peer);
            }
        }
        BattleFrame(delta);
        CampaignFrame(delta);
        DevAuto();
    }

    LanGame Announce(LobbyRoom room)
    {
        var seats = room.Lobby.Seats;
        return new LanGame(Protocol.Version, Ui.Menu.Version, Game, room.Lobby.Mode, seats.Count(s => s.Kind == SeatKind.Human),
            seats.Count(s => s.Kind is SeatKind.Human or SeatKind.Open), Port, As == Role.Server, password is not null, room.Lobby.Direct);
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

    /// <summary>A peer connects: the host gives it a challenge, which the player answers with its Hello (FromHost).</summary>
    void Came(int peer)
    {
        if (Hosting)
        {
            strangers[peer] = clock;
            var challenge = Passwords.Challenge();
            challenges[peer] = challenge;
            net.Send(peer, new Message.Challenge(challenge));
        }
        else if (peer == Transport.HostPeer)
        {
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
        challenges.Remove(peer);
        sentOff.Remove(peer);
        gate.Forget(peer);
        asks.Forget(peer);
        if (Room?.Members.Find(m => m.Peer == peer) is not { } member) return;
        // Gone from a game under way: its seat waits for it, under its name, until the game ends.
        if ((battle is not null || campaign is not null) && !member.Watches
            && Room.Lobby.Seats.Find(s => s.Kind == SeatKind.Human && s.Peer == peer) is { } seat) away[member.Name] = seat.Id;
        Room.Leave(peer);
        Log($"{member.Name} a quitté la partie");
        PlayerLeft(peer, member.Name, member.Watches);
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
        // Sent off: nothing it says counts any more.
        if (sentOff.ContainsKey(from)) return;
        var room = Room!;
        if (m is Message.Hello hello)
        {
            if (!strangers.Remove(from)) return;
            challenges.Remove(from, out var challenge);
            var why = Protocol.Mismatch(hello, Ui.Menu.Version) ?? Unlocked(hello, challenge) ?? Admit(from, hello);
            if (why is not null)
            {
                Refuse(from, why);
                return;
            }
            net.Send(from, new Message.Welcome(from, Game));
            // The chat's recent lines, for one who comes in on a conversation.
            foreach (var l in Chat.Lines.TakeLast(History)) net.Send(from, new Message.Said(l.Who, l.Text));
            Log(room.Watches(from) ? $"{room.NameOf(from)} regarde la partie" : $"{room.NameOf(from)} a rejoint la partie");
            AfterChange();
            CatchUp(from);
            Changes++;
            return;
        }
        if (!room.Members.Exists(member => member.Peer == from)) return;
        switch (m)
        {
            case Message.Ask ask:
                if (!asks.Allow(from, clock)) break;
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
            case Message.Say say: Heard(from, say.Text); break;
            case Message.Spectate s: Spectated(from, s.Side); break;
        }
    }

    /// <summary>
    /// Host: lets in one who said Hello: a player to a free seat, a spectator, or a player who
    /// left the game under way and comes back to its seat under the same name; the reason it cannot, or null.
    /// </summary>
    string? Admit(int from, Message.Hello hello)
    {
        var room = Room!;
        if (hello.Watches) return room.Lobby.Mode == LobbyMode.Campaign ? "Une campagne ne se regarde pas en spectateur." : room.Watch(from, hello.Name);
        if (battle is null && campaign is null) return room.Join(from, hello.Name);
        var name = LobbyRoom.CleanName(hello.Name);
        if (!away.TryGetValue(name, out var seat))
        {
            return battle is not null ? "Une bataille est en cours : reviens à la fin, ou regarde-la en spectateur."
                : "Une campagne est en cours : seuls ceux qui la jouent la rejoignent, sous le même nom.";
        }
        var why = room.Rejoin(from, name, seat);
        if (why is null) away.Remove(name);
        return why;
    }

    /// <summary>
    /// Host: one let in while a game is under way: a spectator starts watching the battle, a
    /// player back takes its side from the AI, and everyone is told; either gets the game's
    /// start, then a whole snapshot.
    /// </summary>
    void CatchUp(int peer)
    {
        var room = Room!;
        if (battle is null && campaign is null) return;
        var name = room.NameOf(peer);
        if (room.Watches(peer))
        {
            if (battle is null) return;
            Spectator(peer);
            net.Send(peer, new Message.Start(room.Lobby));
            if (paused) net.Send(peer, new Message.Pause(true, ""));
            return;
        }
        if (room.Lobby.SideOfPeer(peer) is not { } side) return;
        battle?.Return(peer, side);
        campaign?.Return(peer, side, name);
        net.Send(peer, new Message.Start(room.Lobby));
        if (paused) net.Send(peer, new Message.Pause(true, ""));
        var text = $"{name} est de retour : il reprend sa place.";
        Broadcast(new Message.Notice(text));
        link?.Screen?.Hud.Toast(text);
        Toast(text);
        Log(text);
        campaign?.SendAll();
    }

    /// <summary>
    /// A line of chat from this machine's player: the host relays it at once; a player sends it
    /// to its host, and sees it when the host relays it back. A dedicated server has nobody to write.
    /// </summary>
    public void Say(string text)
    {
        if (Sim.Net.Chat.Clean(text) is not { } line || Room is null) return;
        if (!Hosting) Send(new Message.Say(line));
        else if (As == Role.Host) Spoke(Room.NameOf(Me), line);
    }

    /// <summary>Host: a player's line, relayed unless it writes too fast (it alone is then told to wait).</summary>
    void Heard(int from, string text)
    {
        if (Sim.Net.Chat.Clean(text) is not { } line) return;
        if (gate.Allow(from, clock)) Spoke(Room!.NameOf(from), line);
        else net.Send(from, new Message.Said("", "Trop de messages d'un coup : attends un peu."));
    }

    /// <summary>Host: a line for everyone, into its own chat too; a dedicated server writes it in its log.</summary>
    void Spoke(string who, string line)
    {
        Chat.Add(new ChatLine(who, line, clock));
        Broadcast(new Message.Said(who, line));
        if (As == Role.Server) Log($"{who} : {line}");
    }

    /// <summary>Host: why a player's key does not open the game, or null when it does (or the game has no password).</summary>
    string? Unlocked(Message.Hello hello, byte[]? challenge)
    {
        if (password is null || challenge is not null && Passwords.Opens(password, challenge, hello.Key)) return null;
        return hello.Key.Length == 0 ? "Cette partie est protégée : saisis son mot de passe sur l'écran Multijoueur, puis rejoins-la."
            : "Mot de passe incorrect.";
    }

    void FromHost(Message m)
    {
        switch (m)
        {
            case Message.Challenge c:
                net.Send(Transport.HostPeer, Protocol.Hello(Ui.Menu.Version, name, Passwords.Key(password, c.Nonce), watching));
                break;
            case Message.Welcome w:
                (Me, Game, Status) = (w.Peer, w.Game, "");
                break;
            case Message.Bye bye:
                Close(bye.Reason);
                break;
            case Message.Said said:
                Chat.Add(new ChatLine(said.Who, said.Text, clock));
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
        challenges.Remove(peer);
        net.Send(peer, new Message.Bye(why));
        // Cut a moment later: a connection cut at once loses what was still on its way, the reason with it.
        sentOff[peer] = clock + ByeGrace;
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
    bool devReady, devLaunched, devWaiting, devSaid;
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
        // --say: a line in the chat, once seated, and the lobby with it a moment later.
        if (Dev.Say is { } line && !devSaid && (Hosting || own is not null))
        {
            devSaid = true;
            Say(line);
            Main.Instance.Snap("discussion", 45);
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
