// Diplomacy between galaxy factions (not in the web version): truces, alliances, trade and
// tributes; shared vision, joint wars, ultimatums, requests for help and bundles of terms are
// GalaxyAccords.cs'. Sides at peace (a truce) never meet in battle, at a planet or on a lane; neither
// sends fleets to the other's planets, nor takes them. Allies, of a team or of an alliance, never
// fight, see together and win together. Trade brings both partners credits for as long as the
// peace holds; a tribute buys it, paid once or by the minute. Never with the Errants, nor between
// the members of a team, who need none of it. An AI keeps count of what each faction did to it
// (Trust), and bears a grudge against one that betrayed it.
using System.Text.Json.Serialization;
using SandboxShip.Sim.Data;

namespace SandboxShip.Sim;

/// <summary>
/// What one galaxy faction may offer another: a truce, an alliance, a trade agreement, a tribute
/// it pays for peace, or one it demands for peace; its sight shared, a war on a third faction
/// waged together, an ultimatum (credits or a planet, or war), an AI's request for help, or
/// several terms at once (DealTerms); a lasting peace, a planet or a tech level passed from one to
/// the other for credits (GalaxyRelations.cs). New kinds go last: their numbers travel and are saved.
/// </summary>
public enum DealKind { Truce, Alliance, Trade, Tribute, Demand, Vision, JointWar, Ultimatum, Help, Bundle, Peace, Planet, Tech }

/// <summary>A non-aggression pact between two galaxy factions.</summary>
public sealed class Truce
{
    public Side A, B;
    /// <summary>Galaxy seconds left.</summary>
    public double Remaining;
    /// <summary>A tribute prolonged it already: that happens once only, or peace could be bought for ever.</summary>
    public bool Renewed;
    /// <summary>A tribute bought or prolonged it: even an AI that breaks truces keeps this one.</summary>
    public bool Paid;
    /// <summary>
    /// The peace every two great factions start a campaign with (CampaignSetup.OpeningPeace): nobody
    /// may break it, nor threaten its partner meanwhile, and keeping it builds no trust.
    /// </summary>
    public bool Opening;
    /// <summary>
    /// A lasting peace (DealKind.Peace): it never runs out, and cannot be broken at once, only
    /// denounced (Galaxies.Denounce), which leaves NoticeTime before the war. Signed during the
    /// opening peace, it is both until the opening peace ends.
    /// </summary>
    public bool Lasting;
}

/// <summary>
/// An alliance, a trade agreement or shared vision between two galaxy factions, for as long as
/// both keep it; or a joint war against `Target`, for `Remaining` galaxy seconds.
/// </summary>
public sealed class Pact
{
    public DealKind Kind;
    public Side A, B;
    public Side? Target;
    public double Remaining;
    /// <summary>Galaxy time it was signed: trade grows with its age (Galaxies.TradeRate).</summary>
    public double Since;
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
    /// <summary>A joint war's target.</summary>
    public Side? Target;
    /// <summary>The planet an ultimatum demands, or a request for help asks to defend.</summary>
    public int? Planet;
    /// <summary>A bundle's terms.</summary>
    public DealTerms Terms;
    /// <summary>The tech a level of which a tech deal passes on.</summary>
    public TechId? Tech;
}

public sealed record DealOffered(Side From, Side To, DealKind Kind, double Amount, bool PerMinute, Side? Target = null, int? Planet = null, DealTerms Terms = DealTerms.None,
    TechId? Tech = null) : GalaxyEvent;
