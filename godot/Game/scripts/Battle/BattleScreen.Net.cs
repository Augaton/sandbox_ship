// A battle over the network on its screen (Network.NetBattle). The host's screen steps the world
// as usual and serves the other players around each step; a player's screen shows a replica it
// never steps, glided between snapshots, and sends its commands to the host. The speed stays at
// ×1; the pause is everyone's, with who called it.
using Godot;
using SandboxShip.Core;
using SandboxShip.Sim;
using SandboxShip.Sim.Data;

namespace SandboxShip.Battle;

public partial class BattleScreen
{
    /// <summary>Over the network: who paused the game; null while it runs.</summary>
    public string? PausedBy { get; private set; }
    bool devOrdered, devPaused;

    void NetReady()
    {
        var net = Opts.Net!;
        net.Screen = this;
        Paused = net.Session.Paused;
        // --order: ships to send, built first.
        if (Dev.Order > 0)
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
        else net.Motion.Advance(World, delta);
        DevOrder();
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
        if (Dev.Pause > 0 && !devPaused && World.Time >= Dev.Pause)
        {
            devPaused = true;
            TogglePause();
        }
        if (Dev.Order <= 0 || devOrdered || World.Time < Dev.Order) return;
        devOrdered = true;
        var ships = World.Entities.Where(e => e.Side == Opts.Player && e.Kind == EntityKind.Ship).Select(e => e.Id).ToArray();
        Sink.Send(new Command.Move(ships, World.Width / 2, World.Height / 2, true));
        GD.Print($"[réseau] Ordre envoyé à {World.Time:0} s : {ships.Length} vaisseaux en attaque-mouvement vers ({World.Width / 2:0}, {World.Height / 2:0})");
    }
}
