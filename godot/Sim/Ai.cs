// Battle AI: economy, production, research and attack waves, adapting to what it sees of the
// enemy. Port of src/sim/ai.ts. Sorts are LINQ's, stable like JavaScript's.
using SandboxShip.Sim.Data;
using static SandboxShip.Sim.Structures;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim;

public static class Ai
{
    /// <summary>Mines a station's level calls for at most, whatever a thriftier personality goes on to work.</summary>
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
    /// <summary>Two ships in the queue already: the next one waits for room, not for credits.</summary>
    const string YardBusy = "Chantier occupé";

    /// <summary>Relative odds of each ship class once unlocked, before adapting to the enemy.</summary>
    static readonly Dictionary<ShipClass, double> ShipWeights = new()
    {
        [ShipClass.Scout] = 1, [ShipClass.Fighter] = 1, [ShipClass.Bomber] = 1, [ShipClass.Support] = 1, [ShipClass.Frigate] = 4, [ShipClass.Torpedo] = 2,
        [ShipClass.Battlecruiser] = 3, [ShipClass.Bulwark] = 1, [ShipClass.Carrier] = 1, [ShipClass.Dreadnought] = 2,
        [ShipClass.Titan] = 1, [ShipClass.Leviathan] = 1, [ShipClass.Colossus] = 0.5,
    };

    /// <summary>Goal failures worth waiting through: the AI keeps the goal instead of picking another.</summary>
    static readonly HashSet<string> Wait = [NoCredits, EscortOnTheWay, YardBusy];

    /// <summary>What a thought may try in a row when goals fail: one that cannot be reached costs no time.</summary>
    const int GoalTries = 3;
    /// <summary>Seconds it leaves a kind of goal alone after failing to reach it, and the longest it waits on one (not a ship: those wait for the yard).</summary>
    const double GiveUpFor = 20, GoalPatience = 120;
    /// <summary>A research or a structure this dear goes on the savings rather than hold up the shipyard.</summary>
    const double DearGoal = 1500;
    /// <summary>Share of its income, what goes to the next station level apart, put aside for a dear purchase.</summary>
    const double BigShare = 0.5;
    /// <summary>A foe this close to one of its structures is a threat: the station's own radius, for all of them.</summary>
    const double GuardRange = Station.ConstructionRadius;
    /// <summary>Far from its station, a threat is answered only by this share of what stands with it: a colossus guarding its corner eats no home fleet.</summary>
    const double AnswerShare = 0.6;
    /// <summary>Hard: what a raid is worth at most, what it takes at least, how often one leaves, how close it goes to a station's guns.</summary>
    const double RaidValue = 1500, RaidMin = 500, RaidEvery = 45, GunReach = 800;
    /// <summary>Hard: a look around for raids, every so often; how far a wave's fight is reckoned; how much stronger the foe must be for the wave to come home, and how long before the next.</summary>
    const double ScoutEvery = 25, FightReach = 900, LosingOdds = 1.6, WaveRest = 45;

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

    /// <summary>
    /// Free for an errand: idle, or on a plain move, to the rally point most often. A big hull
    /// jammed in the crowd there may never quite reach its spot and, waited for as idle, would
    /// sit at home for good. Not one sent to hold a spot, not one its mode keeps.
    /// </summary>
    internal static bool Ready(World world, Entity e) =>
        (e.Order is null || (e.Order.Type == OrderType.Move && e.Stance != Stance.Hold)) && !Modes.Kept(world, e);

