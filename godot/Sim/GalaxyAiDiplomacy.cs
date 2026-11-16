// The galaxy AI as a diplomat (not in the web version): how it answers what it is offered, and
// what it offers itself. It weighs its strength against the other's, a third faction it fights,
// its personality, what the other did to it (its trust, the grudge a betrayal leaves) and what
// everyone knows of the other's word (its reputation). It keeps its word: it never breaks an
// alliance, a tribute or a joint war itself, and only the hard AI a truce, one nobody paid for,
// when it pays (GalaxyAi.BreakTruces), and after a warning. Against a faction that grows to
// dominate the galaxy, the AIs league together; an AI in need asks its friends for help; a
// strong one may issue an ultimatum. What it would answer, the diplomacy screen shows before
// anything is sent (Preview).
using SandboxShip.Sim.Data;
using static SandboxShip.Sim.Galaxies;

namespace SandboxShip.Sim;

/// <summary>Why an AI says yes or no: its answer, which the diplomacy screen shows ahead (a reason in words, Reason).</summary>
public enum DealWhy { Yes, GrudgeTruce, Grudge, RecentTruce, Stronger, WantsAll, NotTrusted, Distrust, Price, Spent, NoMeans, NotAfraid, Sworn, NoThreat, Treasured, Yielded }

/// <summary>
/// What an AI would answer the viewer, worked out by the host and sent to a replica, which cannot:
/// each plain deal, a joint war against each faction, and how far it would bow to a demand or
/// an ultimatum (nothing when it fears nothing).
/// </summary>
public sealed class DealPreview
{
    public DealWhy Truce, Alliance, Trade, Vision;
    public Dictionary<Side, DealWhy> JointWar = [];
    /// <summary>The largest tribute a minute it would pay if demanded, and the credits once an ultimatum would get of it.</summary>
    public double DemandLimit, YieldLimit;
    /// <summary>Whether it would cede a lesser planet under an ultimatum.</summary>
    public bool Cedes;
}

public static partial class GalaxyAi
{
    /// <summary>The trust an alliance needs (÷ the AI's personality's Friendship).</summary>
    const double AllianceTrust = 40;
    /// <summary>
    /// How much stronger than a player an AI demands tribute of them (÷ its personality's
    /// Extortion). Never of another AI: tribute after tribute, two AIs left alone kept a peace
    /// that never ended the campaign.
    /// </summary>
    const double DemandEdge = 2.5;
    /// <summary>How much stronger than a player in truce with it an AI issues an ultimatum (÷ its personality's Extortion).</summary>
    const double UltimatumMargin = 2;
    /// <summary>The share of all the might a faction must hold, and how far above the next it must stand, for the others to league against it.</summary>
    const double LeaderShare = 0.35, LeaderEdge = 1.4;
    /// <summary>Galaxy seconds between an AI's requests for help, and between two of its ultimatums; the warning before the hard AI breaks a truce.</summary>
    const double AskPause = 240, ThreatPause = 900, BreakNotice = 30;

    static PersonalityDef DefOf(Galaxy g, Side side) => Personalities.Defs[g.Factions[side].AI!.Personality];

    static bool Grudge(Galaxy g, Side side, Side other) => g.Factions[side].AI!.Grudges.GetValueOrDefault(other) > 0;