/// <summary>`To` turned down what `From` offered: an AI says why, a player does not.</summary>
public sealed record DealRefused(Side From, Side To, DealKind Kind, string Reason) : GalaxyEvent;
public sealed record TruceSigned(Side A, Side B) : GalaxyEvent;
/// <summary>A truce ran out, or `BrokenBy` broke it.</summary>
public sealed record TruceEnded(Side A, Side B, Side? BrokenBy) : GalaxyEvent;
/// <summary>
/// A lasting peace signed between `From` and `To` (Kind Peace), or a planet (Kind Planet, `Extra`
/// its id) or a tech level (Kind Tech, `Extra` the tech) passed from `From` to `To` for `Amount` credits.
/// </summary>
public sealed record Concluded(DealKind Kind, Side From, Side To, double Amount = 0, int Extra = -1) : GalaxyEvent;
/// <summary>`By` denounced its lasting peace with `Other`: war in NoticeTime.</summary>
public sealed record PeaceDenounced(Side By, Side Other) : GalaxyEvent;
/// <summary>An alliance, a trade agreement, shared vision or a joint war (against `Target`) signed.</summary>
public sealed record PactSigned(DealKind Kind, Side A, Side B, Side? Target = null) : GalaxyEvent;
/// <summary>A pact ended: `By` gave it up or broke it, or (null) it ran out or lost the peace it needs.</summary>
public sealed record PactEnded(DealKind Kind, Side A, Side B, Side? By, Side? Target = null) : GalaxyEvent;
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
    /// <summary>The trust each faction's gifts bought it lately, up to TrustBuyable: it wears off over TruceTime.</summary>
    public Dictionary<Side, double> BoughtTrust = [];
    /// <summary>Galaxy seconds before the AI signs a truce again with each AI it just came out of one with.</summary>
    public Dictionary<Side, double> TruceRest = [];
    /// <summary>On a replica, what the snapshot told it asks the viewer for peace, in credits a minute; null on the host, who works it out (GalaxyAi.PeacePrice).</summary>
    [JsonIgnore]
    public double? Asked;
}

public static partial class Galaxies
{
    /// <summary>
    /// Galaxy seconds a truce lasts, an AI wronged by a broken one bears a grudge, an offer to the
    /// player waits for an answer, and an AI waits between two offers.
    /// </summary>
    public const double TruceTime = 600, GrudgeTime = 900, OfferTime = 60, OfferPause = 300;
    /// <summary>
    /// Galaxy seconds two AIs wait, once a truce between them ran out, before signing another:
    /// signed again at once, two AIs left alone kept a peace that never ended the campaign.
    /// </summary>
    public const double RetruceTime = 600;
    /// <summary>Galaxy seconds of truce a broken alliance leaves, for the fleets to part; a tribute by the minute lasts.</summary>
    public const double NoticeTime = 120, TributeTime = 600;
    /// <summary>
    /// Credits a second that trade brings each partner: so much, so much more per lane between
    /// their planets (TradeLanes at most) and a share of the poorer one's planets' worth, the whole
    /// growing from half to one and a half over TradeMaturity galaxy seconds; TradeMax at most.
    /// </summary>
    public const double TradeBase = 1, TradePerLane = 1, TradeLanes = 4, TradeShare = 0.1, TradeMaturity = 600, TradeMax = 30;
    /// <summary>The largest tribute, once and by the minute: a bigger one is refused as it is read.</summary>
    public const double MaxTribute = 10000, MaxTributePerMinute = 1000;
    public const double TrustMax = 100;
    /// <summary>
    /// The trust gifts can buy an AI over TruceTime, one point for 40 credits: an alliance stays
    /// a matter of time, gifts only shortening the wait. Below what any personality's alliance needs.
    /// </summary>
    public const double TrustBuyable = 15;
    /// <summary>
    /// What a second adds to an AI's trust in a faction it has a truce, a trade agreement or an
    /// alliance with; how fast trust drifts back to nothing without any; what a battle it is
    /// attacked in, a broken truce, a broken alliance and an unpaid tribute take off it; what a
    /// faction that broke an alliance loses with every other AI.
    /// </summary>
    const double TrustTruce = 0.05, TrustTrade = 0.05, TrustAlliance = 0.08;
    public const double TrustFade = 0.02;
    const double TrustAttack = 5, TrustBrokenTruce = 40, TrustBrokenAlliance = 70, TrustDefault = 30, TrustWitness = 15;
    /// <summary>Attacks alone sink trust no lower than this: only betrayals go further.</summary>
    const double TrustAttackFloor = -60;

    // Plain loops: asked for every pair of factions at every step, a predicate capturing them
    // allocated each time.
    public static Truce? TruceOf(Galaxy g, Side a, Side b)
    {
        foreach (var t in g.Truces)
        {
            if ((t.A == a && t.B == b) || (t.A == b && t.B == a)) return t;
        }
        return null;
    }

