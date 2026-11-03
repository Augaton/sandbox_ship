// Construction, hangars, economy, production, research and placement. Port of
// src/sim/structures.ts.
using SandboxShip.Sim.Data;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim;

public enum BuildArea { Station, Outpost, Mine }

/// <summary>A disc on the map: a build area, and what it is around.</summary>
public readonly record struct Circle(double X, double Y, double R, BuildArea Area = BuildArea.Station) : IPoint;

/// <summary>Whether a structure may go there, and where it goes: mines snap onto their field.</summary>
public readonly record struct Placement(bool Ok, string Reason, double X, double Y);

public enum QueueSide { Units, Buildings }

public static class Structures
{
    public static void UpdateConstruction(World world, double dt)
    {
        foreach (var e in world.Entities)
        {
            if (e.Build >= 1) continue;
            var step = dt / e.BuildTime;
            e.Build = Math.Min(1, e.Build + step);
            e.Hull = Math.Min(e.MaxHull, e.Hull + e.MaxHull * 0.9 * step);
            if (e.Build >= 1) world.Events.Add(new Built(e.Id));
        }
    }

    static void Launch(World world, Entity hangar, ShipClass cls, int count)
    {
        // Launched craft hold position away from the station center.
        var parent = world.Entities.Get(hangar.ParentId);
        var from = parent ?? hangar;
        var a = parent is not null ? Math.Atan2(hangar.Y - parent.Y, hangar.X - parent.X) : Rand(world) * Math.PI * 2;
        var gx = hangar.X + Math.Cos(a) * 140;
        var gy = hangar.Y + Math.Sin(a) * 140;
        for (var i = 0; i < count; i++)
        {
            var s = SpawnShip(world, hangar.Side, cls, hangar.X, hangar.Y, Math.Atan2(hangar.Y - from.Y, hangar.X - from.X));
            // They keep that place beside what launched them, and follow it if it moves.
            Movement.Assign(s, from, gx + (i - (count - 1) / 2.0) * 24, gy);
            s.Launched = true;
            hangar.Hangar!.Launched.Add(s.Id);
        }
    }

    /// <summary>Hangar research: two more craft per level, launches 15 % faster per level.</summary>
    static (int Cap, double Interval) HangarLimits(World world, Entity e)
    {
        var lvl = ResearchLevel(world, e.Side, TechId.Hangars);
        var h = e.Hangar!.Def;
        return (h.Cap + 2 * lvl, h.Interval * (1 - 0.15 * lvl));
    }

    /// <summary>Hangars launch squadrons in turn, up to their cap of living craft.</summary>
    public static void UpdateHangars(World world, double dt)
    {
        foreach (var e in world.Entities)
        {
            var h = e.Hangar;
            if (h is null || e.Build < 1) continue;
            // Lost craft leave the count; in place, without a closure for every hangar every step.
            var n = 0;
            for (var i = 0; i < h.Launched.Count; i++)
            {
                if (world.Entities.Has(h.Launched[i])) h.Launched[n++] = h.Launched[i];
            }
            h.Launched.RemoveRange(n, h.Launched.Count - n);
            // A carrier's catapults: three times the pace (not in the web version).
            h.Timer -= dt * (AbilitySystem.IsActive(world, e, AbilityId.Launch) ? 3 : 1);
            if (h.Timer > 0) continue;
            var sq = h.Def.Squadrons[h.Next % h.Def.Squadrons.Count];
            var limits = HangarLimits(world, e);
            if (h.Launched.Count + sq.Size > limits.Cap) continue;
            h.Timer = limits.Interval;
            h.Next++;
            Launch(world, e, sq.Cls, sq.Size);
        }
    }

    /// <summary>The AI's difficulty scales its income.</summary>
    static double IncomeMult(FactionState f) => f.AI is not null ? Difficulties[f.AI.Difficulty].Income : 1;

    static void Earn(FactionState f, double amount)
    {
        f.Credits += amount;
        f.Stats.Earned += amount;
    }

