// Galaxy AI: what to build where, and where to send its fleets. Port of src/sim/galaxyAI.ts.
// Beyond the web version: it gathers its fleets at a staging planet to strike together, sends
// help to planets an enemy fleet draws near, keeps to its truces and offers some, claims
// derelicts, and brings worn fleets home from beyond its supply lines. Its difficulty changes
// what it decides as well as its income: the easy AI strikes before it is ready and leaves its
// planets to themselves, the hard one picks its moment and its targets.
using SandboxShip.Sim.Data;
using static SandboxShip.Sim.Galaxies;

namespace SandboxShip.Sim;

public sealed partial class GalaxyAIState
{
    /// <summary>The enemy planet the AI's next offensive strikes, and its own planet next to it where its fleets gather first.</summary>
    public int? Target, Staging;
    /// <summary>Galaxy seconds since the AI last struck an enemy planet: the longer, the smaller the edge it waits for.</summary>
    public double Idle;
    /// <summary>Credits set aside for its next big project (GalaxyAi.Project), out of reach of its other purchases unless a planet is in danger.</summary>
    public double Fund;
    /// <summary>Galaxy seconds before the AI strikes a rival faction's planet again: its fleets regroup and mend between two offensives.</summary>
    public double Regroup;
}

public static partial class GalaxyAi
{
    const double ThinkInterval = 2;
    /// <summary>Galaxy seconds an AI lets pass between two strikes on rival factions' planets (Regroup); the easy AI hardly waits.</summary>
    const double RegroupTime = 90, ClumsyRegroup = 20;
    const string NoCredits = "Crédits insuffisants";


    /// <summary>The easy AI's bad habits: it strikes too soon and alone, sends no help, seeks no deal, builds little and big, ignores specials, what it faces and the star lance.</summary>
    static bool Clumsy(GalaxyAIState ai) => ai.Difficulty == Difficulty.Easy;

    /// <summary>The hard AI's good ones: it researches first, buys what wins for its price, reads the odds, goes for a rival busy elsewhere, and breaks a truce when it pays.</summary>
    static bool Cunning(GalaxyAIState ai) => ai.Difficulty == Difficulty.Hard;

    /// <summary>Relative odds of each ship class once unlocked.</summary>
    static readonly Dictionary<ShipClass, double> ShipWeights = new()
    {
        [ShipClass.Scout] = 1, [ShipClass.Bomber] = 1, [ShipClass.Support] = 1, [ShipClass.Frigate] = 4, [ShipClass.Torpedo] = 2,
        [ShipClass.Battlecruiser] = 3, [ShipClass.Bulwark] = 1, [ShipClass.Carrier] = 1, [ShipClass.Dreadnought] = 2,
        [ShipClass.Titan] = 1, [ShipClass.Leviathan] = 1,
    };

    /// <summary>
    /// The hard AI buys what wins for its price, as fleets of one class against another measure
    /// it: frigates and light craft far ahead, the big hulls only for what they unlock.
    /// </summary>
    static readonly Dictionary<ShipClass, double> ShrewdWeights = new()
    {
        [ShipClass.Scout] = 3, [ShipClass.Bomber] = 2, [ShipClass.Support] = 3, [ShipClass.Frigate] = 8, [ShipClass.Torpedo] = 1,
        [ShipClass.Battlecruiser] = 1, [ShipClass.Carrier] = 2, [ShipClass.Dreadnought] = 1,
    };

    /// <summary>The easy AI goes for what looks big: heavy hulls that do little for their price.</summary>
    static readonly Dictionary<ShipClass, double> ShowyWeights = new()
    {
        [ShipClass.Scout] = 0.5, [ShipClass.Bomber] = 0.5, [ShipClass.Support] = 0.5, [ShipClass.Frigate] = 1, [ShipClass.Torpedo] = 2,
        [ShipClass.Battlecruiser] = 4, [ShipClass.Bulwark] = 4, [ShipClass.Carrier] = 1, [ShipClass.Dreadnought] = 2,
        [ShipClass.Titan] = 3, [ShipClass.Leviathan] = 1,
    };

    static readonly TechId[] TechOrder =
    [
        TechId.Weapons, TechId.Armor, TechId.Shields, TechId.Engines, TechId.Mining, TechId.Hangars,
        TechId.Logistics, TechId.Fortification, TechId.Repair, TechId.Energy,
    ];

    /// <summary>A purchase the AI would like: something to build on a planet, or a tech.</summary>
    readonly record struct Wish(int Planet, GalaxyBuild? Build, TechId? Tech = null);

    /// <summary>
    /// Hops from each planet to the nearest planet held by a rival or with a rival fleet on it.
    /// The Errants are no front: they never leave their planet, and it blocks the way. Unless it
    /// walls the faction off from every rival: their planet is then the front, to take once
    /// clearly stronger, or the fleets would wait at home forever.
    /// </summary>
    static Dictionary<int, int> HopsToEnemy(Galaxy g, Side side)
    {
        var seen = VisiblePlanets(g, side);
        // Truce partners are no front while there is another rival; with none left, the fleets
        // still face them, ready for when the truce runs out.
        // Allies are never one.
        var peace = g.Factions.Keys.Any(f => AtWar(g, side, f) && !g.Factions[f].Eliminated);
        // The Errants and the independents are no rival: they never leave their planets.
        bool Rival(Side? f) => f is Side other && IsFaction(g, other) && !Allies(g, side, other) && (!peace || !AtPeace(g, side, other));
        var frontier = g.Planets
            .Where(p => Rival(p.Owner) || (seen.Contains(p.Id) && FleetsAt(g, p.Id).Exists(f => Rival(f.Side))))
            .Select(p => p.Id);
        var hops = Hops(g, frontier, p => p.Owner != Faction.Errant);
        var own = g.Planets.Where(p => p.Owner == side).ToList();
        if (own.Count == 0 || own.Exists(p => hops.ContainsKey(p.Id))) return hops;
        return Hops(g, g.Planets.Where(p => p.Owner == Faction.Errant).Select(p => p.Id));
    }

