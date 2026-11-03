// Abilities in battle: their effects, zones, delayed strikes, and when a side uses them by
// itself. Port of src/sim/abilities.ts.
using SandboxShip.Sim.Data;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim;

/// <summary>Where an ability is aimed: a spot on the map, and the enemy there if it needs one.</summary>
public readonly record struct Aim(double X, double Y, int? Id = null);

public static class AbilitySystem
{
    const double SpinalDamage = 900;
    /// <summary>Half the width of the spinal beam.</summary>
    const double SpinalWidth = 40;
    const double OrbitalDamage = 650;
    const int SwarmSize = 8;
    const double SwarmLife = 30;
    const double TurretLife = 45;
    /// <summary>Corrosion eats this share of the hull per second, within bounds.</summary>
    const double CorrosionShare = 0.03, CorrosionMin = 10, CorrosionMax = 90;
    const double GraftRegen = 0.03;
    /// <summary>How often the AI weighs using an ability, in seconds.</summary>
    const double AiThink = 0.5;

    /// <summary>The ship or structure an entity is part of; itself if it is not a part.</summary>
    public static Entity OwnerOf(World world, Entity e) =>
        e.Kind == EntityKind.Hardpoint && e.ParentId is not null ? world.Entities.Get(e.ParentId) ?? e : e;

    /// <summary>Whether `e` (or the ship it is a part of) has this ability's effect running.</summary>
    public static bool IsActive(World world, Entity e, AbilityId id)
    {
        // A plain loop: a predicate capturing `id` would cost a closure on every call, thousands a step.
        foreach (var a in OwnerOf(world, e).Abilities)
        {
            if (a.Id == id && a.Active > 0) return true;
        }
        return false;
    }

    public static bool IsStunned(World world, Entity e) => OwnerOf(world, e).Effects.Stunned > 0;

    /// <summary>Whether a zone of this kind, of its side or an ally's, covers the entity.</summary>
    public static bool InZone(World world, Entity e, ZoneKind kind)
    {
        foreach (var z in world.Zones)
        {
            if (z.Kind == kind && Hypot(z.X - e.X, z.Y - e.Y) <= z.R && Allies(world, z.Side, e.Side)) return true;
        }
        return false;
    }

    public static bool InCommandAura(World world, Entity e) => InZone(world, OwnerOf(world, e), ZoneKind.Command);

    /// <summary>Damage taken multiplier: worse when marked, halved under a shield dome.</summary>
    public static double DamageTaken(World world, Entity t)
    {
        var owner = OwnerOf(world, t);
        return (owner.Effects.Marked > 0 ? 1.5 : 1) * (InZone(world, owner, ZoneKind.Dome) ? 0.5 : 1);
    }

    /// <summary>
    /// Deals damage from a side, through the shields of the ship a part belongs to. Allies are
    /// never hurt. `shieldMult` and `hullMult`: how hard it hits each (an ion bolt's, see ApplyDamage).
    /// </summary>
    public static void Hurt(World world, Entity t, double amount, Side from, double shieldMult = 1, double hullMult = 1)
    {
        if (Allies(world, from, t.Side)) return;
        var parent = t.Kind == EntityKind.Hardpoint && t.ParentId is not null ? world.Entities.Get(t.ParentId) : null;
        var dealt = amount * DamageTaken(world, t);
        ApplyDamage(t, dealt, parent?.Kind == EntityKind.Ship ? parent : t, shieldMult, hullMult);
        t.LastHitBy = from;
        t.HitAt = world.Time;
        if (parent is not null)
        {
            parent.LastHitBy = from;
            parent.HitAt = world.Time;
        }
        world.LastDamage = world.Time;
        if (world.Factions.TryGetValue(from, out var f)) f.Stats.Damage += dealt;
    }

    /// <summary>
    /// Uses the ability of `e`. A point ability takes the spot aimed at, brought back within
    /// range; an enemy ability takes the enemy aimed at, which must be seen and within range.
    /// </summary>
    public static Result UseAbility(World world, Entity e, Aim? aim = null) =>
        e.Ability is { } first ? UseAbility(world, e, first.Id, aim) : Result.Fail("Aucune capacité");