    /// <summary>Credits per second a side earns now: its station and its working mines.</summary>
    public static double IncomeRate(World world, Side side)
    {
        if (!world.Factions.TryGetValue(side, out var f)) return 0;
        var rate = StationOf(world, side) is { } station ? Station.Levels[station.Level - 1].Income : 0;
        foreach (var e in world.Entities)
        {
            if (e.Side == side && e.Income > 0 && e.Build >= 1) rate += e.Income * (1 + Techs.MiningBonus * f.Research[TechId.Mining]);
        }
        return rate * IncomeMult(f);
    }

    public static void UpdateEconomy(World world, double dt)
    {
        foreach (var f in world.Factions.Values)
        {
            var station = StationOf(world, f.Id);
            // Each station level brings in a little more.
            if (station is not null) Earn(f, Station.Levels[station.Level - 1].Income * IncomeMult(f) * dt);
        }
        foreach (var e in world.Entities)
        {
            if (e.Income > 0 && e.Build >= 1 && world.Factions.TryGetValue(e.Side, out var f))
                Earn(f, e.Income * (1 + Techs.MiningBonus * f.Research[TechId.Mining]) * IncomeMult(f) * dt);
        }
    }

    static void ApplyUpgrade(World world, Entity station)
    {
        var next = Station.Levels[station.Level];
        station.Level++;
        var hull = next.Hull * HullMult(world, station.Side);
        station.Hull += hull - station.MaxHull;
        station.MaxHull = hull;
        station.MaxShield = next.Shield * ShieldMult(world, station.Side);
        station.ShieldRegen = next.ShieldRegen * ShieldMult(world, station.Side);
        AddHardpoints(world, station, next.Hardpoints);
    }

    static Entity ShipyardPos(World world, Entity station) =>
        ChildrenOf(world, station.Id).Find(c => c.Type == "shipyard") ?? station;

    /// <summary>
    /// Two queues run side by side: ships (paused while the shipyard is down) and station
    /// upgrades. Only the front of each progresses.
    /// </summary>
    public static void UpdateProduction(World world, double dt)
    {
        foreach (var f in world.Factions.Values)
        {
            var station = StationOf(world, f.Id);
            if (station is null) continue;
            var work = f.BuildQueue.Count > 0 ? f.BuildQueue[0] : null;
            if (work is not null && (work.Remaining -= dt) <= 0)
            {
                f.BuildQueue.RemoveAt(0);
                if (work.Kind == QueueKind.Upgrade) ApplyUpgrade(world, station);
                if (work.Kind == QueueKind.Tier && world.Entities.Get(work.TargetId) is { } target) ApplyTier(world, target);
            }
            if (f.HeroCooldowns.Count > 0)
            {
                foreach (var id in f.HeroCooldowns.Keys.ToList()) f.HeroCooldowns[id] = Math.Max(0, f.HeroCooldowns[id] - dt);
            }
            // Classes in a loop go back in the queue once none of them is waiting; short of credits, they wait.
            foreach (var cls in f.Repeat)
            {
                if (!f.Queue.Exists(q => q.Kind == QueueKind.Ship && q.Cls == cls)) EnqueueShip(world, f.Id, cls);
            }
            var item = f.Queue.Count > 0 ? f.Queue[0] : null;
            if (item is null || (item.Kind != QueueKind.Ship && item.Kind != QueueKind.Hero) || !HasHardpoint(world, station.Id, HardpointRole.Shipyard)) continue;
            item.Remaining -= dt;
            if (item.Remaining > 0) continue;
            f.Queue.RemoveAt(0);
            var p = ShipyardPos(world, station);
            var angle = Math.Atan2(f.RallyY - p.Y, f.RallyX - p.X);
            // Squadrons come out together and fly to the rally point side by side.
            var count = item.Kind == QueueKind.Hero ? 1 : Ships.Defs[item.Cls].Squad ?? 1;
            f.Stats.Built += count;
            for (var i = 0; i < count; i++)
            {
                var s = item.Kind == QueueKind.Hero
                    ? SpawnHero(world, f.Id, item.Hero, p.X, p.Y, angle)
                    : SpawnShip(world, f.Id, item.Cls, p.X + i * 6, p.Y, angle);
                var x = f.RallyX + (Rand(world) - 0.5) * 160;
                var y = f.RallyY + (Rand(world) - 0.5) * 160;
                s.Order = Order.Move(x, y);
                world.Events.Add(new Delivered(s.Id));
            }
        }
    }

