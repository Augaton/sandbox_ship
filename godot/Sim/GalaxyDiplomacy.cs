// Diplomacy between galaxy factions (not in the web version): truces, alliances, trade and
// tributes. Sides at peace (a truce) never meet in battle, at a planet or on a lane; neither
// sends fleets to the other's planets, nor takes them. Allies, of a team or of an alliance, never
// fight, see together and win together. Trade brings both partners credits for as long as the
// peace holds; a tribute buys it, paid once or by the minute. Never with the Errants, nor between
// the members of a team, who need none of it. An AI keeps count of what each faction did to it
// (Trust), and bears a grudge against one that betrayed it.
using SandboxShip.Sim.Data;

namespace SandboxShip.Sim;

/// <summary>
/// What one galaxy faction may offer another: a truce, an alliance, a trade agreement, a tribute
/// it pays for peace, or one it demands for peace.
/// </summary>
public enum DealKind { Truce, Alliance, Trade, Tribute, Demand }

/// <summary>A non-aggression pact between two galaxy factions.</summary>
public sealed class Truce
{
    public Side A, B;
    /// <summary>Galaxy seconds left.</summary>
    public double Remaining;
}

/// <summary>An alliance or a trade agreement between two galaxy factions, for as long as both keep it.</summary>
public sealed class Pact
{
    public DealKind Kind;
    public Side A, B;
}

/// <summary>Credits `Payer` pays `Payee` each minute, for peace, while `Remaining` lasts.</summary>
public sealed class Tribute
{
    public Side Payer, Payee;
    public double PerMinute;
    /// <summary>Galaxy seconds left.</summary>
    public double Remaining;
}

/// <summary>
/// A deal offered to a faction that answers in its own time: a player. A tribute is paid by the
/// one who offers it, a demand by the one it is made to: `Amount` credits once, or each minute
/// for TributeTime. Older saves' offers, without a kind, are truces.
/// </summary>
public sealed class DealOffer
{
    public Side From, To;
    public DealKind Kind;
    public double Amount;
    public bool PerMinute;
    /// <summary>Galaxy seconds before the offer lapses.</summary>
    public double Remaining;
}

public sealed record DealOffered(Side From, Side To, DealKind Kind, double Amount, bool PerMinute) : GalaxyEvent;
/// <summary>`To` turned down what `From` offered: an AI says why, a player does not.</summary>
public sealed record DealRefused(Side From, Side To, DealKind Kind, string Reason) : GalaxyEvent;
public sealed record TruceSigned(Side A, Side B) : GalaxyEvent;
/// <summary>A truce ran out, or `BrokenBy` broke it.</summary>
public sealed record TruceEnded(Side A, Side B, Side? BrokenBy) : GalaxyEvent;
/// <summary>An alliance or a trade agreement signed.</summary>
public sealed record PactSigned(DealKind Kind, Side A, Side B) : GalaxyEvent;
/// <summary>An alliance or a trade agreement ended: `By` gave it up, or (null) the trade lost the peace it needs.</summary>
public sealed record PactEnded(DealKind Kind, Side A, Side B, Side? By) : GalaxyEvent;
/// <summary>`Payer` pays `Payee` for peace: `Amount` credits once, or each minute.</summary>
public sealed record TributeSigned(Side Payer, Side Payee, double Amount, bool PerMinute) : GalaxyEvent;
/// <summary>A tribute by the minute ended: run out (By null), let off by its payee, or stopped by its payer (who then broke the peace).</summary>
public sealed record TributeEnded(Side Payer, Side Payee, Side? By) : GalaxyEvent;

public sealed partial class Galaxy
{
    public List<Truce> Truces = [];
    public List<DealOffer> Offers = [];
    public List<Pact> Pacts = [];
    public List<Tribute> Tributes = [];
}

public sealed partial class GalaxyAIState
{
    /// <summary>Galaxy seconds during which the AI refuses any truce or alliance with each faction that betrayed it.</summary>
    public Dictionary<Side, double> Grudges = [];
    /// <summary>Galaxy seconds before the AI offers a deal again.</summary>
    public double OfferWait;
    /// <summary>How far the AI trusts each faction, -100 to 100: peace and trade raise it, attacks and betrayals sink it.</summary>
    public Dictionary<Side, double> Trust = [];
}

