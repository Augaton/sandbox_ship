// What a battle AI decides, and what each level of difficulty decides differently: it goes on
// producing whatever it cannot have, it defends all it owns, and Easy, Normal and Hard differ by
// what they do, not only by what they earn.
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.Steps;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim.Tests;

public class AiLevelsTests
{
    static readonly Side Player = Lobby.SideOf(0), Bot = Lobby.SideOf(1);

    /// <summary>A passive player against an AI of this level.</summary>
    static World Game(int seed, Difficulty level = Difficulty.Normal, Personality personality = Personality.Balanced, Faction race = Faction.Klaed) =>
        Scenario.CreateSkirmish(new SkirmishOptions
        {
            Slots = [new(Player, race), new(Bot, race, 0, level, null, personality)],
            Seed = seed,
        });

    static void RaiseStation(World w, Side side, int level)
    {
        var st = StationOf(w, side)!;
        while (st.Level < level)
        {
            var next = Station.Levels[st.Level];
            st.Level++;
            AddHardpoints(w, st, next.Hardpoints);
        }
    }

    /// <summary>A fleet of this class parked around a spot, holding fire: only there to be seen.</summary>
    static List<Entity> Parked(World w, Side side, ShipClass cls, int n, double x, double y)
    {
        var ships = new List<Entity>();
        for (var i = 0; i < n; i++)
        {
            var e = SpawnShip(w, side, cls, x + i % 4 * 90, y + i / 4 * 90);
            e.Stance = Stance.Passive;
            e.Hull = e.MaxHull = 1e6;
            ships.Add(e);
        }
        return ships;
    }

    /// <summary>A built mine on the nearest field at least this far from its station.</summary>
    static Entity FarMine(World w, Side side, double beyond)
    {
        var st = StationOf(w, side)!;
        var far = w.Fields.Where(fl => Dist(fl, st) > beyond && fl.MineId is null).OrderBy(fl => Dist(fl, st)).First();
        var m = SpawnStructure(w, side, BuildableType.Mine, far.X, far.Y);
        far.MineId = m.Id;
        m.FieldId = far.Id;
        return m;
    }

    static (Entity Station, double Bearing) Home(World w)
    {
        var st = StationOf(w, Bot)!;
        var enemy = StationOf(w, Player)!;
        return (st, Math.Atan2(enemy.Y - st.Y, enemy.X - st.X));
    }

    // It goes on producing.

    [Fact]
    public void Outmatched_with_every_place_around_its_station_taken_it_goes_on_building_ships()
    {
        var w = Game(1);
        var f = w.Factions[Bot];
        RaiseStation(w, Bot, 3);
        var (st, b) = Home(w);
        f.Credits = 20000;
        foreach (var fl in w.Fields.OrderBy(fl => Dist(fl, st)).Take(4))
        {
            var m = SpawnStructure(w, Bot, BuildableType.Mine, fl.X, fl.Y);
            fl.MineId = m.Id;
            m.FieldId = fl.Id;
        }
        foreach (var turn in new[] { -0.8, 0.0, 0.8 })
            SpawnStructure(w, Bot, BuildableType.Platform, st.X + Math.Cos(b + turn) * 350, st.Y + Math.Sin(b + turn) * 350);
        for (var i = 0; i < 3; i++) SpawnShip(w, Bot, ShipClass.Frigate, st.X + Math.Cos(b) * 220 + i * 70, st.Y + Math.Sin(b) * 220);
        // Far enough not to count as a threat, near enough to be seen by the station.
        Parked(w, Player, ShipClass.Frigate, 12, st.X + Math.Cos(b) * 1050, st.Y + Math.Sin(b) * 1050);
        Run(w, 120, 1.0 / 30);
        // It used to ask for a platform it had no way to place, at every thought, and nothing else.
        Expect.Greater(f.Stats.Built, 2);
    }

