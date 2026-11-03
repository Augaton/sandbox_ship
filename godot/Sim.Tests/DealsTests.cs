// Diplomacy beyond truces (not in the web version): alliances that share vision, keep the peace
// and win together; trade that pays both partners while the peace holds; tributes that buy it;
// the betrayals that cost trust; and an AI that answers by its strength, its personality and
// what was done to it. Over the network and in saves too.
using System.Text.Json.Nodes;
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.Galaxies;
using static SandboxShip.Sim.Tests.SupplyTests;

namespace SandboxShip.Sim.Tests;

public class DealsTests
{
    static readonly Side Klaed = Faction.Klaed, Nairan = Faction.Nairan, Nautolan = Faction.Nautolan;

    /// <summary>Three factions, the Kla'ed player and two AIs, without Errants nor random events.</summary>
    static Galaxy Three(Personality nairan = Personality.Balanced)
    {
        var g = CreateGalaxy(
            [new Slot(Klaed, Faction.Klaed), new Slot(Nairan, Faction.Nairan, 0, Difficulty.Normal, null, nairan), new Slot(Nautolan, Faction.Nautolan, 0, Difficulty.Normal)],
            5, GalaxyGen.GenerateLayout(5, 3, MapSize.Small), errants: false, incidents: false);
        g.Player = Klaed;
        return g;
    }

    static int Capital(Galaxy g, Side side) => g.Planets.First(p => p.Owner == side).Id;

    static void Arm(Galaxy g, Side side, int battlecruisers) => AddShipsAt(g, side, Capital(g, side), new ShipCounts { [ShipClass.Battlecruiser] = battlecruisers });

    static void Trust(Galaxy g, Side ai, Side other, double trust) => g.Factions[ai].AI!.Trust[other] = trust;

    [Fact]
    public void An_alliance_shares_vision_keeps_the_peace_and_wins_together()
    {
        var g = Three();
        Trust(g, Nairan, Klaed, 60);
        Assert.DoesNotContain(Capital(g, Nairan), VisiblePlanets(g, Klaed));
        Assert.True(ProposeDeal(g, Klaed, Nairan, DealKind.Alliance).Ok);
        Assert.True(Allied(g, Klaed, Nairan) && Allies(g, Klaed, Nairan) && !AtWar(g, Klaed, Nairan));
        Assert.Contains(g.Events, e => e is PactSigned { Kind: DealKind.Alliance });
        Assert.Contains(Capital(g, Nairan), VisiblePlanets(g, Klaed));
        // Fleets go to an ally's planets without a fight.
        var fleet = AddShipsAt(g, Klaed, Capital(g, Klaed), new ShipCounts { [ShipClass.Frigate] = 2 })!;
        Assert.True(SendFleet(g, fleet.Id, Capital(g, Nairan)).Ok);
        // The last rival gone, the allies win together.
        foreach (var p in g.Planets.Where(p => p.Owner == Nautolan)) p.Owner = Nairan;
        foreach (var f in g.Fleets.Values.Where(f => f.Side == Nautolan).ToList()) g.Fleets.Remove(f.Id);
        RunGalaxy(g, 1);
        Assert.NotNull(g.Winner);
        Assert.True(Won(g, Klaed) && Won(g, Nairan));
        Assert.False(Won(g, Nautolan));
    }

    [Fact]
    public void An_AI_allies_with_a_faction_it_trusts_or_against_a_common_foe_never_to_end_the_campaign()
    {
        var g = Three();
        Assert.Equal(Result.Fail("Elle ne te fait pas encore assez confiance"), ProposeDeal(g, Klaed, Nairan, DealKind.Alliance));
        Assert.Contains(g.Events, e => e is DealRefused { From: var f, Kind: DealKind.Alliance } && f == Klaed);
        // A Nautolan armada facing it: an ally is welcome.
        Arm(g, Nautolan, 40);
        foreach (var p in g.Planets.Where(p => p.Owner is null)) p.Owner = Nautolan;
        Assert.True(ProposeDeal(g, Klaed, Nairan, DealKind.Alliance).Ok);
        // With two factions left, an alliance would be a victory shared: refused.
        var two = CreateGalaxy(Faction.Klaed, [Faction.Nairan], true, errants: false, incidents: false);
        Trust(two, Nairan, Klaed, 100);
        Assert.Equal(Result.Fail("Elle veut la galaxie pour elle seule"), ProposeDeal(two, Klaed, Nairan, DealKind.Alliance));
    }

