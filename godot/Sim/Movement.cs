// Steering, piloting, separation and orders. Port of src/sim/movement.ts, plus rules of this
// version only: ships ordered together move as one fleet (MoveGroup), launched craft escort
// their carrier and go for what it fights, and ships keep out of reach of what they outrange,
// get out of the way of blows coming down, and fall back to be repaired.
using SandboxShip.Sim.Data;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim;

/// <summary>What steering needs of a ship: top speed, acceleration, turn rate and size.</summary>
public sealed class Motion
{
    public double Speed, Accel, TurnRate;
    public SizeClass Size;
}

public static class Movement
{
    /// <summary>Idle ships chase targets only this far from their guard position.</summary>
    const double Leash = 700;

    static readonly Dictionary<ShipClass, Motion> Base = Ships.All.ToDictionary(c => c, c =>
    {
        var d = Ships.Defs[c];
        return new Motion { Speed = d.Speed, Accel = d.Accel, TurnRate = d.TurnRate, Size = d.Size };
    });

    /// <summary>
    /// One object reused ship after ship, read at once: no garbage every step for every ship.
    /// One per thread: worlds stepped side by side (tests in parallel, the warm-up while the
    /// game loads) must not write into each other's.
    /// </summary>
    [ThreadStatic] static Motion? scratch, paced;
    static Motion Scratch => scratch ??= new();
    static Motion Paced => paced ??= new();

    /// <summary>A fleet member this much further from its place than the leader is a straggler: it catches up at full speed.</summary>
    const double Straggler = 150;
    /// <summary>Craft this close to their place fly alongside their carrier instead of steering to it.</summary>
    const double InFormation = 40;

    /// <summary>Returns the angle in (-PI, PI].</summary>
    public static double WrapAngle(double a)
    {
        a = (a + Math.PI) % (Math.PI * 2);
        if (a <= 0) a += Math.PI * 2;
        return a - Math.PI;
    }

    static double TurnTowards(Entity e, Motion def, double angle, double dt)
    {
        var diff = WrapAngle(angle - e.Angle);
        var step = def.TurnRate * dt;
        e.Angle = WrapAngle(e.Angle + Math.Max(-step, Math.Min(step, diff)));
        return diff;
    }

    /// <summary>
    /// Turns and throttles a ship towards a point, braking so that it stops at the point.
    /// Returns true once the ship is within `arrive` of the point.
    /// </summary>
    public static bool SteerTo(World world, Entity e, Motion def, double x, double y, double dt, double arrive)
    {
        var d = Hypot(x - e.X, y - e.Y);
        if (d <= arrive)
        {
            Brake(e, def, dt);
            return true;
        }
        // Around the terrain in the way, if any; the way left is still the one to the point.
        var way = world.Terrain.Count > 0 ? Terrains.Around(world, e, x, y) : new Point(x, y);
        var diff = TurnTowards(e, def, Math.Atan2(way.Y - e.Y, way.X - e.X), dt);
        var align = Math.Cos(diff);
        // Fastest speed from which the ship can still stop over the remaining distance.
        var target = Math.Min(def.Speed, Math.Sqrt(2 * def.Accel * (d - arrive)));
        target *= align < 0 ? 0.25 : 0.25 + 0.75 * align;
        var dv = def.Accel * dt;
        e.Speed += Math.Max(-dv, Math.Min(dv, target - e.Speed));
        return false;
    }

    public static void Brake(Entity e, Motion def, double dt) => e.Speed = Math.Max(0, e.Speed - def.Accel * dt);

    /// <summary>
    /// Room kept outside the reach of a target with a shorter reach (kiting, not in the web version),
    /// and the least room between its reach and ours for keeping away to be worth it.
    /// </summary>
    const double KiteMargin = 25, KiteRoom = 20;

    static void Engage(World world, Entity e, Motion def, Entity t, double dt)
    {
        if (def.Size == SizeClass.Small)
        {
            // Small craft strafe: they keep circling the target instead of parking next to it.
            var orbit = t.Radius + e.Range * 0.6;
            var a = world.Time * (def.Speed * 0.8 / orbit) + e.Id;
            SteerTo(world, e, def, t.X + Math.Cos(a) * orbit, t.Y + Math.Sin(a) * orbit, dt, 0);
            return;
        }
        // A ship that outranges its target and can outrun it keeps out of its reach while still
        // in its own: it backs off to between the two. A part threatens with its ship's reach.
        var foe = AbilitySystem.OwnerOf(world, t);
        var dist = Dist(e, t);
        var safe = foe.Range + e.Radius + KiteMargin;
        var hit = e.Range * 0.9 + t.Radius;
        var kites = foe.Range > 0 && hit > safe + KiteRoom && (foe.Kind != EntityKind.Ship || def.Speed >= CruiseSpeed(world, foe) * 0.8);
        // Otherwise it closes in until most of its guns reach, not only the longest (Combat.StopRange);
        // no further than it keeps away from what it outranges.
        var stop = Combat.StopRange(e, t);
        if (kites) stop = Math.Max(stop, safe + KiteRoom - t.Radius);
        // A carrier fights with its craft, not its flak: it keeps back while they fight for it.
        if (e.Cls == ShipClass.Carrier) stop = Math.Max(stop, CarrierStandoff);
        if (kites && dist < safe && dist > 0)
        {
            var away = (safe + hit) / 2;
            SteerTo(world, e, def, t.X + (e.X - t.X) / dist * away, t.Y + (e.Y - t.Y) / dist * away, dt, 0);
        }
        else if (Gap(t, e.X, e.Y) > stop)
        {
            SteerTo(world, e, def, t.X, t.Y, dt, 0);
        }
        else
        {
            Brake(e, def, dt);
            TurnTowards(e, def, Math.Atan2(t.Y - e.Y, t.X - e.X), dt);
        }
    }

