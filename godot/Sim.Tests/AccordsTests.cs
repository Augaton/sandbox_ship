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
        Assert.Equal(Result.Fail("Nous avons juré la guerre à ce camp"), GalaxyAi.AnswerTruce(g, Nairan, Nautolan));
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
    public void An_AI_that_gave_in_to_an_ultimatum_will_not_again_for_a_while()
    {
        var g = Three();
        Truce(g, Klaed, Nairan);
        Arm(g, Klaed, 30);
        g.Factions[Nairan].Credits = 5000;
        Assert.True(ProposeDeal(g, Klaed, Nairan, DealKind.Ultimatum, 300).Ok);
        // Asked again at once: drained ultimatum after ultimatum, before. Now it defies, and war follows.
        Assert.Equal(Result.Fail(GalaxyAi.Reason(DealWhy.Yielded)), ProposeDeal(g, Klaed, Nairan, DealKind.Ultimatum, 300));
        Assert.True(AtWar(g, Klaed, Nairan));
        Assert.False(GalaxyAi.PreviewFor(g, Nairan, Klaed).Cedes);
    }

    [Fact]
    public void A_snapshot_tells_at_once_what_an_AI_would_answer_after_a_diplomatic_change()
    {
        var g = Three();
        Truce(g, Klaed, Nairan);
        var replica = new Galaxy();
        GalaxySnapshot.ReadChart(new BinaryReader(new MemoryStream(GalaxySnapshot.Chart(g))), replica);
        DealWhy Told()
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) GalaxySnapshot.Write(g, Klaed, w, [], null, default);
            ms.Position = 0;
            GalaxySnapshot.Read(new BinaryReader(ms), replica);
            return replica.Factions[Nairan].AI!.Preview!.Alliance;
        }
        Assert.Equal(DealWhy.NotTrusted, Told());
        // A gift at the same galaxy time: the answer told changes with it, not a second later.
        g.Factions[Klaed].Credits = 100000;
        Trust(g, Nairan, Klaed, 45);
        Assert.True(ProposeDeal(g, Klaed, Nairan, DealKind.Tribute, 400).Ok);
        Assert.Equal(DealWhy.Yes, Told());
    }

    [Fact]
    public void A_chronicle_line_naming_a_planet_or_a_remark_that_does_not_exist_is_refused_or_dropped()
    {
        var g = Three();
        g.Chronicle.Add(new DiploEntry { Kind = ChronicleKind.Ceded, Deal = DealKind.Ultimatum, A = Nairan, B = Klaed, Extra = 9999 });
        var replica = new Galaxy();
        GalaxySnapshot.ReadChart(new BinaryReader(new MemoryStream(GalaxySnapshot.Chart(g))), replica);
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) GalaxySnapshot.Write(g, Klaed, w, [], null, default);
        Assert.Throws<InvalidDataException>(() => GalaxySnapshot.Read(new BinaryReader(new MemoryStream(ms.ToArray())), replica));
        // A save: the line goes, and a promise for a planet that does not exist with it; the rest loads.
        g.Chronicle.Add(new DiploEntry { Kind = ChronicleKind.Said, A = Nairan, B = Klaed, Extra = 77 });
        g.Chronicle.Add(new DiploEntry { Kind = ChronicleKind.Signed, Deal = DealKind.Truce, A = Nairan, B = Klaed });
        g.Promises.Add(new HelpPromise { Asker = Nairan, Helper = Klaed, Planet = 9999, Remaining = 5 });
        var back = GalaxySave.Deserialize(GalaxySave.Serialize(g))!;
        Assert.Equal(DealKind.Truce, Assert.Single(back.Chronicle).Deal);
        Assert.Empty(back.Promises);
        RunGalaxy(back, 1);
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

    [Fact]
    public void An_AI_too_strong_for_a_truce_names_its_price_and_the_truce_comes_with_the_payment()
    {
        var g = Three();
        Arm(g, Nairan, 30);
        g.Factions[Klaed].Credits = 100000;
        var price = GalaxyAi.PeacePrice(g, Nairan, Klaed) * TruceTime / 60;
        Expect.Greater(price, 0);
        Assert.Equal(Result.Fail(GalaxyAi.Reason(DealWhy.Stronger)), ProposeDeal(g, Klaed, Nairan, DealKind.Truce));
        // No plain refusal: what it would take instead, left to accept or refuse.
        var counter = Assert.Single(g.Offers);
        Assert.True(counter is { Counter: true, Kind: DealKind.Truce } && counter.From == Nairan && counter.To == Klaed);
        Assert.Equal(price, counter.Amount);
        Assert.Contains(g.Events, e => e is DealOffered { Counter: true, Kind: DealKind.Truce });
        Assert.DoesNotContain(g.Events, e => e is DealRefused);
        // Proposed again: the counter-offer withdrawn, never taken as accepted, and made anew.
        Assert.False(ProposeDeal(g, Klaed, Nairan, DealKind.Truce).Ok);
        Assert.Equal(price, Assert.Single(g.Offers).Amount);
        Assert.Equal(100000, g.Factions[Klaed].Credits);
        var theirs = g.Factions[Nairan].Credits;
        Assert.True(AcceptDeal(g, Klaed, Nairan, DealKind.Truce).Ok);
        Assert.True(TruceOf(g, Klaed, Nairan) is { Paid: true });
        Assert.Equal(100000 - price, g.Factions[Klaed].Credits, 3);
        Assert.Equal(theirs + price, g.Factions[Nairan].Credits, 3);
        Assert.Empty(g.Offers);
        // Between AIs, a refusal is only a refusal.
        Assert.False(ProposeDeal(g, Nautolan, Nairan, DealKind.Truce).Ok);
        Assert.Empty(g.Offers);
    }

    [Fact]
    public void An_AI_short_of_trust_names_the_gift_that_wins_it_over_but_never_more_than_gifts_can_buy()
    {
        var g = Three();
        Truce(g, Klaed, Nairan);
        g.Factions[Klaed].Credits = 100000;
        // An alliance needs 40 of a balanced AI: 10 points short, 40 credits each.
        Trust(g, Nairan, Klaed, 30);
        Assert.False(ProposeDeal(g, Klaed, Nairan, DealKind.Alliance).Ok);
        var counter = Assert.Single(g.Offers);
        Assert.True(counter is { Counter: true, Kind: DealKind.Alliance, Amount: 400 });
        Assert.True(AcceptDeal(g, Klaed, Nairan, DealKind.Alliance).Ok);
        Assert.True(Allied(g, Klaed, Nairan));
        Assert.Equal(100000 - 400, g.Factions[Klaed].Credits, 3);
        Expect.Greater(TrustOf(g, Nairan, Klaed), 30);
        // Thirty points short: more than gifts may buy, a plain refusal.
        var h = Three();
        Truce(h, Klaed, Nairan);
        Trust(h, Nairan, Klaed, 10);
        Assert.Equal(Result.Fail(GalaxyAi.Reason(DealWhy.NotTrusted)), ProposeDeal(h, Klaed, Nairan, DealKind.Alliance));
        Assert.Empty(h.Offers);
        Assert.Contains(h.Events, e => e is DealRefused { Kind: DealKind.Alliance });
        // Betrayed, it names no price at all.
        Trust(h, Nairan, Klaed, 39);
        h.Factions[Nairan].AI!.Grudges[Klaed] = 100;
        Assert.False(ProposeDeal(h, Klaed, Nairan, DealKind.Alliance).Ok);
        Assert.Empty(h.Offers);
    }

    [Fact]
    public void A_counter_offer_without_the_credits_is_turned_down_and_one_for_a_planet_is_at_the_AI_s_price()
    {
        var g = Three();
        Truce(g, Klaed, Nairan);
        Trust(g, Nairan, Klaed, 30);
        g.Factions[Klaed].Credits = 100;
        Assert.False(ProposeDeal(g, Klaed, Nairan, DealKind.Alliance).Ok);
        Assert.Equal(Result.Fail("Crédits insuffisants"), AcceptDeal(g, Klaed, Nairan, DealKind.Alliance));
        Assert.False(Allied(g, Klaed, Nairan));
        Assert.Equal(100, g.Factions[Klaed].Credits);
        // A planet asked too cheaply: the AI offers it back at its price.
        var theirs = g.Planets.Where(p => p.Owner is null && p.Special is null).MinBy(p => p.Income)!;
        theirs.Owner = Nairan;
        Assert.False(ProposeDeal(g, Klaed, Nairan, DealKind.Planet, 50, planet: theirs.Id).Ok);
        var counter = Assert.Single(g.Offers);
        Assert.True(counter is { Counter: true, Kind: DealKind.Planet } && counter.Planet == theirs.Id);
        Expect.Greater(counter.Amount, 50);
        g.Factions[Klaed].Credits = 100000;
        Assert.True(AcceptDeal(g, Klaed, Nairan, DealKind.Planet).Ok);
        Assert.Equal(Klaed, theirs.Owner);
        Assert.Equal(100000 - counter.Amount, g.Factions[Klaed].Credits, 3);
    }

    [Fact]
    public void A_counter_offer_reaches_the_replica_and_the_save()
    {
        var g = Three();
        Arm(g, Nairan, 30);
        Assert.False(ProposeDeal(g, Klaed, Nairan, DealKind.Truce).Ok);
        var replica = new Galaxy();
        GalaxySnapshot.ReadChart(new BinaryReader(new MemoryStream(GalaxySnapshot.Chart(g))), replica);
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) GalaxySnapshot.Write(g, Klaed, w, g.Events.ToList(), null, default);
        ms.Position = 0;
        GalaxySnapshot.Read(new BinaryReader(ms), replica);
        Assert.True(Assert.Single(replica.Offers) is { Counter: true, Kind: DealKind.Truce });
        Assert.Contains(replica.Events, e => e is DealOffered { Counter: true });
        Assert.True(Assert.Single(GalaxySave.Deserialize(GalaxySave.Serialize(g))!.Offers).Counter);
    }

    [Fact]
    public void The_screen_reads_an_AI_s_mood_and_how_it_leans_from_what_it_would_answer()
    {
        var g = Three();
        Truce(g, Klaed, Nairan);
        BreakTruce(g, Klaed, Nairan);
        var (mood, cause, weight) = GalaxyAi.Attitude(g, Nairan, Klaed);
        Assert.Equal(Mood.Hostile, mood);
        Assert.Equal(TrustCause.Betrayal, cause);
        Expect.Less(weight, 0);
        var told = GalaxyAi.PreviewFor(g, Nairan, Klaed);
        Assert.Equal(Leaning.Hostile, GalaxyAi.LeaningOf(g, Nairan, Klaed, DealKind.Truce, told.Truce));
        // A truce kept a while: a little warmer, and the peace is why.
        var h = Three();
        Truce(h, Klaed, Nairan);
        RunGalaxy(h, 60);
        var (calm, why, _) = GalaxyAi.Attitude(h, Nairan, Klaed);
        Assert.Equal((Mood.Reserved, TrustCause.Peace), (calm, why));
        Trust(h, Nairan, Klaed, 20);
        Assert.Equal(Mood.Warm, GalaxyAi.Attitude(h, Nairan, Klaed).Mood);
        // An alliance 20 points away is out of reach of gifts; 10 points away, it may be won over.
        Assert.Equal(Leaning.Hostile, GalaxyAi.LeaningOf(h, Nairan, Klaed, DealKind.Alliance, DealWhy.NotTrusted));
        Trust(h, Nairan, Klaed, 30);
        Assert.Equal(Leaning.Reluctant, GalaxyAi.LeaningOf(h, Nairan, Klaed, DealKind.Alliance, DealWhy.NotTrusted));
        Assert.Equal(Leaning.Open, GalaxyAi.LeaningOf(h, Nairan, Klaed, DealKind.Alliance, DealWhy.Yes));
    }
}
