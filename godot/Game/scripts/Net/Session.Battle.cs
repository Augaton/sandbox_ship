// The battle half of a Session: launching it from the lobby, serving it (the host) or following
// it (a player), the pause for everyone, players who leave, and back to the lobby at the end. A
// host who plays steps its world in its battle screen (BattleScreen.Net.cs); a dedicated server
// steps it here.
using SandboxShip.Core;
using SandboxShip.Sim;
using SandboxShip.Sim.Net;

namespace SandboxShip.Network;

public sealed partial class Session
{
    /// <summary>Host: the battle under way, and what serves it.</summary>
    BattleHost? battle;
    /// <summary>The battle as this machine's screen plays it: the host's own world, or a player's replica.</summary>
    NetBattle? link;
    /// <summary>Player: the battle has started, and waits for its first snapshot to be shown.</summary>
    Lobby? starting;
    bool paused;
    /// <summary>A dedicated server's step clock, and when its battle was won (-1: not yet).</summary>
    double acc, over = -1;
    /// <summary>Host: commands received from each player in this battle, by name (it outlives a player who left).</summary>
    readonly Dictionary<string, int> orders = [];
    double statsAt, statsMs;
    long statsBytes;
    int statsSnapshots;

    /// <summary>A battle is under way, or about to be shown.</summary>
    public bool InBattle => battle is not null || link is not null;
    public bool Paused => paused;

    /// <summary>Starts the lobby's battle: the host for everyone; a dedicated server's leader asks it to.</summary>
    public void Launch()
    {
        if (!Hosting)
        {
            if (Leads) Send(new Message.Launch());
            return;
        }
        var room = Room!;
        var lobby = room.Lobby;
        if (battle is not null || campaign is not null || lobby.Problems().Count > 0) return;
        if (lobby.Mode == LobbyMode.Campaign)
        {
            LaunchCampaign();
            return;
        }
        // --start=stress: the measuring battle (Sim/Stress.cs), its first seats playing Kla'ed and Nairan.
        var world = Dev.Start == "stress" ? Stress.Create(lobby.Seed) : Scenario.CreateSkirmish(lobby.Skirmish());
        battle = new BattleHost(world, net.Send);
        away.Clear();
        foreach (var m in room.Members)
        {
            if (m.Peer == Lobby.Host) continue;
            if (m.Watches) Spectator(m.Peer);
            else if (lobby.SideOfPeer(m.Peer) is { } side) battle.Add(m.Peer, side);
        }
        (paused, acc, over) = (false, 0, -1);
        orders.Clear();
        (statsAt, statsMs, statsBytes, statsSnapshots) = (clock, 0, net.Sent.Values.Sum(), 0);
        net.PopWireBytes();
        Broadcast(new Message.Start(lobby));
        Log($"Bataille lancée : {string.Join(", ", room.Members.Select(m => m.Name))}, graine {lobby.Seed}");
        if (As != Role.Host) return;
        link = new NetBattle { Session = this, World = world, Side = lobby.SideOfPeer(Lobby.Host)!.Value, Host = battle };
        Main.Instance.Start(lobby, link);
    }

    /// <summary>Everyone back to the lobby, nobody ready: the host for everyone; a dedicated server's leader asks it to.</summary>
    public void BackToLobby()
    {
        if (!Hosting)
        {
            if (Leads) Send(new Message.Back());
            return;
        }
        if (battle is null) return;
        (battle, link) = (null, null);
        away.Clear();
        foreach (var s in Room!.Lobby.Seats) s.Ready = false;
        Broadcast(new Message.Back());
        Broadcast(new Message.Room(Room.Lobby, Room.Members));
        Log("Retour au salon");
        Changes++;
        if (As == Role.Host) Main.Instance.ShowMenu();
    }

    /// <summary>Anyone may stop the game, or set it going again, for everyone.</summary>
    public void AskPause(bool on)
    {
        if (Hosting) SetPause(on, Room!.NameOf(Me));
        else Send(new Message.Pause(on, ""));
    }

    void SetPause(bool on, string who)
    {
        if (battle is null && campaign is null || paused == on) return;
        paused = on;
        Broadcast(new Message.Pause(on, who));
        link?.Screen?.NetPaused(on, who);
        CampaignPaused(on, who);
        if (campaign is not null)
        {
            campaign.Clock = new GalaxyClock(galaxySpeed, paused);
            campaign.SendAll();
        }
        Log(on ? $"Pause : {who}" : $"Reprise : {who}");
    }

    void Ordered(int from, Command c)
    {
        // A battle, or the campaign's battle in the battle view.
        if ((battle ?? fight) is not { } host) return;
        host.Receive(from, c);
        var who = Room!.NameOf(from);
        orders[who] = orders.GetValueOrDefault(who) + 1;
        if (Dev.Host && c is not Command.PilotInput) Log($"Ordre de {who} : {Describe(c)}");
    }

    static string Describe(Command c) => c switch
    {
        Command.Move m => $"{(m.AttackMove ? "attaque-mouvement" : "mouvement")} de {m.Ships.Length} vaisseaux vers ({m.X:0}, {m.Y:0})",
        _ => c.GetType().Name,
    };

    /// <summary>
    /// Host: one left during the battle. A player's side goes to the AI until it comes back
    /// under its name, and everyone is told; a spectator just goes.
    /// </summary>
    void PlayerLeft(int peer, string who, bool watched)
    {
        if (!watched) LeftCampaign(peer, who);
        if (battle?.SideOf(peer) is null) return;
        battle.Drop(peer);
        if (watched) return;
        var text = $"{who} a quitté la partie : l'IA tient sa place jusqu'à son retour.";
        Broadcast(new Message.Notice(text));
        link?.Screen?.Hud.Toast(text);
        // On a dedicated server with no player left, the battle has nobody to play for.
        if (As == Role.Server && !battle.Players.Any()) BackToLobby();
    }