public static partial class Galaxies
{
    /// <summary>
    /// Galaxy seconds a truce lasts, an AI wronged by a broken one bears a grudge, an offer to the
    /// player waits for an answer, and an AI waits between two offers.
    /// </summary>
    public const double TruceTime = 600, GrudgeTime = 900, OfferTime = 60, OfferPause = 300;
    /// <summary>Galaxy seconds of truce a broken alliance leaves, for the fleets to part; a tribute by the minute lasts.</summary>
    public const double NoticeTime = 120, TributeTime = 600;
    /// <summary>Credits a second that trade brings each partner: so much, and so much more per lane between their planets, up to TradeMax.</summary>
    public const double TradeBase = 1, TradePerLane = 1, TradeMax = 6;
    /// <summary>The largest tribute, once and by the minute: a bigger one is refused as it is read.</summary>
    public const double MaxTribute = 10000, MaxTributePerMinute = 1000;
    public const double TrustMax = 100;
    /// <summary>
    /// What a second adds to an AI's trust in a faction it has a truce, a trade agreement or an
    /// alliance with; how fast trust drifts back to nothing without any; what a battle it is
    /// attacked in, a broken truce, a broken alliance and an unpaid tribute take off it; what a
    /// faction that broke an alliance loses with every other AI.
    /// </summary>
    const double TrustTruce = 0.05, TrustTrade = 0.05, TrustAlliance = 0.08, TrustFade = 0.02;
    const double TrustAttack = 5, TrustBrokenTruce = 40, TrustBrokenAlliance = 70, TrustDefault = 30, TrustWitness = 15;
    /// <summary>Attacks alone sink trust no lower than this: only betrayals go further.</summary>
    const double TrustAttackFloor = -60;

    public static Truce? TruceOf(Galaxy g, Side a, Side b) => g.Truces.Find(t => (t.A == a && t.B == b) || (t.A == b && t.B == a));

    public static Pact? PactOf(Galaxy g, DealKind kind, Side a, Side b) =>
        g.Pacts.Find(p => p.Kind == kind && ((p.A == a && p.B == b) || (p.A == b && p.B == a)));

    /// <summary>The tribute one of the two pays the other, if any.</summary>
    public static Tribute? TributeOf(Galaxy g, Side a, Side b) =>
        g.Tributes.Find(t => (t.Payer == a && t.Payee == b) || (t.Payer == b && t.Payee == a));

    /// <summary>Whether two sides are bound by a truce.</summary>
    public static bool AtPeace(Galaxy g, Side a, Side b) => g.Truces.Count > 0 && a != b && TruceOf(g, a, b) is not null;

    /// <summary>Whether two sides signed an alliance.</summary>
    public static bool Allied(Galaxy g, Side a, Side b) => g.Pacts.Count > 0 && a != b && PactOf(g, DealKind.Alliance, a, b) is not null;

    /// <summary>A side's race: its galaxy faction's; a side with none (the Errants) is its own race's (see Side).</summary>
    public static Faction RaceOf(Galaxy g, Side side) => g.Factions.TryGetValue(side, out var f) ? f.Race : Worlds.OwnRace(side);

    /// <summary>A side's team: 0 (alone) for a side with no galaxy faction, the Errants.</summary>
    public static int TeamOf(Galaxy g, Side side) => g.Factions.TryGetValue(side, out var f) ? f.Team : 0;

    /// <summary>The same side, or two sides of the same team other than 0.</summary>
    static bool Teammates(Galaxy g, Side a, Side b) => a == b || (TeamOf(g, a) is var t && t != 0 && t == TeamOf(g, b));

    /// <summary>Teammates, or sides that signed an alliance: they never fight, see together and win together.</summary>
    public static bool Allies(Galaxy g, Side a, Side b) => Teammates(g, a, b) || Allied(g, a, b);

    /// <summary>Whether two sides fight when they meet: neither allies nor bound by a truce. The Errants are at war with all.</summary>
    public static bool AtWar(Galaxy g, Side a, Side b) => !Allies(g, a, b) && !AtPeace(g, a, b);