    [Fact]
    public void Breaking_an_alliance_leaves_a_short_truce_a_long_grudge_and_mistrust_everywhere()
    {
        var g = Three();
        Trust(g, Nairan, Klaed, 60);
        Trust(g, Nautolan, Klaed, 20);
        Assert.True(ProposeDeal(g, Klaed, Nairan, DealKind.Alliance).Ok);
        Assert.True(EndDeal(g, Klaed, Nairan, DealKind.Alliance).Ok);
        Assert.False(Allied(g, Klaed, Nairan));
        Assert.Equal(NoticeTime, TruceOf(g, Klaed, Nairan)!.Remaining);
        Assert.Contains(g.Events, e => e is PactEnded { Kind: DealKind.Alliance, By: var by } && by == Klaed);
        Assert.Equal(2 * GrudgeTime, g.Factions[Nairan].AI!.Grudges[Klaed]);
        Assert.Equal(60 - 70, TrustOf(g, Nairan, Klaed), 6);
        Assert.Equal(20 - 15, TrustOf(g, Nautolan, Klaed), 6);
        Assert.Equal(Result.Fail("Elle n'a pas oublié ta trahison"), ProposeDeal(g, Klaed, Nairan, DealKind.Alliance));
        RunGalaxy(g, NoticeTime + 1);
        Assert.True(AtWar(g, Klaed, Nairan));
        Assert.Equal(Result.Fail("Elle n'a pas oublié la trêve rompue"), ProposeDeal(g, Klaed, Nairan, DealKind.Truce));
        Assert.Equal(Result.Fail("Aucune alliance"), EndDeal(g, Klaed, Nairan, DealKind.Alliance));
    }

    [Fact]
    public void Trade_needs_peace_pays_both_by_the_lanes_between_them_and_ends_with_it()
    {
        var g = CreateGalaxy(Faction.Klaed, [Faction.Nairan], true, errants: false, incidents: false);
        Assert.Equal(Result.Fail("Il faut d'abord la paix"), ProposeDeal(g, Klaed, Nairan, DealKind.Trade));
        g.Truces.Add(new Truce { A = Klaed, B = Nairan, Remaining = TruceTime });
        var before = (IncomeOf(g, Klaed), IncomeOf(g, Nairan));
        Assert.True(ProposeDeal(g, Klaed, Nairan, DealKind.Trade).Ok);
        Assert.Equal(TradeBase, TradeRate(g, Klaed, Nairan));
        Assert.Equal(before.Item1 + TradeBase, IncomeOf(g, Klaed), 6);
        Assert.Equal(before.Item2 + TradeBase, IncomeOf(g, Nairan), 6);
        // Planets side by side: a lane between them pays more.
        g.Planets[Id("Orin")].Owner = Klaed;
        g.Planets[Id("Kaldra")].Owner = Nairan;
        Expect.Greater(TradeRate(g, Klaed, Nairan), TradeBase);
        Assert.True(TradeRate(g, Klaed, Nairan) <= TradeMax);
        Assert.Equal(Result.Fail("Accord commercial déjà en cours"), ProposeDeal(g, Klaed, Nairan, DealKind.Trade));
        // The peace broken, the trade goes with it.
        Assert.True(BreakTruce(g, Klaed, Nairan).Ok);
        Assert.Empty(g.Pacts);
        Assert.Contains(g.Events, e => e is PactEnded { Kind: DealKind.Trade });
        Assert.Equal(0, TradeIncome(g, Klaed));
    }

    /// <summary>The classic galaxy, the Nairan AI far stronger than the Kla'ed player.</summary>
    static Galaxy StrongAi()
    {
        var g = CreateGalaxy(Faction.Klaed, [Faction.Nairan], true, errants: false, incidents: false);
        AddShipsAt(g, Nairan, Id("Kaldra"), new ShipCounts { [ShipClass.Battlecruiser] = 8 });
        g.Planets[Id("Kaldra")].Owner = Nairan;
        return g;
    }

