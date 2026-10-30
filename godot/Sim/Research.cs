// Research queue, shared by battles and the galaxy: levels are queued and paid for up front,
// then researched one after the other. Port of src/sim/research.ts.
using SandboxShip.Sim.Data;

namespace SandboxShip.Sim;

/// <summary>Whoever researches: a battle faction or a galaxy faction.</summary>
public interface IResearcher
{
    double Credits { get; set; }
    ResearchLevels Research { get; }
    /// <summary>The level being researched now.</summary>
    ResearchJob? Researching { get; set; }
    /// <summary>Levels waiting their turn, already paid for.</summary>
    List<ResearchJob> ResearchQueue { get; set; }
}

public static class ResearchQueue
{
    /// <summary>Every planned job, the running one first.</summary>
    public static List<ResearchJob> Jobs(IResearcher r) =>
        r.Researching is { } job ? [job, .. r.ResearchQueue] : [.. r.ResearchQueue];

    /// <summary>Level a tech reaches once what is planned for it is done.</summary>
    public static int PlannedLevel(IResearcher r, TechId tech) => r.Research[tech] + Jobs(r).Count(j => j.Tech == tech);

    /// <summary>Queues the next level of a tech; the caller checks that it is allowed.</summary>
    public static void Queue(IResearcher r, TechId tech)
    {
        var level = PlannedLevel(r, tech);
        var def = Techs.Defs[tech];
        r.Credits -= def.Costs[level];
        var job = new ResearchJob(tech, def.Times[level], def.Costs[level]);
        if (r.Researching is not null) r.ResearchQueue.Add(job);
        else r.Researching = job;
    }

    /// <summary>
    /// Cancels the job at `index` (0 is the running one) and refunds it, with the later levels of
    /// the same tech: they were priced for a level that will now come earlier.
    /// </summary>
    public static void CancelAt(IResearcher r, int index)
    {
        var jobs = Jobs(r);
        if (index < 0 || index >= jobs.Count) return;
        var job = jobs[index];
        var kept = jobs.Where((j, i) => i < index || j.Tech != job.Tech).ToList();
        foreach (var j in jobs) if (!kept.Contains(j)) r.Credits += j.Cost;
        r.Researching = kept.FirstOrDefault();
        r.ResearchQueue = kept.Skip(1).ToList();
    }

    /// <summary>Advances the running job; returns the tech that just gained a level, if any.</summary>
    public static TechId? Advance(IResearcher r, double dt)
    {
        var job = r.Researching;
        if (job is null || (job.Remaining -= dt) > 0) return null;
        r.Research[job.Tech] = Math.Min(Techs.Defs[job.Tech].MaxLevel, r.Research[job.Tech] + 1);
        r.Researching = r.ResearchQueue.Count > 0 ? r.ResearchQueue[0] : null;
        if (r.ResearchQueue.Count > 0) r.ResearchQueue.RemoveAt(0);
        return job.Tech;
    }
}