    /// <summary>
    /// How far a carrier keeps from what it fights, its craft going on (not in the web version).
    /// One that closed in to its flak's reach, as every class does to its guns', lost 63 games,
    /// won 24 and drew 9 of 96 against one that kept back (2026-11-20).
    /// </summary>
    const double CarrierStandoff = 550;

    static double EngineShare(Entity e) => e.Engines is null || e.MaxEngines == 0 ? 1 : Math.Max(0.15, (double)e.Engines.Value / e.MaxEngines);

    /// <summary>Speed multiplier that lasts: race trait, engine research, hero.</summary>
    static double LastingBoost(World world, Entity e) =>
        Factions.Trait(e.Race).Speed * (1 + Techs.EnginesBonus * ResearchLevel(world, e.Side, TechId.Engines))
        * (e.Hero is HeroId h ? Heroes.Defs[h].Speed : 1);

    /// <summary>Top speed of a ship with no ability running: what a fleet's pace is made of.</summary>
    public static double CruiseSpeed(World world, Entity e) => Base[e.Cls].Speed * EngineShare(e) * LastingBoost(world, e);

    /// <summary>
    /// Lost engines slow a ship down and make it turn slower; the race trait, engine
    /// research, the afterburner and an admiral's command change its top speed. A paralysed
    /// ship only brakes.
    /// </summary>
    static Motion EffectiveDef(World world, Entity e)
    {
        var def = Base[e.Cls];
        var engines = EngineShare(e);
        var boost = LastingBoost(world, e) * (AbilitySystem.IsActive(world, e, AbilityId.Sprint) ? 2 : 1)
            * (AbilitySystem.InCommandAura(world, e) ? 1.1 : 1);
        var stunned = e.Effects.Stunned > 0;
        // A gravity well holds it back (Terrain.cs).
        var held = world.Terrain.Count > 0 ? Terrains.SpeedShare(world, e) : 1;
        if (engines == 1 && boost == 1 && held == 1 && !stunned) return def;
        Scratch.Speed = stunned ? 0 : def.Speed * engines * boost * held;
        Scratch.Accel = def.Accel * engines * boost;
        Scratch.TurnRate = stunned ? 0 : def.TurnRate * (0.5 + 0.5 * engines);
        Scratch.Size = def.Size;
        return Scratch;
    }

    /// <summary>
    /// How far a fleet member still has to go: along the fleet's heading for a move, to the
    /// edge of its reach for an attack.
    /// </summary>
    static double Remaining(World world, Entity e, Order o) => o.Type switch
    {
        OrderType.Attack => world.Entities.Get(o.TargetId) is { } t ? Gap(t, e.X, e.Y) - Math.Max(e.Range * 1.6, 350) : 0,
        // A patrol's legs turn: what is left of this one, straight.
        OrderType.Patrol => Hypot(o.X - e.X, o.Y - e.Y),
        _ => (o.X - e.X) * o.Group!.DirX + (o.Y - e.Y) * o.Group.DirY,
    };

    /// <summary>
    /// A fleet member's motion: held to the fleet's pace (twice that under the afterburner),
    /// unless it lags behind the leader and must catch up.
    /// </summary>
    static Motion Pace(World world, Entity e, Motion def, Order o)
    {
        if (o.Group is not MoveGroup g) return def;
        if (g.At != world.Time)
        {
            g.At = world.Time;
            g.Lead = double.PositiveInfinity;
            foreach (var m in world.Entities)
            {
                if (m.Kind == EntityKind.Ship && m.Order?.Group == g) g.Lead = Math.Min(g.Lead, Remaining(world, m, m.Order));
            }
        }
        var left = Remaining(world, e, o);
        if (left <= 0 || left > g.Lead + Straggler) return def;
        var pace = g.Pace * (AbilitySystem.IsActive(world, e, AbilityId.Sprint) ? 2 : 1);
        if (pace >= def.Speed) return def;
        Paced.Speed = pace;
        Paced.Accel = def.Accel;
        Paced.TurnRate = def.TurnRate;
        Paced.Size = def.Size;
        return Paced;
    }

    /// <summary>Hand flying: the pilot sets thrust and turn; reverse is slower than full ahead.</summary>
    static void Fly(Entity e, Motion def, double dt)
    {
        var p = e.Pilot!;
        e.Angle = WrapAngle(e.Angle + p.Turn * def.TurnRate * dt);
        var target = p.Thrust >= 0 ? def.Speed * p.Thrust : def.Speed * 0.35 * p.Thrust;
        var dv = def.Accel * dt;
        e.Speed += Math.Max(-dv, Math.Min(dv, target - e.Speed));
    }

