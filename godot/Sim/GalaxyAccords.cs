// The diplomacy of this version beyond truces, alliances, trade and tributes: shared vision, a
// war waged together on a third faction, an ultimatum (pay or cede a planet, or war), an AI's
// request for help, and offers of several terms at once. Every faction has a reputation the
// others see, made of the word it kept or broke; every AI knows why it trusts each faction as
// much as it does (TrustCause); and the galaxy keeps the chronicle of what was signed, broken
// and refused, for the diplomacy screen.
using System.Text.Json.Serialization;
using SandboxShip.Sim.Data;

namespace SandboxShip.Sim;

/// <summary>The terms an offer of several bundles together (DealKind.Bundle), accepted or refused as one.</summary>
[Flags]
public enum DealTerms : byte { None = 0, Truce = 1, Trade = 2, Vision = 4, Tribute = 8 }

/// <summary>What moved an AI's trust in a faction: shown in the diplomacy screen, what it did and what it is.</summary>
public enum TrustCause { Peace, Trade, Alliance, JointWar, Gifts, Demands, Attacks, Betrayal, Witness, Help, Ultimatum }

/// <summary>What an AI says to a player (AiSaid): its words come from its personality, in the view.</summary>
public enum Remark { Warning, Threat, Thanks, Betrayed, Welcome, Coalition, AskHelp, Grateful, Disappointed, Defiant }

/// <summary>A line of the chronicle: something signed, run out, broken, refused, issued, kept or said.</summary>
public enum ChronicleKind { Signed, Expired, Broken, Ended, Refused, Defaulted, Issued, Accepted, Defied, Ceded, Asked, Kept, Failed, Said }

/// <summary>
/// What happened between `A` and `B`, `Time` into the campaign: `Deal` signed, ended or broken
/// (`By` who broke it), a refusal, an ultimatum, a request for help… `Amount`: credits;
/// `Extra`: the planet of a cession or a request, the target of a joint war, a remark.
/// </summary>
public sealed class DiploEntry
{
    public double Time;
    public ChronicleKind Kind;
    public DealKind Deal;
    public Side A, B;
    public Side? By;
    public double Amount;
    public int Extra = -1;
}

/// <summary>A player who promised an AI to defend one of its planets: kept once a fleet of theirs stands there in time.</summary>
public sealed class HelpPromise
{
    public Side Asker, Helper;
    public int Planet;
    public double Remaining;
}

/// <summary>An ultimatum ran out or was answered: `Accepted`, or defied, and war follows.</summary>
public sealed record UltimatumEnded(Side From, Side To, bool Accepted) : GalaxyEvent;
/// <summary>A planet changed hands without a battle: ceded under an ultimatum.</summary>
public sealed record PlanetCeded(int PlanetId, Side From, Side To) : GalaxyEvent;
/// <summary>A promise of help kept (`Kept`) or broken.</summary>
public sealed record HelpAnswered(Side Asker, Side Helper, int PlanetId, bool Kept) : GalaxyEvent;
/// <summary>An AI speaks to a player: a warning, a threat, its thanks…</summary>
public sealed record AiSaid(Side From, Side To, Remark What) : GalaxyEvent;

public sealed partial class Galaxy
{
    /// <summary>
    /// Diplomatic changes so far (an event, a reputation): what an AI would answer is worked out
    /// again after one. Trust that battles wear away is not one: it moved at every battle, and a
    /// second's delay changes nothing there. Not saved.
    /// </summary>
    public int DiploChanges;
    /// <summary>What was signed, broken, refused… between factions, the oldest first (ChronicleMax at most).</summary>
    public List<DiploEntry> Chronicle = [];
    public List<HelpPromise> Promises = [];
}

