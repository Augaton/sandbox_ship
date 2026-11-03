// Targeting, weapons, projectiles, shields and repairs. Port of src/sim/combat.ts; targeting by
// score, beams, bursts, point defense and ion bolts are this version's.
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

    /// <summary>
    /// What targeting and weapons weigh (not in the web version): the damage already flying at
    /// each entity, and how many ships of each side fight each ship or structure, a part counting
    /// as what it belongs to, gathered at most every GatherEvery seconds; and, every step, the
    /// torpedoes and missiles point defense may shoot down.
    /// </summary>
    internal sealed class Pressure
    {
        public double IncomingAt = double.NaN, ThreatsAt = double.NaN;
        public readonly Dictionary<int, double> Incoming = [];
        public readonly Dictionary<(int Side, int Target), int> Targeted = [];
        public readonly List<Projectile> Threats = [];
    }

    /// <summary>Targets are picked every half second: what they weigh may be a quarter of a second old.</summary>
    const double GatherEvery = 0.25;

    static Pressure Gather(World world)
    {
        var p = world.Pressure;
        if (world.Time - p.IncomingAt < GatherEvery) return p;
        p.IncomingAt = world.Time;
        p.Incoming.Clear();
        p.Targeted.Clear();
        foreach (var s in world.Projectiles)
        {
            if (s.Prey is not null || world.Entities.Get(s.TargetId) is not { } t) continue;
            p.Incoming[t.Id] = p.Incoming.GetValueOrDefault(t.Id) + s.Damage * s.Mult.Of(t.Size);
        }
        foreach (var e in world.Entities)
        {
            if (e.Kind != EntityKind.Ship || world.Entities.Get(e.TargetId) is not { } t) continue;
            var key = (e.Side.Id, OwnerOf(world, t).Id);
            p.Targeted[key] = p.Targeted.GetValueOrDefault(key) + 1;
        }
        return p;
    }

    /// <summary>Damage a weapon keeps up in a second: a burst's rounds over the burst and its reload.</summary>
    static double Rate(WeaponDef w) =>
        w.Kind == WeaponKind.Beam ? w.Damage : w.Damage * w.Burst / Math.Max(0.05, w.Cooldown + (w.Burst - 1) * w.BurstGap);

    /// <summary>How well weapons hit a size class, against how well they hit on average: above 1, what they are made for.</summary>
    static double Effect(IEnumerable<WeaponDef> weapons, SizeClass size)
    {
        double at = 0, all = 0;
        foreach (var w in weapons)
        {
            var m = w.Multipliers;
            var rate = Rate(w);
            at += rate * m.Of(size);
            all += rate * (m.Small + m.Medium + m.Large + m.Structure) / 4;
        }
        return all > 0 ? at / all : 1;
    }

    /// <summary>A ship class's, by size: its own weapons and its turrets' together, what the ship closes in for.</summary>
    static readonly double[][] ClassEffects = Ships.All
        .Select(c => Ships.Defs[c].Weapons.Concat(Ships.Defs[c].Hardpoints?.SelectMany(h => h.Weapons ?? []) ?? []).ToList())
        .Select(ws => Enum.GetValues<SizeClass>().Select(s => Effect(ws, s)).ToArray())
        .ToArray();

    /// <summary>
    /// How much `t` is worth shooting for `e`, `d` away out of `range` (not in the web version,
    /// which took the nearest), in two parts. First what e's weapons are made for (`effect`, by
    /// size), nearer rather than further, and a target already damaged to finish off.
    /// </summary>
    static double Promise(World world, Entity t, double d, double range, ReadOnlySpan<double> effect, double beat, out Entity owner, out double left)
    {
        var s = effect[(int)t.Size] * (1 - 0.5 * Math.Clamp(d / range, 0, 1));
        owner = t;
        left = 0;
        // Out of the running even if finished off: no need to look up what it belongs to.
        if (s * Finish < beat) return 0;
        // A ship's parts share its shields.
        owner = OwnerOf(world, t);
        var shielded = owner.Kind == EntityKind.Ship ? owner : t;
        left = Math.Max(0, t.Hull) + shielded.Shield;
        var full = t.MaxHull + shielded.MaxShield;
        return full > 0 ? s * (1 + (Finish - 1) * (1 - Math.Clamp(left / full, 0, 1))) : s;
    }

    /// <summary>The most a target's damage raises its Promise by: a target all but destroyed.</summary>
    const double Finish = 1.6;

    /// <summary>
    /// Then, on the Promise: little for a target the shots in flight will destroy anyway; for a
    /// ship, the ship or structure its neighbours fight and the one that fights it, by what its
    /// hull aims at (a part counts as what it belongs to; `current` is what e's target belongs to);
    /// and an edge to the target e already has, so that it does not flicker between two.
    /// </summary>
    static double Score(World world, Entity e, Entity t, double s, Entity owner, double left, Pressure p, Entity? current)
    {
        if (p.Incoming.TryGetValue(t.Id, out var coming) && coming >= left) s *= 0.15;
        if (e.Kind == EntityKind.Ship)
        {
            var others = p.Targeted.GetValueOrDefault((e.Side.Id, owner.Id)) - (current == owner ? 1 : 0);
            s *= 1 + 0.3 * Math.Min(others, 3);
            if (world.Entities.Get(owner.TargetId) is { } aimed && OwnerOf(world, aimed) == e) s *= 1.25;
        }
        if (e.TargetId == t.Id) s *= 1.15;
        return s;
    }

    /// <summary>The most Score raises a Promise by, for a ship (neighbours on it, it fighting the ship, it already the target), and for the rest.</summary>
    const double ShipEdge = 1.9 * 1.25 * 1.15, OtherEdge = 1.15;

    /// <summary>
    /// The foe most worth shooting within `range` of `e`; ties go to the lowest id. Once one is
    /// found, a candidate whose Promise cannot beat it, whatever Score adds, is dropped before
    /// asking who it is allied with, whether it is seen and the rest.
    /// </summary>
    static Entity? BestHostile(World world, Entity e, double range)
    {
        // What e's weapons are made for, worked out once rather than for every candidate.
        Span<double> effect = stackalloc double[4];
        if (e.Kind == EntityKind.Ship) ClassEffects[(int)e.Cls].CopyTo(effect);
        else
        {
            // As Effect does, in one pass over the part's or the structure's weapons, with no list.
            var all = 0.0;
            foreach (var w in e.Weapons)
            {
                var m = w.Def.Multipliers;
                var rate = Rate(w.Def);
                effect[0] += rate * m.Small;
                effect[1] += rate * m.Medium;
                effect[2] += rate * m.Large;
                effect[3] += rate * m.Structure;
                all += rate * (m.Small + m.Medium + m.Large + m.Structure) / 4;
            }
            for (var size = 0; size < 4; size++) effect[size] = all > 0 ? effect[size] / all : 1;
        }
        var edge = e.Kind == EntityKind.Ship ? ShipEdge : OtherEdge;
        Entity? best = null;
        var bestScore = 0.0;
        Pressure? p = null;
        var current = world.Entities.Get(e.TargetId) is { } was ? OwnerOf(world, was) : null;
        // Vision.IsVisible for a foe, with what it looks up taken once: whom e's side sees, and
        // which sides are foes (by side id, 1 foe, -1 not, 0 not asked yet).
        var seen = world.Fog ? world.Visible.GetValueOrDefault(e.Side) : null;
        Span<sbyte> foes = stackalloc sbyte[8];
        foreach (var t in world.Grid.Near(e.X, e.Y, range))
        {
            if (t.Side == e.Side) continue;
            var d = Reach(e, t);
            if (d >= range) continue;
            var beat = best is null ? 0 : bestScore / edge;
            var s = Promise(world, t, d, range, effect, beat, out var owner, out var left);
            if (best is not null && s < beat) continue;
            var side = t.Side.Id;
            var foe = side < foes.Length ? (foes[side] != 0 ? foes[side] > 0 : (foes[side] = Foes(world, e.Side, t.Side) ? (sbyte)1 : (sbyte)-1) > 0)
                : Foes(world, e.Side, t.Side);
            if (!foe || Vision.Cloaked(t) || (world.Fog && seen?.Contains(t.Id) != true)) continue;
            s = Score(world, e, t, s, owner, left, p ??= Gather(world), current);
            if (best is null || s > bestScore || (s == bestScore && t.Id < best.Id))
            {
                bestScore = s;
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
            e.TargetId = BestHostile(world, e, AggroRange(e))?.Id;
            // Craft fly for their carrier: they go for what it fights (not in the web version).
            if (Movement.CarrierOf(world, e) is { } carrier && Movement.FocusOf(world, carrier) is { } focus) e.TargetId = focus.Id;
            // Taunted: only the bulwark that did it, while it can be seen (not in the web version).
            if (e.Effects.Taunted > 0 && world.Entities.Get(e.Effects.TauntedBy) is { } taunter && Vision.IsVisible(world, e.Side, taunter))
                e.TargetId = taunter.Id;
        }
    }

    /// <summary>Race trait, weapons research and damage abilities.</summary>
    public static double DamageMult(World world, Entity e)
    {
        var m = Factions.Trait(e.Race).Damage * (1 + Techs.WeaponsBonus * ResearchLevel(world, e.Side, TechId.Weapons));
        if (IsActive(world, e, AbilityId.Barrage)) m *= 1.5;
        if (IsActive(world, e, AbilityId.Broadside)) m *= 2;
        // Out of hiding, the first blows hit hard.
        if (IsActive(world, e, AbilityId.Cloak)) m *= 2;
        var owner = OwnerOf(world, e);
        if (owner.Hero is HeroId h) m *= Heroes.Defs[h].Damage;
        // Stations and platforms, and the guns on them, hit harder with fortifications research.
        if (owner.Kind == EntityKind.Structure) m *= 1 + Techs.FortificationBonus * ResearchLevel(world, e.Side, TechId.Fortification);
        if (InCommandAura(world, owner)) m *= 1.15;
        return m;
    }

    static double Reload(World world, Entity e, WeaponDef w) =>
        // A little jitter keeps identical ships from firing in lockstep.
        w.Cooldown * (0.9 + Rand(world) * 0.2) * (IsActive(world, e, AbilityId.Salvo) ? 1.0 / 3 : 1);

    /// <summary>After a shot: the next round of the burst, or the reload once the burst is spent.</summary>
    static void Fired(World world, Entity e, WeaponState w)
    {
        if (--w.Shots > 0)
        {
            w.Cooldown = w.Def.BurstGap;
            return;
        }
        w.Shots = w.Def.Burst;
        w.Cooldown = Reload(world, e, w.Def);
    }

    /// <summary>
    /// What it takes to shoot a torpedo or a missile down in flight; the rest cannot be. Two flak
    /// rounds for a torpedo: a lone gun may stop one if it is ready as the torpedo comes in reach.
    /// </summary>
    static double Toughness(WeaponKind kind) => kind switch
    {
        WeaponKind.Torpedo => 8,
        WeaponKind.Missile => 3,
        _ => 0,
    };

    /// <summary>Seconds between the sparks of a beam on its target.</summary>
    const double BeamSpark = 0.25;

    /// <summary>Within this of a torpedo or a missile, a point defense round may hit it, one time in InterceptOdds.</summary>
    const double InterceptRadius = 8, InterceptOdds = 0.5;

    static void Fire(World world, Entity e, WeaponState state, Entity t)
    {
        var w = state.Def;
        // Aim where the target will be when the shot arrives.
        var tvx = Math.Cos(t.Angle) * t.Speed;
        var tvy = Math.Sin(t.Angle) * t.Speed;
        var time = Dist(e, t) / w.ProjectileSpeed;
        var ax = t.X + tvx * time - e.X;
        var ay = t.Y + tvy * time - e.Y;
        var len = Hypot(ax, ay);
        if (len == 0) len = 1;
        // The rounds of a homing burst leave side by side: a swarm rather than a string.
        var lane = w.Burst > 1 && w.Homing ? (state.Shots - (w.Burst + 1) / 2.0) * 5 : 0;
        world.Projectiles.Add(new Projectile
        {
            Id = world.NextId++, Side = e.Side, Kind = w.Kind,
            X = e.X - ay / len * lane, Y = e.Y + ax / len * lane,
            Vx = ax / len * w.ProjectileSpeed, Vy = ay / len * w.ProjectileSpeed,
            Speed = w.ProjectileSpeed, Damage = w.Damage * DamageMult(world, e), Mult = w.Multipliers,
            TargetId = t.Id, Homing = w.Homing, Ttl = w.Range * 1.3 / w.ProjectileSpeed,
            ShieldMult = w.ShieldMult, HullMult = w.HullMult, Hp = Toughness(w.Kind),
        });
    }

    /// <summary>
    /// A beam holds on its target while it is in reach (not in the web version): damage every
    /// step, the line for the view, and a spark on the target now and then.
    /// </summary>
    static void HoldBeam(World world, Entity e, WeaponState w, Entity t, double dt)
    {
        var d = w.Def;
        Hurt(world, t, d.Damage * DamageMult(world, e) * d.Multipliers.Of(t.Size) * dt, e.Side, d.ShieldMult, d.HullMult);
        world.Beams.Add(new BeamLine(e.Id, t.Id, e.Side));
        w.Cooldown -= dt;
        if (w.Cooldown > 0) return;
        w.Cooldown = BeamSpark;
        var owner = OwnerOf(world, t);
        var shieldOf = owner.Kind == EntityKind.Ship ? owner : t;
        // On the near side of the target, where the beam meets it.
        var len = Math.Max(1, Dist(e, t));
        var (x, y) = (t.X + (e.X - t.X) / len * t.Radius * 0.7, t.Y + (e.Y - t.Y) / len * t.Radius * 0.7);
        world.Events.Add(new Hit(x, y, WeaponKind.Beam, t.Id, t.Side, shieldOf.Shield > 0 ? shieldOf.Id : null));
    }

    /// <summary>The torpedoes and missiles in flight, gathered once a step for point defense.</summary>
    static void GatherThreats(World world)
    {
        var p = world.Pressure;
        if (p.ThreatsAt == world.Time) return;
        p.ThreatsAt = world.Time;
        p.Threats.Clear();
        foreach (var s in world.Projectiles)
        {
            if (s.Hp > 0) p.Threats.Add(s);
        }
    }

    /// <summary>
    /// The torpedo or missile most worth shooting down within `range` of `e`: a foe's, flying at
    /// e's side or an ally's, not yet doomed by the rounds already sent at it, the nearest to what
    /// it flies at. The list is in id order: ties go to the oldest.
    /// </summary>
    static Projectile? Threat(World world, Entity e, double range)
    {
        Projectile? best = null;
        var bestD = double.PositiveInfinity;
        foreach (var p in world.Pressure.Threats)
        {
            if (p.Ttl <= 0 || p.Aimed >= p.Hp || Allies(world, p.Side, e.Side) || Hypot(p.X - e.X, p.Y - e.Y) > range) continue;
            if (world.Entities.Get(p.TargetId) is not { } t || !Allies(world, t.Side, e.Side)) continue;
            var d = Dist(p, t);
            if (d < bestD)
            {
                bestD = d;
                best = p;
            }
        }
        return best;
    }

    /// <summary>A point defense round at a torpedo or a missile, aimed where it will be.</summary>
    static void Intercept(World world, Entity e, WeaponDef w, Projectile prey)
    {
        var damage = w.Damage * DamageMult(world, e);
        prey.Aimed += damage;
        var time = Hypot(prey.X - e.X, prey.Y - e.Y) / w.ProjectileSpeed;
        var ax = prey.X + prey.Vx * time - e.X;
        var ay = prey.Y + prey.Vy * time - e.Y;
        var len = Hypot(ax, ay);
        if (len == 0) len = 1;
        world.Projectiles.Add(new Projectile
        {
            Id = world.NextId++, Side = e.Side, Kind = w.Kind,
            X = e.X, Y = e.Y,
            Vx = ax / len * w.ProjectileSpeed, Vy = ay / len * w.ProjectileSpeed,
            Speed = w.ProjectileSpeed, Damage = damage, Homing = true, Prey = prey, Ttl = w.Range * 1.3 / w.ProjectileSpeed,
        });
    }

    /// <summary>The foe closest to where a pilot aims, among those within `range`.</summary>
    public static Entity? AimedTarget(World world, Entity e, Pilot pilot, double range)
    {
        Entity? best = null;
        var bestD = double.PositiveInfinity;
        foreach (var t in world.Entities)
        {
            if (!Foes(world, e.Side, t.Side) || Reach(e, t) > range || !Vision.IsVisible(world, e.Side, t)) continue;
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
        world.Beams.Clear();
        GatherThreats(world);
        foreach (var e in world.Entities)
        {
            if (e.Weapons.Count == 0 || !Operational(e) || IsStunned(world, e)) continue;
            var pilot = PilotOf(world, e);
            if (pilot is not null)
            {
                foreach (var w in e.Weapons)
                {
                    if (w.Def.Kind == WeaponKind.Beam)
                    {
                        if (pilot.Firing && AimedTarget(world, e, pilot, w.Def.Range) is { } held) HoldBeam(world, e, w, held, dt);
                        continue;
                    }
                    w.Cooldown -= dt;
                    if (w.Cooldown > 0) continue;
                    var t = pilot.Firing ? AimedTarget(world, e, pilot, w.Def.Range) : null;
                    if (t is null)
                    {
                        w.Cooldown = 0;
                        continue;
                    }
                    Fire(world, e, w, t);
                    Fired(world, e, w);
                }
                continue;
            }
            // A ship's turrets fire at what the ship was ordered to attack, once it is in their reach,
            // rather than at the nearest foe (not in the web version): focus fire, and a flagship under
            // its station's guns can still be singled out. A weapon that barely scratches that target
            // (flak against a big ship) fires at its own instead, and point defense shoots torpedoes
            // and missiles down first.
            var commander = e.Kind == EntityKind.Hardpoint ? OwnerOf(world, e) : e;
            var ordered = commander.Order?.Type == OrderType.Attack ? world.Entities.Get(commander.Order.TargetId) : null;
            var auto = world.Entities.Get(e.TargetId);
            foreach (var w in e.Weapons)
            {
                var beam = w.Def.Kind == WeaponKind.Beam;
                if (!beam)
                {
                    w.Cooldown -= dt;
                    if (w.Cooldown > 0) continue;
                    if (w.Def.PointDefense && Threat(world, e, w.Def.Range) is { } prey)
                    {
                        Intercept(world, e, w.Def, prey);
                        Fired(world, e, w);
                        continue;
                    }
                }
                var orderedInReach = ordered is not null && Reach(e, ordered) <= w.Def.Range;
                var t = orderedInReach && w.Def.Multipliers.Of(ordered!.Size) >= 0.5 ? ordered
                    : auto is not null && Reach(e, auto) <= w.Def.Range ? auto
                    : orderedInReach ? ordered : null;
                if (beam)
                {
                    if (t is not null) HoldBeam(world, e, w, t, dt);
                    continue;
                }
                if (t is null)
                {
                    w.Cooldown = 0;
                    continue;
                }
                Fire(world, e, w, t);
                Fired(world, e, w);
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
            // Shot down by a round that came before it in the list.
            if (p.Ttl <= 0) continue;
            if (p.Prey is not null)
            {
                Chase(world, p, dt);
                if (p.Ttl > 0) all[kept++] = p;
                continue;
            }
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
            // Crashed on the rocks of a belt (Terrain.cs): a spark, as a shot brought down.
            if (world.Terrain.Count > 0 && Terrains.Stops(world, p.Kind, p.X, p.Y))
            {
                world.Events.Add(new Intercepted(p.X, p.Y, p.Side));
                p.Ttl = 0;
                continue;
            }
            if (t is not null && Dist(p, t) <= t.Radius)
            {
                var owner = OwnerOf(world, t);
                var shieldOf = owner.Kind == EntityKind.Ship ? owner : t;
                int? shieldId = shieldOf.Shield > 0 ? shieldOf.Id : null;
                Hurt(world, t, p.Damage * p.Mult.Of(t.Size), p.Side, p.ShieldMult, p.HullMult);
                world.Events.Add(new Hit(p.X, p.Y, p.Kind, t.Id, t.Side, shieldId));
                // Landed: point defense rounds still after it give up.
                p.Ttl = 0;
                continue;
            }
            if (p.Ttl > 0) all[kept++] = p;
        }
        all.RemoveRange(kept, all.Count - kept);
    }

    /// <summary>
    /// A point defense round flies at its torpedo or missile and, half the time, hits it: enough
    /// blows bring it down. One that misses, or is spent before reaching it, lets other rounds be
    /// sent at it; one whose prey is gone flies on, harmless, until it is spent.
    /// </summary>
    static void Chase(World world, Projectile p, double dt)
    {
        var prey = p.Prey!;
        if (prey.Ttl <= 0)
        {
            p.Prey = null;
            p.Homing = false;
        }
        else
        {
            var len = Dist(p, prey);
            if (len == 0) len = 1;
            p.Vx = (prey.X - p.X) / len * p.Speed;
            p.Vy = (prey.Y - p.Y) / len * p.Speed;
        }
        p.X += p.Vx * dt;
        p.Y += p.Vy * dt;
        p.Ttl -= dt;
        if (p.Prey is null) return;
        if (Dist(p, prey) <= InterceptRadius)
        {
            prey.Aimed -= p.Damage;
            p.Ttl = 0;
            if (Rand(world) >= InterceptOdds) return;
            prey.Hp -= p.Damage;
            if (prey.Hp > 0) return;
            prey.Ttl = 0;
            world.Events.Add(new Intercepted(prey.X, prey.Y, p.Side));
            return;
        }
        if (p.Ttl <= 0) prey.Aimed -= p.Damage;
    }

    /// <summary>Stations and ships with shield generators lose their shields with the last generator.</summary>
    public static void UpdateShields(World world, double dt)
    {
        foreach (var e in world.Entities)
        {
            // Slower in a nebula (Terrain.cs).
            var share = Terrains.RepairShare(world, e);
            // Biomechanical hulls mend themselves, but a ship already lost stays lost.
            var regen = Factions.Trait(e.Race).HullRegen;
            if (regen > 0 && e.Kind == EntityKind.Ship && e.Hull > 0) e.Hull = Math.Min(e.MaxHull, e.Hull + e.MaxHull * regen * share * dt);
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
            e.Shield = Math.Min(e.MaxShield, e.Shield + e.ShieldRegen * boost * share * dt);
        }
    }

    /// <summary>Support ships repair the most damaged ship or structure of their side or an ally's in range; repair research speeds them up.</summary>
    public static void UpdateRepair(World world, double dt)
    {
        foreach (var e in world.Entities)
        {
            // Repair docks mend every ship of their side and its allies around them.
            var dock = e.Kind == EntityKind.Structure && Operational(e) && Ids.IsBuildable(e.Type, out var b) ? Buildables.Defs[b].Repair : null;
            if (dock is not null)
            {
                foreach (var t in world.Grid.Near(e.X, e.Y, dock.Range))
                {
                    if (t.Kind == EntityKind.Ship && t.Hull > 0 && Allies(world, t.Side, e.Side) && Dist(e, t) <= dock.Range)
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
                if (t == e || !Operational(t) || t.Hull <= 0 || !Allies(world, t.Side, e.Side)) continue;
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

    static double RepairBoost(World world, Entity e) => 1 + Techs.RepairBonus * ResearchLevel(world, e.Side, TechId.Repair);
}
