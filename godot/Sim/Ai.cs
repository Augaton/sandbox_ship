// Battle AI: economy, production, research and attack waves, adapting to what it sees of the
// enemy. Port of src/sim/ai.ts. Sorts are LINQ's, stable like JavaScript's.
using SandboxShip.Sim.Data;
using static SandboxShip.Sim.Structures;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim;

public static class Ai
{
    const int MaxMines = 4;
    /// <summary>Biggest wave the AI waits for, in ship value (9000 in the web version: games are longer here).</summary>
    const double MaxWave = 12000;
    /// <summary>Seconds after which the AI attacks with what it has.</summary>
    const double PressureTime = 1200;
    // Past its personality's AllInTime (1800 s for Balanced) the AI goes all in (not in the web
    // version): once its army is worth a wave, every warship goes, home fleet included, and none
    // is called back home. Its ships' own good sense (focus fire, falling back to mend, keeping
    // their distance) made a home fleet under its station's guns too much for the small waves it
    // sent one after the other: two AIs then traded ships for an hour without a station ever
    // scratched. A whole army against a whole army decides, and what is left of the winner finds
    // a station with no fleet.
    /// <summary>Shares of the wave the AI waits for: for the whole army to go, and for reinforcements to join a wave already out.</summary>
    const double AllInShare = 0.6, ReinforceShare = 0.3;
    /// <summary>
    /// Before that, an edge over what was seen sends a wave of at least this share of the wave the
    /// AI waits for, not a handful of ships (1500 credits in the web version): handfuls fed the
    /// tougher hulls of the Nairan (108 games: Nairan 68 %, Kla'ed 36 %; 47 % and 56 % with it).
    /// The attackers of an assault keep the handfuls: they have the clock against them.
    /// </summary>
    const double EdgeShare = 0.5;

    static double EdgeWave(World world, FactionState f) =>
        world.Mode.Kind == BattleMode.Assault && f.Team == Assault.Attackers ? 1500 : Math.Max(1500, f.AI!.WaveValue * EdgeShare);
    /// <summary>Refused for want of a ship on the spot: the AI sends one.</summary>
    const string Unheld = "Un vaisseau doit tenir la position";
    const string NoCredits = "Crédits insuffisants";
    const string EscortOnTheWay = "Escorte en route";

    /// <summary>Relative odds of each ship class once unlocked, before adapting to the enemy.</summary>
    static readonly Dictionary<ShipClass, double> ShipWeights = new()
    {
        [ShipClass.Scout] = 1, [ShipClass.Fighter] = 1, [ShipClass.Bomber] = 1, [ShipClass.Support] = 1, [ShipClass.Frigate] = 4, [ShipClass.Torpedo] = 2,
        [ShipClass.Battlecruiser] = 3, [ShipClass.Bulwark] = 1, [ShipClass.Carrier] = 1, [ShipClass.Dreadnought] = 2,
        [ShipClass.Titan] = 1, [ShipClass.Leviathan] = 1, [ShipClass.Colossus] = 0.5,
    };

    /// <summary>Goal failures worth waiting through: the AI keeps the goal instead of picking another.</summary>
    static readonly HashSet<string> Wait = [NoCredits, EscortOnTheWay];

    static readonly TechId[] TechOrder =
    [
        TechId.Weapons, TechId.Armor, TechId.Shields, TechId.Engines, TechId.Mining, TechId.Hangars,
        TechId.Logistics, TechId.Fortification, TechId.Repair, TechId.Energy,
    ];

    static int Count(World world, FactionState f, string type)
    {
        var n = 0;
        foreach (var e in world.Entities) if (e.Side == f.Id && e.Type == type) n++;
        return n;
    }