    /// <summary>A ship at its price, a hero at its own: as a kill pays (Worlds.BountyOf) and a lance aims (AbilitySystem.Worth).</summary>
    static double ShipValue(Entity e) => e.Hero is HeroId h ? Heroes.Defs[h].Cost : Ships.UnitValue(e.Cls);

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
        var level = Difficulties[f.AI!.Difficulty];
        var weights = new Dictionary<ShipClass, double>(ShipWeights);
        if (level.Adapts) Counter(weights, f.AI.Intel);
        if (level.Sharp) CounterShares(weights, f.AI.Intel);
        // Its personality leans to small or big hulls (× 1 for Balanced).
        var def = Personalities.Defs[f.AI.Personality];
        foreach (var c in Ships.All) if (weights.TryGetValue(c, out var w)) weights[c] = w * def.Lean(Ships.Defs[c].Size);
        // A limited capital ship only with a fleet around it and a quarter of it paid: saving up
        // for it from nothing would leave the AI without ships for minutes.
        var fleet = Warships(world, f).Count;
        var options = Ships.All.Where(c => weights.ContainsKey(c) && station.Level >= Ships.Defs[c].MinStationLevel
            && (Ships.Defs[c].Limit is not int limit
                || (f.AI.Big is null && fleet >= 8 && f.Credits >= Ships.Cost(c, f.Race) / 4 && Structures.InService(world, f.Id, c) < limit))).ToList();
        var total = options.Sum(c => weights[c]);
        var r = Rand(world) * total;
        foreach (var c in options)
        {
            r -= weights[c];
            if (r < 0) return c;
        }
        return null;
    }

    /// <summary>Swarms of small craft: flak frigates. Middle hulls: burst cannons. Big hulls: torpedoes and bombers. Turrets: bombers.</summary>
    static void Counter(Dictionary<ShipClass, double> weights, Intel intel)
    {
        if (intel.Small >= 6) weights[ShipClass.Frigate] += 4;
        if (intel.Medium >= 4) weights[ShipClass.Battlecruiser] += 3;
        if (intel.Large >= 2)
        {
            weights[ShipClass.Torpedo] += 4;
            weights[ShipClass.Bomber] += 3;
        }
        if (intel.Structures >= 3) weights[ShipClass.Bomber] += 3;
    }

    /// <summary>
    /// A hard AI goes by what most of the foe's fleet is, and drops what would be wasted on it:
    /// torpedoes against small craft, light craft against what they barely scratch.
    /// </summary>
    static void CounterShares(Dictionary<ShipClass, double> weights, Intel intel)
    {
        var seen = intel.Small + intel.Medium + intel.Large;
        if (seen < 3) return;
        var (small, medium, large) = (intel.Small / seen, intel.Medium / seen, intel.Large / seen);
        weights[ShipClass.Frigate] += 6 * small;
        weights[ShipClass.Battlecruiser] += 6 * medium + 2 * large;
        weights[ShipClass.Dreadnought] += 4 * medium + 3 * large;
        weights[ShipClass.Torpedo] = (weights[ShipClass.Torpedo] + 6 * large) * (1 - 0.8 * small);
        weights[ShipClass.Fighter] *= 2 * small;
        weights[ShipClass.Scout] *= 0.3;
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
        var ai = f.AI!;
        var def = Personalities.Defs[ai.Personality];
        bool Open(GoalKind kind) => world.Time >= ai.GaveUp.GetValueOrDefault(kind);
        var mines = Count(world, f, "mine");
        if (mines < MineQuota(def, station) && Open(GoalKind.Mine) && MineableField(world, f)) return new AIGoal(GoalKind.Mine);
        // A first squadron early: it escorts the miners and scouts the map.
        var fleet = Warships(world, f).Count;
        if (fleet == 0 && HasHardpoint(world, station.Id, HardpointRole.Shipyard)) return new AIGoal(GoalKind.Ship, Cls: ShipClass.Scout);
        // The technological AI puts its lab before anything else, by its research odds: always
        // was too much (13 games won of 16 against the others).
        var researches = f.Researching is null && ai.Big?.Kind != GoalKind.Research && Open(GoalKind.Research) && Structures.HasLab(world, f.Id);
        var researchOdds = def.ResearchOdds * Difficulties[ai.Difficulty].Research;
        if (ai.Personality == Personality.Technological && researches && Rand(world) < researchOdds
            && PickTech(f, station) is TechId first) return new AIGoal(GoalKind.Research, Tech: first);
        // Outmatched: dig in before anything else, around the station, what the enemy comes for.
        var platforms = Count(world, f, "platform");
        var placing = Open(GoalKind.Platform);
        if (defensive && placing && StationRoom(world, f, station) > 0 && platforms < station.Level * Math.Max(2, def.Platforms)) return new AIGoal(GoalKind.Platform);
        // Upgrades are dear: savings put aside build up to one while the rest keeps the fleet
        // growing (not in the web version, where they were cheap).
        if (CanSave(world, f, station) && f.Credits - (ai.Guarded ? ai.BigSaved : 0) >= Station.Levels[station.Level].UpgradeCost) return new AIGoal(GoalKind.Upgrade);
        var room = Structures.PlatformRoom(world, f.Id) > 0;
        var wanted = station.Level * def.Platforms;
        if (placing && room && platforms < wanted && Rand(world) < def.PlatformOdds) return new AIGoal(GoalKind.Platform);
        // Every place it wants taken, a level up for one that stands; digging in, whatever its personality.
        var tierOdds = defensive ? Math.Max(def.TierOdds, 0.25) : def.TierOdds;
        if (tierOdds > 0 && Open(GoalKind.Tier) && (!room || platforms >= wanted) && NextTier(world, f, station) is { } tier && Rand(world) < tierOdds)
            return new AIGoal(GoalKind.Tier, Target: tier.Id);
        // Mid game, expand: an outpost by a free field beyond reach, once the mines nearby are
        // taken, while it has a use for one more.
        if (station.Level >= 2 && mines >= 3 && mines < def.Mines && Open(GoalKind.Outpost) && Count(world, f, "outpost") < def.Outposts && fleet >= 2 && Rand(world) < def.OutpostOdds && FarFields(world, f, station).Count > 0)
            return new AIGoal(GoalKind.Outpost);
        if (Open(GoalKind.Structure))
        {
            if (fleet >= 3 && Count(world, f, "repairDock") == 0 && Rand(world) < 0.1) return new AIGoal(GoalKind.Structure, BuildableType.RepairDock);
            if (Count(world, f, "lab") == 0 && Rand(world) < def.LabOdds) return new AIGoal(GoalKind.Structure, BuildableType.Lab);
            if (station.Level >= 3 && Count(world, f, "ionCannon") == 0 && Rand(world) < 0.15) return new AIGoal(GoalKind.Structure, BuildableType.IonCannon);
            // The endgame weapon, once most of its price is there: the rest is saved up while waiting.
            if (station.Level >= 4 && Count(world, f, "starLance") == 0 && fleet >= def.LanceFleet && f.Credits >= Buildables.Defs[BuildableType.StarLance].Cost * 0.8
                && Rand(world) < def.LanceOdds) return new AIGoal(GoalKind.Structure, BuildableType.StarLance);
        }
        var tech = researches && Rand(world) < researchOdds ? PickTech(f, station) : null;
        if (tech is TechId t) return new AIGoal(GoalKind.Research, Tech: t);
        // Now and then, a hero: whichever is available. Too dear for now, it is saved up for
        // while the shipyard goes on working.
        var hero = Heroes.Of(f.Race).FirstOrDefault(h =>
        {
            var r = CanBuildHero(world, f.Id, h.Id);
            return r.Ok || r.Reason == NoCredits;
        });
        if (hero is not null && Rand(world) < 0.12)
        {
            var goal = new AIGoal(GoalKind.Hero, Hero: hero.Id);
            if (Free(f) >= hero.Cost) return goal;
            ai.Big ??= goal;
        }
        if (PickShip(world, f, station) is not ShipClass cls) return null;
        if (Ships.Defs[cls].Limit is null || Free(f) >= Ships.Cost(cls, f.Race)) return new AIGoal(GoalKind.Ship, Cls: cls);
        // So with a capital ship: once one is being saved for, PickShip offers none.
        ai.Big ??= new AIGoal(GoalKind.Ship, Cls: cls);
        return PickShip(world, f, station) is ShipClass other ? new AIGoal(GoalKind.Ship, Cls: other) : null;
    }

    /// <summary>Mines it wants at this station level: one more than the level, a step further for a personality that wants more than most.</summary>
    static int MineQuota(PersonalityDef def, Entity station) => Math.Min(def.Mines, station.Level + (def.Mines > MaxMines ? 2 : 1));

    /// <summary>What it may spend: its savings are out of sight, unless it is outmatched.</summary>
    static double Free(FactionState f) => f.Credits - (f.AI!.Guarded ? f.AI.Saved + f.AI.BigSaved : 0);

    /// <summary>Room for platforms around the station itself.</summary>
    static int StationRoom(World world, FactionState f, Entity station) =>
        Structures.RoomIn(world, f.Id, new Circle(station.X, station.Y, Station.ConstructionRadius));

    /// <summary>The platform to take a level up: one at a time, the lowest first, and no higher than the station's level.</summary>
    static Entity? NextTier(World world, FactionState f, Entity station)
    {
        if (f.BuildQueue.Exists(q => q.Kind == QueueKind.Tier)) return null;
        Entity? best = null;
        foreach (var e in world.Entities)
        {
            if (e.Side == f.Id && e.Type == "platform" && e.Build >= 1 && e.Level < station.Level && e.Level <= Buildables.Defs[BuildableType.Platform].Tiers!.Count
                && (best is null || e.Level < best.Level)) best = e;
        }
        return best;
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

    /// <summary>
    /// Defensive structures go on the side of the station facing the enemy. A platform with no
    /// room left there goes to the other build areas, where PlatformRoom found some, the nearest
    /// the enemy first.
    /// </summary>
    static Result PlaceDefense(World world, FactionState f, Entity station, Entity? toward, BuildableType type)
    {
        if (type == BuildableType.Platform && StationRoom(world, f, station) == 0) return PlacePlatformAway(world, f, toward ?? station);
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

    static Result PlacePlatformAway(World world, FactionState f, Entity toward)
    {
        var r = Result.Fail("Aucun emplacement");
        var zones = BuildZones(world, f.Id, BuildableType.Platform).Where(z => z.Area != BuildArea.Station && RoomIn(world, f.Id, z) > 0).OrderBy(z => Dist(z, toward));
        foreach (var z in zones)
        {
            var bearing = Math.Atan2(toward.Y - z.Y, toward.X - z.X);
            for (var i = 0; i < 4; i++)
            {
                var a = bearing + (Rand(world) - 0.5) * Math.PI;
                var d = 80 + Rand(world) * z.R * 0.6;
                r = PlaceStructure(world, f.Id, BuildableType.Platform, z.X + Math.Cos(a) * d, z.Y + Math.Sin(a) * d);
                if (r.Ok || r.Reason == NoCredits) return r;
            }
        }
        return r;
    }

    /// <summary>Whether the next station level is the one to save for: mines up, none queued yet. A thrifty AI does not wait for its extra mines.</summary>
    static bool CanSave(World world, FactionState f, Entity station) =>
        station.Level < Station.Levels.Count
        && Count(world, f, "mine") >= Math.Min(Math.Min(Personalities.Defs[f.AI!.Personality].Mines, MaxMines), station.Level + 1)
        && !f.BuildQueue.Exists(q => q.Kind == QueueKind.Upgrade);

    /// <summary>What a dear purchase costs.</summary>
    static double Price(FactionState f, AIGoal goal) => goal.Kind switch
    {
        GoalKind.Hero => Heroes.Defs[goal.Hero].Cost,
        GoalKind.Ship => Ships.Cost(goal.Cls, f.Race),
        GoalKind.Structure => Buildables.Defs[goal.Structure].Cost,
        GoalKind.Research => Techs.Defs[goal.Tech].Costs[Math.Min(ResearchQueue.PlannedLevel(f, goal.Tech), Techs.Defs[goal.Tech].MaxLevel - 1)],
        _ => 0,
    };

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
        var income = Structures.IncomeRate(world, f.Id) * interval;
        if (station.Level >= Station.Levels.Count || f.BuildQueue.Exists(q => q.Kind == QueueKind.Upgrade)) ai.Saved = 0;
        else if (!defensive && CanSave(world, f, station))
        {
            var share = income * Personalities.Defs[ai.Personality].SaveShare;
            income -= share;
            ai.Saved = Math.Min(Math.Min(Station.Levels[station.Level].UpgradeCost, f.Credits), ai.Saved + share);
        }
        // The same for a dear purchase, out of what is left.
        ai.BigSaved = Math.Min(ai.BigSaved, f.Credits - ai.Saved);
        if (ai.Big is { } big && !defensive) ai.BigSaved = Math.Min(Math.Min(Price(f, big), f.Credits - ai.Saved), ai.BigSaved + income * BigShare);
    }

    /// <summary>Tries to buy the goal, with what is not set aside: the savings are out of sight while it tries, and what it spends comes out of the rest.</summary>
    static Result Pursue(World world, FactionState f, Entity station, AIGoal goal, Entity? enemy)
    {
        if ((goal.Kind == GoalKind.Ship || goal.Kind == GoalKind.Hero) && f.Queue.Count >= 2) return Result.Fail(YardBusy);
        var ai = f.AI!;
        // A station level comes out of its own savings, and only those of the dear purchase are out of sight.
        var saved = !ai.Guarded ? 0 : goal.Kind == GoalKind.Upgrade ? ai.BigSaved : ai.Saved + ai.BigSaved;
        f.Credits -= saved;
        var r = goal.Kind switch
        {
            GoalKind.Upgrade => EnqueueUpgrade(world, f.Id),
            GoalKind.Mine => PlaceMine(world, f, station),
            GoalKind.Outpost => PlaceOutpost(world, f, station),
            GoalKind.Platform => PlaceDefense(world, f, station, enemy, BuildableType.Platform),
            GoalKind.Tier => UpgradeStructure(world, f.Id, goal.Target),
            GoalKind.Structure => PlaceDefense(world, f, station, enemy, goal.Structure),
            GoalKind.Research => StartResearch(world, f.Id, goal.Tech),
            GoalKind.Hero => EnqueueHero(world, f.Id, goal.Hero),
            _ => EnqueueShip(world, f.Id, goal.Cls),
        };
        f.Credits += saved;
        if (goal.Kind == GoalKind.Upgrade && r.Ok) ai.Saved = 0;
        return r;
    }

    /// <summary>The dear purchase, once saved up for, with what is put aside for it; dropped once it can no longer be made.</summary>
    static void PursueBig(World world, FactionState f, Entity station, Entity? enemy)
    {
        var ai = f.AI!;
        if (ai.Big is not { } big) return;
        var saved = ai.BigSaved;
        ai.BigSaved = 0;
        var r = Pursue(world, f, station, big, enemy);
        ai.BigSaved = saved;
        if (!r.Ok && Wait.Contains(r.Reason)) return;
        ai.Big = null;
        ai.BigSaved = 0;
    }

    /// <summary>
    /// Works at its goal, or a new one. A goal out of reach gives way at once to the next, and that
    /// kind is left alone a while: asking again every thought for what cannot be had, a platform
    /// with no room, would leave nothing for the rest. A research or a structure too dear for now
    /// goes on the savings, so does a hero or a capital ship (ChooseGoal): the shipyard goes on working.
    /// </summary>
    static void Work(World world, FactionState f, Entity station, Entity? enemy, bool defensive)
    {
        var ai = f.AI!;
        for (var tries = 0; tries < GoalTries; tries++)
        {
            if (ai.Goal is null)
            {
                ai.Goal = ChooseGoal(world, f, station, defensive);
                ai.GoalAt = world.Time;
            }
            if (ai.Goal is not { } goal) return;
            var r = Pursue(world, f, station, goal, enemy);
            if (r.Ok)
            {
                ai.Goal = null;
                return;
            }
            var dear = r.Reason == NoCredits && goal.Kind is GoalKind.Research or GoalKind.Structure && Price(f, goal) >= DearGoal;
            if (dear && ai.Big is null)
            {
                ai.Big = goal;
                ai.Goal = null;
                continue;
            }
            if (!dear && Wait.Contains(r.Reason) && (goal.Kind is GoalKind.Ship or GoalKind.Hero || world.Time - ai.GoalAt < GoalPatience)) return;
            ai.Goal = null;
            ai.GaveUp[goal.Kind] = world.Time + GiveUpFor;
        }
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
    /// The foe it sees nearest its station among those close to it or, unless it cares only for
    /// its home, to any of its structures.
    /// </summary>
    static Entity? ThreatTo(World world, FactionState f, Entity station, DifficultyDef level)
    {
        // Around each of them, from the grid: every foe ship against every structure, with a
        // closure for each ship, made the battle AI allocate twice what it did.
        Entity? threat = null;
        var nearest = double.PositiveInfinity;
        Watch(world, f, station, station, Station.ConstructionRadius, ref threat, ref nearest);
        if (level.HomeOnly) return threat;
        foreach (var s in world.Entities)
        {
            if (s.Kind == EntityKind.Structure && s.Side == f.Id && s != station) Watch(world, f, station, s, GuardRange, ref threat, ref nearest);
        }
        return threat;
    }

    /// <summary>The foe ship it sees within `reach` of `around` nearest its station, if nearer than `nearest`.</summary>
    static void Watch(World world, FactionState f, Entity station, Entity around, double reach, ref Entity? threat, ref double nearest)
    {
        foreach (var e in world.Grid.Near(around.X, around.Y, reach))
        {
            if (e.Kind != EntityKind.Ship || Dist(around, e) >= reach) continue;
            var d = Dist(e, station);
            // The same distance: the first in the world's order, as a walk over every entity found it.
            if (d > nearest || d == nearest && threat is not null && e.Id > threat.Id) continue;
            if (!Foes(world, f.Id, e.Side) || !Vision.IsVisible(world, f.Id, e)) continue;
            (threat, nearest) = (e, d);
        }
    }

    /// <summary>What the foes it sees around this spot are worth, as they stand now.</summary>
    static double FoesAround(World world, FactionState f, IPoint at, double reach)
    {
        var spot = new Point(at.X, at.Y);
        var sum = 0.0;
        foreach (var e in world.Entities)
        {
            if (e.Kind == EntityKind.Hardpoint || (e.Kind == EntityKind.Ship ? e.Launched : e.Weapons.Count == 0)) continue;
            if (Foes(world, f.Id, e.Side) && Dist(e, spot) < reach && Vision.IsVisible(world, f.Id, e)) sum += Power(e);
        }
        return sum;
    }

    /// <summary>What a ship is worth as it stands, what a gun on a structure is worth to whoever must face it.</summary>
    static double Power(Entity e) =>
        e.Kind == EntityKind.Ship ? ShipValue(e) * (e.Hull + e.Shield) / Math.Max(1, e.MaxHull + e.MaxShield)
        : e.Type == "station" ? 1500 * e.Level
        : Ids.IsBuildable(e.Type, out var b) ? Buildables.Defs[b].Cost * e.Level : 0;

    /// <summary>
    /// Who answers a threat. At home everyone; far from home, a hard AI sends what it takes,
    /// counting those on their way already, the nearest first, and keeps the rest for its waves.
    /// </summary>
    static List<Entity> Answer(World world, FactionState f, Entity station, Entity threat, List<Entity> ships, List<Entity> ready, DifficultyDef level)
    {
        if (!level.Sharp || Dist(threat, station) < Station.ConstructionRadius) return ready;
        // Ships answer a threat on attack-move at the defensive stance; a wave goes at the aggressive one.
        var needed = FoesAround(world, f, threat, 500) * 1.5 - ships.Where(e => e.Order is { Type: OrderType.AttackMove } o && e.Stance == Stance.Defensive
            && Hypot(o.X - threat.X, o.Y - threat.Y) < 600).Sum(ShipValue);
        var sent = new List<Entity>();
        var value = 0.0;
        foreach (var e in ready.OrderBy(e => Dist(e, threat)))
        {
            if (value >= needed) break;
            sent.Add(e);
            value += ShipValue(e);
        }
        return sent;
    }

    /// <summary>Hard: the one ship its whole fleet shoots first: the dearest for what it takes to bring down, among the big ones in its fleet's reach; it stays on it.</summary>
    static void PickFocus(World world, FactionState f, List<Entity> ships)
    {
        var ai = f.AI!;
        Entity? best = null;
        var most = 0.0;
        foreach (var e in world.Entities)
        {
            if (e.Kind != EntityKind.Ship || e.Launched || e.Size == SizeClass.Small || !Foes(world, f.Id, e.Side) || !Vision.IsVisible(world, f.Id, e)) continue;
            if (!ships.Exists(s => Dist(s, e) - e.Radius < s.Range)) continue;
            var worth = ShipValue(e) / Math.Max(1, e.Hull + e.Shield) * (e.Id == ai.FocusId ? 1.5 : 1);
            if (worth > most)
            {
                most = worth;
                best = e;
            }
        }
        ai.FocusId = best?.Id;
    }

    /// <summary>Hard: a wave up against much more than itself comes home to fight another day, while it can. Past the all-in, it goes on; so does an assault, on its clock.</summary>
    static void CallBackLosing(World world, FactionState f, Entity station, double allIn)
    {
        if (world.Time >= allIn || world.Mode.Kind == BattleMode.Assault) return;
        var wave = Warships(world, f).Where(e => e.Order?.Type == OrderType.AttackMove && e.Stance == Stance.Aggressive && Dist(e, station) > Station.ConstructionRadius * 2).ToList();
        if (wave.Count == 0) return;
        var centre = new Point(wave.Average(e => e.X), wave.Average(e => e.Y));
        if (FoesAround(world, f, centre, FightReach) <= wave.Sum(Power) * LosingOdds) return;
        foreach (var e in wave) e.Stance = Stance.Defensive;
        Movement.IssueMove(world, wave.Select(e => e.Id), f.RallyX, f.RallyY);
        f.AI!.NextWave = world.Time + WaveRest;
    }

    /// <summary>
    /// Hard: a handful of light ships off to a structure of the foe's it has seen and that nothing
    /// guards: a mine far from the guns. Returns whether one left.
    /// </summary>
    static bool Raid(World world, FactionState f, List<Entity> ready)
    {
        var ai = f.AI!;
        if (world.Time < ai.NextRaid) return false;
        var light = ready.Where(e => e.Order is null && e.Stance != Stance.Hold && (e.Size == SizeClass.Small || (e.Size == SizeClass.Medium && Ships.Defs[e.Cls].Repair is null))).ToList();
        if (light.Sum(ShipValue) < RaidMin) return false;
        Entity? prey = null;
        foreach (var t in world.Entities)
        {
            if (t.Kind != EntityKind.Structure || t.Type is not ("mine" or "outpost" or "repairDock" or "lab") || t.Race == Faction.Errant || !Foes(world, f.Id, t.Side)) continue;
            if (!Vision.IsVisible(world, f.Id, t) && !Vision.WasSeen(world, f.Id, t)) continue;
            if (prey is not null && Dist(t, light[0]) >= Dist(prey, light[0])) continue;
            if (!Guarded(world, f, t)) prey = t;
        }
        if (prey is null) return false;
        var sent = new List<Entity>();
        var value = 0.0;
        foreach (var e in light.OrderBy(e => Dist(e, prey)))
        {
            if (value >= RaidValue) break;
            sent.Add(e);
            value += ShipValue(e);
        }
        Send(world, sent, prey, Stance.Aggressive);
        ai.NextRaid = world.Time + RaidEvery;
        return true;
    }

    /// <summary>Whether a station's guns, a platform or a fleet it has seen watch over this structure.</summary>
    static bool Guarded(World world, FactionState f, Entity t)
    {
        foreach (var e in world.Entities)
        {
            if (e.Side != t.Side || e == t || e.Kind == EntityKind.Hardpoint) continue;
            var reach = e.Type == "station" ? GunReach : e.Kind == EntityKind.Structure && e.Weapons.Count > 0 ? e.Range + 100 : 0;
            if (reach > 0 && Dist(e, t) < reach && (Vision.IsVisible(world, f.Id, e) || Vision.WasSeen(world, f.Id, e))) return true;
        }
        return FoesAround(world, f, t, 600) > RaidValue * 0.7;
    }

    /// <summary>Hard: a light ship off to a field, every so often: mines are where raids find them.</summary>
    static void LookAround(World world, FactionState f, List<Entity> ready)
    {
        var ai = f.AI!;
        if (world.Time < ai.NextScout || world.Fields.Count == 0) return;
        ai.NextScout = world.Time + ScoutEvery;
        var scout = ready.Find(e => e.Order is null && e.Cls == ShipClass.Scout);
        if (scout is null) return;
        var field = world.Fields[(int)(Rand(world) * world.Fields.Count)];
        scout.Stance = Stance.Passive;
        Movement.IssueMove(world, [scout.Id], field.X, field.Y);
    }

    /// <summary>
    /// Defends what it owns when a foe comes near; otherwise sends warships at the nearest enemy
    /// station once the wave is big enough, or right away when clearly stronger than what it has
    /// seen. Returns whether it is outmatched.
    /// </summary>
    static bool Command(World world, FactionState f, Entity station, Entity? enemy)
    {
        var ai = f.AI!;
        var level = Difficulties[ai.Difficulty];
        var def = Personalities.Defs[ai.Personality];
        var ships = Warships(world, f);
        var ready = ships.Where(e => Ready(world, e)).ToList();
        var threat = ThreatTo(world, f, station, level);
        if (threat is null) ai.ThreatSeen = -1;
        else if (ai.ThreatSeen < 0) ai.ThreatSeen = world.Time;
        var intel = UpdateIntel(world, f);
        var army = ships.Sum(ShipValue);
        // Weighing nothing, it is never outmatched: it neither digs in nor holds back.
        var outmatched = level.WeighsOdds && intel.Army > 0 && army < intel.Army * 0.7;
        if (level.Sharp)
        {
            PickFocus(world, f, ships);
            CallBackLosing(world, f, station, def.AllInTime);
        }
        if (world.Time >= def.AllInTime && enemy is not null)
        {
            // The whole army once nothing is out; while a wave is out, or if it sees little
            // to stop it, reinforcements by the group, not one by one as they come out of the shipyard.
            var home = ships.Where(e => e.Order?.Type != OrderType.AttackMove && !Modes.Kept(world, e)).ToList();
            var out_ = home.Count < ships.Count;
            var needed = ai.WaveValue * (out_ || army > intel.Army * 1.5 ? ReinforceShare : AllInShare);
            if (home.Count > 0 && home.Sum(ShipValue) >= needed) Send(world, home, enemy, Stance.Aggressive);
            return outmatched;
        }
        // A threat it can face, once it has reacted: far from home, not an army that would eat what it has at home.
        if (threat is not null && world.Time - ai.ThreatSeen >= level.Reaction
            && (Dist(threat, station) < Station.ConstructionRadius || ready.Sum(ShipValue) >= FoesAround(world, f, threat, 500) * AnswerShare))
        {
            // Ships already out on a wave keep going; only the home fleet defends, and comes back.
            Send(world, Answer(world, f, station, threat, ships, ready, level), threat, Stance.Defensive);
            ai.LastThreat = world.Time;
            return outmatched;
        }
        var idle = ready.Where(e => e.Order is null).ToList();
        // A raid dealt with, those who answered it come home (in the standard rules: a mode's
        // ships may have business out there).
        if (world.Mode.Kind == BattleMode.Standard)
        {
            var strays = idle.Where(e => e.Stance == Stance.Defensive && e.TargetId is null && Dist(e, station) > Station.ConstructionRadius * 2).Select(e => e.Id).ToList();
            if (strays.Count > 0) Movement.IssueMove(world, strays, f.RallyX, f.RallyY);
        }
        // An assault just beaten off: a defensive AI strikes back while the attacker is thin,
        // if what it holds outweighs what it saw of the enemy.
        var value = ready.Sum(ShipValue);
        if (enemy is not null && def.Counter > 0 && ai.LastThreat >= 0 && world.Time - ai.LastThreat < def.Counter
            && ready.Count > 0 && army > intel.Army * 1.1 && value >= EdgeWave(world, f))
        {
            Send(world, ready, enemy, Stance.Aggressive);
            ai.LastThreat = -1;
            return outmatched;
        }
        // As the game drags on, the AI settles for smaller waves and a thinner edge: no endless standoff.
        var pressure = Math.Min(1, world.Time / PressureTime);
        var stronger = level.WeighsOdds && intel.Army > 0 && army > intel.Army * (1.4 - 0.3 * pressure) && value >= EdgeWave(world, f);
        if (enemy is not null && !outmatched && world.Time >= ai.NextWave && (value >= ai.WaveValue * (1 - 0.4 * pressure) || stronger))
        {
            Send(world, ready, enemy, Stance.Aggressive);
            ai.WaveValue = Math.Min(ai.WaveValue * def.WaveGrowth, MaxWave);
            return outmatched;
        }
        if (level.Sharp && enemy is not null && !outmatched && !Raid(world, f, ready)) LookAround(world, f, ready);
        // Between waves, the fleet at home waits in a nebula near the station, if there is one:
        // out of the enemy's sight until it comes close (Terrain.cs). Shields come back slowly
        // there: a ship goes once they are up. One holding a spot for a mine stays there.
        if (world.Terrain.Count > 0 && Hideout(world, station) is { } hide)
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
        PursueBig(world, f, station, enemy);
        Work(world, f, station, enemy, defensive);
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
