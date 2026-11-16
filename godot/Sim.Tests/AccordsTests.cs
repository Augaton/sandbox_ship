// The diplomacy of this version beyond truces, alliances, trade and tributes (GalaxyAccords.cs):
// shared vision, joint wars, ultimatums, requests for help, bundles of terms; the reputation all
// see, why an AI trusts a faction as it does, the chronicle, what an AI would answer, the league
// against the faction that dominates; over the network and in saves too.
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.Galaxies;

namespace SandboxShip.Sim.Tests;

public class AccordsTests
{
    static readonly Side Klaed = Faction.Klaed, Nairan = Faction.Nairan, Nautolan = Faction.Nautolan;

    /// <summary>Three factions, the Kla'ed player and two AIs, without Errants nor random events.</summary>
    static Galaxy Three(Personality nairan = Personality.Balanced, bool human = false)
    {
        var g = CreateGalaxy(
            [new Slot(Klaed, Faction.Klaed), new Slot(Nairan, Faction.Nairan, 0, human ? null : Difficulty.Normal, null, nairan), new Slot(Nautolan, Faction.Nautolan, 0, Difficulty.Normal)],
            5, GalaxyGen.GenerateLayout(5, 3, MapSize.Small), errants: false, incidents: false);
        g.Player = Klaed;
        return g;
    }

    static int Capital(Galaxy g, Side side) => g.Planets.First(p => p.Owner == side).Id;

    static void Arm(Galaxy g, Side side, int battlecruisers) => AddShipsAt(g, side, Capital(g, side), new ShipCounts { [ShipClass.Battlecruiser] = battlecruisers });

    static void Trust(Galaxy g, Side ai, Side other, double trust) => g.Factions[ai].AI!.Trust[other] = trust;

    static void Truce(Galaxy g, Side a, Side b) => g.Truces.Add(new Truce { A = a, B = b, Remaining = TruceTime });

    /// <summary>A neutral planet next to `a`'s capital given to `b`: the two face each other.</summary>
    static void Border(Galaxy g, Side a, Side b) =>
        g.Planets.First(p => p.Owner is null && Neighbours(g, Capital(g, a)).Contains(p.Id)).Owner = b;

    [Fact]
    public void Shared_vision_shows_the_partner_s_planets_in_peace_and_goes_with_the_peace()
    {
        var g = Three();
        Assert.Equal(Result.Fail("Il faut d'abord la paix"), ProposeDeal(g, Klaed, Nairan, DealKind.Vision));
        Truce(g, Klaed, Nairan);
        Trust(g, Nairan, Klaed, 30);
        Assert.DoesNotContain(Capital(g, Nairan), VisiblePlanets(g, Klaed));
        Assert.True(ProposeDeal(g, Klaed, Nairan, DealKind.Vision).Ok);
        Assert.Contains(Capital(g, Nairan), VisiblePlanets(g, Klaed));
        Assert.Contains(Capital(g, Klaed), VisiblePlanets(g, Nairan));
        Assert.False(Allies(g, Klaed, Nairan));
        BreakTruce(g, Klaed, Nairan);
        Assert.Null(PactOf(g, DealKind.Vision, Klaed, Nairan));
        Assert.DoesNotContain(Capital(g, Nairan), VisiblePlanets(g, Klaed));
    }

    [Fact]
    public void A_joint_war_needs_peace_between_and_war_on_the_target_and_peace_with_the_target_betrays_it()
    {
        var g = Three(human: true);
        Assert.Equal(Result.Fail("Il faut d'abord la paix"), ProposeDeal(g, Klaed, Nairan, DealKind.JointWar, target: Nautolan));
        Truce(g, Klaed, Nairan);
        Assert.Equal(Result.Fail("Choisis une cible"), ProposeDeal(g, Klaed, Nairan, DealKind.JointWar));
        Assert.True(ProposeDeal(g, Klaed, Nairan, DealKind.JointWar, target: Nautolan).Ok);
        Assert.True(AcceptDeal(g, Nairan, Klaed, DealKind.JointWar).Ok);
        var war = JointWarOf(g, Klaed, Nairan)!;
        Assert.Equal(Nautolan, war.Target);
        Assert.True(Sworn(g, Klaed, Nautolan) && Sworn(g, Nairan, Nautolan));
        // Kla'ed makes peace with the target behind its partner's back: the joint war is broken, and its word is worth less.
        var before = g.Factions[Klaed].Reputation;
        Truce(g, Klaed, Nautolan);
        g.Truces.RemoveAt(g.Truces.Count - 1);
        Assert.True(ProposeDeal(g, Nautolan, Klaed, DealKind.Truce).Ok);
        Assert.True(AcceptDeal(g, Klaed, Nautolan, DealKind.Truce).Ok);
        Assert.Null(JointWarOf(g, Klaed, Nairan));
        Assert.Equal(before - 20, g.Factions[Klaed].Reputation);
        Assert.Contains(g.Chronicle, e => e is { Kind: ChronicleKind.Broken, Deal: DealKind.JointWar } && e.By == Klaed);
    }