    /// <summary>The nearest station of a foe: allies' stations are none of its business.</summary>
    static Entity? NearestEnemyStation(World world, FactionState f, Entity from)
    {
        Entity? best = null;
        foreach (var other in world.Factions.Values)
        {
            if (!Foes(world, f.Id, other.Id)) continue;
            var s = StationOf(world, other.Id);
            if (s is not null && (best is null || Dist(from, s) < Dist(from, best))) best = s;
        }
        return best;
    }

    /// <summary>Warships that answer to the AI: built ones, not hangar craft, not those falling back.</summary>
    internal static List<Entity> Warships(World world, FactionState f)
    {
        var out_ = new List<Entity>();
        foreach (var e in world.Entities)
        {
            if (e.Side == f.Id && e.Kind == EntityKind.Ship && !e.Launched && !e.Retreating && !e.Mending && e.Pilot is null) out_.Add(e);
        }
        return out_;
    }

    static double ShipValue(Entity e) => Ships.UnitValue(e.Cls) * (e.Hero is not null ? 4 : 1);

    /// <summary>What its foes field, from what the AI sees now; older sightings fade. Allies are no threat.</summary>
    static Intel UpdateIntel(World world, FactionState f)
    {
        var seen = new Intel();
        foreach (var e in world.Entities)
        {
            if (!Foes(world, f.Id, e.Side) || !Vision.IsVisible(world, f.Id, e)) continue;
            if (e.Kind == EntityKind.Ship && !e.Launched)
            {
                if (e.Size == SizeClass.Small) seen.Small++;
                else if (e.Size == SizeClass.Medium) seen.Medium++;
                else seen.Large++;
                seen.Army += ShipValue(e);
            }
            else if (e.Kind == EntityKind.Ship)
            {
                seen.Small++;
            }
            else if (e.Kind == EntityKind.Structure && e.Weapons.Count > 0)
            {
                seen.Structures++;
            }
        }
        var old = f.AI!.Intel;
        old.Small = Math.Max(seen.Small, old.Small * 0.97);
        old.Medium = Math.Max(seen.Medium, old.Medium * 0.97);
        old.Large = Math.Max(seen.Large, old.Large * 0.97);
        old.Structures = Math.Max(seen.Structures, old.Structures * 0.97);
        old.Army = Math.Max(seen.Army, old.Army * 0.97);
        return old;
    }

    /// <summary>Ship weights tilted towards what counters the enemy the AI has seen.</summary>
    public static ShipClass? PickShip(World world, FactionState f, Entity station)
    {
        if (!HasHardpoint(world, station.Id, HardpointRole.Shipyard)) return null;
        var intel = f.AI!.Intel;
        var weights = new Dictionary<ShipClass, double>(ShipWeights);
        // Swarms of small craft: flak frigates. Big hulls: torpedoes and bombers. Turrets: bombers.
        if (intel.Small >= 6) weights[ShipClass.Frigate] += 4;
        if (intel.Large >= 2)
        {
            weights[ShipClass.Torpedo] += 4;
            weights[ShipClass.Bomber] += 3;
        }
        if (intel.Structures >= 3) weights[ShipClass.Bomber] += 3;
        // Its personality leans to small or big hulls (× 1 for Balanced).
        var def = Personalities.Defs[f.AI.Personality];
        foreach (var c in Ships.All) if (weights.TryGetValue(c, out var w)) weights[c] = w * def.Lean(Ships.Defs[c].Size);
        // A limited capital ship only with a fleet around it and a quarter of it paid: saving up
        // for it from nothing would leave the AI without ships for minutes.
        var fleet = Warships(world, f).Count;
        var options = Ships.All.Where(c => weights.ContainsKey(c) && station.Level >= Ships.Defs[c].MinStationLevel
            && (Ships.Defs[c].Limit is not int limit
                || (fleet >= 8 && f.Credits >= Ships.Cost(c, f.Race) / 4 && Structures.InService(world, f.Id, c) < limit))).ToList();
        var total = options.Sum(c => weights[c]);
        var r = Rand(world) * total;
        foreach (var c in options)
        {
            r -= weights[c];
            if (r < 0) return c;
        }
        return null;
    }

