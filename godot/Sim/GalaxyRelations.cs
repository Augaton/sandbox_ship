// What makes peace worth seeking (not in the web version): war weariness, which wears down a
// faction at war, and the independents a faction may win over without a shot, for credits and
// time. Lasting peace, trade and exchanges are GalaxyDiplomacy.cs' and GalaxyAccords.cs'.
using SandboxShip.Sim.Data;

namespace SandboxShip.Sim;

/// <summary>`Side` started to win over the independents of a planet: in CourtTime, it is theirs unless someone takes it first.</summary>
public sealed record CourtshipBegun(int PlanetId, Side Side) : GalaxyEvent;
/// <summary>The independents of a planet joined `Side`, garrison and platforms with them.</summary>
public sealed record Joined(int PlanetId, Side Side) : GalaxyEvent;

/// <summary>A galaxy faction winning over an independent planet: `Paid` credits, `Remaining` galaxy seconds before it joins.</summary>
public sealed class Courtship
{
    public Side Side;
    public int Planet;
    public double Remaining, Paid;
}

public sealed partial class GalaxyFaction
{
    /// <summary>
    /// War weariness, 0 to 100 (not in the web version): ships and planets lost to other galaxy
    /// factions and fronts held raise it, peace lowers it; it costs income (Galaxies.WearyIncome).
    /// </summary>
    public double Weariness;
}

public sealed partial class Galaxy
{
    /// <summary>Independent planets being won over, at most one per faction (Galaxies.Court).</summary>
    public List<Courtship> Courtships = [];
}

public static partial class Galaxies
{
    /// <summary>
    /// What weariness takes: a battle WearyLoss × the share of its might the faction lost there,
    /// WearyBattle at most; a planet lost WearyPlanet (a capital WearyCapital), each rival faced in
    /// a war WearyFront a minute; it drops by WearyRest a minute, WearyCalm with no war faced at
    /// all. Four fronts held wear a faction down slowly, fewer let it rest. At 100, income × 0.7.
    /// </summary>
    public const double WearyLoss = 15, WearyBattle = 10, WearyPlanet = 6, WearyCapital = 15, WearyFront = 0.5, WearyRest = 2, WearyCalm = 4,
        WearyMax = 100, WearyCost = 0.3;

    /// <summary>The share of its income a faction keeps, worn down by war.</summary>
    public static double WearyIncome(GalaxyFaction f) => 1 - WearyCost * f.Weariness / WearyMax;

    static void Wear(GalaxyFaction f, double by) => f.Weariness = Math.Clamp(f.Weariness + by, 0, WearyMax);

    /// <summary>
    /// A battle between two galaxy factions wears both down by the share of their might each lost
    /// there (GalaxyAi.Might: fleets and a minute of income): the same fleet lost weighs more on a
    /// small realm. Battles with the Errants and the independents are no war between peoples:
    /// they weary nobody.
    /// </summary>
    static void WearBattle(Galaxy g, Side attacker, Side defender, BattleToll toll)
    {
        if (!g.Factions.TryGetValue(attacker, out var a) || !g.Factions.TryGetValue(defender, out var d)) return;
        foreach (var (f, lost) in new[] { (a, toll.Attacker), (d, toll.Defender) })
        {
            var value = Battle.FleetValue(lost.Ships, lost.Heroes);
            if (value > 0) Wear(f, Math.Min(WearyBattle, WearyLoss * value / (GalaxyAi.Might(g, f.Id) + value)));
        }
    }

    /// <summary>A planet taken from a galaxy faction by another one wears its old owner down, its capital most of all.</summary>
    static void WearCapture(Galaxy g, Planet planet, Side? from, Side to)
    {
        if (from is not Side old || !g.Factions.TryGetValue(old, out var f) || !g.Factions.ContainsKey(to)) return;
        Wear(f, planet.Home == old ? WearyCapital : WearyPlanet);
    }

    /// <summary>Each second: fronts held wear factions down, the rest of the time rests them.</summary>
    static void UpdateWeariness(Galaxy g, double dt)
    {
        foreach (var f in g.Factions.Values)
        {
            if (f.Eliminated) continue;
            var fronts = 0;
            foreach (var o in g.Factions.Values)
            {
                if (o.Id != f.Id && !o.Eliminated && AtWar(g, f.Id, o.Id) && GalaxyAi.Contact(g, f.Id, o.Id)) fronts++;
            }
            Wear(f, (fronts > 0 ? fronts * WearyFront - WearyRest : -WearyCalm) * dt / 60);
        }
    }

    /// <summary>Galaxy seconds the independents of a planet take to be won over.</summary>
    public const double CourtTime = 120;

