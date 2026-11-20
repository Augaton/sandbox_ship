// The galaxy AI beyond the web version: it gathers its fleets to strike together, sends help
// where an enemy fleet draws near, keeps its truces, claims derelicts and brings worn fleets home.
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.Galaxies;
using static SandboxShip.Sim.Tests.SupplyTests;

namespace SandboxShip.Sim.Tests;

public class GalaxyAiTests
{
    /// <summary>The classic galaxy with Nairan on the AI against a passive Kla'ed, watched from off the map.</summary>
    static Galaxy Versus(params string[] nairan)
    {
        var g = CreateGalaxy(Faction.Klaed, [Faction.Nairan], true, errants: false, incidents: false);
        g.Player = Faction.Nautolan;
        foreach (var name in nairan) g.Planets[Id(name)].Owner = Faction.Nairan;
        return g;
    }

    static ShipCounts Frigates(int n) => new() { [ShipClass.Frigate] = n };

    static bool Heading(Fleet f, int planet) => f.At == planet || f.To == planet || f.Path.Contains(planet);

    [Fact]
    public void A_planet_an_enemy_fleet_draws_near_gets_help_from_within_two_lanes()
    {
        foreach (var strong in new[] { true, false })
        {
            var g = Versus("Nadir", "Calyx");
            var nadir = g.Planets[Id("Nadir")];
            nadir.Station = 1;
            // A Kla'ed fleet next to Nadir, at Sorel, two lanes from Calyx.
            AddShipsAt(g, Faction.Klaed, Id("Sorel"), Frigates(strong ? 6 : 1));
            var help = AddShipsAt(g, Faction.Nairan, Id("Calyx"), new ShipCounts { [ShipClass.Battlecruiser] = 4 })!;
            RunGalaxy(g, 1);
            // Stronger than the station alone: the Calyx fleet goes to Nadir, whole. A lone
            // frigate is no danger, and the fleet goes about its business.
            Assert.Equal(strong, Heading(help, nadir.Id) && help.Ships.Of(ShipClass.Battlecruiser) == 4);
        }
    }

    [Fact]
    public void Fleets_too_weak_alone_gather_at_a_staging_planet_and_strike_together()
    {
        // Nairan holds every planet around Nadir, the Kla'ed target; Calyx has a station.
        var g = Versus("Kaldra", "Thessa", "Pélion", "Yvra", "Tarsis", "Brume", "Calyx");
        g.Planets[Id("Calyx")].Station = 1;
        var nadir = g.Planets[Id("Nadir")];
        nadir.Owner = Faction.Klaed;
        nadir.Station = 1;
        var a = AddShipsAt(g, Faction.Nairan, Id("Calyx"), Frigates(4))!;
        var b = AddShipsAt(g, Faction.Nairan, Id("Tarsis"), Frigates(4))!;
        // Neither could take Nadir alone.
        foreach (var f in new[] { a, b }) Expect.Less(FleetStrength(f), DefenseValue(g, nadir) * 1.4 + 300);
        GalaxyBattle? battle = null;
        for (var t = 0; t < 90 && battle is null; t++)
        {
            RunGalaxy(g, 1);
            battle = g.Battles.Find(x => x.PlanetId == nadir.Id);
        }
        Assert.NotNull(battle);
        Assert.Equal(Faction.Nairan, battle.Attacker);
        Expect.GreaterOrEqual(battle.World.Entities.Count(e => e.Side == Faction.Nairan && e.Type == "frigate"), 8);
        var ai = g.Factions[Faction.Nairan].AI!;
        Assert.Equal((Id("Calyx"), nadir.Id), (ai.Staging, ai.Target));
    }