    /// <summary>Fields the AI could mine now: near its station, or with an idle ship to send there.</summary>
    static bool MineableField(World world, FactionState f)
    {
        var free = world.Fields.Where(fl => fl.MineId is null).ToList();
        var zones = MineZones(world, f);
        if (free.Exists(fl => InZones(zones, fl))) return true;
        return free.Count > 0 && Warships(world, f).Exists(e => e.Order is null);
    }

    /// <summary>Where the AI may build mines: worked out once for all the fields, each time a walk over every entity.</summary>
    static List<Circle> MineZones(World world, FactionState f) => BuildZones(world, f.Id, BuildableType.Mine);

    static bool InZones(List<Circle> zones, AsteroidField fl) => zones.Exists(z => Dist(z, fl) <= z.R + fl.Radius);

    /// <summary>Free fields beyond the build areas: where an outpost would open new ground.</summary>
    static List<AsteroidField> FarFields(World world, FactionState f, Entity station)
    {
        var zones = MineZones(world, f);
        return world.Fields.Where(fl => fl.MineId is null && !InZones(zones, fl)).OrderBy(fl => Dist(fl, station)).ToList();
    }

    /// <summary>Sends the nearest idle warship to hold a spot, unless one is on its way already.</summary>
    static Result EscortTo(World world, FactionState f, IPoint at, double near)
    {
        var ships = Warships(world, f);
        var heading = ships.Exists(e => e.Order is { } o && o.Type != OrderType.Attack && Hypot(o.X - at.X, o.Y - at.Y) < near);
        if (heading) return Result.Fail(EscortOnTheWay);
        var spot = new Point(at.X, at.Y);
        var escort = ships.Where(e => e.Order is null && !Modes.Kept(world, e)).OrderBy(e => Dist(e, spot)).FirstOrDefault();
        if (escort is null) return Result.Fail("Aucune escorte");
        // It holds the spot rather than chase what passes by (not in the web version).
        escort.Stance = Stance.Hold;
        Movement.IssueMove(world, [escort.Id], at.X, at.Y);
        return Result.Fail(EscortOnTheWay);
    }

