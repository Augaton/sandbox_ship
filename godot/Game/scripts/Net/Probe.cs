// `--probe`, a measure for development: how long a message takes from a host to its player and
// back, directly (Transport, on --port) and through the online server (--master), between two
// connections of this same process polled without pause. What the online server adds is the
// difference. Small reliable notices, then snapshot-sized unreliable states answered by a notice.
using System.Diagnostics;
using Godot;
using SandboxShip.Core;
using SandboxShip.Sim;
using SandboxShip.Sim.Net;

namespace SandboxShip.Network;

public static class Probe
{
    const int Rounds = 200;

    public static void Run()
    {
        var game = new LanGame(Protocol.Version, Ui.Menu.Version, "Sonde", LobbyMode.Skirmish, 1, 2, 0);
        var direct = Transport.Listen(Dev.Port, 2, null, out var error);
        if (direct is not null)
        {
            Measure("direct", direct, Transport.Join("127.0.0.1", Dev.Port, null, out _)!);
            direct.Close();
        }
        else GD.PrintErr(error);
        var host = RelayTransport.Host(Session.MasterAddress, game, null, out error);
        if (host is not null)
        {
            var clock = Stopwatch.StartNew();
            while (host.Room == 0 && clock.Elapsed.TotalSeconds < 10) host.Poll(_ => { }, _ => { }, (_, _) => { }, (_, _) => { });
            if (host.Room > 0) Measure("par le serveur en ligne", host, RelayTransport.Join(Session.MasterAddress, host.Room, false, null, out _)!);
            else GD.PrintErr($"Serveur en ligne {Session.MasterAddress} : {host.Lost ?? "pas de réponse"}");
            host.Close();
        }
        else GD.PrintErr(error);
        Main.Instance.QuitGame();
    }

    /// <summary>Round trips between a host and its player, once both see each other: notices, then 12 Kio states.</summary>
    static void Measure(string how, ITransport host, ITransport player)
    {
        var clock = Stopwatch.StartNew();
        int peer = -1, back = -1;
        var up = false;
        void Pump()
        {
            host.Poll(p => peer = p, _ => { }, (_, m) => back = m is Message.Notice n ? int.Parse(n.Text) : back, (_, _) => { });
            player.Poll(_ => up = true, _ => { }, (_, m) =>
            {
                if (m is Message.Notice n) player.Send(Transport.HostPeer, n);
                else if (m is Message.State s) player.Send(Transport.HostPeer, new Message.Notice(s.Snapshot[0].ToString()));
            }, (_, _) => { });
        }
        while ((peer < 0 || !up) && clock.Elapsed.TotalSeconds < 10) Pump();
        if (peer < 0 || !up)
        {
            GD.PrintErr($"[sonde] {how} : pas de connexion");
            return;
        }
        foreach (var big in new[] { false, true })
        {
            var times = new List<double>();
            for (var i = 0; i < Rounds; i++)
            {
                var id = i;
                var t0 = clock.Elapsed.TotalMilliseconds;
                back = -1;
                host.Send(peer, big ? new Message.State([(byte)id, .. new byte[12 * 1024 - 1]]) : new Message.Notice(id.ToString()));
                while (back != id && clock.Elapsed.TotalMilliseconds - t0 < 1000) Pump();
                if (back == id) times.Add(clock.Elapsed.TotalMilliseconds - t0);
            }
            times.Sort();
            var lost = Rounds - times.Count;
            GD.Print(times.Count == 0 ? $"[sonde] {how} : aucune réponse" : $"[sonde] {how}, {(big ? "instantanés de 12 Kio non fiables" : "avis fiables")} : aller-retour médian "
                + $"{times[times.Count / 2]:0.00} ms, 95 % sous {times[(int)(times.Count * 0.95)]:0.00} ms, maximum {times[^1]:0.00} ms{(lost > 0 ? $", {lost} perdus" : "")}");
        }
        player.Close();
    }
}
