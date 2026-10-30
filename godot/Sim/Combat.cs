// Targeting, weapons, projectiles, shields and repairs. Port of src/sim/combat.ts.
using SandboxShip.Sim.Data;
using static SandboxShip.Sim.AbilitySystem;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim;

public static class Combat
{
    const double RetargetInterval = 0.5;

    static bool Operational(Entity e) => e.Build >= 1;

    /// <summary>Edge-to-center distance: how far `e` must reach to hit `t`.</summary>
    static double Reach(Entity e, Entity t) => Dist(e, t) - t.Radius;

    static Entity? NearestHostile(World world, Entity e, double range)
    {
        Entity? best = null;
        var bestD = range;
        foreach (var t in world.Grid.Near(e.X, e.Y, range))
        {
            if (t.Faction == e.Faction) continue;
            // Sight is checked last: it costs more than the distance, and only a nearer target needs it.
            var d = Reach(e, t);
            if (d < bestD && Vision.IsVisible(world, e.Faction, t))
            {
                bestD = d;
                best = t;
            }
        }
        return best;
    }

    /// <summary>Ships look further than they shoot, so that they move in to engage.</summary>
    static double AggroRange(Entity e) => e.Kind == EntityKind.Ship ? Math.Max(e.Range * 1.6, 350) : e.Range;

    /// <summary>Armed entities pick targets; so do ships whose weapons sit on their turrets, to close in.</summary>
    public static void UpdateTargeting(World world, double dt)
    {
        foreach (var e in world.Entities)
        {
            var seeks = e.Weapons.Count > 0 || (e.Kind == EntityKind.Ship && e.Range > 0);
            if (!seeks || !Operational(e)) continue;
            e.Retarget -= dt;
            if (e.Retarget > 0) continue;
            e.Retarget = RetargetInterval;
            e.TargetId = NearestHostile(world, e, AggroRange(e))?.Id;
            // Craft fly for their carrier: they go for what it fights (not in the web version).
            if (Movement.CarrierOf(world, e) is { } carrier && Movement.FocusOf(world, carrier) is { } focus) e.TargetId = focus.Id;
            // Taunted: only the bulwark that did it, while it can be seen (not in the web version).
            if (e.Effects.Taunted > 0 && world.Entities.Get(e.Effects.TauntedBy) is { } taunter && Vision.IsVisible(world, e.Faction, taunter))
                e.TargetId = taunter.Id;
        }
    }

    /// <summary>Faction trait, weapons research and damage abilities.</summary>
    public static double DamageMult(World world, Entity e)
    {
        var m = Factions.Trait(e.Faction).Damage * (1 + Techs.WeaponsBonus * ResearchLevel(world, e.Faction, TechId.Weapons));
        if (IsActive(world, e, AbilityId.Barrage)) m *= 1.5;
        if (IsActive(world, e, AbilityId.Broadside)) m *= 2;
        // Out of hiding, the first blows hit hard.
        if (IsActive(world, e, AbilityId.Cloak)) m *= 2;
        var owner = OwnerOf(world, e);
        if (owner.Hero is HeroId h) m *= Heroes.Defs[h].Damage;
        // Stations and platforms, and the guns on them, hit harder with fortifications research.
        if (owner.Kind == EntityKind.Structure) m *= 1 + Techs.FortificationBonus * ResearchLevel(world, e.Faction, TechId.Fortification);
        if (InCommandAura(world, owner)) m *= 1.15;
        return m;
    }

    static double Reload(World world, Entity e, WeaponDef w) =>
        // A little jitter keeps identical ships from firing in lockstep.
        w.Cooldown * (0.9 + Rand(world) * 0.2) * (IsActive(world, e, AbilityId.Salvo) ? 1.0 / 3 : 1);

    static void Fire(World world, Entity e, WeaponDef w, Entity t)
    {
        // Aim where the target will be when the shot arrives.
        var tvx = Math.Cos(t.Angle) * t.Speed;
        var tvy = Math.Sin(t.Angle) * t.Speed;
        var time = Dist(e, t) / w.ProjectileSpeed;
        var ax = t.X + tvx * time - e.X;
        var ay = t.Y + tvy * time - e.Y;
        var len = Hypot(ax, ay);
        if (len == 0) len = 1;
        world.Projectiles.Add(new Projectile
        {
            Id = world.NextId++, Faction = e.Faction, Kind = w.Kind,
            X = e.X, Y = e.Y,
            Vx = ax / len * w.ProjectileSpeed, Vy = ay / len * w.ProjectileSpeed,
            Speed = w.ProjectileSpeed, Damage = w.Damage * DamageMult(world, e), Mult = w.Multipliers,
            TargetId = t.Id, Homing = w.Homing, Ttl = w.Range * 1.3 / w.ProjectileSpeed,
        });
    }

    /// <summary>The enemy closest to where a pilot aims, among those within `range`.</summary>
    public static Entity? AimedTarget(World world, Entity e, Pilot pilot, double range)
    {
        Entity? best = null;
        var bestD = double.PositiveInfinity;
        foreach (var t in world.Entities)
        {
            if (t.Faction == e.Faction || Reach(e, t) > range || !Vision.IsVisible(world, e.Faction, t)) continue;
            var d = Hypot(t.X - pilot.AimX, t.Y - pilot.AimY) - t.Radius;
            if (d < bestD)
            {
                bestD = d;
                best = t;
            }
        }
        return best;
    }

    /// <summary>A hand-flown ship and its turrets only fire while the pilot holds the trigger.</summary>
    static Pilot? PilotOf(World world, Entity e) => e.Pilot ?? world.Entities.Get(e.ParentId)?.Pilot;