    static Entity? HostileTarget(World world, Entity e)
    {
        var t = world.Entities.Get(e.TargetId);
        return t is not null && Foes(world, e.Side, t.Side) ? t : null;
    }

    /// <summary>Room a ship leaves between itself and a blow coming down when it gets out of the way.</summary>
    const double DodgeRoom = 40;

    /// <summary>
    /// Where to go to get out of a blow coming down on the ship (an orbital strike, a star lance,
    /// a spinal beam charging up), not in the web version: straight out of a blast, sideways out of
    /// a beam. Null when none threatens it; blows of its side and its allies spare it.
    /// </summary>
    static Point? Dodge(World world, Entity e)
    {
        foreach (var s in world.Strikes)
        {
            if (s.Delay <= 0 || Allies(world, s.Side, e.Side)) continue;
            var clear = s.Radius + e.Radius + DodgeRoom;
            if (s.Kind == StrikeKind.Beam)
            {
                // The nearest point of the beam, then away from it, square to it.
                var (bx, by) = (s.X2 - s.X, s.Y2 - s.Y);
                var len2 = bx * bx + by * by;
                var k = len2 > 0 ? Math.Clamp(((e.X - s.X) * bx + (e.Y - s.Y) * by) / len2, 0, 1) : 0;
                var (nx, ny) = (s.X + bx * k, s.Y + by * k);
                var d = Hypot(e.X - nx, e.Y - ny);
                if (d >= clear) continue;
                var (ux, uy) = d > 0 ? ((e.X - nx) / d, (e.Y - ny) / d) : (-by / Math.Sqrt(len2), bx / Math.Sqrt(len2));
                return new Point(nx + ux * (clear + DodgeRoom), ny + uy * (clear + DodgeRoom));
            }
            var r = Hypot(e.X - s.X, e.Y - s.Y);
            if (r >= clear) continue;
            var (dx, dy) = r > 0 ? ((e.X - s.X) / r, (e.Y - s.Y) / r) : (Math.Cos(e.Angle), Math.Sin(e.Angle));
            return new Point(s.X + dx * (clear + DodgeRoom), s.Y + dy * (clear + DodgeRoom));
        }
        return null;
    }

    /// <summary>Below this share of its hull a ship falls back to be repaired (Mend); it comes back at Mended.</summary>
    const double Wounded = 0.3, Mended = 0.8;

    /// <summary>
    /// What may repair a ship, the nearest first: a support ship or a repair dock of its side or
    /// an ally's; for a race whose hulls mend by themselves, its station, out of the fight.
    /// </summary>
    static Entity? Mender(World world, Entity e)
    {
        Entity? best = null;
        var bestD = double.PositiveInfinity;
        var heals = Factions.Trait(e.Race).HullRegen > 0;
        // A support ship nearer what it flees than itself is no refuge: it would fly to the front.
        var foe = HostileTarget(world, e);
        foreach (var t in world.Entities)
        {
            if (t == e || t.Build < 1 || !Allies(world, t.Side, e.Side)) continue;
            var mends = t.Kind == EntityKind.Ship ? Ships.Defs[t.Cls].Repair is not null && !t.Mending && (foe is null || Dist(t, foe) >= Dist(e, foe))
                : t.Type == "repairDock" || (heals && t.Type == "station" && t.Side == e.Side);
            if (!mends) continue;
            var d = Dist(e, t);
            if (d < bestD)
            {
                bestD = d;
                best = t;
            }
        }
        return best;
    }

    /// <summary>How close to what repairs it a ship waits: well within a support's or a dock's reach, beside a station.</summary>
    static double MendingRange(Entity mender) => mender.Kind == EntityKind.Ship ? Ships.Defs[mender.Cls].Repair!.Range * 0.6
        : Ids.IsBuildable(mender.Type, out var b) && Buildables.Defs[b].Repair is { } r ? r.Range * 0.6 : mender.Radius + 150;

    /// <summary>
    /// A badly damaged ship with no order of its player's (or, on a human side, out on an attack
    /// move) falls back by itself to what can repair it, and returns once mended: to its guard
    /// post, or to its attack move. Not in the web version. An AI's wave answers to the AI's own
    /// call (Ai.RetreatDamaged); craft from hangars and summoned ones never fall back. Returns
    /// whether the ship is falling back this step.
    /// </summary>
    static bool Mend(World world, Entity e, Motion def, double dt)
    {
        var o = e.Order;
        var free = o is null || (o.Type is OrderType.AttackMove or OrderType.Patrol or OrderType.Guard && world.Factions.GetValueOrDefault(e.Side)?.AI is null);
        if (!free || e.Launched || e.Expires is not null || e.Retreating || (e.Mending && e.Hull >= e.MaxHull * Mended))
        {
            e.Mending = false;
            return false;
        }
        if (!e.Mending && e.Hull >= e.MaxHull * Wounded) return false;
        var mender = world.Entities.Get(e.MenderId);
        if (mender is null || mender.Mending)
        {
            // Looking costs a walk over every entity: twice a second at most.
            if (world.Time < e.MendLook)
            {
                e.Mending = false;
                return false;
            }
            e.MendLook = world.Time + 0.5;
            mender = Mender(world, e);
            e.MenderId = mender?.Id;
        }
        e.Mending = mender is not null;
        if (mender is null) return false;
        if (Dist(e, mender) > MendingRange(mender)) SteerTo(world, e, def, mender.X, mender.Y, dt, 0);
        else Brake(e, def, dt);
        return true;
    }

