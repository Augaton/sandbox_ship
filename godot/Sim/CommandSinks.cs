// Where a screen sends what its player does (not in the web version): one sink per screen, for
// battle commands or galaxy commands. Single player applies them at once; a host keeps those of
// remote players for the start of the next step; a client will send them to its host.
namespace SandboxShip.Sim;

/// <summary>
/// Takes a screen's commands. The result is the answer known when sent: a sink that only applies
/// them later answers Ok, and the world shows what came of them.
/// </summary>
public interface ICommandSink<in T>
{
    Result Send(T command);
}

/// <summary>Applies each command at once, where the screen used to call the simulation itself.</summary>
public sealed class LocalSink<T>(Func<T, Result> apply) : ICommandSink<T>
{
    public Result Send(T command) => apply(command);
}

/// <summary>
/// Keeps commands until the next step starts, then applies them in the order they came: what a
/// host does with a remote player's commands. Flush is called right before each step.
/// </summary>
public sealed class DelayedSink<T>(Func<T, Result> apply) : ICommandSink<T>
{
    readonly List<T> queue = [];

    /// <summary>Commands waiting for the next step.</summary>
    public int Pending => queue.Count;

    public Result Send(T command)
    {
        queue.Add(command);
        return Result.OK;
    }

    /// <summary>Applies the commands sent since the last flush, in order; their results, in the same order.</summary>
    public List<Result> Flush()
    {
        var batch = queue.ToArray();
        queue.Clear();
        var results = new List<Result>(batch.Length);
        foreach (var c in batch) results.Add(apply(c));
        return results;
    }
}
