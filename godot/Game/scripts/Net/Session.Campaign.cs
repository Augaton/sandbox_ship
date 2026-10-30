// The campaign half of a Session (not in the web version): launching it from the lobby, new or
// resumed from the host's save; the host steps the galaxy here and serves it to the players,
// at the speed it sets for everyone; the battles commanded in the battle view, one at a time,
// stepped here too and served to those who command them and those who watch; players who
// leave. A player follows a replica of the galaxy, and of the battle it commands or watches.
using Godot;
using SandboxShip.Battle;
using SandboxShip.Core;
using SandboxShip.Sim;
using SandboxShip.Sim.Net;

namespace SandboxShip.Network;

public sealed partial class Session
{
    /// <summary>Time the galaxy may take per frame, in milliseconds, as on the galaxy screen alone.</summary>
    const double StepBudgetMs = 12;

    /// <summary>Host: the campaign under way, and what serves it.</summary>
    GalaxyHost? campaign;
    /// <summary>The campaign as this machine's galaxy screen plays it: the host's galaxy, or a player's replica.</summary>
    NetGalaxy? map;
    /// <summary>Host: the saved galaxy a resumed lobby starts from.</summary>
    Galaxy? resume;
    /// <summary>Player: the campaign has started, and waits for its first snapshot to be shown.</summary>
    bool mapping;
    /// <summary>Host: the galaxy's speed for everyone, and its step clock.</summary>
    double galaxySpeed = 1, galaxyAcc;

    /// <summary>Host: the battle commanded in the battle view, what serves it, the players commanding it, and its step clock.</summary>
    GalaxyBattle? fought;
    BattleHost? fight;
    readonly Dictionary<int, Side> commanding = [];
    double fightAcc;
    /// <summary>The campaign battle this machine's battle screen shows, commanded or watched, and how to show it.</summary>
    NetBattle? front;
    BattleOptions? frontOpts;
    int frontBattle = -1;

    /// <summary>A campaign is under way: hosted here, or followed.</summary>
    public bool InCampaign => campaign is not null || map is not null;
    public NetGalaxy? Map => map;
    /// <summary>The battle in the battle view this machine's player may watch: one it does not command.</summary>
    public int? Watchable => map?.Galaxy.Battles.Find(b => b.Status == BattleStatus.Tactical && !b.Commanders.Contains(map.Side))?.Id;

    /// <summary>Hosts a campaign saved over the network, its players' seats kept for them (Lobby.Resume); the reason it cannot, or null.</summary>
    public static string? Resume(Galaxy saved, int port)
    {
        if (Lobby.Resume(saved, Random.Shared.Next(1, 1_000_000)) is not { } lobby) return "Cette sauvegarde ne se reprend pas en réseau.";
        var error = Host(lobby, port);
        if (error is null) Current!.resume = saved;
        return error;
    }

    /// <summary>Host: starts the campaign of the lobby, or the saved one it resumes, for everyone.</summary>
    void LaunchCampaign()
    {
        var room = Room!;
        var lobby = room.Lobby;
        var g = resume ?? Galaxies.CreateGalaxy(lobby.Slots(), lobby.Seed, lobby.Layout());
        resume = null;
        lobby.Seat(g, room.NameOf);
        campaign = new GalaxyHost(g, net.Send);
        foreach (var m in room.Members) if (m.Peer != Lobby.Host && lobby.SideOfPeer(m.Peer) is { } side) campaign.Add(m.Peer, side);
        (paused, galaxyAcc, galaxySpeed) = (false, 0, Dev.Speed > 0 ? Dev.Speed : 1);
        campaign.Clock = new GalaxyClock(galaxySpeed, false);
        Broadcast(new Message.Start(lobby));
        Log($"Campagne lancée : {string.Join(", ", room.Members.Select(m => m.Name))}{(lobby.Resumed ? $", reprise à {Ui.Names.Duration(g.Time)}" : $", graine {lobby.Seed}")}");
        map = new NetGalaxy { Session = this, Galaxy = g, Side = g.Player, Host = campaign };
        Main.Instance.StartCampaign(new MenuChoice { Net = map });
        campaign.SendAll();
    }