    /// <summary>How far around it a support ship in a fight looks for whom to stay by.</summary>
    const double AttendReach = 800;

    /// <summary>
    /// A support ship in a fight does not close in on the foe, its guns are not what it is for: it
    /// stays by the ally that most needs it, or else the nearest, within reach of its repairs.
    /// Returns false with no ally around: it fights as any ship does.
    /// </summary>
    static bool Attend(World world, Entity e, Motion def, double dt)
    {
        var ward = world.Entities.Get(e.WardId);
        // Looking walks everything around, hundreds of ships in a big fight: twice a second at most,
        // as for a mender (Mend), the ward kept meanwhile while it is there to keep.
        if (world.Time >= e.WardLook || ward is null || ward.Launched || Dist(e, ward) > AttendReach)
        {
            e.WardLook = world.Time + 0.5;
            ward = Ward(world, e);
            e.WardId = ward?.Id;
        }
        if (ward is null) return false;
        if (Gap(ward, e.X, e.Y) > Ships.Defs[e.Cls].Repair!.Range * 0.7) SteerTo(world, e, def, ward.X, ward.Y, dt, 0);
        else Brake(e, def, dt);
        return true;
    }

    /// <summary>The ally a support ship stays by: the most damaged around, or else the nearest; none of them a repairer itself.</summary>
    static Entity? Ward(World world, Entity e)
    {
        Entity? ward = null;
        var worst = double.PositiveInfinity;
        foreach (var t in world.Grid.Near(e.X, e.Y, AttendReach))
        {
            if (t == e || t.Kind != EntityKind.Ship || t.Launched || !Allies(world, t.Side, e.Side) || Ships.Defs[t.Cls].Repair is not null) continue;
            var d = Dist(e, t);
            if (d > AttendReach) continue;
            // The most damaged first; among those untouched, the nearest.
            var need = t.Hull < t.MaxHull ? t.Hull / t.MaxHull : 1 + d;
            if (need < worst)
            {
                worst = need;
                ward = t;
            }
        }
        return ward;
    }

    /// <summary>Fights this foe: as it may, a support ship by staying with those it repairs.</summary>
    static void Fight(World world, Entity e, Motion def, Entity t, double dt)
    {
        if (Ships.Defs[e.Cls].Repair is null || !Attend(world, e, def, dt)) Engage(world, e, def, t, dt);
    }

    static void UpdateShip(World world, Entity e, double dt)
    {
        var def = EffectiveDef(world, e);
        if (e.Pilot is not null)
        {
            Fly(e, def, dt);
            return;
        }
        var o = e.Order;
        var arrive = Math.Max(6, e.Radius * 0.3) + (o?.Slack ?? 0);
        // Holding the line: no stepping aside, no falling back, no chase.
        var hold = e.Stance == Stance.Hold;
        // Out of a blow coming down first, unless the player said where to go.
        if (o?.Type != OrderType.Move && !hold && Dodge(world, e) is { } way)
        {
            SteerTo(world, e, def, way.X, way.Y, dt, 0);
            return;
        }
        if (!hold && Mend(world, e, def, dt)) return;

        if (o?.Type == OrderType.Attack)
        {
            // A target lost in the fog is lost.
            var t = world.Entities.Get(o.TargetId);
            if (t is not null && Vision.IsVisible(world, e.Side, t))
            {
                Engage(world, e, Pace(world, e, def, o), t, dt);
                return;
            }
            if (Done(e, e.X, e.Y)) return;
        }
        else if (o?.Type == OrderType.Guard)
        {
            var ward = world.Entities.Get(o.TargetId);
            if (ward is not null && ward != e && Allies(world, e.Side, ward.Side))
            {
                Escort(world, e, def, ward, o.X, o.Y, dt);
                return;
            }
            if (Done(e, e.X, e.Y)) return;
        }
        else if (o is not null)
        {
            // Holding, it shoots what it passes within reach but keeps going.
            var t = o.Type != OrderType.Move && !hold ? HostileTarget(world, e) : null;
            if (t is not null)
            {
                Fight(world, e, def, t, dt);
                return;
            }
            if (SteerTo(world, e, Pace(world, e, def, o), o.X, o.Y, dt, arrive))
            {
                if (o.Route is { } route)
                {
                    var leg = (o.Leg + 1) % route.Count;
                    e.Order = o with { Leg = leg, X = route[leg].X, Y = route[leg].Y };
                }
                // Arrived short of its spot in a crowd, it holds where it is.
                else if (o.Slack > 0) Done(e, e.X, e.Y);
                else Done(e, o.X, o.Y);
            }
            return;
        }

        if (CarrierOf(world, e) is { } carrier)
        {
            Escort(world, e, def, carrier, e.SlotX, e.SlotY, dt);
            return;
        }
        var hostile = hold ? null : HostileTarget(world, e);
        var aggressive = e.Stance == Stance.Aggressive;
        if (hostile is not null && (aggressive || Dist(hostile, new Point(e.GuardX, e.GuardY)) < Leash))
        {
            Fight(world, e, def, hostile, dt);
            // It stays where the fight took it.
            if (aggressive)
            {
                e.GuardX = e.X;
                e.GuardY = e.Y;
            }
        }
        else if (Hypot(e.GuardX - e.X, e.GuardY - e.Y) > 30) SteerTo(world, e, def, e.GuardX, e.GuardY, dt, arrive);
        else Brake(e, def, dt);
    }