    static int HopsOf(Dictionary<int, int> hops, int id) => hops.GetValueOrDefault(id, 99);

    /// <summary>
    /// Strength an attacker would meet at a planet, as far as the faction knows (the Errants' site,
    /// always): what holds it and the fleets it sees there, truce partners left out.
    /// </summary>
    static double Threat(Galaxy g, Planet planet, Side side)
    {
        if (!Known(g, side, planet)) return 0;
        var hostile = planet.Owner is Side o && AtWar(g, side, o);
        var owned = hostile ? DefenseValue(g, planet) - FleetsAt(g, planet.Id, planet.Owner).Sum(FleetStrength) : 0;
        var fleets = FleetsAt(g, planet.Id).Where(f => AtWar(g, side, f.Side) && !InNebula(g, f));
        return owned + fleets.Sum(FleetStrength);
    }

    /// <summary>
    /// What it really takes to win a planet, as series of battles measure it: what stands in orbit
    /// falls more easily than its hull suggests, docked fleets fight harder than their price, and
    /// the fleets next door come back for it. The hard AI's estimate, in place of Threat.
    /// </summary>
    static double Odds(Galaxy g, Side side, Planet planet, HashSet<int> seen)
    {
        var all = Threat(g, planet, side);
        var fleets = all == 0 ? 0 : FleetsAt(g, planet.Id).Where(f => AtWar(g, side, f.Side) && !InNebula(g, f)).Sum(FleetStrength);
        var near = Neighbours(g, planet.Id).SelectMany(n => FleetsAt(g, n)).Where(f => Hostile(g, side, f, seen)).Sum(FleetStrength);
        return (all - fleets) * 0.6 + fleets * 1.3 + near * 0.5;
    }

    /// <summary>Whether a fleet the faction sees can strike it: not its own, not an ally's or a truce partner's, not the guard of the Errants' or the independents' planet, which never leaves it.</summary>
    static bool Hostile(Galaxy g, Side side, Fleet f, HashSet<int> seen) =>
        AtWar(g, side, f.Side) && FleetVisible(g, side, f, seen)
        && !(!IsFaction(g, f.Side) && f.At is int at && g.Planets[at].Owner == f.Side);

    /// <summary>Whether the faction is at war with a planet's owner: a rival galaxy faction, neither ally nor truce partner, or the Errants.</summary>
    static bool EnemyPlanet(Galaxy g, Side side, Planet p) => p.Owner is Side o && AtWar(g, side, o);

    /// <summary>Ship weights tilted towards what counters the enemy fleets the faction can see.</summary>
    static ShipClass? PickShip(Galaxy g, Side side, Planet planet)
    {
        var ai = g.Factions[side].AI!;
        var weights = new Dictionary<ShipClass, double>(Cunning(ai) ? ShrewdWeights : Clumsy(ai) ? ShowyWeights : ShipWeights);
        var seen = VisiblePlanets(g, side);
        var small = 0;
        var large = 0;
        foreach (var f in g.Fleets.Values)
        {
            if (Clumsy(ai) || Allies(g, f.Side, side) || !FleetVisible(g, side, f, seen)) continue;
            foreach (var c in Ships.All)
            {
                if (Ships.Defs[c].Size == SizeClass.Small) small += f.Ships.Of(c);
                if (Ships.Defs[c].Size == SizeClass.Large) large += f.Ships.Of(c);
            }
        }
        if (small >= 6) weights[ShipClass.Frigate] += 4;
        if (large >= 2 && !Cunning(ai))
        {
            weights[ShipClass.Torpedo] += 4;
            weights[ShipClass.Bomber] += 3;
        }
        // Its personality leans to small or big hulls (× 1 for Balanced).
        var def = Personalities.Defs[ai.Personality];
        foreach (var c in Ships.All) if (weights.TryGetValue(c, out var w)) weights[c] = w * def.Lean(Ships.Defs[c].Size);
        // Classes at their limit are left out: wanting one would block every other purchase.
        var options = Ships.All.Where(c => weights.ContainsKey(c) && planet.Station >= Ships.Defs[c].MinStationLevel
            && (Ships.Defs[c].Limit is not int limit || ClassCount(g, side, c) < limit)).ToList();
        var total = options.Sum(c => weights[c]);
        var r = GalaxyRand(g) * total;
        foreach (var c in options)
        {
            r -= weights[c];
            if (r < 0) return c;
        }
        return options.Count > 0 ? options[0] : null;
    }

    /// <summary>What pays best first, as far as its stations allow: the hard AI's order.</summary>
    static readonly TechId[] ScholarOrder =
    [
        TechId.Mining, TechId.Weapons, TechId.Shields, TechId.Armor, TechId.Logistics, TechId.Repair, TechId.Engines, TechId.Fortification, TechId.Hangars, TechId.Energy,
    ];

    /// <summary>Least researched tech not at its last level, combat techs first.</summary>
    static TechId? PickTech(Galaxy g, Side side)
    {
        var r = g.Factions[side].Research;
        if (Cunning(g.Factions[side].AI!))
        {
            var top = g.Planets.Where(p => p.Owner == side).Select(p => p.Station).Append(0).Max();
            foreach (var t in ScholarOrder) if (r[t] < Techs.Defs[t].MaxLevel && Techs.StationFor(r[t] + 1) <= top) return t;
            return null;
        }
        foreach (var t in TechOrder.Where(t => r[t] < Techs.Defs[t].MaxLevel).OrderBy(t => r[t])) return t;
        return null;
    }

