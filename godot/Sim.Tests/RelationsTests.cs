// What makes peace worth seeking (GalaxyRelations.cs): war weariness, winning independents over,
// lasting peace, trade that grows with it, planets and tech levels exchanged; the AI's answers,
// and all of it over the network and in saves.
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.Galaxies;

namespace SandboxShip.Sim.Tests;

public class RelationsTests
{
    static readonly Side Klaed = Faction.Klaed, Nairan = Faction.Nairan, Nautolan = Faction.Nautolan;

    /// <summary>A new campaign's setup without the opening peace: the Kla'ed player and two AIs, without Errants nor random events.</summary>
    static Galaxy Three(int peace = 0)
    {
        var g = CreateGalaxy(
            [new Slot(Klaed, Faction.Klaed), new Slot(Nairan, Faction.Nairan, 0, Difficulty.Normal), new Slot(Nautolan, Faction.Nautolan, 0, Difficulty.Normal)],
            5, GalaxyGen.GenerateLayout(5, 3, MapSize.Small), errants: false, incidents: false, setup: CampaignSetup.Of(peace));
        g.Player = Klaed;
        return g;
    }

    static Planet Capital(Galaxy g, Side side) => g.Planets.First(p => p.Home == side);

    /// <summary>An independent planet next to `side`'s capital, the cheapest to win over.</summary>
    static Planet NextDoor(Galaxy g, Side side) =>
        g.Planets.Where(p => p.Owner == Independent && Neighbours(g, Capital(g, side).Id).Contains(p.Id)).OrderBy(p => CourtPrice(g, side, p)).First();

    static void Still(Galaxy g)
    {
        foreach (var f in g.Factions.Values) f.AI = null;
    }

