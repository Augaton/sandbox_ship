// Truces (not in the web version): two galaxy factions agree not to fight for a while. Sides at
// peace never meet in battle, at a planet or on a lane; neither sends fleets to the other's
// planets, nor takes them. Never with the Errants. With two factions left, a truce is a ceasefire.
using SandboxShip.Sim.Data;

namespace SandboxShip.Sim;

/// <summary>A non-aggression pact between two galaxy factions.</summary>
public sealed class Truce
{
    public Faction A, B;
    /// <summary>Galaxy seconds left.</summary>
    public double Remaining;
}

/// <summary>A truce offered to a faction that answers in its own time: the player.</summary>
public sealed class TruceOffer
{
    public Faction From, To;
    /// <summary>Galaxy seconds before the offer lapses.</summary>
    public double Remaining;
}

public sealed record TruceOffered(Faction From, Faction To) : GalaxyEvent;
public sealed record TruceSigned(Faction A, Faction B) : GalaxyEvent;
/// <summary>A truce ran out, or `BrokenBy` broke it.</summary>
public sealed record TruceEnded(Faction A, Faction B, Faction? BrokenBy) : GalaxyEvent;

public sealed partial class Galaxy
{
    public List<Truce> Truces = [];
    public List<TruceOffer> Offers = [];
}

public sealed partial class GalaxyAIState
{
    /// <summary>Galaxy seconds during which the AI refuses any truce with each faction that broke one with it.</summary>
    public Dictionary<Faction, double> Grudges = [];
    /// <summary>Galaxy seconds before the AI offers a truce again.</summary>
    public double OfferWait;
}

public static partial class Galaxies
{
    /// <summary>
    /// Galaxy seconds a truce lasts, an AI wronged by a broken one bears a grudge, an offer to the
    /// player waits for an answer, and an AI waits between two offers.
    /// </summary>
    public const double TruceTime = 600, GrudgeTime = 900, OfferTime = 60, OfferPause = 300;

    public static Truce? TruceOf(Galaxy g, Faction a, Faction b) => g.Truces.Find(t => (t.A == a && t.B == b) || (t.A == b && t.B == a));

    /// <summary>Whether two factions are bound by a truce.</summary>
    public static bool AtPeace(Galaxy g, Faction a, Faction b) => g.Truces.Count > 0 && a != b && TruceOf(g, a, b) is not null;

    /// <summary>Whether `from` may offer `to` a truce now, and if not, why.</summary>
    public static Result CanOfferTruce(Galaxy g, Faction from, Faction to)
    {
        if (from == to || !g.Factions.TryGetValue(from, out var a) || !g.Factions.TryGetValue(to, out var b)) return Result.Fail("Aucune trêve possible");
        if (a.Eliminated || b.Eliminated) return Result.Fail("Faction éliminée");
        if (TruceOf(g, from, to) is not null) return Result.Fail("Trêve déjà en cours");
        if (g.Offers.Exists(o => o.From == from && o.To == to)) return Result.Fail("Offre déjà faite");
        return Result.OK;
    }

    /// <summary>
    /// Offers a truce. An AI answers at once: Ok means signed, else the reason it refused. Anyone
    /// else (the player) is left an offer to accept or refuse; an offer they made already is accepted.
    /// </summary>
    public static Result ProposeTruce(Galaxy g, Faction from, Faction to)
    {
        var r = CanOfferTruce(g, from, to);
        if (!r.Ok) return r;
        if (g.Offers.Exists(o => o.From == to && o.To == from)) return AcceptTruce(g, from, to);
        if (g.Factions[to].AI is not null)
        {
            var answer = GalaxyAi.AnswerTruce(g, to, from);
            if (answer.Ok) Sign(g, from, to);
            return answer;
        }
        g.Offers.Add(new TruceOffer { From = from, To = to, Remaining = OfferTime });
        g.Events.Add(new TruceOffered(from, to));
        return Result.OK;
    }

    /// <summary>`faction` accepts the truce `from` offered it.</summary>
    public static Result AcceptTruce(Galaxy g, Faction faction, Faction from)
    {
        var offer = g.Offers.Find(o => o.From == from && o.To == faction);
        if (offer is null) return Result.Fail("Aucune offre");
        g.Offers.Remove(offer);
        Sign(g, from, faction);
        return Result.OK;
    }

    public static void RefuseTruce(Galaxy g, Faction faction, Faction from) => g.Offers.RemoveAll(o => o.From == from && o.To == faction);

    /// <summary>Breaks a truce at once. An AI wronged so refuses any truce with the breaker for a while.</summary>
    public static Result BreakTruce(Galaxy g, Faction by, Faction other)
    {
        var truce = TruceOf(g, by, other);
        if (truce is null) return Result.Fail("Aucune trêve");
        g.Truces.Remove(truce);
        if (g.Factions.TryGetValue(other, out var o) && o.AI is not null) o.AI.Grudges[by] = GrudgeTime;
        g.Events.Add(new TruceEnded(by, other, by));
        return Result.OK;
    }

    static void Sign(Galaxy g, Faction a, Faction b)
    {
        g.Offers.RemoveAll(o => (o.From == a && o.To == b) || (o.From == b && o.To == a));
        g.Truces.Add(new Truce { A = a, B = b, Remaining = TruceTime });
        g.Events.Add(new TruceSigned(a, b));
    }

    /// <summary>Truces and offers run out; an eliminated faction's go with it. Grudges fade.</summary>
    static void UpdateDiplomacy(Galaxy g, double dt)
    {
        bool Gone(Faction f) => !g.Factions.TryGetValue(f, out var gf) || gf.Eliminated;
        foreach (var t in g.Truces.ToList())
        {
            t.Remaining -= dt;
            if (Gone(t.A) || Gone(t.B)) g.Truces.Remove(t);
            else if (t.Remaining <= 0)
            {
                g.Truces.Remove(t);
                g.Events.Add(new TruceEnded(t.A, t.B, null));
            }
        }
        foreach (var o in g.Offers) o.Remaining -= dt;
        g.Offers.RemoveAll(o => o.Remaining <= 0 || Gone(o.From) || Gone(o.To));
        foreach (var f in g.Factions.Values)
        {
            if (f.AI is not { } ai) continue;
            ai.OfferWait = Math.Max(0, ai.OfferWait - dt);
            foreach (var k in ai.Grudges.Keys.ToList())
            {
                if ((ai.Grudges[k] -= dt) <= 0) ai.Grudges.Remove(k);
            }
        }
    }

    /// <summary>
    /// Who fights at a planet: its owner when it defends it, else the first fleet to have come,
    /// against the last fleet to come of a side at war with it. Sides at peace leave each other
    /// be: the next one in line defends then. Null when nobody here is at war.
    /// </summary>
    static (Fleet Attacker, Faction Defender)? Clash(Galaxy g, List<Fleet> here, Faction? defending)
    {
        var defenders = here.Select(f => f.Faction).Distinct();
        if (defending is Faction o) defenders = defenders.Prepend(o);
        foreach (var d in defenders)
        {
            var last = here.LastOrDefault(f => f.Faction != d && !AtPeace(g, f.Faction, d));
            if (last is not null) return (last, d);
        }
        return null;
    }
}