    /// <summary>A reason in words for an answer: what a refusal says.</summary>
    public static string Reason(DealWhy why, double price = 0) => why switch
    {
        DealWhy.GrudgeTruce => "Elle n'a pas oublié la trêve rompue",
        DealWhy.Grudge => "Elle n'a pas oublié ta trahison",
        DealWhy.RecentTruce => "Elle sort à peine d'une trêve avec toi",
        DealWhy.Stronger => "Elle se croit la plus forte",
        DealWhy.WantsAll => "Elle veut la galaxie pour elle seule",
        DealWhy.NotTrusted => "Elle ne te fait pas encore assez confiance",
        DealWhy.Distrust => "Elle se méfie de toi",
        DealWhy.Price => $"Elle en veut au moins {(int)price} crédits par minute de trêve",
        DealWhy.Spent => "Elle n'attend plus rien de toi pour l'instant",
        DealWhy.NoMeans => "Elle n'a pas de quoi payer",
        DealWhy.NotAfraid => "Elle ne te craint pas",
        DealWhy.Sworn => "Elle a juré la guerre à ce camp",
        DealWhy.NoThreat => "Cette cible ne l'inquiète pas",
        DealWhy.Treasured => "Elle tient trop à cette planète",
        DealWhy.Yielded => "Elle t'a déjà cédé une fois : plus maintenant",
        _ => "",
    };

    static Result Answer(DealWhy why, double price = 0) => why == DealWhy.Yes ? Result.OK : Result.Fail(Reason(why, price));

    /// <summary>What an AI answers a deal `from` offers it: Ok to sign, else why not.</summary>
    public static Result AnswerDeal(Galaxy g, Side side, Side from, DealKind kind, double amount, bool perMinute,
        Side? target = null, int? planet = null, DealTerms terms = DealTerms.None) => kind switch
    {
        DealKind.Truce => AnswerTruce(g, side, from),
        DealKind.Alliance => Answer(AnswerAlliance(g, side, from)),
        DealKind.Trade => Answer(AnswerTrade(g, side, from)),
        DealKind.Vision => Answer(AnswerVision(g, side, from)),
        DealKind.Tribute => AnswerTribute(g, side, from, perMinute ? amount : amount * 60 / TruceTime),
        DealKind.JointWar => Answer(target is Side t ? AnswerJointWar(g, side, from, t) : DealWhy.NoThreat),
        DealKind.Ultimatum => Answer(AnswerUltimatum(g, side, from, amount, planet)),
        DealKind.Bundle => AnswerBundle(g, side, from, terms, amount, perMinute),
        _ => Answer(AnswerDemand(g, side, from, amount, perMinute)),
    };

    /// <summary>
    /// An AI's answer to a truce: yes when it is the weaker (its personality has it feel so sooner
    /// or later), or busy with a third faction; never while it bears a grudge against one that
    /// betrayed it, nor from an AI it just came out of a truce with, nor from the faction it swore
    /// a joint war against. Trust has no say: grown over a truce, it had two AIs sign the next one forever.
    /// </summary>
    public static Result AnswerTruce(Galaxy g, Side side, Side from) => Answer(TruceWhy(g, side, from));

    static DealWhy TruceWhy(Galaxy g, Side side, Side from)
    {
        if (Grudge(g, side, from)) return DealWhy.GrudgeTruce;
        if (g.Factions[side].AI!.TruceRest.ContainsKey(from)) return DealWhy.RecentTruce;
        if (Sworn(g, side, from)) return DealWhy.Sworn;
        return Might(g, side) < Might(g, from) * DefOf(g, side).Peace || BusyElsewhere(g, side, from) ? DealWhy.Yes : DealWhy.Stronger;
    }

    /// <summary>
    /// An alliance: with a faction it regards well enough, or against a third, stronger than either,
    /// that fights both; never one that would end the campaign in a victory shared, nor with a
    /// faction that betrayed it.
    /// </summary>
    static DealWhy AnswerAlliance(Galaxy g, Side side, Side from)
    {
        var def = DefOf(g, side);
        if (Grudge(g, side, from)) return DealWhy.Grudge;
        if (EndsTheWar(g, side, from)) return DealWhy.WantsAll;
        var regard = Regard(g, side, from);
        return regard >= AllianceTrust / def.Friendship || regard >= -20 && CommonFoe(g, side, from, def) ? DealWhy.Yes : DealWhy.NotTrusted;
    }