    static AIGoal? ChooseGoal(World world, FactionState f, Entity station, bool defensive)
    {
        var def = Personalities.Defs[f.AI!.Personality];
        var mines = Count(world, f, "mine");
        if (mines < Math.Min(MaxMines, station.Level + 1) && MineableField(world, f)) return new AIGoal(GoalKind.Mine);
        // A first squadron early: it escorts the miners and scouts the map.
        var fleet = Warships(world, f).Count;
        if (fleet == 0 && HasHardpoint(world, station.Id, HardpointRole.Shipyard)) return new AIGoal(GoalKind.Ship, Cls: ShipClass.Scout);
        // The technological AI puts its lab before anything else, by its research odds: always
        // was too much (13 games won of 16 against the others).
        if (f.AI.Personality == Personality.Technological && f.Researching is null && Structures.HasLab(world, f.Id) && Rand(world) < def.ResearchOdds
            && PickTech(f, station) is TechId first) return new AIGoal(GoalKind.Research, Tech: first);
        // Outmatched: dig in before anything else.
        var room = Structures.PlatformRoom(world, f.Id) > 0;
        if (defensive && room && Count(world, f, "platform") < station.Level * Math.Max(2, def.Platforms)) return new AIGoal(GoalKind.Platform);
        // Upgrades are dear: savings put aside build up to one while the rest keeps the fleet
        // growing (not in the web version, where they were cheap).
        if (CanSave(world, f, station) && f.Credits >= Station.Levels[station.Level].UpgradeCost) return new AIGoal(GoalKind.Upgrade);
        if (room && Count(world, f, "platform") < station.Level * def.Platforms && Rand(world) < def.PlatformOdds) return new AIGoal(GoalKind.Platform);
        // Mid game, expand: an outpost by a free field beyond reach, once the mines nearby are taken.
        if (station.Level >= 2 && mines >= 3 && Count(world, f, "outpost") < def.Outposts && fleet >= 2 && Rand(world) < def.OutpostOdds && FarFields(world, f, station).Count > 0)
            return new AIGoal(GoalKind.Outpost);
        if (fleet >= 3 && Count(world, f, "repairDock") == 0 && Rand(world) < 0.1) return new AIGoal(GoalKind.Structure, BuildableType.RepairDock);
        if (Count(world, f, "lab") == 0 && Rand(world) < def.LabOdds) return new AIGoal(GoalKind.Structure, BuildableType.Lab);
        if (station.Level >= 3 && Count(world, f, "ionCannon") == 0 && Rand(world) < 0.15) return new AIGoal(GoalKind.Structure, BuildableType.IonCannon);
        // The endgame weapon, once most of its price is there: the rest is saved up while waiting.
        if (station.Level >= 4 && Count(world, f, "starLance") == 0 && fleet >= def.LanceFleet && f.Credits >= Buildables.Defs[BuildableType.StarLance].Cost * 0.8
            && Rand(world) < def.LanceOdds) return new AIGoal(GoalKind.Structure, BuildableType.StarLance);
        var tech = f.Researching is null && Structures.HasLab(world, f.Id) && Rand(world) < def.ResearchOdds ? PickTech(f, station) : null;
        if (tech is TechId t) return new AIGoal(GoalKind.Research, Tech: t);
        // Now and then, a hero: whichever is available, saving up for it if needed.
        var hero = Heroes.Of(f.Race).FirstOrDefault(h =>
        {
            var r = CanBuildHero(world, f.Id, h.Id);
            return r.Ok || r.Reason == NoCredits;
        });
        if (hero is not null && Rand(world) < 0.12) return new AIGoal(GoalKind.Hero, Hero: hero.Id);
        return PickShip(world, f, station) is ShipClass cls ? new AIGoal(GoalKind.Ship, Cls: cls) : null;
    }

    /// <summary>Nearest free field it may mine; far fields get an escort sent first.</summary>
    static Result PlaceMine(World world, FactionState f, Entity station)
    {
        var free = world.Fields.Where(fl => fl.MineId is null).OrderBy(fl => Dist(fl, station)).Take(3).ToList();
        foreach (var field in free)
        {
            var r = PlaceStructure(world, f.Id, BuildableType.Mine, field.X, field.Y);
            if (r.Ok || r.Reason == NoCredits) return r;
            if (r.Reason != Unheld) continue;
            var sent = EscortTo(world, f, field, field.Radius);
            if (sent.Ok || sent.Reason == EscortOnTheWay) return sent;
        }
        return Result.Fail("Aucun champ accessible");
    }

    /// <summary>An outpost on the station's side of the nearest far field, which then lies in its zone.</summary>
    static Result PlaceOutpost(World world, FactionState f, Entity station)
    {
        var field = FarFields(world, f, station).FirstOrDefault();
        if (field is null) return Result.Fail("Aucun champ accessible");
        var d = Dist(field, station);
        if (d == 0) d = 1;
        var back = field.Radius + 90;
        foreach (var turn in new[] { 0, 0.5, -0.5 })
        {
            var a = Math.Atan2(station.Y - field.Y, station.X - field.X) + turn;
            var at = new Point(field.X + Math.Cos(a) * back, field.Y + Math.Sin(a) * back);
            if (d < back) break;
            var r = PlaceStructure(world, f.Id, BuildableType.Outpost, at.X, at.Y);
            if (r.Ok || r.Reason == NoCredits) return r;
            if (r.Reason == Unheld) return EscortTo(world, f, at, 150);
        }
        return Result.Fail("Aucun emplacement");
    }