    /// <summary>Whether the side can queue this ship class right now, and if not, why.</summary>
    public static Result CanBuildShip(World world, Side side, ShipClass cls)
    {
        var def = Ships.Defs[cls];
        var station = StationOf(world, side);
        if (!world.Factions.TryGetValue(side, out var f) || station is null) return Result.Fail("Aucune station");
        if (def.FromHangar) return Result.Fail("Lancé par les hangars");
        if (!HasHardpoint(world, station.Id, HardpointRole.Shipyard)) return Result.Fail("Chantier naval détruit");
        if (station.Level < def.MinStationLevel) return Result.Fail($"Station niveau {def.MinStationLevel} requise");
        if (f.Queue.Count >= Buildables.MaxQueue) return Result.Fail("File d'attente pleine");
        if (def.Limit is int limit && InService(world, side, cls) >= limit) return Result.Fail(def.LimitReason);
        if (f.Credits < Ships.Cost(cls, f.Race)) return Result.Fail("Crédits insuffisants");
        return Result.OK;
    }

    /// <summary>How many of this class the side fields or has in its queue.</summary>
    public static int InService(World world, Side side, ShipClass cls)
    {
        var type = Ids.Of(cls);
        var n = 0;
        foreach (var e in world.Entities) if (e.Type == type && e.Kind == EntityKind.Ship && e.Side == side) n++;
        return n + (world.Factions.TryGetValue(side, out var f) ? f.Queue.Count(q => q.Kind == QueueKind.Ship && q.Cls == cls) : 0);
    }

    public static Result EnqueueShip(World world, Side side, ShipClass cls)
    {
        var r = CanBuildShip(world, side, cls);
        if (!r.Ok) return r;
        var def = Ships.Defs[cls];
        var f = world.Factions[side];
        var cost = Ships.Cost(cls, f.Race);
        f.Credits -= cost;
        var time = def.BuildTime * (1 - Techs.LogisticsBonus * f.Research[TechId.Logistics]);
        f.Queue.Add(new QueueItem { Kind = QueueKind.Ship, Cls = cls, Remaining = time, Total = time, Cost = cost });
        return Result.OK;
    }

    static bool HeroInService(World world, Side side, HeroId id)
    {
        foreach (var e in world.Entities) if (e.Hero == id && e.Side == side) return true;
        return world.Factions.TryGetValue(side, out var f) && f.Queue.Exists(q => q.Kind == QueueKind.Hero && q.Hero == id);
    }

    /// <summary>One of each hero at a time, and a delay before a fallen hero can be built again.</summary>
    public static Result CanBuildHero(World world, Side side, HeroId id)
    {
        var def = Heroes.Defs[id];
        var station = StationOf(world, side);
        if (!world.Factions.TryGetValue(side, out var f) || station is null) return Result.Fail("Aucune station");
        if (def.Id.Faction != f.Race) return Result.Fail("Héros d'une autre faction");
        if (!HasHardpoint(world, station.Id, HardpointRole.Shipyard)) return Result.Fail("Chantier naval détruit");
        if (station.Level < def.MinStationLevel) return Result.Fail($"Station niveau {def.MinStationLevel} requise");
        if (HeroInService(world, side, id)) return Result.Fail("Déjà en service");
        var wait = f.HeroCooldowns.GetValueOrDefault(id);
        if (wait > 0) return Result.Fail($"Disponible dans {Math.Ceiling(wait)} s");
        if (f.Queue.Count >= Buildables.MaxQueue) return Result.Fail("File d'attente pleine");
        if (f.Credits < def.Cost) return Result.Fail("Crédits insuffisants");
        return Result.OK;
    }