    /// <summary>Host: the galaxy's speed, for everyone; setting it sets the galaxy going again.</summary>
    public void SetGalaxySpeed(double speed)
    {
        if (campaign is null) return;
        galaxySpeed = speed;
        if (paused) SetPause(false, Room!.NameOf(Me));
        campaign.Clock = new GalaxyClock(galaxySpeed, paused);
        campaign.SendAll();
    }

    void Planned(int from, GalaxyCommand c)
    {
        if (campaign is null) return;
        campaign.Receive(from, c);
        if (Dev.Host) Log($"Ordre galactique de {Room!.NameOf(from)} : {c.GetType().Name}");
    }

    /// <summary>
    /// Host, every frame: the players' orders, the galaxy's steps at the shared speed (none while
    /// a battle waits or is commanded: the galaxy waits for it), the commanded battle, then each
    /// player's snapshot when due with the events of the frame.
    /// </summary>
    void CampaignFrame(double delta)
    {
        if (campaign is null) return;
        var g = campaign.Galaxy;
        var before = g.Events.Count;
        campaign.BeforeStep();
        if (!paused && g.Winner is null)
        {
            galaxyAcc += Math.Min(delta, 0.25) * galaxySpeed;
            var deadline = Time.GetTicksUsec() + (ulong)(StepBudgetMs * 1000);
            while (galaxyAcc >= Galaxies.Step && Time.GetTicksUsec() < deadline)
            {
                campaign.BeforeStep();
                Galaxies.StepGalaxy(g);
                galaxyAcc -= Galaxies.Step;
            }
            galaxyAcc = Math.Min(galaxyAcc, 1);
        }
        FightFrame(delta);
        campaign.Collect(g.Events.Skip(before));
        campaign.Clock = new GalaxyClock(galaxySpeed, paused);
        campaign.Tick(delta);
        CampaignStats();
    }

    /// <summary>
    /// Host: the battle commanded in the battle view. The first one chosen so starts once none
    /// is under way; it steps here, at ×1, the AI playing the side nobody commands; it ends when
    /// decided, when its attacker retreats, or when nobody commands it any more (it then plays
    /// on by itself, in the galaxy).
    /// </summary>
    void FightFrame(double delta)
    {
        var g = campaign!.Galaxy;
        if (fought is not null)
        {
            // Retreating concludes it at once (Commands.Apply).
            if (!g.Battles.Contains(fought)) EndFight(BattleResult.Retreat);
            else if (fought.Status != BattleStatus.Tactical) EndFight(null);
        }
        if (fought is null && g.Winner is null && g.Battles.Find(b => b.Status == BattleStatus.Tactical) is { } next) StartFight(next);
        if (fought is null || fight is null) return;
        var world = fight.World;
        // Who left the battle view for the galaxy is no longer served: the AI plays its side.
        foreach (var (peer, side) in commanding.Where(c => !fought.Commanders.Contains(c.Value)).ToList())
        {
            commanding.Remove(peer);
            fight.Remove(peer);
        }
        if (!paused)
        {
            fightAcc += Math.Min(delta, 0.1);
            var n = 0;
            for (; fightAcc >= Steps.Step && n < 20; n++)
            {
                fight.BeforeStep();
                Steps.StepWorld(world);
                Sim.Battle.UpdateBattleAI(world, Uncommanded(fought), Steps.Step);
                fightAcc -= Steps.Step;
            }
            if (n == 20) fightAcc = 0;
        }
        else fight.BeforeStep();
        foreach (var side in Uncommanded(fought)) if (world.Factions.TryGetValue(side, out var f)) f.AutoAbilities = true;
        fight.Collect();
        // The host's own battle screen shows the events, then clears them; without it, nobody would.
        if (front?.Screen is null) world.Events.Clear();
        fight.Tick(delta);
        if (Sim.Battle.Outcome(world, fought.Attacker, fought.Defender) is BattleResult r)
        {
            Galaxies.ConcludeBattle(g, fought.Id, r);
            EndFight(r);
        }
    }

    static IEnumerable<Side> Uncommanded(GalaxyBattle b) => new[] { b.Attacker, b.Defender }.Where(s => !b.Commanders.Contains(s));