    /// <summary>Least researched tech the station allows, combat techs first.</summary>
    static TechId? PickTech(FactionState f, Entity station)
    {
        // The defensive AI's defenses come first.
        var order = f.AI!.Personality == Personality.Defensive ? TechOrder.OrderBy(t => t != TechId.Fortification) : TechOrder.AsEnumerable();
        var open = order.Where(t => f.Research[t] < Techs.Defs[t].MaxLevel && station.Level >= Techs.StationFor(f.Research[t] + 1)).OrderBy(t => f.Research[t]);
        foreach (var t in open) return t;
        return null;
    }

    /// <summary>Defensive structures go on the side of the station facing the enemy.</summary>
    static Result PlaceDefense(World world, FactionState f, Entity station, Entity? toward, BuildableType type)
    {
        var bearing = toward is not null ? Math.Atan2(toward.Y - station.Y, toward.X - station.X) : 0;
        // A star lance is no defense: it goes behind the station, away from the enemy.
        if (type == BuildableType.StarLance) bearing += Math.PI;
        var r = Result.Fail("Aucun emplacement");
        for (var i = 0; i < 6 && !r.Ok; i++)
        {
            var a = bearing + (Rand(world) - 0.5) * Math.PI;
            var d = Station.Radius + 150 + Rand(world) * 300;
            r = PlaceStructure(world, f.Id, type, station.X + Math.Cos(a) * d, station.Y + Math.Sin(a) * d);
            if (!r.Ok && r.Reason == NoCredits) break;
        }
        return r;
    }

    /// <summary>Share of its income the AI sets aside for the next station level.</summary>
    /// <summary>Whether the next station level is the one to save for: mines up, none queued yet.</summary>
    static bool CanSave(World world, FactionState f, Entity station) =>
        station.Level < Station.Levels.Count && Count(world, f, "mine") >= Math.Min(MaxMines, station.Level + 1)
        && !f.BuildQueue.Exists(q => q.Kind == QueueKind.Upgrade);

    /// <summary>
    /// Sets aside part of what came in since the last thought, up to the price of the next
    /// level. Short of mines, it waits to have them back; outmatched, it may draw on the
    /// savings for its defense. Either way what is saved stays saved.
    /// </summary>
    static void Save(World world, FactionState f, Entity station, bool defensive, double interval)
    {
        var ai = f.AI!;
        ai.Guarded = !defensive;
        ai.Saved = Math.Min(ai.Saved, f.Credits);
        if (station.Level >= Station.Levels.Count || f.BuildQueue.Exists(q => q.Kind == QueueKind.Upgrade))
        {
            ai.Saved = 0;
            return;
        }
        if (defensive || !CanSave(world, f, station)) return;
        var price = Station.Levels[station.Level].UpgradeCost;
        ai.Saved = Math.Min(Math.Min(price, f.Credits), ai.Saved + Structures.IncomeRate(world, f.Id) * Personalities.Defs[ai.Personality].SaveShare * interval);
    }

    /// <summary>Tries to buy the goal. Returns true when the goal is done or can no longer be reached.</summary>
    static bool Pursue(World world, FactionState f, Entity station, AIGoal goal, Entity? enemy)
    {
        if ((goal.Kind == GoalKind.Ship || goal.Kind == GoalKind.Hero) && f.Queue.Count >= 2) return false;
        if (goal.Kind == GoalKind.Upgrade)
        {
            var up = EnqueueUpgrade(world, f.Id);
            if (up.Ok) f.AI!.Saved = 0;
            return up.Ok || !Wait.Contains(up.Reason);
        }
        // Everything else is bought with what is not set aside: the savings are out of sight
        // while it tries, and what it spends comes out of the rest.
        var saved = f.AI!.Guarded ? f.AI.Saved : 0;
        f.Credits -= saved;
        var r = goal.Kind switch
        {
            GoalKind.Mine => PlaceMine(world, f, station),
            GoalKind.Outpost => PlaceOutpost(world, f, station),
            GoalKind.Platform => PlaceDefense(world, f, station, enemy, BuildableType.Platform),
            GoalKind.Structure => PlaceDefense(world, f, station, enemy, goal.Structure),
            GoalKind.Research => StartResearch(world, f.Id, goal.Tech),
            GoalKind.Hero => EnqueueHero(world, f.Id, goal.Hero),
            _ => EnqueueShip(world, f.Id, goal.Cls),
        };
        f.Credits += saved;
        return r.Ok || !Wait.Contains(r.Reason);
    }

