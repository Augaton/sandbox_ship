// A battle played over the network, as its screen plays it (see BattleScreen.Net.cs).
using SandboxShip.Battle;
using SandboxShip.Sim;
using SandboxShip.Sim.Net;

namespace SandboxShip.Network;

/// <summary>
/// On the host: the world the screen steps as usual, and the BattleHost that serves the remote
/// players around each step; for a campaign battle, the world the session steps (Driven), that
/// the screen only shows. On a player's machine: a replica of the host's world that the screen
/// never steps, brought up to date by snapshots and smoothed by ReplicaMotion, and the sink that
/// sends the player's commands to the host.
/// </summary>
public sealed class NetBattle
{
    public required Session Session { get; init; }
    public required World World { get; init; }
    /// <summary>The side this machine's player plays, or watches through the eyes of.</summary>
    public required Side Side { get; init; }
    /// <summary>On the host: what serves the other players, when the screen steps the world.</summary>
    public BattleHost? Host { get; init; }
    /// <summary>On the host, a campaign battle: the session steps the world and serves the players, the screen shows it.</summary>
    public bool Driven { get; init; }
    /// <summary>The player only watches a campaign battle: it gives no order.</summary>
    public bool Watching { get; init; }
    /// <summary>On a player's machine: how the replica moves between snapshots.</summary>
    public ReplicaMotion Motion { get; } = new();
    /// <summary>The screen showing the battle, once it is up, until it goes.</summary>
    public BattleScreen? Screen { get; set; }
    /// <summary>A campaign battle is over for this screen: decided (Outcome), or left to play on by itself (no Result).</summary>
    public bool Ended { get; set; }
    public BattleResult? Outcome { get; set; }

    /// <summary>The world is the host's copy, read from snapshots: no step runs here.</summary>
    public bool Replica => Host is null && !Driven;

    /// <summary>Where a player's commands go: to the host; nowhere for one who watches. Null on the host, whose own are applied at once.</summary>
    public ICommandSink<Command>? Sink => Watching ? new Nowhere() : Replica ? new ToHost(Session) : null;

    sealed class ToHost(Session session) : ICommandSink<Command>
    {
        public Result Send(Command command)
        {
            session.Send(new Message.Order(command));
            return Result.OK;
        }
    }

    sealed class Nowhere : ICommandSink<Command>
    {
        public Result Send(Command command) => Result.Fail("Tu regardes la bataille");
    }
}