public sealed partial class GalaxyAIState
{
    /// <summary>What moved its trust in each faction lately, cause by cause: each fades as trust does.</summary>
    public Dictionary<Side, Dictionary<TrustCause, double>> Why = [];
    /// <summary>The galaxy time at which the hard AI breaks its truce with each faction it warned.</summary>
    public Dictionary<Side, double> Breaking = [];
    /// <summary>Galaxy seconds before the AI asks for help again, and before it issues another ultimatum.</summary>
    public double AskWait, ThreatWait;
    /// <summary>Galaxy seconds during which the AI, having given in to each faction's ultimatum, will not again (UltimatumMemory).</summary>
    public Dictionary<Side, double> Yielded = [];
    /// <summary>On the host, what the AI would answer each viewer and its peace price, worked out at a galaxy time: a snapshot reuses them for a second.</summary>
    [JsonIgnore]
    internal Dictionary<Side, (double At, int Changes, double Price, DealPreview Preview)> Minds = [];
    /// <summary>On a replica, what the AI would answer the viewer (sent by the host, who works it out); null on the host.</summary>
    [JsonIgnore]
    public DealPreview? Preview;
}

public sealed partial class GalaxyFaction
{
    /// <summary>The word it kept and broke, -100 to 100, seen by all: every AI weighs it.</summary>
    public double Reputation;
}

public static partial class Galaxies
{
    /// <summary>Galaxy seconds a joint war lasts, an ultimatum waits for its answer, and a player has to keep a promise of help.</summary>
    public const double JointWarTime = 600, UltimatumTime = 90, HelpTime = 180;
    /// <summary>
    /// Galaxy seconds an AI that gave in to an ultimatum refuses another from the same faction:
    /// asked again and again, a strong player drained a weak AI's credits every few seconds.
    /// </summary>
    public const double UltimatumMemory = 900;
    /// <summary>How much stronger than the one it is made to an ultimatum's maker must be.</summary>
    public const double UltimatumEdge = 1.5;
    /// <summary>The longest chronicle the galaxy keeps.</summary>
    public const int ChronicleMax = 400;
    /// <summary>What reputation weighs in an AI's regard for a faction, and how fast it drifts back to nothing (a second).</summary>
    public const double ReputationWeight = 0.25, ReputationFade = 0.01;
    /// <summary>What a second of joint war adds to the partners' trust.</summary>
    const double TrustJointWar = 0.08;

    /// <summary>What an AI makes of a faction: its trust, and what everyone knows of the faction's word.</summary>
    public static double Regard(Galaxy g, Side ai, Side other) =>
        TrustOf(g, ai, other) + (g.Factions.TryGetValue(other, out var f) ? f.Reputation * ReputationWeight : 0);

    /// <summary>A faction's reputation moved by `delta`, within ±TrustMax.</summary>
    static void Repute(Galaxy g, Side side, double delta)
    {
        if (!g.Factions.TryGetValue(side, out var f)) return;
        f.Reputation = Math.Clamp(f.Reputation + delta, -TrustMax, TrustMax);
        g.DiploChanges++;
    }

    /// <summary>The joint war `a` and `b` wage together, if any.</summary>
    public static Pact? JointWarOf(Galaxy g, Side a, Side b) => PactOf(g, DealKind.JointWar, a, b);

    /// <summary>Whether `side` swore a joint war against `target` with anyone.</summary>
    public static bool Sworn(Galaxy g, Side side, Side target)
    {
        foreach (var p in g.Pacts)
        {
            if (p.Kind == DealKind.JointWar && p.Target == target && (p.A == side || p.B == side)) return true;
        }
        return false;
    }

    /// <summary>Whether two sides share their sight: allies, or a pact of vision.</summary>
    public static bool SharesSight(Galaxy g, Side a, Side b) => Allies(g, a, b) || (g.Pacts.Count > 0 && PactOf(g, DealKind.Vision, a, b) is not null);

