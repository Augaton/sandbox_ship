// Port of tests/variety.test.ts.
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.AbilitySystem;
using static SandboxShip.Sim.Steps;
using static SandboxShip.Sim.Structures;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim.Tests;

public class VarietyTests
{
    static World NewWorld(double credits = 100000)
    {
        var w = CreateWorld(6000, 4000);
        AddFaction(w, Faction.Klaed, credits);
        AddFaction(w, Faction.Nairan, credits);
        AddFaction(w, Faction.Nautolan, credits);
        return w;
    }

    static List<Entity> ShipsOf(World w, string type) => w.Entities.Where(e => e.Kind == EntityKind.Ship && e.Type == type).ToList();

    static void PlacementFails(Placement p, string reason)
    {
        Assert.False(p.Ok);
        Assert.Equal(reason, p.Reason);
    }

    static double Distance(IPoint a, IPoint b) => Hypot(a.X - b.X, a.Y - b.Y);

    // Two production queues.

    [Fact]
    public void Build_ships_while_the_station_upgrades()
    {
        var w = NewWorld();
        SpawnStation(w, Faction.Klaed, 700, 2000);
        Assert.True(EnqueueUpgrade(w, Faction.Klaed).Ok);
        Assert.True(EnqueueShip(w, Faction.Klaed, ShipClass.Scout).Ok);
        Run(w, Ships.Defs[ShipClass.Scout].BuildTime + 0.5);
        Assert.Equal(Ships.Defs[ShipClass.Scout].Squad!.Value, ShipsOf(w, "scout").Count);
        var f = w.Factions[Faction.Klaed];
        Assert.Single(f.BuildQueue);
        Run(w, Station.Levels[1].UpgradeTime);
        Assert.Empty(f.BuildQueue);
    }

    [Fact]
    public void A_construction_site_can_be_called_off_for_a_full_refund()
    {
        var w = NewWorld(1000);
        SpawnStation(w, Faction.Klaed, 700, 2000);
        PlaceStructure(w, Faction.Klaed, BuildableType.Platform, 1000, 2300);
        var site = w.Entities.First(e => e.Type == "platform");
        CancelConstruction(w, Faction.Klaed, site.Id);
        Assert.False(w.Entities.Has(site.Id));
        Expect.Close(1000, w.Factions[Faction.Klaed].Credits, 0);
    }

    // Research.

    [Fact]
    public void Research_costs_credits_takes_time_and_needs_a_matching_station_level()
    {
        var w = NewWorld();
        SpawnStation(w, Faction.Klaed, 700, 2000);
        SpawnStructure(w, Faction.Klaed, BuildableType.Lab, 900, 2400);
        var f = w.Factions[Faction.Klaed];
        Assert.True(StartResearch(w, Faction.Klaed, TechId.Weapons).Ok);
        Assert.Equal(100000 - Techs.Defs[TechId.Weapons].Costs[0], f.Credits);
        // Its next level would need a level 2 station, even while level 1 is still underway.
        Assert.Equal(Result.Fail("Station niveau 2 requise"), CanResearch(w, Faction.Klaed, TechId.Weapons));
        Run(w, Techs.Defs[TechId.Weapons].Times[0] + 0.5);
        Assert.Equal(1, f.Research[TechId.Weapons]);
        Assert.Equal(Result.Fail("Station niveau 2 requise"), CanResearch(w, Faction.Klaed, TechId.Weapons));
    }

    [Fact]
    public void Research_queues_levels_one_after_the_other_each_paid_up_front()
    {
        var w = NewWorld();
        SpawnStation(w, Faction.Klaed, 700, 2000, 3);
        SpawnStructure(w, Faction.Klaed, BuildableType.Lab, 900, 2400);
        var f = w.Factions[Faction.Klaed];
        var weapons = Techs.Defs[TechId.Weapons];
        var armor = Techs.Defs[TechId.Armor];
        Assert.True(StartResearch(w, Faction.Klaed, TechId.Weapons).Ok);
        Assert.True(StartResearch(w, Faction.Klaed, TechId.Weapons).Ok);
        Assert.True(StartResearch(w, Faction.Klaed, TechId.Armor).Ok);
        Assert.Equal(100000 - weapons.Costs[0] - weapons.Costs[1] - armor.Costs[0], f.Credits);
        Run(w, weapons.Times[0] + weapons.Times[1] + armor.Times[0] + 1);
        Assert.Equal(2, f.Research[TechId.Weapons]);
        Assert.Equal(1, f.Research[TechId.Armor]);
        Assert.Null(f.Researching);
    }

