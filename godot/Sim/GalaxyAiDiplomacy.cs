// The galaxy AI as a diplomat (not in the web version): how it answers what it is offered, and
// what it offers itself. It weighs its strength against the other's, a third faction it fights,
// its personality, and what the other did to it: its trust, and the grudge a betrayal leaves.
// It keeps its word: an AI never breaks a truce, an alliance or a tribute itself.
using SandboxShip.Sim.Data;
using static SandboxShip.Sim.Galaxies;

namespace SandboxShip.Sim;

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

    static PersonalityDef DefOf(Galaxy g, Side side) => Personalities.Defs[g.Factions[side].AI!.Personality];

    static bool Grudge(Galaxy g, Side side, Side other) => g.Factions[side].AI!.Grudges.GetValueOrDefault(other) > 0;

    /// <summary>What an AI answers a deal `from` offers it: Ok to sign, else why not.</summary>
    public static Result AnswerDeal(Galaxy g, Side side, Side from, DealKind kind, double amount, bool perMinute) => kind switch
    {
        DealKind.Truce => AnswerTruce(g, side, from),
        DealKind.Alliance => AnswerAlliance(g, side, from),
        DealKind.Trade => AnswerTrade(g, side, from),
        DealKind.Tribute => AnswerTribute(g, side, from, perMinute ? amount : amount * 60 / TruceTime),
        _ => AnswerDemand(g, side, from, amount, perMinute),
    };

    /// <summary>
    /// An AI's answer to a truce: yes when it is the weaker (its personality has it feel so sooner
    /// or later), or busy with a third faction; never while it bears a grudge against one that
    /// betrayed it. Trust has no say: grown over a truce, it had two AIs sign the next one forever.
    /// </summary>
    public static Result AnswerTruce(Galaxy g, Side side, Side from)
    {
        if (Grudge(g, side, from)) return Result.Fail("Elle n'a pas oublié la trêve rompue");
        if (Might(g, side) < Might(g, from) * DefOf(g, side).Peace || BusyElsewhere(g, side, from)) return Result.OK;
        return Result.Fail("Elle se croit la plus forte");
    }

    /// <summary>
    /// An alliance: with a faction it trusts, or against a third, stronger than either, that
    /// fights both; never one that would end the campaign in a victory shared, nor with a
    /// faction that betrayed it.
    /// </summary>
    static Result AnswerAlliance(Galaxy g, Side side, Side from)
    {
        var def = DefOf(g, side);
        if (Grudge(g, side, from)) return Result.Fail("Elle n'a pas oublié ta trahison");
        if (EndsTheWar(g, side, from)) return Result.Fail("Elle veut la galaxie pour elle seule");
        var trust = TrustOf(g, side, from);
        if (trust >= AllianceTrust / def.Friendship || trust >= -20 && CommonFoe(g, side, from, def)) return Result.OK;
        return Result.Fail("Elle ne te fait pas encore assez confiance");
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
    static Result AnswerTrade(Galaxy g, Side side, Side from)
    {
        if (Grudge(g, side, from)) return Result.Fail("Elle n'a pas oublié ta trahison");
        return TrustOf(g, side, from) >= 10 - 20 * DefOf(g, side).Trade ? Result.OK : Result.Fail("Elle se méfie de toi");
    }

    /// <summary>
    /// What peace with `from` is worth to the AI, in credits a minute, rounded up to ten: nothing
    /// when it is the weaker (by its personality's measure), more the stronger it is, up to a
    /// third of its own income; half as much when it fights a third faction besides.
    /// </summary>
    public static double PeacePrice(Galaxy g, Side side, Side from)
    {
        var ratio = Might(g, side) / Math.Max(1, Might(g, from));
        var price = Math.Clamp(ratio - DefOf(g, side).Peace, 0, 1) * IncomeOf(g, side) * 60 / 3;
        if (BusyElsewhere(g, side, from)) price /= 2;
        return Math.Ceiling(price / 10) * 10;
    }

    /// <summary>A tribute it is offered, `perMinute` (a sum paid once spread over a truce): worth its peace price. In peace, a gift: always welcome.</summary>
    static Result AnswerTribute(Galaxy g, Side side, Side from, double perMinute)
    {
        if (Grudge(g, side, from)) return Result.Fail("Elle n'a pas oublié ta trahison");
        if (!AtWar(g, side, from)) return Result.OK;
        var price = PeacePrice(g, side, from);
        return perMinute >= price ? Result.OK : Result.Fail($"Elle en veut au moins {(int)price} crédits par minute de trêve");
    }

    /// <summary>A tribute demanded of it: paid out of fear of a far stronger faction (its personality bears it more or less), if it has the means.</summary>
    static Result AnswerDemand(Galaxy g, Side side, Side from, double amount, bool perMinute)
    {
        var means = perMinute ? IncomeOf(g, side) * 60 * 0.5 : g.Factions[side].Credits * 0.5;
        if (amount > means) return Result.Fail("Elle n'a pas de quoi payer");
        return Might(g, side) < Might(g, from) * 0.6 * DefOf(g, side).Peace ? Result.OK : Result.Fail("Elle ne te craint pas");
    }

    /// <summary>
    /// Now and then, an AI offers a deal, one at a time: a truce to the strongest rival it faces
    /// when it is clearly the weaker or has its hands full elsewhere; else an alliance or trade
    /// that it would accept itself; else a tribute by the minute demanded of a player it faces,
    /// far weaker than itself (sooner for an aggressive AI, hardly ever for a defensive one).
    /// </summary>
    static void OfferDeals(Galaxy g, Side side)
    {
        var ai = g.Factions[side].AI!;
        if (ai.OfferWait > 0) return;
        var def = DefOf(g, side);
        var mine = Might(g, side);
        var rivals = g.Factions.Values.Where(f => !f.Eliminated && f.Id != side).OrderByDescending(f => Might(g, f.Id)).ToList();
        void Offer(DealKind kind, Side to, double amount = 0, bool perMinute = false)
        {
            ai.OfferWait = OfferPause;
            ProposeDeal(g, side, to, kind, amount, perMinute);
        }
        foreach (var r in rivals)
        {
            if (ai.Grudges.ContainsKey(r.Id) || !CanOfferTruce(g, side, r.Id).Ok || !Contact(g, side, r.Id)
                || (mine >= Might(g, r.Id) * 0.7 * def.Peace && !BusyElsewhere(g, side, r.Id))) continue;
            Offer(DealKind.Truce, r.Id);
            return;
        }
        foreach (var r in rivals)
        {
            if (CanPropose(g, side, r.Id, DealKind.Alliance).Ok && AnswerAlliance(g, side, r.Id).Ok)
            {
                Offer(DealKind.Alliance, r.Id);
                return;
            }
            if (CanPropose(g, side, r.Id, DealKind.Trade).Ok && AnswerTrade(g, side, r.Id).Ok && (def.Trade >= 1 || TrustOf(g, side, r.Id) >= 20))
            {
                Offer(DealKind.Trade, r.Id);
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
    }
}
