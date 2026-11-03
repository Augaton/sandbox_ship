// Truces (not in the web version): sides at peace do not fight, at a planet or on a lane, nor
// send fleets to each other's planets. AIs accept them when weaker or busy with a third faction,
// offer them, and bear a grudge against whoever breaks one.
using System.Text.Json.Nodes;
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.Galaxies;
using static SandboxShip.Sim.Tests.SupplyTests;

namespace SandboxShip.Sim.Tests;

public class DiplomacyTests
{
    static void Sign(Galaxy g, double remaining = TruceTime) =>
        g.Truces.Add(new Truce { A = Faction.Klaed, B = Faction.Nairan, Remaining = remaining });

    /// <summary>The classic galaxy with Nairan on the AI, much weaker than the Kla'ed player.</summary>
    static Galaxy WeakAi()
    {
        var g = CreateGalaxy(Faction.Klaed, [Faction.Nairan], true, errants: false, incidents: false);
        AddShipsAt(g, Faction.Klaed, Id("Vesper"), new ShipCounts { [ShipClass.Battlecruiser] = 8 });
        return g;
    }

    [Fact]
    public void Sides_at_peace_do_not_fight_over_a_planet_until_the_truce_runs_out()
    {
        var g = Quiet();
        var kaldra = g.Planets[Id("Kaldra")];
        kaldra.Owner = Faction.Nairan;
        kaldra.Station = 1;
        Sign(g, 30);
        // A fleet there from before the truce.
        AddShipsAt(g, Faction.Klaed, kaldra.Id, new ShipCounts { [ShipClass.Frigate] = 3 });
        RunGalaxy(g, 25);
        Assert.Empty(g.Battles);
        Assert.Equal(Faction.Nairan, kaldra.Owner);
        Assert.Null(kaldra.Occupation);
        RunGalaxy(g, 6);
        Assert.Contains(g.Events, e => e is TruceEnded { BrokenBy: null });
        Assert.Empty(g.Truces);
        Assert.True(BattleAt(g, kaldra.Id) || g.Events.Exists(e => e is BattleEnded b && b.PlanetId == kaldra.Id));
    }

    [Fact]
    public void Sides_at_peace_pass_each_other_on_a_lane()
    {
        var g = Quiet();
        var (orin, kaldra) = (Id("Orin"), Id("Kaldra"));
        var klaed = AddShipsAt(g, Faction.Klaed, orin, new ShipCounts { [ShipClass.Frigate] = 2 })!;
        var nairan = AddShipsAt(g, Faction.Nairan, kaldra, new ShipCounts { [ShipClass.Frigate] = 2 })!;
        Sign(g);
        Assert.True(SendFleet(g, klaed.Id, kaldra).Ok);
        Assert.True(SendFleet(g, nairan.Id, orin).Ok);
        RunGalaxy(g, 20);
        Assert.Empty(g.Battles);
        Assert.DoesNotContain(g.Events, e => e is BattleStarted);
        Assert.Equal((kaldra, orin), (klaed.At, nairan.At));
    }

    [Fact]
    public void No_fleet_is_sent_to_a_truce_partner_s_planets_but_fleets_pass_through_them()
    {
        var g = Quiet();
        var (kaldra, nadir) = (Id("Kaldra"), Id("Nadir"));
        g.Planets[kaldra].Owner = Faction.Nairan;
        foreach (var id in new[] { Id("Orin"), Id("Maru") }) g.Planets[id].Owner = Faction.Klaed;
        Sign(g);
        var fleet = AddShipsAt(g, Faction.Klaed, Id("Maru"), new ShipCounts { [ShipClass.Frigate] = 2 })!;
        Assert.Equal(Result.Fail("Trêve en cours"), SendFleet(g, fleet.Id, kaldra));
        // From Maru, Nadir lies beyond Kaldra: the fleet goes through without stopping.
        Assert.Equal([kaldra, nadir], FindPath(g, Id("Maru"), nadir)!);
        Assert.True(SendFleet(g, fleet.Id, nadir).Ok);
        Assert.Equal(Result.Fail("Trêve en cours"), RedirectFleet(g, fleet.Id, kaldra));
        RunGalaxy(g, 40);
        Assert.Equal(nadir, fleet.At);
        Assert.Equal(Faction.Nairan, g.Planets[kaldra].Owner);
    }