    [Fact]
    public void A_tribute_by_the_minute_buys_peace_from_a_stronger_AI_at_its_price()
    {
        var g = StrongAi();
        Assert.Equal(Result.Fail("Elle se croit la plus forte"), ProposeTruce(g, Klaed, Nairan));
        var price = GalaxyAi.PeacePrice(g, Nairan, Klaed);
        Expect.Greater(price, 0);
        Assert.Equal(Result.Fail($"Elle en veut au moins {(int)price} crédits par minute de trêve"), ProposeDeal(g, Klaed, Nairan, DealKind.Tribute, price - 10, true));
        Assert.True(ProposeDeal(g, Klaed, Nairan, DealKind.Tribute, price, true).Ok);
        Assert.True(AtPeace(g, Klaed, Nairan));
        var t = Assert.Single(g.Tributes);
        Assert.Equal((Klaed, Nairan, price), (t.Payer, t.Payee, t.PerMinute));
        g.Factions[Klaed].Credits = 100000;
        // The AI left to think no more, nothing but income and the tribute moves the credits.
        g.Factions[Nairan].AI!.Timer = 1e9;
        var (k, n) = (g.Factions[Klaed].Credits, g.Factions[Nairan].Credits);
        RunGalaxy(g, 60);
        // A minute's income besides: the tribute is what one lost and the other gained beyond it.
        var gained = g.Factions[Nairan].Credits - n - IncomeOf(g, Nairan) * 60 * Worlds.Difficulties[Difficulty.Normal].Income;
        Assert.InRange(gained, price - 1, price + 1);
        Assert.InRange(k + IncomeOf(g, Klaed) * 60 - g.Factions[Klaed].Credits, price - 1, price + 1);
        Assert.Equal(Result.Fail("Tribut déjà en cours"), ProposeDeal(g, Klaed, Nairan, DealKind.Tribute, 50));
    }

    [Fact]
    public void A_tribute_paid_once_buys_the_same_peace_and_warms_the_AI()
    {
        var g = StrongAi();
        var price = GalaxyAi.PeacePrice(g, Nairan, Klaed);
        var once = price * TruceTime / 60;
        g.Factions[Klaed].Credits = once - 1;
        Assert.Equal(Result.Fail("Crédits insuffisants"), ProposeDeal(g, Klaed, Nairan, DealKind.Tribute, once));
        g.Factions[Klaed].Credits = once + 5;
        var theirs = g.Factions[Nairan].Credits;
        Assert.True(ProposeDeal(g, Klaed, Nairan, DealKind.Tribute, once).Ok);
        Assert.Equal(5, g.Factions[Klaed].Credits, 6);
        Assert.Equal(theirs + once, g.Factions[Nairan].Credits, 6);
        Assert.Equal(TruceTime, TruceOf(g, Klaed, Nairan)!.Remaining);
        Expect.Greater(TrustOf(g, Nairan, Klaed), 0);
        Assert.Empty(g.Tributes);
    }

    [Fact]
    public void A_tribute_unpaid_or_stopped_by_its_payer_breaks_the_peace()
    {
        var g = StrongAi();
        var price = GalaxyAi.PeacePrice(g, Nairan, Klaed);
        Assert.True(ProposeDeal(g, Klaed, Nairan, DealKind.Tribute, price, true).Ok);
        g.Factions[Klaed].Credits = 0;
        foreach (var p in g.Planets.Where(p => p.Owner == Klaed)) p.Owner = null;
        g.Planets[Id("Vesper")].Owner = Klaed;
        g.Planets[Id("Vesper")].Income = 0;
        g.Planets[Id("Vesper")].Mines = 0;
        RunGalaxy(g, 1);
        Assert.Empty(g.Tributes);
        Assert.Contains(g.Events, e => e is TributeEnded { By: var by } && by == Klaed);
        Assert.True(AtWar(g, Klaed, Nairan));
        Assert.True(g.Factions[Nairan].AI!.Grudges.ContainsKey(Klaed));

        var h = StrongAi();
        Assert.True(ProposeDeal(h, Klaed, Nairan, DealKind.Tribute, GalaxyAi.PeacePrice(h, Nairan, Klaed), true).Ok);
        Assert.True(EndDeal(h, Klaed, Nairan, DealKind.Tribute).Ok);
        Assert.True(AtWar(h, Klaed, Nairan));
        Assert.Contains(h.Events, e => e is TruceEnded { BrokenBy: var by } && by == Klaed);
    }