    /// <summary>
    /// Candidate purchases, most wanted first. Its personality has a say: the aggressive AI builds
    /// few defenses and ships first, the defensive one arms every station, the thrifty one raises
    /// more stations and its economy first, the technological one a lab from its first planet and
    /// research before the rest. Rich mines come first and a shipyard is preferred, for all but
    /// the easy AI. `danger`: a planet with a station faces more than it can hold.
    /// </summary>
    static List<Wish> Wishes(Galaxy g, Side side, Dictionary<int, int> hops, out bool danger)
    {
        var ai = g.Factions[side].AI!;
        var personality = ai.Personality;
        var clumsy = Clumsy(ai);
        var own = g.Planets.Where(p => p.Owner == side).ToList();
        var byFront = own.OrderBy(p => HopsOf(hops, p.Id)).ToList();
        static Wish At(Planet p, GalaxyBuild build) => new(p.Id, build);
        var out_ = own.OrderByDescending(p => !clumsy && p.Special == PlanetSpecial.Rich).ThenByDescending(p => p.Income)
            .Select(p => At(p, GalaxyBuild.Of(GalaxyBuildKind.Mine))).ToList();

        var infra = new List<Wish>();
        var capital = own.OrderByDescending(p => p.Station).FirstOrDefault();
        if (capital is not null && capital.Station < Station.Levels.Count) infra.Add(At(capital, GalaxyBuild.Of(GalaxyBuildKind.Station)));
        var stations = own.Count(p => p.Station > 0 || p.Buildings.Exists(q => q.Kind == GalaxyBuildKind.Station));
        // Where a station pays twice first: a shipyard builds faster, a fortress holds more platforms.
        var staging = byFront.Where(p => p.Station == 0 && !p.Buildings.Exists(q => q.Kind == GalaxyBuildKind.Station) && (p.Income >= 3 || (!clumsy && p.Special == PlanetSpecial.Shipyard)))
            .OrderByDescending(p => !clumsy && p.Special is PlanetSpecial.Shipyard or PlanetSpecial.Fortress).FirstOrDefault();
        if (staging is not null && stations < 1 + own.Count / (personality == Personality.Economic || Cunning(ai) ? 2.0 : 3.0)) infra.Add(At(staging, GalaxyBuild.Of(GalaxyBuildKind.Station)));
        // Research: a lab in the rear once the empire grows, then one tech after another.
        var rear = byFront.LastOrDefault();
        var studious = personality == Personality.Technological;
        // The hard AI puts research before all but its mines as soon as it pays: a level is worth
        // a tenth of the fleet it strengthens, so a fleet four times its price or more. Before
        // that, ships come first: with nothing flying, research loses the war at level 10.
        var scholar = Cunning(ai);
        var first = new List<Wish>();
        var armada = g.Fleets.Values.Where(f => f.Side == side).Sum(FleetStrength);
        // It takes its supply along too: a station where its fleets stand out of reach of one.
        if (scholar && armada >= GalaxyRules.StationCost * 4)
        {
            foreach (var f in g.Fleets.Values)
            {
                if (f.Side != side || f.At is not int at || g.Planets[at].Owner != side || g.Planets[at].Station > 0 || Supplied(g, f)) continue;
                if (!g.Planets[at].Buildings.Exists(q => q.Kind == GalaxyBuildKind.Station)) first.Add(At(g.Planets[at], GalaxyBuild.Of(GalaxyBuildKind.Station)));
            }
        }
        if (rear is not null && own.Count >= (studious || scholar ? 1 : 3) && !own.Exists(p => p.Lab > 0)) (scholar && armada >= GalaxyRules.LabCost * 4 ? first : infra).Add(At(rear, GalaxyBuild.Of(GalaxyBuildKind.Lab)));
        if (own.Exists(p => p.Lab > 0) && g.Factions[side].Researching is null && PickTech(g, side) is TechId tech)
        {
            // Mining pays for itself once there are mines to speak of.
            var pays = tech == TechId.Mining ? own.Sum(p => p.Mines) >= 4 : armada >= PriceOf(g, side, new Wish(-1, null, tech)) * 4;
            if (scholar && pays) first.Add(new Wish(-1, null, tech));
            else if (studious) infra.Insert(0, new Wish(-1, null, tech));
            else infra.Add(new Wish(-1, null, tech));
        }
        foreach (var p in byFront)
        {
            if (p.Station == 0 || HopsOf(hops, p.Id) > 1 && personality != Personality.Defensive) continue;
            if (personality != Personality.Aggressive)
            {
                // A few platforms gain more from a level than from one more of them.
                if (!clumsy && p.Platforms >= 2) infra.Add(At(p, GalaxyBuild.Of(GalaxyBuildKind.PlatformTier)));
                infra.Add(At(p, GalaxyBuild.Of(GalaxyBuildKind.Platform)));
            }
            infra.Add(At(p, GalaxyBuild.Of(GalaxyBuildKind.RepairDock)));
            if (personality != Personality.Aggressive) infra.Add(At(p, GalaxyBuild.Of(GalaxyBuildKind.IonCannon)));
        }

        var ships = new List<Wish>();
        var yards = byFront.Where(p => p.Station > 0 && p.Units.Count < (clumsy ? 1 : 2)).ToList();
        var yard = yards.Find(p => !clumsy && p.Special == PlanetSpecial.Shipyard) ?? yards.FirstOrDefault();
        if (yard is not null) ai.Want ??= PickShip(g, side, yard);
        if (yard is not null && ai.Want is ShipClass want) ships.Add(At(yard, GalaxyBuild.Ship(want)));
        // Heroes now and then, at the front shipyard.
        var hero = yard is null ? null : Heroes.Of(g.Factions[side].Race).FirstOrDefault(h =>
        {
            var r = CanBuild(g, side, yard.Id, GalaxyBuild.HeroOf(h.Id));
            return r.Ok || r.Reason == NoCredits;
        });
        if (yard is not null && hero is not null && GalaxyRand(g) < 0.15) ships.Insert(0, At(yard, GalaxyBuild.HeroOf(hero.Id)));

        // A planet facing a stronger enemy fleet next door gets its defenses first; the hard AI
        // answers with ships before that, which fall less easily and can go where they are
        // needed. The Errants' guard never leaves its planet: no danger.
        var seen = VisiblePlanets(g, side);
        var urgent = new List<Wish>();
        danger = false;
        foreach (var p in own)
        {
            var near = Neighbours(g, p.Id).SelectMany(n => FleetsAt(g, n)).Where(f => Hostile(g, side, f, seen));
            var menace = near.Sum(FleetStrength);
            if (menace > 0 && menace > DefenseValue(g, p))
            {
                danger |= p.Station > 0;
                urgent.Add(At(p, GalaxyBuild.Of(p.Station == 0 ? GalaxyBuildKind.Station : GalaxyBuildKind.Platform)));
                if (!clumsy) urgent.Add(At(p, GalaxyBuild.Of(GalaxyBuildKind.PlatformTier)));
                urgent.Add(At(p, GalaxyBuild.Of(GalaxyBuildKind.IonCannon)));
            }
        }
        if (urgent.Count > 0) return scholar ? [.. out_, .. first, .. ships, .. urgent, .. infra] : [.. urgent, .. out_, .. first, .. ships, .. infra];
        // Otherwise alternate between growing the fleet and the infrastructure, by turns of 20 s:
        // one in two, two in three for the aggressive AI, one in three for the defensive one,
        // and none in the thrifty one's first five minutes.
        var turn = Math.Floor(g.Time / 20);
        var shipsFirst = personality switch
        {
            Personality.Aggressive => turn % 3 != 2,
            Personality.Defensive => turn % 3 == 0,
            Personality.Economic => g.Time >= 300 && turn % 2 == 0,
            _ => turn % 2 == 0,
        };
        // The easy AI thinks of its fleet last: one turn in three, whoever it is.
        if (clumsy) shipsFirst = turn % 3 == 0;
        return shipsFirst ? [.. out_, .. first, .. ships, .. infra] : [.. out_, .. first, .. infra, .. ships];
    }

