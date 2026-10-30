// A campaign played over the network, as its galaxy screen plays it (see GalaxyScreen.Net.cs).
using SandboxShip.Campaign;
using SandboxShip.Sim;
using SandboxShip.Sim.Net;

namespace SandboxShip.Network;

/// <summary>
/// On the host: the galaxy the session steps, and the GalaxyHost that serves the remote players;
/// the screen only shows it, and applies its own player's commands at once. On a player's
/// machine: a replica of the host's galaxy that nothing steps, brought up to date by snapshots
/// and smoothed by GalaxyMotion, and the sink that sends the player's commands to the host.
/// </summary>
public sealed class NetGalaxy
{
    public required Session Session { get; init; }
    public required Galaxy Galaxy { get; init; }
    /// <summary>The side this machine's player plays.</summary>
    public required Side Side { get; init; }
    /// <summary>On the host: what serves the other players.</summary>
    public GalaxyHost? Host { get; init; }
    /// <summary>On a player's machine: how the replica's fleets move between snapshots, and the host's clock.</summary>
    public GalaxyMotion Motion { get; } = new();
    /// <summary>The screen showing the galaxy, once it is up.</summary>
    public GalaxyScreen? Screen { get; set; }

    public bool Replica => Host is null;

    /// <summary>The galaxy's speed and pause, as the host set them for everyone.</summary>
    public GalaxyClock Clock => Host?.Clock ?? Motion.Clock;

    /// <summary>
    /// Where this machine's player's commands go: to the host from a player's machine; on the
    /// host, applied at once, what they set off kept for the other players' next snapshot.
    /// </summary>
    public ICommandSink<GalaxyCommand> Sink => Host is { } host ? new AtHost(host, Side) : new ToHost(Session);

    sealed class ToHost(Session session) : ICommandSink<GalaxyCommand>
    {
        public Result Send(GalaxyCommand command)
        {
            session.Send(new Message.Plan(command));
            return Result.OK;
        }
    }

    sealed class AtHost(GalaxyHost host, Side side) : ICommandSink<GalaxyCommand>
    {
        public Result Send(GalaxyCommand command)
        {
            var g = host.Galaxy;
            var before = g.Events.Count;
            var r = Commands.Apply(g, side, command);
            // The screen shows and clears the galaxy's events before the session collects them again.
            host.Collect(g.Events.Skip(before));
            return r;
        }
    }
}
