// Port of the functions of src/sim/world.ts.
using SandboxShip.Sim.Data;

namespace SandboxShip.Sim;

public static class Worlds
{
    /// <summary>What the difficulty changes: AI income and how often it thinks.</summary>
    public static readonly IReadOnlyDictionary<Difficulty, DifficultyDef> Difficulties = new Dictionary<Difficulty, DifficultyDef>
    {
        [Difficulty.Easy] = new("Facile", 0.75, 2.5),
        [Difficulty.Normal] = new("Normale", 1, 1.5),
        [Difficulty.Hard] = new("Difficile", 1.35, 0.9),
    };

    public static World CreateWorld(double width, double height, int seed = 1) => new() { Width = width, Height = height, Seed = seed };

    /// <summary>Deterministic random number in [0, 1) (mulberry32).</summary>
    public static double Rand(World world) => Rng.Mulberry32(ref world.Seed);

    public static AIState NewAI(Difficulty difficulty = Difficulty.Normal) => new(difficulty);

    public static FactionState AddFaction(World world, Faction id, double credits, bool ai = false, Difficulty difficulty = Difficulty.Normal)
    {
        var f = new FactionState
        {
            Id = id, Credits = credits, AutoAbilities = ai,
            RallyX = world.Width / 2, RallyY = world.Height / 2,
            AI = ai ? NewAI(difficulty) : null,
        };
        world.Factions[id] = f;
        return f;
    }

    public static AsteroidField AddField(World world, double x, double y, double radius)
    {
        var field = new AsteroidField(world.NextId++, x, y, radius);
        world.Fields.Add(field);
        return field;
    }

    static Entity BaseEntity(World world, EntityKind kind, string type, Faction faction, double x, double y) =>
        new() { Id = world.NextId++, Kind = kind, Type = type, Faction = faction, X = x, Y = y, GuardX = x, GuardY = y };

    public static int ResearchLevel(World world, Faction faction, TechId tech) =>
        world.Factions.TryGetValue(faction, out var f) ? f.Research[tech] : 0;

    /// <summary>Hull multiplier of a faction: its trait and its armor research.</summary>
    public static double HullMult(World world, Faction faction) =>
        Factions.Trait(faction).Hull * (1 + Techs.ArmorBonus * ResearchLevel(world, faction, TechId.Armor));

    /// <summary>Shield multiplier of a faction: its trait and its shield research.</summary>
    public static double ShieldMult(World world, Faction faction) =>
        Factions.Trait(faction).Shield * (1 + Techs.ShieldsBonus * ResearchLevel(world, faction, TechId.Shields));

    static void ScaleDefenses(World world, Entity e)
    {
        var h = HullMult(world, e.Faction);
        var s = ShieldMult(world, e.Faction);
        e.Hull *= h;
        e.MaxHull *= h;
        e.Shield *= s;
        e.MaxShield *= s;
        e.ShieldRegen *= s;
    }

    /// <summary>A finished armor or shield research upgrades what the faction already fields.</summary>
    public static void RescaleForResearch(World world, Faction faction, TechId tech, double before)
    {
        if (tech != TechId.Armor && tech != TechId.Shields) return;
        var after = tech == TechId.Armor ? HullMult(world, faction) : ShieldMult(world, faction);
        var ratio = after / before;
        foreach (var e in world.Entities)
        {
            if (e.Faction != faction) continue;
            if (tech == TechId.Armor)
            {
                e.Hull *= ratio;
                e.MaxHull *= ratio;
            }
            else
            {
                e.Shield *= ratio;
                e.MaxShield *= ratio;
                e.ShieldRegen *= ratio;
            }
        }
    }

    public static List<WeaponState> Armed(IEnumerable<WeaponDef> defs) => defs.Select(d => new WeaponState(d)).ToList();

    static HangarState? HangarOf(HangarDef? def) => def is null ? null : new HangarState(def);