    [Fact]
    public void The_AI_keeps_its_truce_and_strikes_once_it_runs_out()
    {
        var g = Versus("Calyx", "Thessa", "Brume", "Kaldra", "Tarsis");
        var nadir = g.Planets[Id("Nadir")];
        nadir.Owner = Faction.Klaed;
        nadir.Station = 1;
        AddShipsAt(g, Faction.Nairan, Id("Calyx"), new ShipCounts { [ShipClass.Battlecruiser] = 8 });
        g.Truces.Add(new Truce { A = Faction.Klaed, B = Faction.Nairan, Remaining = 60 });
        RunGalaxy(g, 55);
        Assert.DoesNotContain(g.Events, e => e is BattleStarted);
        Assert.Equal(Faction.Klaed, nadir.Owner);
        RunGalaxy(g, 60);
        Assert.Contains(g.Events, e => e is BattleEnded ended && ended.Attacker == Faction.Nairan && ended.PlanetId == nadir.Id);
    }

    [Fact]
    public void The_AI_sends_a_ship_to_claim_a_derelict_two_lanes_away()
    {
        var g = Versus("Calyx", "Thessa");
        var maru = Id("Maru");
        g.Ongoing.Add(new Incident { Kind = IncidentKind.Derelict, Planet = maru, Remaining = DerelictTime, Ships = Frigates(3) });
        AddShipsAt(g, Faction.Nairan, Id("Calyx"), Frigates(3));
        RunGalaxy(g, 1);
        var sent = Assert.Single(g.Fleets.Values, f => f.Side == Faction.Nairan && f.At is null && Heading(f, maru));
        Assert.Equal(1, Battle.CountShips(sent.Ships));
        RunGalaxy(g, 20);
        Assert.Contains(g.Events, e => e is IncidentEnded { Kind: IncidentKind.Derelict } i && i.Side == Faction.Nairan);
    }

    /// <summary>As Versus, the AI at this difficulty.</summary>
    static Galaxy Level(Difficulty difficulty, params string[] nairan)
    {
        var g = Versus(nairan);
        g.Factions[Faction.Nairan].AI!.Difficulty = difficulty;
        return g;
    }

    [Fact]
    public void The_AI_buys_platform_levels_and_counts_them()
    {
        var g = Versus("Calyx", "Thessa");
        var calyx = g.Planets[Id("Calyx")];
        calyx.Station = 2;
        calyx.Platforms = 3;
        var tier1 = DefenseValue(g, calyx);
        // Both levels strengthen hull and shield: the estimate grows with them.
        calyx.PlatformTier = 3;
        var platform = Buildables.Defs[BuildableType.Platform];
        var top = platform.Tiers![1];
        Expect.Close(tier1 + 3 * platform.Cost * ((top.Hull + top.Shield) / (platform.Hull + platform.Shield) - 1), DefenseValue(g, calyx), 6);
        // Facing the Kla'ed next door, with credits to spare, the AI buys one; the easy AI never does.
        foreach (var (level, buys) in new[] { (Difficulty.Normal, true), (Difficulty.Easy, false) })
        {
            var h = Level(level, "Calyx", "Thessa");
            var at = h.Planets[Id("Calyx")];
            at.Station = 2;
            at.Platforms = 3;
            at.Mines = at.MineSlots;
            // Out of reach for now, so that the front stays where it is.
            h.Planets[Id("Kaldra")].Owner = Faction.Klaed;
            h.Planets[Id("Kaldra")].Station = Station.Levels.Count;
            h.Factions[Faction.Nairan].Credits = 100000;
            RunGalaxy(h, 60);
            Assert.Equal(buys, at.PlatformTier > 1 || at.Buildings.Exists(q => q.Kind == GalaxyBuildKind.PlatformTier));
        }
    }