    [Fact]
    public void A_far_weaker_AI_pays_a_demand_a_stronger_one_does_not()
    {
        var weak = CreateGalaxy(Faction.Klaed, [Faction.Nairan], true, errants: false, incidents: false);
        AddShipsAt(weak, Klaed, Id("Vesper"), new ShipCounts { [ShipClass.Battlecruiser] = 8 });
        Assert.Equal(Result.Fail("Elle n'a pas de quoi payer"), ProposeDeal(weak, Klaed, Nairan, DealKind.Demand, 900, true));
        Assert.True(ProposeDeal(weak, Klaed, Nairan, DealKind.Demand, 30, true).Ok);
        var t = Assert.Single(weak.Tributes);
        Assert.Equal((Nairan, Klaed), (t.Payer, t.Payee));
        Assert.True(AtPeace(weak, Klaed, Nairan));
        Expect.Less(TrustOf(weak, Nairan, Klaed), 0);
        // Let off by the one it pays, it ends in peace.
        Assert.True(EndDeal(weak, Klaed, Nairan, DealKind.Demand).Ok);
        Assert.True(AtPeace(weak, Klaed, Nairan));

        var strong = StrongAi();
        Assert.Equal(Result.Fail("Elle ne te craint pas"), ProposeDeal(strong, Klaed, Nairan, DealKind.Demand, 30, true));
        Assert.Equal(Result.Fail("Montant invalide"), ProposeDeal(strong, Klaed, Nairan, DealKind.Demand, double.NaN));
        Assert.Equal(Result.Fail("Montant invalide"), ProposeDeal(strong, Klaed, Nairan, DealKind.Tribute, MaxTribute + 1));
    }

    [Fact]
    public void Trust_grows_in_peace_and_sinks_when_attacked()
    {
        var g = CreateGalaxy(Faction.Klaed, [Faction.Nairan], true, errants: false, incidents: false);
        // An AI that never moves: the battle is Kla'ed's attack.
        g.Factions[Nairan].AI!.Timer = 1e9;
        g.Truces.Add(new Truce { A = Klaed, B = Nairan, Remaining = 200 });
        RunGalaxy(g, 100);
        var grown = TrustOf(g, Nairan, Klaed);
        Assert.InRange(grown, 4, 6);
        RunGalaxy(g, 101);
        Assert.True(AtWar(g, Klaed, Nairan));
        var peaceful = TrustOf(g, Nairan, Klaed);
        Assert.InRange(peaceful, 9, 10);
        // Kla'ed strikes a Nairan planet: the AI trusts it less.
        var kaldra = g.Planets[Id("Kaldra")];
        kaldra.Owner = Nairan;
        AddShipsAt(g, Nairan, kaldra.Id, new ShipCounts { [ShipClass.Frigate] = 1 });
        var fleet = AddShipsAt(g, Klaed, Id("Orin"), new ShipCounts { [ShipClass.Battlecruiser] = 3 })!;
        Assert.True(SendFleet(g, fleet.Id, kaldra.Id).Ok);
        for (var s = 0; s < 120 && !g.Events.Exists(e => e is BattleStarted); s++) RunGalaxy(g, 1);
        Assert.Contains(g.Events, e => e is BattleStarted);
        Expect.Less(TrustOf(g, Nairan, Klaed), peaceful - 4);
    }

    [Fact]
    public void Personalities_weigh_on_peace_an_aggressive_AI_asks_more_and_demands_sooner()
    {
        // Somewhat stronger than the player: the aggressive AI wants paying for peace, the
        // defensive one not; the aggressive one demands tribute, the balanced one not yet.
        Galaxy Edge(Personality p)
        {
            var g = CreateGalaxy(Faction.Klaed, [Faction.Nairan], true, errants: false, incidents: false);
            g.Factions[Nairan].AI!.Personality = p;
            g.Planets[Id("Orin")].Owner = Klaed;
            g.Planets[Id("Kaldra")].Owner = Nairan;
            for (var n = 1; GalaxyAi.Might(g, Nairan) < GalaxyAi.Might(g, Klaed) * 1.5; n++) AddShipsAt(g, Nairan, Id("Kaldra"), new ShipCounts { [ShipClass.Frigate] = 1 });
            RunGalaxy(g, 3);
            return g;
        }
        Expect.Greater(GalaxyAi.PeacePrice(Edge(Personality.Aggressive), Nairan, Klaed), 0);
        Assert.Equal(0, GalaxyAi.PeacePrice(Edge(Personality.Defensive), Nairan, Klaed));
        var a = Edge(Personality.Aggressive);
        Assert.Contains(a.Offers, o => o.From == Nairan && o.Kind == DealKind.Demand && o.PerMinute);
        Assert.DoesNotContain(Edge(Personality.Balanced).Offers, o => o.Kind == DealKind.Demand);
        // The player pays it, or refuses it.
        Assert.True(AcceptDeal(a, Klaed, Nairan, DealKind.Demand).Ok);
        Assert.Equal(Klaed, Assert.Single(a.Tributes).Payer);
    }