    [Fact]
    public void War_wears_a_faction_down_and_peace_lets_it_rest()
    {
        var g = Three();
        Still(g);
        var f = g.Factions[Klaed];
        var income = IncomeOf(g, Klaed);
        f.Weariness = 50;
        Assert.Equal(income * (1 - WearyCost / 2), IncomeOf(g, Klaed), 6);
        // Far from everyone, at war with nobody it faces: it rests fast.
        RunGalaxy(g, 600);
        Expect.Less(f.Weariness, 50 - WearyCalm * 10 + 1);
        f.Weariness = 0;
        // A planet lost to another faction wears it down; its capital more.
        var planet = g.Planets.First(p => p.Owner == Independent);
        planet.Owner = Klaed;
        var capture = typeof(Galaxies).GetMethod("Capture", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        capture.Invoke(null, [g, planet, Nairan]);
        Assert.Equal(WearyPlanet, f.Weariness, 6);
        capture.Invoke(null, [g, Capital(g, Klaed), Nairan]);
        Assert.Equal(WearyPlanet + WearyCapital, f.Weariness, 6);
    }

    [Fact]
    public void A_bloody_battle_between_factions_wears_both_but_never_one_against_independents()
    {
        var g = Three();
        Still(g);
        g.Player = Faction.Errant;
        var home = Capital(g, Klaed);
        var target = NextDoor(g, Klaed);
        var fleet = AddShipsAt(g, Klaed, home.Id, new ShipCounts { [ShipClass.Frigate] = 2 })!;
        SendFleet(g, fleet.Id, target.Id);
        RunGalaxy(g, 120);
        Assert.Contains(g.Events, e => e is BattleEnded b && b.Defender == Independent);
        Assert.Equal(0, g.Factions[Klaed].Weariness);
        // Against another faction, the losses count, a share of what each could bring.
        var toll = new BattleToll(new BattleLosses(new ShipCounts { [ShipClass.Battlecruiser] = 5 }, []), new BattleLosses([], []), null, null);
        typeof(Galaxies).GetMethod("WearBattle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, [g, Klaed, Nairan, toll]);
        Expect.Greater(g.Factions[Klaed].Weariness, 5);
        Expect.Less(g.Factions[Klaed].Weariness, WearyBattle + 0.001);
        Assert.Equal(0, g.Factions[Nairan].Weariness);
    }

    [Fact]
    public void An_independent_planet_won_over_joins_with_its_garrison()
    {
        var g = Three();
        Still(g);
        var target = NextDoor(g, Klaed);
        var garrison = FleetsAt(g, target.Id, Independent)[0];
        var ships = Battle.CountShips(garrison.Ships);
        var price = CourtPrice(g, Klaed, target);
        g.Factions[Klaed].Credits = price - 1;
        Assert.False(CanCourt(g, Klaed, target.Id).Ok);
        g.Factions[Klaed].Credits = price;
        Assert.True(Court(g, Klaed, target.Id).Ok);
        Assert.Equal(0, g.Factions[Klaed].Credits);
        Assert.False(CanCourt(g, Klaed, g.Planets.First(p => p.Owner == Independent && p.Id != target.Id).Id).Ok);
        RunGalaxy(g, CourtTime - 1);
        Assert.Equal(Independent, target.Owner);
        RunGalaxy(g, 2);
        Assert.Equal(Klaed, target.Owner);
        Assert.Equal(ships, Battle.CountShips(FleetsAt(g, target.Id, Klaed)[0].Ships));
        Assert.True(target.Platforms >= 1);
        Assert.Contains(g.Events, e => e is Joined j && j.Side == Klaed && j.PlanetId == target.Id);
    }

    [Fact]
    public void A_good_word_lowers_the_price_and_attacking_the_planet_ends_the_courtship()
    {
        var g = Three();
        Still(g);
        g.Player = Faction.Errant;
        var target = NextDoor(g, Klaed);
        var plain = CourtPrice(g, Klaed, target);
        g.Factions[Klaed].Reputation = 50;
        Expect.Less(CourtPrice(g, Klaed, target), plain);
        g.Factions[Klaed].Credits = 1e6;
        Assert.True(Court(g, Klaed, target.Id).Ok);
        var fleet = AddShipsAt(g, Klaed, Capital(g, Klaed).Id, new ShipCounts { [ShipClass.Frigate] = 1 })!;
        SendFleet(g, fleet.Id, target.Id);
        RunGalaxy(g, 60);
        Assert.Empty(g.Courtships);
    }

    [Fact]
    public void The_first_to_win_a_planet_over_takes_it_and_the_others_get_half_back()
    {
        var g = Three();
        Still(g);
        g.Player = Faction.Errant;
        var target = g.Planets.First(p => p.Owner == Independent);
        // Two factions next to it, as though they held planets there, their garrisons gone.
        var (a, b) = (Neighbours(g, target.Id)[0], Neighbours(g, target.Id).Last());
        foreach (var f in g.Fleets.Values.Where(f => f.At == a || f.At == b).ToList()) g.Fleets.Remove(f.Id);
        g.Planets[a].Owner = Klaed;
        g.Planets[b].Owner = Nairan;
        g.Factions[Klaed].Credits = g.Factions[Nairan].Credits = 1e5;
        Assert.True(Court(g, Klaed, target.Id).Ok);
        RunGalaxy(g, 10);
        Assert.True(Court(g, Nairan, target.Id).Ok);
        var paid = g.Courtships.First(c => c.Side == Nairan).Paid;
        var left = g.Factions[Nairan].Credits;
        RunGalaxy(g, CourtTime);
        Assert.Equal(Klaed, target.Owner);
        Assert.Empty(g.Courtships);
        // Half of what it paid back, on top of what its planets brought meanwhile.
        Expect.GreaterOrEqual(g.Factions[Nairan].Credits, left + paid / 2);
    }

    [Fact]
    public void A_lasting_peace_never_runs_out_and_is_only_denounced()
    {
        var g = Three();
        g.Factions[Nairan].AI = null;
        Assert.True(ProposeDeal(g, Klaed, Nairan, DealKind.Peace).Ok);
        Assert.True(AcceptDeal(g, Nairan, Klaed, DealKind.Peace).Ok);
        Assert.True(TruceOf(g, Klaed, Nairan) is { Lasting: true });
        RunGalaxy(g, TruceTime * 3);
        Assert.True(AtPeace(g, Klaed, Nairan));
        Assert.False(BreakTruce(g, Klaed, Nairan).Ok);
        var repute = g.Factions[Klaed].Reputation;
        Assert.True(EndDeal(g, Klaed, Nairan, DealKind.Peace).Ok);
        Assert.True(TruceOf(g, Klaed, Nairan) is { Lasting: false, Renewed: true } t && t.Remaining == NoticeTime);
        Expect.Less(g.Factions[Klaed].Reputation, repute);
        Assert.Contains(g.Events, e => e is PeaceDenounced d && d.By == Klaed);
        RunGalaxy(g, NoticeTime + 1);
        Assert.True(AtWar(g, Klaed, Nairan));
    }

    [Fact]
    public void A_lasting_peace_signed_in_the_opening_peace_outlives_it()
    {
        var g = Three(peace: 5);
        g.Factions[Nairan].AI = null;
        ProposeDeal(g, Klaed, Nairan, DealKind.Peace);
        AcceptDeal(g, Nairan, Klaed, DealKind.Peace);
        Assert.False(Denounce(g, Klaed, Nairan).Ok);
        RunGalaxy(g, 301);
        Assert.True(TruceOf(g, Klaed, Nairan) is { Lasting: true, Opening: false });
        Assert.Null(TruceOf(g, Klaed, Nautolan));
        Assert.True(Denounce(g, Klaed, Nairan).Ok);
    }

    [Fact]
    public void An_AI_makes_a_lasting_peace_with_a_friend_but_never_one_that_ends_all_its_wars()
    {
        var g = Three();
        g.Factions[Nairan].AI!.Trust[Klaed] = 60;
        Assert.True(ProposeDeal(g, Klaed, Nairan, DealKind.Peace).Ok);
        // At peace with the player already, the Nautolan would have nobody left to fight.
        g.Factions[Nautolan].AI!.Trust[Klaed] = 60;
        g.Factions[Nautolan].AI!.Trust[Nairan] = 60;
        Sign(g, Nautolan, Nairan);
        Assert.False(ProposeDeal(g, Klaed, Nautolan, DealKind.Peace).Ok);
    }

    static void Sign(Galaxy g, Side a, Side b) =>
        typeof(Galaxies).GetMethod("Sign", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, [g, a, b]);

    [Fact]
    public void An_AI_tired_of_war_takes_a_truce_even_from_the_weaker()
    {
        var g = Three();
        AddShipsAt(g, Nairan, Capital(g, Nairan).Id, new ShipCounts { [ShipClass.Titan] = 3 });
        Assert.False(GalaxyAi.AnswerTruce(g, Nairan, Klaed).Ok);
        g.Factions[Nairan].Weariness = 60;
        Assert.True(GalaxyAi.AnswerTruce(g, Nairan, Klaed).Ok);
    }

    [Fact]
    public void Trade_grows_with_the_peace_and_the_poorer_partners_worth()
    {
        // Past the end of the opening peace, the trade would go with it.
        var g = Three(peace: 15);
        Still(g);
        Assert.True(ProposeDeal(g, Klaed, Nairan, DealKind.Trade).Ok);
        Assert.True(AcceptDeal(g, Nairan, Klaed, DealKind.Trade).Ok);
        var young = TradeRate(g, Klaed, Nairan);
        RunGalaxy(g, TradeMaturity);
        var grown = TradeRate(g, Klaed, Nairan);
        Assert.Equal(young * 3, grown, 6);
        foreach (var p in g.Planets.Where(p => p.Owner == Independent).Take(4)) p.Owner = Klaed;
        foreach (var p in g.Planets.Where(p => p.Owner == Independent).Take(4)) p.Owner = Nairan;
        Expect.Greater(TradeRate(g, Klaed, Nairan), grown);
    }

    [Fact]
    public void A_planet_changes_hands_for_credits_and_an_AI_asks_its_price()
    {
        var g = Three(peace: 10);
        var theirs = g.Planets.First(p => p.Owner == Independent);
        theirs.Owner = Nairan;
        var mine = g.Planets.First(p => p.Owner == Independent);
        mine.Owner = Klaed;
        g.Factions[Klaed].Credits = g.Factions[Nairan].Credits = 1e5;
        // A capital never changes hands.
        Assert.False(CanPropose(g, Klaed, Nairan, DealKind.Planet, 0, planet: Capital(g, Nairan).Id).Ok);
        // Too little: the AI says what it wants.
        var low = ProposeDeal(g, Klaed, Nairan, DealKind.Planet, 50, planet: theirs.Id);
        Assert.False(low.Ok);
        Assert.Contains("moins de", low.Reason);
        Assert.True(ProposeDeal(g, Klaed, Nairan, DealKind.Planet, 1e4, planet: theirs.Id).Ok);
        Assert.Equal(Klaed, theirs.Owner);
        Assert.Equal(1e5 - 1e4, g.Factions[Klaed].Credits, 0);
        // A gift: always welcome, and it warms the AI.
        var trust = TrustOf(g, Nairan, Klaed);
        Assert.True(ProposeDeal(g, Klaed, Nairan, DealKind.Planet, 0, planet: mine.Id).Ok);
        Assert.Equal(Nairan, mine.Owner);
        Expect.Greater(TrustOf(g, Nairan, Klaed), trust);
        Assert.Contains(g.Events, e => e is Concluded { Kind: DealKind.Planet });
    }

    [Fact]
    public void A_tech_level_passes_from_the_one_ahead_and_an_AI_will_not_arm_the_stronger()
    {
        var g = Three(peace: 10);
        g.Factions[Klaed].Credits = 1e5;
        g.Factions[Nairan].Research[TechId.Shields] = 3;
        Assert.False(CanPropose(g, Klaed, Nairan, DealKind.Tech, 0, tech: TechId.Weapons).Ok);
        g.Factions[Nairan].AI!.Trust[Klaed] = 50;
        Assert.True(ProposeDeal(g, Klaed, Nairan, DealKind.Tech, 5000, tech: TechId.Shields).Ok);
        Assert.Equal(1, g.Factions[Klaed].Research[TechId.Shields]);
        // Stronger than the AI, without dominating the galaxy (the Nautolan as strong): it would not arm it.
        AddShipsAt(g, Klaed, Capital(g, Klaed).Id, new ShipCounts { [ShipClass.Titan] = 4 });
        AddShipsAt(g, Nautolan, Capital(g, Nautolan).Id, new ShipCounts { [ShipClass.Titan] = 4 });
        var r = ProposeDeal(g, Klaed, Nairan, DealKind.Tech, 5000, tech: TechId.Shields);
        Assert.False(r.Ok);
        Assert.Equal(GalaxyAi.Reason(DealWhy.Arming), r.Reason);
        // Nothing while the level is being researched: it would be paid for twice.
        ResearchQueue.Queue(g.Factions[Klaed], TechId.Shields);
        Assert.False(CanPropose(g, Klaed, Nairan, DealKind.Tech, 0, tech: TechId.Shields).Ok);
    }

    [Fact]
    public void Snapshots_and_saves_carry_peace_weariness_courtships_and_known_research()
    {
        var g = Three(peace: 10);
        g.Factions[Nairan].AI = null;
        ProposeDeal(g, Klaed, Nairan, DealKind.Peace);
        AcceptDeal(g, Nairan, Klaed, DealKind.Peace);
        ProposeDeal(g, Klaed, Nairan, DealKind.Trade);
        AcceptDeal(g, Nairan, Klaed, DealKind.Trade);
        g.Factions[Nairan].Weariness = 37;
        g.Factions[Nairan].Research[TechId.Armor] = 2;
        g.Factions[Klaed].Credits = 1e5;
        Court(g, Klaed, NextDoor(g, Klaed).Id);
        RunGalaxy(g, 30);
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms)) GalaxySnapshot.Write(g, Klaed, w, null, null, default);
        var replica = new Galaxy();
        GalaxySnapshot.ReadChart(new BinaryReader(new MemoryStream(GalaxySnapshot.Chart(g))), replica);
        Assert.NotNull(GalaxySnapshot.Read(new BinaryReader(new MemoryStream(ms.ToArray())), replica));
        Assert.True(TruceOf(replica, Klaed, Nairan) is { Lasting: true, Opening: true });
        Assert.Equal(g.Factions[Nairan].Weariness, replica.Factions[Nairan].Weariness, 3);
        Assert.Equal(2, replica.Factions[Nairan].Research[TechId.Armor]);
        Assert.Equal(g.Courtships.Count, replica.Courtships.Count);
        Assert.Contains(replica.Courtships, c => c.Side == Klaed);
        Assert.Equal(TradeRate(g, Klaed, Nairan), TradeRate(replica, Klaed, Nairan), 3);

        var back = GalaxySave.Deserialize(GalaxySave.Serialize(g))!;
        Assert.True(TruceOf(back, Klaed, Nairan) is { Lasting: true });
        Assert.Equal(g.Factions[Nairan].Weariness, back.Factions[Nairan].Weariness, 6);
        Assert.Equal(g.Courtships.Count, back.Courtships.Count);
        Assert.Equal(g.Pacts[0].Since, back.Pacts[0].Since);
    }

