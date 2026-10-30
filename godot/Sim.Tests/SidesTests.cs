// Sides, races and teams (not in the web version): who owns what is a side, which race it plays
// is apart, and sides of the same team are allies. Two sides may share a race; allies never hurt
// nor target each other, see together and win together; the Errants are everyone's foes.
using System.Text.Json.Nodes;
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim.Tests;

public class SidesTests
{
    static readonly Side Fourth = new(4), Fifth = new(5), Sixth = new(6);

    /// <summary>Two teams of two: Kla'ed and Nautolan (team 1) against Nairan and a second Kla'ed (team 2).</summary>
    static World TwoOnTwo()
    {
        var w = CreateWorld(4000, 3000);
        AddSide(w, Faction.Klaed, Faction.Klaed, 0, team: 1);
        AddSide(w, Faction.Nairan, Faction.Nairan, 0, team: 2);
        AddSide(w, Faction.Nautolan, Faction.Nautolan, 0, team: 1);
        AddSide(w, Fourth, Faction.Klaed, 0, team: 2);
        return w;
    }

    [Fact]
    public void A_race_converts_to_its_own_side_and_extra_sides_take_the_player_colors()
    {
        Assert.Equal(new Side(0), (Side)Faction.Klaed);
        Assert.Equal(new Side(3), (Side)Faction.Errant);
        var w = CreateWorld(1000, 1000);
        var klaed = AddFaction(w, Faction.Klaed, 0);
        Assert.Equal((Faction.Klaed, 0, Factions.Defs[Faction.Klaed].Color), (klaed.Race, klaed.Team, klaed.Color));
        var colors = new[] { Fourth, Fifth, Sixth, new Side(7), new Side(8), new Side(9) }.Select(s => AddSide(w, s, Faction.Klaed, 0).Color).ToList();
        Assert.Equal(Factions.PlayerColors, colors);
        // None of them is a race's color: every side of a six-player game reads apart.
        Assert.Empty(colors.Intersect(Factions.Defs.Values.Select(d => d.Color)));
        Assert.Equal(0x123456u, AddSide(w, new Side(10), Faction.Nairan, 0, color: 0x123456).Color);
    }