    /// <summary>Most orders a ship keeps waiting after its own: past them, more are dropped.</summary>
    public const int MaxQueue = 32;

    /// <summary>
    /// The order is done: the next one given with Shift starts, else the ship holds at (x, y).
    /// Returns whether another order started.
    /// </summary>
    static bool Done(Entity e, double x, double y)
    {
        if (e.Queue is { Count: > 0 } queue)
        {
            e.Order = queue[0];
            queue.RemoveAt(0);
            return true;
        }
        e.Order = null;
        e.GuardX = x;
        e.GuardY = y;
        return false;
    }

    /// <summary>The ship or structure a launched craft flies for, while it stands.</summary>
    public static Entity? CarrierOf(World world, Entity e) => e.CarrierId is int id ? world.Entities.Get(id) : null;

    /// <summary>What a ship or structure fights: the foe it was ordered to attack, else the one it picked; seen ones only.</summary>
    public static Entity? FocusOf(World world, Entity owner)
    {
        var t = world.Entities.Get(owner.Order?.Type == OrderType.Attack ? owner.Order.TargetId : owner.TargetId);
        return t is not null && Foes(world, owner.Side, t.Side) && Vision.IsVisible(world, owner.Side, t) ? t : null;
    }

    /// <summary>Gives a launched craft its carrier, and its place at the world point (x, y) for now.</summary>
    public static void Assign(Entity craft, Entity carrier, double x, double y)
    {
        var c = Math.Cos(carrier.Angle);
        var s = Math.Sin(carrier.Angle);
        var dx = x - carrier.X;
        var dy = y - carrier.Y;
        craft.CarrierId = carrier.Id;
        craft.SlotX = dx * c + dy * s;
        craft.SlotY = -dx * s + dy * c;
        craft.GuardX = x;
        craft.GuardY = y;
    }

    /// <summary>
    /// Craft go for what their carrier fights, else for enemies near it; otherwise they keep
    /// their place beside it, (slotX, slotY) in its frame, flying along when it moves. So do
    /// ships guarding another, but for those holding or passive, who keep their place.
    /// </summary>
    static void Escort(World world, Entity e, Motion def, Entity carrier, double slotX, double slotY, double dt)
    {
        var c = Math.Cos(carrier.Angle);
        var s = Math.Sin(carrier.Angle);
        var sx = carrier.X + slotX * c - slotY * s;
        var sy = carrier.Y + slotX * s + slotY * c;
        // Should the carrier fall, they hold where they last were with it.
        e.GuardX = sx;
        e.GuardY = sy;
        // A taunted craft goes for the bulwark; otherwise for what its carrier fights, or what comes near it.
        var t = e.Effects.Taunted > 0 ? HostileTarget(world, e) : null;
        if (e.Stance is not (Stance.Hold or Stance.Passive))
        {
            t ??= FocusOf(world, carrier);
            if (t is null && HostileTarget(world, e) is { } hostile && Dist(hostile, carrier) < Leash) t = hostile;
        }
        if (t is not null)
        {
            Engage(world, e, def, t, dt);
            return;
        }
        if (Hypot(sx - e.X, sy - e.Y) > InFormation)
        {
            SteerTo(world, e, def, sx, sy, dt, 0);
            return;
        }
        TurnTowards(e, def, carrier.Angle, dt);
        var dv = def.Accel * dt;
        e.Speed += Math.Max(-dv, Math.Min(dv, Math.Min(def.Speed, carrier.Speed) - e.Speed));
    }

    public static void UpdateShips(World world, double dt)
    {
        foreach (var e in world.Entities)
        {
            if (e.Kind != EntityKind.Ship) continue;
            UpdateShip(world, e, dt);
            e.X += Math.Cos(e.Angle) * e.Speed * dt;
            e.Y += Math.Sin(e.Angle) * e.Speed * dt;
        }
    }

    /// <summary>Ships thrown aside (Entity.KnockX/Y) drift with it, and it dies out within a second or so.</summary>
    public static void Drift(World world, double dt)
    {
        var fade = Math.Exp(-4 * dt);
        foreach (var e in world.Entities)
        {
            if (e.KnockX == 0 && e.KnockY == 0) continue;
            e.X += e.KnockX * dt;
            e.Y += e.KnockY * dt;
            (e.KnockX, e.KnockY) = (e.KnockX * fade, e.KnockY * fade);
            if (e.KnockX * e.KnockX + e.KnockY * e.KnockY < 4) (e.KnockX, e.KnockY) = (0, 0);
        }
    }