    public static Result EnqueueHero(World world, Side side, HeroId id)
    {
        var r = CanBuildHero(world, side, id);
        if (!r.Ok) return r;
        var def = Heroes.Defs[id];
        var f = world.Factions[side];
        f.Credits -= def.Cost;
        f.Queue.Add(new QueueItem { Kind = QueueKind.Hero, Hero = id, Remaining = def.BuildTime, Total = def.BuildTime, Cost = def.Cost });
        return Result.OK;
    }

    public static Result CanUpgrade(World world, Side side)
    {
        var station = StationOf(world, side);
        if (!world.Factions.TryGetValue(side, out var f) || station is null) return Result.Fail("Aucune station");
        if (station.Level >= Station.Levels.Count) return Result.Fail("Niveau maximum");
        if (f.BuildQueue.Exists(q => q.Kind == QueueKind.Upgrade)) return Result.Fail("Amélioration déjà en cours");
        if (f.BuildQueue.Count >= Buildables.MaxQueue) return Result.Fail("File d'attente pleine");
        if (f.Credits < Station.Levels[station.Level].UpgradeCost) return Result.Fail("Crédits insuffisants");
        return Result.OK;
    }

    public static Result EnqueueUpgrade(World world, Side side)
    {
        var r = CanUpgrade(world, side);
        if (!r.Ok) return r;
        var f = world.Factions[side];
        var next = Station.Levels[StationOf(world, side)!.Level];
        f.Credits -= next.UpgradeCost;
        f.BuildQueue.Add(new QueueItem { Kind = QueueKind.Upgrade, Remaining = next.UpgradeTime, Total = next.UpgradeTime, Cost = next.UpgradeCost });
        return Result.OK;
    }

    /// <summary>Cancels a queued ship (units) or station upgrade (buildings) and refunds it in full.</summary>
    public static void CancelQueued(World world, Side side, int index, QueueSide which = QueueSide.Units)
    {
        if (!world.Factions.TryGetValue(side, out var f)) return;
        var queue = which == QueueSide.Units ? f.Queue : f.BuildQueue;
        if (index < 0 || index >= queue.Count) return;
        var item = queue[index];
        queue.RemoveAt(index);
        f.Credits += item.Cost;
    }

    /// <summary>A station rebuilds its lost parts one at a time, while nobody shoots at it.</summary>
    public static void UpdateRebuild(World world, double dt)
    {
        foreach (var e in world.Entities)
        {
            if (e.Rebuild.Count == 0 || world.Time - e.HitAt < Buildables.RebuildQuiet) continue;
            var next = e.Rebuild[0];
            next.Timer -= dt;
            if (next.Timer > 0) continue;
            e.Rebuild.RemoveAt(0);
            AddHardpoints(world, e, [new HardpointSlot(next.Role, next.X, next.Y)]);
            world.Events.Add(new Rebuilt(e.Id, e.Side, next.Role));
        }
    }

    static BuildableDef? BuildableOf(Entity e) => Ids.IsBuildable(e.Type, out var b) ? Buildables.Defs[b] : null;

    /// <summary>Next level of a platform or outpost: sturdier, better armed, a wider build area.</summary>
    public static void ApplyTier(World world, Entity e)
    {
        var tiers = BuildableOf(e)?.Tiers;
        if (tiers is null || e.Level - 1 >= tiers.Count) return;
        var tier = tiers[e.Level - 1];
        e.Level++;
        var hull = tier.Hull * HullMult(world, e.Side);
        e.Hull += hull - e.MaxHull;
        e.MaxHull = hull;
        e.MaxShield = tier.Shield * ShieldMult(world, e.Side);
        if (tier.Weapons is not null)
        {
            e.Weapons = Armed(tier.Weapons);
            e.Range = Weapons.MaxRange(tier.Weapons);
        }
        if (tier.Hangar is not null && e.Hangar is not null) e.Hangar.Def = tier.Hangar;
    }

