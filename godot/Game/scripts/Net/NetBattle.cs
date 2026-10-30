// A battle played over the network, as its screen plays it (see BattleScreen.Net.cs).
using SandboxShip.Battle;
using SandboxShip.Sim;
using SandboxShip.Sim.Net;

namespace SandboxShip.Network;

/// <summary>
/// On the host: the world the screen steps as usual, and the BattleHost that serves the remote
/// players around each step. On a player's machine: a replica of the host's world that the
/// screen never steps, brought up to date by snapshots and smoothed by ReplicaMotion, and the
/// sink that sends the player's commands to the host.
/// </summary>
public sealed class NetBattle
{
    public required Session Session { get; init; }
    public required World World { get; init; }
    /// <summary>The side this machine's player plays.</summary>
    public required Side Side { get; init; }
    /// <summary>On the host: what serves the other players.</summary>
    public BattleHost? Host { get; init; }
    /// <summary>On a player's machine: how the replica moves between snapshots.</summary>
    public ReplicaMotion Motion { get; } = new();
    /// <summary>The screen showing the battle, once it is up.</summary>
    public BattleScreen? Screen { get; set; }

    /// <summary>The world is the host's copy, read from snapshots: no step runs here.</summary>
    public bool Replica => Host is null;

    /// <summary>Where a player's commands go: to the host. Null on the host, whose own are applied at once.</summary>
    public ICommandSink<Command>? Sink => Replica ? new ToHost(Session) : null;

    sealed class ToHost(Session session) : ICommandSink<Command>
    {
        public Result Send(Command command)
        {
            session.Send(new Message.Order(command));
            return Result.OK;
        }
    }
}