    [Fact]
    public void On_a_neutral_planet_the_first_to_come_takes_it_while_its_partner_waits_by()
    {
        var g = Quiet();
        var thessa = g.Planets[Id("Thessa")];
        Sign(g);
        AddShipsAt(g, Faction.Klaed, thessa.Id, new ShipCounts { [ShipClass.Frigate] = 1 });
        RunGalaxy(g, 1);
        AddShipsAt(g, Faction.Nairan, thessa.Id, new ShipCounts { [ShipClass.Frigate] = 3 });
        RunGalaxy(g, 1);
        Assert.Equal(Faction.Klaed, thessa.Occupation?.Side);
        RunGalaxy(g, GalaxyRules.NeutralCaptureTime);
        Assert.Equal(Faction.Klaed, thessa.Owner);
        // Now a partner's planet: the Nairan fleet neither takes it nor fights for it.
        RunGalaxy(g, GalaxyRules.CaptureTime + 5);
        Assert.Equal(Faction.Klaed, thessa.Owner);
        Assert.Null(thessa.Occupation);
        Assert.Empty(g.Battles);
        Assert.Equal(2, FleetsAt(g, thessa.Id).Count);
    }

    [Fact]
    public void No_truce_with_the_Errants_with_oneself_or_with_a_faction_out_of_the_game()
    {
        var g = WeakAi();
        Assert.False(ProposeTruce(g, Faction.Klaed, Faction.Errant).Ok);
        Assert.False(ProposeTruce(g, Faction.Klaed, Faction.Klaed).Ok);
        g.Factions[Faction.Nairan].Eliminated = true;
        Assert.Equal(Result.Fail("Faction éliminée"), ProposeTruce(g, Faction.Klaed, Faction.Nairan));
    }

    [Fact]
    public void An_AI_accepts_a_truce_when_weaker_and_refuses_it_when_stronger()
    {
        var g = WeakAi();
        Assert.True(ProposeTruce(g, Faction.Klaed, Faction.Nairan).Ok);
        Assert.True(AtPeace(g, Faction.Nairan, Faction.Klaed));
        Assert.Contains(g.Events, e => e is TruceSigned);
        Assert.Equal(Result.Fail("Trêve déjà en cours"), ProposeTruce(g, Faction.Klaed, Faction.Nairan));
        var strong = CreateGalaxy(Faction.Klaed, [Faction.Nairan], true, errants: false, incidents: false);
        AddShipsAt(strong, Faction.Nairan, Id("Ostra"), new ShipCounts { [ShipClass.Battlecruiser] = 8 });
        Assert.Equal(Result.Fail("Elle se croit la plus forte"), ProposeTruce(strong, Faction.Klaed, Faction.Nairan));
        Assert.Empty(strong.Truces);
    }

    [Fact]
    public void An_AI_busy_with_a_third_faction_accepts_even_when_stronger()
    {
        var layout = GalaxyGen.GenerateLayout(21, 3);
        var g = CreateGalaxy(Faction.Klaed, [Faction.Nairan, Faction.Nautolan], true, 21, layout, errants: false, incidents: false);
        var nairan = layout.Capitals[1];
        AddShipsAt(g, Faction.Nairan, nairan, new ShipCounts { [ShipClass.Battlecruiser] = 8 });
        // Nautolan out of the game: nothing keeps Nairan busy.
        g.Factions[Faction.Nautolan].Eliminated = true;
        Assert.False(ProposeTruce(g, Faction.Klaed, Faction.Nairan).Ok);
        // Back, and right at its door.
        g.Factions[Faction.Nautolan].Eliminated = false;
        g.Planets[Neighbours(g, nairan)[0]].Owner = Faction.Nautolan;
        Assert.True(ProposeTruce(g, Faction.Klaed, Faction.Nairan).Ok);
    }

    [Fact]
    public void Breaking_a_truce_angers_the_AI_which_refuses_truces_for_a_while()
    {
        var g = WeakAi();
        Assert.True(ProposeTruce(g, Faction.Klaed, Faction.Nairan).Ok);
        Assert.Equal(Result.Fail("Aucune trêve"), BreakTruce(g, Faction.Nairan, Faction.Nautolan));
        Assert.True(BreakTruce(g, Faction.Klaed, Faction.Nairan).Ok);
        Assert.Contains(g.Events, e => e is TruceEnded t && t.A == Faction.Klaed && t.B == Faction.Nairan && t.BrokenBy == Faction.Klaed);
        Assert.False(AtPeace(g, Faction.Klaed, Faction.Nairan));
        var ai = g.Factions[Faction.Nairan].AI!;
        Assert.Equal(GrudgeTime, ai.Grudges[Faction.Klaed]);
        Assert.Equal(Result.Fail("Elle n'a pas oublié la trêve rompue"), ProposeTruce(g, Faction.Klaed, Faction.Nairan));
        // The grudge fades with time.
        ai.Grudges[Faction.Klaed] = 1;
        RunGalaxy(g, 2);
        Assert.Empty(ai.Grudges);
        Assert.True(ProposeTruce(g, Faction.Klaed, Faction.Nairan).Ok);
    }