    static int QueuedTiers(FactionState f, int id) => f.BuildQueue.Count(q => q.Kind == QueueKind.Tier && q.TargetId == id);

    public static Result CanUpgradeStructure(World world, Side side, int id)
    {
        var e = world.Entities.Get(id);
        var tiers = e?.Kind == EntityKind.Structure ? BuildableOf(e)?.Tiers : null;
        if (e is null || !world.Factions.TryGetValue(side, out var f) || e.Side != side || tiers is null) return Result.Fail("Rien à améliorer");
        if (e.Build < 1) return Result.Fail("En construction");
        var queued = QueuedTiers(f, id);
        if (e.Level + queued > tiers.Count) return Result.Fail("Niveau maximum");
        if (f.BuildQueue.Count >= Buildables.MaxQueue) return Result.Fail("File d'attente pleine");
        if (f.Credits < tiers[e.Level - 1 + queued].Cost) return Result.Fail("Crédits insuffisants");
        return Result.OK;
    }

    /// <summary>Queues the next level of a platform or hangar in the buildings queue.</summary>
    public static Result UpgradeStructure(World world, Side side, int id)
    {
        var r = CanUpgradeStructure(world, side, id);
        if (!r.Ok) return r;
        var f = world.Factions[side];
        var e = world.Entities.Get(id)!;
        var tier = BuildableOf(e)!.Tiers![e.Level - 1 + QueuedTiers(f, id)];
        f.Credits -= tier.Cost;
        f.BuildQueue.Add(new QueueItem { Kind = QueueKind.Tier, TargetId = id, Remaining = tier.Time, Total = tier.Time, Cost = tier.Cost });
        return Result.OK;
    }

    /// <summary>Calls off a structure still under construction, for a full refund.</summary>
    public static void CancelConstruction(World world, Side side, int id)
    {
        var e = world.Entities.Get(id);
        if (e is null || !world.Factions.TryGetValue(side, out var f) || e.Side != side || e.Kind != EntityKind.Structure || e.Build >= 1 || e.Type == "station") return;
        world.Entities.Remove(id);
        f.Credits += BuildableOf(e)!.Cost;
        var field = world.Fields.Find(fl => fl.MineId == id);
        if (field is not null) field.MineId = null;
    }

    /// <summary>Whether the next level of this tech can join the queue: judged on the level it would reach.</summary>
    public static Result CanResearch(World world, Side side, TechId tech)
    {
        var station = StationOf(world, side);
        if (!world.Factions.TryGetValue(side, out var f) || station is null) return Result.Fail("Aucune station");
        var level = ResearchQueue.PlannedLevel(f, tech);
        if (!HasLab(world, side)) return Result.Fail("Laboratoire requis");
        if (level >= Techs.Defs[tech].MaxLevel) return Result.Fail("Niveau maximum");
        if (ResearchQueue.Jobs(f).Count >= Buildables.MaxQueue) return Result.Fail("File d'attente pleine");
        // Research levels past the first need a station of that level, the last ones the last station.
        var needs = Techs.StationFor(level + 1);
        if (station.Level < needs) return Result.Fail($"Station niveau {needs} requise");
        if (f.Credits < Techs.Defs[tech].Costs[level]) return Result.Fail("Crédits insuffisants");
        return Result.OK;
    }

    public static Result StartResearch(World world, Side side, TechId tech)
    {
        var r = CanResearch(world, side, tech);
        if (!r.Ok) return r;
        ResearchQueue.Queue(world.Factions[side], tech);
        return Result.OK;
    }

    /// <summary>Cancels a research job, 0 being the running one; later levels of the same tech go too.</summary>
    public static void CancelResearch(World world, Side side, int index = 0)
    {
        if (world.Factions.TryGetValue(side, out var f)) ResearchQueue.CancelAt(f, index);
    }