    public static Entity SpawnShip(World world, Faction faction, ShipClass cls, double x, double y, double angle = 0)
    {
        var def = Ships.Defs[cls];
        var e = BaseEntity(world, EntityKind.Ship, Ids.Of(cls), faction, x, y);
        e.Angle = angle;
        e.Radius = def.Radius;
        e.Size = def.Size;
        e.Hull = e.MaxHull = def.Hull;
        e.Shield = e.MaxShield = def.Shield;
        e.ShieldRegen = def.ShieldRegen;
        e.Weapons = Armed(def.Weapons);
        e.Range = Weapons.MaxRange(def.Weapons);
        e.Retarget = Rand(world) * 0.5;
        if (def.Ability is AbilityId a) e.Abilities = [new AbilityState(a), .. (def.Extra ?? []).Select(x => new AbilityState(x))];
        ScaleDefenses(world, e);
        world.Entities.Add(e);
        if (def.Hardpoints is not null) AddHardpoints(world, e, def.Hardpoints);
        return e;
    }

    /// <summary>A hero: its base ship, much tougher, with the hero's own ability.</summary>
    public static Entity SpawnHero(World world, Faction faction, HeroId id, double x, double y, double angle = 0)
    {
        var def = Heroes.Defs[id];
        var e = SpawnShip(world, faction, def.Base, x, y, angle);
        e.Hero = id;
        foreach (var part in ChildrenOf(world, e.Id).Prepend(e))
        {
            part.Hull *= def.Hull;
            part.MaxHull *= def.Hull;
        }
        e.Shield *= def.Shield;
        e.MaxShield *= def.Shield;
        e.ShieldRegen *= def.Shield;
        e.Abilities = [new AbilityState(def.Ability)];
        return e;
    }

    /// <summary>Spawns a station already upgraded to `level`, with the hardpoints of every level up to it.</summary>
    public static Entity SpawnStation(World world, Faction faction, double x, double y, int level = 1)
    {
        var lvl = Station.Levels[level - 1];
        var e = BaseEntity(world, EntityKind.Structure, "station", faction, x, y);
        e.Radius = Station.Radius;
        e.Level = level;
        e.Hull = e.MaxHull = lvl.Hull;
        e.Shield = e.MaxShield = lvl.Shield;
        e.ShieldRegen = lvl.ShieldRegen;
        e.Weapons = Armed(Station.Weapons);
        e.Range = Weapons.MaxRange(Station.Weapons);
        ScaleDefenses(world, e);
        world.Entities.Add(e);
        for (var i = 0; i < level; i++) AddHardpoints(world, e, Station.Levels[i].Hardpoints);
        if (world.Factions.TryGetValue(faction, out var f))
        {
            f.StationId = e.Id;
            f.HadStation = true;
            // Default rally point: in front of the station, towards the map center.
            var a = Math.Atan2(world.Height / 2 - y, world.Width / 2 - x);
            f.RallyX = x + Math.Cos(a) * 320;
            f.RallyY = y + Math.Sin(a) * 320;
        }
        return e;
    }

    /// <summary>Attaches destructible parts to a station or a ship. Ship parts take the ship's size class.</summary>
    public static void AddHardpoints(World world, Entity parent, IEnumerable<HardpointSlot> slots)
    {
        foreach (var slot in slots)
        {
            var def = Hardpoints.Defs[slot.Role];
            var weapons = slot.Weapons ?? def.Weapons;
            var hull = slot.Hull ?? def.Hull;
            var e = BaseEntity(world, EntityKind.Hardpoint, Ids.Of(slot.Role), parent.Faction, parent.X, parent.Y);
            e.Radius = slot.Radius ?? def.Radius;
            e.Hull = e.MaxHull = hull;
            e.Size = parent.Kind == EntityKind.Ship ? parent.Size : SizeClass.Structure;
            e.Weapons = Armed(weapons);
            e.Range = Weapons.MaxRange(weapons);
            e.ParentId = parent.Id;
            e.Ox = slot.X;
            e.Oy = slot.Y;
            e.Hangar = HangarOf(slot.Hangar ?? def.Hangar);
            e.Retarget = Rand(world) * 0.5;
            PlaceOnParent(e, parent);
            ScaleDefenses(world, e);
            world.Entities.Add(e);
            if (slot.Role == HardpointRole.Engine)
            {
                parent.Engines = (parent.Engines ?? 0) + 1;
                parent.MaxEngines++;
            }
            if (slot.Role == HardpointRole.Shield) parent.ShieldGens = (parent.ShieldGens ?? 0) + 1;
            // A ship engages from as far as its longest-reaching turret.
            if (parent.Kind == EntityKind.Ship) parent.Range = Math.Max(parent.Range, e.Range);
        }
    }