    /// <summary>Whether, these two allied, every faction left would be the first one's ally: the campaign won by all (see CheckElimination).</summary>
    static bool EndsTheWar(Galaxy g, Side a, Side b)
    {
        var alive = g.Factions.Values.Where(f => !f.Eliminated).Select(f => f.Id).ToList();
        if (alive.Count == 0) return false;
        var first = alive[0];
        return alive.TrueForAll(s => Allies(g, s, first) || (s == a && first == b) || (s == b && first == a));
    }

    /// <summary>A third faction at war with both and facing the AI, stronger than either of them (in the eyes of its personality).</summary>
    static bool CommonFoe(Galaxy g, Side side, Side from, PersonalityDef def)
    {
        var stronger = Math.Max(Might(g, side), Might(g, from));
        return g.Factions.Values.Any(t => !t.Eliminated && t.Id != side && t.Id != from && AtWar(g, side, t.Id) && AtWar(g, from, t.Id)
            && Contact(g, side, t.Id) && Might(g, t.Id) * def.Friendship > stronger);
    }

    /// <summary>Trade, in peace: with a faction it does not mistrust too much, the thrifty AI the least wary.</summary>
    static DealWhy AnswerTrade(Galaxy g, Side side, Side from)
    {
        if (Grudge(g, side, from)) return DealWhy.Grudge;
        return Regard(g, side, from) >= 10 - 20 * DefOf(g, side).Trade ? DealWhy.Yes : DealWhy.Distrust;
    }

    /// <summary>Its sight shared, in peace: a little more trust than trade asks, short of an alliance.</summary>
    static DealWhy AnswerVision(Galaxy g, Side side, Side from)
    {
        if (Grudge(g, side, from)) return DealWhy.Grudge;
        return Regard(g, side, from) >= 20 - 15 * DefOf(g, side).Trade ? DealWhy.Yes : DealWhy.Distrust;
    }

    /// <summary>
    /// A joint war on `target`: against a faction that threatens it (as strong as it nearly, or the
    /// one dominating the galaxy), that it faces, with a faction it does not mistrust.
    /// </summary>
    static DealWhy AnswerJointWar(Galaxy g, Side side, Side from, Side target) => AnswerJointWar(g, side, from, target, Leader(g));

    /// <summary>The same, the leader worked out once by the caller (PreviewFor asks it for every third faction).</summary>
    static DealWhy AnswerJointWar(Galaxy g, Side side, Side from, Side target, Side? leader)
    {
        if (Grudge(g, side, from)) return DealWhy.Grudge;
        if (Regard(g, side, from) < -10) return DealWhy.Distrust;
        if (!Contact(g, side, target)) return DealWhy.NoThreat;
        return Might(g, target) >= Might(g, side) * 0.8 || leader == target || Sworn(g, side, target) ? DealWhy.Yes : DealWhy.NoThreat;
    }

    /// <summary>
    /// What peace with `from` is worth to the AI, in credits a minute, rounded up to ten: nothing
    /// when it is the weaker (by its personality's measure), more the stronger it is, up to a
    /// third of its own income; half as much when it fights a third faction besides, half as much
    /// again from the faction dominating the galaxy. Never more than a tribute can be, or nothing
    /// could ever buy it.
    /// </summary>
    public static double PeacePrice(Galaxy g, Side side, Side from)
    {
        var ratio = Might(g, side) / Math.Max(1, Might(g, from));
        var price = Math.Clamp(ratio - DefOf(g, side).Peace, 0, 1) * IncomeOf(g, side) * 60 / 3;
        if (BusyElsewhere(g, side, from)) price /= 2;
        if (Leader(g) == from) price *= 1.5;
        return Math.Min(MaxTributePerMinute, Math.Ceiling(price / 10) * 10);
    }

    /// <summary>Whether a tribute `from` pays `side` is worth a truce: at the AI's price, a sum paid once spread over a truce. Between people, whatever they agreed.</summary>
    internal static bool WorthPeace(Galaxy g, Side side, Side from, double amount, bool perMinute) =>
        g.Factions[side].AI is null || (perMinute ? amount : amount * 60 / TruceTime) >= PeacePrice(g, side, from);