    [Fact]
    public void A_place_it_cannot_find_leaves_that_kind_of_goal_alone_for_a_while()
    {
        var w = Game(2);
        var f = w.Factions[Bot];
        var st = StationOf(w, Bot)!;
        foreach (var turn in new[] { 0, 2, 4 }) SpawnStructure(w, Bot, BuildableType.Platform, st.X + Math.Cos(turn) * 300, st.Y + Math.Sin(turn) * 300);
        f.Credits = 5000;
        f.AI!.Goal = new AIGoal(GoalKind.Platform);
        // Every zone full: no spot anywhere.
        foreach (var fl in w.Fields) fl.MineId = -1;
        Assert.Equal(0, Structures.RoomIn(w, Bot, new Circle(st.X, st.Y, Station.ConstructionRadius)));
        Run(w, 3, 1.0 / 30);
        Assert.True(f.AI.Goal?.Kind != GoalKind.Platform);
        Expect.Greater(f.AI.GaveUp[GoalKind.Platform], w.Time);
    }

    [Fact]
    public void A_hero_too_dear_is_saved_for_while_the_shipyard_goes_on_working_and_is_bought_in_the_end()
    {
        var w = Game(3);
        var f = w.Factions[Bot];
        RaiseStation(w, Bot, 3);
        var admiral = new HeroId(Faction.Klaed, HeroRole.Admiral);
        f.Credits = 3000;
        f.AI!.WaveValue = 1e9;
        f.AI.Big = new AIGoal(GoalKind.Hero, Hero: admiral);
        Run(w, 90, 1.0 / 30);
        // It used to wait for the whole price, with nothing else bought.
        Expect.Greater(f.Stats.Built, 2);
        Expect.Greater(f.AI.BigSaved, 0);
        Assert.DoesNotContain(w.Entities, e => e.Hero == admiral);
        // The savings make the price in the end, ships or no ships.
        f.AI.BigSaved = Heroes.Defs[admiral].Cost;
        f.AI.Saved = 0;
        f.Credits = Heroes.Defs[admiral].Cost + 100;
        f.Queue.Clear();
        Run(w, Heroes.Defs[admiral].BuildTime + 5, 1.0 / 30);
        Assert.Contains(w.Entities, e => e.Hero == admiral && e.Side == Bot);
        Assert.False(f.AI.Big?.Hero == admiral);
    }

    [Fact]
    public void A_hero_counts_for_what_it_costs_in_what_the_AI_weighs()
    {
        var w = Game(4);
        var f = w.Factions[Bot];
        var (st, b) = Home(w);
        var id = new HeroId(Faction.Klaed, HeroRole.Admiral);
        var hero = SpawnHero(w, Player, id, st.X + Math.Cos(b) * 1050, st.Y + Math.Sin(b) * 1050, b + Math.PI);
        hero.Stance = Stance.Passive;
        Run(w, 3, 1.0 / 30);
        Assert.Equal(Heroes.Defs[id].Cost, f.AI!.Intel.Army);
    }

    // It defends all it owns.

    static (World W, FactionState F, Entity Mine, List<Entity> Home, List<Entity> Raid) RaidOnAMine(int seed, Difficulty level, int raiders = 3)
    {
        var w = Game(seed, level);
        var f = w.Factions[Bot];
        var st = StationOf(w, Bot)!;
        f.AI!.WaveValue = 1e9;
        f.AI.NextRaid = f.AI.NextScout = 1e9;
        var mine = FarMine(w, Bot, 1200);
        var home = Enumerable.Range(0, 6).Select(i => SpawnShip(w, Bot, ShipClass.Frigate, st.X + 300 + i * 70, st.Y)).ToList();
        Run(w, 5, 1.0 / 30);
        var a = Math.Atan2(st.Y - mine.Y, st.X - mine.X);
        var raid = Enumerable.Range(0, raiders).Select(i => SpawnShip(w, Player, ShipClass.Frigate, mine.X - Math.Cos(a) * 200 + i * 70, mine.Y - Math.Sin(a) * 200)).ToList();
        Movement.IssueAttack(w, raid.Select(e => e.Id), mine.Id);
        return (w, f, mine, home, raid);
    }

    [Fact]
    public void The_home_fleet_answers_a_raid_on_a_far_mine_and_comes_back()
    {
        var (w, f, _, home, raid) = RaidOnAMine(1, Difficulty.Normal);
        var st = StationOf(w, Bot)!;
        var closest = double.PositiveInfinity;
        for (var t = 0; t < 120; t++)
        {
            Run(w, 1, 1.0 / 30);
            foreach (var e in home)
            {
                foreach (var r in raid) if (w.Entities.Has(e.Id) && w.Entities.Has(r.Id)) closest = Math.Min(closest, Dist(e, r));
            }
        }
        Expect.Less(closest, 400);
        Assert.Equal(0, raid.Count(e => w.Entities.Has(e.Id)));
        Run(w, 60, 1.0 / 30);
        // Those that were sent are back around the rally point.
        foreach (var e in home.Where(e => w.Entities.Has(e.Id))) Expect.Less(Dist(e, st), Station.ConstructionRadius * 2 + 50);
        Assert.True(f.AI!.LastThreat >= 0);
    }