    /// <summary>The planet an ultimatum may demand: the other's, not its last, next to one of the maker's, and no battle there.</summary>
    public static Result CanDemandPlanet(Galaxy g, Side from, Side to, int planet)
    {
        if ((uint)planet >= (uint)g.Planets.Count || g.Planets[planet].Owner != to) return Result.Fail("Ce n'est pas une de ses planètes");
        if (g.Planets.Count(p => p.Owner == to) < 2) return Result.Fail("C'est sa dernière planète");
        if (!Neighbours(g, planet).Exists(n => g.Planets[n].Owner == from)) return Result.Fail("Planète trop loin de tes frontières");
        if (g.Battles.Exists(b => b.PlanetId == planet && b.Lane is null)) return Result.Fail("On se bat déjà là-bas");
        return Result.OK;
    }

    /// <summary>Whether this bundle may be offered: known terms, each possible with the others (a truce brings the peace trade and vision need).</summary>
    static Result CanBundle(Galaxy g, Side from, Side to, DealTerms terms, double amount, bool perMinute, GalaxyFaction maker)
    {
        if (terms == DealTerms.None || (terms & ~(DealTerms.Truce | DealTerms.Trade | DealTerms.Vision | DealTerms.Tribute)) != 0) return Result.Fail("Aucun terme");
        var peace = !AtWar(g, from, to) || (terms & DealTerms.Truce) != 0;
        if ((terms & DealTerms.Truce) != 0 && (Allied(g, from, to) || TruceOf(g, from, to) is not null)) return Result.Fail("Trêve déjà en cours");
        if ((terms & DealTerms.Trade) != 0)
        {
            if (PactOf(g, DealKind.Trade, from, to) is not null) return Result.Fail("Accord commercial déjà en cours");
            if (!peace) return Result.Fail("Il faut d'abord la paix");
        }
        if ((terms & DealTerms.Vision) != 0)
        {
            if (Allied(g, from, to) || PactOf(g, DealKind.Vision, from, to) is not null) return Result.Fail("Vision déjà partagée");
            if (!peace) return Result.Fail("Il faut d'abord la paix");
        }
        if ((terms & DealTerms.Tribute) != 0)
        {
            if (!double.IsFinite(amount) || amount <= 0 || amount > (perMinute ? MaxTributePerMinute : MaxTribute)) return Result.Fail("Montant invalide");
            if (TributeOf(g, from, to) is not null) return Result.Fail("Tribut déjà en cours");
            if (!perMinute && maker.Credits < amount) return Result.Fail("Crédits insuffisants");
        }
        return Result.OK;
    }

    /// <summary>The terms of a bundle take effect: the truce first, which trade and vision need, the tribute last (it may prolong the truce).</summary>
    static Result ConcludeBundle(Galaxy g, Side from, Side to, DealTerms terms, double amount, bool perMinute)
    {
        if ((terms & DealTerms.Tribute) != 0 && !perMinute && g.Factions[from].Credits < amount) return Result.Fail("Crédits insuffisants");
        if ((terms & DealTerms.Truce) != 0 && AtWar(g, from, to)) Sign(g, from, to);
        if ((terms & DealTerms.Trade) != 0) Conclude(g, from, to, DealKind.Trade, 0, false, null, null);
        if ((terms & DealTerms.Vision) != 0) Conclude(g, from, to, DealKind.Vision, 0, false, null, null);
        if ((terms & DealTerms.Tribute) != 0) Conclude(g, from, to, DealKind.Tribute, amount, perMinute, null, null);
        return Result.OK;
    }

    /// <summary>An ultimatum `to` gives in to: credits paid, or the planet ceded; a truce if they were at war. Its maker is resented.</summary>
    static Result Yield(Galaxy g, Side from, Side to, double amount, int? planet)
    {
        var victim = g.Factions[to];
        if (planet is int p)
        {
            var r = CanDemandPlanet(g, from, to, p);
            if (!r.Ok) return r;
            Cede(g, g.Planets[p], from);
        }
        else
        {
            if (victim.Credits < amount) return Result.Fail("Crédits insuffisants");
            victim.Credits -= amount;
            g.Factions[from].Credits += amount;
        }
        if (AtWar(g, from, to)) Sign(g, from, to);
        Trusts(g, to, from, -15, cause: TrustCause.Ultimatum);
        if (victim.AI is { } ai) ai.Yielded[from] = UltimatumMemory;
        Note(g, new UltimatumEnded(from, to, true));
        return Result.OK;
    }