    /// <summary>
    /// A tribute it is offered, `perMinute` (a sum paid once spread over a truce): worth its peace
    /// price. During a truce, a gift: welcome as long as it can still buy something, trust or a
    /// truce prolonged. Between allies, always.
    /// </summary>
    static Result AnswerTribute(Galaxy g, Side side, Side from, double perMinute)
    {
        var price = PeacePrice(g, side, from);
        return Answer(TributeWhy(g, side, from, perMinute, price), price);
    }

    static DealWhy TributeWhy(Galaxy g, Side side, Side from, double perMinute, double price)
    {
        if (Grudge(g, side, from)) return DealWhy.Grudge;
        if (TruceOf(g, side, from) is { } truce)
        {
            var spent = g.Factions[side].AI!.BoughtTrust.GetValueOrDefault(from) >= TrustBuyable;
            return spent && (truce.Renewed || perMinute < price) ? DealWhy.Spent : DealWhy.Yes;
        }
        if (!AtWar(g, side, from)) return DealWhy.Yes;
        return perMinute >= price ? DealWhy.Yes : DealWhy.Price;
    }

    /// <summary>Whether the AI fears `from` enough to bow to its demands (its personality bears it more or less).</summary>
    static bool Fears(Galaxy g, Side side, Side from) => Might(g, side) < Might(g, from) * 0.6 * DefOf(g, side).Peace;

    /// <summary>The largest tribute a minute it could pay, and the credits once it could give up.</summary>
    static double MeansPerMinute(Galaxy g, Side side) => IncomeOf(g, side) * 60 * 0.5;

    static double MeansOnce(Galaxy g, Side side) => g.Factions[side].Credits * 0.5;

    /// <summary>A tribute demanded of it: paid out of fear of a far stronger faction, if it has the means.</summary>
    static DealWhy AnswerDemand(Galaxy g, Side side, Side from, double amount, bool perMinute)
    {
        if (amount > (perMinute ? MeansPerMinute(g, side) : MeansOnce(g, side))) return DealWhy.NoMeans;
        return Fears(g, side, from) ? DealWhy.Yes : DealWhy.NotAfraid;
    }

    /// <summary>An ultimatum: given in to out of fear, if it can pay, or if the planet is a lesser one of its own; defied otherwise, and war follows.</summary>
    static DealWhy AnswerUltimatum(Galaxy g, Side side, Side from, double amount, int? planet)
    {
        if (g.Factions[side].AI!.Yielded.ContainsKey(from)) return DealWhy.Yielded;
        if (!Fears(g, side, from)) return DealWhy.NotAfraid;
        if (planet is int p) return Lesser(g, side, g.Planets[p]) ? DealWhy.Yes : DealWhy.Treasured;
        return amount <= MeansOnce(g, side) ? DealWhy.Yes : DealWhy.NoMeans;
    }

    /// <summary>A planet an AI would cede: no shipyard station on it, and worth a quarter of its realm at most.</summary>
    static bool Lesser(Galaxy g, Side side, Planet p) =>
        p.Station < 2 && Worth(p) <= g.Planets.Where(o => o.Owner == side).Sum(Worth) * 0.25;

    /// <summary>
    /// Several terms at once, all or nothing: the truce on its own merits or bought by the tribute
    /// in it, trade and vision as though in peace already, a tribute alone as any tribute.
    /// </summary>
    static Result AnswerBundle(Galaxy g, Side side, Side from, DealTerms terms, double amount, bool perMinute)
    {
        var price = PeacePrice(g, side, from);
        return Answer(BundleWhy(TruceWhy(g, side, from), AnswerTrade(g, side, from), AnswerVision(g, side, from),
            TributeWhy(g, side, from, perMinute ? amount : amount * 60 / TruceTime, price), price, terms, amount, perMinute), price);
    }