    [Fact]
    public void Too_strong_a_foe_by_a_far_mine_draws_no_fleet_out()
    {
        var w = Game(2);
        var f = w.Factions[Bot];
        f.AI!.WaveValue = 1e9;
        var st = StationOf(w, Bot)!;
        var mine = FarMine(w, Bot, 1200);
        var home = Enumerable.Range(0, 3).Select(i => SpawnShip(w, Bot, ShipClass.Frigate, st.X + 300 + i * 70, st.Y)).ToList();
        // The mine sees it, it comes no closer.
        Parked(w, Player, ShipClass.Bulwark, 8, mine.X + 300, mine.Y);
        Run(w, 30, 1.0 / 30);
        foreach (var e in home) Assert.False(e.Order?.Type == OrderType.AttackMove);
    }

    // Past its all-in.

    [Fact]
    public void Past_its_all_in_it_goes_with_less_when_it_sees_little_to_stop_it()
    {
        bool Goes(int bulwarks)
        {
            var w = Game(6);
            var f = w.Factions[Bot];
            var (st, b) = Home(w);
            f.AI!.WaveValue = 1e9;
            Parked(w, Player, ShipClass.Bulwark, bulwarks, st.X + Math.Cos(b) * 1000, st.Y + Math.Sin(b) * 1000);
            Run(w, 2, 1.0 / 30);
            // Worth more than a reinforcement, less than the whole army it waits for.
            var ships = Enumerable.Range(0, 6).Select(i => SpawnShip(w, Bot, ShipClass.Frigate, st.X + 200 + i * 40, st.Y)).ToList();
            f.AI.WaveValue = 12000;
            w.Time = Personalities.Defs[Personality.Balanced].AllInTime;
            Run(w, 3, 1.0 / 30);
            return ships.Exists(e => e.Order?.Type == OrderType.AttackMove);
        }
        Assert.True(Goes(0));
        Assert.False(Goes(3));
    }

    // Easy.

    [Fact]
    public void Easy_leaves_a_far_mine_to_its_fate()
    {
        var (w, _, mine, home, raid) = RaidOnAMine(1, Difficulty.Easy);
        Run(w, 60, 1.0 / 30);
        Assert.False(w.Entities.Has(mine.Id));
        foreach (var e in home) Assert.NotEqual(OrderType.AttackMove, e.Order?.Type);
        Assert.Equal(3, raid.Count(e => w.Entities.Has(e.Id)));
    }

    [Fact]
    public void Easy_answers_a_threat_at_home_late()
    {
        double Answer(Difficulty level)
        {
            var w = Game(5, level);
            var f = w.Factions[Bot];
            f.AI!.WaveValue = 1e9;
            var st = StationOf(w, Bot)!;
            var home = Enumerable.Range(0, 4).Select(i => SpawnShip(w, Bot, ShipClass.Frigate, st.X + 300 + i * 70, st.Y)).ToList();
            Run(w, 3, 1.0 / 30);
            Parked(w, Player, ShipClass.Scout, 1, st.X - 500, st.Y);
            var start = w.Time;
            while (w.Time - start < 60 && !home.Exists(e => e.Order?.Type == OrderType.AttackMove)) Run(w, 0.5, 1.0 / 30);
            return w.Time - start;
        }
        Expect.Less(Answer(Difficulty.Normal), 3);
        Expect.Greater(Answer(Difficulty.Easy), Difficulties[Difficulty.Easy].Reaction - 1);
    }

