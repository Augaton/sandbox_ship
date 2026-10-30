// Random galactic events (not in the web version). Every few minutes something happens: a
// derelict fleet waits at a neutral planet for whoever reaches it first, an Errant war band
// gathers next to a border planet and strikes it, an ion storm slows a lane, a trade boom
// doubles a planet's income. They draw from a random stream of their own, so that the battles
// and the AI keep theirs.
using SandboxShip.Sim.Data;

namespace SandboxShip.Sim;

public enum IncidentKind { Derelict, WarBand, IonStorm, TradeBoom }

/// <summary>A random event still running.</summary>
public sealed class Incident
{
    public IncidentKind Kind;
    /// <summary>Where it happens: for a storm, one end of its lane; for a war band, where it gathers.</summary>
    public int Planet;
    /// <summary>A storm's other lane end, or the planet a war band strikes.</summary>
    public int Other = -1;
    /// <summary>Galaxy seconds left: before a war band strikes, before the others are over.</summary>
    public double Remaining;
    /// <summary>A derelict fleet's ships.</summary>
    public ShipCounts Ships = [];
}

/// <summary>A random event begins at `Planet` (see Incident for `Other`); a war band's target belongs to `Faction`.</summary>
public sealed record IncidentStarted(IncidentKind Kind, int Planet, int Other = -1, Faction? Faction = null) : GalaxyEvent;
/// <summary>
/// A random event is over: run out, a derelict claimed by `Faction`, or a war band that
/// pillaged `Planet`, held by `Faction`.
/// </summary>
public sealed record IncidentEnded(IncidentKind Kind, int Planet, int Other = -1, Faction? Faction = null) : GalaxyEvent;

public sealed partial class Galaxy
{
    /// <summary>Whether random events happen: off for the tests of the web version's rules.</summary>
    public bool Incidents = true;
    /// <summary>Galaxy time of the next random event; 0 until it is drawn.</summary>
    public double NextIncident;
    /// <summary>State of the random events' own Park-Miller generator; 0 until it is seeded from Seed.</summary>
    public long IncidentSeed;
    public List<Incident> Ongoing = [];
}

public static partial class Galaxies
{
    /// <summary>The first event comes 4 to 6 minutes in, then one every 3 to 5 minutes.</summary>
    public const double FirstIncident = 240, IncidentGap = 180, IncidentSpread = 120;
    /// <summary>How long a derelict waits, a war band gathers, a storm and a boom last, in galaxy seconds.</summary>
    public const double DerelictTime = 240, WarBandWarning = 30, StormTime = 180, BoomTime = 240;
    /// <summary>Speed factor in an ion storm, income factor of a booming planet, condition of a derelict's hulls.</summary>
    public const double StormSpeed = 0.5, BoomIncome = 2, DerelictCondition = 0.6;

    /// <summary>Deterministic random number in [0, 1) for random events and ruins (Park-Miller, its own stream).</summary>
    public static double IncidentRand(Galaxy g)
    {
        if (g.IncidentSeed is <= 0 or >= 2147483647) g.IncidentSeed = 1 + Math.Abs(g.Seed * 48271 % 2147483646);
        return Rng.ParkMiller(ref g.IncidentSeed);
    }

    static int Draw(Galaxy g, int n) => Math.Min(n - 1, (int)Math.Floor(IncidentRand(g) * n));

    public static bool StormOn(Galaxy g, int a, int b) =>
        g.Ongoing.Count > 0 && g.Ongoing.Exists(i => i.Kind == IncidentKind.IonStorm && ((i.Planet == a && i.Other == b) || (i.Planet == b && i.Other == a)));

    public static bool Booming(Galaxy g, int planetId) =>
        g.Ongoing.Count > 0 && g.Ongoing.Exists(i => i.Kind == IncidentKind.TradeBoom && i.Planet == planetId);

    /// <summary>Income factor of a planet: doubled during a trade boom.</summary>
    public static double BoomOf(Galaxy g, Planet p) => Booming(g, p.Id) ? BoomIncome : 1;

    /// <summary>What a derelict holds, or a war band brings: more as the galaxy grows older.</summary>
    static ShipCounts Spoils(double time, bool band) => (time / 60, band) switch
    {
        ( < 12, false) => new() { [ShipClass.Frigate] = 2, [ShipClass.Battlecruiser] = 1 },
        ( < 12, true) => new() { [ShipClass.Frigate] = 3, [ShipClass.Battlecruiser] = 1 },
        ( < 25, false) => new() { [ShipClass.Frigate] = 3, [ShipClass.Battlecruiser] = 2 },
        ( < 25, true) => new() { [ShipClass.Frigate] = 4, [ShipClass.Battlecruiser] = 2 },
        (_, false) => new() { [ShipClass.Frigate] = 2, [ShipClass.Battlecruiser] = 2, [ShipClass.Dreadnought] = 1 },
        _ => new() { [ShipClass.Frigate] = 4, [ShipClass.Battlecruiser] = 2, [ShipClass.Dreadnought] = 1 },
    };

    static bool Empty(Galaxy g, int planetId) =>
        !g.Fleets.Values.Any(f => f.At == planetId) && !g.Ongoing.Exists(i => i.Planet == planetId || i.Other == planetId);

    static bool Held(Galaxy g, int planetId) => g.Planets[planetId].Owner is Faction o && g.Factions.ContainsKey(o);