    [Fact]
    public void A_player_answers_an_offer_in_its_own_time()
    {
        var g = Three();
        g.Humans[Klaed] = "Ana";
        g.Humans[Nautolan] = "Bea";
        g.Factions[Nautolan].AI = null;
        Assert.True(ProposeDeal(g, Klaed, Nautolan, DealKind.Alliance).Ok);
        Assert.Contains(g.Events, e => e is DealOffered { Kind: DealKind.Alliance });
        Assert.Equal(Result.Fail("Offre déjà faite"), ProposeDeal(g, Klaed, Nautolan, DealKind.Alliance));
        RefuseDeal(g, Nautolan, Klaed, DealKind.Alliance);
        Assert.Contains(g.Events, e => e is DealRefused { Reason: "" });
        Assert.Empty(g.Offers);
        // The same offer made both ways is an agreement.
        Assert.True(ProposeDeal(g, Klaed, Nautolan, DealKind.Alliance).Ok);
        Assert.True(ProposeDeal(g, Nautolan, Klaed, DealKind.Alliance).Ok);
        Assert.True(Allied(g, Klaed, Nautolan));
        // An offer that no longer holds is not signed.
        Assert.True(ProposeDeal(g, Nautolan, Klaed, DealKind.Tribute, 500).Ok);
        g.Factions[Nautolan].Credits = 0;
        Assert.Equal(Result.Fail("L'offre n'est plus valable"), AcceptDeal(g, Klaed, Nautolan, DealKind.Tribute));
    }

    [Fact]
    public void Commands_carry_deals_between_players()
    {
        var g = Three();
        g.Humans[Klaed] = "Ana";
        g.Humans[Nautolan] = "Bea";
        g.Factions[Nautolan].AI = null;
        Assert.True(Commands.Apply(g, Klaed, new GalaxyCommand.ProposeDeal(Nautolan, DealKind.Truce)).Ok);
        Assert.True(Commands.Apply(g, Nautolan, new GalaxyCommand.AnswerDeal(Klaed, DealKind.Truce, true)).Ok);
        Assert.True(Commands.Apply(g, Klaed, new GalaxyCommand.ProposeDeal(Nautolan, DealKind.Trade)).Ok);
        Assert.True(Commands.Apply(g, Nautolan, new GalaxyCommand.AnswerDeal(Klaed, DealKind.Trade, true)).Ok);
        Assert.NotNull(PactOf(g, DealKind.Trade, Klaed, Nautolan));
        Assert.True(Commands.Apply(g, Nautolan, new GalaxyCommand.EndDeal(Klaed, DealKind.Trade)).Ok);
        Assert.Null(PactOf(g, DealKind.Trade, Klaed, Nautolan));
        Assert.False(Commands.Apply(g, Klaed, new GalaxyCommand.ProposeDeal(Nautolan, (DealKind)9)).Ok);
        Assert.False(Commands.Apply(g, Klaed, new GalaxyCommand.ProposeDeal(Nautolan, DealKind.Tribute, double.PositiveInfinity)).Ok);
        Assert.False(Commands.Apply(g, Klaed, new GalaxyCommand.ProposeDeal(Klaed, DealKind.Alliance)).Ok);
    }