    [Fact]
    public void Easy_builds_the_same_fleet_whatever_it_saw_and_Normal_does_not()
    {
        double Share(Difficulty level, ShipClass cls, Action<Intel> seen)
        {
            var w = Game(5, level);
            var f = w.Factions[Bot];
            var st = StationOf(w, Bot)!;
            RaiseStation(w, Bot, 3);
            seen(f.AI!.Intel);
            var n = 0;
            for (var i = 0; i < 2000; i++) if (Ai.PickShip(w, f, st) == cls) n++;
            return n / 2000.0;
        }
        void BigHulls(Intel i) => i.Large = 6;
        Assert.InRange(Share(Difficulty.Easy, ShipClass.Torpedo, BigHulls) - Share(Difficulty.Easy, ShipClass.Torpedo, _ => { }), -0.03, 0.03);
        Expect.Greater(Share(Difficulty.Normal, ShipClass.Torpedo, BigHulls), Share(Difficulty.Normal, ShipClass.Torpedo, _ => { }) + 0.1);
    }

    [Fact]
    public void Easy_sends_its_wave_at_a_stronger_foe_and_Normal_holds_it_back()
    {
        bool Sent(Difficulty level)
        {
            var w = Game(6, level);
            var f = w.Factions[Bot];
            var (st, b) = Home(w);
            f.AI!.WaveValue = 1e9;
            var ships = Enumerable.Range(0, 3).Select(i => SpawnShip(w, Bot, ShipClass.Frigate, st.X + 200 + i * 70, st.Y)).ToList();
            Parked(w, Player, ShipClass.Bulwark, 4, st.X + Math.Cos(b) * 1000, st.Y + Math.Sin(b) * 1000);
            Run(w, 3, 1.0 / 30);
            f.AI.WaveValue = 1000;
            Run(w, 6, 1.0 / 30);
            return ships.Exists(e => e.Order?.Type == OrderType.AttackMove);
        }
        Assert.True(Sent(Difficulty.Easy));
        Assert.False(Sent(Difficulty.Normal));
    }

    [Fact]
    public void Easy_lets_most_of_its_chances_to_use_an_ability_go_by()
    {
        int Uses(Difficulty level)
        {
            var w = Game(7, level);
            w.Factions[Bot].AI!.WaveValue = 1e9;
            var used = 0;
            var st = StationOf(w, Bot)!;
            var ship = SpawnShip(w, Bot, ShipClass.Frigate, st.X - 2000, st.Y);
            var foe = SpawnShip(w, Player, ShipClass.Frigate, st.X - 2300, st.Y);
            foreach (var e in new[] { ship, foe })
            {
                e.Stance = Stance.Hold;
                e.Hull = e.MaxHull = 1e7;
                foreach (var part in ChildrenOf(w, e.Id)) part.Hull = part.MaxHull = 1e7;
            }
            for (var t = 0; t < 600; t++)
            {
                // Its shields low: what the ability is for.
                ship.Shield = 0;
                Run(w, 1, 1.0 / 30);
                used += w.Events.OfType<AbilityUsed>().Count(u => u.EntityId == ship.Id);
                w.Events.Clear();
            }
            return used;
        }
        var normal = Uses(Difficulty.Normal);
        Expect.Greater(normal, 8);
        Expect.Less(Uses(Difficulty.Easy), normal * 0.7);
    }

    // Hard.

    [Fact]
    public void Hard_singles_out_the_ship_worth_bringing_down_and_its_whole_fleet_shoots_it()
    {
        List<int> Targets(Difficulty level, out int frail)
        {
            var w = Game(8, level);
            var f = w.Factions[Bot];
            f.AI!.WaveValue = 1e9;
            var st = StationOf(w, Bot)!;
            var (x, y) = (st.X - 2000, 2000.0);
            var ours = Enumerable.Range(0, 3).Select(i => SpawnShip(w, Bot, ShipClass.Frigate, x, y + i * 70)).ToList();
            foreach (var e in ours) e.Stance = Stance.Hold;
            // A frigate right in front of them, a support ship further: dearer for what it takes to bring down.
            SpawnShip(w, Player, ShipClass.Frigate, x - 190, y + 70).Stance = Stance.Passive;
            var support = SpawnShip(w, Player, ShipClass.Support, x - 300, y + 70);
            support.Stance = Stance.Passive;
            frail = support.Id;
            Run(w, 2, 1.0 / 30);
            return ours.Select(e => w.Entities.Get(e.TargetId) is { } t ? AbilitySystem.OwnerOf(w, t).Id : 0).ToList();
        }
        var hard = Targets(Difficulty.Hard, out var frail);
        Assert.All(hard, t => Assert.Equal(frail, t));
        var normal = Targets(Difficulty.Normal, out frail);
        Assert.DoesNotContain(frail, normal);
    }