    /// <summary>
    /// The big project the AI saves for, beyond what its turns of spending ever gather: a star
    /// lance, once it has a station at the top level. None for the easy AI.
    /// </summary>
    static Wish? Project(Galaxy g, Side side)
    {
        if (Clumsy(g.Factions[side].AI!) || HasLance(g, side)) return null;
        var site = g.Planets.Where(p => p.Owner == side && p.Station >= Station.Levels.Count).OrderBy(p => p.Id).FirstOrDefault();
        return site is null ? null : new Wish(site.Id, GalaxyBuild.Of(GalaxyBuildKind.StarLance));
    }

    /// <summary>What a wish costs right now.</summary>
    static double PriceOf(Galaxy g, Side side, Wish wish) => wish.Tech is TechId t
        ? Techs.Defs[t].Costs[ResearchQueue.PlannedLevel(g.Factions[side], t)]
        : BuildPrice(g, g.Planets[wish.Planet], wish.Build!.Value);

    /// <summary>
    /// Buys the first wish it can; stops to save up when the wish is only too expensive, and passes
    /// it over when two minutes of income would not pay it (SaveHorizon), but for the easy AI. A
    /// share of its income goes to its project, and only a planet in danger touches it; a treasury
    /// well beyond the project's price buys it at once.
    /// </summary>
    static void Spend(Galaxy g, Side side, Dictionary<int, int> hops, double interval)
    {
        var f = g.Factions[side];
        var ai = f.AI!;
        var wishes = Wishes(g, side, hops, out var danger);
        if (Project(g, side) is { } project)
        {
            ai.Fund += IncomeOf(g, side) * interval * Math.Min(0.6, Personalities.Defs[ai.Personality].SaveShare);
            var price = PriceOf(g, side, project);
            if ((ai.Fund >= price || f.Credits >= price * 1.5) && EnqueueBuild(g, side, project.Planet, project.Build!.Value).Ok)
            {
                ai.Fund = 0;
                return;
            }
        }
        else ai.Fund = 0;
        ai.Fund = Math.Min(ai.Fund, f.Credits);
        foreach (var wish in wishes)
        {
            var r = wish.Tech is TechId tech ? CanResearchTech(g, side, tech) : CanBuild(g, side, wish.Planet, wish.Build!.Value);
            if (r.Ok)
            {
                if (!danger && PriceOf(g, side, wish) > f.Credits - ai.Fund) return;
                if (wish.Tech is TechId t) StartTech(g, side, t);
                else EnqueueBuild(g, side, wish.Planet, wish.Build!.Value);
                if (wish.Build?.Kind == GalaxyBuildKind.Ship) ai.Want = null;
                return;
            }
            // What two minutes of income would not pay is passed over rather than saved for, but by the easy AI: on its
            // turns of ships the AI spent what it had put aside, and a station level it never reached kept it from a lab,
            // research and platforms for the whole campaign.
            if (r.Reason == NoCredits && (Clumsy(ai) || PriceOf(g, side, wish) <= IncomeOf(g, side) * SaveHorizon)) return;
            // A ship out of reach makes way for another class; one it may no longer build (its limit reached meanwhile) too.
            if (r.Reason == NoCredits)
            {
                if (wish.Build?.Kind == GalaxyBuildKind.Ship) ai.Want = null;
                continue;
            }
            // A ship it may no longer build (its limit reached meanwhile): want another one.
            if (wish.Build?.Kind == GalaxyBuildKind.Ship) ai.Want = null;
        }
    }

    /// <summary>Galaxy seconds of income beyond which the AI passes over a purchase rather than save for it.</summary>
    const double SaveHorizon = 120;

    /// <summary>A strike takes this much more than the fleet cap lets fight at once, or than its target asks.</summary>
    const double SplitShare = 1.5;