    public static Pact? PactOf(Galaxy g, DealKind kind, Side a, Side b)
    {
        foreach (var p in g.Pacts)
        {
            if (p.Kind == kind && ((p.A == a && p.B == b) || (p.A == b && p.B == a))) return p;
        }
        return null;
    }

    /// <summary>The tribute one of the two pays the other, if any.</summary>
    public static Tribute? TributeOf(Galaxy g, Side a, Side b)
    {
        foreach (var t in g.Tributes)
        {
            if ((t.Payer == a && t.Payee == b) || (t.Payer == b && t.Payee == a)) return t;
        }
        return null;
    }

    /// <summary>Whether two sides are bound by a truce.</summary>
    public static bool AtPeace(Galaxy g, Side a, Side b) => g.Truces.Count > 0 && a != b && TruceOf(g, a, b) is not null;

    /// <summary>Whether two sides signed an alliance.</summary>
    public static bool Allied(Galaxy g, Side a, Side b) => g.Pacts.Count > 0 && a != b && PactOf(g, DealKind.Alliance, a, b) is not null;

    /// <summary>A side's race: its galaxy faction's; a side with none (the Errants) is its own race's (see Side).</summary>
    public static Faction RaceOf(Galaxy g, Side side) => g.Factions.TryGetValue(side, out var f) ? f.Race : Worlds.OwnRace(side);

    /// <summary>A side's team: 0 (alone) for a side with no galaxy faction, the Errants.</summary>
    public static int TeamOf(Galaxy g, Side side) => g.Factions.TryGetValue(side, out var f) ? f.Team : 0;

    /// <summary>The same side, or two sides of the same team other than 0.</summary>
    public static bool Teammates(Galaxy g, Side a, Side b) => a == b || (TeamOf(g, a) is var t && t != 0 && t == TeamOf(g, b));

    /// <summary>Teammates, or sides that signed an alliance: they never fight, see together and win together.</summary>
    public static bool Allies(Galaxy g, Side a, Side b) => Teammates(g, a, b) || Allied(g, a, b);

    /// <summary>Whether two sides fight when they meet: neither allies nor bound by a truce. The Errants are at war with all.</summary>
    public static bool AtWar(Galaxy g, Side a, Side b) => !Allies(g, a, b) && !AtPeace(g, a, b);

    /// <summary>Whether `side` won the campaign: its own victory or its allies'.</summary>
    public static bool Won(Galaxy g, Side side) => g.Winner is Side w && Allies(g, w, side);

    /// <summary>How far `ai`'s AI trusts `other`: 0 for a side with no AI.</summary>
    public static double TrustOf(Galaxy g, Side ai, Side other) => g.Factions.TryGetValue(ai, out var f) ? f.AI?.Trust.GetValueOrDefault(other) ?? 0 : 0;

    /// <summary>A gift worth `worth` warms `ai` to `from`, as far as what it bought lately leaves room for.</summary>
    static void BuyTrust(Galaxy g, Side ai, Side from, double worth)
    {
        if (!g.Factions.TryGetValue(ai, out var f) || f.AI is not { } state) return;
        var bought = state.BoughtTrust.GetValueOrDefault(from);
        var gain = Math.Min(worth, TrustBuyable - bought);
        if (gain <= 0) return;
        state.BoughtTrust[from] = bought + gain;
        Trusts(g, ai, from, gain, cause: TrustCause.Gifts);
    }

    /// <summary>Moves `ai`'s trust in `other` by `delta`, within ±TrustMax (or no lower than `floor`, if it was above); `cause` keeps what moved it.</summary>
    static void Trusts(Galaxy g, Side ai, Side other, double delta, double floor = -TrustMax, TrustCause cause = TrustCause.Attacks)
    {
        if (!g.Factions.TryGetValue(ai, out var f) || f.AI is not { } state || ai == other) return;
        var now = state.Trust.GetValueOrDefault(other);
        var low = Math.Min(now, floor);
        var next = Math.Clamp(now + delta, Math.Max(-TrustMax, low), TrustMax);
        state.Trust[other] = next;
        Credit(state, other, cause, next - now);
    }

    /// <summary>What `cause` did to an AI's trust in `other`, kept for the diplomacy screen.</summary>
    static void Credit(GalaxyAIState ai, Side other, TrustCause cause, double delta)
    {
        if (delta == 0) return;
        if (!ai.Why.TryGetValue(other, out var causes)) ai.Why[other] = causes = [];
        causes[cause] = causes.GetValueOrDefault(cause) + delta;
    }

