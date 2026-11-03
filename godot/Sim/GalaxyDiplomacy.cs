// Truces (not in the web version): two galaxy factions agree not to fight for a while. Sides at
// peace never meet in battle, at a planet or on a lane; neither sends fleets to the other's
// planets, nor takes them. Never with the Errants, nor between allies, who never fight anyway.
// With two factions left, a truce is a ceasefire.
using SandboxShip.Sim.Data;

namespace SandboxShip.Sim;

/// <summary>A non-aggression pact between two galaxy factions.</summary>
public sealed class Truce
{
    public Side A, B;
    /// <summary>Galaxy seconds left.</summary>
    public double Remaining;
}

/// <summary>A truce offered to a faction that answers in its own time: the player.</summary>
public sealed class TruceOffer
{
    public Side From, To;
    /// <summary>Galaxy seconds before the offer lapses.</summary>
    public double Remaining;
}

public sealed record TruceOffered(Side From, Side To) : GalaxyEvent;
public sealed record TruceSigned(Side A, Side B) : GalaxyEvent;
/// <summary>A truce ran out, or `BrokenBy` broke it.</summary>
public sealed record TruceEnded(Side A, Side B, Side? BrokenBy) : GalaxyEvent;

public sealed partial class Galaxy
{
    public List<Truce> Truces = [];
    public List<TruceOffer> Offers = [];
}

public sealed partial class GalaxyAIState
{
    /// <summary>Galaxy seconds during which the AI refuses any truce with each faction that broke one with it.</summary>
    public Dictionary<Side, double> Grudges = [];
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

    public static Truce? TruceOf(Galaxy g, Side a, Side b) => g.Truces.Find(t => (t.A == a && t.B == b) || (t.A == b && t.B == a));

    /// <summary>Whether two sides are bound by a truce.</summary>
    public static bool AtPeace(Galaxy g, Side a, Side b) => g.Truces.Count > 0 && a != b && TruceOf(g, a, b) is not null;

    /// <summary>A side's race: its galaxy faction's; a side with none (the Errants) is its own race's (see Side).</summary>
    public static Faction RaceOf(Galaxy g, Side side) => g.Factions.TryGetValue(side, out var f) ? f.Race : Worlds.OwnRace(side);

    /// <summary>A side's team: 0 (alone) for a side with no galaxy faction, the Errants.</summary>
    public static int TeamOf(Galaxy g, Side side) => g.Factions.TryGetValue(side, out var f) ? f.Team : 0;

    /// <summary>The same side, or two sides of the same team other than 0: they never fight, and see together.</summary>
    public static bool Allies(Galaxy g, Side a, Side b) => a == b || (TeamOf(g, a) is var t && t != 0 && t == TeamOf(g, b));

    /// <summary>Whether two sides fight when they meet: neither allies nor bound by a truce. The Errants are at war with all.</summary>
    public static bool AtWar(Galaxy g, Side a, Side b) => !Allies(g, a, b) && !AtPeace(g, a, b);

    /// <summary>Whether `side` won the campaign: its own victory or its team's.</summary>
    public static bool Won(Galaxy g, Side side) => g.Winner is Side w && Allies(g, w, side);

    /// <summary>Whether `from` may offer `to` a truce now, and if not, why.</summary>
    public static Result CanOfferTruce(Galaxy g, Side from, Side to)
    {
        if (Allies(g, from, to) || !g.Factions.TryGetValue(from, out var a) || !g.Factions.TryGetValue(to, out var b)) return Result.Fail("Aucune trêve possible");
        if (a.Eliminated || b.Eliminated) return Result.Fail("Faction éliminée");
        if (TruceOf(g, from, to) is not null) return Result.Fail("Trêve déjà en cours");
        if (g.Offers.Exists(o => o.From == from && o.To == to)) return Result.Fail("Offre déjà faite");
        return Result.OK;
    }

    /// <summary>
    /// Offers a truce. An AI answers at once: Ok means signed, else the reason it refused. Anyone
    /// else (the player) is left an offer to accept or refuse; an offer they made already is accepted.
    /// </summary>
    public static Result ProposeTruce(Galaxy g, Side from, Side to)
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

    /// <summary>`side` accepts the truce `from` offered it.</summary>
    public static Result AcceptTruce(Galaxy g, Side side, Side from)
    {
        var offer = g.Offers.Find(o => o.From == from && o.To == side);
        if (offer is null) return Result.Fail("Aucune offre");
        g.Offers.Remove(offer);
        Sign(g, from, side);
        return Result.OK;
    }

    public static void RefuseTruce(Galaxy g, Side side, Side from) => g.Offers.RemoveAll(o => o.From == from && o.To == side);

    /// <summary>Breaks a truce at once. An AI wronged so refuses any truce with the breaker for a while.</summary>
    public static Result BreakTruce(Galaxy g, Side by, Side other)
    {
        var truce = TruceOf(g, by, other);
        if (truce is null) return Result.Fail("Aucune trêve");
        g.Truces.Remove(truce);
        if (g.Factions.TryGetValue(other, out var o) && o.AI is not null) o.AI.Grudges[by] = GrudgeTime;
        g.Events.Add(new TruceEnded(by, other, by));
        return Result.OK;
    }

    static void Sign(Galaxy g, Side a, Side b)
    {
        g.Offers.RemoveAll(o => (o.From == a && o.To == b) || (o.From == b && o.To == a));
        g.Truces.Add(new Truce { A = a, B = b, Remaining = TruceTime });
        g.Events.Add(new TruceSigned(a, b));
    }

    /// <summary>Truces and offers run out; an eliminated faction's go with it. Grudges fade.</summary>
    static void UpdateDiplomacy(Galaxy g, double dt)
    {
        bool Gone(Side f) => !g.Factions.TryGetValue(f, out var gf) || gf.Eliminated;
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
    /// against the last fleet to come of a side at war with it. Allies and sides at peace leave
    /// each other be: the next one in line defends then. Null when nobody here is at war.
    /// </summary>
    static (Fleet Attacker, Side Defender)? Clash(Galaxy g, List<Fleet> here, Side? defending)
    {
        var defenders = here.Select(f => f.Side).Distinct();
        if (defending is Side o) defenders = defenders.Prepend(o);
        foreach (var d in defenders)
        {
            var last = here.LastOrDefault(f => AtWar(g, f.Side, d));
            if (last is not null) return (last, d);
        }
        return null;
    }
}