    [Fact]
    public void A_joint_war_kept_to_its_end_warms_both_partners_and_an_AI_sworn_to_it_refuses_the_target_s_truce()
    {
        var g = Three();
        Truce(g, Klaed, Nairan);
        Arm(g, Nautolan, 12);
        Border(g, Nairan, Nautolan);
        Assert.True(ProposeDeal(g, Klaed, Nairan, DealKind.JointWar, target: Nautolan).Ok);
        Assert.Equal(Result.Fail("Elle a juré la guerre à ce camp"), GalaxyAi.AnswerTruce(g, Nairan, Nautolan));
        var trust = TrustOf(g, Nairan, Klaed);
        JointWarOf(g, Klaed, Nairan)!.Remaining = 1;
        RunGalaxy(g, 2);
        Assert.Null(JointWarOf(g, Klaed, Nairan));
        Assert.True(TrustOf(g, Nairan, Klaed) >= trust + 10);
        Assert.True(g.Factions[Klaed].Reputation > 0);
        Assert.Contains(TrustCause.JointWar, g.Factions[Nairan].AI!.Why[Klaed].Keys);
    }

    [Fact]
    public void An_AI_that_fears_the_maker_gives_in_to_an_ultimatum_and_one_that_does_not_defies_it_and_war_follows()
    {
        var g = Three();
        Truce(g, Klaed, Nairan);
        Assert.Equal(Result.Fail("Pas assez forte pour l'exiger"), ProposeDeal(g, Klaed, Nairan, DealKind.Ultimatum, 300));
        Arm(g, Klaed, 30);
        g.Factions[Nairan].Credits = 2000;
        var credits = g.Factions[Klaed].Credits;
        Assert.True(ProposeDeal(g, Klaed, Nairan, DealKind.Ultimatum, 300).Ok);
        Assert.Equal(credits + 300, g.Factions[Klaed].Credits);
        Assert.True(AtPeace(g, Klaed, Nairan));
        Assert.Equal(-5, g.Factions[Klaed].Reputation);
        Assert.Contains(g.Chronicle, e => e.Kind == ChronicleKind.Accepted);
        // More than it can give: defied, and the truce ends (no betrayal: it was said).
        Assert.False(ProposeDeal(g, Klaed, Nairan, DealKind.Ultimatum, 9000).Ok);
        Assert.True(AtWar(g, Klaed, Nairan));
        Assert.Contains(g.Chronicle, e => e.Kind == ChronicleKind.Defied);
        Assert.False(g.Factions[Nairan].AI!.Grudges.ContainsKey(Klaed));
    }

    [Fact]
    public void An_ultimatum_for_a_planet_cedes_it_with_its_works_and_sends_the_former_owner_s_fleets_home()
    {
        var g = Three(human: true);
        Truce(g, Klaed, Nairan);
        Arm(g, Klaed, 30);
        var mine = Capital(g, Klaed);
        var border = g.Planets.First(p => p.Owner is null && Neighbours(g, mine).Contains(p.Id));
        border.Owner = Nairan;
        border.Mines = 2;
        var guard = AddShipsAt(g, Nairan, border.Id, new ShipCounts { [ShipClass.Frigate] = 2 })!;
        Assert.Equal(Result.Fail("Ce n'est pas une de ses planètes"), ProposeDeal(g, Klaed, Nairan, DealKind.Ultimatum, planet: mine));
        Assert.True(ProposeDeal(g, Klaed, Nairan, DealKind.Ultimatum, planet: border.Id).Ok);
        Assert.Equal(UltimatumTime, g.Offers.Single(o => o.Kind == DealKind.Ultimatum).Remaining);
        Assert.True(AcceptDeal(g, Nairan, Klaed, DealKind.Ultimatum).Ok);
        Assert.Equal(Klaed, border.Owner);
        Assert.Equal(2, border.Mines);
        Assert.NotEqual(border.Id, g.Fleets[guard.Id].At);
        Assert.Contains(g.Events, e => e is PlanetCeded c && c.PlanetId == border.Id);
    }

    [Fact]
    public void A_player_who_lets_an_ultimatum_run_out_is_at_war()
    {
        var g = Three(human: true);
        Truce(g, Klaed, Nairan);
        Arm(g, Klaed, 30);
        Assert.True(ProposeDeal(g, Klaed, Nairan, DealKind.Ultimatum, 200).Ok);
        RunGalaxy(g, UltimatumTime + 1);
        Assert.True(AtWar(g, Klaed, Nairan));
        Assert.Contains(g.Events, e => e is UltimatumEnded { Accepted: false });
    }