    [Fact]
    public void Hard_goes_by_what_most_of_the_enemy_fleet_is()
    {
        double Share(Difficulty level, ShipClass cls, Action<Intel> seen)
        {
            var w = Game(5, level);
            var f = w.Factions[Bot];
            var st = StationOf(w, Bot)!;
            RaiseStation(w, Bot, 3);
            seen(f.AI!.Intel);
            var n = 0;
            for (var i = 0; i < 2000; i++) if (Ai.PickShip(w, f, st) == cls) n++;
            return n / 2000.0;
        }
        // Big hulls: more torpedoes than Normal would.
        Expect.Greater(Share(Difficulty.Hard, ShipClass.Torpedo, i => i.Large = 8), Share(Difficulty.Normal, ShipClass.Torpedo, i => i.Large = 8) + 0.03);
        // Swarms: almost no torpedoes, of no use against them.
        Expect.Less(Share(Difficulty.Hard, ShipClass.Torpedo, i => i.Small = 12), Share(Difficulty.Normal, ShipClass.Torpedo, i => i.Small = 12) * 0.5);
        Expect.Greater(Share(Difficulty.Hard, ShipClass.Frigate, i => i.Small = 12), Share(Difficulty.Normal, ShipClass.Frigate, i => i.Small = 12) + 0.03);
    }

    /// <summary>An AI's frigates idle at home, a foe's mine it has seen, nothing around it.</summary>
    static (World W, FactionState F, Entity Mine, List<Entity> Ships) MineInSight(Difficulty level)
    {
        var w = Game(9, level);
        var f = w.Factions[Bot];
        f.AI!.WaveValue = 1e9;
        var st = StationOf(w, Bot)!;
        var mine = FarMine(w, Player, 1200);
        var ships = Enumerable.Range(0, 3).Select(i => SpawnShip(w, Bot, ShipClass.Frigate, st.X + 200 + i * 70, st.Y)).ToList();
        // Its scout looks at the mine.
        var scout = SpawnShip(w, Bot, ShipClass.Scout, mine.X + 200, mine.Y);
        scout.Stance = Stance.Hold;
        return (w, f, mine, ships);
    }

    [Fact]
    public void Hard_raids_a_mine_it_has_seen_with_nothing_around_it()
    {
        var (w, _, mine, ships) = MineInSight(Difficulty.Hard);
        Run(w, 5, 1.0 / 30);
        var raiders = ships.Where(e => e.Order?.Type == OrderType.AttackMove).ToList();
        Assert.NotEmpty(raiders);
        Assert.All(raiders, e => Expect.Less(Hypot(e.Order!.X - mine.X, e.Order.Y - mine.Y), 300));
        var (n, _, _, others) = MineInSight(Difficulty.Normal);
        Run(n, 5, 1.0 / 30);
        Assert.DoesNotContain(others, e => e.Order?.Type == OrderType.AttackMove);
    }

    [Fact]
    public void Hard_sends_what_a_raid_calls_for_and_Normal_its_whole_fleet()
    {
        int Answering(Difficulty level)
        {
            var (w, _, _, home, _) = RaidOnAMine(1, level, raiders: 1);
            Run(w, 3, 1.0 / 30);
            return home.Count(e => e.Order?.Type == OrderType.AttackMove);
        }
        Assert.Equal(2, Answering(Difficulty.Hard));
        Assert.Equal(6, Answering(Difficulty.Normal));
    }

    [Fact]
    public void Hard_leaves_alone_a_mine_a_platform_watches_over()
    {
        var (w, _, mine, ships) = MineInSight(Difficulty.Hard);
        SpawnStructure(w, Player, BuildableType.Platform, mine.X - 200, mine.Y);
        Run(w, 5, 1.0 / 30);
        Assert.DoesNotContain(ships, e => e.Order?.Type == OrderType.AttackMove);
    }

