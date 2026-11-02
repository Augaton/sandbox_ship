// The campaign over the network on the galaxy screen (Network.NetGalaxy). The session steps the
// host's galaxy and a player's replica follows snapshots: this screen never steps it. The speed
// is the host's, for everyone, and the pause anyone's. A battle waiting for another player's
// choice, or commanded in the battle view, holds the galaxy for all: a line under the top bar
// says so, and a battle commanded by others may be watched. The dev runs' automation too
// (--attack, --raid, --truce, --answer, --choose, --snap).
using Godot;
using SandboxShip.Core;
using SandboxShip.Sim;
using SandboxShip.Sim.Data;

namespace SandboxShip.Campaign;

public partial class GalaxyScreen
{
    /// <summary>Over the network: who paused the galaxy; null while it runs.</summary>
    public string? PausedBy { get; private set; }
    /// <summary>Over the network: battles this player answered, until the host's snapshot says so.</summary>
    readonly HashSet<int> answered = [];
    /// <summary>The battle commanded in the battle view this screen last offered to watch: offered once.</summary>
    int toldFight = -1;
    /// <summary>The chat over the galaxy, at the bottom left, once the screen is built.</summary>
    Ui.ChatBox? chat;

    void NetReady()
    {
        var clock = Opts.Net!.Clock;
        (Speed, Paused) = (clock.Speed, clock.Paused);
        Main.Instance.Snap("galaxie", 90);
    }

    /// <summary>Every frame over the network: the replica's fleets glide, the host's clock shows, what holds the galaxy is said.</summary>
    void NetFrame(double delta)
    {
        var net = Opts.Net!;
        if (chat is null) AddChild(chat = new Ui.ChatBox(false));
        if (net.Replica) net.Motion.Advance(Galaxy, delta);
        var clock = net.Clock;
        if (clock.Speed != Speed || clock.Paused != Paused)
        {
            (Speed, Paused) = (clock.Speed, clock.Paused);
            if (!Paused) PausedBy = null;
            Hud.RefreshModes();
        }
        Hud.Banner(Holding());
        OfferFight();
        DevCampaign();
    }

    /// <summary>The pause for everyone, with who asked it: on the pause button, and in a toast.</summary>
    public void NetPaused(bool on, string who)
    {
        Paused = on;
        PausedBy = on ? who : null;
        Hud.RefreshModes();
        Hud.Toast(on ? $"Pause demandée par {who}" : $"{who} relance la galaxie");
    }

    /// <summary>A side as the other players know it: by its player's name, else by its race.</summary>
    string Who(Side side) => Galaxy.Humans.GetValueOrDefault(side) ?? Factions.Defs[Galaxies.RaceOf(Galaxy, side)].Name;

    string Names(IEnumerable<Side> sides) => string.Join(" et ", sides.Select(Who));

    /// <summary>Whose eyes, in a sentence: "de Bob", "des Errants".</summary>
    string Eyes(Side side) => Galaxy.Humans.TryGetValue(side, out var name) ? $"de {name}" : $"des {Factions.Defs[Galaxies.RaceOf(Galaxy, side)].Name}";

    /// <summary>What holds the galaxy for everyone, in a line: a battle commanded in the battle view, or waiting for a player's choice.</summary>
    string? Holding()
    {
        var g = Galaxy;
        if (g.Winner is not null) return null;
        if (g.Battles.Find(b => b.Status == BattleStatus.Tactical) is { } fought)
        {
            return $"{Galaxies.BattleName(g, fought.PlanetId, fought.Lane)}, commandée par {Names(fought.Commanders)} : la galaxie attend sa fin.";
        }
        foreach (var b in g.Battles.Where(b => b.Status == BattleStatus.Pending))
        {
            var waiting = Galaxies.HumansIn(g, b).Where(s => s != Player && !b.Choices.ContainsKey(s)).ToList();
            if (waiting.Count > 0) return $"{Galaxies.BattleName(g, b.PlanetId, b.Lane)} : la galaxie attend le choix de {Names(waiting)}.";
        }
        return null;
    }

    /// <summary>A battle that others command in the battle view: offered once to watch.</summary>
    void OfferFight()
    {
        var g = Galaxy;
        if (Opts.Net!.Session.Watchable is not int id || id == toldFight || Hud.DialogOpen) return;
        toldFight = id;
        var b = g.Battles.Find(x => x.Id == id)!;
        Hud.ShowFight(Galaxies.BattleName(g, b.PlanetId, b.Lane), $"{Names(b.Commanders)} la commande{(b.Commanders.Count > 1 ? "nt" : "")} : la galaxie "
            + $"attend sa fin. Tu peux la regarder, avec les yeux {Eyes(Galaxies.Viewpoint(g, b, Player))}.", WatchFight);
        Main.Instance.Snap("bataille-des-autres");
        if (Dev.Watch) devWatch = Godot.Time.GetTicksMsec();
    }

    public void WatchFight() => Opts.Net?.Session.WatchFight();