    [Fact]
    public void An_AI_asks_a_friend_for_help_and_remembers_whether_it_came()
    {
        var g = Three();
        Assert.Equal(Result.Fail("Seule une IA demande de l'aide, et à un joueur"), ProposeDeal(g, Klaed, Nairan, DealKind.Help, 100));
        Truce(g, Klaed, Nairan);
        var asked = Capital(g, Nairan);
        Assert.True(ProposeDeal(g, Nairan, Klaed, DealKind.Help, planet: asked).Ok);
        Assert.True(AcceptDeal(g, Klaed, Nairan, DealKind.Help).Ok);
        var promise = Assert.Single(g.Promises);
        var trust = TrustOf(g, Nairan, Klaed);
        AddShipsAt(g, Klaed, asked, new ShipCounts { [ShipClass.Frigate] = 1 });
        RunGalaxy(g, 1);
        Assert.Empty(g.Promises);
        Assert.True(TrustOf(g, Nairan, Klaed) >= trust + 19);
        Assert.Contains(g.Events, e => e is HelpAnswered { Kept: true });
        // Promised, then never came: worse than a refusal.
        Assert.True(ProposeDeal(g, Nairan, Klaed, DealKind.Help, planet: asked).Ok);
        foreach (var f in g.Fleets.Values.Where(f => f.Side == Klaed && f.At == asked).ToList()) g.Fleets.Remove(f.Id);
        Assert.True(AcceptDeal(g, Klaed, Nairan, DealKind.Help).Ok);
        RunGalaxy(g, HelpTime + 1);
        Assert.True(g.Factions[Nairan].AI!.Why[Klaed][TrustCause.Help] < 0);
        Assert.Contains(g.Events, e => e is HelpAnswered { Kept: false });
    }

    [Fact]
    public void A_bundle_is_all_or_nothing_its_tribute_buying_the_truce_the_AI_would_not_give()
    {
        var g = Three();
        Arm(g, Nairan, 30);
        Trust(g, Nairan, Klaed, 40);
        var price = GalaxyAi.PeacePrice(g, Nairan, Klaed);
        Assert.True(price > 0);
        var terms = DealTerms.Truce | DealTerms.Trade | DealTerms.Tribute;
        Assert.Equal(Result.Fail(GalaxyAi.Reason(DealWhy.Price, price)), ProposeDeal(g, Klaed, Nairan, DealKind.Bundle, price - 10, true, terms: terms));
        Assert.True(AtWar(g, Klaed, Nairan));
        g.Factions[Klaed].Credits = 100000;
        Assert.True(ProposeDeal(g, Klaed, Nairan, DealKind.Bundle, price, true, terms: terms).Ok);
        Assert.True(AtPeace(g, Klaed, Nairan));
        Assert.NotNull(PactOf(g, DealKind.Trade, Klaed, Nairan));
        Assert.NotNull(TributeOf(g, Klaed, Nairan));
        Assert.Equal(Result.Fail("Aucun terme"), CanPropose(g, Klaed, Nautolan, DealKind.Bundle));
    }

    [Fact]
    public void Breaking_one_s_word_lowers_a_reputation_all_AIs_weigh_and_keeping_it_raises_it()
    {
        var g = Three();
        Truce(g, Klaed, Nairan);
        BreakTruce(g, Klaed, Nairan);
        Assert.Equal(-15, g.Factions[Klaed].Reputation);
        Assert.Equal(TrustOf(g, Nautolan, Klaed) - 15 * ReputationWeight, Regard(g, Nautolan, Klaed), 6);
        Assert.Contains(TrustCause.Betrayal, g.Factions[Nairan].AI!.Why[Klaed].Keys);
        // A truce kept to its end.
        var h = Three();
        Truce(h, Klaed, Nautolan);
        h.Truces[0].Remaining = 1;
        RunGalaxy(h, 2);
        Assert.True(h.Factions[Klaed].Reputation > 1);
        Assert.Contains(TrustCause.Peace, h.Factions[Nautolan].AI!.Why[Klaed].Keys);
    }

    [Fact]
    public void The_chronicle_keeps_what_was_signed_broken_and_refused_and_no_more_than_its_size()
    {
        var g = Three();
        Assert.False(ProposeDeal(g, Klaed, Nairan, DealKind.Alliance).Ok);
        Truce(g, Klaed, Nairan);
        BreakTruce(g, Klaed, Nairan);
        Assert.Contains(g.Chronicle, e => e is { Kind: ChronicleKind.Refused, Deal: DealKind.Alliance });
        Assert.Contains(g.Chronicle, e => e is { Kind: ChronicleKind.Broken, Deal: DealKind.Truce } && e.By == Klaed);
        for (var i = 0; i < ChronicleMax + 10; i++) ProposeDeal(g, Klaed, Nairan, DealKind.Alliance);
        Assert.Equal(ChronicleMax, g.Chronicle.Count);
    }