    [Fact]
    public void The_AI_mines_a_rich_deposit_first_and_builds_at_a_shipyard_the_easy_AI_does_not()
    {
        foreach (var (level, wise) in new[] { (Difficulty.Normal, true), (Difficulty.Easy, false) })
        {
            // Nadir is rich but pays little itself, Calyx the other way round.
            var g = Level(level, "Calyx", "Nadir");
            var (nadir, calyx) = (g.Planets[Id("Nadir")], g.Planets[Id("Calyx")]);
            nadir.Income = 1;
            calyx.Income = 8;
            calyx.Special = null;
            foreach (var p in g.Planets.Where(p => p.Owner == Faction.Nairan && p != nadir && p != calyx)) p.Mines = p.MineSlots;
            g.Factions[Faction.Nairan].Credits = 500;
            RunGalaxy(g, 0.2);
            Assert.Equal(wise, nadir.Buildings.Exists(q => q.Kind == GalaxyBuildKind.Mine));
            Assert.Equal(!wise, calyx.Buildings.Exists(q => q.Kind == GalaxyBuildKind.Mine));

            // Two yards, the shipyard further from the front.
            var h = Level(level, "Kaldra", "Calyx");
            h.Planets[Id("Maru")].Owner = Faction.Klaed;
            var (kaldra, far) = (h.Planets[Id("Kaldra")], h.Planets.First(p => p.Owner == Faction.Nairan && p.Station > 0));
            Assert.Equal(PlanetSpecial.Shipyard, kaldra.Special);
            (kaldra.Station, far.Station) = (1, 1);
            (kaldra.Special, far.Special) = (null, PlanetSpecial.Shipyard);
            foreach (var p in h.Planets.Where(p => p.Owner == Faction.Nairan)) p.Mines = p.MineSlots;
            h.Factions[Faction.Nairan].AI!.Want = ShipClass.Frigate;
            h.Factions[Faction.Nairan].AI!.Personality = Personality.Aggressive;
            h.Factions[Faction.Nairan].Credits = 100000;
            RunGalaxy(h, 0.2);
            Assert.Equal(wise, far.Units.Count > 0);
            Assert.Equal(!wise, kaldra.Units.Count > 0);
        }
    }

    [Fact]
    public void The_hard_AI_puts_a_lab_and_research_before_its_ships_the_others_do_not()
    {
        foreach (var (level, studies) in new[] { (Difficulty.Hard, true), (Difficulty.Normal, false), (Difficulty.Easy, false) })
        {
            var g = Level(level, "Calyx");
            var own = g.Planets.Where(p => p.Owner == Faction.Nairan).ToList();
            foreach (var p in own) p.Mines = p.MineSlots;
            AddShipsAt(g, Faction.Nairan, Id("Calyx"), Frigates(6));
            g.Factions[Faction.Nairan].Credits = 5000;
            RunGalaxy(g, 0.2);
            Assert.Equal(studies, own.Exists(p => p.Buildings.Exists(q => q.Kind == GalaxyBuildKind.Lab)));
            // A lab there, what pays most comes first.
            var h = Level(level, "Calyx");
            foreach (var p in h.Planets.Where(p => p.Owner == Faction.Nairan))
            {
                p.Mines = p.MineSlots;
                p.Lab = 1;
            }
            AddShipsAt(h, Faction.Nairan, Id("Calyx"), Frigates(6));
            h.Factions[Faction.Nairan].Credits = 5000;
            RunGalaxy(h, 0.2);
            Assert.Equal(studies, h.Factions[Faction.Nairan].Researching?.Tech == TechId.Mining);
        }
    }

    [Fact]
    public void With_no_fleet_to_speak_of_the_hard_AI_builds_ships_before_research()
    {
        var g = Level(Difficulty.Hard, "Calyx");
        foreach (var p in g.Planets.Where(p => p.Owner == Faction.Nairan))
        {
            p.Mines = p.MineSlots;
            p.Lab = 1;
        }
        foreach (var f in g.Fleets.Values.Where(f => f.Side == Faction.Nairan).ToList()) g.Fleets.Remove(f.Id);
        g.Factions[Faction.Nairan].Credits = 5000;
        g.Factions[Faction.Nairan].Research[TechId.Mining] = 1;
        RunGalaxy(g, 0.2);
        Assert.Null(g.Factions[Faction.Nairan].Researching);
        Assert.Contains(g.Planets.Where(p => p.Owner == Faction.Nairan).SelectMany(p => p.Units), q => q.Kind == GalaxyBuildKind.Ship);
    }