    /// <summary>
    /// What winning over a planet's independents costs `side`: what guards it, which joins the
    /// faction, and its wealth; a faction whose word is good pays less, one known to betray more.
    /// </summary>
    public static double CourtPrice(Galaxy g, Side side, Planet p)
    {
        var reputation = g.Factions.TryGetValue(side, out var f) ? f.Reputation : 0;
        var price = (DefenseValue(g, p) + 300 * p.Income) * (1 - reputation / 250);
        return Math.Ceiling(price / 50) * 50;
    }

    /// <summary>Whether `side` may start winning over the independents of `planet` now, and if not, why.</summary>
    public static Result CanCourt(Galaxy g, Side side, int planet)
    {
        if ((uint)planet >= (uint)g.Planets.Count || !g.Factions.TryGetValue(side, out var f) || f.Eliminated) return Result.Fail("Ordre invalide");
        var p = g.Planets[planet];
        if (p.Owner != Independent) return Result.Fail("Pas une planète indépendante");
        if (g.Courtships.Exists(c => c.Side == side)) return Result.Fail("Un ralliement à la fois");
        if (!Neighbours(g, planet).Exists(n => g.Planets[n].Owner == side)) return Result.Fail("Il faut une planète voisine");
        if (BattleAt(g, planet)) return Result.Fail("Bataille en cours");
        if (f.Credits < CourtPrice(g, side, p)) return Result.Fail("Crédits insuffisants");
        return Result.OK;
    }

    /// <summary>Starts winning over the independents of `planet`: paid now, theirs in CourtTime.</summary>
    public static Result Court(Galaxy g, Side side, int planet)
    {
        var r = CanCourt(g, side, planet);
        if (!r.Ok) return r;
        var price = CourtPrice(g, side, g.Planets[planet]);
        g.Factions[side].Credits -= price;
        g.Courtships.Add(new Courtship { Side = side, Planet = planet, Remaining = CourtTime, Paid = price });
        g.Events.Add(new CourtshipBegun(planet, side));
        return Result.OK;
    }

    /// <summary>
    /// Courtships run, paused while a battle rages at their planet. The first to finish wins the
    /// planet with its garrison and platforms; the others get half of what they paid back. One
    /// whose faction attacked the planet meanwhile, or whose planet fell to arms, is lost.
    /// </summary>
    static void UpdateCourtships(Galaxy g, double dt)
    {
        if (g.Courtships.Count == 0) return;
        foreach (var c in g.Courtships.ToList())
        {
            var p = g.Planets[c.Planet];
            var gone = !g.Factions.TryGetValue(c.Side, out var f) || f.Eliminated;
            if (gone || p.Owner != Independent || g.Battles.Exists(b => b.PlanetId == c.Planet && b.Attacker == c.Side))
            {
                g.Courtships.Remove(c);
                continue;
            }
            if (BattleAt(g, c.Planet) || (c.Remaining -= dt) > 0) continue;
            g.Courtships.Remove(c);
            p.Owner = c.Side;
            foreach (var fleet in g.Fleets.Values)
            {
                if (fleet.Side == Independent && fleet.At == c.Planet) fleet.Side = c.Side;
            }
            foreach (var other in g.Courtships.Where(o => o.Planet == c.Planet).ToList())
            {
                g.Courtships.Remove(other);
                if (g.Factions.TryGetValue(other.Side, out var of)) of.Credits += other.Paid / 2;
            }
            g.Events.Add(new Joined(c.Planet, c.Side));
        }
    }

    /// <summary>
    /// `by` denounces its lasting peace with `other`: a plain truce of NoticeTime is left, that no
    /// tribute prolongs, then war. Said ahead, it is no betrayal; still, its word weighs a little
    /// less, and the other trusts it less. The opening peace binds it still.
    /// </summary>
    public static Result Denounce(Galaxy g, Side by, Side other)
    {
        if (TruceOf(g, by, other) is not { Lasting: true } peace) return Result.Fail("Aucune paix durable");
        if (peace.Opening) return Result.Fail("La paix de départ ne se rompt pas");
        peace.Lasting = false;
        peace.Remaining = NoticeTime;
        peace.Renewed = true;
        peace.Paid = false;
        Repute(g, by, -3);
        Trusts(g, other, by, -10, cause: TrustCause.Betrayal);
        Note(g, new PeaceDenounced(by, other));
        Say(g, other, by, Remark.Defiant);
        return Result.OK;
    }

    /// <summary>What a planet is worth in an exchange: its yield over a few minutes, and what stands in orbit.</summary>
    public static double PlanetValue(Galaxy g, Planet p) => Math.Ceiling((GalaxyAi.WorthOf(p) * 200 + DefenseValue(g, p) * 0.5) / 50) * 50;