    /// <summary>A bundle's answer from those of its terms: shared by the host and by a replica's preview.</summary>
    public static DealWhy BundleWhy(DealWhy truce, DealWhy trade, DealWhy vision, DealWhy tribute, double price, DealTerms terms, double amount, bool perMinute)
    {
        var paid = (terms & DealTerms.Tribute) != 0 && (perMinute ? amount : amount * 60 / TruceTime) >= price;
        if ((terms & DealTerms.Truce) != 0 && truce != DealWhy.Yes && !(paid && truce is DealWhy.Stronger))
            return (terms & DealTerms.Tribute) != 0 && truce is DealWhy.Stronger ? DealWhy.Price : truce;
        if ((terms & DealTerms.Trade) != 0 && trade != DealWhy.Yes) return trade;
        if ((terms & DealTerms.Vision) != 0 && vision != DealWhy.Yes) return vision;
        if ((terms & (DealTerms.Truce | DealTerms.Tribute)) == DealTerms.Tribute && tribute != DealWhy.Yes) return tribute;
        return DealWhy.Yes;
    }

    /// <summary>Galaxy seconds the host reuses what an AI would answer a viewer, if nothing diplomatic happened meanwhile.</summary>
    const double MindLife = 3;

    /// <summary>
    /// What the AI `side` would answer `viewer` and its peace price, for the snapshots and the
    /// screen: worked out at most every MindLife galaxy seconds, and at once after a diplomatic change
    /// (Galaxy.DiploChanges). Every snapshot of every player asked it of every AI, five times a second.
    /// </summary>
    public static (double Price, DealPreview? Preview) Mind(Galaxy g, Side side, Side viewer)
    {
        var ai = g.Factions[side].AI!;
        if (ai.Minds.TryGetValue(viewer, out var m) && m.Changes == g.DiploChanges && g.Time >= m.At && g.Time - m.At < MindLife) return (m.Price, m.Preview);
        m = (g.Time, g.DiploChanges, PeacePrice(g, side, viewer), PreviewFor(g, side, viewer));
        ai.Minds[viewer] = m;
        return (m.Price, m.Preview);
    }

    /// <summary>What the AI `side` would answer `viewer`, for the diplomacy screen: on the host directly, sent to a replica.</summary>
    public static DealPreview PreviewFor(Galaxy g, Side side, Side viewer)
    {
        var p = new DealPreview
        {
            Truce = TruceWhy(g, side, viewer), Alliance = AnswerAlliance(g, side, viewer),
            Trade = AnswerTrade(g, side, viewer), Vision = AnswerVision(g, side, viewer),
        };
        var leader = Leader(g);
        foreach (var t in g.Factions.Values)
        {
            if (t.Id != side && t.Id != viewer && !t.Eliminated) p.JointWar[t.Id] = AnswerJointWar(g, side, viewer, t.Id, leader);
        }
        if (Fears(g, side, viewer) && !g.Factions[side].AI!.Yielded.ContainsKey(viewer))
        {
            (p.DemandLimit, p.YieldLimit, p.Cedes) = (MeansPerMinute(g, side), MeansOnce(g, side), true);
        }
        return p;
    }

    /// <summary>
    /// The faction dominating the galaxy, if one does: the others league against it. It holds a
    /// good share of all the might, well above the next faction.
    /// </summary>
    public static Side? Leader(Galaxy g)
    {
        Side? best = null;
        double first = 0, second = 0, total = 0;
        foreach (var f in g.Factions.Values)
        {
            if (f.Eliminated) continue;
            var m = Might(g, f.Id);
            total += m;
            if (m > first)
            {
                (second, first, best) = (first, m, f.Id);
            }
            else if (m > second) second = m;
        }
        return best is not null && first >= total * LeaderShare && first >= second * LeaderEdge ? best : null;
    }