    /// <summary>Badly damaged warships fall back to a repair dock, and rejoin once mended.</summary>
    static void RetreatDamaged(World world, FactionState f)
    {
        var docks = world.Entities.Where(e => e.Side == f.Id && e.Type == "repairDock" && e.Build >= 1).ToList();
        var mends = docks.Count > 0 || Factions.Trait(f.Race).HullRegen > 0;
        // Ships on a wave hold on longer, and late in the game not at all: a wave that keeps
        // breaking off never takes a station.
        var waveLimit = world.Time > PressureTime / 2 ? 0 : 0.15;
        foreach (var e in world.Entities)
        {
            if (e.Side != f.Id || e.Kind != EntityKind.Ship || e.Launched || e.Size == SizeClass.Small || e.Pilot is not null) continue;
            var limit = e.Order?.Type == OrderType.AttackMove ? waveLimit : 0.3;
            if (e.Retreating && e.Hull >= e.MaxHull * 0.85)
            {
                e.Retreating = false;
                e.Order = null;
            }
            else if (!e.Retreating && mends && e.Hull < e.MaxHull * limit)
            {
                e.Retreating = true;
                docks = docks.OrderBy(a => Dist(a, e)).ToList();
                var to = docks.FirstOrDefault() ?? StationOf(world, f.Id);
                if (to is not null) Movement.IssueMove(world, [e.Id], to.X, to.Y);
            }
        }
    }

    /// <summary>
    /// Defends home when a foe comes near; otherwise sends idle warships at the nearest enemy station
    /// once the wave is big enough, or right away when clearly stronger than what it has seen.
    /// Returns whether it is outmatched.
    /// </summary>
    static bool Command(World world, FactionState f, Entity station, Entity? enemy)
    {
        var ships = Warships(world, f);
        var idle = ships.Where(e => e.Order is null && !Modes.Kept(world, e)).ToList();
        var threatened = false;
        foreach (var e in world.Entities)
        {
            if (e.Kind == EntityKind.Ship && Dist(e, station) < Station.ConstructionRadius && Foes(world, f.Id, e.Side) && Vision.IsVisible(world, f.Id, e))
            {
                threatened = true;
                break;
            }
        }
        var intel = UpdateIntel(world, f);
        var army = ships.Sum(ShipValue);
        var outmatched = intel.Army > 0 && army < intel.Army * 0.7;
        var def = Personalities.Defs[f.AI!.Personality];
        if (world.Time >= def.AllInTime && enemy is not null)
        {
            // The whole army once nothing is out; while a wave is out, reinforcements by the
            // group, not one by one as they come out of the shipyard.
            var home = ships.Where(e => e.Order?.Type != OrderType.AttackMove && !Modes.Kept(world, e)).ToList();
            var out_ = home.Count < ships.Count;
            var needed = f.AI.WaveValue * (out_ ? ReinforceShare : AllInShare);
            if (home.Count > 0 && home.Sum(ShipValue) >= needed) Send(world, home, enemy, Stance.Aggressive);
            return outmatched;
        }
        if (threatened)
        {
            // Ships already out on a wave keep going; only the home fleet defends, and comes back.
            Send(world, idle, station, Stance.Defensive);
            f.AI.LastThreat = world.Time;
            return outmatched;
        }
        // An assault just beaten off: a defensive AI strikes back while the attacker is thin,
        // if what it holds outweighs what it saw of the enemy.
        if (enemy is not null && def.Counter > 0 && f.AI.LastThreat >= 0 && world.Time - f.AI.LastThreat < def.Counter
            && idle.Count > 0 && army > intel.Army * 1.1 && idle.Sum(ShipValue) >= EdgeWave(world, f))
        {
            Send(world, idle, enemy, Stance.Aggressive);
            f.AI.LastThreat = -1;
            return outmatched;
        }
        var value = idle.Sum(ShipValue);
        // As the game drags on, the AI settles for smaller waves and a thinner edge: no endless standoff.
        var pressure = Math.Min(1, world.Time / PressureTime);
        var stronger = intel.Army > 0 && army > intel.Army * (1.4 - 0.3 * pressure) && value >= EdgeWave(world, f);
        if (enemy is not null && !outmatched && (value >= f.AI.WaveValue * (1 - 0.4 * pressure) || stronger))
        {
            Send(world, idle, enemy, Stance.Aggressive);
            f.AI.WaveValue = Math.Min(f.AI.WaveValue * def.WaveGrowth, MaxWave);
        }
        // Between waves, the fleet at home waits in a nebula near the station, if there is one:
        // out of the enemy's sight until it comes close (Terrain.cs). Shields come back slowly
        // there: a ship goes once they are up. One holding a spot for a mine stays there.
        else if (world.Terrain.Count > 0 && Hideout(world, station) is { } hide)
        {
            var exposed = idle.Where(e => e.Stance != Stance.Hold && !Terrains.InNebula(world, e.X, e.Y) && e.Shield >= e.MaxShield * 0.9).Select(e => e.Id).ToList();
            if (exposed.Count > 0) Movement.IssueMove(world, exposed, hide.X, hide.Y);
        }
        return outmatched;
    }