    /// <summary>The strength of the share of a fleet that fits under the fleet cap at once, its classes in proportion: all of it below the cap.</summary>
    static double CapShare(Galaxy g, Fleet f)
    {
        var points = 0;
        foreach (var (c, n) in f.Ships) points += n * Reserves.Points(c);
        var value = FleetStrength(f);
        return points <= 0 ? value : value * Math.Min(1, Reserves.CapPoints(g.FleetCap) / (double)points);
    }

    /// <summary>
    /// Ships of a fleet worth about `strength`, each class in proportion; null when that is most of
    /// it anyway, and the whole fleet goes.
    /// </summary>
    static ShipCounts? Share(Fleet f, double strength)
    {
        var value = FleetStrength(f);
        if (strength >= value * 0.75) return null;
        var part = strength / value;
        var ships = new ShipCounts();
        foreach (var (c, n) in f.Ships)
        {
            var k = Math.Min(n, (int)Math.Ceiling(n * part));
            if (k > 0) ships[c] = k;
        }
        return ships.Count > 0 ? ships : null;
    }

    static bool Heading(Galaxy g, Side side, int planetId) =>
        g.Fleets.Values.Any(f => f.Side == side && (f.To == planetId || f.Path.Contains(planetId)));

    static ShipClass Cheapest(Fleet f) => Ships.All.Where(c => f.Ships.Of(c) > 0).OrderBy(c => Ships.Defs[c].Cost).First();

    /// <summary>What a planet brings to its owner: its special included, ruins most of all, for whoever gets there first.</summary>
    /// <summary>What a planet brings, as the AI weighs it: for exchanges (Galaxies.PlanetValue).</summary>
    internal static double WorthOf(Planet p) => Worth(p);

    static double Worth(Planet p) => p.Income + p.Mines * GalaxyRules.MineIncome + p.Station * 5 + p.Special switch
    {
        PlanetSpecial.Ruins => 15,
        PlanetSpecial.Rich => p.MineSlots * GalaxyRules.MineIncome,
        PlanetSpecial.Shipyard or PlanetSpecial.Archive => 5,
        PlanetSpecial.Fortress or PlanetSpecial.Relay => 2,
        _ => 0,
    };

    /// <summary>
    /// The edge an attack waits for: 50 %, down to 20 % after ten idle minutes, × its personality's
    /// (40 % down to 10 % after six minutes before campaigns were made to last). The easy AI goes
    /// in well short of that, and often loses its fleet.
    /// </summary>
    static double Edge(GalaxyAIState ai) => Math.Max(1.2, 1.5 - ai.Idle / 2000) * Personalities.Defs[ai.Personality].Edge * (Clumsy(ai) ? 0.5 : Cunning(ai) ? 0.75 : 1);

    /// <summary>Planets within two lanes of a planet, itself included.</summary>
    static HashSet<int> Around(Galaxy g, int planetId)
    {
        var out_ = new HashSet<int> { planetId };
        foreach (var n in Neighbours(g, planetId))
        {
            out_.Add(n);
            foreach (var m in Neighbours(g, n)) out_.Add(m);
        }
        return out_;
    }

    /// <summary>
    /// Strength of the hostile fleets the faction sees within two lanes of a planet, or on a lane
    /// towards it or a neighbour, and of a war band announced to strike it from the dark.
    /// </summary>
    static double Menace(Galaxy g, Side side, Planet planet, HashSet<int> seen)
    {
        var around = Around(g, planet.Id);
        var next = Neighbours(g, planet.Id).Append(planet.Id).ToHashSet();
        return g.Fleets.Values
            .Where(f => (f.At is int at ? around.Contains(at) : f.To is int to && next.Contains(to)) && Hostile(g, side, f, seen))
            .Sum(FleetStrength)
            + g.Ongoing.Where(i => i.Kind == IncidentKind.WarBand && i.Other == planet.Id).Sum(i => Battle.FleetValue(i.Ships));
    }

    /// <summary>A route from `from` to `to` that stops at no hostile planet before the end.</summary>
    static bool SafeRoute(Galaxy g, Side side, int from, int to, HashSet<int> seen)
    {
        var path = FindPath(g, from, to);
        return path is not null && path.Take(path.Count - 1).All(id =>
            !EnemyPlanet(g, side, g.Planets[id]) && !FleetsAt(g, id).Exists(f => Hostile(g, side, f, seen)));
    }

    /// <summary>
    /// Defensive reactions: a planet an enemy fleet draws near, within two lanes, and that cannot
    /// hold alone, gets help from the fleets docked within two lanes of it, as long as they can
    /// hold it together. A fleet leaves a planet it guards only for a more valuable one. Returns
    /// the fleets sent.
    /// </summary>
    static HashSet<int> Defend(Galaxy g, Side side, List<Planet> own, HashSet<int> seen)
    {
        var sent = new HashSet<int>();
        var menace = own.ToDictionary(p => p.Id, p => Menace(g, side, p, seen));
        foreach (var p in own.Where(p => menace[p.Id] > 0).OrderByDescending(Worth))
        {
            var need = menace[p.Id] * 1.1 - DefenseValue(g, p);
            if (need <= 0) continue;
            var around = Around(g, p.Id);
            var helpers = own.Where(q => q.Id != p.Id && around.Contains(q.Id))
                .SelectMany(q => FleetsAt(g, q.Id, side).Where(f => !sent.Contains(f.Id)
                    && (Worth(q) < Worth(p) || DefenseValue(g, q) - FleetStrength(f) >= menace[q.Id] * 1.1)))
                .Where(f => SafeRoute(g, side, f.At!.Value, p.Id, seen))
                .OrderByDescending(FleetStrength).ToList();
            if (DefenseValue(g, p) + helpers.Sum(FleetStrength) < menace[p.Id]) continue;
            foreach (var f in helpers)
            {
                if (need <= 0) break;
                if (!SendFleet(g, f.Id, p.Id).Ok) continue;
                sent.Add(f.Id);
                need -= FleetStrength(f);
            }
        }
        return sent;
    }