    /// <summary>Uses one ability of `e`, by its id, the same way.</summary>
    public static Result UseAbility(World world, Entity e, AbilityId id, Aim? aim)
    {
        var a = e.Abilities.Find(x => x.Id == id);
        if (a is null) return Result.Fail("Aucune capacité");
        if (a.Cooldown > 0) return Result.Fail($"Prête dans {Math.Ceiling(a.Cooldown)} s");
        if (e.Effects.Stunned > 0) return Result.Fail("Paralysé");
        var def = Abilities.Defs[a.Id];
        var point = new Point(e.X, e.Y);
        Entity? target = null;
        if (def.Target == AbilityTarget.Enemy)
        {
            var t = aim?.Id is int aimed ? world.Entities.Get(aimed) : null;
            if (t is null || !Foes(world, e.Side, t.Side) || !Vision.IsVisible(world, e.Side, t)) return Result.Fail("Choisis un ennemi");
            if (Dist(e, t) - t.Radius > def.Range!.Value) return Result.Fail("Cible hors de portée");
            target = OwnerOf(world, t);
            point = new Point(t.X, t.Y);
        }
        else if (def.Target == AbilityTarget.Point)
        {
            if (aim is not Aim p) return Result.Fail("Choisis un point");
            var d = Hypot(p.X - e.X, p.Y - e.Y);
            var range = def.Range!.Value;
            var k = d > range ? range / d : 1;
            point = new Point(e.X + (p.X - e.X) * k, e.Y + (p.Y - e.Y) * k);
        }
        a.Cooldown = def.Cooldown * (1 - Techs.EnergyBonus * ResearchLevel(world, e.Side, TechId.Energy));
        a.Active = def.Duration;
        Apply(world, e, def, point, target);
        world.Events.Add(new AbilityUsed(a.Id, e.Side, e.Id, e.X, e.Y, point.X, point.Y));
        return Result.OK;
    }