    /// <summary>Whether `side` won the campaign: its own victory or its allies'.</summary>
    public static bool Won(Galaxy g, Side side) => g.Winner is Side w && Allies(g, w, side);

    /// <summary>How far `ai`'s AI trusts `other`: 0 for a side with no AI.</summary>
    public static double TrustOf(Galaxy g, Side ai, Side other) => g.Factions.TryGetValue(ai, out var f) ? f.AI?.Trust.GetValueOrDefault(other) ?? 0 : 0;

    /// <summary>Moves `ai`'s trust in `other` by `delta`, within ±TrustMax (or no lower than `floor`, if it was above).</summary>
    static void Trusts(Galaxy g, Side ai, Side other, double delta, double floor = -TrustMax)
    {
        if (!g.Factions.TryGetValue(ai, out var f) || f.AI is not { } state || ai == other) return;
        var now = state.Trust.GetValueOrDefault(other);
        var low = Math.Min(now, floor);
        state.Trust[other] = Math.Clamp(now + delta, Math.Max(-TrustMax, low), TrustMax);
    }

    /// <summary>`attacker` opened a battle against `defender`: the defender's AI trusts it less.</summary>
    static void Attacked(Galaxy g, Side attacker, Side defender) => Trusts(g, defender, attacker, -TrustAttack, TrustAttackFloor);

    /// <summary>The credits a second that trade brings each of `a` and `b`: more with each lane between their planets.</summary>
    public static double TradeRate(Galaxy g, Side a, Side b)
    {
        var lanes = 0;
        foreach (var l in g.Lanes)
        {
            var (x, y) = (g.Planets[l.A].Owner, g.Planets[l.B].Owner);
            if ((x == a && y == b) || (x == b && y == a)) lanes++;
        }
        return Math.Min(TradeMax, TradeBase + TradePerLane * lanes);
    }

    /// <summary>What a side's trade agreements bring it each second.</summary>
    public static double TradeIncome(Galaxy g, Side side)
    {
        double s = 0;
        foreach (var p in g.Pacts)
        {
            if (p.Kind == DealKind.Trade && (p.A == side || p.B == side)) s += TradeRate(g, p.A, p.B);
        }
        return s;
    }

    /// <summary>Whether `from` may offer `to` this deal now, and if not, why.</summary>
    public static Result CanPropose(Galaxy g, Side from, Side to, DealKind kind, double amount = 0, bool perMinute = false)
    {
        if (!Enum.IsDefined(kind) || Teammates(g, from, to) || !g.Factions.TryGetValue(from, out var a) || !g.Factions.TryGetValue(to, out var b))
            return Result.Fail("Aucun accord possible");
        if (a.Eliminated || b.Eliminated) return Result.Fail("Faction éliminée");
        if (g.Offers.Exists(o => o.From == from && o.To == to && o.Kind == kind)) return Result.Fail("Offre déjà faite");
        switch (kind)
        {
            case DealKind.Truce:
                if (Allied(g, from, to)) return Result.Fail("Déjà alliées");
                if (TruceOf(g, from, to) is not null) return Result.Fail("Trêve déjà en cours");
                break;
            case DealKind.Alliance:
                if (Allied(g, from, to)) return Result.Fail("Alliance déjà en cours");
                break;
            case DealKind.Trade:
                if (PactOf(g, DealKind.Trade, from, to) is not null) return Result.Fail("Accord commercial déjà en cours");
                if (AtWar(g, from, to)) return Result.Fail("Il faut d'abord la paix");
                break;
            default:
                if (!double.IsFinite(amount) || amount <= 0 || amount > (perMinute ? MaxTributePerMinute : MaxTribute)) return Result.Fail("Montant invalide");
                if (TributeOf(g, from, to) is not null) return Result.Fail("Tribut déjà en cours");
                if (kind == DealKind.Tribute && !perMinute && a.Credits < amount) return Result.Fail("Crédits insuffisants");
                break;
        }
        return Result.OK;
    }

    /// <summary>Whether `from` may offer `to` a truce now, and if not, why.</summary>
    public static Result CanOfferTruce(Galaxy g, Side from, Side to) => CanPropose(g, from, to, DealKind.Truce);