    [Fact]
    public void Hard_calls_a_losing_wave_home_and_Normal_lets_it_die()
    {
        bool Recalled(Difficulty level)
        {
            var w = Game(10, level);
            var f = w.Factions[Bot];
            var st = StationOf(w, Bot)!;
            f.AI!.WaveValue = 1e9;
            var enemy = StationOf(w, Player)!;
            var wave = Enumerable.Range(0, 3).Select(i => SpawnShip(w, Bot, ShipClass.Frigate, (st.X + enemy.X) / 2 + i * 70, (st.Y + enemy.Y) / 2)).ToList();
            Parked(w, Player, ShipClass.Dreadnought, 4, (st.X + enemy.X) / 2 - 500, (st.Y + enemy.Y) / 2);
            foreach (var e in wave) e.Stance = Stance.Aggressive;
            Movement.IssueMove(w, wave.Select(e => e.Id), enemy.X, enemy.Y, true);
            Run(w, 3, 1.0 / 30);
            return wave.TrueForAll(e => e.Order?.Type == OrderType.Move);
        }
        Assert.True(Recalled(Difficulty.Hard));
        Assert.False(Recalled(Difficulty.Normal));
    }

    [Fact]
    public void Hard_aims_at_the_guns_of_big_ships_where_Normal_shoots_any_part()
    {
        bool AtGun(Difficulty level)
        {
            var w = Game(11, level);
            w.Factions[Bot].AI!.WaveValue = 1e9;
            var st = StationOf(w, Bot)!;
            var x = st.X - 2000;
            var cruiser = SpawnShip(w, Bot, ShipClass.Battlecruiser, x, 2000);
            cruiser.Stance = Stance.Hold;
            cruiser.Hull = cruiser.MaxHull = 1e6;
            var big = SpawnShip(w, Player, ShipClass.Dreadnought, x + 300, 2000, Math.PI);
            big.Stance = Stance.Passive;
            foreach (var p in ChildrenOf(w, big.Id).Prepend(big)) p.Hull = p.MaxHull = 1e6;
            // Its shield down: what the guns are aimed at once it gives way.
            big.Shield = big.MaxShield = 0;
            Run(w, 2, 1.0 / 30);
            var gunnery = ChildrenOf(w, cruiser.Id).Where(p => p.Weapons.Count > 0 && p.Weapons[0].Def != Weapons.Flak).ToList();
            return gunnery.TrueForAll(p => w.Entities.Get(p.TargetId) is { Kind: EntityKind.Hardpoint } t && t.Weapons.Count > 0);
        }
        Assert.True(AtGun(Difficulty.Hard));
        Assert.False(AtGun(Difficulty.Normal));
    }

    [Fact]
    public void Hard_keeps_its_barrage_for_more_than_small_craft()
    {
        double? Used(Difficulty level)
        {
            var w = Game(12, level);
            w.Factions[Bot].AI!.WaveValue = 1e9;
            var st = StationOf(w, Bot)!;
            var x = st.X - 2000;
            var cruiser = SpawnShip(w, Bot, ShipClass.Battlecruiser, x, 2000);
            cruiser.Stance = Stance.Hold;
            var craft = SpawnShip(w, Player, ShipClass.Fighter, x + 200, 2000);
            craft.Stance = Stance.Passive;
            craft.Hull = craft.MaxHull = 1e6;
            craft.Pilot = new Pilot();
            Run(w, 3, 1.0 / 30);
            return cruiser.Abilities.Exists(a => a.Id == AbilityId.Barrage && a.Cooldown > 0) ? 1 : null;
        }
        Assert.Null(Used(Difficulty.Hard));
        Assert.NotNull(Used(Difficulty.Normal));
    }

    [Fact]
    public void Hard_drops_its_orbital_strike_where_it_harms_most_where_Normal_counts_heads()
    {
        Aim? Blow(Difficulty level)
        {
            var w = Game(13, level, race: Faction.Klaed);
            w.Factions[Bot].AI!.WaveValue = 1e9;
            var st = StationOf(w, Bot)!;
            var x = st.X - 2000;
            var hero = SpawnHero(w, Bot, new HeroId(Faction.Klaed, HeroRole.Admiral), x, 2000);
            hero.Stance = Stance.Passive;
            // A few light ships on one side, one dreadnought on the other.
            Parked(w, Player, ShipClass.Support, 3, x + 400, 1300);
            var big = SpawnShip(w, Player, ShipClass.Dreadnought, x + 400, 2700);
            big.Stance = Stance.Passive;
            w.Strikes.Clear();
            for (var i = 0; i < 120 && w.Strikes.Count == 0; i++) StepWorld(w);
            return w.Strikes.Count == 0 ? null : new Aim(w.Strikes[0].X, w.Strikes[0].Y);
        }
        var hard = Blow(Difficulty.Hard);
        Assert.NotNull(hard);
        Expect.Greater(hard!.Value.Y, 2300);
        var normal = Blow(Difficulty.Normal);
        Assert.NotNull(normal);
        Expect.Less(normal!.Value.Y, 1700);
    }

