// A scene for measuring, not for playing: the three factions' large fleets meet in the middle
// of a large map, with capital ships, carriers, heroes and level 3 platforms; fog and AI on
// for everyone. Shared by the game's --start=stress and by timing runs of the simulation alone.
using SandboxShip.Sim.Data;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim;

public static class Stress
{
    /// <summary>Ships per faction, by class: about 1500 entities with their parts and launched craft.</summary>
    static readonly (ShipClass Cls, int Count)[] Fleet =
    [
        (ShipClass.Leviathan, 1), (ShipClass.Titan, 1), (ShipClass.Carrier, 4), (ShipClass.Dreadnought, 5),
        (ShipClass.Battlecruiser, 10), (ShipClass.Bulwark, 4), (ShipClass.Frigate, 40), (ShipClass.Torpedo, 16),
        (ShipClass.Support, 12), (ShipClass.Bomber, 30), (ShipClass.Fighter, 60), (ShipClass.Scout, 8),
    ];

    /// <summary>Distance from the map center to each fleet's center: close enough to fight at once.</summary>
    const double FleetRing = 750;

    public static World Create(int seed = 7)
    {
        var w = Scenario.CreateSkirmish(new SkirmishOptions
        {
            Player = Faction.Klaed, Enemies = [Faction.Nairan, Faction.Nautolan], EnemyAI = true, Seed = seed, Size = MapSize.Large,
        });
        var (cx, cy) = (w.Width / 2, w.Height / 2);
        var i = 0;
        foreach (var f in w.Factions.Values)
        {
            f.AI ??= NewAI();
            f.AutoAbilities = true;
            var bearing = -Math.PI / 2 + i++ * Math.Tau / 3;
            var (fx, fy) = (cx + Math.Cos(bearing) * FleetRing, cy + Math.Sin(bearing) * FleetRing);
            // Heavy ships in the middle, facing the center.
            var list = Fleet.SelectMany(k => Enumerable.Repeat(k.Cls, k.Count)).OrderByDescending(c => Ships.Defs[c].Radius).ToList();
            var offsets = Movement.FormationOffsets(list.Count + 1, 90).OrderBy(p => Hypot(p.X, p.Y)).ToList();
            var facing = bearing + Math.PI;
            SpawnHero(w, f.Id, Heroes.Of(f.Id).First().Id, fx + offsets[0].X, fy + offsets[0].Y, facing);
            for (var k = 0; k < list.Count; k++) SpawnShip(w, f.Id, list[k], fx + offsets[k + 1].X, fy + offsets[k + 1].Y, facing);
            // Three platforms behind the fleet, at their last level.
            for (var p = -1; p <= 1; p++)
            {
                var a = bearing + p * 0.35;
                var platform = SpawnStructure(w, f.Id, BuildableType.Platform, cx + Math.Cos(a) * (FleetRing + 650), cy + Math.Sin(a) * (FleetRing + 650));
                Structures.ApplyTier(w, platform);
                Structures.ApplyTier(w, platform);
            }
        }
        return w;
    }
}