    /// <summary>
    /// Offers a deal. An AI answers at once: Ok means signed, else the reason it refused. Anyone
    /// else (a player) is left an offer to accept or refuse; the same offer they made already is
    /// accepted.
    /// </summary>
    public static Result ProposeDeal(Galaxy g, Side from, Side to, DealKind kind, double amount = 0, bool perMinute = false)
    {
        var r = CanPropose(g, from, to, kind, amount, perMinute);
        if (!r.Ok) return r;
        if (kind is DealKind.Truce or DealKind.Alliance or DealKind.Trade && g.Offers.Exists(o => o.From == to && o.To == from && o.Kind == kind))
            return AcceptDeal(g, from, to, kind);
        if (g.Factions[to].AI is not null)
        {
            var answer = GalaxyAi.AnswerDeal(g, to, from, kind, amount, perMinute);
            if (answer.Ok) answer = Conclude(g, from, to, kind, amount, perMinute);
            if (!answer.Ok) g.Events.Add(new DealRefused(from, to, kind, answer.Reason));
            return answer;
        }
        g.Offers.Add(new DealOffer { From = from, To = to, Kind = kind, Amount = amount, PerMinute = perMinute, Remaining = OfferTime });
        g.Events.Add(new DealOffered(from, to, kind, amount, perMinute));
        return Result.OK;
    }

    public static Result ProposeTruce(Galaxy g, Side from, Side to) => ProposeDeal(g, from, to, DealKind.Truce);

    /// <summary>`side` accepts the deal of this kind `from` offered it, if it still holds.</summary>
    public static Result AcceptDeal(Galaxy g, Side side, Side from, DealKind kind)
    {
        var offer = g.Offers.Find(o => o.From == from && o.To == side && o.Kind == kind);
        if (offer is null) return Result.Fail("Aucune offre");
        g.Offers.Remove(offer);
        // What changed since it was made may have undone it: a tribute nobody can pay, a trade without peace.
        var still = CanPropose(g, from, side, kind, offer.Amount, offer.PerMinute);
        return still.Ok ? Conclude(g, from, side, kind, offer.Amount, offer.PerMinute) : Result.Fail("L'offre n'est plus valable");
    }

    public static Result AcceptTruce(Galaxy g, Side side, Side from) => AcceptDeal(g, side, from, DealKind.Truce);

    /// <summary>`side` turns down the deal of this kind `from` offered it.</summary>
    public static void RefuseDeal(Galaxy g, Side side, Side from, DealKind kind)
    {
        if (g.Offers.RemoveAll(o => o.From == from && o.To == side && o.Kind == kind) > 0) g.Events.Add(new DealRefused(from, side, kind, ""));
    }

    public static void RefuseTruce(Galaxy g, Side side, Side from) => RefuseDeal(g, side, from, DealKind.Truce);

    /// <summary>The deal `from` offered `to` takes effect, both having agreed.</summary>
    static Result Conclude(Galaxy g, Side from, Side to, DealKind kind, double amount, bool perMinute)
    {
        switch (kind)
        {
            case DealKind.Truce:
                Sign(g, from, to);
                break;
            case DealKind.Alliance:
                // The alliance takes over from a truce; whatever else they agreed stands.
                g.Offers.RemoveAll(o => o.Kind is DealKind.Alliance or DealKind.Truce && Pair(o.From, o.To, from, to));
                if (TruceOf(g, from, to) is { } truce) g.Truces.Remove(truce);
                g.Pacts.Add(new Pact { Kind = DealKind.Alliance, A = from, B = to });
                g.Events.Add(new PactSigned(DealKind.Alliance, from, to));
                break;
            case DealKind.Trade:
                g.Offers.RemoveAll(o => o.Kind == DealKind.Trade && Pair(o.From, o.To, from, to));
                g.Pacts.Add(new Pact { Kind = DealKind.Trade, A = from, B = to });
                g.Events.Add(new PactSigned(DealKind.Trade, from, to));
                break;
            default:
            {
                var (payer, payee) = kind == DealKind.Tribute ? (from, to) : (to, from);
                var p = g.Factions[payer];
                if (!perMinute && p.Credits < amount) return Result.Fail("Crédits insuffisants");
                g.Offers.RemoveAll(o => o.Kind is DealKind.Tribute or DealKind.Demand && Pair(o.From, o.To, from, to));
                if (perMinute) g.Tributes.Add(new Tribute { Payer = payer, Payee = payee, PerMinute = amount, Remaining = TributeTime });
                else
                {
                    p.Credits -= amount;
                    g.Factions[payee].Credits += amount;
                }
                // Paid for peace: a truce if there was none, a longer one if there was.
                if (TruceOf(g, payer, payee) is { } bought) bought.Remaining = Math.Max(bought.Remaining, TruceTime);
                else if (AtWar(g, payer, payee)) Sign(g, payer, payee);
                // An AI paid warms to the payer; one made to pay resents it.
                Trusts(g, payee, payer, Math.Min(25, (perMinute ? amount * TributeTime / 60 : amount) / 40));
                if (kind == DealKind.Demand) Trusts(g, payer, payee, -10);
                g.Events.Add(new TributeSigned(payer, payee, amount, perMinute));
                break;
            }
        }
        return Result.OK;
    }

