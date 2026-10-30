// A battle over the network on its screen (Network.NetBattle). The host's screen steps the world
// as usual and serves the other players around each step; a player's screen shows a replica it
// never steps, glided between snapshots, and sends its commands to the host. The speed stays at
// ×1; the pause is everyone's, with who called it. A campaign battle is stepped by the host's
// session, and decided there: each screen hears of its end (CampaignOver), and one who only
// watches gives no order.
using Godot;
using SandboxShip.Core;
using SandboxShip.Sim;
using SandboxShip.Sim.Data;

namespace SandboxShip.Battle;

public partial class BattleScreen
{
    /// <summary>Over the network: who paused the game; null while it runs.</summary>
    public string? PausedBy { get; private set; }
    bool devOrdered, devPaused, devLeft;

    void NetReady()
    {
        var net = Opts.Net!;
        net.Screen = this;
        Paused = net.Session.Paused;
        if (Opts.Campaign is not null) Main.Instance.Snap(net.Watching ? "bataille-regardee" : "bataille-commandee", 120);
        // --order: ships to send, built first (a campaign battle has no shipyard for its attacker).
        if (Dev.Order > 0 && Opts.Campaign is null)
        {
            Sink.Send(new Command.EnqueueShip(ShipClass.Scout));
            Sink.Send(new Command.EnqueueShip(ShipClass.Fighter, 2));
        }
    }

    /// <summary>After the frame's steps, before its events are shown and cleared.</summary>
    void NetFrame(double delta)
    {
        var net = Opts.Net!;
        if (net.Host is { } host)
        {
            // Paused, no step runs, but the players' commands still apply, as the host's own do.
            if (Paused) host.BeforeStep();
            host.Collect();
            host.Tick(delta);
        }
        else if (net.Replica) net.Motion.Advance(World, delta);
        if (net.Ended && !campaignOver) CampaignOver(net.Outcome);
        DevOrder();
        // A dev run (--choose) goes back to the galaxy on its own, after a capture of the end.
        if (overAt is ulong at && Dev.Choose.Length > 0 && Godot.Time.GetTicksMsec() - at > 3500)
        {
            overAt = null;
            ReturnToGalaxy();
        }
    }

    bool campaignOver;
    ulong? overAt;

    /// <summary>
    /// Over the network, the host's word on the campaign battle: decided, the end dialog, then back
    /// to the galaxy; left to play on by itself (nobody commands it any more), back to the galaxy now.
    /// </summary>
    void CampaignOver(BattleResult? r)
    {
        campaignOver = true;
        var c = Opts.Campaign!;
        if (r is not BattleResult res)
        {
            Main.Instance.ShowGalaxy();
            return;
        }
        result = res;
        var winner = res == BattleResult.Attacker ? c.Attacker : c.Defender;
        var watched = Opts.Net!.Watching ? $"Les {Factions.Defs[Worlds.RaceOf(World, winner)].Name} l'emportent." : null;
        Hud.ShowBattleEnd(winner == Opts.Player, ReturnToGalaxy, watched);
        Main.Instance.Snap("fin-de-bataille", 40);
        overAt = Godot.Time.GetTicksMsec();
    }

    /// <summary>One who watched a campaign battle goes back to the galaxy; the battle goes on without it.</summary>
    public void StopWatching()
    {
        if (Opts.Net is { Watching: true } && result is null) Main.Instance.ShowGalaxy();
    }

    /// <summary>The host's word on the pause: on the pause button, and in a toast.</summary>
    public void NetPaused(bool on, string who)
    {
        Paused = on;
        PausedBy = on ? who : null;
        Hud.RefreshModes();
        Hud.Toast(on ? $"Pause demandée par {who}" : $"{who} relance la partie");
    }

    /// <summary>
    /// --order=SECONDS: once the battle is that old, this player's ships attack-move to the middle
    /// of the map. --pause=SECONDS: then, it pauses the game for everyone.
    /// </summary>
    void DevOrder()
    {
        // A campaign battle's captures follow the player's ships, wherever the order took them.
        if (devOrdered && Opts.Campaign is not null && result is null && World.Entities.Any(e => e.Side == Opts.Player && e.Kind == EntityKind.Ship))
        {
            var own = FleetCenter(Opts.Player);
            CenterOn(own.X, own.Y);
        }
        // --leave: a commander leaves its campaign battle to the AI.
        if (Dev.Leave > 0 && !devLeft && Opts.Campaign is not null && Opts.Net is { Watching: false } && World.Time >= Dev.Leave && result is null)
        {
            devLeft = true;
            GD.Print($"[réseau] Bataille laissée à l'IA à {World.Time:0} s");
            Delegate();
            return;
        }
        // In a campaign, --pause is the galaxy's (GalaxyScreen.Net.cs).
        if (Dev.Pause > 0 && !devPaused && World.Time >= Dev.Pause && Opts.Campaign is null)
        {
            devPaused = true;
            TogglePause();
        }
        if (Dev.Order <= 0 || devOrdered || World.Time < Dev.Order || Opts.Net is { Watching: true }) return;
        devOrdered = true;
        var ships = World.Entities.Where(e => e.Side == Opts.Player && e.Kind == EntityKind.Ship).Select(e => e.Id).ToArray();
        // In a campaign battle, the attacker goes for the defender's station, on the left of the map (Battle.CreatePlanetBattle).
        var (x, y) = Opts.Campaign?.Attacker == Opts.Player ? (800.0, World.Height / 2) : (World.Width / 2, World.Height / 2);
        Sink.Send(new Command.Move(ships, x, y, true));
        GD.Print($"[réseau] Ordre envoyé à {World.Time:0} s : {ships.Length} vaisseaux en attaque-mouvement vers ({x:0}, {y:0})");
        if (Opts.Campaign is not null) Main.Instance.Snap("combat", 900);
        else Main.Instance.Snap("ordre", 600);
    }
}