    /// <summary>
    /// The next offensive: a rival planet next to one of the faction's, or of an ally's, where its
    /// fleets gather: a rival walled off behind an ally is still within reach, or two factions
    /// allied to the same third would never end the war between them. It holds while both stay
    /// what they are; then the most valuable planet for what guards it, its own staging planets
    /// first. The hard AI weighs it anew all the time, and gladly turns to a planet whose owner
    /// has its hands full elsewhere: its capital, more often than not, stripped of its fleet.
    /// </summary>
    static (int Staging, int Target)? Offensive(Galaxy g, Side side, GalaxyAIState ai, List<Planet> own)
    {
        bool Rival(Planet t) => t.Owner is Side o && g.Factions.ContainsKey(o) && AtWar(g, side, o);
        bool Base(Planet p) => p.Owner is Side o && Allies(g, side, o);
        var held = ai.Staging is int s0 && ai.Target is int t0 && Base(g.Planets[s0]) && Rival(g.Planets[t0]) && !StormOn(g, s0, t0) ? (s0, t0) : ((int, int)?)null;
        if (held is not null && !Cunning(ai)) return held;
        // A faction it swore a joint war against comes first: its partner counts on it.
        double Score(Planet t) => Worth(t) / (Threat(g, t, side) + 1000) * (Cunning(ai) && BusyElsewhere(g, t.Owner!.Value, side) ? 2 : 1)
            * (Sworn(g, side, t.Owner!.Value) ? 2.5 : 1);
        var bases = g.Pacts.Count > 0 || g.Factions.Values.Any(f => f.Id != side && f.Team != 0 && Teammates(g, f.Id, side)) ? g.Planets.Where(Base).ToList() : own;
        var best = bases.SelectMany(s => Neighbours(g, s.Id).Where(id => !StormOn(g, s.Id, id)).Select(id => g.Planets[id]).Where(Rival).Select(t => (Staging: s, Target: t)))
            .OrderByDescending(x => x.Staging.Owner == side)
            .ThenByDescending(x => Score(x.Target))
            .ThenByDescending(x => x.Staging.Station)
            .Select(x => ((int, int)?)(x.Staging.Id, x.Target.Id)).FirstOrDefault();
        // Not for a trifle: fleets on the way would turn back for nothing.
        if (held is { } h && (best is not { } b || Score(g.Planets[b.Item2]) < Score(g.Planets[h.Item2]) * 1.3)) return h;
        ai.Staging = best?.Item1;
        ai.Target = best?.Item2;
        return best;
    }

    /// <summary>
    /// The hard AI breaks a truce when it pays, as the rules let anyone: nobody paid for it, the
    /// other is far weaker or busy elsewhere, and a fleet of its own stands next to a planet of
    /// theirs it can take. A player is warned first, BreakNotice seconds ahead (and nothing comes
    /// of it if the occasion passes); another AI is not. The wronged AI bears the grudge.
    /// </summary>
    static void BreakTruces(Galaxy g, Side side, GalaxyAIState ai)
    {
        foreach (var t in g.Truces.ToList())
        {
            if ((t.A != side && t.B != side) || t.Paid || t.Opening) continue;
            var other = t.A == side ? t.B : t.A;
            var wants = (Might(g, side) >= Might(g, other) * 1.3 || BusyElsewhere(g, other, side)) && g.Fleets.Values.Any(f => f.Side == side && f.At is int at
                && f.BattleId is null && f.Condition >= 0.5 && Neighbours(g, at).Exists(n =>
                g.Planets[n].Owner == other && !StormOn(g, at, n) && FleetStrength(f) >= DefenseValue(g, g.Planets[n]) * Edge(ai) + 300));
            // A lasting peace is denounced, its notice standing for the warning.
            if (t.Lasting)
            {
                if (wants) Denounce(g, side, other);
                continue;
            }
            if (ai.Breaking.TryGetValue(other, out var at))
            {
                if (!wants) ai.Breaking.Remove(other);
                else if (g.Time >= at)
                {
                    ai.Breaking.Remove(other);
                    BreakTruce(g, side, other);
                }
                continue;
            }
            if (!wants) continue;
            if (g.Factions[other].AI is null)
            {
                ai.Breaking[other] = g.Time + BreakNotice;
                Say(g, side, other, Remark.Warning);
            }
            else BreakTruce(g, side, other);
        }
        // A warning about a truce gone since (run out, or broken by the other) is void.
        foreach (var o in ai.Breaking.Keys.ToList())
        {
            if (TruceOf(g, side, o) is null) ai.Breaking.Remove(o);
        }
    }

    /// <summary>
    /// An AI keeps a lasting peace while it has a war to wage elsewhere and its partner does not
    /// dominate the galaxy; otherwise it denounces it, with its notice: against the faction that
    /// dominates, or, at war with nobody left, against its weakest partner, for the campaign to end.
    /// </summary>
    static void Reconsider(Galaxy g, Side side)
    {
        var lasting = g.Truces.Where(t => t.Lasting && !t.Opening && (t.A == side || t.B == side)).Select(t => t.A == side ? t.B : t.A).ToList();
        if (lasting.Count == 0) return;
        var leader = Leader(g);
        if (leader is Side top && lasting.Contains(top))
        {
            Denounce(g, side, top);
            return;
        }
        if (g.Factions.Values.Any(o => !o.Eliminated && o.Id != side && AtWar(g, side, o.Id))) return;
        Denounce(g, side, lasting.OrderBy(o => Might(g, o)).First());
    }