    /// <summary>Host: a battle commanded in the battle view starts for those who command it; the others may watch it.</summary>
    void StartFight(GalaxyBattle b)
    {
        var g = campaign!.Galaxy;
        (fought, fight, fightAcc) = (b, new BattleHost(b.World, net.Send), 0);
        commanding.Clear();
        foreach (var side in b.Commanders)
        {
            if (campaign.PeerOf(side) is not int peer) continue;
            fight.Add(peer, side);
            commanding[peer] = side;
            net.Send(peer, new Message.Engage(b.Id, true));
        }
        Log($"{Galaxies.BattleName(g, b.PlanetId, b.Lane)} commandée par {string.Join(" et ", b.Commanders.Select(s => g.Humans.GetValueOrDefault(s, "?")))}");
        if (b.Commanders.Contains(g.Player)) OpenFront(b, g.Player, false);
        campaign.SendAll();
    }

    /// <summary>Host: the commanded battle is over for those it served, decided or (null) left to play on by itself.</summary>
    void EndFight(BattleResult? result)
    {
        var id = fought!.Id;
        // A last snapshot first, so that the end's tally shows the last blows.
        fight!.Tick(1);
        foreach (var peer in fight.Peers.ToList()) net.Send(peer, new Message.Over(id, result));
        Log($"Bataille {id} terminée : {result?.ToString() ?? "laissée à l'IA"}");
        (fought, fight) = (null, null);
        commanding.Clear();
        CloseFront(id, result);
        campaign!.SendAll();
    }

    /// <summary>Host: this machine's player watches the battle in the battle view, through the eyes of its side or an ally's, or the defender's.</summary>
    public void WatchFight()
    {
        if (Watchable is not int id) return;
        if (!Hosting)
        {
            Send(new Message.Watch(id, true));
            return;
        }
        if (fought?.Id == id) OpenFront(fought, Galaxies.Viewpoint(map!.Galaxy, fought, map.Side), true);
    }

    void Watched(int from, Message.Watch w)
    {
        if (fight is null || fought?.Id != w.Battle || campaign?.SideOf(from) is not Side side || commanding.ContainsKey(from)) return;
        if (!w.On)
        {
            fight.Remove(from);
            return;
        }
        fight.Add(from, Galaxies.Viewpoint(campaign.Galaxy, fought, side), watches: true);
        net.Send(from, new Message.Engage(fought.Id, false));
    }

    /// <summary>The battle as this machine's battle screen will show it: the host's world itself, or a replica it waits to fill.</summary>
    void OpenFront(GalaxyBattle b, Side side, bool watching)
    {
        var g = map!.Galaxy;
        front = new NetBattle { Session = this, World = Hosting ? b.World : new World(), Side = side, Driven = Hosting, Watching = watching };
        frontBattle = b.Id;
        var title = Galaxies.BattleName(g, b.PlanetId, b.Lane) + (watching ? " · spectateur" : "");
        frontOpts = new BattleOptions
        {
            Player = side, Mode = GameMode.Campaign, Net = front,
            Campaign = new CampaignBattle(front.World, b.Id, title, b.Attacker, b.Defender),
        };
        // A player's replica is shown once its first snapshot fills it.
        if (Hosting) ShowFront();
    }

    void ShowFront()
    {
        if (frontOpts is not { } opts) return;
        frontOpts = null;
        Main.Instance.StartBattle(opts);
    }

    /// <summary>The battle this screen showed is over: its screen says so, or goes back to the galaxy.</summary>
    void CloseFront(int battle, BattleResult? result)
    {
        if (front is not { } f || frontBattle != battle) return;
        (f.Ended, f.Outcome) = (true, result);
        (front, frontOpts) = (null, null);
    }

    /// <summary>
    /// The battle screen of a campaign battle went: back to the galaxy. A player who watched
    /// tells the host, which stops serving it; one who commanded told it already (Retreat,
    /// DelegateBattle), or the battle is over.
    /// </summary>
    public void LeftFront(NetBattle link)
    {
        if (front != link) return;
        (front, frontOpts) = (null, null);
        if (!Hosting && link.Watching && !link.Ended) Send(new Message.Watch(frontBattle, false));
    }

    /// <summary>A player: what the host says of the campaign; false for what is not the campaign's.</summary>
    bool FromHostInCampaign(Message m)
    {
        switch (m)
        {
            case Message.Start s when s.Lobby.Mode == LobbyMode.Campaign:
                Room!.Lobby = s.Lobby;
                paused = false;
                if (s.Lobby.SideOfPeer(Me) is not { } side)
                {
                    Close("La campagne a commencé sans toi.");
                    return true;
                }
                map = new NetGalaxy { Session = this, Galaxy = new Galaxy(), Side = side };
                mapping = true;
                return true;
            case Message.Map snapshot when map is not null:
                if (!Read(() => map.Motion.Read(Reader(snapshot.Snapshot), map.Galaxy, clock))) return true;
                // The galaxy is shown once there is something to show: its planets, the player's capital.
                if (mapping)
                {
                    mapping = false;
                    Main.Instance.StartCampaign(new MenuChoice { Net = map });
                }
                if (paused != map.Clock.Paused) paused = map.Clock.Paused;
                return true;
            case Message.Tidings t when map is not null:
                Read(() => GalaxySnapshot.ReadEvents(Reader(t.Events), map.Galaxy));
                return true;
            case Message.Engage e when map?.Galaxy.Battles.Find(b => b.Id == e.Battle) is { } b:
                OpenFront(b, e.Commands ? map.Side : Galaxies.Viewpoint(map.Galaxy, b, map.Side), !e.Commands);
                return true;
            case Message.State state when front is not null:
                if (!Read(() => front.Motion.Read(Reader(state.Snapshot), front.World, clock))) return true;
                ShowFront();
                return true;
            case Message.News n when front is not null:
                Read(() => Snapshot.ReadEvents(Reader(n.Events), front.World));
                return true;
            case Message.Over o:
                CloseFront(o.Battle, o.Result);
                return true;
            case Message.Refused r when map is not null:
                Toast(r.Reason);
                return true;
            case Message.Notice n when map is not null:
                Toast(n.Text);
                return true;
            case Message.Pause p when map is not null:
                paused = p.On;
                CampaignPaused(p.On, p.Who);
                return true;
        }
        return false;
    }

    static BinaryReader Reader(byte[] bytes) => new(new MemoryStream(bytes));

    /// <summary>Reads what the host sent; a snapshot that does not read ends the game for this player.</summary>
    bool Read(Action read)
    {
        try
        {
            read();
            return true;
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException)
        {
            Close($"Instantané illisible : {e.Message}");
            return false;
        }
    }

    /// <summary>A line on whichever campaign screen is shown: the battle's, else the galaxy's.</summary>
    void Toast(string text)
    {
        if (front?.Screen is { } screen && screen.IsInsideTree()) screen.Hud.Toast(text);
        else map?.Screen?.Hud.Toast(text);
    }

    /// <summary>The pause for everyone, on the campaign's screens.</summary>
    void CampaignPaused(bool on, string who)
    {
        front?.Screen?.NetPaused(on, who);
        map?.Screen?.NetPaused(on, who);
    }

    /// <summary>Host: a player left the campaign; the AI takes its side, in the galaxy and in the battle it commanded.</summary>
    void LeftCampaign(int peer, string who)
    {
        if (campaign?.SideOf(peer) is null) return;
        campaign.Drop(peer);
        commanding.Remove(peer);
        fight?.Remove(peer);
        var text = $"{who} a quitté la partie : l'IA prend sa place.";
        Broadcast(new Message.Notice(text));
        Toast(text);
        campaign.SendAll();
    }

    double campaignStatsAt;

    /// <summary>Every ten seconds on a dev host: what the campaign costs the host and the network.</summary>
    void CampaignStats()
    {
        if (!Dev.Host || clock - campaignStatsAt < 10 || campaign is null) return;
        var span = clock - campaignStatsAt;
        campaignStatsAt = clock;
        var players = Math.Max(1, campaign.Peers.Count());
        var g = campaign.Galaxy;
        Log($"{Ui.Names.Duration(g.Time)} de galaxie, ×{galaxySpeed:0}{(paused ? ", en pause" : "")} : {campaign.Snapshots} instantanés de galaxie, "
            + $"{campaign.Peers.Sum(campaign.BytesTo) / Math.Max(1, campaign.Snapshots)} octets en moyenne, {campaign.WriteMs / Math.Max(1, campaign.Snapshots):0.000} ms d'écriture ; "
            + $"{net.PopWireBytes() / span / players / 1024:0.0} Kio/s compressés par joueur{(fight is not null ? $", bataille {fought!.Id} : {fight.World.Entities.Count} entités" : "")}");
    }
}