    /// <summary>
    /// Pushes overlapping ships apart and out of structures; heavier ships move less. Small craft
    /// fly above the others (the view gives them their height): they overlap any ship and are
    /// only pushed out of structures.
    /// </summary>
    public static void Separate(World world)
    {
        world.Grid.Rebuild(world.Entities);
        // Parts push nothing and small craft push no one: their own grid leaves them out of the
        // queries. It reaches as far as the main grid, so the same cells are visited in the same
        // order, and what pushes comes in the same order.
        var heavies = world.Heavies;
        heavies.Clear();
        foreach (var e in world.Entities)
            if (e.Kind == EntityKind.Structure || (e.Kind == EntityKind.Ship && e.Size != SizeClass.Small)) heavies.Add(e);
        heavies.MaxRadius = world.Grid.MaxRadius;
        foreach (var a in world.Entities)
        {
            if (a.Kind != EntityKind.Ship) continue;
            foreach (var b in heavies.Near(a.X, a.Y, a.Radius))
            {
                if (b.Kind == EntityKind.Structure)
                {
                    PushOut(a, b);
                    continue;
                }
                // Each pair once.
                if (a.Size == SizeClass.Small || b.Id <= a.Id) continue;
                var min = (a.Radius + b.Radius) * 0.9;
                var dx = b.X - a.X;
                var dy = b.Y - a.Y;
                var longHull = a.Beam > 0 || b.Beam > 0;
                var bound = longHull ? a.Radius + b.Radius : min;
                if (Math.Abs(dx) >= bound || Math.Abs(dy) >= bound) continue;
                if (longHull)
                {
                    // A long hull: apart from the nearest points of the two keels, by their girths.
                    var (ax, ay) = Worlds.Keel(a, b.X, b.Y);
                    var (bx, by) = Worlds.Keel(b, ax, ay);
                    (ax, ay) = Worlds.Keel(a, bx, by);
                    (dx, dy) = (bx - ax, by - ay);
                    min = (a.Girth + b.Girth) * 0.9;
                }
                var d = Hypot(dx, dy);
                if (d >= min) continue;
                if (d == 0)
                {
                    dx = Math.Cos(a.Id);
                    dy = Math.Sin(a.Id);
                    d = 1;
                }
                var push = (min - d) / d;
                var ma = a.Radius * a.Radius;
                var mb = b.Radius * b.Radius;
                var wa = mb / (ma + mb);
                a.X -= dx * push * wa;
                a.Y -= dy * push * wa;
                b.X += dx * push * (1 - wa);
                b.Y += dy * push * (1 - wa);
            }
            if (world.Terrain.Count > 0) Terrains.PushOut(world, a);
            a.X = Math.Max(0, Math.Min(world.Width, a.X));
            a.Y = Math.Max(0, Math.Min(world.Height, a.Y));
        }
    }

    static void PushOut(Entity a, Entity s)
    {
        if (a.Beam > 0)
        {
            // A long hull: its keel's nearest point kept its beam off the structure.
            var (kx, ky) = Worlds.Keel(a, s.X, s.Y);
            var (ox, oy) = (kx - s.X, ky - s.Y);
            var (gap, room) = (Hypot(ox, oy), a.Beam + s.Radius);
            if (gap >= room || gap == 0) return;
            a.X += ox / gap * room - ox;
            a.Y += oy / gap * room - oy;
            return;
        }
        var dx = a.X - s.X;
        var dy = a.Y - s.Y;
        var min = a.Radius + s.Radius;
        var d = Hypot(dx, dy);
        if (d >= min || d == 0) return;
        a.X = s.X + dx / d * min;
        a.Y = s.Y + dy / d * min;
    }

    /// <summary>Grid of `n` offsets centered on (0, 0).</summary>
    public static List<Point> FormationOffsets(int n, double spacing)
    {
        var cols = (int)Math.Ceiling(Math.Sqrt(n));
        var rows = (int)Math.Ceiling((double)n / cols);
        var out_ = new List<Point>();
        for (var i = 0; i < n; i++)
        {
            var c = i % cols;
            var r = i / cols;
            out_.Add(new Point((c - (cols - 1) / 2.0) * spacing, (r - (rows - 1) / 2.0) * spacing));
        }
        return out_;
    }

    static List<Entity> OwnShips(World world, IEnumerable<int> ids)
    {
        var out_ = new List<Entity>();
        foreach (var id in ids)
        {
            var e = world.Entities.Get(id);
            if (e?.Kind == EntityKind.Ship) out_.Add(e);
        }
        return out_;
    }

    /// <summary>A fleet for ships ordered together: none for a lone ship.</summary>
    static MoveGroup? GroupOf(World world, List<Entity> ships, double dirX, double dirY) =>
        ships.Count < 2 ? null : new MoveGroup(ships.Min(s => CruiseSpeed(world, s)), dirX, dirY);

