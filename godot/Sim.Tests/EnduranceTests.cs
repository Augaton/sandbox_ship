// A campaign that lasts (GalaxyEndurance.cs): free planets held by independents who fight for
// them, fortified capitals that raise their platforms again, the opening peace nobody may break,
// and the patient AI; in snapshots, saves and the lobby too.
using SandboxShip.Sim.Data;
using SandboxShip.Sim.Net;
using Xunit;
using static SandboxShip.Sim.Galaxies;

namespace SandboxShip.Sim.Tests;

public class EnduranceTests
{
    static readonly Side Klaed = Faction.Klaed, Nairan = Faction.Nairan, Nautolan = Faction.Nautolan;

    /// <summary>Three factions set up as a new campaign is, the Kla'ed player and two AIs, without Errants nor random events.</summary>
    static Galaxy Three(int peace = 10, MapSize size = MapSize.Small)
    {
        var g = CreateGalaxy(
            [new Slot(Klaed, Faction.Klaed), new Slot(Nairan, Faction.Nairan, 0, Difficulty.Normal), new Slot(Nautolan, Faction.Nautolan, 0, Difficulty.Normal)],
            5, GalaxyGen.GenerateLayout(5, 3, size), errants: false, incidents: false, setup: CampaignSetup.Of(peace));
        g.Player = Klaed;
        return g;
    }

    static Planet Capital(Galaxy g, Side side) => g.Planets.First(p => p.Home == side);

    [Fact]
    public void Free_planets_are_held_by_garrisons_stronger_far_from_the_capitals()
    {
        var g = Three(size: MapSize.Large);
        var free = g.Planets.Where(p => p.Home is null).ToList();
        Assert.All(free, p => Assert.Equal(Independent, p.Owner));
        Assert.All(free, p => Assert.True(p.Platforms >= 1 && FleetsAt(g, p.Id, Independent).Count == 1));
        Assert.False(IsFaction(g, Independent));
        // Next door to a capital, a starting fleet is enough for the poorest; far away, a war fleet is needed.
        var hops = Hops(g, g.Planets.Where(p => p.Home is not null).Select(p => p.Id));
        var near = free.Where(p => hops[p.Id] == 1).Min(p => DefenseValue(g, p));
        var far = free.Where(p => hops[p.Id] >= 3).Max(p => DefenseValue(g, p));
        Expect.Greater(far, near * 2.5);
        Expect.Less(near, Galaxies.FleetStrength(FleetsAt(g, Capital(g, Klaed).Id, Klaed)[0]) * 1.2);
        // No draw in what guards a planet: the same planet, the same garrison.
        var p0 = free[0];
        Assert.Equal(Garrison(p0, hops[p0.Id]).Ships.Of(ShipClass.Frigate), Garrison(p0, hops[p0.Id]).Ships.Of(ShipClass.Frigate));
    }

    [Fact]
    public void The_classic_galaxy_stays_mirrored_with_its_garrisons()
    {
        var g = CreateGalaxy(Faction.Klaed, [Faction.Nairan], true, 7, errants: false, setup: CampaignSetup.Of(10));
        for (var i = 0; i < 7; i++)
        {
            var (a, b) = (g.Planets[i], g.Planets[i + 7]);
            Assert.Equal(DefenseValue(g, a), DefenseValue(g, b));
        }
    }

    [Fact]
    public void A_garrison_fights_and_the_winner_takes_its_planet()
    {
        var g = Three(peace: 0);
        foreach (var f in g.Factions.Values) f.AI = null;
        // Nobody's battle waits for a choice: it is resolved at once.
        g.Player = Faction.Errant;
        var home = Capital(g, Klaed);
        var target = g.Planets.Where(p => p.Owner == Independent && Neighbours(g, home.Id).Contains(p.Id)).OrderBy(p => DefenseValue(g, p)).First();
        var fleet = AddShipsAt(g, Klaed, home.Id, new ShipCounts { [ShipClass.Battlecruiser] = 8 })!;
        Assert.True(SendFleet(g, fleet.Id, target.Id).Ok);
        RunGalaxy(g, 120);
        Assert.Contains(g.Events, e => e is BattleEnded b && b.Attacker == Klaed && b.Defender == Independent);
        Assert.Equal(Klaed, target.Owner);
        Assert.Empty(FleetsAt(g, target.Id, Independent));
    }