    /// <summary>
    /// Now and then, an AI offers a deal, one at a time: a joint war against the faction dominating
    /// the galaxy, if a partner will have one, rather than bow to it; else a truce to the strongest
    /// rival it faces when it is clearly the weaker or has its hands full elsewhere; else a joint
    /// war against a stronger common foe; else an alliance, trade or its sight
    /// shared, that it would accept itself; else a tribute by the minute demanded of a player it
    /// faces, far weaker than itself; else, for a bold AI, an ultimatum to a player far weaker in
    /// truce with it. Apart, on its own clock, it asks its friends for help when it needs it.
    /// </summary>
    static void OfferDeals(Galaxy g, Side side)
    {
        var ai = g.Factions[side].AI!;
        AskForHelp(g, side, ai);
        if (ai.OfferWait > 0) return;
        var def = DefOf(g, side);
        var mine = Might(g, side);
        var rivals = g.Factions.Values.Where(f => !f.Eliminated && f.Id != side).OrderByDescending(f => Might(g, f.Id)).ToList();
        void Offer(DealKind kind, Side to, double amount = 0, bool perMinute = false, Side? target = null, int? planet = null)
        {
            ai.OfferWait = OfferPause;
            ProposeDeal(g, side, to, kind, amount, perMinute, target, planet);
        }
        // A league against the faction that dominates, or a war shared on a common foe stronger than the AI.
        var leader = Leader(g);
        bool League(Side t)
        {
            if (!AtWar(g, side, t) || !Contact(g, side, t)) return false;
            foreach (var r in rivals)
            {
                if (r.Id == t || !CanPropose(g, side, r.Id, DealKind.JointWar, target: t).Ok || AnswerJointWar(g, side, r.Id, t) != DealWhy.Yes) continue;
                if (r.AI is not null && AnswerJointWar(g, r.Id, side, t) != DealWhy.Yes) continue;
                if (t == leader) Say(g, side, r.Id, Remark.Coalition);
                Offer(DealKind.JointWar, r.Id, target: t);
                return true;
            }
            return false;
        }
        if (leader is Side top && top != side && League(top)) return;
        foreach (var r in rivals)
        {
            if (ai.Grudges.ContainsKey(r.Id) || ai.TruceRest.ContainsKey(r.Id) || !CanOfferTruce(g, side, r.Id).Ok || !Contact(g, side, r.Id)
                || Sworn(g, side, r.Id) || (mine >= Might(g, r.Id) * 0.7 * def.Peace && !BusyElsewhere(g, side, r.Id))) continue;
            Offer(DealKind.Truce, r.Id);
            return;
        }
        foreach (var t in rivals)
        {
            if (t.Id != leader && Might(g, t.Id) > mine && League(t.Id)) return;
        }
        foreach (var r in rivals)
        {
            if (CanPropose(g, side, r.Id, DealKind.Alliance).Ok && AnswerAlliance(g, side, r.Id) == DealWhy.Yes)
            {
                Offer(DealKind.Alliance, r.Id);
                return;
            }
            if (CanPropose(g, side, r.Id, DealKind.Trade).Ok && AnswerTrade(g, side, r.Id) == DealWhy.Yes && (def.Trade >= 1 || TrustOf(g, side, r.Id) >= 20))
            {
                Offer(DealKind.Trade, r.Id);
                return;
            }
            if (CanPropose(g, side, r.Id, DealKind.Vision).Ok && Regard(g, side, r.Id) >= 30 && (r.AI is null || AnswerVision(g, r.Id, side) == DealWhy.Yes))
            {
                Offer(DealKind.Vision, r.Id);
                return;
            }
        }
        foreach (var r in rivals)
        {
            if (r.AI is not null || !AtWar(g, side, r.Id) || mine <= Might(g, r.Id) * DemandEdge / def.Extortion || !Contact(g, side, r.Id)) continue;
            var amount = Math.Clamp(Math.Round(IncomeOf(g, r.Id) * 60 * 0.25 / 10) * 10, 30, MaxTributePerMinute);
            if (!CanPropose(g, side, r.Id, DealKind.Demand, amount, true).Ok) continue;
            Offer(DealKind.Demand, r.Id, amount, true);
            return;
        }
        Threaten(g, side, ai, def, rivals);
    }