    /// <summary>An ultimatum defied, or let run out: the war its maker announced. Not a betrayal: it was said.</summary>
    static void Defied(Galaxy g, Side from, Side to)
    {
        Note(g, new UltimatumEnded(from, to, false));
        if (TruceOf(g, from, to) is { } truce)
        {
            g.Truces.Remove(truce);
            Note(g, new TruceEnded(from, to, from));
            PeaceLost(g, from, to, from);
        }
        Say(g, to, from, Remark.Defiant);
    }

    /// <summary>A planet changes hands without a fight, its works kept; the former owner's fleets there leave for its nearest planet.</summary>
    static void Cede(Galaxy g, Planet planet, Side to)
    {
        var from = planet.Owner!.Value;
        planet.Owner = to;
        planet.Units = [];
        planet.Buildings = [];
        planet.Repeat = [];
        planet.Occupation = null;
        planet.Rally = null;
        var home = NearestOwn(g, from, planet.Id);
        foreach (var f in g.Fleets.Values.Where(f => f.Side == from && f.At == planet.Id && f.BattleId is null).ToList())
        {
            if (home is int h) SendFleet(g, f.Id, h);
        }
        Note(g, new PlanetCeded(planet.Id, from, to));
    }

    /// <summary>The planet of `side` fewest lanes from `from`, `from` itself left out.</summary>
    static int? NearestOwn(Galaxy g, Side side, int from)
    {
        var seen = new HashSet<int> { from };
        var next = new Queue<int>();
        next.Enqueue(from);
        while (next.Count > 0)
        {
            var at = next.Dequeue();
            if (at != from && g.Planets[at].Owner == side) return at;
            foreach (var n in Neighbours(g, at))
            {
                if (seen.Add(n)) next.Enqueue(n);
            }
        }
        return null;
    }

    /// <summary>A request for help answered yes: credits paid now, or a promise to defend the planet in time.</summary>
    static Result Help(Galaxy g, Side asker, Side helper, double amount, int? planet)
    {
        if (planet is int p)
        {
            g.Promises.RemoveAll(x => x.Asker == asker && x.Helper == helper);
            g.Promises.Add(new HelpPromise { Asker = asker, Helper = helper, Planet = p, Remaining = HelpTime });
            return Result.OK;
        }
        var h = g.Factions[helper];
        if (h.Credits < amount) return Result.Fail("Crédits insuffisants");
        h.Credits -= amount;
        g.Factions[asker].Credits += amount;
        Trusts(g, asker, helper, Math.Min(20, amount / 30), cause: TrustCause.Help);
        Repute(g, helper, 3);
        Note(g, new HelpAnswered(asker, helper, -1, true));
        Say(g, asker, helper, Remark.Grateful);
        return Result.OK;
    }

    /// <summary>
    /// A war waged together ends: run out or its target gone (the word kept, both gain from it),
    /// or broken by `by`, who made peace with the target behind its partner's back.
    /// </summary>
    static void EndJointWar(Galaxy g, Pact war, Side? by)
    {
        g.Pacts.Remove(war);
        if (by is Side b)
        {
            var partner = war.A == b ? war.B : war.A;
            Trusts(g, partner, b, -40, cause: TrustCause.Betrayal);
            Repute(g, b, -20);
            Say(g, partner, b, Remark.Betrayed);
        }
        else
        {
            Trusts(g, war.A, war.B, 10, cause: TrustCause.JointWar);
            Trusts(g, war.B, war.A, 10, cause: TrustCause.JointWar);
            Repute(g, war.A, 3);
            Repute(g, war.B, 3);
        }
        Note(g, new PactEnded(DealKind.JointWar, war.A, war.B, by, war.Target));
    }