    [Fact]
    public void The_hard_AI_answers_danger_with_ships_the_others_with_defenses()
    {
        foreach (var (level, ships) in new[] { (Difficulty.Hard, true), (Difficulty.Normal, false) })
        {
            var g = Level(level, "Calyx");
            var home = g.Planets.First(p => p.Owner == Faction.Nairan && p.Station > 0);
            foreach (var p in g.Planets.Where(p => p.Owner == Faction.Nairan)) p.Mines = p.MineSlots;
            AddShipsAt(g, Faction.Klaed, Neighbours(g, home.Id)[0], new ShipCounts { [ShipClass.Titan] = 3 });
            g.Factions[Faction.Nairan].AI!.Want = ShipClass.Frigate;
            g.Factions[Faction.Nairan].Credits = 100000;
            RunGalaxy(g, 0.2);
            Assert.Equal(ships, home.Units.Count > 0);
            Assert.Equal(!ships, home.Buildings.Exists(q => q.Kind == GalaxyBuildKind.Platform));
        }
    }

    [Fact]
    public void The_hard_AI_buys_what_wins_for_its_price_the_easy_one_what_looks_big()
    {
        int Big(Difficulty level)
        {
            var g = Level(level, "Calyx");
            var home = g.Planets.First(p => p.Owner == Faction.Nairan && p.Station > 0);
            home.Station = 3;
            var (f, ai) = (g.Factions[Faction.Nairan], g.Factions[Faction.Nairan].AI!);
            var big = 0;
            for (var draw = 0; draw < 200; draw++)
            {
                // Nothing to buy with: it only says what it wants.
                f.Credits = 0;
                ai.Want = null;
                ai.Timer = 0;
                StepGalaxy(g);
                if (ai.Want is ShipClass.Bulwark or ShipClass.Titan) big++;
            }
            return big;
        }
        var (hard, normal, easy) = (Big(Difficulty.Hard), Big(Difficulty.Normal), Big(Difficulty.Easy));
        Assert.Equal(0, hard);
        Expect.Greater(easy, normal);
    }

    [Fact]
    public void The_hard_AI_raises_a_station_where_its_fleet_stands_out_of_supply_the_normal_one_does_not()
    {
        foreach (var (level, raises) in new[] { (Difficulty.Hard, true), (Difficulty.Normal, false) })
        {
            // Sorel is far from Ostra, the Nairan station, and pays too little to tempt the normal AI.
            var g = Level(level, "Sorel");
            var sorel = g.Planets[Id("Sorel")];
            foreach (var p in g.Planets.Where(p => p.Owner == Faction.Nairan)) p.Mines = p.MineSlots;
            var fleet = AddShipsAt(g, Faction.Nairan, sorel.Id, Frigates(10))!;
            Assert.False(Supplied(g, fleet));
            g.Factions[Faction.Nairan].Credits = 2000;
            RunGalaxy(g, 0.2);
            Assert.Equal(raises, sorel.Buildings.Exists(q => q.Kind == GalaxyBuildKind.Station));
        }
    }

    [Fact]
    public void A_worn_fleet_at_home_out_of_supply_goes_to_a_station_to_mend()
    {
        var g = Versus("Sorel");
        var worn = AddShipsAt(g, Faction.Nairan, Id("Sorel"), new ShipCounts { [ShipClass.Battlecruiser] = 1 }, condition: 0.4)!;
        Assert.False(Supplied(g, worn));
        RunGalaxy(g, 1);
        Assert.Equal(Id("Ostra"), worn.Path.Count > 0 ? worn.Path[^1] : worn.To);
    }

    [Fact]
    public void The_easy_AI_keeps_a_single_ship_in_production_where_the_others_keep_two()
    {
        foreach (var (level, most) in new[] { (Difficulty.Easy, 1), (Difficulty.Normal, 2) })
        {
            var g = Level(level, "Calyx");
            var home = g.Planets.First(p => p.Owner == Faction.Nairan && p.Station > 0);
            foreach (var p in g.Planets.Where(p => p.Owner == Faction.Nairan)) p.Mines = p.MineSlots;
            var seen = 0;
            for (var t = 0; t < 60; t++)
            {
                g.Factions[Faction.Nairan].Credits = 100000;
                RunGalaxy(g, 1);
                seen = Math.Max(seen, home.Units.Count);
            }
            Assert.Equal(most, seen);
        }
    }