    [Fact]
    public void A_truce_lasts_ten_minutes_with_two_factions_a_ceasefire()
    {
        var g = WeakAi();
        Assert.True(ProposeTruce(g, Faction.Klaed, Faction.Nairan).Ok);
        Assert.Equal(TruceTime, TruceOf(g, Faction.Klaed, Faction.Nairan)!.Remaining);
        RunGalaxy(g, TruceTime - 1);
        Assert.True(AtPeace(g, Faction.Klaed, Faction.Nairan));
        Assert.DoesNotContain(g.Events, e => e is BattleStarted);
        RunGalaxy(g, 2);
        Assert.False(AtPeace(g, Faction.Klaed, Faction.Nairan));
    }

    /// <summary>
    /// The weak Nairan AI faces Kla'ed, on no AI, across Orin and Kaldra. The player watches from
    /// off the map: battles do not wait for them, while offers to Kla'ed wait for an answer.
    /// </summary>
    static Galaxy Facing()
    {
        var g = WeakAi();
        g.Player = Faction.Nautolan;
        g.Planets[Id("Orin")].Owner = Faction.Klaed;
        g.Planets[Id("Kaldra")].Owner = Faction.Nairan;
        return g;
    }

    static DealOffered RunToOffer(Galaxy g)
    {
        for (var t = 0; t < 30 && !g.Events.Exists(e => e is DealOffered); t++) RunGalaxy(g, 1);
        return Assert.Single(g.Events.OfType<DealOffered>());
    }

    [Fact]
    public void A_weaker_AI_offers_the_player_a_truce_they_may_accept()
    {
        var g = Facing();
        var offer = RunToOffer(g);
        Assert.Equal((Faction.Nairan, Faction.Klaed), (offer.From, offer.To));
        Assert.Single(g.Offers);
        Assert.True(AcceptTruce(g, Faction.Klaed, Faction.Nairan).Ok);
        Assert.Empty(g.Offers);
        Assert.True(AtPeace(g, Faction.Klaed, Faction.Nairan));
        Assert.Equal(Result.Fail("Aucune offre"), AcceptTruce(g, Faction.Klaed, Faction.Nairan));
    }

    [Fact]
    public void An_offer_refused_or_left_unanswered_lapses_and_the_AI_waits_before_the_next()
    {
        var g = Facing();
        RunToOffer(g);
        RefuseTruce(g, Faction.Klaed, Faction.Nairan);
        Assert.Empty(g.Offers);
        Assert.Empty(g.Truces);
        Expect.Greater(g.Factions[Faction.Nairan].AI!.OfferWait, OfferPause - 60);
        g.Events.Clear();
        g.Factions[Faction.Nairan].AI!.OfferWait = 0;
        RunToOffer(g);
        RunGalaxy(g, OfferTime + 1);
        Assert.Empty(g.Offers);
        Assert.Empty(g.Truces);
    }

    [Fact]
    public void Truces_offers_and_grudges_survive_a_save_and_older_saves_have_none()
    {
        var g = Facing();
        RunToOffer(g);
        g.Truces.Add(new Truce { A = Faction.Klaed, B = Faction.Nautolan, Remaining = 100 });
        g.Factions[Faction.Nairan].AI!.Grudges[Faction.Klaed] = 50;
        var back = GalaxySave.Deserialize(GalaxySave.Serialize(g))!;
        Assert.Equal(GalaxySave.Serialize(g, 0), GalaxySave.Serialize(back, 0));
        Assert.Equal(100, TruceOf(back, Faction.Nautolan, Faction.Klaed)!.Remaining);
        Assert.Equal(Faction.Nairan, Assert.Single(back.Offers).From);
        Assert.Equal(50, back.Factions[Faction.Nairan].AI!.Grudges[Faction.Klaed]);
        var old = JsonNode.Parse(GalaxySave.Serialize(g))!.AsObject();
        Assert.True(old.Remove("truces") && old.Remove("offers"));
        foreach (var f in old["factions"]!.AsArray())
        {
            if (f!["ai"] is JsonObject ai) ai.Remove("grudges");
        }
        var loaded = GalaxySave.Deserialize(old.ToJsonString())!;
        Assert.Empty(loaded.Truces);
        Assert.Empty(loaded.Offers);
        Assert.Empty(loaded.Factions[Faction.Nairan].AI!.Grudges);
    }
}
