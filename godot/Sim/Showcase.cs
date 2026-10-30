// The main menu's backdrop: random fleets of two or three factions meet in the middle of a
// small map and fight it out, for show. No stations, no fog, no AI: the ships attack-move
// toward each other, abilities on, and `Steer` sends the idle ones back into the fight.
using SandboxShip.Sim.Data;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim;

public static class Showcase
{
    /// <summary>Each side's fleet, in credits of ships.</summary>
    const double Budget = 12000;
    /// <summary>Distance from the map center to each fleet at the start.</summary>
    const double Ring = 650;

    /// <summary>How often each class is drawn: many small craft, fewer warships.</summary>
    static readonly (ShipClass Cls, double Weight)[] Mix =
    [
        (ShipClass.Fighter, 5), (ShipClass.Bomber, 3), (ShipClass.Scout, 1), (ShipClass.Torpedo, 2), (ShipClass.Support, 1),
        (ShipClass.Frigate, 4), (ShipClass.Battlecruiser, 2), (ShipClass.Bulwark, 0.8), (ShipClass.Carrier, 0.6), (ShipClass.Dreadnought, 0.8),
    ];

    static readonly Faction[] Sides = [Faction.Klaed, Faction.Nairan, Faction.Nautolan, Faction.Errant];

    public static World Create(int seed)
    {
        var world = CreateWorld(4500, 3000, seed);
        world.Fog = false;
        var (cx, cy) = (world.Width / 2, world.Height / 2);
        var pool = Sides.ToList();
        var count = Rand(world) < 0.3 ? 3 : 2;
        var start = Rand(world) * Math.Tau;
        var spots = new List<Point>();
        for (var i = 0; i < count; i++)
        {
            var f = pool[(int)(Rand(world) * pool.Count)];
            pool.Remove(f);
            AddFaction(world, f, 0).AutoAbilities = true;
            var bearing = start + i * Math.Tau / count;
            var (fx, fy) = (cx + Math.Cos(bearing) * Ring, cy + Math.Sin(bearing) * Ring);
            spots.Add(new Point(fx, fy));
            Fleet(world, f, fx, fy, bearing + Math.PI);
        }
        Scenario.ScatterFields(world, 5, spots, 350);
        return world;
    }

    /// <summary>A random fleet facing `facing`, its heavy ships in the middle; sometimes a hero or a titan leads it.</summary>
    static void Fleet(World world, Faction f, double x, double y, double facing)
    {
        var list = new List<ShipClass>();
        if (Rand(world) < 0.15) list.Add(ShipClass.Titan);
        var left = Budget * (0.8 + Rand(world) * 0.4);
        var total = Mix.Sum(m => m.Weight);
        for (var tries = 0; tries < 200 && left >= 300; tries++)
        {
            var pick = Rand(world) * total;
            var cls = Mix.First(m => (pick -= m.Weight) < 0).Cls;
            var def = Ships.Defs[cls];
            if (def.Cost > left) continue;
            left -= def.Cost;
            list.AddRange(Enumerable.Repeat(cls, def.Squad ?? 1));
        }
        list = [.. list.OrderByDescending(c => Ships.Defs[c].Radius)];
        var offsets = Movement.FormationOffsets(list.Count + 1, 80).OrderBy(p => Hypot(p.X, p.Y)).ToList();
        var ids = new List<int>();
        if (f != Faction.Errant && Rand(world) < 0.3)
        {
            var heroes = Heroes.Of(f).ToList();
            ids.Add(SpawnHero(world, f, heroes[(int)(Rand(world) * heroes.Count)].Id, x + offsets[^1].X, y + offsets[^1].Y, facing).Id);
        }
        for (var k = 0; k < list.Count; k++) ids.Add(SpawnShip(world, f, list[k], x + offsets[k].X, y + offsets[k].Y, facing).Id);
        Movement.IssueMove(world, ids, world.Width / 2, world.Height / 2, true);
    }

    /// <summary>The factions with ships left.</summary>
    public static int Standing(World world)
    {
        var n = 0;
        foreach (var f in world.Factions.Keys)
        {
            foreach (var e in world.Entities)
            {
                if (e.Kind != EntityKind.Ship || e.Faction != f) continue;
                n++;
                break;
            }
        }
        return n;
    }

    /// <summary>Sends each faction's idle ships at the nearest enemy: nobody stays out of the fight.</summary>
    public static void Steer(World world)
    {
        foreach (var f in world.Factions.Keys)
        {
            var idle = world.Entities.Where(e => e.Kind == EntityKind.Ship && e.Faction == f && e.Order is null && e.CarrierId is null).ToList();
            if (idle.Count == 0) continue;
            var (mx, my) = (idle.Average(e => e.X), idle.Average(e => e.Y));
            var foe = world.Entities.Where(e => e.Kind == EntityKind.Ship && e.Faction != f).MinBy(e => Hypot(e.X - mx, e.Y - my));
            if (foe is not null) Movement.IssueMove(world, idle.Select(e => e.Id), foe.X, foe.Y, true);
        }
    }
}
