// Galaxy AI: what to build where, and where to send its fleets. Port of src/sim/galaxyAI.ts.
// Beyond the web version: it gathers its fleets at a staging planet to strike together, sends
// help to planets an enemy fleet draws near, keeps to its truces and offers some, claims
// derelicts, and brings worn fleets home from beyond its supply lines.
using SandboxShip.Sim.Data;
using static SandboxShip.Sim.Galaxies;

namespace SandboxShip.Sim;

public sealed partial class GalaxyAIState
{
    /// <summary>The enemy planet the AI's next offensive strikes, and its own planet next to it where its fleets gather first.</summary>
    public int? Target, Staging;
    /// <summary>Galaxy seconds since the AI last struck an enemy planet: the longer, the smaller the edge it waits for.</summary>
    public double Idle;
}

public static class GalaxyAi
{
    const double ThinkInterval = 2;
    const string NoCredits = "Crédits insuffisants";

    /// <summary>Relative odds of each ship class once unlocked.</summary>
    static readonly Dictionary<ShipClass, double> ShipWeights = new()
    {
        [ShipClass.Scout] = 1, [ShipClass.Bomber] = 1, [ShipClass.Support] = 1, [ShipClass.Frigate] = 4, [ShipClass.Torpedo] = 2,
        [ShipClass.Battlecruiser] = 3, [ShipClass.Bulwark] = 1, [ShipClass.Carrier] = 1, [ShipClass.Dreadnought] = 2,
        [ShipClass.Titan] = 1, [ShipClass.Leviathan] = 1,
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
    static Dictionary<int, int> HopsToEnemy(Galaxy g, Faction faction)
    {
        var seen = VisiblePlanets(g, faction);
        // Truce partners are no front while there is another rival; with none left, the fleets
        // still face them, ready for when the truce runs out.
        var peace = g.Factions.Keys.Any(f => f != faction && !AtPeace(g, faction, f) && !g.Factions[f].Eliminated);
        bool Rival(Faction? f) => f is Faction other && other != faction && other != Faction.Errant && (!peace || !AtPeace(g, faction, other));
        var frontier = g.Planets
            .Where(p => Rival(p.Owner) || (seen.Contains(p.Id) && FleetsAt(g, p.Id).Exists(f => Rival(f.Faction))))
            .Select(p => p.Id);
        var hops = Hops(g, frontier, p => p.Owner != Faction.Errant);
        var own = g.Planets.Where(p => p.Owner == faction).ToList();
        if (own.Count == 0 || own.Exists(p => hops.ContainsKey(p.Id))) return hops;
        return Hops(g, g.Planets.Where(p => p.Owner == Faction.Errant).Select(p => p.Id));
    }

    static int HopsOf(Dictionary<int, int> hops, int id) => hops.GetValueOrDefault(id, 99);

    /// <summary>
    /// Strength an attacker would meet at a planet, as far as the faction knows (the Errants' site,
    /// always): what holds it and the fleets it sees there, truce partners left out.
    /// </summary>
    static double Threat(Galaxy g, Planet planet, Faction faction)
    {
        if (!Known(g, faction, planet)) return 0;
        var hostile = planet.Owner is Faction o && o != faction && !AtPeace(g, faction, o);
        var owned = hostile ? DefenseValue(g, planet) - FleetsAt(g, planet.Id, planet.Owner).Sum(FleetStrength) : 0;
        var fleets = FleetsAt(g, planet.Id).Where(f => f.Faction != faction && !AtPeace(g, faction, f.Faction) && !InNebula(g, f));
        return owned + fleets.Sum(FleetStrength);
    }

    /// <summary>Whether a fleet the faction sees can strike it: not its own, not a truce partner's, not the Errants' guard, which never leaves its planet.</summary>
    static bool Hostile(Galaxy g, Faction faction, Fleet f, HashSet<int> seen) =>
        f.Faction != faction && !AtPeace(g, faction, f.Faction) && FleetVisible(g, faction, f, seen)
        && !(f.Faction == Faction.Errant && f.At is int at && g.Planets[at].Owner == Faction.Errant);

    /// <summary>Whether the faction is at war with a planet's owner: a rival galaxy faction with no truce, or the Errants.</summary>
    static bool EnemyPlanet(Galaxy g, Faction faction, Planet p) => p.Owner is Faction o && o != faction && !AtPeace(g, faction, o);

    /// <summary>Ship weights tilted towards what counters the enemy fleets the faction can see.</summary>
    static ShipClass? PickShip(Galaxy g, Faction faction, Planet planet)
    {
        var weights = new Dictionary<ShipClass, double>(ShipWeights);
        var seen = VisiblePlanets(g, faction);
        var small = 0;
        var large = 0;
        foreach (var f in g.Fleets.Values)
        {
            if (f.Faction == faction || !FleetVisible(g, faction, f, seen)) continue;
            foreach (var c in Ships.All)
            {
                if (Ships.Defs[c].Size == SizeClass.Small) small += f.Ships.Of(c);
                if (Ships.Defs[c].Size == SizeClass.Large) large += f.Ships.Of(c);
            }
        }
        if (small >= 6) weights[ShipClass.Frigate] += 4;
        if (large >= 2)
        {
            weights[ShipClass.Torpedo] += 4;
            weights[ShipClass.Bomber] += 3;
        }
        // Classes at their limit are left out: wanting one would block every other purchase.
        var options = Ships.All.Where(c => weights.ContainsKey(c) && planet.Station >= Ships.Defs[c].MinStationLevel
            && (Ships.Defs[c].Limit is not int limit || ClassCount(g, faction, c) < limit)).ToList();
        var total = options.Sum(c => weights[c]);
        var r = GalaxyRand(g) * total;
        foreach (var c in options)
        {
            r -= weights[c];
            if (r < 0) return c;
        }
        return options.Count > 0 ? options[0] : null;
    }

    /// <summary>Least researched tech not at its last level, combat techs first.</summary>
    static TechId? PickTech(Galaxy g, Faction faction)
    {
        var r = g.Factions[faction].Research;
        foreach (var t in TechOrder.Where(t => r[t] < Techs.Defs[t].MaxLevel).OrderBy(t => r[t])) return t;
        return null;
    }

    /// <summary>Candidate purchases, most wanted first.</summary>
    static List<Wish> Wishes(Galaxy g, Faction faction, Dictionary<int, int> hops)
    {
        var own = g.Planets.Where(p => p.Owner == faction).ToList();
        var byFront = own.OrderBy(p => HopsOf(hops, p.Id)).ToList();
        static Wish At(Planet p, GalaxyBuild build) => new(p.Id, build);
        var out_ = own.OrderByDescending(p => p.Income).Select(p => At(p, GalaxyBuild.Of(GalaxyBuildKind.Mine))).ToList();

        var infra = new List<Wish>();
        var capital = own.OrderByDescending(p => p.Station).FirstOrDefault();
        if (capital is not null && capital.Station < Station.Levels.Count) infra.Add(At(capital, GalaxyBuild.Of(GalaxyBuildKind.Station)));
        var stations = own.Count(p => p.Station > 0);
        var staging = byFront.Find(p => p.Station == 0 && p.Income >= 3);
        if (staging is not null && stations < 1 + own.Count / 3.0) infra.Add(At(staging, GalaxyBuild.Of(GalaxyBuildKind.Station)));
        // Research: a lab in the rear once the empire grows, then one tech after another.
        var rear = byFront.LastOrDefault();
        if (rear is not null && own.Count >= 3 && !own.Exists(p => p.Lab > 0)) infra.Add(At(rear, GalaxyBuild.Of(GalaxyBuildKind.Lab)));
        if (own.Exists(p => p.Lab > 0) && g.Factions[faction].Researching is null && PickTech(g, faction) is TechId tech) infra.Add(new Wish(-1, null, tech));
        foreach (var p in byFront)
        {
            if (p.Station == 0 || HopsOf(hops, p.Id) > 1) continue;
            infra.Add(At(p, GalaxyBuild.Of(GalaxyBuildKind.Platform)));
            infra.Add(At(p, GalaxyBuild.Of(GalaxyBuildKind.RepairDock)));
            infra.Add(At(p, GalaxyBuild.Of(GalaxyBuildKind.IonCannon)));
        }

        var ships = new List<Wish>();
        var yard = byFront.Find(p => p.Station > 0 && p.Units.Count < 2);
        var ai = g.Factions[faction].AI!;
        if (yard is not null) ai.Want ??= PickShip(g, faction, yard);
        if (yard is not null && ai.Want is ShipClass want) ships.Add(At(yard, GalaxyBuild.Ship(want)));
        // Heroes now and then, at the front shipyard.
        var hero = yard is null ? null : Heroes.Of(faction).FirstOrDefault(h =>
        {
            var r = CanBuild(g, faction, yard.Id, GalaxyBuild.HeroOf(h.Id));
            return r.Ok || r.Reason == NoCredits;
        });
        if (yard is not null && hero is not null && GalaxyRand(g) < 0.15) ships.Insert(0, At(yard, GalaxyBuild.HeroOf(hero.Id)));

        // A planet facing a stronger enemy fleet next door gets its defenses first. The Errants'
        // guard never leaves its planet: no danger.
        var seen = VisiblePlanets(g, faction);
        var danger = new List<Wish>();
        foreach (var p in own)
        {
            var near = Neighbours(g, p.Id).SelectMany(n => FleetsAt(g, n)).Where(f => Hostile(g, faction, f, seen));
            var menace = near.Sum(FleetStrength);
            if (menace > 0 && menace > DefenseValue(g, p))
            {
                danger.Add(At(p, GalaxyBuild.Of(p.Station == 0 ? GalaxyBuildKind.Station : GalaxyBuildKind.Platform)));
                danger.Add(At(p, GalaxyBuild.Of(GalaxyBuildKind.IonCannon)));
            }
        }
        if (danger.Count > 0) return [.. danger, .. out_, .. ships, .. infra];
        // Otherwise alternate between growing the fleet and the infrastructure.
        return Math.Floor(g.Time / 20) % 2 == 0 ? [.. out_, .. ships, .. infra] : [.. out_, .. infra, .. ships];
    }

    /// <summary>Buys the first wish it can; stops to save up when the wish is only too expensive.</summary>
    static void Spend(Galaxy g, Faction faction, Dictionary<int, int> hops)
    {
        foreach (var wish in Wishes(g, faction, hops))
        {
            var r = wish.Tech is TechId tech ? CanResearchTech(g, faction, tech) : CanBuild(g, faction, wish.Planet, wish.Build!.Value);
            if (r.Ok)
            {
                if (wish.Tech is TechId t) StartTech(g, faction, t);
                else EnqueueBuild(g, faction, wish.Planet, wish.Build!.Value);
                if (wish.Build?.Kind == GalaxyBuildKind.Ship) g.Factions[faction].AI!.Want = null;
                return;
            }
            if (r.Reason == NoCredits) return;
            // A ship it may no longer build (its limit reached meanwhile): want another one.
            if (wish.Build?.Kind == GalaxyBuildKind.Ship) g.Factions[faction].AI!.Want = null;
        }
    }

    static bool Heading(Galaxy g, Faction faction, int planetId) =>
        g.Fleets.Values.Any(f => f.Faction == faction && (f.To == planetId || f.Path.Contains(planetId)));

    static ShipClass Cheapest(Fleet f) => Ships.All.Where(c => f.Ships.Of(c) > 0).OrderBy(c => Ships.Defs[c].Cost).First();

    /// <summary>What a planet brings to its owner.</summary>
    static double Worth(Planet p) => p.Income + p.Mines * GalaxyRules.MineIncome + p.Station * 5;

    /// <summary>The edge an attack on a rival waits for: 40 %, down to 10 % after six idle minutes.</summary>
    static double Edge(GalaxyAIState ai) => Math.Max(1.1, 1.4 - ai.Idle / 1200);

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

    /// <summary>Strength of the hostile fleets the faction sees within two lanes of a planet, or on a lane towards it or a neighbour.</summary>
    static double Menace(Galaxy g, Faction faction, Planet planet, HashSet<int> seen)
    {
        var around = Around(g, planet.Id);
        var next = Neighbours(g, planet.Id).Append(planet.Id).ToHashSet();
        return g.Fleets.Values
            .Where(f => (f.At is int at ? around.Contains(at) : f.To is int to && next.Contains(to)) && Hostile(g, faction, f, seen))
            .Sum(FleetStrength);
    }

    /// <summary>A route from `from` to `to` that stops at no hostile planet before the end.</summary>
    static bool SafeRoute(Galaxy g, Faction faction, int from, int to, HashSet<int> seen)
    {
        var path = FindPath(g, from, to);
        return path is not null && path.Take(path.Count - 1).All(id =>
            !EnemyPlanet(g, faction, g.Planets[id]) && !FleetsAt(g, id).Exists(f => Hostile(g, faction, f, seen)));
    }

    /// <summary>
    /// Defensive reactions: a planet an enemy fleet draws near, within two lanes, and that cannot
    /// hold alone, gets help from the fleets docked within two lanes of it, as long as they can
    /// hold it together. A fleet leaves a planet it guards only for a more valuable one. Returns
    /// the fleets sent.
    /// </summary>
    static HashSet<int> Defend(Galaxy g, Faction faction, List<Planet> own, HashSet<int> seen)
    {
        var sent = new HashSet<int>();
        var menace = own.ToDictionary(p => p.Id, p => Menace(g, faction, p, seen));
        foreach (var p in own.Where(p => menace[p.Id] > 0).OrderByDescending(Worth))
        {
            var need = menace[p.Id] * 1.1 - DefenseValue(g, p);
            if (need <= 0) continue;
            var around = Around(g, p.Id);
            var helpers = own.Where(q => q.Id != p.Id && around.Contains(q.Id))
                .SelectMany(q => FleetsAt(g, q.Id, faction).Where(f => !sent.Contains(f.Id)
                    && (Worth(q) < Worth(p) || DefenseValue(g, q) - FleetStrength(f) >= menace[q.Id] * 1.1)))
                .Where(f => SafeRoute(g, faction, f.At!.Value, p.Id, seen))
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
    /// The next offensive: a rival planet next to one of the faction's, where its fleets gather.
    /// It holds while both stay what they are; then the most valuable planet for what guards it.
    /// </summary>
    static (int Staging, int Target)? Offensive(Galaxy g, Faction faction, GalaxyAIState ai, List<Planet> own)
    {
        bool Rival(Planet t) => t.Owner is Faction o && o != faction && g.Factions.ContainsKey(o) && !AtPeace(g, faction, o);
        if (ai.Staging is int s0 && ai.Target is int t0 && g.Planets[s0].Owner == faction && Rival(g.Planets[t0])) return (s0, t0);
        var best = own.SelectMany(s => Neighbours(g, s.Id).Select(id => g.Planets[id]).Where(Rival).Select(t => (Staging: s, Target: t)))
            .OrderByDescending(x => Worth(x.Target) / (Threat(g, x.Target, faction) + 1000))
            .ThenByDescending(x => x.Staging.Station)
            .Select(x => ((int, int)?)(x.Staging.Id, x.Target.Id)).FirstOrDefault();
        ai.Staging = best?.Item1;
        ai.Target = best?.Item2;
        return best;
    }

    /// <summary>The nearest planet of the faction with a station, where a worn fleet mends fastest; any of its planets without one.</summary>
    static Planet? Home(Galaxy g, Faction faction, int from)
    {
        var hops = Hops(g, [from]);
        return g.Planets.Where(p => p.Owner == faction && hops.ContainsKey(p.Id))
            .OrderByDescending(p => p.Station > 0).ThenBy(p => hops[p.Id]).FirstOrDefault();
    }

    static void Command(Galaxy g, Faction faction, Dictionary<int, int> hops)
    {
        var ai = g.Factions[faction].AI!;
        var seen = VisiblePlanets(g, faction);
        var own = g.Planets.Where(p => p.Owner == faction).ToList();
        var sent = Defend(g, faction, own, seen);
        var plan = Offensive(g, faction, ai, own);
        var struck = false;
        var derelicts = g.Ongoing.Where(i => i.Kind == IncidentKind.Derelict).Select(i => i.Planet).ToList();
        foreach (var fleet in g.Fleets.Values.ToList())
        {
            if (fleet.Faction != faction || fleet.At is not int at || fleet.BattleId is not null || sent.Contains(fleet.Id)) continue;
            var near = Neighbours(g, at).Select(id => g.Planets[id]).ToList();
            var value = FleetStrength(fleet);
            // A worn fleet stays home to mend, unless its own planet needs it.
            var worn = fleet.Condition < 0.5;
            var home = g.Planets[at].Owner == faction;

            // Defend first: an own planet with enemies on it.
            var rescue = near.Find(p => p.Owner == faction && Threat(g, p, faction) > 0 && value >= Threat(g, p, faction) * 1.2);
            if (rescue is not null)
            {
                SendFleet(g, fleet.Id, rescue.Id);
                continue;
            }

            // Then the most valuable enemy planet the fleet can clearly beat; the Errants, only
            // with the full edge.
            var target = near
                .Where(p => (EnemyPlanet(g, faction, p) || Threat(g, p, faction) > 0)
                    && value >= Threat(g, p, faction) * (p.Owner == Faction.Errant ? 1.4 : Edge(ai)) + 300)
                .OrderByDescending(Worth).FirstOrDefault();
            if (target is not null && !worn && SendFleet(g, fleet.Id, target.Id).Ok)
            {
                struck |= target.Owner is not null;
                continue;
            }

            // A derelict within two lanes, for whoever gets there first: one ship goes to claim it.
            var wreck = derelicts.Where(d => d != at && Around(g, at).Contains(d) && !Heading(g, faction, d)
                && Threat(g, g.Planets[d], faction) == 0 && SafeRoute(g, faction, at, d, seen)).Select(d => (int?)d).FirstOrDefault();
            if (wreck is int d0)
            {
                var one = Battle.CountShips(fleet.Ships) >= 2 ? new ShipCounts { [Cheapest(fleet)] = 1 } : null;
                if (SendFleet(g, fleet.Id, d0, one).Ok) continue;
            }

            var free = near.Find(p => p.Owner is null && !Heading(g, faction, p.Id) && Threat(g, p, faction) == 0);
            if (free is not null && Battle.CountShips(fleet.Ships) >= 2)
            {
                SendFleet(g, fleet.Id, free.Id, new ShipCounts { [Cheapest(fleet)] = 1 });
                continue;
            }

            // Stranded at a truce partner's planet, or worn beyond its supply lines: back home.
            var partner = g.Planets[at].Owner is Faction o && o != faction && AtPeace(g, faction, o);
            if (partner || (worn && !home && !Supplied(g, fleet)))
            {
                if (Home(g, faction, at) is { } back) SendFleet(g, fleet.Id, back.Id);
                continue;
            }
            if (worn && home) continue;

            // Gather for the offensive at its staging planet, from the faction's own planets; at
            // the staging planet, wait for the edge (the strike itself is the attack above).
            if (plan is { } p0 && home)
            {
                if (at != p0.Staging && SafeRoute(g, faction, at, p0.Staging, seen)) SendFleet(g, fleet.Id, p0.Staging);
                continue;
            }

            // Move up towards the front through planets the faction can cross. Only from its own:
            // elsewhere the fleet stays to take the planet over, or a planet behind the front
            // would never be taken, its fleet called back again and again.
            var here = HopsOf(hops, at);
            var closer = near
                .Where(p => (p.Owner is null || p.Owner == faction) && HopsOf(hops, p.Id) < here)
                .OrderBy(p => HopsOf(hops, p.Id)).FirstOrDefault();
            if (closer is not null && here > 1 && home) SendFleet(g, fleet.Id, closer.Id);
        }
        ai.Idle = struck ? 0 : ai.Idle + ThinkInterval;
    }

    /// <summary>What a faction could bring to a war: its fleets, and a minute of its income.</summary>
    public static double Might(Galaxy g, Faction faction) =>
        g.Fleets.Values.Where(f => f.Faction == faction).Sum(FleetStrength) + IncomeOf(g, faction) * 60;

    /// <summary>Whether two factions face each other: planets two lanes apart at most, or a battle between them.</summary>
    static bool Contact(Galaxy g, Faction a, Faction b)
    {
        if (g.Battles.Exists(x => (x.Attacker == a && x.Defender == b) || (x.Attacker == b && x.Defender == a))) return true;
        var hops = Hops(g, g.Planets.Where(p => p.Owner == a).Select(p => p.Id));
        return g.Planets.Exists(p => p.Owner == b && hops.GetValueOrDefault(p.Id, 99) <= 2);
    }

    /// <summary>Whether the faction is at war, face to face, with a third galaxy faction besides `other`.</summary>
    static bool BusyElsewhere(Galaxy g, Faction faction, Faction other) =>
        g.Factions.Values.Any(t => !t.Eliminated && t.Id != faction && t.Id != other && !AtPeace(g, faction, t.Id) && Contact(g, faction, t.Id));

    /// <summary>
    /// An AI's answer to a truce: yes when it is the weaker, or busy with a third faction; never
    /// while it bears a grudge against one that broke a truce with it.
    /// </summary>
    public static Result AnswerTruce(Galaxy g, Faction faction, Faction from)
    {
        var ai = g.Factions[faction].AI!;
        if (ai.Grudges.GetValueOrDefault(from) > 0) return Result.Fail("Elle n'a pas oublié la trêve rompue");
        if (Might(g, faction) < Might(g, from) || BusyElsewhere(g, faction, from)) return Result.OK;
        return Result.Fail("Elle se croit la plus forte");
    }

    /// <summary>
    /// Now and then, an AI offers a truce to the strongest rival it faces: when it is clearly the
    /// weaker, or to have its hands free against a third faction.
    /// </summary>
    static void OfferTruces(Galaxy g, Faction faction)
    {
        var ai = g.Factions[faction].AI!;
        if (ai.OfferWait > 0) return;
        var mine = Might(g, faction);
        var rivals = g.Factions.Values
            .Where(f => !f.Eliminated && f.Id != faction && !ai.Grudges.ContainsKey(f.Id) && CanOfferTruce(g, faction, f.Id).Ok)
            .OrderByDescending(f => Might(g, f.Id));
        foreach (var r in rivals)
        {
            if (!Contact(g, faction, r.Id) || (mine >= Might(g, r.Id) * 0.7 && !BusyElsewhere(g, faction, r.Id))) continue;
            ai.OfferWait = OfferPause;
            ProposeTruce(g, faction, r.Id);
            return;
        }
    }

    public static void UpdateGalaxyAI(Galaxy g, double dt)
    {
        foreach (var f in g.Factions.Values)
        {
            if (f.AI is null || f.Eliminated) continue;
            f.AI.Timer -= dt;
            if (f.AI.Timer > 0) continue;
            f.AI.Timer = ThinkInterval * (Worlds.Difficulties[f.AI.Difficulty].Think / Worlds.Difficulties[Difficulty.Normal].Think);
            var hops = HopsToEnemy(g, f.Id);
            OfferTruces(g, f.Id);
            Spend(g, f.Id, hops);
            Command(g, f.Id, hops);
        }
    }
}