    [Fact]
    public void Cancelling_a_level_takes_back_the_later_levels_of_that_tech_and_refunds_them_all()
    {
        var w = NewWorld();
        SpawnStation(w, Faction.Klaed, 700, 2000, 3);
        SpawnStructure(w, Faction.Klaed, BuildableType.Lab, 900, 2400);
        var f = w.Factions[Faction.Klaed];
        StartResearch(w, Faction.Klaed, TechId.Weapons);
        StartResearch(w, Faction.Klaed, TechId.Armor);
        StartResearch(w, Faction.Klaed, TechId.Weapons);
        CancelResearch(w, Faction.Klaed, 0);
        Assert.Equal(100000 - Techs.Defs[TechId.Armor].Costs[0], f.Credits);
        Assert.Equal(TechId.Armor, f.Researching?.Tech);
        Assert.Empty(f.ResearchQueue);
    }

    [Fact]
    public void Armor_research_thickens_the_hulls_already_in_service()
    {
        var w = NewWorld();
        SpawnStation(w, Faction.Klaed, 700, 2000);
        SpawnStructure(w, Faction.Klaed, BuildableType.Lab, 900, 2400);
        var s = SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 1500, 2000);
        var before = s.MaxHull;
        StartResearch(w, Faction.Klaed, TechId.Armor);
        Run(w, Techs.Defs[TechId.Armor].Times[0] + 0.5);
        Expect.Close(before * 1.1, s.MaxHull, 5);
        Expect.Close(before * 1.1, SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 1500, 2100).MaxHull, 5);
    }

    [Fact]
    public void Weapons_research_hits_harder()
    {
        var w = NewWorld();
        var target = SpawnShip(w, Faction.Nairan, ShipClass.Battlecruiser, 1300, 1000);
        var a = SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 1000, 1000);
        w.Factions[Faction.Klaed].Research[TechId.Weapons] = 2;
        StepWorld(w);
        target.Pilot = new Pilot();
        a.Pilot = new Pilot { Firing = true, AimX = target.X, AimY = target.Y };
        Run(w, 0.4);
        var shot = w.Projectiles.First(p => p.Faction == Faction.Klaed);
        // Base damage (flak 4, laser 16) x Kla'ed trait x two levels of weapons research.
        Expect.Close((shot.Kind == WeaponKind.Laser ? 16 : 4) * Factions.Trait(Faction.Klaed).Damage * 1.2, shot.Damage, 5);
    }

    // Factions.

    [Fact]
    public void Factions_have_their_own_strengths()
    {
        var w = NewWorld();
        AddFaction(w, Faction.Nautolan, 0);
        var k = SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 0, 0);
        var n = SpawnShip(w, Faction.Nairan, ShipClass.Frigate, 0, 200);
        var t = SpawnShip(w, Faction.Nautolan, ShipClass.Frigate, 0, 400);
        Expect.Greater(n.MaxShield, k.MaxShield);
        Expect.Less(n.MaxHull, k.MaxHull);
        Expect.Less(Ships.Cost(ShipClass.Frigate, Faction.Nautolan), Ships.Cost(ShipClass.Frigate, Faction.Klaed));
        t.Hull = t.MaxHull / 2;
        Run(w, 10);
        Expect.Close(t.MaxHull * 0.6, t.Hull, 0);
    }

    // Abilities.

    [Fact]
    public void Abilities_go_on_cooldown_once_used()
    {
        var w = NewWorld();
        var s = SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 1000, 1000);
        Assert.True(UseAbility(w, s).Ok);
        Assert.False(UseAbility(w, s).Ok);
        Run(w, Abilities.Defs[AbilityId.Overcharge].Cooldown + 0.1);
        Assert.True(UseAbility(w, s).Ok);
    }

    [Fact]
    public void Emergency_repair_mends_nearby_allies()
    {
        var w = NewWorld();
        var support = SpawnShip(w, Faction.Klaed, ShipClass.Support, 1000, 1000);
        var hurt = SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 1100, 1000);
        hurt.Hull = hurt.MaxHull * 0.3;
        UseAbility(w, support);
        Expect.Close(hurt.MaxHull * 0.55, hurt.Hull, 5);
    }

    [Fact]
    public void Afterburner_doubles_speed()
    {
        var w = NewWorld();
        var a = SpawnShip(w, Faction.Klaed, ShipClass.Scout, 1000, 1000);
        var b = SpawnShip(w, Faction.Klaed, ShipClass.Scout, 1000, 2000);
        foreach (var s in new[] { a, b }) s.Pilot = new Pilot { Thrust = 1 };
        UseAbility(w, b);
        Run(w, 3);
        Expect.Greater(b.X - 1000, (a.X - 1000) * 1.6);
    }

    [Fact]
    public void The_AI_uses_abilities_in_combat()
    {
        var w = NewWorld();
        w.Factions[Faction.Nairan].AutoAbilities = true;
        var torpedo = SpawnShip(w, Faction.Nairan, ShipClass.Torpedo, 1000, 1000);
        SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 1300, 1000);
        Run(w, 2);
        Expect.Greater(torpedo.Ability!.Cooldown, 0);
    }

    // New structures and capital ships.

    [Fact]
    public void A_repair_dock_mends_ships_around_it()
    {
        var w = NewWorld();
        SpawnStructure(w, Faction.Klaed, BuildableType.RepairDock, 1000, 1000);
        var s = SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 1150, 1000);
        s.Hull = 100;
        Run(w, 5);
        Expect.Close(100 + Buildables.Defs[BuildableType.RepairDock].Repair!.Rate * 5, s.Hull, 0);
    }

    [Fact]
    public void The_ion_cannon_needs_a_level_3_station()
    {
        var w = NewWorld();
        SpawnStation(w, Faction.Klaed, 700, 2000);
        PlacementFails(CheckPlacement(w, Faction.Klaed, BuildableType.IonCannon, 1000, 2300), "Station niveau 3 requise");
    }

    [Fact]
    public void Capital_ships_close_in_on_enemies_even_though_their_guns_sit_on_turrets()
    {
        var w = NewWorld();
        var bc = SpawnShip(w, Faction.Klaed, ShipClass.Battlecruiser, 1000, 1000);
        SpawnShip(w, Faction.Nairan, ShipClass.Frigate, 1600, 1000);
        Run(w, 1);
        Assert.NotNull(bc.TargetId);
        Expect.Greater(bc.Speed, 0);
    }

    // Build zones and structure levels.

    [Fact]
    public void Platforms_and_docks_may_go_near_a_built_mine_far_from_the_station()
    {
        var w = NewWorld();
        SpawnStation(w, Faction.Klaed, 700, 2000);
        var mine = SpawnStructure(w, Faction.Klaed, BuildableType.Mine, 3000, 1000);
        Assert.True(CheckPlacement(w, Faction.Klaed, BuildableType.Platform, 3200, 1000).Ok);
        Assert.True(CheckPlacement(w, Faction.Klaed, BuildableType.RepairDock, 3200, 1100).Ok);
        Assert.False(CheckPlacement(w, Faction.Klaed, BuildableType.IonCannon, 3200, 1000).Ok);
        mine.Build = 0.5;
        PlacementFails(CheckPlacement(w, Faction.Klaed, BuildableType.Platform, 3200, 1000), "Trop loin de la station, des avant-postes et des mines");
    }

    [Fact]
    public void A_platform_goes_up_two_levels_through_the_buildings_queue()
    {
        var w = NewWorld();
        SpawnStation(w, Faction.Klaed, 700, 2000);
        var p = SpawnStructure(w, Faction.Klaed, BuildableType.Platform, 1000, 2300);
        var tiers = Buildables.Defs[BuildableType.Platform].Tiers!;
        Assert.True(UpgradeStructure(w, Faction.Klaed, p.Id).Ok);
        Assert.True(UpgradeStructure(w, Faction.Klaed, p.Id).Ok);
        Assert.Equal(Result.Fail("Niveau maximum"), UpgradeStructure(w, Faction.Klaed, p.Id));
        Run(w, tiers[0].Time + tiers[1].Time + 1);
        Assert.Equal(3, p.Level);
        Expect.Close(tiers[1].Hull * 1, p.MaxHull, 5);
        Assert.Equal(3, p.Weapons.Count);
    }

    [Fact]
    public void An_outpost_goes_where_a_ship_holds_the_spot_away_from_any_fight_and_opens_a_build_area()
    {
        var w = NewWorld();
        SpawnStation(w, Faction.Klaed, 700, 2000);
        PlacementFails(CheckPlacement(w, Faction.Klaed, BuildableType.Outpost, 3500, 2000), "Un vaisseau doit tenir la position");
        SpawnShip(w, Faction.Klaed, ShipClass.Scout, 3600, 2000);
        var raider = SpawnShip(w, Faction.Nairan, ShipClass.Scout, 3900, 2200);
        PlacementFails(CheckPlacement(w, Faction.Klaed, BuildableType.Outpost, 3500, 2000), "Combat trop proche");
        w.Entities.Remove(raider.Id);
        Assert.True(PlaceStructure(w, Faction.Klaed, BuildableType.Outpost, 3500, 2000).Ok);
        var outpost = w.Entities.First(e => e.Type == "outpost");
        // Only once built does it open its area.
        Assert.False(CheckPlacement(w, Faction.Klaed, BuildableType.Platform, 3800, 2000).Ok);
        Run(w, Buildables.Defs[BuildableType.Outpost].BuildTime + 0.5);
        Assert.True(CheckPlacement(w, Faction.Klaed, BuildableType.Platform, 3800, 2000).Ok);
        Assert.False(CheckPlacement(w, Faction.Klaed, BuildableType.Platform, 4100, 2000).Ok);
        // Its next level widens the area.
        ApplyTier(w, outpost);
        Assert.True(CheckPlacement(w, Faction.Klaed, BuildableType.Platform, 4050, 2000).Ok);
    }

    [Fact]
    public void A_field_inside_an_outpost_area_needs_no_escort()
    {
        var w = NewWorld();
        SpawnStation(w, Faction.Klaed, 700, 2000);
        SpawnStructure(w, Faction.Klaed, BuildableType.Outpost, 3500, 2000);
        var field = AddField(w, 3800, 2000, 120);
        Assert.True(CheckPlacement(w, Faction.Klaed, BuildableType.Mine, field.X, field.Y).Ok);
    }

    // Random skirmish maps.

    [Fact]
    public void Random_maps_depend_on_the_seed_and_keep_fields_clear_of_the_stations()
    {
        static World Map(int seed) => Scenario.CreateSkirmish(new SkirmishOptions { Player = Faction.Klaed, Enemies = [Faction.Nairan], EnemyAI = false, Seed = seed });
        static List<(int, double, double, double, int?)> Fields(World w) => w.Fields.Select(f => (f.Id, f.X, f.Y, f.Radius, f.MineId)).ToList();
        var a = Map(5);
        var b = Map(5);
        var c = Map(6);
        Assert.Equal(Fields(a), Fields(b));
        Assert.NotEqual(Fields(a), Fields(c));
        Entity[] stations = [StationOf(a, Faction.Klaed)!, StationOf(a, Faction.Nairan)!];
        foreach (var f in a.Fields)
        {
            foreach (var st in stations) Expect.Greater(Distance(f, st), 300);
        }
        // Every station has two fields right behind it.
        foreach (var st in stations) Assert.True(a.Fields.Count(f => Distance(f, st) < 450) >= 2);
    }

    // Heroes.

    [Fact]
    public void Heroes_are_unique_tied_to_their_faction_and_station_level_and_come_back_after_a_delay()
    {
        var w = NewWorld();
        var station = SpawnStation(w, Faction.Klaed, 700, 2000);
        var ace = new HeroId(Faction.Klaed, HeroRole.Ace);
        Assert.Equal(Result.Fail("Héros d'une autre faction"), CanBuildHero(w, Faction.Klaed, new HeroId(Faction.Nairan, HeroRole.Ace)));
        Assert.Equal(Result.Fail("Station niveau 3 requise"), CanBuildHero(w, Faction.Klaed, new HeroId(Faction.Klaed, HeroRole.Admiral)));
        Assert.True(EnqueueHero(w, Faction.Klaed, ace).Ok);
        Assert.Equal(Result.Fail("Déjà en service"), CanBuildHero(w, Faction.Klaed, ace));
        Run(w, Heroes.Defs[ace].BuildTime + 0.5);
        var kesh = w.Entities.First(e => e.Hero == ace);
        Expect.Close(Ships.Defs[ShipClass.Scout].Hull * Heroes.Defs[ace].Hull, kesh.MaxHull, 5);
        kesh.Hull = 0;
        StepWorld(w);
        Assert.False(CanBuildHero(w, Faction.Klaed, ace).Ok);
        Run(w, Heroes.Defs[ace].Respawn + 1);
        Assert.True(CanBuildHero(w, Faction.Klaed, ace).Ok);
        Assert.Equal(1, station.Level);
    }

    // Fog of war.

    [Fact]
    public void Nothing_out_of_sight_is_targeted_and_structures_once_seen_are_remembered()
    {
        var w = NewWorld();
        w.Fog = true;
        // Long guns, short eyes: the ion cannon outranges what a frigate sees.
        var watcher = SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 1000, 1000);
        var far = SpawnStructure(w, Faction.Nairan, BuildableType.Mine, 2600, 1000);
        Run(w, 1);
        Assert.False(Vision.IsVisible(w, Faction.Klaed, far));
        Assert.Null(watcher.TargetId);
        watcher.X = 2000;
        Run(w, 0.5);
        Assert.True(Vision.IsVisible(w, Faction.Klaed, far));
        watcher.X = 1000;
        watcher.Order = null;
        watcher.GuardX = 1000;
        Run(w, 0.5);
        Assert.False(Vision.IsVisible(w, Faction.Klaed, far));
        Assert.True(Vision.WasSeen(w, Faction.Klaed, far));
    }

    // Free-for-all and map sizes.

    [Fact]
    public void Three_factions_each_get_a_station_and_two_fields_behind_it_on_maps_of_three_sizes()
    {
        foreach (var size in new[] { MapSize.Small, MapSize.Medium, MapSize.Large })
        {
            var w = Scenario.CreateSkirmish(new SkirmishOptions
            {
                Player = Faction.Klaed, Enemies = [Faction.Nairan, Faction.Nautolan], EnemyAI = true, Seed = 9, Size = size,
            });
            Assert.Equal(Scenario.MapSizes[size].W, w.Width);
            foreach (var f in new[] { Faction.Klaed, Faction.Nairan, Faction.Nautolan })
            {
                var st = StationOf(w, f);
                Assert.NotNull(st);
                Assert.True(w.Fields.Count(fl => Distance(fl, st) < 450) >= 2);
            }
        }
    }

    [Fact]
    public void Three_AIs_play_on_without_breaking()
    {
        var w = Scenario.CreateSkirmish(new SkirmishOptions
        {
            Player = Faction.Klaed, Enemies = [Faction.Nairan, Faction.Nautolan], EnemyAI = true, Seed = 4, Size = MapSize.Small,
        });
        w.Factions[Faction.Klaed].AI = NewAI();
        Run(w, 240, 1.0 / 30);
        foreach (var e in w.Entities) Assert.True(double.IsFinite(e.X) && double.IsFinite(e.Hull));
    }

    // Squadrons and retreats.

    [Fact]
    public void A_bomber_order_delivers_a_squadron_hangar_craft_never_leave_the_battle()
    {
        var w = NewWorld();
        SpawnStation(w, Faction.Klaed, 700, 2000);
        var squad = Ships.Defs[ShipClass.Bomber].Squad!.Value;
        Assert.True(EnqueueShip(w, Faction.Klaed, ShipClass.Bomber).Ok);
        Run(w, Ships.Defs[ShipClass.Bomber].BuildTime + 0.5);
        Assert.Equal(squad, ShipsOf(w, "bomber").Count(e => !e.Launched));
        Run(w, 20);
        Assert.All(ShipsOf(w, "fighter"), e => Assert.True(e.Launched));
        Assert.Equal(new ShipCounts { [ShipClass.Bomber] = squad }, Battle.Survivors(w, Faction.Klaed));
    }

    [Fact]
    public void The_AI_pulls_a_badly_damaged_ship_back_to_its_repair_dock()
    {
        var w = NewWorld();
        var f = w.Factions[Faction.Nairan];
        f.AI = NewAI();
        SpawnStation(w, Faction.Nairan, 5000, 2000);
        var dock = SpawnStructure(w, Faction.Nairan, BuildableType.RepairDock, 4600, 2000);
        var bc = SpawnShip(w, Faction.Nairan, ShipClass.Battlecruiser, 2000, 2000);
        bc.Hull = bc.MaxHull * 0.2;
        Run(w, 2);
        Assert.True(bc.Retreating);
        Run(w, 60);
        Expect.Less(Hypot(bc.X - dock.X, bc.Y - dock.Y), 400);
    }

    // Not in the web version: research needs a lab.

    [Fact]
    public void Research_needs_a_lab_and_waits_while_it_is_down()
    {
        var w = NewWorld();
        SpawnStation(w, Faction.Klaed, 700, 2000);
        Assert.Equal(Result.Fail("Laboratoire requis"), StartResearch(w, Faction.Klaed, TechId.Weapons));
        var lab = SpawnStructure(w, Faction.Klaed, BuildableType.Lab, 900, 2400);
        Assert.True(StartResearch(w, Faction.Klaed, TechId.Weapons).Ok);
        lab.Hull = 0;
        Run(w, Techs.Defs[TechId.Weapons].Times[0] + 1);
        Assert.Equal(0, w.Factions[Faction.Klaed].Research[TechId.Weapons]);
        SpawnStructure(w, Faction.Klaed, BuildableType.Lab, 900, 2400);
        Run(w, Techs.Defs[TechId.Weapons].Times[0] + 1);
        Assert.Equal(1, w.Factions[Faction.Klaed].Research[TechId.Weapons]);
    }
}