    /// <summary>Small craft and support ships keep behind the warships.</summary>
    static bool Rear(Entity s) => s.Size == SizeClass.Small || Ships.Defs[s.Cls].Repair is not null;

    static double Toughness(Entity s) => s.MaxHull + s.MaxShield;

    /// <summary>
    /// Places of a fleet formation as (forward, lateral) offsets: the `front` ships' rows first,
    /// then the `back` ones' rows behind them; each row fills from its middle outwards.
    /// </summary>
    public static List<(double Forward, double Lateral)> FleetSlots(int front, int back, double spacing)
    {
        var rows = new List<int>();
        void Rows(int n)
        {
            var cols = (int)Math.Ceiling(Math.Sqrt(n));
            for (var left = n; left > 0; left -= cols) rows.Add(Math.Min(cols, left));
        }
        Rows(front);
        Rows(back);
        var out_ = new List<(double, double)>();
        for (var k = 0; k < rows.Count; k++)
        {
            var forward = ((rows.Count - 1) / 2.0 - k) * spacing;
            var m = rows[k];
            foreach (var j in Enumerable.Range(0, m).OrderBy(j => Math.Abs(j - (m - 1) / 2.0)).ThenBy(j => j))
                out_.Add((forward, (j - (m - 1) / 2.0) * spacing));
        }
        return out_;
    }

    /// <summary>
    /// Sends ships to a point in formation, facing the way they go: warships in the front rows,
    /// toughest in the middle, small craft and support ships behind them, all at the pace of
    /// the slowest. `queued` (Shift): once what they were told before is done.
    /// </summary>
    public static void IssueMove(World world, IEnumerable<int> ids, double x, double y, bool attackMove = false, bool queued = false)
    {
        var ships = OwnShips(world, ids);
        if (ships.Count == 0) return;
        var (dx, dy, slots) = Formation(world, ships, x, y, queued);
        var group = GroupOf(world, ships, dx, dy);
        var structures = Structures(world);
        for (var i = 0; i < ships.Count; i++)
        {
            var (f, l) = slots[i];
            var p = Reachable(world, structures, ships[i], x + f * dx - l * dy, y + f * dy + l * dx);
            Give(ships[i], attackMove ? Order.AttackMove(p.X, p.Y, group) : Order.Move(p.X, p.Y, group), queued);
        }
    }

    /// <summary>
    /// The fleet's heading to (x, y), and the places in it, (forward, lateral), of `ships`, sorted
    /// in place to fill them: from where they stand, or with `queued` where their orders leave them.
    /// </summary>
    static (double Dx, double Dy, List<(double Forward, double Lateral)> Slots) Formation(World world, List<Entity> ships, double x, double y, bool queued)
    {
        var spacing = ships.Max(s => s.Radius) * 2.4;
        var (cx, cy) = Centre(world, ships, queued);
        var heading = Hypot(x - cx, y - cy) > 1
            ? Math.Atan2(y - cy, x - cx)
            : Math.Atan2(ships.Sum(s => Math.Sin(s.Angle)), ships.Sum(s => Math.Cos(s.Angle)));
        // Stable sorts: equals keep the order they were selected in.
        var front = ships.Where(s => !Rear(s)).OrderByDescending(Toughness).ToList();
        var back = ships.Where(Rear).OrderByDescending(Toughness).ToList();
        ships.Clear();
        ships.AddRange(front);
        ships.AddRange(back);
        return (Math.Cos(heading), Math.Sin(heading), FleetSlots(front.Count, back.Count, spacing));
    }

    /// <summary>The middle of a fleet: of where its ships stand, or with `queued` of where their orders leave them.</summary>
    static (double X, double Y) Centre(World world, List<Entity> ships, bool queued) =>
        (ships.Average(s => queued ? PlanEnd(world, s).X : s.X), ships.Average(s => queued ? PlanEnd(world, s).Y : s.Y));

    /// <summary>The structures, picked out once for a whole fleet rather than once per ship.</summary>
    static List<Entity> Structures(World world)
    {
        var structures = new List<Entity>();
        foreach (var e in world.Entities) if (e.Kind == EntityKind.Structure) structures.Add(e);
        return structures;
    }

    /// <summary>Where a ship will be once its orders are done, as far as can be told now.</summary>
    static Point PlanEnd(World world, Entity s)
    {
        var last = s.Queue is { Count: > 0 } q ? q[^1] : s.Order;
        return last?.Type switch
        {
            OrderType.Move or OrderType.AttackMove or OrderType.Patrol => new Point(last.X, last.Y),
            OrderType.Attack or OrderType.Guard when world.Entities.Get(last.TargetId) is { } t => new Point(t.X, t.Y),
            _ => new Point(s.X, s.Y),
        };
    }

    /// <summary>Gives a ship an order now, or with `queued` once the ones it has are done; a full queue takes no more.</summary>
    static void Give(Entity s, Order o, bool queued)
    {
        if (queued && s.Order is not null)
        {
            s.Queue ??= [];
            if (s.Queue.Count < MaxQueue) s.Queue.Add(o);
            return;
        }
        s.Order = o;
        s.Queue?.Clear();
    }