    /// <summary>
    /// Wins over the independents of a neighbouring planet, one at a time, when its credits allow
    /// it beside its project: the planet that brings most for its price. The aggressive AI would
    /// rather take it by force, unless rich; the easy AI never thinks of it.
    /// </summary>
    static void Woo(Galaxy g, Side side, GalaxyAIState ai)
    {
        if (Clumsy(ai) || g.Courtships.Exists(c => c.Side == side)) return;
        var spare = g.Factions[side].Credits - ai.Fund;
        var margin = ai.Personality == Personality.Aggressive ? 2.5 : 1.2;
        Planet? best = null;
        var score = 0.0;
        foreach (var p in g.Planets)
        {
            if (p.Owner != Independent || !CanCourt(g, side, p.Id).Ok) continue;
            var price = CourtPrice(g, side, p);
            if (spare < price * margin || Worth(p) / price <= score) continue;
            (best, score) = (p, Worth(p) / price);
        }
        if (best is not null) Court(g, side, best.Id);
    }

    /// <summary>The nearest planet of the faction with a station, where a worn fleet mends fastest; any of its planets without one.</summary>
    static Planet? Home(Galaxy g, Side side, int from)
    {
        var hops = Hops(g, [from]);
        return g.Planets.Where(p => p.Owner == side && hops.ContainsKey(p.Id))
            .OrderByDescending(p => p.Station > 0).ThenBy(p => hops[p.Id]).FirstOrDefault();
    }

    static void Command(Galaxy g, Side side, Dictionary<int, int> hops)
    {
        var ai = g.Factions[side].AI!;
        var seen = VisiblePlanets(g, side);
        var own = g.Planets.Where(p => p.Owner == side).ToList();
        var clumsy = Clumsy(ai);
        var cunning = Cunning(ai);
        var sent = Clumsy(ai) ? new HashSet<int>() : Defend(g, side, own, seen);
        var plan = Offensive(g, side, ai, own);
        var struck = false;
        var rival = false;
        // Lanes to the staging planet through the faction's planets and its allies', worked out once if needed.
        Dictionary<int, int>? route = null;
        bool Friendly(Planet p) => p.Owner is Side o && Allies(g, o, side);
        var derelicts = g.Ongoing.Where(i => i.Kind == IncidentKind.Derelict).Select(i => i.Planet).ToList();
        foreach (var fleet in g.Fleets.Values.ToList())
        {
            if (fleet.Side != side || fleet.At is not int at || fleet.BattleId is not null || sent.Contains(fleet.Id)) continue;
            // A lane an ion storm closes leads nowhere for now.
            var near = Neighbours(g, at).Where(id => !StormOn(g, at, id)).Select(id => g.Planets[id]).ToList();
            var value = FleetStrength(fleet);
            // A worn fleet stays home to mend, unless its own planet needs it; the easy AI hardly
            // waits, the hard one mends well before it goes back, where it can.
            var worn = fleet.Condition < (clumsy ? 0.3 : cunning && Supplied(g, fleet) ? 0.75 : 0.65);
            var home = g.Planets[at].Owner == side;

            // Defend first: an own planet with enemies on it.
            var rescue = near.Find(p => p.Owner == side && Threat(g, p, side) > 0 && value >= Threat(g, p, side) * 1.2);
            if (rescue is not null)
            {
                SendFleet(g, fleet.Id, rescue.Id);
                continue;
            }

            // Then the most valuable enemy planet the fleet can clearly beat; the Errants, only
            // with the full edge. The hard AI reads the odds better: it counts the fleets next
            // door that would come back for it, and goes first for what is worth the most for
            // what guards it.
            double Guard(Planet p) => Cunning(ai) ? Odds(g, side, p, seen) : Threat(g, p, side);
            // A rival faction's planet waits for the fleets to have regrouped since the last strike.
            bool Rival(Planet p) => p.Owner is Side o && IsFaction(g, o);
            double Bar(Planet p) => Guard(p) * (p.Owner == Faction.Errant ? 1.4 : Edge(ai)) + 300;
            var targets = near
                .Where(p => (EnemyPlanet(g, side, p) || Threat(g, p, side) > 0) && (ai.Regroup <= 0 || !Rival(p))
                    && !g.Courtships.Exists(c => c.Side == side && c.Planet == p.Id)
                    && value >= Bar(p))
                .OrderByDescending(p => cunning ? Worth(p) / (Guard(p) + 500) : Worth(p)).ToList();
            if (targets.Count > 0 && !worn)
            {
                // A fleet far past what the fleet cap lets fight at once splits, but for the easy AI: each strike takes
                // half again what fits under the cap, or what its target asks, and what is left strikes the next target,
                // or stays. One huge fleet took a planet at a time, its reserve waiting, and the campaign dragged on.
                var cap = clumsy ? double.PositiveInfinity : CapShare(g, fleet);
                var hit = false;
                foreach (var t in clumsy ? targets.Take(1) : targets)
                {
                    if (fleet.At is null || FleetStrength(fleet) < Bar(t)) continue;
                    var share = clumsy ? null : Share(fleet, Math.Max(cap, Bar(t)) * SplitShare);
                    if (!SendFleet(g, fleet.Id, t.Id, share, share is null ? null : fleet.Heroes).Ok) continue;
                    hit = true;
                    struck |= t.Owner is not null;
                    rival |= Rival(t);
                }
                if (hit) continue;
            }

            // A derelict within two lanes, for whoever gets there first: one ship goes to claim it.
            var wreck = derelicts.Where(d => d != at && Around(g, at).Contains(d) && !Heading(g, side, d)
                && Threat(g, g.Planets[d], side) == 0 && SafeRoute(g, side, at, d, seen)).Select(d => (int?)d).FirstOrDefault();
            if (wreck is int d0)
            {
                var one = Battle.CountShips(fleet.Ships) >= 2 ? new ShipCounts { [Cheapest(fleet)] = 1 } : null;
                if (SendFleet(g, fleet.Id, d0, one).Ok) continue;
            }

            var free = near.Where(p => p.Owner is null && !Heading(g, side, p.Id) && Threat(g, p, side) == 0).OrderByDescending(p => clumsy ? 0 : Worth(p)).FirstOrDefault();
            if (free is not null && Battle.CountShips(fleet.Ships) >= 2)
            {
                SendFleet(g, fleet.Id, free.Id, new ShipCounts { [Cheapest(fleet)] = 1 });
                continue;
            }

            // Stranded at a truce partner's planet, or worn beyond its supply lines: back home.
            var partner = g.Planets[at].Owner is Side o && o != side && AtPeace(g, side, o);
            if (partner || (worn && !Supplied(g, fleet)))
            {
                if (Home(g, side, at) is { } back) SendFleet(g, fleet.Id, back.Id);
                continue;
            }
            if (worn && home) continue;

            // Gather for the offensive at its staging planet, from the faction's own planets or its
            // allies'; at the staging planet, wait for the edge (the strike itself is the attack
            // above). Without a safe way there at once, one planet nearer at a time through friendly
            // ones: a fleet left at an ally's planet, or behind a rival's, stayed there for good.
            // The easy AI never gathers: each fleet moves up and strikes on its own.
            if (plan is { } p0 && Friendly(g.Planets[at]) && !Clumsy(ai))
            {
                if (at != p0.Staging && SafeRoute(g, side, at, p0.Staging, seen)) SendFleet(g, fleet.Id, p0.Staging);
                else if (at != p0.Staging)
                {
                    route ??= Hops(g, [p0.Staging], Friendly);
                    var left = route.GetValueOrDefault(at, int.MaxValue);
                    var step = near.Where(p => Friendly(p) && route.GetValueOrDefault(p.Id, int.MaxValue) < left).OrderBy(p => route[p.Id]).FirstOrDefault();
                    if (step is not null) SendFleet(g, fleet.Id, step.Id);
                }
                continue;
            }

            // Move up towards the front through planets the faction can cross. Only from its own:
            // elsewhere the fleet stays to take the planet over, or a planet behind the front
            // would never be taken, its fleet called back again and again.
            var here = HopsOf(hops, at);
            var closer = near
                .Where(p => (p.Owner is not Side o || Allies(g, o, side)) && HopsOf(hops, p.Id) < here)
                .OrderBy(p => HopsOf(hops, p.Id)).FirstOrDefault();
            if (closer is not null && here > 1 && home) SendFleet(g, fleet.Id, closer.Id);
        }
        ai.Idle = struck ? 0 : ai.Idle + ThinkInterval;
        // Every fleet ready struck together this time; the next strike on a rival waits.
        if (rival) ai.Regroup = clumsy ? ClumsyRegroup : RegroupTime;
    }