    /// <summary>`attacker` opened a battle against `defender`: the defender's AI trusts it less.</summary>
    static void Attacked(Galaxy g, Side attacker, Side defender) => Trusts(g, defender, attacker, -TrustAttack, TrustAttackFloor, TrustCause.Attacks);

    /// <summary>
    /// The credits a second that trade brings each of `a` and `b`: more with each lane between
    /// their planets and the richer the poorer of the two, more the longer it lasts (from what
    /// all know: who holds which planet, and what it yields at least).
    /// </summary>
    public static double TradeRate(Galaxy g, Side a, Side b)
    {
        var lanes = 0;
        foreach (var l in g.Lanes)
        {
            var (x, y) = (g.Planets[l.A].Owner, g.Planets[l.B].Owner);
            if ((x == a && y == b) || (x == b && y == a)) lanes++;
        }
        double worthA = 0, worthB = 0;
        foreach (var p in g.Planets)
        {
            if (p.Owner == a) worthA += p.Income;
            else if (p.Owner == b) worthB += p.Income;
        }
        var age = PactOf(g, DealKind.Trade, a, b) is { } pact ? g.Time - pact.Since : 0;
        var maturity = 0.5 + Math.Clamp(age / TradeMaturity, 0, 1);
        return Math.Min(TradeMax, (TradeBase + TradePerLane * Math.Min(TradeLanes, lanes) + TradeShare * Math.Min(worthA, worthB)) * maturity);
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
    public static Result CanPropose(Galaxy g, Side from, Side to, DealKind kind, double amount = 0, bool perMinute = false,
        Side? target = null, int? planet = null, DealTerms terms = DealTerms.None, TechId? tech = null)
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
            case DealKind.Vision:
                if (Allied(g, from, to) || PactOf(g, DealKind.Vision, from, to) is not null) return Result.Fail("Vision déjà partagée");
                if (AtWar(g, from, to)) return Result.Fail("Il faut d'abord la paix");
                break;
            case DealKind.JointWar:
                if (target is not Side t || t == from || t == to || !g.Factions.TryGetValue(t, out var aim) || aim.Eliminated) return Result.Fail("Choisis une cible");
                if (AtWar(g, from, to)) return Result.Fail("Il faut d'abord la paix");
                if (!AtWar(g, from, t) || !AtWar(g, to, t)) return Result.Fail("Il faut être tous deux en guerre contre elle");
                if (JointWarOf(g, from, to) is not null) return Result.Fail("Guerre commune déjà en cours");
                break;
            case DealKind.Ultimatum:
                if (Allied(g, from, to)) return Result.Fail("Pas entre alliées");
                if (AtWar(g, from, to)) return Result.Fail("Déjà en guerre : exige plutôt un tribut");
                if (TruceOf(g, from, to) is { Opening: true }) return Result.Fail("Pas pendant la paix de départ");
                if (GalaxyAi.Might(g, from) < GalaxyAi.Might(g, to) * UltimatumEdge) return Result.Fail("Pas assez forte pour l'exiger");
                if (planet is int p)
                {
                    var r = CanDemandPlanet(g, from, to, p);
                    if (!r.Ok) return r;
                }
                else if (!double.IsFinite(amount) || amount <= 0 || amount > MaxTribute) return Result.Fail("Montant invalide");
                break;
            case DealKind.Help:
                if (a.AI is null || b.AI is not null) return Result.Fail("Seule une IA demande de l'aide, et à un joueur");
                if (planet is int asked ? (uint)asked >= (uint)g.Planets.Count || g.Planets[asked].Owner != from
                    : !double.IsFinite(amount) || amount <= 0 || amount > MaxTribute) return Result.Fail("Demande invalide");
                break;
            case DealKind.Bundle:
                return CanBundle(g, from, to, terms, amount, perMinute, a);
            case DealKind.Peace:
                if (Allied(g, from, to)) return Result.Fail("Déjà alliées");
                if (TruceOf(g, from, to) is { Lasting: true }) return Result.Fail("Paix durable déjà en cours");
                break;
            case DealKind.Planet:
                return CanTradePlanet(g, from, to, planet, amount);
            case DealKind.Tech:
                return CanTradeTech(g, from, to, tech, amount);
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
    public static Result ProposeDeal(Galaxy g, Side from, Side to, DealKind kind, double amount = 0, bool perMinute = false,
        Side? target = null, int? planet = null, DealTerms terms = DealTerms.None, TechId? tech = null)
    {
        var r = CanPropose(g, from, to, kind, amount, perMinute, target, planet, terms, tech);
        if (!r.Ok) return r;
        if (kind is DealKind.Truce or DealKind.Alliance or DealKind.Trade or DealKind.Vision or DealKind.Peace && g.Offers.Exists(o => o.From == to && o.To == from && o.Kind == kind))
            return AcceptDeal(g, from, to, kind);
        // A threat is held against its maker, whatever comes of it.
        if (kind == DealKind.Ultimatum) Repute(g, from, -5);
        if (g.Factions[to].AI is not null)
        {
            var answer = GalaxyAi.AnswerDeal(g, to, from, kind, amount, perMinute, target, planet, terms, tech);
            if (answer.Ok) answer = Conclude(g, from, to, kind, amount, perMinute, target, planet, terms, tech);
            if (!answer.Ok)
            {
                Note(g, new DealRefused(from, to, kind, answer.Reason));
                if (kind == DealKind.Ultimatum) Defied(g, from, to);
            }
            return answer;
        }
        var remaining = kind == DealKind.Ultimatum ? UltimatumTime : OfferTime;
        g.Offers.Add(new DealOffer { From = from, To = to, Kind = kind, Amount = amount, PerMinute = perMinute, Remaining = remaining, Target = target, Planet = planet, Terms = terms, Tech = tech });
        Note(g, new DealOffered(from, to, kind, amount, perMinute, target, planet, terms, tech));
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
        var still = CanPropose(g, from, side, kind, offer.Amount, offer.PerMinute, offer.Target, offer.Planet, offer.Terms, offer.Tech);
        return still.Ok ? Conclude(g, from, side, kind, offer.Amount, offer.PerMinute, offer.Target, offer.Planet, offer.Terms, offer.Tech) : Result.Fail("L'offre n'est plus valable");
    }

    public static Result AcceptTruce(Galaxy g, Side side, Side from) => AcceptDeal(g, side, from, DealKind.Truce);

    /// <summary>`side` turns down the deal of this kind `from` offered it.</summary>
    public static void RefuseDeal(Galaxy g, Side side, Side from, DealKind kind)
    {
        if (g.Offers.Find(o => o.From == from && o.To == side && o.Kind == kind) is not { } offer) return;
        g.Offers.Remove(offer);
        Note(g, new DealRefused(from, side, kind, ""));
        Turned(g, offer);
    }

    /// <summary>What a refusal, or an offer let lapse, brings: war after an ultimatum, an AI disappointed after a request for help.</summary>
    static void Turned(Galaxy g, DealOffer o)
    {
        if (o.Kind == DealKind.Ultimatum) Defied(g, o.From, o.To);
        else if (o.Kind == DealKind.Help)
        {
            Trusts(g, o.From, o.To, -8, cause: TrustCause.Help);
            Say(g, o.From, o.To, Remark.Disappointed);
        }
    }

    public static void RefuseTruce(Galaxy g, Side side, Side from) => RefuseDeal(g, side, from, DealKind.Truce);

    /// <summary>The deal `from` offered `to` takes effect, both having agreed.</summary>
    static Result Conclude(Galaxy g, Side from, Side to, DealKind kind, double amount, bool perMinute, Side? target, int? planet, DealTerms terms = DealTerms.None,
        TechId? tech = null)
    {
        switch (kind)
        {
            case DealKind.Truce:
                Sign(g, from, to);
                break;
            case DealKind.Peace:
                g.Offers.RemoveAll(o => o.Kind is DealKind.Truce or DealKind.Peace && Pair(o.From, o.To, from, to));
                if (TruceOf(g, from, to) is not { } peace)
                {
                    Sign(g, from, to);
                    peace = TruceOf(g, from, to)!;
                }
                peace.Lasting = true;
                Note(g, new Concluded(DealKind.Peace, from, to));
                Say(g, to, from, Remark.Welcome);
                break;
            case DealKind.Planet:
                return TradePlanet(g, from, to, planet!.Value, amount);
            case DealKind.Tech:
                return TradeTech(g, from, to, tech!.Value, amount);
            case DealKind.Alliance:
                // The alliance takes over from a truce and shared vision; whatever else they agreed stands.
                g.Offers.RemoveAll(o => o.Kind is DealKind.Alliance or DealKind.Truce or DealKind.Vision && Pair(o.From, o.To, from, to));
                if (TruceOf(g, from, to) is { } truce) g.Truces.Remove(truce);
                if (PactOf(g, DealKind.Vision, from, to) is { } sight) g.Pacts.Remove(sight);
                PeaceWithTarget(g, from, to);
                g.Pacts.Add(new Pact { Kind = DealKind.Alliance, A = from, B = to });
                Note(g, new PactSigned(DealKind.Alliance, from, to));
                Say(g, to, from, Remark.Welcome);
                Say(g, from, to, Remark.Welcome);
                break;
            case DealKind.Trade or DealKind.Vision:
                g.Offers.RemoveAll(o => o.Kind == kind && Pair(o.From, o.To, from, to));
                g.Pacts.Add(new Pact { Kind = kind, A = from, B = to, Since = g.Time });
                Note(g, new PactSigned(kind, from, to));
                break;
            case DealKind.JointWar:
                g.Offers.RemoveAll(o => o.Kind == DealKind.JointWar && Pair(o.From, o.To, from, to));
                g.Pacts.Add(new Pact { Kind = DealKind.JointWar, A = from, B = to, Target = target, Remaining = JointWarTime });
                Note(g, new PactSigned(DealKind.JointWar, from, to, target));
                break;
            case DealKind.Ultimatum:
                return Yield(g, from, to, amount, planet);
            case DealKind.Help:
                return Help(g, from, to, amount, planet);
            case DealKind.Bundle:
                return ConcludeBundle(g, from, to, terms, amount, perMinute);
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
                // Paid for peace: a truce if there was none; one under way is prolonged once, if
                // the tribute is worth what the AI paid asks for it. A smaller gift only warms it.
                if (TruceOf(g, payer, payee) is { } bought)
                {
                    if (!bought.Renewed && GalaxyAi.WorthPeace(g, payee, payer, amount, perMinute))
                    {
                        bought.Remaining = Math.Max(bought.Remaining, TruceTime);
                        bought.Renewed = bought.Paid = true;
                    }
                }
                else if (AtWar(g, payer, payee))
                {
                    Sign(g, payer, payee);
                    TruceOf(g, payer, payee)!.Paid = true;
                }
                // An AI paid warms to the payer; one made to pay resents it.
                BuyTrust(g, payee, payer, (perMinute ? amount * TributeTime / 60 : amount) / 40);
                if (kind == DealKind.Demand) Trusts(g, payer, payee, -10, cause: TrustCause.Demands);
                else Say(g, payee, payer, Remark.Thanks);
                Note(g, new TributeSigned(payer, payee, amount, perMinute));
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
        if (truce.Opening) return Result.Fail("La paix de départ ne se rompt pas");
        if (truce.Lasting) return Result.Fail("Une paix durable se dénonce, avec un préavis");
        g.Truces.Remove(truce);
        if (g.Factions.TryGetValue(other, out var o) && o.AI is not null) o.AI.Grudges[by] = Math.Max(o.AI.Grudges.GetValueOrDefault(by), GrudgeTime);
        Trusts(g, other, by, -TrustBrokenTruce, cause: TrustCause.Betrayal);
        Repute(g, by, -15);
        Note(g, new TruceEnded(by, other, by));
        Say(g, other, by, Remark.Betrayed);
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
            case DealKind.Peace:
                return Denounce(g, by, other);
            case DealKind.Alliance:
                return BreakAlliance(g, by, other);
            case DealKind.Trade:
            {
                if (PactOf(g, DealKind.Trade, by, other) is not { } trade) return Result.Fail("Aucun accord commercial");
                g.Pacts.Remove(trade);
                Trusts(g, other, by, -5, cause: TrustCause.Trade);
                Note(g, new PactEnded(DealKind.Trade, by, other, by));
                return Result.OK;
            }
            case DealKind.Vision:
            {
                if (PactOf(g, DealKind.Vision, by, other) is not { } sight) return Result.Fail("Aucune vision partagée");
                g.Pacts.Remove(sight);
                Trusts(g, other, by, -5, cause: TrustCause.Betrayal);
                Note(g, new PactEnded(DealKind.Vision, by, other, by));
                return Result.OK;
            }
            case DealKind.JointWar:
            {
                // Left alone in the war: less than a betrayal, more than nothing.
                if (JointWarOf(g, by, other) is not { } war) return Result.Fail("Aucune guerre commune");
                g.Pacts.Remove(war);
                Trusts(g, other, by, -15, cause: TrustCause.Betrayal);
                Repute(g, by, -5);
                Note(g, new PactEnded(DealKind.JointWar, by, other, by, war.Target));
                return Result.OK;
            }
            case DealKind.Tribute or DealKind.Demand:
            {
                if (TributeOf(g, by, other) is not { } t) return Result.Fail("Aucun tribut");
                g.Tributes.Remove(t);
                Note(g, new TributeEnded(t.Payer, t.Payee, by));
                if (by == t.Payee) Trusts(g, t.Payer, by, 10, cause: TrustCause.Gifts);
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
        Trusts(g, other, by, -TrustBrokenAlliance, cause: TrustCause.Betrayal);
        foreach (var f in g.Factions.Values)
        {
            if (f.Id != by && f.Id != other) Trusts(g, f.Id, by, -TrustWitness, cause: TrustCause.Witness);
        }
        Repute(g, by, -30);
        g.Truces.Add(new Truce { A = by, B = other, Remaining = NoticeTime });
        Note(g, new PactEnded(DealKind.Alliance, by, other, by));
        Say(g, other, by, Remark.Betrayed);
        return Result.OK;
    }

    /// <summary>A payer that stopped paying broke the peace it paid for: an AI paid holds it against it.</summary>
    static void Defaulted(Galaxy g, Tribute t)
    {
        Trusts(g, t.Payee, t.Payer, -TrustDefault, cause: TrustCause.Betrayal);
        Repute(g, t.Payer, -10);
        if (TruceOf(g, t.Payer, t.Payee) is not null) BreakTruce(g, t.Payer, t.Payee);
    }

    /// <summary>Two sides back at war: their trade and their tributes end with the peace.</summary>
    static void PeaceLost(Galaxy g, Side a, Side b, Side? by)
    {
        if (!AtWar(g, a, b)) return;
        foreach (var kind in (ReadOnlySpan<DealKind>)[DealKind.Trade, DealKind.Vision, DealKind.JointWar])
        {
            if (PactOf(g, kind, a, b) is not { } pact) continue;
            g.Pacts.Remove(pact);
            Note(g, new PactEnded(kind, a, b, null, pact.Target));
        }
        if (TributeOf(g, a, b) is { } t)
        {
            g.Tributes.Remove(t);
            Note(g, new TributeEnded(t.Payer, t.Payee, by));
        }
    }

    static void Sign(Galaxy g, Side a, Side b)
    {
        g.Offers.RemoveAll(o => o.Kind == DealKind.Truce && Pair(o.From, o.To, a, b));
        PeaceWithTarget(g, a, b);
        g.Truces.Add(new Truce { A = a, B = b, Remaining = TruceTime });
        Note(g, new TruceSigned(a, b));
    }

    /// <summary>
    /// Truces, offers and tributes run out; an eliminated faction's go with it, its pacts too.
    /// Tributes are paid, or broken when their payer has nothing left. Grudges fade, and trust
    /// grows with peace or drifts back to nothing.
    /// </summary>
    static void UpdateDiplomacy(Galaxy g, double dt)
    {
        bool Gone(Side f) => !g.Factions.TryGetValue(f, out var gf) || gf.Eliminated;
        var opened = false;
        foreach (var t in g.Truces.ToList())
        {
            // A lasting peace never runs out; the opening peace it may still be does.
            if (!t.Lasting || t.Opening) t.Remaining -= dt;
            if (Gone(t.A) || Gone(t.B)) g.Truces.Remove(t);
            else if (t.Remaining <= 0 && t.Opening)
            {
                // The opening peace ends for all at once: one notice (PeaceOver), not one per pair.
                // A lasting peace signed meanwhile holds on.
                opened = true;
                t.Opening = false;
                if (t.Lasting) continue;
                g.Truces.Remove(t);
                PeaceLost(g, t.A, t.B, null);
            }
            else if (t.Remaining <= 0 && !t.Lasting)
            {
                // Kept to its end: both sides' word is worth a little more.
                g.Truces.Remove(t);
                Repute(g, t.A, 2);
                Repute(g, t.B, 2);
                Note(g, new TruceEnded(t.A, t.B, null));
                PeaceLost(g, t.A, t.B, null);
                if (g.Factions[t.A].AI is { } a && g.Factions[t.B].AI is { } b)
                {
                    a.TruceRest[t.B] = RetruceTime;
                    b.TruceRest[t.A] = RetruceTime;
                }
            }
        }
        if (opened && !g.Truces.Exists(t => t.Opening)) g.Events.Add(new PeaceOver());
        for (var i = g.Offers.Count - 1; i >= 0; i--)
        {
            var o = g.Offers[i];
            o.Remaining -= dt;
            if (Gone(o.From) || Gone(o.To)) g.Offers.RemoveAt(i);
            else if (o.Remaining <= 0)
            {
                g.Offers.RemoveAt(i);
                Turned(g, o);
                i = Math.Min(i, g.Offers.Count);
            }
        }
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
                Note(g, new TributeEnded(t.Payer, t.Payee, t.Payer));
                Defaulted(g, t);
                continue;
            }
            payer.Credits -= due;
            payee.Credits += due;
            if ((t.Remaining -= dt) <= 0)
            {
                g.Tributes.Remove(t);
                Repute(g, t.Payer, 3);
                Note(g, new TributeEnded(t.Payer, t.Payee, null));
            }
        }
        foreach (var f in g.Factions.Values)
        {
            if (f.AI is not { } ai) continue;
            ai.OfferWait = Math.Max(0, ai.OfferWait - dt);
            Fade(ai.Grudges, dt);
            Fade(ai.TruceRest, dt);
            Fade(ai.Yielded, dt);
            Fade(ai.BoughtTrust, dt * TrustBuyable / TruceTime);
            if (f.Eliminated) continue;
            foreach (var other in g.Factions.Values)
            {
                if (other.Id == f.Id || other.Eliminated) continue;
                var peace = TruceOf(g, f.Id, other.Id) is { Opening: false } ? TrustTruce : 0;
                var alliance = Allied(g, f.Id, other.Id) ? TrustAlliance : 0;
                var trade = g.Pacts.Count > 0 && PactOf(g, DealKind.Trade, f.Id, other.Id) is not null ? TrustTrade : 0;
                var joint = g.Pacts.Count > 0 && JointWarOf(g, f.Id, other.Id) is not null ? TrustJointWar : 0;
                var gain = peace + alliance + trade + joint;
                var now = ai.Trust.GetValueOrDefault(other.Id);
                if (gain > 0)
                {
                    if (now >= TrustMax) continue;
                    ai.Trust[other.Id] = Math.Min(TrustMax, now + gain * dt);
                    Credit(ai, other.Id, TrustCause.Peace, peace * dt);
                    Credit(ai, other.Id, TrustCause.Alliance, alliance * dt);
                    Credit(ai, other.Id, TrustCause.Trade, trade * dt);
                    Credit(ai, other.Id, TrustCause.JointWar, joint * dt);
                }
                else if (now != 0) ai.Trust[other.Id] = now > 0 ? Math.Max(0, now - TrustFade * dt) : Math.Min(0, now + TrustFade * dt);
            }
        }
        UpdateAccords(g, dt);
    }

    /// <summary>What each faction still counts for wears off; at nothing, it is forgotten.</summary>
    static void Fade(Dictionary<Side, double> left, double by)
    {
        if (left.Count == 0) return;
        // In place: a value changed and an entry removed as the walk goes leave it valid (.NET Core 3
        // and later); a copy of the keys allocated for every AI at every step.
        foreach (var k in left.Keys)
        {
            ref var v = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrNullRef(left, k);
            if ((v -= by) <= 0) left.Remove(k);
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