    /// <summary>Most points of a patrol, where it starts included.</summary>
    public const int MaxRoute = 16;

    /// <summary>
    /// Sends ships round a patrol for good, in formation, fighting what they meet: from where they
    /// are (queued: where their orders leave them) through each point and back.
    /// </summary>
    public static void IssuePatrol(World world, IEnumerable<int> ids, IReadOnlyList<Point> points, bool queued = false)
    {
        var ships = OwnShips(world, ids);
        if (ships.Count == 0 || points.Count == 0) return;
        var (sx, sy) = Centre(world, ships, queued);
        var (dx, dy, slots) = Formation(world, ships, points[0].X, points[0].Y, queued);
        var group = GroupOf(world, ships, dx, dy);
        var structures = Structures(world);
        var count = Math.Min(points.Count, MaxRoute - 1) + 1;
        for (var i = 0; i < ships.Count; i++)
        {
            // Each keeps its place in the fleet the whole way round.
            var (f, l) = slots[i];
            var (ox, oy) = (f * dx - l * dy, f * dy + l * dx);
            var route = new Point[count];
            for (var k = 0; k < count; k++)
            {
                var p = k == 0 ? new Point(sx, sy) : points[k - 1];
                route[k] = Reachable(world, structures, ships[i], p.X + ox, p.Y + oy);
            }
            Give(ships[i], Order.Patrol(route, 1, group), queued);
        }
    }

    /// <summary>
    /// Sets ships to guard one of their side's or an ally's: around it, they go for what it fights
    /// or what comes near it, and follow it where it goes. A ship never guards itself.
    /// </summary>
    public static void IssueGuard(World world, IEnumerable<int> ids, int wardId, bool queued = false)
    {
        if (world.Entities.Get(wardId) is not { } part) return;
        // A part is guarded through what it belongs to.
        var ward = AbilitySystem.OwnerOf(world, part);
        // Nor does it guard the ship guarding it: the two would chase each other's places.
        var ships = OwnShips(world, ids)
            .Where(s => s != ward && Allies(world, s.Side, ward.Side) && !(ward.Order is { Type: OrderType.Guard } wo && wo.TargetId == s.Id)).ToList();
        // Spread behind it, in an arc that closes into a ring for many.
        var spread = ships.Count > 0 ? Math.Min(Math.PI * 2 / ships.Count, 0.6) : 0;
        for (var i = 0; i < ships.Count; i++)
        {
            var a = Math.PI + (i - (ships.Count - 1) / 2.0) * spread;
            var r = ward.Radius + ships[i].Radius * 2 + 30;
            Give(ships[i], Order.Guard(ward.Id, Math.Cos(a) * r, Math.Sin(a) * r), queued);
        }
    }

    /// <summary>How ships fight between orders.</summary>
    public static void SetStance(World world, IEnumerable<int> ids, Stance stance)
    {
        foreach (var s in OwnShips(world, ids)) s.Stance = stance;
    }

    /// <summary>
    /// A new ship's way to the rally point: a spot it can reach, clear of what stands there, and it
    /// need not reach it exactly: where ships gather, another one may well sit on it.
    /// </summary>
    public static Order RallyOrder(World world, Entity s, double x, double y)
    {
        var p = Reachable(world, Structures(world), s, x, y);
        return Order.Move(p.X, p.Y) with { Slack = s.Radius * 2 + 40 };
    }

    /// <summary>
    /// Clamps a destination to the map and moves it out of structures: ships are pushed
    /// out of them, so a point inside one could never be reached.
    /// </summary>
    static Point Reachable(World world, List<Entity> structures, Entity s, double x, double y)
    {
        var px = Math.Max(0, Math.Min(world.Width, x));
        var py = Math.Max(0, Math.Min(world.Height, y));
        foreach (var st in structures)
        {
            var min = st.Radius + s.Radius + 4;
            var dx = px - st.X;
            var dy = py - st.Y;
            var d = Hypot(dx, dy);
            if (d >= min) continue;
            if (d == 0)
            {
                dx = s.X - st.X;
                dy = s.Y - st.Y;
                d = Hypot(dx, dy);
                if (d == 0) d = 1;
            }
            px = st.X + dx / d * min;
            py = st.Y + dy / d * min;
        }
        return world.Terrain.Count > 0 ? Terrains.OffRocks(world, px, py, s.Radius) : new Point(px, py);
    }

    /// <summary>Sends ships at a foe; together, they close in at the pace of the slowest. Allies of the target stay put.</summary>
    public static void IssueAttack(World world, IEnumerable<int> ids, int targetId, bool queued = false)
    {
        var target = world.Entities.Get(targetId);
        var ships = OwnShips(world, ids).Where(s => target is null || Foes(world, s.Side, target.Side)).ToList();
        var group = GroupOf(world, ships, 0, 0);
        foreach (var s in ships) Give(s, Order.Attack(targetId, group), queued);
    }

    public static void IssueStop(World world, IEnumerable<int> ids)
    {
        foreach (var s in OwnShips(world, ids))
        {
            s.Order = null;
            s.Queue?.Clear();
            s.GuardX = s.X;
            s.GuardY = s.Y;
        }
    }
}