    /// <summary>Instant effects; timed ones (sprint, salvo, dome...) only need `Active`.</summary>
    static void Apply(World world, Entity e, AbilityDef def, Point p, Entity? target)
    {
        switch (def.Id)
        {
            case AbilityId.Repair:
                foreach (var t in world.Entities)
                {
                    if (t.Kind == EntityKind.Ship && t.Hull > 0 && Allies(world, t.Side, e.Side) && Dist(t, e) <= def.Radius!.Value)
                        t.Hull = Math.Min(t.MaxHull, t.Hull + t.MaxHull * 0.25);
                }
                break;
            case AbilityId.Overcharge:
                e.Shield = e.MaxShield;
                break;
            case AbilityId.Broadside:
                foreach (var part in ChildrenOf(world, e.Id).Prepend(e)) foreach (var w in part.Weapons) w.Cooldown = 0;
                break;
            case AbilityId.Spinal:
            {
                var a = Math.Atan2(p.Y - e.Y, p.X - e.X);
                var len = def.Range!.Value;
                world.Strikes.Add(new Strike
                {
                    Kind = StrikeKind.Beam, Side = e.Side, X = e.X, Y = e.Y, X2 = e.X + Math.Cos(a) * len, Y2 = e.Y + Math.Sin(a) * len,
                    Radius = SpinalWidth, Damage = SpinalDamage, Delay = Abilities.SpinalCharge, Total = Abilities.SpinalCharge,
                });
                break;
            }
            case AbilityId.OrbitalStrike:
                world.Strikes.Add(new Strike
                {
                    Kind = StrikeKind.Orbital, Side = e.Side, X = p.X, Y = p.Y, X2 = p.X, Y2 = p.Y,
                    Radius = def.Radius!.Value, Damage = OrbitalDamage, Delay = Abilities.OrbitalDelay, Total = Abilities.OrbitalDelay,
                });
                break;
            case AbilityId.StarLance:
                world.Strikes.Add(new Strike
                {
                    Kind = StrikeKind.Stellar, Side = e.Side, X = p.X, Y = p.Y, X2 = p.X, Y2 = p.Y,
                    Radius = def.Radius!.Value, Damage = Abilities.StarLanceDamage, Delay = Abilities.StarLanceDelay, Total = Abilities.StarLanceDelay,
                });
                break;
            case AbilityId.HuntMark:
                target!.Effects.Marked = Abilities.Marked;
                break;
            case AbilityId.Spores:
                target!.Effects.Corroded = Abilities.Corroded;
                break;
            case AbilityId.FieldTurret:
                SpawnStructure(world, e.Side, BuildableType.Platform, p.X, p.Y).Expires = TurretLife;
                break;
            case AbilityId.Emp:
                foreach (var t in world.Entities)
                {
                    if (t.Kind != EntityKind.Ship || Dist(t, e) > def.Radius!.Value + t.Radius || !Foes(world, t.Side, e.Side)) continue;
                    t.Shield = 0;
                    t.Effects.Stunned = Abilities.Stunned;
                }
                break;
            case AbilityId.Taunt:
                foreach (var t in world.Entities)
                {
                    if (t.Kind == EntityKind.Hardpoint || Dist(t, e) > def.Radius!.Value + t.Radius || !Foes(world, t.Side, e.Side)) continue;
                    t.Effects.Taunted = Abilities.Taunted;
                    t.Effects.TauntedBy = e.Id;
                    t.TargetId = e.Id;
                }
                break;
            case AbilityId.Launch:
                // The next squadron goes now; the faster pace is in Structures.UpdateHangars.
                foreach (var part in ChildrenOf(world, e.Id)) if (part.Hangar is { } h) h.Timer = 0;
                break;
            case AbilityId.Swarm:
                for (var i = 0; i < SwarmSize; i++)
                {
                    var a = (double)i / SwarmSize * Math.PI * 2;
                    var s = SpawnShip(world, e.Side, ShipClass.Fighter, e.X + Math.Cos(a) * (e.Radius + 20), e.Y + Math.Sin(a) * (e.Radius + 20), a);
                    Movement.Assign(s, e, s.X, s.Y);
                    s.Launched = true;
                    s.Expires = SwarmLife;
                }
                break;
        }
    }

    static void Land(World world, Strike s)
    {
        var reach = s.Kind != StrikeKind.Beam ? s.Radius : Hypot(s.X2 - s.X, s.Y2 - s.Y) / 2 + s.Radius;
        var cx = (s.X + s.X2) / 2;
        var cy = (s.Y + s.Y2) / 2;
        var hit = new List<Entity>();
        foreach (var t in world.Grid.Near(cx, cy, reach))
        {
            if (!Foes(world, t.Side, s.Side)) continue;
            var d = s.Kind != StrikeKind.Beam ? Hypot(t.X - s.X, t.Y - s.Y) : SegmentDistance(t, s);
            if (d <= s.Radius + t.Radius) hit.Add(t);
        }
        foreach (var t in hit) Hurt(world, t, s.Damage, s.Side);
        if (s.Kind != StrikeKind.Beam && world.Terrain.Count > 0) Terrains.Blast(world, s.X, s.Y, s.Radius);
        world.Events.Add(new StrikeLanded(s));
    }

    static double SegmentDistance(Entity p, Strike s)
    {
        var dx = s.X2 - s.X;
        var dy = s.Y2 - s.Y;
        var len2 = dx * dx + dy * dy;
        if (len2 == 0) len2 = 1;
        var k = Math.Max(0, Math.Min(1, ((p.X - s.X) * dx + (p.Y - s.Y) * dy) / len2));
        return Hypot(p.X - (s.X + dx * k), p.Y - (s.Y + dy * k));
    }

    static int Count(World world, IPoint at, double r, Func<Entity, bool> test)
    {
        var n = 0;
        var c = new Point(at.X, at.Y);
        foreach (var t in world.Grid.Near(c.X, c.Y, r))
        {
            if (test(t) && Dist(t, c) <= r + t.Radius) n++;
        }
        return n;
    }

