// Raids (not in the web version): a neutral colossus guards the spot of the map farthest from
// every station, strikes whatever comes near it, and pays a bounty to whoever brings it down.
using SandboxShip.Sim.Data;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim;

public static class Raids
{
    /// <summary>Credits paid to the faction that lands the last blow.</summary>
    public const double Bounty = 8000;
    /// <summary>Distance kept from the map's edges.</summary>
    const double Inset = 420;

    /// <summary>
    /// Puts the colossus, on the neutral side, at the corner or edge middle farthest from every
    /// station, facing the middle of the map. Its guardians use their abilities by themselves.
    /// </summary>
    public static Entity Place(World world)
    {
        var guard = world.Factions.TryGetValue(Faction.Errant, out var f) ? f : AddFaction(world, Faction.Errant, 0);
        guard.AutoAbilities = true;
        var (w, h) = (world.Width, world.Height);
        Point[] spots =
        [
            new(Inset, Inset), new(w / 2, Inset), new(w - Inset, Inset), new(w - Inset, h / 2),
            new(w - Inset, h - Inset), new(w / 2, h - Inset), new(Inset, h - Inset), new(Inset, h / 2),
        ];
        var stations = world.Entities.Where(e => e.Type == "station").ToList();
        var at = spots.OrderByDescending(s => stations.Count == 0 ? 0 : stations.Min(st => Dist(st, s))).First();
        var facing = Math.Atan2(h / 2 - at.Y, w / 2 - at.X);
        var colossus = SpawnShip(world, Faction.Errant, ShipClass.Colossus, at.X, at.Y, facing);
        // Its escort holds the flanks: a battlecruiser and two frigates on each side.
        foreach (var (cls, ahead, side) in new[] { (ShipClass.Battlecruiser, 140.0, 300.0), (ShipClass.Frigate, 60.0, 360.0), (ShipClass.Frigate, -80.0, 330.0) })
        {
            foreach (var dir in new[] { -1, 1 })
            {
                var x = at.X + Math.Cos(facing) * ahead - Math.Sin(facing) * side * dir;
                var y = at.Y + Math.Sin(facing) * ahead + Math.Cos(facing) * side * dir;
                SpawnShip(world, Faction.Errant, cls, Math.Clamp(x, 100, w - 100), Math.Clamp(y, 100, h - 100), facing);
            }
        }
        return colossus;
    }

    /// <summary>
    /// The bounty goes to whoever hit the colossus last: credits, and a free level of one of its
    /// techs not yet at the top, drawn at random.
    /// </summary>
    public static void Reward(World world, Entity colossus)
    {
        if (colossus.LastHitBy is not Faction by || by == colossus.Faction || !world.Factions.TryGetValue(by, out var f)) return;
        f.Credits += Bounty;
        f.Bounties += Bounty;
        f.Stats.Earned += Bounty;
        var open = Techs.All.Where(t => f.Research[t] < Techs.Defs[t].MaxLevel).ToList();
        TechId? tech = null;
        if (open.Count > 0)
        {
            var t = open[(int)Math.Floor(Rand(world) * open.Count)];
            var before = t == TechId.Armor ? HullMult(world, by) : ShieldMult(world, by);
            f.Research[t]++;
            RescaleForResearch(world, by, t, before);
            tech = t;
        }
        world.Events.Add(new RaidWon(by, Bounty, tech));
    }
}
