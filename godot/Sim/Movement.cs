// Steering, piloting, separation and orders. Port of src/sim/movement.ts, plus two rules of
// this version only: ships ordered together move as one fleet (MoveGroup), and launched craft
// escort their carrier and go for what it fights.
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
    public static bool SteerTo(Entity e, Motion def, double x, double y, double dt, double arrive)
    {
        var d = Hypot(x - e.X, y - e.Y);
        if (d <= arrive)
        {
            Brake(e, def, dt);
            return true;
        }
        var diff = TurnTowards(e, def, Math.Atan2(y - e.Y, x - e.X), dt);
        var align = Math.Cos(diff);
        // Fastest speed from which the ship can still stop over the remaining distance.
        var target = Math.Min(def.Speed, Math.Sqrt(2 * def.Accel * (d - arrive)));
        target *= align < 0 ? 0.25 : 0.25 + 0.75 * align;
        var dv = def.Accel * dt;
        e.Speed += Math.Max(-dv, Math.Min(dv, target - e.Speed));
        return false;
    }

    public static void Brake(Entity e, Motion def, double dt) => e.Speed = Math.Max(0, e.Speed - def.Accel * dt);

    static void Engage(World world, Entity e, Motion def, Entity t, double dt)
    {
        if (def.Size == SizeClass.Small)
        {
            // Small craft strafe: they keep circling the target instead of parking next to it.
            var orbit = t.Radius + e.Range * 0.6;
            var a = world.Time * (def.Speed * 0.8 / orbit) + e.Id;
            SteerTo(e, def, t.X + Math.Cos(a) * orbit, t.Y + Math.Sin(a) * orbit, dt, 0);
        }
        else if (Dist(e, t) - t.Radius > e.Range * 0.85)
        {
            SteerTo(e, def, t.X, t.Y, dt, 0);
        }
        else
        {
            Brake(e, def, dt);
            TurnTowards(e, def, Math.Atan2(t.Y - e.Y, t.X - e.X), dt);
        }
    }

    static double EngineShare(Entity e) => e.Engines is null || e.MaxEngines == 0 ? 1 : Math.Max(0.15, (double)e.Engines.Value / e.MaxEngines);

    /// <summary>Speed multiplier that lasts: faction trait, engine research, hero.</summary>
    static double LastingBoost(World world, Entity e) =>
        Factions.Trait(e.Faction).Speed * (1 + Techs.EnginesBonus * ResearchLevel(world, e.Faction, TechId.Engines))
        * (e.Hero is HeroId h ? Heroes.Defs[h].Speed : 1);

    /// <summary>Top speed of a ship with no ability running: what a fleet's pace is made of.</summary>
    public static double CruiseSpeed(World world, Entity e) => Base[e.Cls].Speed * EngineShare(e) * LastingBoost(world, e);

    /// <summary>
    /// Lost engines slow a ship down and make it turn slower; the faction trait, engine
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
        if (engines == 1 && boost == 1 && !stunned) return def;
        Scratch.Speed = stunned ? 0 : def.Speed * engines * boost;
        Scratch.Accel = def.Accel * engines * boost;
        Scratch.TurnRate = stunned ? 0 : def.TurnRate * (0.5 + 0.5 * engines);
        Scratch.Size = def.Size;
        return Scratch;
    }

    /// <summary>
    /// How far a fleet member still has to go: along the fleet's heading for a move, to the
    /// edge of its reach for an attack.
    /// </summary>
    static double Remaining(World world, Entity e, Order o) => o.Type == OrderType.Attack
        ? (world.Entities.Get(o.TargetId) is { } t ? Dist(e, t) - t.Radius - Math.Max(e.Range * 1.6, 350) : 0)
        : (o.X - e.X) * o.Group!.DirX + (o.Y - e.Y) * o.Group.DirY;

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
        return t is not null && t.Faction != e.Faction ? t : null;
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
        var arrive = Math.Max(6, e.Radius * 0.3);

        if (o?.Type == OrderType.Attack)
        {
            // A target lost in the fog is lost.
            var t = world.Entities.Get(o.TargetId);
            if (t is not null && Vision.IsVisible(world, e.Faction, t))
            {
                Engage(world, e, Pace(world, e, def, o), t, dt);
                return;
            }
            e.Order = null;
            e.GuardX = e.X;
            e.GuardY = e.Y;
        }
        else if (o is not null)
        {
            var t = o.Type == OrderType.AttackMove ? HostileTarget(world, e) : null;
            if (t is not null)
            {
                Engage(world, e, def, t, dt);
                return;
            }
            if (SteerTo(e, Pace(world, e, def, o), o.X, o.Y, dt, arrive))
            {
                e.Order = null;
                e.GuardX = o.X;
                e.GuardY = o.Y;
            }
            return;
        }

        if (CarrierOf(world, e) is { } carrier)
        {
            Escort(world, e, def, carrier, dt);
            return;
        }
        var hostile = HostileTarget(world, e);
        if (hostile is not null && Dist(hostile, new Point(e.GuardX, e.GuardY)) < Leash) Engage(world, e, def, hostile, dt);
        else if (Hypot(e.GuardX - e.X, e.GuardY - e.Y) > 30) SteerTo(e, def, e.GuardX, e.GuardY, dt, arrive);
        else Brake(e, def, dt);
    }

    /// <summary>The ship or structure a launched craft flies for, while it stands.</summary>
    public static Entity? CarrierOf(World world, Entity e) => e.CarrierId is int id ? world.Entities.Get(id) : null;

    /// <summary>What a ship or structure fights: the enemy it was ordered to attack, else the one it picked; seen ones only.</summary>
    public static Entity? FocusOf(World world, Entity owner)
    {
        var t = world.Entities.Get(owner.Order?.Type == OrderType.Attack ? owner.Order.TargetId : owner.TargetId);
        return t is not null && t.Faction != owner.Faction && Vision.IsVisible(world, owner.Faction, t) ? t : null;
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
    /// their place beside it, flying along when it moves.
    /// </summary>
    static void Escort(World world, Entity e, Motion def, Entity carrier, double dt)
    {
        var c = Math.Cos(carrier.Angle);
        var s = Math.Sin(carrier.Angle);
        var sx = carrier.X + e.SlotX * c - e.SlotY * s;
        var sy = carrier.Y + e.SlotX * s + e.SlotY * c;
        // Should the carrier fall, they hold where they last were with it.
        e.GuardX = sx;
        e.GuardY = sy;
        // A taunted craft goes for the bulwark; otherwise for what its carrier fights, or what comes near it.
        var t = e.Effects.Taunted > 0 ? HostileTarget(world, e) : null;
        t ??= FocusOf(world, carrier);
        if (t is null && HostileTarget(world, e) is { } hostile && Dist(hostile, carrier) < Leash) t = hostile;
        if (t is not null)
        {
            Engage(world, e, def, t, dt);
            return;
        }
        if (Hypot(sx - e.X, sy - e.Y) > InFormation)
        {
            SteerTo(e, def, sx, sy, dt, 0);
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

    /// <summary>
    /// Pushes overlapping ships apart (small craft may overlap each other) and out of
    /// structures. Heavier ships move less.
    /// </summary>
    public static void Separate(World world)
    {
        world.Grid.Rebuild(world.Entities);
        // Parts push nothing and small craft do not push each other: two grids leave them out
        // of the queries. They reach as far as the main grid, so the same cells are visited in
        // the same order, and what pushes comes in the same order: the result is the same.
        var (bodies, heavies) = (world.Bodies, world.Heavies);
        bodies.Clear();
        heavies.Clear();
        foreach (var e in world.Entities)
        {
            if (e.Kind == EntityKind.Hardpoint) continue;
            bodies.Add(e);
            if (e.Kind != EntityKind.Ship || e.Size != SizeClass.Small) heavies.Add(e);
        }
        bodies.MaxRadius = heavies.MaxRadius = world.Grid.MaxRadius;
        foreach (var a in world.Entities)
        {
            if (a.Kind != EntityKind.Ship) continue;
            foreach (var b in (a.Size == SizeClass.Small ? heavies : bodies).Near(a.X, a.Y, a.Radius))
            {
                if (b.Kind == EntityKind.Structure)
                {
                    PushOut(a, b);
                    continue;
                }
                // Each pair once; small craft may overlap each other.
                if (b.Kind != EntityKind.Ship || b.Id <= a.Id || (a.Size == SizeClass.Small && b.Size == SizeClass.Small)) continue;
                var min = (a.Radius + b.Radius) * 0.9;
                var dx = b.X - a.X;
                var dy = b.Y - a.Y;
                if (Math.Abs(dx) >= min || Math.Abs(dy) >= min) continue;
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
            a.X = Math.Max(0, Math.Min(world.Width, a.X));
            a.Y = Math.Max(0, Math.Min(world.Height, a.Y));
        }
    }

    static void PushOut(Entity a, Entity s)
    {
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
    /// the slowest.
    /// </summary>
    public static void IssueMove(World world, IEnumerable<int> ids, double x, double y, bool attackMove = false)
    {
        var ships = OwnShips(world, ids);
        if (ships.Count == 0) return;
        var spacing = ships.Max(s => s.Radius) * 2.4;
        var cx = ships.Average(s => s.X);
        var cy = ships.Average(s => s.Y);
        var heading = Hypot(x - cx, y - cy) > 1
            ? Math.Atan2(y - cy, x - cx)
            : Math.Atan2(ships.Sum(s => Math.Sin(s.Angle)), ships.Sum(s => Math.Cos(s.Angle)));
        var (dx, dy) = (Math.Cos(heading), Math.Sin(heading));
        // Stable sorts: equals keep the order they were selected in.
        var front = ships.Where(s => !Rear(s)).OrderByDescending(Toughness).ToList();
        var back = ships.Where(Rear).OrderByDescending(Toughness).ToList();
        ships = [.. front, .. back];
        var slots = FleetSlots(front.Count, back.Count, spacing);
        var group = GroupOf(world, ships, dx, dy);
        // The structures, picked out once for the whole fleet rather than once per ship.
        var structures = new List<Entity>();
        foreach (var e in world.Entities) if (e.Kind == EntityKind.Structure) structures.Add(e);
        for (var i = 0; i < ships.Count; i++)
        {
            var (f, l) = slots[i];
            var p = Reachable(world, structures, ships[i], x + f * dx - l * dy, y + f * dy + l * dx);
            ships[i].Order = attackMove ? Order.AttackMove(p.X, p.Y, group) : Order.Move(p.X, p.Y, group);
        }
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
        return new Point(px, py);
    }

    /// <summary>Sends ships at an enemy; together, they close in at the pace of the slowest.</summary>
    public static void IssueAttack(World world, IEnumerable<int> ids, int targetId)
    {
        var target = world.Entities.Get(targetId);
        var ships = OwnShips(world, ids).Where(s => s.Faction != target?.Faction).ToList();
        var group = GroupOf(world, ships, 0, 0);
        foreach (var s in ships) s.Order = Order.Attack(targetId, group);
    }

    public static void IssueStop(World world, IEnumerable<int> ids)
    {
        foreach (var s in OwnShips(world, ids))
        {
            s.Order = null;
            s.GuardX = s.X;
            s.GuardY = s.Y;
        }
    }
}