    /// <summary>
    /// A bold AI (an aggressive one, or any hard one) far stronger than a player in truce with it
    /// issues an ultimatum, now and then: the weakest planet of theirs next to its own, or credits.
    /// </summary>
    static void Threaten(Galaxy g, Side side, GalaxyAIState ai, PersonalityDef def, List<GalaxyFaction> rivals)
    {
        if (ai.ThreatWait > 0 || Clumsy(ai) || !(Cunning(ai) || def.Extortion > 1)) return;
        var mine = Might(g, side);
        foreach (var r in rivals)
        {
            if (r.AI is not null || TruceOf(g, side, r.Id) is not { Paid: false } || mine < Might(g, r.Id) * UltimatumMargin / def.Extortion || !Contact(g, side, r.Id)) continue;
            var planet = g.Planets.Where(p => CanDemandPlanet(g, side, r.Id, p.Id).Ok).OrderBy(p => DefenseValue(g, p)).ThenBy(Worth).FirstOrDefault();
            var amount = Math.Clamp(Math.Round(IncomeOf(g, r.Id) * 120 / 10) * 10, 200, MaxTribute);
            var r0 = planet is not null ? CanPropose(g, side, r.Id, DealKind.Ultimatum, planet: planet.Id) : CanPropose(g, side, r.Id, DealKind.Ultimatum, amount);
            if (!r0.Ok) continue;
            ai.OfferWait = OfferPause;
            ai.ThreatWait = ThreatPause;
            Say(g, side, r.Id, Remark.Threat);
            if (planet is not null) ProposeDeal(g, side, r.Id, DealKind.Ultimatum, planet: planet.Id);
            else ProposeDeal(g, side, r.Id, DealKind.Ultimatum, amount);
            return;
        }
    }

    /// <summary>
    /// An AI whose planet is attacked asks a player it is allied with, or at peace with and trusts,
    /// to come and defend it, if a fleet of theirs is near enough; an AI short of credits asks an
    /// ally for some. One request at a time, and not too often.
    /// </summary>
    static void AskForHelp(Galaxy g, Side side, GalaxyAIState ai)
    {
        if (ai.AskWait > 0 || Clumsy(ai)) return;
        var me = g.Factions[side];
        foreach (var h in g.Factions.Values)
        {
            if (h.AI is not null || h.Eliminated || g.Offers.Exists(o => o.From == side && o.To == h.Id && o.Kind == DealKind.Help)) continue;
            var friend = Allied(g, side, h.Id) || AtPeace(g, side, h.Id) && Regard(g, side, h.Id) >= 20;
            if (!friend) continue;
            var attacked = g.Battles.Find(b => b.Defender == side && b.Lane is null && b.Attacker != h.Id);
            if (attacked is not null)
            {
                var near = Hops(g, [attacked.PlanetId]);
                if (g.Fleets.Values.Any(f => f.Side == h.Id && f.At is int at && near.GetValueOrDefault(at, 99) <= 2)
                    && CanPropose(g, side, h.Id, DealKind.Help, planet: attacked.PlanetId).Ok)
                {
                    ai.AskWait = AskPause;
                    Say(g, side, h.Id, Remark.AskHelp);
                    ProposeDeal(g, side, h.Id, DealKind.Help, planet: attacked.PlanetId);
                    return;
                }
            }
            if (Allied(g, side, h.Id) && me.Credits < 150 && h.Credits >= 800 && CanPropose(g, side, h.Id, DealKind.Help, 300).Ok)
            {
                ai.AskWait = AskPause;
                Say(g, side, h.Id, Remark.AskHelp);
                ProposeDeal(g, side, h.Id, DealKind.Help, 300);
                return;
            }
        }
    }
}