    static void PlaceOnParent(Entity e, Entity p)
    {
        var c = Math.Cos(p.Angle);
        var s = Math.Sin(p.Angle);
        e.X = p.X + e.Ox * c - e.Oy * s;
        e.Y = p.Y + e.Ox * s + e.Oy * c;
        e.Angle = p.Angle;
    }

    /// <summary>Keeps ship parts in place on their moving, turning ship.</summary>
    public static void AttachHardpoints(World world)
    {
        foreach (var e in world.Entities)
        {
            if (e.Kind != EntityKind.Hardpoint || e.ParentId is null) continue;
            var p = world.Entities.Get(e.ParentId);
            if (p?.Kind == EntityKind.Ship) PlaceOnParent(e, p);
        }
    }

    /// <summary>Spawns a buildable structure; `built: false` starts it as a construction site.</summary>
    public static Entity SpawnStructure(World world, Faction faction, BuildableType type, double x, double y, bool built = true)
    {
        var def = Buildables.Defs[type];
        var e = BaseEntity(world, EntityKind.Structure, Ids.Of(type), faction, x, y);
        e.Radius = def.Radius;
        e.Hull = built ? def.Hull : def.Hull * 0.1;
        e.MaxHull = def.Hull;
        e.Shield = built ? def.Shield : 0;
        e.MaxShield = def.Shield;
        e.ShieldRegen = def.ShieldRegen;
        e.Weapons = Armed(def.Weapons);
        e.Range = Weapons.MaxRange(def.Weapons);
        e.Build = built ? 1 : 0;
        e.BuildTime = def.BuildTime;
        e.Level = 1;
        e.Hangar = HangarOf(def.Hangar);
        e.Income = def.Income;
        e.Retarget = Rand(world) * 0.5;
        // Its ability starts charging once it is built.
        if (def.Ability is AbilityId a) e.Abilities = [new AbilityState(a) { Cooldown = Abilities.Defs[a].Cooldown }];
        ScaleDefenses(world, e);
        world.Entities.Add(e);
        return e;
    }

    public static double Dist<TA, TB>(TA a, TB b) where TA : IPoint where TB : IPoint => Hypot(a.X - b.X, a.Y - b.Y);

    public static double Hypot(double x, double y) => Math.Sqrt(x * x + y * y);

    public static List<Entity> ChildrenOf(World world, int parentId)
    {
        var out_ = new List<Entity>();
        foreach (var e in world.Entities) if (e.ParentId == parentId) out_.Add(e);
        return out_;
    }

    public static bool HasHardpoint(World world, int stationId, HardpointRole role)
    {
        var type = Ids.Of(role);
        foreach (var e in world.Entities) if (e.ParentId == stationId && e.Type == type) return true;
        return false;
    }

    public static Entity? StationOf(World world, Faction faction) =>
        world.Factions.TryGetValue(faction, out var f) ? world.Entities.Get(f.StationId) : null;

    /// <summary>Shields absorb damage first; the rest goes to the hull. A ship's shields cover its parts too.</summary>
    public static void ApplyDamage(Entity target, double amount, Entity? shieldOf = null)
    {
        shieldOf ??= target;
        var absorbed = Math.Min(shieldOf.Shield, amount);
        shieldOf.Shield -= absorbed;
        target.Hull -= amount - absorbed;
        // A shield knocked down stays down a while before it comes back (not in the web version).
        if (absorbed > 0 && shieldOf.Shield <= 0) shieldOf.ShieldDown = ShieldBreak(shieldOf.Size);
    }