    /// <summary>`a` and `b` make peace: a joint war one of them swore against the other is broken by the one who swore it.</summary>
    static void PeaceWithTarget(Galaxy g, Side a, Side b)
    {
        foreach (var war in g.Pacts.Where(p => p.Kind == DealKind.JointWar && (p.Target == b && (p.A == a || p.B == a) || p.Target == a && (p.A == b || p.B == b))).ToList())
            EndJointWar(g, war, war.Target == b ? a : b);
    }

    /// <summary>Joint wars run out, or end with their target; promises of help are kept, or broken when their time is out.</summary>
    static void UpdateAccords(Galaxy g, double dt)
    {
        bool Gone(Side f) => !g.Factions.TryGetValue(f, out var gf) || gf.Eliminated;
        // Backwards by index: an end removes its pact or promise, and nothing is copied at every step.
        for (var i = g.Pacts.Count - 1; i >= 0; i--)
        {
            var war = g.Pacts[i];
            if (war.Kind != DealKind.JointWar) continue;
            war.Remaining -= dt;
            if (Gone(war.A) || Gone(war.B)) g.Pacts.RemoveAt(i);
            else if (war.Remaining <= 0 || war.Target is not Side t || Gone(t)) EndJointWar(g, war, null);
            i = Math.Min(i, g.Pacts.Count);
        }
        for (var i = g.Promises.Count - 1; i >= 0; i--)
        {
            var p = g.Promises[i];
            p.Remaining -= dt;
            if (Gone(p.Asker) || Gone(p.Helper) || g.Planets[p.Planet].Owner != p.Asker)
            {
                g.Promises.RemoveAt(i);
                continue;
            }
            var kept = false;
            foreach (var f in g.Fleets.Values)
            {
                if (f.Side == p.Helper && f.At == p.Planet) kept = true;
            }
            if (!kept && p.Remaining > 0) continue;
            g.Promises.RemoveAt(i);
            Trusts(g, p.Asker, p.Helper, kept ? 20 : -25, cause: TrustCause.Help);
            Repute(g, p.Helper, kept ? 8 : -10);
            Note(g, new HelpAnswered(p.Asker, p.Helper, p.Planet, kept));
            Say(g, p.Asker, p.Helper, kept ? Remark.Grateful : Remark.Disappointed);
        }
        foreach (var f in g.Factions.Values)
        {
            if (f.Reputation != 0) f.Reputation = f.Reputation > 0 ? Math.Max(0, f.Reputation - ReputationFade * dt) : Math.Min(0, f.Reputation + ReputationFade * dt);
            if (f.AI is not { } ai) continue;
            ai.AskWait = Math.Max(0, ai.AskWait - dt);
            ai.ThreatWait = Math.Max(0, ai.ThreatWait - dt);
            foreach (var causes in ai.Why.Values) Settle(causes, TrustFade * dt);
        }
    }

    /// <summary>What drifts back to nothing by `by`, either way; at nothing, it is forgotten.</summary>
    static void Settle<T>(Dictionary<T, double> left, double by) where T : notnull
    {
        if (left.Count == 0) return;
        // In place, as Fade: no copy of the keys for every AI and every faction at every step.
        foreach (var k in left.Keys)
        {
            ref var v = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrNullRef(left, k);
            if (Math.Abs(v) <= by) left.Remove(k);
            else v -= Math.Sign(v) * by;
        }
    }

    /// <summary>
    /// Whether a chronicle line's extra is one its kind may have: a planet of the galaxy for a
    /// cession, an ultimatum or a request for help, a side for a joint war, a known remark for
    /// words said, none otherwise. The view looks them up: one out of range would bring it down.
    /// </summary>
    public static bool SaneEntry(DiploEntry e, Galaxy g) => e.Extra == -1 || e.Kind switch
    {
        ChronicleKind.Said => Enum.IsDefined((Remark)e.Extra),
        ChronicleKind.Ceded or ChronicleKind.Issued or ChronicleKind.Asked or ChronicleKind.Kept or ChronicleKind.Failed => (uint)e.Extra < (uint)g.Planets.Count,
        _ => e.Deal == DealKind.JointWar && e.Extra is >= 0 and <= Lobby.MaxSeats,
    };