    public static void UpdateWeapons(World world, double dt)
    {
        foreach (var e in world.Entities)
        {
            if (e.Weapons.Count == 0 || !Operational(e) || IsStunned(world, e)) continue;
            var pilot = PilotOf(world, e);
            if (pilot is not null)
            {
                foreach (var w in e.Weapons)
                {
                    w.Cooldown -= dt;
                    if (w.Cooldown > 0) continue;
                    var t = pilot.Firing ? AimedTarget(world, e, pilot, w.Def.Range) : null;
                    if (t is null)
                    {
                        w.Cooldown = 0;
                        continue;
                    }
                    Fire(world, e, w.Def, t);
                    w.Cooldown = Reload(world, e, w.Def);
                }
                continue;
            }
            var ordered = e.Order?.Type == OrderType.Attack ? world.Entities.Get(e.Order.TargetId) : null;
            var auto = world.Entities.Get(e.TargetId);
            foreach (var w in e.Weapons)
            {
                w.Cooldown -= dt;
                if (w.Cooldown > 0) continue;
                var t = ordered is not null && Reach(e, ordered) <= w.Def.Range ? ordered
                    : auto is not null && Reach(e, auto) <= w.Def.Range ? auto : null;
                if (t is null)
                {
                    w.Cooldown = 0;
                    continue;
                }
                Fire(world, e, w.Def, t);
                w.Cooldown = Reload(world, e, w.Def);
            }
        }
    }

    public static void UpdateProjectiles(World world, double dt)
    {
        // Survivors are packed to the front of the same list, in order: no new list every step.
        var all = world.Projectiles;
        var kept = 0;
        for (var i = 0; i < all.Count; i++)
        {
            var p = all[i];
            var t = world.Entities.Get(p.TargetId);
            if (p.Homing && t is not null)
            {
                var len = Dist(p, t);
                if (len == 0) len = 1;
                p.Vx = (t.X - p.X) / len * p.Speed;
                p.Vy = (t.Y - p.Y) / len * p.Speed;
            }
            p.X += p.Vx * dt;
            p.Y += p.Vy * dt;
            p.Ttl -= dt;
            if (t is not null && Dist(p, t) <= t.Radius)
            {
                var owner = OwnerOf(world, t);
                var shieldOf = owner.Kind == EntityKind.Ship ? owner : t;
                int? shieldId = shieldOf.Shield > 0 ? shieldOf.Id : null;
                Hurt(world, t, p.Damage * p.Mult.Of(t.Size), p.Faction);
                world.Events.Add(new Hit(p.X, p.Y, p.Kind, t.Id, t.Faction, shieldId));
                continue;
            }
            if (p.Ttl > 0) all[kept++] = p;
        }
        all.RemoveRange(kept, all.Count - kept);
    }

    /// <summary>Stations and ships with shield generators lose their shields with the last generator.</summary>
    public static void UpdateShields(World world, double dt)
    {
        foreach (var e in world.Entities)
        {
            // Biomechanical hulls mend themselves, but a ship already lost stays lost.
            var regen = Factions.Trait(e.Faction).HullRegen;
            if (regen > 0 && e.Kind == EntityKind.Ship && e.Hull > 0) e.Hull = Math.Min(e.MaxHull, e.Hull + e.MaxHull * regen * dt);
            if (e.MaxShield <= 0 || !Operational(e) || e.Effects.Stunned > 0) continue;
            if (e.ShieldDown > 0)
            {
                e.ShieldDown -= dt;
                continue;
            }
            if (e.ShieldGens == 0)
            {
                e.Shield = 0;
                continue;
            }
            var boost = IsActive(world, e, AbilityId.Overcharge) ? 4 : 1;
            e.Shield = Math.Min(e.MaxShield, e.Shield + e.ShieldRegen * boost * dt);
        }
    }

    /// <summary>Support ships repair the most damaged friendly ship or structure in range; repair research speeds them up.</summary>
    public static void UpdateRepair(World world, double dt)
    {
        foreach (var e in world.Entities)
        {
            // Repair docks mend every friendly ship around them.
            var dock = e.Kind == EntityKind.Structure && Operational(e) && Ids.IsBuildable(e.Type, out var b) ? Buildables.Defs[b].Repair : null;
            if (dock is not null)
            {
                foreach (var t in world.Grid.Near(e.X, e.Y, dock.Range))
                {
                    if (t.Kind == EntityKind.Ship && t.Faction == e.Faction && t.Hull > 0 && Dist(e, t) <= dock.Range)
                        t.Hull = Math.Min(t.MaxHull, t.Hull + dock.Rate * RepairBoost(world, e) * dt);
                }
                continue;
            }
            if (e.Kind != EntityKind.Ship) continue;
            var repair = Ships.Defs[e.Cls].Repair;
            if (repair is null) continue;
            Entity? best = null;
            var bestRatio = 1.0;
            foreach (var t in world.Grid.Near(e.X, e.Y, repair.Range))
            {
                // What is already lost this step is not brought back.
                if (t == e || t.Faction != e.Faction || !Operational(t) || t.Hull <= 0) continue;
                var ratio = t.Hull / t.MaxHull;
                if (ratio < bestRatio && Dist(e, t) - t.Radius <= repair.Range)
                {
                    bestRatio = ratio;
                    best = t;
                }
            }
            if (best is not null) best.Hull = Math.Min(best.MaxHull, best.Hull + repair.Rate * RepairBoost(world, e) * dt);
        }
    }

    static double RepairBoost(World world, Entity e) => 1 + Techs.RepairBonus * ResearchLevel(world, e.Faction, TechId.Repair);
}