    [Fact]
    public void A_replica_knows_the_pacts_tributes_its_offers_and_the_AIs_trust_in_its_viewer()
    {
        var g = Three(Personality.Technological);
        Trust(g, Nairan, Klaed, 60);
        Trust(g, Nautolan, Klaed, -30);
        Assert.True(ProposeDeal(g, Klaed, Nairan, DealKind.Alliance).Ok);
        g.Tributes.Add(new Tribute { Payer = Nautolan, Payee = Nairan, PerMinute = 60, Remaining = 300 });
        g.Offers.Add(new DealOffer { From = Nautolan, To = Klaed, Kind = DealKind.Demand, Amount = 120, PerMinute = true, Remaining = 30 });
        g.Offers.Add(new DealOffer { From = Nautolan, To = Nairan, Kind = DealKind.Trade, Remaining = 30 });
        var events = new List<GalaxyEvent> { new DealRefused(Klaed, Nautolan, DealKind.Alliance, "Elle se méfie de toi"), new TributeEnded(Nautolan, Nairan, null) };
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms)) GalaxySnapshot.Write(g, Klaed, w, events);
        var replica = new Galaxy();
        GalaxySnapshot.ReadChart(new BinaryReader(new MemoryStream(GalaxySnapshot.Chart(g))), replica);
        Assert.NotNull(GalaxySnapshot.Read(new BinaryReader(new MemoryStream(ms.ToArray())), replica));
        Assert.True(Allied(replica, Klaed, Nairan));
        Assert.Equal((Nautolan, Nairan, 60.0), (replica.Tributes[0].Payer, replica.Tributes[0].Payee, replica.Tributes[0].PerMinute));
        var offer = Assert.Single(replica.Offers);
        Assert.Equal((DealKind.Demand, 120.0, true), (offer.Kind, offer.Amount, offer.PerMinute));
        Assert.Equal(Personality.Technological, replica.Factions[Nairan].AI!.Personality);
        Assert.Equal(60, TrustOf(replica, Nairan, Klaed), 3);
        Assert.Equal(-30, TrustOf(replica, Nautolan, Klaed), 3);
        Assert.Contains(replica.Events, e => e is DealRefused { Reason: "Elle se méfie de toi" });
        Assert.Contains(replica.Events, e => e is TributeEnded);
    }

    [Fact]
    public void Deals_and_trust_survive_a_save_and_older_saves_have_none()
    {
        var g = Three();
        Trust(g, Nairan, Klaed, 60);
        Assert.True(ProposeDeal(g, Klaed, Nairan, DealKind.Alliance).Ok);
        Assert.True(ProposeDeal(g, Klaed, Nairan, DealKind.Trade).Ok);
        g.Tributes.Add(new Tribute { Payer = Nautolan, Payee = Klaed, PerMinute = 30, Remaining = 200 });
        g.Offers.Add(new DealOffer { From = Nautolan, To = Klaed, Kind = DealKind.Demand, Amount = 60, PerMinute = true, Remaining = 30 });
        var back = GalaxySave.Deserialize(GalaxySave.Serialize(g))!;
        Assert.Equal(GalaxySave.Serialize(g, 0), GalaxySave.Serialize(back, 0));
        Assert.True(Allied(back, Klaed, Nairan));
        Assert.NotNull(PactOf(back, DealKind.Trade, Klaed, Nairan));
        Assert.Equal(30, Assert.Single(back.Tributes).PerMinute);
        Assert.Equal(DealKind.Demand, Assert.Single(back.Offers).Kind);
        Assert.Equal(60, TrustOf(back, Nairan, Klaed));
        // A save from before: no pacts, no tributes, no trust, offers without a kind (truces).
        var old = JsonNode.Parse(GalaxySave.Serialize(g))!.AsObject();
        Assert.True(old.Remove("pacts") && old.Remove("tributes"));
        foreach (var o in old["offers"]!.AsArray()) Assert.True(o!.AsObject().Remove("kind") && o.AsObject().Remove("amount") && o.AsObject().Remove("perMinute"));
        foreach (var f in old["factions"]!.AsArray())
        {
            if (f!["ai"] is JsonObject ai) Assert.True(ai.Remove("trust") && ai.Remove("personality"));
        }
        var loaded = GalaxySave.Deserialize(old.ToJsonString())!;
        Assert.Empty(loaded.Pacts);
        Assert.Empty(loaded.Tributes);
        Assert.Equal(DealKind.Truce, Assert.Single(loaded.Offers).Kind);
        Assert.Empty(loaded.Factions[Nairan].AI!.Trust);
        Assert.Equal(Personality.Balanced, loaded.Factions[Nairan].AI!.Personality);
        // A forged kind is refused.
        var forged = JsonNode.Parse(GalaxySave.Serialize(g))!.AsObject();
        forged["pacts"]![0]!["kind"] = "conquest";
        Assert.Null(GalaxySave.Deserialize(forged.ToJsonString()));
    }
}