    /// <summary>What a faction could bring to a war: its fleets, and a minute of its income.</summary>
    public static double Might(Galaxy g, Side side)
    {
        // A plain loop: diplomacy asks it of every faction, many times a second.
        var s = 0.0;
        foreach (var f in g.Fleets.Values)
        {
            if (f.Side == side) s += FleetStrength(f);
        }
        return s + IncomeOf(g, side) * 60;
    }

    /// <summary>Whether two factions face each other: planets two lanes apart at most, or a battle between them.</summary>
    internal static bool Contact(Galaxy g, Side a, Side b)
    {
        foreach (var x in g.Battles)
        {
            if ((x.Attacker == a && x.Defender == b) || (x.Attacker == b && x.Defender == a)) return true;
        }
        // Two walks over the lanes, nothing allocated: `near`, a's planets and those next to them;
        // then b's within one lane of those. It walked the whole galaxy from a's planets, a list
        // of neighbours made for each, and diplomacy asks it of every pair many times a second.
        var count = g.Planets.Count;
        Span<bool> near = count <= 512 ? stackalloc bool[count] : new bool[count];
        foreach (var p in g.Planets)
        {
            if (p.Owner == a) near[p.Id] = true;
        }
        foreach (var (x, y) in g.Lanes)
        {
            if (g.Planets[x].Owner == a) near[y] = true;
            if (g.Planets[y].Owner == a) near[x] = true;
        }
        foreach (var p in g.Planets)
        {
            if (p.Owner == b && near[p.Id]) return true;
        }
        foreach (var (x, y) in g.Lanes)
        {
            if ((near[x] && g.Planets[y].Owner == b) || (near[y] && g.Planets[x].Owner == b)) return true;
        }
        return false;
    }

    /// <summary>Whether the faction is at war, face to face, with a third galaxy faction besides `other`.</summary>
    static bool BusyElsewhere(Galaxy g, Side side, Side other)
    {
        foreach (var t in g.Factions.Values)
        {
            if (!t.Eliminated && t.Id != other && AtWar(g, side, t.Id) && Contact(g, side, t.Id)) return true;
        }
        return false;
    }

    public static void UpdateGalaxyAI(Galaxy g, double dt)
    {
        foreach (var f in g.Factions.Values)
        {
            if (f.AI is null || f.Eliminated) continue;
            f.AI.Regroup = Math.Max(0, f.AI.Regroup - dt);
            f.AI.Timer -= dt;
            if (f.AI.Timer > 0) continue;
            var interval = ThinkInterval * (Worlds.Difficulties[f.AI.Difficulty].Think / Worlds.Difficulties[Difficulty.Normal].Think);
            f.AI.Timer = interval;
            var hops = HopsToEnemy(g, f.Id);
            if (Cunning(f.AI)) BreakTruces(g, f.Id, f.AI);
            Reconsider(g, f.Id);
            if (!Clumsy(f.AI)) OfferDeals(g, f.Id);
            Woo(g, f.Id, f.AI);
            Spend(g, f.Id, hops, interval);
            Command(g, f.Id, hops);
        }
    }
}