    [Fact]
    public void Two_Klaed_sides_fight_each_other()
    {
        var w = CreateWorld(2000, 1200);
        AddFaction(w, Faction.Klaed, 0);
        AddSide(w, Fourth, Faction.Klaed, 0);
        var a = SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 800, 600);
        var b = SpawnShip(w, Fourth, ShipClass.Frigate, 1100, 600, Math.PI);
        Assert.Equal((Faction.Klaed, Faction.Klaed), (a.Race, b.Race));
        Assert.True(Foes(w, a.Side, b.Side));
        // The same race: the same trait, the same price.
        Assert.Equal(Combat.DamageMult(w, a), Combat.DamageMult(w, b));
        Assert.Equal(a.MaxShield, b.MaxShield);
        Steps.Run(w, 1);
        Assert.Equal((b.Id, a.Id), (a.TargetId, b.TargetId));
        while (w.Time < 120 && w.Entities.Has(a.Id) && w.Entities.Has(b.Id)) Steps.Run(w, 1);
        var (won, lost) = w.Entities.Has(a.Id) ? ((Side)Faction.Klaed, Fourth) : (Fourth, (Side)Faction.Klaed);
        Assert.False(w.Entities.Has(lost == Fourth ? b.Id : a.Id));
        Assert.Equal(1, w.Factions[won].Stats.Kills);
        Expect.Greater(w.Factions[won].Bounties, 0);
        Assert.Equal(1, w.Factions[lost].Stats.Lost);
    }

    [Fact]
    public void Allies_never_target_nor_hurt_each_other()
    {
        var w = TwoOnTwo();
        Assert.True(Allies(w, Faction.Klaed, Faction.Nautolan));
        Assert.True(Allies(w, Faction.Nairan, Fourth));
        Assert.True(Foes(w, Faction.Klaed, Fourth));
        // Allies side by side, a foe beyond.
        var klaed = SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 1000, 1000);
        var nautolan = SpawnShip(w, Faction.Nautolan, ShipClass.Frigate, 1080, 1000);
        var foe = SpawnShip(w, Fourth, ShipClass.Frigate, 1350, 1000, Math.PI);
        Steps.Run(w, 1);
        Assert.Equal(foe.Id, klaed.TargetId);
        Assert.Equal(foe.Id, nautolan.TargetId);
        Assert.Contains(foe.TargetId, new int?[] { klaed.Id, nautolan.Id });
        // No blow lands on an ally, whatever brings it: a shot, an orbital strike, a star lance.
        var before = nautolan.Hull + nautolan.Shield;
        AbilitySystem.Hurt(w, nautolan, 500, Faction.Klaed);
        Assert.Equal(before, nautolan.Hull + nautolan.Shield);
        w.Strikes.Add(new Strike { Kind = StrikeKind.Orbital, Side = Faction.Klaed, X = 1200, Y = 1000, X2 = 1200, Y2 = 1000, Radius = 400, Damage = 300, Delay = 0.01, Total = 0.01 });
        w.Strikes.Add(new Strike { Kind = StrikeKind.Stellar, Side = Faction.Nautolan, X = 1200, Y = 1000, X2 = 1200, Y2 = 1000, Radius = 400, Damage = 300, Delay = 0.01, Total = 0.01 });
        var foeBefore = foe.Hull + foe.Shield;
        Steps.Run(w, 0.05);
        Assert.Equal(nautolan.MaxHull, nautolan.Hull);
        Assert.Equal(klaed.MaxHull, klaed.Hull);
        Expect.Less(foe.Hull + foe.Shield, foeBefore - 500);
        // The fight goes on: never a shot at an ally.
        for (var t = 0; t < 600 && w.Entities.Has(foe.Id); t++)
        {
            Steps.StepWorld(w);
            foreach (var p in w.Projectiles) Assert.True(w.Entities.Get(p.TargetId) is not { } target || Foes(w, p.Side, target.Side));
        }
        Assert.Equal(nautolan.MaxHull, nautolan.Hull);
    }

    [Fact]
    public void Allies_help_each_other_mend_and_command()
    {
        var w = TwoOnTwo();
        var hurt = SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 1000, 1000);
        hurt.Hull = hurt.MaxHull * 0.5;
        SpawnShip(w, Faction.Nautolan, ShipClass.Support, 1060, 1000);
        var foe = SpawnShip(w, Faction.Nairan, ShipClass.Frigate, 3500, 2500);
        foe.Hull = foe.MaxHull * 0.5;
        Steps.Run(w, 3);
        Expect.Greater(hurt.Hull, hurt.MaxHull * 0.5);
        Assert.Equal(foe.MaxHull * 0.5, foe.Hull, 6);
        // An admiral's command and a shield dome cover the allies around them, not the foes.
        var admiral = SpawnHero(w, Faction.Nautolan, new HeroId(Faction.Nautolan, HeroRole.Admiral), 1100, 1100);
        var near = SpawnShip(w, Fourth, ShipClass.Frigate, 1150, 1150);
        Steps.Run(w, 0.1);
        Assert.True(AbilitySystem.InCommandAura(w, hurt));
        Assert.False(AbilitySystem.InCommandAura(w, near));
        Assert.Equal(Faction.Nautolan, admiral.Race);
    }

    [Fact]
    public void Allies_see_what_any_of_them_sees()
    {
        var w = TwoOnTwo();
        w.Fog = true;
        AddSide(w, Fifth, Faction.Nairan, 0);
        var scout = SpawnShip(w, Faction.Klaed, ShipClass.Scout, 500, 500);
        var foe = SpawnShip(w, Faction.Nairan, ShipClass.Frigate, 1200, 500);
        var outpost = SpawnStructure(w, Faction.Nairan, BuildableType.Outpost, 1400, 800);
        Steps.Run(w, 0.5);
        // The Nautolan have no ship here: they see through their Kla'ed ally's scout.
        Assert.True(Vision.IsVisible(w, Faction.Nautolan, foe));
        Assert.True(Vision.WasSeen(w, Faction.Nautolan, outpost));
        Assert.True(Vision.IsVisible(w, Faction.Nautolan, scout));
        // The second Kla'ed sees the scout through its Nairan ally; a side alone sees nothing.
        Assert.True(Vision.IsVisible(w, Fourth, scout));
        Assert.False(Vision.IsVisible(w, Fifth, scout));
        Assert.False(Vision.IsVisible(w, Fifth, foe));
        // Seen, the foe is a target for the ally too.
        var nautolan = SpawnShip(w, Faction.Nautolan, ShipClass.Torpedo, 700, 500);
        Steps.Run(w, 1);
        Assert.Equal(foe.Id, nautolan.TargetId);
    }

    [Fact]
    public void A_team_wins_together_once_the_other_team_has_no_station()
    {
        var w = TwoOnTwo();
        var stations = new Dictionary<Side, Entity>
        {
            [Faction.Klaed] = SpawnStation(w, Faction.Klaed, 500, 500),
            [Faction.Nairan] = SpawnStation(w, Faction.Nairan, 3500, 500),
            [Faction.Nautolan] = SpawnStation(w, Faction.Nautolan, 500, 2500),
            [Fourth] = SpawnStation(w, Fourth, 3500, 2500),
        };
        void Fall(Side side)
        {
            foreach (var e in ChildrenOf(w, stations[side].Id).Prepend(stations[side])) e.Hull = 0;
            Steps.StepWorld(w);
        }
        Fall(Faction.Klaed);
        Fall(Faction.Nairan);
        Assert.Null(w.Winner);
        Fall(Fourth);
        // The Nautolan hold the last station; the Kla'ed, without one, win with them.
        Assert.Equal(Faction.Nautolan, w.Winner);
        Assert.Contains(w.Events, e => e is Victory v && v.Side == Faction.Nautolan && v.Team == 1);
        Assert.True(Won(w, Faction.Klaed));
        Assert.True(Won(w, Faction.Nautolan));
        Assert.False(Won(w, Faction.Nairan));
        Assert.False(Won(w, Fourth));
    }

    [Fact]
    public void In_a_team_game_the_skirmish_AIs_never_turn_on_their_allies()
    {
        var w = Scenario.CreateSkirmish(new SkirmishOptions
        {
            Slots =
            [
                new(Faction.Klaed, Faction.Klaed, 1, Difficulty.Normal), new(Faction.Nairan, Faction.Nairan, 2, Difficulty.Normal),
                new(Faction.Nautolan, Faction.Nautolan, 1, Difficulty.Normal), new(Fourth, Faction.Klaed, 2, Difficulty.Normal),
            ],
            Credits = 20000, Seed = 5, Size = MapSize.Large, Raid = true,
        });
        foreach (var f in w.Factions.Values) Assert.Equal(f.Race == Faction.Errant ? 0 : f.Id == Faction.Klaed || f.Id == Faction.Nautolan ? 1 : 2, f.Team);
        // Judged on the teams themselves, not on the rule under test.
        bool Mates(Side a, Side b) => a == b || (w.Factions[a].Team != 0 && w.Factions[a].Team == w.Factions[b].Team);
        var foesHit = 0;
        for (var t = 0; t < 60 * 60 * 6; t++)
        {
            Steps.StepWorld(w);
            if (t % 30 != 0) continue;
            foreach (var e in w.Entities)
            {
                if (w.Entities.Get(e.TargetId) is { } target) Assert.False(Mates(e.Side, target.Side));
                if (e.Order?.Type == OrderType.Attack && w.Entities.Get(e.Order.TargetId) is { } ordered) Assert.False(Mates(e.Side, ordered.Side));
            }
            foreach (var p in w.Projectiles)
            {
                if (w.Entities.Get(p.TargetId) is not { } target) continue;
                Assert.False(Mates(p.Side, target.Side));
                foesHit++;
            }
        }
        // They did fight, their foes.
        Expect.Greater(foesHit, 0);
    }

    [Fact]
    public void Four_sides_without_teams_play_every_one_against_all()
    {
        var w = Scenario.CreateSkirmish(new SkirmishOptions
        {
            Slots = [new(Faction.Klaed, Faction.Klaed), new(Faction.Nairan, Faction.Nairan), new(Faction.Nautolan, Faction.Nautolan), new(Fourth, Faction.Klaed)],
            Seed = 9, Size = MapSize.Large,
        });
        var sides = w.Factions.Keys.ToList();
        Assert.Equal(4, sides.Count);
        foreach (var a in sides) foreach (var b in sides) Assert.Equal(a != b, Foes(w, a, b));
        Assert.Equal(Faction.Klaed, w.Factions[Fourth].Race);
        foreach (var s in sides.SkipLast(1))
        {
            Assert.Null(w.Winner);
            var station = StationOf(w, s)!;
            foreach (var e in ChildrenOf(w, station.Id).Prepend(station)) e.Hull = 0;
            Steps.StepWorld(w);
        }
        // The last station standing wins alone.
        Assert.Equal(Fourth, w.Winner);
        Assert.Contains(w.Events, e => e is Victory v && v.Side == Fourth && v.Team == 0);
        Assert.False(Won(w, Faction.Klaed));
    }

    [Theory]
    [InlineData(MapSize.Large, 5, 1)]
    [InlineData(MapSize.Large, 6, 1)]
    [InlineData(MapSize.Large, 6, 2)]
    [InlineData(MapSize.Large, 6, 3)]
    [InlineData(MapSize.Medium, 6, 4)]
    public void Up_to_six_sides_start_apart_inside_the_map(MapSize size, int count, int seed)
    {
        Side[] seats = [Faction.Klaed, Faction.Nairan, Faction.Nautolan, Fourth, Fifth, Sixth];
        var slots = seats.Take(count).Select((s, i) => new Slot(s, (Faction)(i % 3), i % 2 + 1, Difficulty.Normal)).ToList();
        var w = Scenario.CreateSkirmish(new SkirmishOptions { Slots = slots, Seed = seed, Size = size, Raid = true });
        var stations = slots.Select(s => StationOf(w, s.Side)!).ToList();
        Assert.Equal(count, stations.Distinct().Count());
        foreach (var s in stations)
        {
            Assert.InRange(s.X, 600, w.Width - 600);
            Assert.InRange(s.Y, 600, w.Height - 600);
            // Each one has its lab and its two fields behind it.
            Assert.Contains(w.Entities, e => e.Type == "lab" && e.Side == s.Side);
            Expect.GreaterOrEqual(w.Fields.Count(f => Dist(f, s) < 600), 2);
        }
        var gap = stations.SelectMany(a => stations.Where(b => b != a).Select(b => Dist(a, b))).Min();
        Expect.Greater(gap, size == MapSize.Large ? 1800 : 1300);
        // Every side reads apart, and the raid's Errants keep their own side.
        Assert.Equal(count, slots.Select(s => w.Factions[s.Side].Color).Distinct().Count());
        Assert.Equal(Faction.Errant, w.Factions[Faction.Errant].Race);
    }

    [Fact]
    public void Seats_must_be_apart_and_leave_the_Errants_their_side()
    {
        Assert.Throws<ArgumentException>(() => Scenario.CreateSkirmish(new SkirmishOptions { Slots = [new(Faction.Klaed, Faction.Klaed), new(Faction.Klaed, Faction.Nairan)] }));
        Assert.Throws<ArgumentException>(() => Scenario.CreateSkirmish(new SkirmishOptions { Slots = [new(Faction.Klaed, Faction.Klaed), new(Faction.Errant, Faction.Nairan)], Raid = true }));
        Assert.Throws<ArgumentException>(() => Galaxies.CreateGalaxy([new Slot(Faction.Klaed, Faction.Klaed), new Slot(Faction.Errant, Faction.Nairan)], 7, GalaxyGen.GenerateLayout(7)));
    }

    [Fact]
    public void The_Errants_are_everyones_foes_teams_or_not()
    {
        var w = TwoOnTwo();
        // Even told to join a team, they join none.
        var errants = AddSide(w, Faction.Errant, Faction.Errant, 0, team: 1);
        Assert.Equal(0, errants.Team);
        foreach (var s in w.Factions.Keys.Where(s => s != Faction.Errant)) Assert.True(Foes(w, Faction.Errant, s));
        var colossus = Raids.Place(w);
        var ship = SpawnShip(w, Faction.Nautolan, ShipClass.Frigate, colossus.X + 300, colossus.Y);
        Steps.Run(w, 1);
        Assert.Equal(ship.Id, colossus.TargetId);
        // In the galaxy too: at war with every side, of any team.
        var g = Galaxies.CreateGalaxy(
            [new Slot(Faction.Klaed, Faction.Klaed, 1), new Slot(Faction.Nairan, Faction.Nairan, 2), new Slot(Faction.Nautolan, Faction.Nautolan, 1), new Slot(Fourth, Faction.Klaed, 2)],
            7, GalaxyGen.GenerateLayout(7, 4));
        Assert.Contains(g.Planets, p => p.Owner == Faction.Errant);
        foreach (var s in g.Factions.Keys) Assert.True(Galaxies.AtWar(g, Faction.Errant, s));
    }

    /// <summary>Four sides in two teams on a medium galaxy, on no AI unless told: Kla'ed and Nautolan against Nairan and a second Kla'ed.</summary>
    static Galaxy TeamGalaxy(Difficulty? ai = null, int seed = 11) => Galaxies.CreateGalaxy(
        [
            new Slot(Faction.Klaed, Faction.Klaed, 1, ai), new Slot(Faction.Nairan, Faction.Nairan, 2, ai),
            new Slot(Faction.Nautolan, Faction.Nautolan, 1, ai), new Slot(Fourth, Faction.Klaed, 2, ai),
        ],
        seed, GalaxyGen.GenerateLayout(seed, 4), errants: false, incidents: false);

    static int Capital(Galaxy g, Side side) => g.Planets.Find(p => p.Owner == side)!.Id;

    [Fact]
    public void Galaxy_sides_carry_their_race_team_and_color()
    {
        var g = TeamGalaxy();
        Assert.Equal(Faction.Klaed, g.Player);
        Assert.Equal(4, g.Planets.Count(p => p.Owner is not null));
        var fourth = g.Factions[Fourth];
        Assert.Equal((Faction.Klaed, 2, Factions.PlayerColors[0]), (fourth.Race, fourth.Team, fourth.Color));
        Assert.True(Galaxies.Allies(g, Faction.Klaed, Faction.Nautolan));
        Assert.True(Galaxies.AtWar(g, Faction.Klaed, Fourth));
        // Its own race's price for each side, its own heroes.
        var home = g.Planets[Capital(g, Faction.Nautolan)];
        Assert.Equal(Ships.Cost(ShipClass.Frigate, Faction.Nautolan), Galaxies.BuildPrice(g, home, GalaxyBuild.Ship(ShipClass.Frigate)));
        Assert.True(Galaxies.CanBuild(g, Fourth, Capital(g, Fourth), GalaxyBuild.HeroOf(new HeroId(Faction.Klaed, HeroRole.Ace))).Reason != "Héros d'une autre faction");
        Assert.Equal("Héros d'une autre faction", Galaxies.CanBuild(g, Fourth, Capital(g, Fourth), GalaxyBuild.HeroOf(new HeroId(Faction.Nairan, HeroRole.Ace))).Reason);
        // No truce between allies: they need none.
        Assert.False(Galaxies.CanOfferTruce(g, Faction.Klaed, Faction.Nautolan).Ok);
        Assert.True(Galaxies.CanOfferTruce(g, Faction.Klaed, Faction.Nairan).Ok);
    }

    [Fact]
    public void Allied_fleets_share_planets_and_lanes_without_a_fight()
    {
        var g = TeamGalaxy();
        var home = Capital(g, Faction.Klaed);
        // A Nautolan fleet comes to the Kla'ed capital along the lane a Kla'ed fleet leaves by.
        var next = Galaxies.Neighbours(g, home).First();
        var ally = Galaxies.AddShipsAt(g, Faction.Nautolan, next, new ShipCounts { [ShipClass.Frigate] = 3 })!;
        var own = Galaxies.FleetsAt(g, home, Faction.Klaed)[0];
        Assert.True(Galaxies.SendFleet(g, ally.Id, home).Ok);
        Assert.True(Galaxies.SendFleet(g, own.Id, next).Ok);
        for (var t = 0.0; t < 120 && (ally.At is null || own.At is null); t += Galaxies.Step) Galaxies.StepGalaxy(g);
        Assert.Equal((home, next), (ally.At, own.At));
        // Docked at its ally's capital, it neither fights for it nor takes it.
        Galaxies.RunGalaxy(g, 60);
        Assert.Empty(g.Battles);
        Assert.DoesNotContain(g.Events, e => e is BattleStarted);
        Assert.Equal(Faction.Klaed, g.Planets[home].Owner);
        Assert.Null(g.Planets[home].Occupation);
        Assert.True(Galaxies.Supplied(g, ally));
        // A foe's fleet there fights at once, against the planet's owner alone.
        Galaxies.AddShipsAt(g, Fourth, home, new ShipCounts { [ShipClass.Frigate] = 1 });
        Galaxies.StepGalaxy(g);
        var b = Assert.Single(g.Battles);
        Assert.Equal((Fourth, (Side)Faction.Klaed), (b.Attacker, b.Defender));
        Assert.Null(ally.BattleId);
    }

    [Fact]
    public void Allies_see_the_galaxy_together()
    {
        var g = TeamGalaxy();
        var nautolan = Galaxies.VisiblePlanets(g, Faction.Nautolan);
        Assert.Contains(Capital(g, Faction.Klaed), nautolan);
        Assert.DoesNotContain(Capital(g, Fourth), nautolan);
        Assert.True(Galaxies.FleetVisible(g, Faction.Nautolan, Galaxies.FleetsAt(g, Capital(g, Faction.Klaed))[0]));
    }

    [Fact]
    public void In_the_galaxy_a_team_wins_together()
    {
        var g = TeamGalaxy();
        // Team 2 loses every planet and every fleet.
        foreach (var p in g.Planets.Where(p => p.Owner is Side o && Galaxies.TeamOf(g, o) == 2)) p.Owner = Faction.Nautolan;
        foreach (var f in g.Fleets.Values.Where(f => Galaxies.TeamOf(g, f.Side) == 2).ToList()) g.Fleets.Remove(f.Id);
        Galaxies.StepGalaxy(g);
        Assert.True(g.Factions[Faction.Nairan].Eliminated && g.Factions[Fourth].Eliminated);
        Assert.Equal(Faction.Klaed, g.Winner);
        Assert.Contains(g.Events, e => e is GalaxyVictory v && v.Side == Faction.Klaed && v.Team == 1);
        Assert.True(Galaxies.Won(g, Faction.Nautolan));
        Assert.False(Galaxies.Won(g, Faction.Nairan));
    }

    [Fact]
    public void Galaxy_AIs_of_a_team_never_fight_each_other()
    {
        var g = TeamGalaxy(Difficulty.Normal);
        g.Player = new Side(99);
        // Judged on the teams themselves, not on the rule under test.
        bool Mates(Side a, Side b) => a == b || (g.Factions.TryGetValue(a, out var fa) && g.Factions.TryGetValue(b, out var fb) && fa.Team != 0 && fa.Team == fb.Team);
        var battles = new List<BattleEnded>();
        for (var minute = 0; minute < 15 && g.Winner is null; minute++)
        {
            Galaxies.RunGalaxy(g, 60);
            battles.AddRange(g.Events.OfType<BattleEnded>());
            foreach (var b in g.Battles) Assert.False(Mates(b.Attacker, b.Defender));
            g.Events.Clear();
        }
        Assert.NotEmpty(battles);
        Assert.All(battles, b => Assert.False(Mates(b.Attacker, b.Defender)));
    }

    [Fact]
    public void Old_saves_with_race_names_load_one_side_per_race()
    {
        // As saved before sides: race names where owners are, no race, team nor color.
        const string old = """
            {"version":1,"savedAt":1700000000000,"time":42,"player":"nairan","w":1300,"h":850,
             "planets":[
               {"x":100,"y":100,"id":0,"name":"Vesper","income":6,"mineSlots":3,"owner":"nairan","station":1,"mines":1},
               {"x":400,"y":120,"id":1,"name":"Orin","income":3,"mineSlots":2,"owner":null,"occupation":{"faction":"klaed","remaining":20}},
               {"x":700,"y":140,"id":2,"name":"Maru","income":6,"mineSlots":3,"owner":"klaed","station":1,"mines":1},
               {"x":400,"y":500,"id":3,"name":"Nadir","income":4,"mineSlots":2,"owner":"errant","station":3,"platforms":4,"platformTier":3}],
             "lanes":[{"a":0,"b":1},{"a":1,"b":2},{"a":1,"b":3}],
             "fleets":[
               {"id":4,"faction":"nairan","ships":{"frigate":2,"scout":2},"heroes":[],"at":0,"from":0,"came":0,"condition":1},
               {"id":5,"faction":"klaed","ships":{"scout":1},"heroes":["klaed-ace"],"at":1,"from":1,"came":2,"condition":0.8},
               {"id":6,"faction":"errant","ships":{"colossus":1},"heroes":[],"at":3,"from":3,"came":3,"condition":1}],
             "factions":[
               {"id":"nairan","credits":900,"research":{"weapons":1},"ai":null,"eliminated":false,"heroCooldowns":{}},
               {"id":"klaed","credits":500,"research":{},"ai":{"difficulty":"hard","grudges":{"nairan":30}},"eliminated":false,"heroCooldowns":{"klaed-admiral":12}}],
             "nextId":7,"seed":7,"winner":null,
             "truces":[{"a":"klaed","b":"nairan","remaining":100}],
             "offers":[{"from":"nairan","to":"klaed","remaining":10}]}
            """;
        var g = GalaxySave.Deserialize(old)!;
        Assert.NotNull(g);
        Assert.Equal(Faction.Nairan, g.Player);
        Assert.Equal(new Side[] { Faction.Nairan, Faction.Klaed }, g.Factions.Keys);
        foreach (var f in g.Factions.Values) Assert.Equal(((Faction)f.Id.Id, 0, Factions.Defs[(Faction)f.Id.Id].Color), (f.Race, f.Team, f.Color));
        Assert.Equal(new Side?[] { Faction.Nairan, null, Faction.Klaed, Faction.Errant }, g.Planets.Select(p => p.Owner));
        Assert.Equal(Faction.Klaed, g.Planets[1].Occupation!.Side);
        Assert.Equal(new Side[] { Faction.Nairan, Faction.Klaed, Faction.Errant }, g.Fleets.Values.Select(f => f.Side));
        Assert.Equal(30, g.Factions[Faction.Klaed].AI!.Grudges[Faction.Nairan]);
        Assert.Equal((Faction.Klaed, Faction.Nairan), (g.Truces[0].A, g.Truces[0].B));
        Assert.Equal(Faction.Klaed, g.Offers[0].To);
        Assert.Equal(1, g.Factions[Faction.Nairan].Research[TechId.Weapons]);
        // It plays on, and saves in the new form.
        Galaxies.RunGalaxy(g, 5);
        var again = GalaxySave.Deserialize(GalaxySave.Serialize(g))!;
        Assert.Equal(g.Factions.Keys, again.Factions.Keys);
        Assert.Equal(g.Planets.Select(p => p.Owner), again.Planets.Select(p => p.Owner));
        var path = Path.Combine(Path.GetTempPath(), $"sides-old-save-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, old);
            Assert.Equal(new SaveInfo(1700000000000, Faction.Nairan, Faction.Nairan, 1), GalaxySave.Info(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void New_saves_keep_extra_sides_their_race_team_and_color()
    {
        var g = TeamGalaxy();
        g.Factions[Fourth].Color = 0xabcdef;
        var json = GalaxySave.Serialize(g);
        // Sides are written as numbers.
        Assert.Equal(4, JsonNode.Parse(json)!["factions"]![3]!["id"]!.GetValue<int>());
        var back = GalaxySave.Deserialize(json)!;
        var fourth = back.Factions[Fourth];
        Assert.Equal((Faction.Klaed, 2, 0xabcdefu), (fourth.Race, fourth.Team, fourth.Color));
        Assert.Equal(g.Planets.Select(p => p.Owner), back.Planets.Select(p => p.Owner));
        Assert.Equal(g.Fleets.Values.Select(f => f.Side), back.Fleets.Values.Select(f => f.Side));
    }
}