    [Fact]
    public void The_easy_AI_strikes_too_soon_alone_the_others_wait_for_the_edge()
    {
        foreach (var (level, strikes) in new[] { (Difficulty.Easy, true), (Difficulty.Normal, false), (Difficulty.Hard, false) })
        {
            var g = Level(level, "Calyx", "Thessa", "Brume", "Kaldra", "Tarsis");
            var nadir = g.Planets[Id("Nadir")];
            nadir.Owner = Faction.Klaed;
            AddShipsAt(g, Faction.Klaed, nadir.Id, Frigates(4));
            // Stronger than the fleet at Nadir, not by the edge a battle against a fleet takes.
            var fleet = AddShipsAt(g, Faction.Nairan, Id("Calyx"), Frigates(1))!;
            while (FleetStrength(fleet) < DefenseValue(g, nadir) * 1.1 + 300) fleet.Ships.Increase(ShipClass.Frigate, 1);
            RunGalaxy(g, 0.2);
            Assert.Equal(strikes, Heading(fleet, nadir.Id));
        }
    }

    [Fact]
    public void The_hard_AI_knows_a_station_falls_more_easily_than_its_hull_suggests()
    {
        foreach (var (level, strikes) in new[] { (Difficulty.Hard, true), (Difficulty.Normal, false) })
        {
            var g = Level(level, "Calyx", "Thessa", "Brume", "Kaldra", "Tarsis");
            var nadir = g.Planets[Id("Nadir")];
            nadir.Owner = Faction.Klaed;
            nadir.Station = 2;
            var fleet = AddShipsAt(g, Faction.Nairan, Id("Calyx"), Frigates(1))!;
            while (FleetStrength(fleet) < DefenseValue(g, nadir) * 0.8 + 300) fleet.Ships.Increase(ShipClass.Frigate, 1);
            RunGalaxy(g, 0.2);
            Assert.Equal(strikes, Heading(fleet, nadir.Id));
        }
    }

    [Fact]
    public void The_easy_AI_sends_no_help_and_offers_no_deal()
    {
        foreach (var (level, wise) in new[] { (Difficulty.Easy, false), (Difficulty.Normal, true) })
        {
            // Calyx is at the front itself, Kaldra too strong to strike: the fleet there only moves for Nadir.
            var g = Level(level, "Nadir", "Calyx");
            var nadir = g.Planets[Id("Nadir")];
            nadir.Station = 1;
            g.Planets[Id("Kaldra")].Owner = Faction.Klaed;
            g.Planets[Id("Kaldra")].Station = Station.Levels.Count;
            AddShipsAt(g, Faction.Klaed, Id("Sorel"), Frigates(6));
            // Far weaker than the Kla'ed, face to face: a wise AI seeks a truce.
            AddShipsAt(g, Faction.Klaed, Id("Vesper"), new ShipCounts { [ShipClass.Battlecruiser] = 8 });
            var help = AddShipsAt(g, Faction.Nairan, Id("Calyx"), new ShipCounts { [ShipClass.Battlecruiser] = 4 })!;
            RunGalaxy(g, 1);
            Assert.Equal(wise, Heading(help, nadir.Id));
            Assert.Equal(wise, g.Offers.Exists(o => o.From == Faction.Nairan && o.Kind == DealKind.Truce));
        }
    }