    static bool Pair(Side x, Side y, Side a, Side b) => (x == a && y == b) || (x == b && y == a);

    /// <summary>Breaks a truce at once. An AI wronged so refuses any truce with the breaker for a while, and trusts it far less.</summary>
    public static Result BreakTruce(Galaxy g, Side by, Side other)
    {
        var truce = TruceOf(g, by, other);
        if (truce is null) return Result.Fail("Aucune trêve");
        g.Truces.Remove(truce);
        if (g.Factions.TryGetValue(other, out var o) && o.AI is not null) o.AI.Grudges[by] = Math.Max(o.AI.Grudges.GetValueOrDefault(by), GrudgeTime);
        Trusts(g, other, by, -TrustBrokenTruce);
        g.Events.Add(new TruceEnded(by, other, by));
        PeaceLost(g, by, other, by);
        return Result.OK;
    }

    /// <summary>
    /// `by` ends a deal with `other`. A truce or an alliance broken is a betrayal (BreakTruce,
    /// BreakAlliance); trade may be given up by either. A tribute by the minute stopped by its
    /// payer breaks the peace it paid for; let off by its payee, it just ends.
    /// </summary>
    public static Result EndDeal(Galaxy g, Side by, Side other, DealKind kind)
    {
        switch (kind)
        {
            case DealKind.Truce:
                return BreakTruce(g, by, other);
            case DealKind.Alliance:
                return BreakAlliance(g, by, other);
            case DealKind.Trade:
            {
                if (PactOf(g, DealKind.Trade, by, other) is not { } trade) return Result.Fail("Aucun accord commercial");
                g.Pacts.Remove(trade);
                Trusts(g, other, by, -5);
                g.Events.Add(new PactEnded(DealKind.Trade, by, other, by));
                return Result.OK;
            }
            case DealKind.Tribute or DealKind.Demand:
            {
                if (TributeOf(g, by, other) is not { } t) return Result.Fail("Aucun tribut");
                g.Tributes.Remove(t);
                g.Events.Add(new TributeEnded(t.Payer, t.Payee, by));
                if (by == t.Payee) Trusts(g, t.Payer, by, 10);
                else Defaulted(g, t);
                return Result.OK;
            }
        }
        return Result.Fail("Aucun accord");
    }

    /// <summary>
    /// Breaks an alliance. The wronged AI bears a grudge twice as long as for a truce, and trusts
    /// the breaker far less; every other AI trusts it less too. A short truce follows, for the
    /// fleets to part.
    /// </summary>
    public static Result BreakAlliance(Galaxy g, Side by, Side other)
    {
        if (PactOf(g, DealKind.Alliance, by, other) is not { } alliance) return Result.Fail("Aucune alliance");
        g.Pacts.Remove(alliance);
        if (g.Factions.TryGetValue(other, out var o) && o.AI is not null) o.AI.Grudges[by] = 2 * GrudgeTime;
        Trusts(g, other, by, -TrustBrokenAlliance);
        foreach (var f in g.Factions.Values)
        {
            if (f.Id != by && f.Id != other) Trusts(g, f.Id, by, -TrustWitness);
        }
        g.Truces.Add(new Truce { A = by, B = other, Remaining = NoticeTime });
        g.Events.Add(new PactEnded(DealKind.Alliance, by, other, by));
        return Result.OK;
    }