    [Fact]
    public void A_garrison_battle_flies_the_planets_people_in_grey()
    {
        var g = Three(peace: 0);
        foreach (var f in g.Factions.Values) f.AI = null;
        var home = Capital(g, Klaed);
        var target = g.Planets.First(p => p.Owner == Independent && Neighbours(g, home.Id).Contains(p.Id));
        var fleet = AddShipsAt(g, Klaed, home.Id, new ShipCounts { [ShipClass.Frigate] = 1 })!;
        SendFleet(g, fleet.Id, target.Id);
        for (var i = 0; i < 2000 && g.Battles.Count == 0; i++) StepGalaxy(g);
        var b = Assert.Single(g.Battles);
        Assert.Equal(IndependentRace(target), b.World.Factions[Independent].Race);
        Assert.Equal(IndependentColor, b.World.Factions[Independent].Color);
    }

    [Fact]
    public void Garrisons_never_leave_and_never_count_as_a_menace()
    {
        var g = Three(peace: 0);
        var at = g.Fleets.Values.Where(f => f.Side == Independent).ToDictionary(f => f.Id, f => f.At);
        RunGalaxy(g, 300);
        foreach (var f in g.Fleets.Values.Where(f => f.Side == Independent)) Assert.Equal(at[f.Id], f.At);
        // Next to garrisons only, no AI arms a planet against them as though they would strike.
        Assert.DoesNotContain(g.Planets, p => p.Owner == Nairan && p.Home is null && p.Platforms > 0 && p.Station == 0);
    }

    [Fact]
    public void The_opening_peace_binds_everyone_then_ends_with_one_notice()
    {
        var g = Three(peace: 5);
        Assert.Equal(3, g.Truces.Count);
        Assert.All(g.Truces, t => Assert.True(t.Opening && t.Remaining == 300));
        Assert.True(AtPeace(g, Klaed, Nairan) && AtPeace(g, Nairan, Nautolan));
        Assert.False(BreakTruce(g, Klaed, Nairan).Ok);
        Assert.False(SendFleet(g, FleetsAt(g, Capital(g, Klaed).Id, Klaed)[0].Id, Capital(g, Nairan).Id).Ok);
        // No ultimatum may tear it, however strong the one who threatens.
        AddShipsAt(g, Klaed, Capital(g, Klaed).Id, new ShipCounts { [ShipClass.Titan] = 4 });
        Assert.False(CanPropose(g, Klaed, Nairan, DealKind.Ultimatum, 500).Ok);
        // Keeping it builds no trust: everyone is bound to.
        RunGalaxy(g, 200);
        Assert.Equal(0, TrustOf(g, Nairan, Klaed), 3);
        var ended = 0;
        for (var i = 0; i < 1200; i++)
        {
            StepGalaxy(g);
            ended += g.Events.Count(e => e is PeaceOver);
            Assert.DoesNotContain(g.Events, e => e is TruceEnded);
            g.Events.Clear();
        }
        Assert.Equal(1, ended);
        // The AIs may sign truces of their own once it is over.
        Assert.DoesNotContain(g.Truces, t => t.Opening);
        Assert.True(AtWar(g, Klaed, Nairan));
    }

    [Fact]
    public void Teammates_need_no_opening_peace_and_none_means_war_at_once()
    {
        var g = CreateGalaxy([new Slot(Klaed, Faction.Klaed, 1), new Slot(Nairan, Faction.Nairan, 1), new Slot(Nautolan, Faction.Nautolan, 2)],
            5, GalaxyGen.GenerateLayout(5, 3, MapSize.Small), setup: CampaignSetup.Of(10));
        Assert.Equal(2, g.Truces.Count);
        Assert.Null(TruceOf(g, Klaed, Nairan));
        Assert.Empty(Three(peace: 0).Truces);
    }