    /// <summary>Where to drop an orbital strike: on the thickest visible group of foes in range, if any.</summary>
    static Aim? BestCluster(World world, Entity e, AbilityDef def)
    {
        Aim? best = null;
        var most = 1;
        var range = def.Range!.Value;
        bool Enemy(Entity t) => t.Kind != EntityKind.Hardpoint && Foes(world, e.Side, t.Side) && Vision.IsVisible(world, e.Side, t);
        foreach (var c in world.Grid.Near(e.X, e.Y, range))
        {
            if (!Enemy(c) || Dist(c, e) > range) continue;
            var n = Count(world, c, def.Radius!.Value, Enemy) + (c.Kind == EntityKind.Structure ? 1 : 0);
            if (n > most)
            {
                most = n;
                best = new Aim(c.X, c.Y);
            }
        }
        return best;
    }

    /// <summary>What a blow there would destroy, in credits: ships at their price, stations above all.</summary>
    static double Worth(Entity t) => t.Kind switch
    {
        EntityKind.Ship => t.Hero is HeroId h ? Heroes.Defs[h].Cost : Ships.UnitValue(t.Cls),
        EntityKind.Structure when t.Type == "station" => 10000,
        EntityKind.Structure when Ids.IsBuildable(t.Type, out var b) => Buildables.Defs[b].Cost,
        _ => 0,
    };

    /// <summary>
    /// Where the star lance does the most harm among the foes its side sees: the spot
    /// worth the most within its radius, if worth a blow at all.
    /// </summary>
    static Aim? RichestTarget(World world, Entity e, AbilityDef def)
    {
        Aim? best = null;
        var most = 4000.0;
        var r = def.Radius!.Value;
        bool Enemy(Entity t) => t.Kind != EntityKind.Hardpoint && Foes(world, e.Side, t.Side) && Vision.IsVisible(world, e.Side, t);
        foreach (var c in world.Entities)
        {
            if (!Enemy(c) || (c.Kind == EntityKind.Ship && Ships.Defs[c.Cls].Size == SizeClass.Small)) continue;
            var worth = 0.0;
            foreach (var t in world.Grid.Near(c.X, c.Y, r)) if (Enemy(t) && Dist(t, c) <= r) worth += Worth(t);
            if (worth <= most) continue;
            most = worth;
            best = new Aim(c.X, c.Y);
        }
        return best;
    }

    /// <summary>When and where an automatic ability goes: null to hold it for now.</summary>
    static Aim? AutoAim(World world, Entity e, AbilityState ability)
    {
        var def = Abilities.Defs[ability.Id];
        var self = new Aim(e.X, e.Y);
        var t = world.Entities.Get(e.TargetId);
        var reach = t is not null ? Dist(e, t) - t.Radius : double.PositiveInfinity;
        Func<Entity, bool> Ally(double min) => a => a.Kind == EntityKind.Ship && a.Hull < a.MaxHull * min && Allies(world, a.Side, e.Side);
        switch (def.Id)
        {
            case AbilityId.Repair:
                return Count(world, e, def.Radius!.Value, Ally(0.6)) >= 1 ? self : null;
            case AbilityId.Graft:
                return Count(world, e, def.Radius!.Value, Ally(0.7)) >= 2 ? self : null;
            case AbilityId.Overcharge:
                return t is not null && e.Shield < e.MaxShield * 0.3 ? self : null;
            case AbilityId.Sprint:
                return t is not null && reach > e.Range ? self : null;
            case AbilityId.Emp:
                return Count(world, e, def.Radius!.Value, a => a.Kind == EntityKind.Ship && Foes(world, a.Side, e.Side)) >= 2 ? self : null;
            case AbilityId.ShieldDome:
                return t is not null && Count(world, e, def.Radius!.Value, Ally(1.01)) >= 3 ? self : null;
            case AbilityId.OrbitalStrike:
                return BestCluster(world, e, def);
            case AbilityId.StarLance:
                return RichestTarget(world, e, def);
            case AbilityId.Spinal:
                return t is not null && Dist(e, t) <= def.Range!.Value ? new Aim(t.X, t.Y) : null;
            case AbilityId.HuntMark:
            case AbilityId.Spores:
                return t is not null && reach <= def.Range!.Value ? new Aim(t.X, t.Y, t.Id) : null;
            case AbilityId.FieldTurret:
            {
                if (t is null || reach > 600) return null;
                var d = Dist(e, t);
                if (d == 0) d = 1;
                return new Aim(e.X + (t.X - e.X) / d * 150, e.Y + (t.Y - e.Y) / d * 150);
            }
            case AbilityId.Cloak:
            case AbilityId.Swarm:
            case AbilityId.Launch:
                return t is not null ? self : null;
            case AbilityId.Taunt:
                // Worth it once several enemies would turn on it rather than on frailer ships.
                return Count(world, e, def.Radius!.Value, a => a.Kind == EntityKind.Ship && Foes(world, a.Side, e.Side)) >= 3 ? self : null;
            default:
                // Salvo, barrage, broadside: once in range.
                return t is not null && reach <= e.Range ? self : null;
        }
    }