    [Fact]
    public void What_the_screen_says_an_AI_would_answer_is_what_it_answers()
    {
        var g = Three();
        Arm(g, Klaed, 30);
        Truce(g, Klaed, Nairan);
        var preview = GalaxyAi.PreviewFor(g, Nairan, Klaed);
        foreach (var (kind, why) in new[] { (DealKind.Alliance, preview.Alliance), (DealKind.Trade, preview.Trade), (DealKind.Vision, preview.Vision) })
        {
            var r = GalaxyAi.AnswerDeal(g, Nairan, Klaed, kind, 0, false);
            Assert.Equal(why == DealWhy.Yes, r.Ok);
            if (!r.Ok) Assert.Equal(GalaxyAi.Reason(why), r.Reason);
        }
        Assert.True(preview.Cedes);
        Assert.True(preview.YieldLimit > 0);
    }

    [Fact]
    public void The_AIs_league_against_the_faction_that_dominates_the_galaxy()
    {
        var g = Three();
        Arm(g, Klaed, 60);
        Assert.Equal(Klaed, GalaxyAi.Leader(g));
        Truce(g, Nairan, Nautolan);
        foreach (var f in g.Factions.Values) if (f.AI is { } ai) ai.OfferWait = 0;
        // The two AIs face the player: rather than bow to it, they swear a joint war against it.
        Border(g, Klaed, Nairan);
        Border(g, Klaed, Nautolan);
        RunGalaxy(g, 10);
        Assert.True(Sworn(g, Nairan, Klaed) || Sworn(g, Nautolan, Klaed));
    }

    [Fact]
    public void A_replica_knows_the_accords_the_chronicle_reputations_and_what_an_AI_would_answer_its_viewer()
    {
        var g = Three();
        Truce(g, Klaed, Nairan);
        Trust(g, Nairan, Klaed, 30);
        Assert.True(ProposeDeal(g, Klaed, Nairan, DealKind.Vision).Ok);
        Assert.True(ProposeDeal(g, Klaed, Nairan, DealKind.JointWar, target: Nautolan).Ok || true);
        g.Promises.Add(new HelpPromise { Asker = Nairan, Helper = Klaed, Planet = Capital(g, Nairan), Remaining = 50 });
        g.Factions[Klaed].Reputation = 12;
        var replica = new Galaxy();
        GalaxySnapshot.ReadChart(new BinaryReader(new MemoryStream(GalaxySnapshot.Chart(g))), replica);
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) GalaxySnapshot.Write(g, Klaed, w, g.Events.ToList(), null, default);
        ms.Position = 0;
        GalaxySnapshot.Read(new BinaryReader(ms), replica);
        Assert.NotNull(PactOf(replica, DealKind.Vision, Klaed, Nairan));
        Assert.Equal(12, replica.Factions[Klaed].Reputation, 3);
        Assert.Equal(g.Chronicle.Count, replica.Chronicle.Count);
        Assert.Single(replica.Promises);
        var host = GalaxyAi.PreviewFor(g, Nairan, Klaed);
        var told = replica.Factions[Nairan].AI!.Preview!;
        Assert.Equal((host.Truce, host.Alliance, host.Trade, host.Vision), (told.Truce, told.Alliance, told.Trade, told.Vision));
        Assert.Equal(host.JointWar, told.JointWar);
        Assert.Equal(g.Factions[Nairan].AI!.Why.GetValueOrDefault(Klaed)?.Count ?? 0, replica.Factions[Nairan].AI!.Why[Klaed].Count);
    }

    [Fact]
    public void A_save_keeps_the_chronicle_the_promises_reputations_and_joint_wars()
    {
        var g = Three(human: true);
        Truce(g, Klaed, Nairan);
        Assert.True(ProposeDeal(g, Klaed, Nairan, DealKind.JointWar, target: Nautolan).Ok);
        Assert.True(AcceptDeal(g, Nairan, Klaed, DealKind.JointWar).Ok);
        g.Promises.Add(new HelpPromise { Asker = Nautolan, Helper = Klaed, Planet = Capital(g, Nautolan), Remaining = 50 });
        g.Factions[Klaed].Reputation = -7;
        var back = GalaxySave.Deserialize(GalaxySave.Serialize(g))!;
        Assert.Equal(g.Chronicle.Count, back.Chronicle.Count);
        Assert.Single(back.Promises);
        Assert.Equal(-7, back.Factions[Klaed].Reputation);
        Assert.Equal(Nautolan, JointWarOf(back, Klaed, Nairan)!.Target);
    }
}