    /// <summary>Host: a spectator watches the battle, at first everything, without fog.</summary>
    void Spectator(int peer)
    {
        if (battle is null) return;
        battle.Add(peer, Lobby.SideOf(0), watches: true);
        battle.View(peer, Lobby.SideOf(0), true);
    }

    /// <summary>Host: a spectator picks whose eyes it watches through: a side of the battle, or (-1) all of them.</summary>
    void Spectated(int from, int side)
    {
        if (battle is null || !Room!.Watches(from)) return;
        if (side < 0) battle.View(from, Lobby.SideOf(0), true);
        else if (battle.World.Factions.ContainsKey(new Side(side))) battle.View(from, new Side(side), false);
    }

    /// <summary>A spectator: watch the battle through the eyes of a side, or (-1) of all of them, without fog.</summary>
    public void Spectate(int side)
    {
        if (!watching || link is null) return;
        Viewing = side;
        Send(new Message.Spectate(side));
    }

    /// <summary>A spectator: whose eyes it watches through, -1 for everyone's.</summary>
    public int Viewing { get; private set; } = -1;

    void FromHostInBattle(Message m)
    {
        switch (m)
        {
            case Message.Start s:
                Room!.Lobby = s.Lobby;
                paused = false;
                // A spectator watches through the first side's eyes, as the host has it, until it picks another.
                var seen = watching ? Lobby.SideOf(0) : s.Lobby.SideOfPeer(Me);
                if (seen is not { } side)
                {
                    Close("La bataille a commencé sans toi.");
                    return;
                }
                starting = s.Lobby;
                Viewing = -1;
                link = new NetBattle { Session = this, World = new World(), Side = side, Watching = watching };
                break;
            case Message.State state when link is not null:
                Snapshot.PartRead part;
                try
                {
                    // Written against a snapshot this player no longer has: the next one will do.
                    part = link.Motion.ReadPart(new BinaryReader(new MemoryStream(state.Snapshot)), link.World, clock);
                    if (!part.Read) return;
                }
                catch (Exception e) when (e is InvalidDataException or EndOfStreamException)
                {
                    Close($"Instantané illisible : {e.Message}");
                    return;
                }
                // Acknowledged once every part of it came: the next ones are written against it.
                if (part.Completed == 0) return;
                Send(new Message.Ack(part.Completed));
                // The battle is shown once there is something to show: its map, the player's base, the camera on it.
                if (starting is { } lobby)
                {
                    starting = null;
                    Main.Instance.Start(lobby, link);
                }
                break;
            case Message.News n when link is not null:
                try
                {
                    Snapshot.ReadEvents(new BinaryReader(new MemoryStream(n.Events)), link.World);
                }
                catch (Exception e) when (e is InvalidDataException or EndOfStreamException)
                {
                    Close($"Événements illisibles : {e.Message}");
                }
                break;
            case Message.Refused r: link?.Screen?.Hud.Toast(r.Reason); break;
            case Message.Pause p:
                paused = p.On;
                link?.Screen?.NetPaused(p.On, p.Who);
                break;
            case Message.Notice n: link?.Screen?.Hud.Toast(n.Text); break;
            case Message.Back:
                (link, starting) = (null, null);
                Main.Instance.ShowMenu();
                break;
        }
    }

    /// <summary>
    /// A dedicated server steps its battle here, as the battle screen of a host who plays does:
    /// commands before each step, the events kept for the players, snapshots when due.
    /// </summary>
    void BattleFrame(double delta)
    {
        if (battle is null) return;
        if (As == Role.Server)
        {
            var world = battle.World;
            if (!paused)
            {
                acc += Math.Min(delta, 0.1);
                var n = 0;
                for (; acc >= Steps.Step && n < 20; n++)
                {
                    battle.BeforeStep();
                    Steps.StepWorld(world);
                    acc -= Steps.Step;
                }
                if (n == 20) acc = 0;
            }
            else battle.BeforeStep();
            battle.Collect();
            world.Events.Clear();
            battle.Tick(delta);
            if (world.Winner is not null && over < 0)
            {
                over = clock;
                Log("Bataille finie");
            }
            // Nobody brought everyone back: the server does, a minute after the end.
            if (over >= 0 && clock - over > 60) BackToLobby();
        }
        if (battle is not null) Stats(battle);
    }

    /// <summary>Every ten seconds, on a dedicated server (or a dev host): what the battle costs the host and the network.</summary>
    void Stats(BattleHost b)
    {
        if (As == Role.Host && !Dev.Host || clock - statsAt < 10) return;
        var span = clock - statsAt;
        var players = Math.Max(1, b.Peers.Count());
        var count = Math.Max(1, b.Snapshots - statsSnapshots);
        var sent = net.Sent.Values.Sum();
        var raw = (sent - statsBytes) / span / players / 1024;
        var wire = net.PopWireBytes() / span / players / 1024;
        var ms = (b.WriteMs - statsMs) / count;
        Log($"{Ui.Names.Duration(b.World.Time)} de bataille, {b.World.Entities.Count} entités : {(b.Snapshots - statsSnapshots) / span / players:0.0} instantanés/s "
            + $"par joueur, {ms:0.00} ms d'écriture chacun ; {raw:0.0} Kio/s par joueur ({wire:0.0} compressés) ; ordres reçus : "
            + string.Join(", ", orders.Select(o => $"{o.Key} {o.Value}")));
        (statsAt, statsMs, statsBytes, statsSnapshots) = (clock, b.WriteMs, sent, b.Snapshots);
    }
}