    /// <summary>A battle waiting for this player's choice: its prompt; in a dev run (--choose), answered after a capture.</summary>
    void Prompt(GalaxyBattle b)
    {
        Hud.PromptBattle(b);
        if (Dev.Choose.Length == 0) return;
        Main.Instance.Snap("invite");
        devPrompt = (b.Id, Godot.Time.GetTicksMsec());
    }

    /// <summary>The player's answer to a battle's prompt: remembered over the network until the host's snapshot says it.</summary>
    void Answered(int battleId)
    {
        if (Opts.Net is not null) answered.Add(battleId);
    }

    // Dev runs.

    (int Battle, ulong At)? devPrompt;
    int devChoices, devAttacks, devRaids, devBuilds;
    bool devTruce;
    ulong? devWatch, devPausedAt;
    (Side From, ulong At)? devOffer;

    void DevCampaign()
    {
        var g = Galaxy;
        var now = Godot.Time.GetTicksMsec();
        if (devWatch is ulong w && now - w > 2500)
        {
            devWatch = null;
            Hud.CloseDialog();
            GD.Print("[réseau] Regarde la bataille");
            WatchFight();
        }
        if (Dev.Pause > 0 && devPausedAt is null && g.Time >= Dev.Pause && !Paused)
        {
            devPausedAt = now;
            TogglePause();
            Main.Instance.Snap("pause", 40);
        }
        else if (devPausedAt is ulong at && at > 0 && now - at > 4000 && Paused)
        {
            devPausedAt = 0;
            TogglePause();
        }
        if (devPrompt is { } p && now - p.At > 2500)
        {
            devPrompt = null;
            Hud.CloseDialog();
            var choice = Dev.Choose[Math.Min(devChoices++, Dev.Choose.Length - 1)];
            GD.Print($"[réseau] Bataille {p.Battle} : {choice}");
            if (choice == "command") Fight(p.Battle);
            else AutoResolve(p.Battle);
        }
        if (devAttacks < Dev.Attack.Length && g.Time >= Dev.Attack[devAttacks])
        {
            devAttacks++;
            DevSend(g.Planets.Where(x => x.Owner is Side o && o != Player && Galaxies.Human(g, o) && !Galaxies.Allies(g, o, Player)));
        }
        if (devBuilds < Dev.Build.Length && g.Time >= Dev.Build[devBuilds])
        {
            devBuilds++;
            var queued = 0;
            foreach (var yard in g.Planets.Where(x => x.Owner == Player && x.Station > 0))
            {
                for (var i = 0; i < 4 && Sink.Send(new GalaxyCommand.EnqueueBuild(yard.Id, GalaxyBuild.Ship(ShipClass.Frigate))).Ok; i++) queued++;
            }
            GD.Print($"[réseau] {queued} frégates commandées");
        }
        if (devRaids < Dev.Raid.Length && g.Time >= Dev.Raid[devRaids])
        {
            devRaids++;
            DevSend(g.Planets.Where(x => x.Owner == Faction.Errant));
        }
        if (Dev.Truce > 0 && !devTruce && g.Time >= Dev.Truce)
        {
            devTruce = true;
            foreach (var rival in g.Humans.Keys.Where(s => s != Player && !Galaxies.Allies(g, s, Player)).Take(1))
            {
                var r = Sink.Send(new GalaxyCommand.OfferTruce(rival));
                GD.Print($"[réseau] Trêve proposée à {Who(rival)} : {(r.Ok ? "envoyée" : r.Reason)}");
            }
        }
        if (Dev.Answer is null) return;
        if (devOffer is null && g.Offers.Find(o => o.To == Player) is { } offer && Hud.DialogOpen)
        {
            devOffer = (offer.From, now);
            Main.Instance.Snap("offre-de-treve");
        }
        if (devOffer is { } d && now - d.At > 2500)
        {
            devOffer = null;
            Hud.CloseDialog();
            Sink.Send(new GalaxyCommand.AnswerTruce(d.From, Dev.Answer == "yes"));
            GD.Print($"[réseau] Trêve de {Who(d.From)} : {(Dev.Answer == "yes" ? "acceptée" : "refusée")}");
            Main.Instance.Snap("treve-repondue", 60);
        }
    }

    /// <summary>Every fleet of the player waiting at a planet leaves for the nearest of these planets, by its route.</summary>
    void DevSend(IEnumerable<Planet> targets)
    {
        var g = Galaxy;
        var list = targets.ToList();
        foreach (var f in g.Fleets.Values.Where(f => f.Side == Player && f.At is not null && f.BattleId is null).ToList())
        {
            var from = f.At!.Value;
            var best = list.Select(t => (t.Id, Path: Galaxies.FindPath(g, from, t.Id))).Where(t => t.Path is not null)
                .OrderBy(t => t.Path!.Count).Select(t => (int?)t.Id).FirstOrDefault();
            if (best is not int dest) continue;
            var r = Sink.Send(new GalaxyCommand.SendFleet(f.Id, dest));
            GD.Print($"[réseau] Flotte {f.Id} vers {g.Planets[dest].Name} : {(r.Ok ? "envoyée" : r.Reason)}");
        }
    }
}