    /// <summary>An AI speaks to a player; to another AI, it keeps its words.</summary>
    internal static void Say(Galaxy g, Side from, Side to, Remark what)
    {
        if (from == to || !g.Factions.TryGetValue(from, out var f) || f.AI is null || !g.Factions.TryGetValue(to, out var t) || t.AI is not null) return;
        Note(g, new AiSaid(from, to, what));
    }

    /// <summary>A diplomatic event: told as any other, and kept in the chronicle.</summary>
    static void Note(Galaxy g, GalaxyEvent ev)
    {
        g.Events.Add(ev);
        g.DiploChanges++;
        if (Entry(g, ev) is not { } e) return;
        e.Time = g.Time;
        g.Chronicle.Add(e);
        if (g.Chronicle.Count > ChronicleMax) g.Chronicle.RemoveRange(0, g.Chronicle.Count - ChronicleMax);
    }

    /// <summary>The chronicle's line for a diplomatic event; none for an offer but an ultimatum or a request for help.</summary>
    static DiploEntry? Entry(Galaxy g, GalaxyEvent ev) => ev switch
    {
        DealOffered o when o.Kind is DealKind.Ultimatum or DealKind.Help =>
            new() { Kind = o.Kind == DealKind.Ultimatum ? ChronicleKind.Issued : ChronicleKind.Asked, Deal = o.Kind, A = o.From, B = o.To, Amount = o.Amount, Extra = o.Planet ?? -1 },
        DealRefused r when r.Kind != DealKind.Ultimatum => new() { Kind = ChronicleKind.Refused, Deal = r.Kind, A = r.From, B = r.To, By = r.To },
        TruceSigned t => new() { Kind = ChronicleKind.Signed, Deal = DealKind.Truce, A = t.A, B = t.B },
        TruceEnded t => new() { Kind = t.BrokenBy is null ? ChronicleKind.Expired : ChronicleKind.Broken, Deal = DealKind.Truce, A = t.A, B = t.B, By = t.BrokenBy },
        PactSigned p => new() { Kind = ChronicleKind.Signed, Deal = p.Kind, A = p.A, B = p.B, Extra = p.Target?.Id ?? -1 },
        PactEnded p => new() { Kind = p.By is null ? ChronicleKind.Ended : p.Kind is DealKind.Alliance or DealKind.JointWar ? ChronicleKind.Broken : ChronicleKind.Ended,
            Deal = p.Kind, A = p.A, B = p.B, By = p.By, Extra = p.Target?.Id ?? -1 },
        TributeSigned t => new() { Kind = ChronicleKind.Signed, Deal = DealKind.Tribute, A = t.Payer, B = t.Payee, Amount = t.Amount },
        TributeEnded t => new() { Kind = t.By == t.Payer ? ChronicleKind.Defaulted : ChronicleKind.Ended, Deal = DealKind.Tribute, A = t.Payer, B = t.Payee, By = t.By },
        UltimatumEnded u => new() { Kind = u.Accepted ? ChronicleKind.Accepted : ChronicleKind.Defied, Deal = DealKind.Ultimatum, A = u.From, B = u.To },
        PlanetCeded c => new() { Kind = ChronicleKind.Ceded, Deal = DealKind.Ultimatum, A = c.From, B = c.To, Extra = c.PlanetId },
        HelpAnswered h => new() { Kind = h.Kept ? ChronicleKind.Kept : ChronicleKind.Failed, Deal = DealKind.Help, A = h.Asker, B = h.Helper, Extra = h.PlanetId },
        AiSaid s => new() { Kind = ChronicleKind.Said, A = s.From, B = s.To, Extra = (int)s.What },
        _ => null,
    };
}