    public static void UpdateResearch(World world, double dt)
    {
        foreach (var f in world.Factions.Values)
        {
            var job = f.Researching;
            // Without its lab, research waits for a new one.
            if (job is null || !HasLab(world, f.Id)) continue;
            var before = job.Tech == TechId.Armor ? HullMult(world, f.Id) : ShieldMult(world, f.Id);
            if (ResearchQueue.Advance(f, dt) is not TechId done) continue;
            RescaleForResearch(world, f.Id, done, before);
            world.Events.Add(new Researched(f.Id, done));
        }
    }

    /// <summary>Construction radius of a built outpost at its level; 0 for anything else.</summary>
    public static double ZoneOf(Entity e)
    {
        var def = e.Kind == EntityKind.Structure && e.Build >= 1 ? BuildableOf(e) : null;
        if (def?.Zone is not double zone) return 0;
        return e.Level > 1 ? def.Tiers![e.Level - 2].Zone ?? zone : zone;
    }

    /// <summary>Where this structure may go: around the station and outposts, and around mines for some.</summary>
    public static List<Circle> BuildZones(World world, Side side, BuildableType type)
    {
        var station = StationOf(world, side);
        var zones = station is not null ? new List<Circle> { new(station.X, station.Y, Station.ConstructionRadius) } : [];
        var nearMines = Buildables.Defs[type].NearMines;
        foreach (var e in world.Entities)
        {
            if (e.Side != side || e.Kind != EntityKind.Structure) continue;
            var r = ZoneOf(e);
            if (r > 0) zones.Add(new Circle(e.X, e.Y, r, BuildArea.Outpost));
            else if (nearMines && e.Type == "mine" && e.Build >= 1) zones.Add(new Circle(e.X, e.Y, Buildables.MineZone, BuildArea.Mine));
        }
        return zones;
    }

    static int PlatformCap(BuildArea area) => area switch
    {
        BuildArea.Station => Buildables.StationPlatforms,
        BuildArea.Outpost => Buildables.OutpostPlatforms,
        _ => Buildables.MinePlatforms,
    };

    static int PlatformsIn(World world, Side side, Circle z) =>
        world.Entities.Count(e => e.Side == side && e.Type == "platform" && Hypot(e.X - z.X, e.Y - z.Y) <= z.R);

    /// <summary>Platforms the side may still build, over all its build areas.</summary>
    public static int PlatformRoom(World world, Side side) =>
        BuildZones(world, side, BuildableType.Platform).Sum(z => Math.Max(0, PlatformCap(z.Area) - PlatformsIn(world, side, z)));

    /// <summary>Whether a research building stands and works.</summary>
    public static bool HasLab(World world, Side side)
    {
        foreach (var e in world.Entities)
        {
            if (e.Side == side && e.Type == "lab" && e.Build >= 1) return true;
        }
        return false;
    }

    /// <summary>Outside the build areas: a ship of the side must hold the spot, with no foe fighting near.</summary>
    static Result HoldCheck(World world, Side side, IPoint at, double r)
    {
        var held = false;
        var fight = false;
        var c = new Point(at.X, at.Y);
        foreach (var e in world.Entities)
        {
            var d = Dist(e, c) - r;
            if (e.Side == side && e.Kind == EntityKind.Ship && d <= Buildables.HoldRange) held = true;
            if (e.Kind != EntityKind.Hardpoint && (e.Kind == EntityKind.Ship || e.Weapons.Count > 0) && d <= Buildables.DangerRange && Foes(world, e.Side, side)) fight = true;
        }
        if (fight) return Result.Fail("Combat trop proche");
        if (!held) return Result.Fail("Un vaisseau doit tenir la position");
        return Result.OK;
    }

    static bool InBuildZone(World world, Side side, BuildableType type, double x, double y) =>
        BuildZones(world, side, type).Exists(z => Hypot(z.X - x, z.Y - y) <= z.R);