    public static void UpdateAbilities(World world, double dt)
    {
        // Zones follow the units that cast them; admirals command around them all the time.
        world.Zones.Clear();
        foreach (var e in world.Entities)
        {
            if (e.Hero?.Role == HeroRole.Admiral) world.Zones.Add(new Zone(ZoneKind.Command, e.Side, e.X, e.Y, Abilities.CommandRadius));
            foreach (var ab in e.Abilities)
            {
                if (ab.Active > 0 && (ab.Id == AbilityId.ShieldDome || ab.Id == AbilityId.Graft))
                    world.Zones.Add(new Zone(ab.Id == AbilityId.ShieldDome ? ZoneKind.Dome : ZoneKind.Graft, e.Side, e.X, e.Y, Abilities.Defs[ab.Id].Radius!.Value));
            }
        }
        var think = Math.Floor(world.Time / AiThink) != Math.Floor((world.Time - dt) / AiThink);
        foreach (var e in world.Entities)
        {
            var fx = e.Effects;
            if (fx.Corroded > 0)
            {
                e.Hull -= Math.Min(CorrosionMax, Math.Max(CorrosionMin, e.MaxHull * CorrosionShare)) * dt;
                world.LastDamage = world.Time;
                fx.Corroded = Math.Max(0, fx.Corroded - dt);
            }
            if (fx.Marked > 0) fx.Marked = Math.Max(0, fx.Marked - dt);
            if (fx.Stunned > 0) fx.Stunned = Math.Max(0, fx.Stunned - dt);
            if (fx.Taunted > 0) fx.Taunted = Math.Max(0, fx.Taunted - dt);
            if (e.Expires is not null)
            {
                e.Expires -= dt;
                if (e.Expires <= 0) e.Hull = 0;
            }
            if (e.Kind == EntityKind.Ship && e.Hull > 0 && e.Hull < e.MaxHull && InZone(world, e, ZoneKind.Graft))
            {
                e.Hull = Math.Min(e.MaxHull, e.Hull + e.MaxHull * GraftRegen * dt);
            }
            // By index: an ability may add entities, never abilities, but stay safe if one does.
            // A star lance under construction is not charging yet.
            for (var i = 0; i < e.Abilities.Count && e.Build >= 1; i++)
            {
                var a = e.Abilities[i];
                a.Cooldown = Math.Max(0, a.Cooldown - dt);
                a.Active = Math.Max(0, a.Active - dt);
                if (!think || a.Cooldown > 0 || e.Pilot is not null || fx.Stunned > 0) continue;
                if (!world.Factions.TryGetValue(e.Side, out var f) || !f.AutoAbilities) continue;
                // A player's star lance fires only when the player says so.
                if (e.Kind == EntityKind.Structure && f.AI is null) continue;
                if (AutoAim(world, e, a) is Aim aim) UseAbility(world, e, a.Id, aim);
            }
        }
        if (world.Strikes.Count == 0) return;
        var waiting = new List<Strike>();
        foreach (var s in world.Strikes)
        {
            s.Delay -= dt;
            if (s.Delay <= 0) Land(world, s);
            else waiting.Add(s);
        }
        world.Strikes = waiting;
    }
}