    [Fact]
    public void The_new_orders_cross_the_wire()
    {
        foreach (var c in new GalaxyCommand[] { new GalaxyCommand.Court(12), new GalaxyCommand.ProposeAccord(Nairan, DealKind.Tech, 300, Tech: TechId.Energy),
            new GalaxyCommand.ProposeAccord(Nairan, DealKind.Planet, 0, Planet: 3) })
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms)) Commands.Write(w, c);
            Assert.Equal(c, Commands.ReadGalaxyCommand(new BinaryReader(new MemoryStream(ms.ToArray()))));
        }
    }

    [Fact]
    public void AIs_win_independents_over_and_still_end_the_campaign()
    {
        var layout = GalaxyGen.GenerateLayout(2, 3, MapSize.Medium);
        var g = CreateGalaxy([new Slot(Klaed, Faction.Klaed, 0, Difficulty.Normal, null, Personality.Economic), new Slot(Nairan, Faction.Nairan, 0, Difficulty.Normal),
            new Slot(Nautolan, Faction.Nautolan, 0, Difficulty.Normal, null, Personality.Defensive)], 2, layout, setup: CampaignSetup.Of(10));
        g.Player = (Faction)99;
        var joined = 0;
        for (var i = 0; i < 100 * 600 && g.Winner is null; i++)
        {
            StepGalaxy(g);
            joined += g.Events.Count(e => e is Joined);
            g.Events.Clear();
        }
        Expect.Greater(joined, 0);
        Assert.NotNull(g.Winner);
    }
}