    [Fact]
    public void The_hard_AI_breaks_a_truce_when_it_pays_never_one_paid_for_and_the_others_keep_theirs()
    {
        foreach (var (level, paid, breaks) in new[] { (Difficulty.Hard, false, true), (Difficulty.Hard, true, false), (Difficulty.Normal, false, false) })
        {
            var g = Level(level, "Calyx", "Thessa", "Brume", "Kaldra", "Tarsis");
            var nadir = g.Planets[Id("Nadir")];
            nadir.Owner = Faction.Klaed;
            AddShipsAt(g, Faction.Nairan, Id("Calyx"), new ShipCounts { [ShipClass.Battlecruiser] = 8 });
            g.Truces.Add(new Truce { A = Faction.Klaed, B = Faction.Nairan, Remaining = 300, Paid = paid });
            RunGalaxy(g, 3);
            // A player is warned first: the truce holds a while longer.
            Assert.True(AtPeace(g, Faction.Klaed, Faction.Nairan));
            Assert.Equal(breaks, g.Chronicle.Exists(e => e.Kind == ChronicleKind.Said && e.Extra == (int)Remark.Warning));
            RunGalaxy(g, 31);
            Assert.Equal(breaks, !AtPeace(g, Faction.Klaed, Faction.Nairan));
            Assert.Equal(breaks, g.Chronicle.Exists(e => e is { Kind: ChronicleKind.Broken, Deal: DealKind.Truce } && e.By == Faction.Nairan));
        }
        // Not as strong as that, with nothing it could take: it keeps it too.
        var h = Level(Difficulty.Hard, "Calyx");
        h.Truces.Add(new Truce { A = Faction.Klaed, B = Faction.Nairan, Remaining = 300 });
        RunGalaxy(h, 3);
        Assert.True(AtPeace(h, Faction.Klaed, Faction.Nairan));
    }

    [Fact]
    public void The_hard_AI_turns_on_a_rival_busy_elsewhere_the_normal_one_goes_for_the_worth()
    {
        // Four sides on a large map: Nairan stands between Kla'ed, who fights a fourth side, and
        // Nautolan, at peace with Kla'ed. Nautolan's planet is worth more, Kla'ed's capital busy.
        foreach (var (level, busy) in new[] { (Difficulty.Hard, true), (Difficulty.Normal, false) })
        {
            var fourth = Lobby.SideOf(4);
            var layout = GalaxyGen.GenerateLayout(11, 4, MapSize.Large);
            var g = CreateGalaxy([new Slot(Faction.Klaed, Faction.Klaed), new Slot(Faction.Nairan, Faction.Nairan, 0, level), new Slot(Faction.Nautolan, Faction.Nautolan), new Slot(fourth, Faction.Klaed)],
                11, layout, errants: false, incidents: false);
            g.Player = (Faction)99;
            foreach (var p in g.Planets)
            {
                p.Owner = null;
                p.Station = 0;
                p.Mines = 0;
                p.Special = null;
                p.Income = 1;
            }
            g.Fleets.Clear();
            var (s, a, b, c) = (from s0 in g.Planets
                                from a0 in Neighbours(g, s0.Id) from b0 in Neighbours(g, s0.Id) where a0 != b0
                                from c0 in Neighbours(g, a0) where c0 != s0.Id && LanesBetween(g, c0, b0) >= 3 && LanesBetween(g, c0, s0.Id) >= 2
                                select (s0.Id, a0, b0, c0)).First();
            g.Planets[s].Owner = Faction.Nairan;
            g.Planets[a].Owner = Faction.Klaed;
            g.Planets[a].Income = 4;
            g.Planets[b].Owner = Faction.Nautolan;
            g.Planets[b].Income = 6;
            g.Planets[c].Owner = fourth;
            g.Truces.Add(new Truce { A = Faction.Klaed, B = Faction.Nautolan, Remaining = 3000 });
            RunGalaxy(g, 1);
            Assert.Equal(busy ? a : b, g.Factions[Faction.Nairan].AI!.Target);
        }
    }