    /// <summary>Running events go on; a new one comes when its time has come, among those the galaxy has room for.</summary>
    static void UpdateIncidents(Galaxy g, double dt)
    {
        foreach (var i in g.Ongoing.ToList())
        {
            if (i.Kind == IncidentKind.Derelict && Claim(g, i)) continue;
            if ((i.Remaining -= dt) > 0) continue;
            g.Ongoing.Remove(i);
            if (i.Kind == IncidentKind.WarBand)
            {
                if (FleetsAt(g, i.Planet, Faction.Errant).FirstOrDefault() is { } band) SendFleet(g, band.Id, i.Other);
            }
            else g.Events.Add(new IncidentEnded(i.Kind, i.Planet, i.Other));
        }
        Raid(g);
        if (!g.Incidents || g.Winner is not null) return;
        if (g.NextIncident <= 0) g.NextIncident = g.Time + FirstIncident + IncidentRand(g) * IncidentSpread;
        if (g.Time < g.NextIncident) return;
        g.NextIncident = g.Time + IncidentGap + IncidentRand(g) * IncidentSpread;
        StartIncident(g);
    }

    /// <summary>One of the events the galaxy has room for, at random: each needs its own kind of place.</summary>
    static void StartIncident(Galaxy g)
    {
        var options = new List<Action>();
        var neutral = g.Planets.Where(p => p.Owner is null && Empty(g, p.Id)).Select(p => p.Id).ToList();
        if (neutral.Count > 0)
        {
            options.Add(() =>
            {
                var at = neutral[Draw(g, neutral.Count)];
                g.Ongoing.Add(new Incident { Kind = IncidentKind.Derelict, Planet = at, Remaining = DerelictTime, Ships = Spoils(g.Time, false) });
                g.Events.Add(new IncidentStarted(IncidentKind.Derelict, at));
            });
        }
        // A war band gathers at a neutral planet next to the border planet it means to strike.
        var raids = (from p in g.Planets where Held(g, p.Id)
                     from n in Neighbours(g, p.Id) where g.Planets[n].Owner is null && Empty(g, n)
                     select (Target: p, From: n)).ToList();
        if (raids.Count > 0)
        {
            options.Add(() =>
            {
                var (target, from) = raids[Draw(g, raids.Count)];
                AddShipsAt(g, Faction.Errant, from, Spoils(g.Time, true));
                g.Ongoing.Add(new Incident { Kind = IncidentKind.WarBand, Planet = from, Other = target.Id, Remaining = WarBandWarning });
                g.Events.Add(new IncidentStarted(IncidentKind.WarBand, from, target.Id, target.Owner));
            });
        }
        var lanes = g.Lanes.Where(l => (Held(g, l.A) || Held(g, l.B)) && !StormOn(g, l.A, l.B)).ToList();
        if (lanes.Count > 0)
        {
            options.Add(() =>
            {
                var l = lanes[Draw(g, lanes.Count)];
                g.Ongoing.Add(new Incident { Kind = IncidentKind.IonStorm, Planet = l.A, Other = l.B, Remaining = StormTime });
                g.Events.Add(new IncidentStarted(IncidentKind.IonStorm, l.A, l.B));
            });
        }
        var held = g.Planets.Where(p => Held(g, p.Id) && !Booming(g, p.Id)).ToList();
        if (held.Count > 0)
        {
            options.Add(() =>
            {
                var p = held[Draw(g, held.Count)];
                g.Ongoing.Add(new Incident { Kind = IncidentKind.TradeBoom, Planet = p.Id, Remaining = BoomTime });
                g.Events.Add(new IncidentStarted(IncidentKind.TradeBoom, p.Id, -1, p.Owner));
            });
        }
        if (options.Count > 0) options[Draw(g, options.Count)]();
    }

    /// <summary>The first galaxy fleet docked at a derelict, out of battle, takes its ships in.</summary>
    static bool Claim(Galaxy g, Incident derelict)
    {
        var by = FleetsAt(g, derelict.Planet).Where(f => g.Factions.ContainsKey(f.Faction)).OrderBy(f => f.ArrivedAt).FirstOrDefault();
        if (by is null) return false;
        AddShipsAt(g, by.Faction, derelict.Planet, derelict.Ships, null, DerelictCondition);
        g.Ongoing.Remove(derelict);
        g.Events.Add(new IncidentEnded(IncidentKind.Derelict, derelict.Planet, -1, by.Faction));
        return true;
    }

    /// <summary>
    /// War bands never take planets. One at rest at a galaxy faction's planet, with nothing left
    /// there to fight it, pillages it: its mines are lost. Then, or once back where it gathered,
    /// it vanishes into the dark. The Errants' guard, on their own planet, stays.
    /// </summary>
    static void Raid(Galaxy g)
    {
        foreach (var band in g.Fleets.Values.Where(f => f.Faction == Faction.Errant && f.At is int && f.BattleId is null).ToList())
        {
            var at = g.Planets[band.At!.Value];
            if (at.Owner == Faction.Errant || g.Ongoing.Exists(i => i.Kind == IncidentKind.WarBand && i.Planet == at.Id) || BattleAt(g, at.Id)) continue;
            if (Held(g, at.Id) && Clash(g, FleetsAt(g, at.Id), Fortified(at) ? at.Owner : null) is not null) continue;
            g.Fleets.Remove(band.Id);
            if (!Held(g, at.Id)) continue;
            at.Mines = 0;
            g.Events.Add(new IncidentEnded(IncidentKind.WarBand, at.Id, -1, at.Owner));
        }
    }
}