    [Fact]
    public void A_fortified_capital_raises_its_lost_platforms_while_its_faction_holds_it()
    {
        var g = Three(peace: 0);
        var cap = Capital(g, Klaed);
        Assert.Equal((CapitalStation, CapitalPlatforms), (cap.Station, cap.Platforms));
        cap.Platforms = 0;
        RunGalaxy(g, RaiseTime - 1);
        Assert.Equal(0, cap.Platforms);
        RunGalaxy(g, 2 * RaiseTime);
        Assert.Equal(CapitalPlatforms, cap.Platforms);
        RunGalaxy(g, RaiseTime * 2);
        Assert.Equal(CapitalPlatforms, cap.Platforms);
        // Taken, it raises nothing for its new owner.
        cap.Owner = Nairan;
        cap.Platforms = 0;
        RunGalaxy(g, RaiseTime * 2);
        Assert.Equal(0, cap.Platforms);
    }

    [Fact]
    public void Older_galaxies_and_the_tests_default_keep_free_planets_and_bare_capitals()
    {
        var g = CreateGalaxy(Faction.Klaed, [Faction.Nairan], true, 7, GalaxyGen.GenerateLayout(7));
        Assert.DoesNotContain(g.Planets, p => p.Owner == Independent || p.Home is not null);
        Assert.Empty(g.Truces);
        Assert.Equal(1, g.Planets.First(p => p.Owner == Klaed).Station);
    }

    [Fact]
    public void Snapshots_and_saves_carry_the_capitals_the_peace_and_the_independents()
    {
        var g = Three();
        RunGalaxy(g, 30);
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms)) GalaxySnapshot.Write(g, Klaed, w, null, null, default);
        var replica = new Galaxy();
        GalaxySnapshot.ReadChart(new BinaryReader(new MemoryStream(GalaxySnapshot.Chart(g))), replica);
        Assert.NotNull(GalaxySnapshot.Read(new BinaryReader(new MemoryStream(ms.ToArray())), replica));
        Assert.Equal(g.Planets.Select(p => p.Home), replica.Planets.Select(p => p.Home));
        Assert.Equal(g.Planets.Select(p => p.Owner), replica.Planets.Select(p => p.Owner));
        Assert.All(replica.Truces, t => Assert.True(t.Opening));
        Assert.Contains(replica.Fleets.Values, f => f.Side == Independent);

        var back = GalaxySave.Deserialize(GalaxySave.Serialize(g))!;
        Assert.Equal(g.Planets.Select(p => p.Home), back.Planets.Select(p => p.Home));
        Assert.All(back.Truces, t => Assert.True(t.Opening));
        Assert.Equal(g.Planets.Select(p => p.Owner), back.Planets.Select(p => p.Owner));
    }

    [Fact]
    public void The_lobby_sends_its_opening_peace_and_refuses_one_it_does_not_offer()
    {
        var l = Lobby.Create(LobbyMode.Campaign, 3);
        Assert.Equal(10, l.Peace);
        Assert.True(LobbyRules.Apply(l, Lobby.Host, true, new LobbyRequest.Settings(LobbySettings.Of(l) with { Peace = 15 })));
        Assert.Equal(15, l.Peace);
        Assert.Equal(900, l.Setup().OpeningPeace);
        Assert.False(LobbyRules.Apply(l, Lobby.Host, true, new LobbyRequest.Settings(LobbySettings.Of(l) with { Peace = 7 })));
        Assert.Equal(15, l.Peace);
        l.Peace = 7;
        Assert.False(l.Sane());
    }

    [Fact]
    public void AIs_alone_play_a_lasting_campaign_to_its_end()
    {
        // Lasting: no faction falls in the opening peace, and the galaxy is still at war well past it.
        var layout = GalaxyGen.GenerateLayout(3, 3, MapSize.Medium);
        var g = CreateGalaxy([new Slot(Klaed, Faction.Klaed, 0, Difficulty.Normal), new Slot(Nairan, Faction.Nairan, 0, Difficulty.Normal),
            new Slot(Nautolan, Faction.Nautolan, 0, Difficulty.Normal)], 3, layout, setup: CampaignSetup.Of(10));
        g.Player = (Faction)99;
        RunGalaxy(g, 600);
        Assert.DoesNotContain(g.Factions.Values, f => f.Eliminated);
        Assert.DoesNotContain(g.Planets, p => p.Owner is Side o && IsFaction(g, o) && p.Home is Side h && h != o);
        for (var minute = 10; minute < 90 && g.Winner is null; minute++) RunGalaxy(g, 60);
        Assert.NotNull(g.Winner);
        Expect.Greater(g.Time, 15 * 60);
    }
}