    /// <summary>Seconds a knocked-down shield stays down: longer on bigger hulls.</summary>
    public static double ShieldBreak(SizeClass size) => size switch
    {
        SizeClass.Small => 3,
        SizeClass.Medium => 4,
        _ => 6,
    };

    /// <summary>Share of a ship's value paid to whoever shoots it down (not in the web version).</summary>
    public const double KillShare = 0.2;

    /// <summary>What shooting a ship down pays. The raid's colossus pays its own bounty instead (Raids.Reward).</summary>
    public static double BountyOf(Entity e) =>
        e.Faction == Faction.Errant && e.Cls == ShipClass.Colossus ? 0
            : Js.Round(KillShare * (e.Hero is HeroId h ? Heroes.Defs[h].Cost : Ships.UnitValue(e.Cls)));

    /// <summary>
    /// Removes destroyed entities. Hardpoints die with what they are attached to, and a lost
    /// engine or shield generator weakens its parent.
    /// </summary>
    public static void RemoveDead(World world)
    {
        List<Entity>? dead = null;
        foreach (var e in world.Entities) if (e.Hull <= 0) (dead ??= []).Add(e);
        if (dead is null) return;
        for (var i = 0; i < dead.Count; i++)
        {
            var e = dead[i];
            if (!world.Entities.Remove(e.Id)) continue;
            if (e.Kind != EntityKind.Hardpoint && !e.Launched)
            {
                if (e.Kind == EntityKind.Ship && world.Factions.TryGetValue(e.Faction, out var own)) own.Stats.Lost++;
                if (e.LastHitBy is Faction by && by != e.Faction && world.Factions.TryGetValue(by, out var killer)) killer.Stats.Kills++;
            }
            // A ship shot down pays its killer, hangar craft included.
            if (e.Kind == EntityKind.Ship && e.LastHitBy is Faction hunter && hunter != e.Faction && world.Factions.TryGetValue(hunter, out var paid))
            {
                var bounty = BountyOf(e);
                paid.Credits += bounty;
                paid.Bounties += bounty;
                paid.Stats.Earned += bounty;
            }
            world.Events.Add(new Destroyed(e.Id, e.Kind, e.Type, e.Faction, e.X, e.Y, e.Radius));
            // The raid's bounty is for the Errants' colossus, not for one a faction built.
            if (e.Type == "colossus" && e.Faction == Faction.Errant) Raids.Reward(world, e);
            if (e.Hero is HeroId hero && world.Factions.TryGetValue(e.Faction, out var hf)) hf.HeroCooldowns[hero] = Heroes.Defs[hero].Respawn;
            var parent = world.Entities.Get(e.ParentId);
            if (parent?.Type == "station") parent.Rebuild.Add(new Rebuild(Ids.Role(e.Type), e.Ox, e.Oy));
            if (parent is not null && e.Type == "engine" && parent.Engines > 0) parent.Engines--;
            if (parent is not null && e.Type == "shield" && parent.ShieldGens > 0) parent.ShieldGens--;
            dead.AddRange(ChildrenOf(world, e.Id));
            if (e.Type == "station" && world.Factions.TryGetValue(e.Faction, out var sf) && sf.StationId == e.Id)
            {
                sf.StationId = null;
                sf.Queue = [];
                sf.BuildQueue = [];
            }
            if (e.FieldId is int fieldId)
            {
                var field = world.Fields.Find(fl => fl.Id == fieldId);
                if (field is not null) field.MineId = null;
            }
        }
    }

    /// <summary>The last faction holding a station wins, once at least two factions had one.</summary>
    public static void CheckVictory(World world)
    {
        if (world.Winner is not null) return;
        // Counted in place: this runs every step.
        int contenders = 0, alive = 0;
        FactionState? last = null;
        foreach (var f in world.Factions.Values)
        {
            if (!f.HadStation) continue;
            contenders++;
            if (f.StationId is null) continue;
            alive++;
            last = f;
        }
        if (contenders >= 2 && alive == 1)
        {
            world.Winner = last!.Id;
            world.Events.Add(new Victory(last.Id));
        }
    }
}