    /// <summary>What the next level of a tech is worth to the faction behind: its price.</summary>
    public static double TechValue(int level, TechId tech) => Techs.Defs[tech].Costs[Math.Min(level, Techs.Defs[tech].MaxLevel - 1)];

    /// <summary>Who gives in a tech deal: the one of the two ahead in that tech; null when they stand level.</summary>
    public static (Side Giver, Side Taker)? TechSides(Galaxy g, Side a, Side b, TechId tech)
    {
        var (la, lb) = (g.Factions[a].Research[tech], g.Factions[b].Research[tech]);
        return la == lb ? null : la > lb ? (a, b) : (b, a);
    }

    /// <summary>
    /// Whether `planet` may pass between `from` and `to`, from its owner to the other, who pays
    /// `amount` (0: a gift): in peace or alliance, never a capital nor its owner's last planet, and
    /// not during a battle there.
    /// </summary>
    static Result CanTradePlanet(Galaxy g, Side from, Side to, int? planet, double amount)
    {
        if (planet is not int id || (uint)id >= (uint)g.Planets.Count) return Result.Fail("Choisis une planète");
        var p = g.Planets[id];
        if (p.Owner != from && p.Owner != to) return Result.Fail("Choisis une planète à l'une de vous deux");
        if (AtWar(g, from, to)) return Result.Fail("Il faut d'abord la paix");
        if (p.Home == p.Owner) return Result.Fail("Une capitale ne se cède pas");
        if (g.Planets.Count(o => o.Owner == p.Owner) < 2) return Result.Fail("Sa dernière planète");
        if (BattleAt(g, id)) return Result.Fail("Bataille en cours");
        if (!double.IsFinite(amount) || amount < 0 || amount > MaxTribute) return Result.Fail("Montant invalide");
        if (p.Owner == to && g.Factions[from].Credits < amount) return Result.Fail("Crédits insuffisants");
        return Result.OK;
    }

    /// <summary>Whether a level of `tech` may pass between `from` and `to`, from the one ahead to the other, who pays `amount`: in peace or alliance.</summary>
    static Result CanTradeTech(Galaxy g, Side from, Side to, TechId? tech, double amount)
    {
        if (tech is not TechId t || !Enum.IsDefined(t)) return Result.Fail("Choisis une technologie");
        if (AtWar(g, from, to)) return Result.Fail("Il faut d'abord la paix");
        if (TechSides(g, from, to, t) is not { } sides) return Result.Fail("Vous en êtes au même niveau");
        // A level researched meanwhile would go past the last one, or be paid for twice.
        if (ResearchQueue.Jobs(g.Factions[sides.Taker]).Exists(j => j.Tech == t)) return Result.Fail("Déjà en recherche");
        if (!double.IsFinite(amount) || amount < 0 || amount > MaxTribute) return Result.Fail("Montant invalide");
        if (sides.Taker == from && g.Factions[from].Credits < amount) return Result.Fail("Crédits insuffisants");
        return Result.OK;
    }

    /// <summary>The planet passes to the other, who pays its owner; a gift to an AI warms it.</summary>
    static Result TradePlanet(Galaxy g, Side from, Side to, int planet, double amount)
    {
        var p = g.Planets[planet];
        var (giver, taker) = p.Owner == from ? (from, to) : (to, from);
        if (g.Factions[taker].Credits < amount) return Result.Fail("Crédits insuffisants");
        g.Factions[taker].Credits -= amount;
        g.Factions[giver].Credits += amount;
        var worth = PlanetValue(g, p);
        Cede(g, p, taker, ceded: false);
        if (amount < worth) BuyTrust(g, taker, giver, (worth - amount) / 40);
        Note(g, new Concluded(DealKind.Planet, giver, taker, amount, planet));
        return Result.OK;
    }

    /// <summary>The one behind in the tech gains a level, and pays the one ahead; a gift to an AI warms it.</summary>
    static Result TradeTech(Galaxy g, Side from, Side to, TechId tech, double amount)
    {
        var (giver, taker) = TechSides(g, from, to, tech)!.Value;
        var t = g.Factions[taker];
        if (t.Credits < amount) return Result.Fail("Crédits insuffisants");
        var worth = TechValue(t.Research[tech], tech);
        t.Credits -= amount;
        g.Factions[giver].Credits += amount;
        t.Research[tech]++;
        if (amount < worth) BuyTrust(g, taker, giver, (worth - amount) / 40);
        Note(g, new Concluded(DealKind.Tech, giver, taker, amount, (int)tech));
        return Result.OK;
    }
}