    /// <summary>
    /// Mines snap onto a free asteroid field anywhere on the map. Other structures go
    /// inside the station's construction radius, clear of everything else.
    /// </summary>
    public static Placement CheckPlacement(World world, Side side, BuildableType type, double x, double y)
    {
        var def = Buildables.Defs[type];
        var station = StationOf(world, side);
        var spot = new Point(x, y);
        Placement At(Result r, double px, double py) => new(r.Ok, r.Reason, px, py);
        if (!world.Factions.TryGetValue(side, out var f)) return At(Result.Fail("Faction inconnue"), x, y);
        // Without a station (an invasion force), only what a level 1 station would allow.
        if ((station?.Level ?? 1) < def.MinStationLevel) return At(Result.Fail($"Station niveau {def.MinStationLevel} requise"), x, y);
        if (f.Credits < def.Cost) return At(Result.Fail("Crédits insuffisants"), x, y);
        if (type == BuildableType.StarLance && world.Entities.Any(e => e.Side == side && e.Type == "starLance"))
            return At(Result.Fail("Une seule Lance stellaire"), x, y);
        if (type == BuildableType.Mine)
        {
            var field = world.Fields.Find(fl => Dist(fl, spot) <= fl.Radius);
            if (field is null) return At(Result.Fail("Placer sur un champ d'astéroïdes"), x, y);
            if (field.MineId is not null) return At(Result.Fail("Champ déjà exploité"), field.X, field.Y);
            // Within a build area the field is safe; away from them, a ship must hold it, with no fight there.
            var home = BuildZones(world, side, BuildableType.Mine).Exists(z => Dist(z, field) <= z.R + field.Radius);
            if (!home)
            {
                var held = HoldCheck(world, side, field, field.Radius);
                if (!held.Ok) return At(held, field.X, field.Y);
            }
            return At(Result.OK, field.X, field.Y);
        }
        if (x < 0 || y < 0 || x > world.Width || y > world.Height) return At(Result.Fail("Hors de la carte"), x, y);
        if (type == BuildableType.Outpost)
        {
            if (world.Fields.Exists(fl => Dist(fl, spot) < fl.Radius + def.Radius)) return At(Result.Fail("Pas sur un champ d'astéroïdes"), x, y);
            if (!InBuildZone(world, side, type, x, y))
            {
                var held = HoldCheck(world, side, spot, def.Radius);
                if (!held.Ok) return At(held, x, y);
            }
        }
        else if (!InBuildZone(world, side, type, x, y))
        {
            return At(Result.Fail(def.NearMines ? "Trop loin de la station, des avant-postes et des mines" : "Trop loin de la station et des avant-postes"), x, y);
        }
        else if (type == BuildableType.Platform)
        {
            // Each area holds a few platforms: 3 around the station, 2 per outpost, 1 per mine.
            var here = BuildZones(world, side, type).Where(z => Hypot(z.X - x, z.Y - y) <= z.R).ToList();
            if (!here.Exists(z => PlatformsIn(world, side, z) < PlatformCap(z.Area)))
                return At(Result.Fail($"Plateformes au maximum ici ({here.Max(z => PlatformCap(z.Area))})"), x, y);
        }
        foreach (var e in world.Entities)
        {
            if (e.Kind != EntityKind.Ship && Dist(e, spot) < e.Radius + def.Radius + 10) return At(Result.Fail("Emplacement occupé"), x, y);
        }
        if (world.Terrain.Count > 0 && !Terrains.Clear(world, x, y, def.Radius)) return At(Result.Fail("Terrain impraticable"), x, y);
        return At(Result.OK, x, y);
    }

    public static Result PlaceStructure(World world, Side side, BuildableType type, double x, double y)
    {
        var p = CheckPlacement(world, side, type, x, y);
        if (!p.Ok) return Result.Fail(p.Reason);
        world.Factions[side].Credits -= Buildables.Defs[type].Cost;
        var e = SpawnStructure(world, side, type, p.X, p.Y, false);
        if (type == BuildableType.Mine)
        {
            var field = world.Fields.Find(fl => fl.X == p.X && fl.Y == p.Y)!;
            field.MineId = e.Id;
            e.FieldId = field.Id;
        }
        return Result.OK;
    }
}