    /// <summary>A payer that stopped paying broke the peace it paid for: an AI paid holds it against it.</summary>
    static void Defaulted(Galaxy g, Tribute t)
    {
        Trusts(g, t.Payee, t.Payer, -TrustDefault);
        if (TruceOf(g, t.Payer, t.Payee) is not null) BreakTruce(g, t.Payer, t.Payee);
    }

    /// <summary>Two sides back at war: their trade and their tributes end with the peace.</summary>
    static void PeaceLost(Galaxy g, Side a, Side b, Side? by)
    {
        if (!AtWar(g, a, b)) return;
        if (PactOf(g, DealKind.Trade, a, b) is { } trade)
        {
            g.Pacts.Remove(trade);
            g.Events.Add(new PactEnded(DealKind.Trade, a, b, null));
        }
        if (TributeOf(g, a, b) is { } t)
        {
            g.Tributes.Remove(t);
            g.Events.Add(new TributeEnded(t.Payer, t.Payee, by));
        }
    }

    static void Sign(Galaxy g, Side a, Side b)
    {
        g.Offers.RemoveAll(o => o.Kind == DealKind.Truce && Pair(o.From, o.To, a, b));
        g.Truces.Add(new Truce { A = a, B = b, Remaining = TruceTime });
        g.Events.Add(new TruceSigned(a, b));
    }

    /// <summary>
    /// Truces, offers and tributes run out; an eliminated faction's go with it, its pacts too.
    /// Tributes are paid, or broken when their payer has nothing left. Grudges fade, and trust
    /// grows with peace or drifts back to nothing.
    /// </summary>
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
                PeaceLost(g, t.A, t.B, null);
            }
        }
        foreach (var o in g.Offers) o.Remaining -= dt;
        g.Offers.RemoveAll(o => o.Remaining <= 0 || Gone(o.From) || Gone(o.To));
        g.Pacts.RemoveAll(p => Gone(p.A) || Gone(p.B));
        foreach (var t in g.Tributes.ToList())
        {
            if (Gone(t.Payer) || Gone(t.Payee))
            {
                g.Tributes.Remove(t);
                continue;
            }
            var (payer, payee) = (g.Factions[t.Payer], g.Factions[t.Payee]);
            var due = t.PerMinute / 60 * dt;
            if (payer.Credits < due)
            {
                g.Tributes.Remove(t);
                g.Events.Add(new TributeEnded(t.Payer, t.Payee, t.Payer));
                Defaulted(g, t);
                continue;
            }
            payer.Credits -= due;
            payee.Credits += due;
            if ((t.Remaining -= dt) <= 0)
            {
                g.Tributes.Remove(t);
                g.Events.Add(new TributeEnded(t.Payer, t.Payee, null));
            }
        }
        foreach (var f in g.Factions.Values)
        {
            if (f.AI is not { } ai) continue;
            ai.OfferWait = Math.Max(0, ai.OfferWait - dt);
            foreach (var k in ai.Grudges.Keys.ToList())
            {
                if ((ai.Grudges[k] -= dt) <= 0) ai.Grudges.Remove(k);
            }
            if (f.Eliminated) continue;
            foreach (var other in g.Factions.Values)
            {
                if (other.Id == f.Id || other.Eliminated) continue;
                var gain = (AtPeace(g, f.Id, other.Id) ? TrustTruce : 0) + (Allied(g, f.Id, other.Id) ? TrustAlliance : 0)
                    + (g.Pacts.Count > 0 && PactOf(g, DealKind.Trade, f.Id, other.Id) is not null ? TrustTrade : 0);
                var now = ai.Trust.GetValueOrDefault(other.Id);
                if (gain > 0) ai.Trust[other.Id] = Math.Min(TrustMax, now + gain * dt);
                else if (now != 0) ai.Trust[other.Id] = now > 0 ? Math.Max(0, now - TrustFade * dt) : Math.Min(0, now + TrustFade * dt);
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