    /// <summary>
    /// Sends ships at a station on attack-move (not in the web version: the stance): a wave goes
    /// for what it sees and stays on it, the home fleet fights back and comes back.
    /// </summary>
    internal static void Send(World world, List<Entity> ships, Entity at, Stance stance)
    {
        foreach (var e in ships) e.Stance = stance;
        Movement.IssueMove(world, ships.Select(e => e.Id), at.X, at.Y, true);
    }

    /// <summary>A nebula this close to the station hides its fleet; the fleet waits on the station's side of it.</summary>
    const double HideoutReach = 1800;

    static Point? Hideout(World world, Entity station)
    {
        TerrainPatch? best = null;
        foreach (var p in world.Terrain)
        {
            if (p.Kind != TerrainKind.Nebula) continue;
            var d = Dist(p, station);
            if (d < HideoutReach && (best is null || d < Dist(best, station))) best = p;
        }
        if (best is null) return null;
        var reach = Dist(best, station);
        if (reach < 1) return new Point(best.X, best.Y);
        var deep = best.Radius * 0.35;
        return new Point(best.X + (station.X - best.X) / reach * deep, best.Y + (station.Y - best.Y) / reach * deep);
    }

    static void Think(World world, FactionState f)
    {
        var ai = f.AI!;
        var station = StationOf(world, f.Id);
        if (station is null) return;
        var enemy = NearestEnemyStation(world, f, station);
        RetreatDamaged(world, f);
        var defensive = Command(world, f, station, Modes.WaveTarget(world, f, enemy));
        Save(world, f, station, defensive, Difficulties[ai.Difficulty].Think);
        ai.Goal ??= ChooseGoal(world, f, station, defensive);
        if (ai.Goal is not null && Pursue(world, f, station, ai.Goal, enemy)) ai.Goal = null;
    }

    public static void UpdateAI(World world, double dt)
    {
        foreach (var f in world.Factions.Values)
        {
            if (f.AI is null) continue;
            f.AI.Timer -= dt;
            if (f.AI.Timer > 0) continue;
            f.AI.Timer = Difficulties[f.AI.Difficulty].Think;
            Think(world, f);
            Modes.Think(world, f);
        }
    }
}