    [Fact]
    public void The_hard_AI_counts_the_fleets_next_door_the_normal_one_only_what_guards_the_planet()
    {
        foreach (var (level, strikes) in new[] { (Difficulty.Normal, true), (Difficulty.Hard, false) })
        {
            // Orin keeps Sorel in sight.
            var g = Level(level, "Calyx", "Thessa", "Brume", "Kaldra", "Tarsis", "Orin");
            var nadir = g.Planets[Id("Nadir")];
            nadir.Owner = Faction.Klaed;
            g.Planets[Id("Sorel")].Owner = Faction.Klaed;
            // Nothing at Nadir; a big fleet next door, at Sorel.
            var fleet = AddShipsAt(g, Faction.Nairan, Id("Calyx"), new ShipCounts { [ShipClass.Battlecruiser] = 3 })!;
            AddShipsAt(g, Faction.Klaed, Id("Sorel"), new ShipCounts { [ShipClass.Battlecruiser] = 8 });
            RunGalaxy(g, 0.2);
            Assert.Equal(strikes, Heading(fleet, nadir.Id));
        }
    }

    [Fact]
    public void A_storm_keeps_the_AI_fleet_in_and_it_strikes_once_it_is_over()
    {
        // Calyx alone next to Nadir, for now.
        var g = Versus("Calyx", "Thessa", "Brume");
        var nadir = g.Planets[Id("Nadir")];
        nadir.Owner = Faction.Klaed;
        var fleet = AddShipsAt(g, Faction.Nairan, Id("Calyx"), new ShipCounts { [ShipClass.Battlecruiser] = 8 })!;
        g.Ongoing.Add(new Incident { Kind = IncidentKind.IonStorm, Planet = Id("Calyx"), Remaining = 20 });
        RunGalaxy(g, 15);
        Assert.Equal(Id("Calyx"), fleet.At);
        RunGalaxy(g, 40);
        Assert.Equal(Faction.Nairan, nadir.Owner);
    }

    [Fact]
    public void The_AI_answers_a_war_band_it_was_warned_of()
    {
        var g = Versus("Nadir", "Calyx");
        var nadir = g.Planets[Id("Nadir")];
        nadir.Station = 1;
        var help = AddShipsAt(g, Faction.Nairan, Id("Calyx"), new ShipCounts { [ShipClass.Battlecruiser] = 4 })!;
        g.Ongoing.Add(new Incident { Kind = IncidentKind.WarBand, Planet = Id("Sorel"), Other = nadir.Id, Remaining = WarBandWarning, Ships = new ShipCounts { [ShipClass.Battlecruiser] = 3 } });
        RunGalaxy(g, 1);
        Assert.True(Heading(help, nadir.Id));
    }

    [Fact]
    public void A_worn_fleet_beyond_its_supply_lines_heads_home_to_mend()
    {
        var g = Versus();
        var worn = AddShipsAt(g, Faction.Nairan, Id("Sorel"), new ShipCounts { [ShipClass.Battlecruiser] = 1 }, condition: 0.4)!;
        Assert.False(Supplied(g, worn));
        RunGalaxy(g, 1);
        Assert.Equal(Id("Ostra"), worn.Path.Count > 0 ? worn.Path[^1] : worn.To);
    }

    [Fact]
    public void A_station_level_far_beyond_its_income_does_not_keep_the_AI_from_a_lab()
    {
        // Its capital wants level 4 (18 000 credits, past two minutes of its income); a lab costs 800.
        var g = Versus("Calyx", "Tarsis");
        var ai = g.Factions[Faction.Nairan].AI!;
        ai.Personality = Personality.Economic;
        g.Planets[Id("Ostra")].Station = 3;
        foreach (var p in g.Planets.Where(p => p.Owner == Faction.Nairan)) p.Mines = p.MineSlots;
        // Enough for two stations, which come before it, and the lab.
        g.Factions[Faction.Nairan].Credits = 5000;
        Expect.Greater(18000, IncomeOf(g, Faction.Nairan) * 120);
        RunGalaxy(g, 6);
        Assert.Contains(g.Planets, p => p.Owner == Faction.Nairan && (p.Lab > 0 || p.Buildings.Exists(q => q.Kind == GalaxyBuildKind.Lab)));
    }
}