    [Fact]
    public void The_higher_the_level_the_more_it_researches()
    {
        int Levels(Difficulty level) => Enumerable.Range(1, 6).Sum(seed =>
        {
            var w = Game(seed * 7, level);
            w.Factions[Bot].AI!.WaveValue = 1e9;
            Run(w, 8 * 60, 1.0 / 30);
            return Techs.All.Sum(t => w.Factions[Bot].Research[t]);
        });
        var (easy, normal, hard) = (Levels(Difficulty.Easy), Levels(Difficulty.Normal), Levels(Difficulty.Hard));
        Expect.Greater(normal, easy);
        Expect.Greater(hard, normal);
    }

    [Fact]
    public void Every_level_says_what_it_does()
    {
        foreach (var d in Enum.GetValues<Difficulty>()) Assert.False(string.IsNullOrWhiteSpace(Difficulties[d].Description));
        // In the campaign, whose AI decides alike at every level, the income stays what sets them apart.
        Assert.True(Difficulties[Difficulty.Easy].Income < Difficulties[Difficulty.Normal].Income);
        Assert.True(Difficulties[Difficulty.Hard].Income > Difficulties[Difficulty.Normal].Income);
        // In battle, Hard wins by what it decides: it earns no more than Normal.
        Assert.Equal(Difficulties[Difficulty.Normal].BattleIncome, Difficulties[Difficulty.Hard].BattleIncome);
    }

    // Personalities.

    /// <summary>The Kla'ed AI with this personality, `minutes` against a player who does nothing: what it holds in the end.</summary>
    static (int Mines, int Outposts, int TopTier) Plays(Personality personality, int seed, double minutes)
    {
        var w = Game(seed, personality: personality);
        var (mines, outposts, tier) = (0, 0, 0);
        for (var t = 0; t < minutes * 60 && w.Winner is null; t++)
        {
            Run(w, 1, 1.0 / 30);
            w.Events.Clear();
            mines = Math.Max(mines, w.Entities.Count(e => e.Side == Bot && e.Type == "mine"));
            outposts = Math.Max(outposts, w.Entities.Count(e => e.Side == Bot && e.Type == "outpost"));
            tier = Math.Max(tier, w.Entities.Where(e => e.Side == Bot && e.Type == "platform").Select(e => e.Level).DefaultIfEmpty(0).Max());
        }
        return (mines, outposts, tier);
    }

    [Fact]
    public void The_thrifty_AI_works_more_mines_than_the_others_and_the_defensive_one_raises_its_platforms()
    {
        var thrifty = Plays(Personality.Economic, 4, 12);
        var balanced = Plays(Personality.Balanced, 4, 12);
        var aggressive = Plays(Personality.Aggressive, 4, 12);
        var defensive = Plays(Personality.Defensive, 4, 12);
        Expect.Greater(thrifty.Mines, balanced.Mines);
        Expect.Less(aggressive.Mines, balanced.Mines + 0.5);
        // With no use for one more mine, no outpost.
        Assert.Equal(0, aggressive.Outposts);
        Expect.Greater(defensive.TopTier, 1);
        Assert.True(aggressive.TopTier <= 1);
    }

    [Fact]
    public void Middle_hulls_call_for_burst_cannons()
    {
        var w = Game(5);
        var f = w.Factions[Bot];
        var st = StationOf(w, Bot)!;
        RaiseStation(w, Bot, 3);
        double Share(ShipClass cls)
        {
            var n = 0;
            for (var i = 0; i < 2000; i++) if (Ai.PickShip(w, f, st) == cls) n++;
            return n / 2000.0;
        }
        var before = Share(ShipClass.Battlecruiser);
        f.AI!.Intel.Medium = 6;
        Expect.Greater(Share(ShipClass.Battlecruiser), before + 0.08);
    }
}
