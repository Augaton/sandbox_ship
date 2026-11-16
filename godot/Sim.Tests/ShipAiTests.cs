// How ships fight by themselves, every side alike (not in the web version): they pick the target
// their weapons are made for, finish off the damaged, spare shots on the doomed and gang up on
// what their neighbours fight; they keep out of reach of what they outrange, get out of the way
// of blows coming down, and fall back to be repaired.
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.Steps;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim.Tests;

public class ShipAiTests
{
    static World NewWorld()
    {
        var w = CreateWorld(6000, 4000);
        AddFaction(w, Faction.Klaed, 0);
        AddFaction(w, Faction.Nairan, 0);
        return w;
    }

    /// <summary>Takes the weapons off a ship and its parts: it fights no more, it only moves.</summary>
    static Entity Unarmed(World w, Entity e)
    {
        e.Weapons = [];
        foreach (var part in ChildrenOf(w, e.Id)) part.Weapons = [];
        return e;
    }

    // Picking a target.

    [Fact]
    public void Flak_shoots_the_fighter_while_the_ship_is_ordered_onto_a_big_one_and_the_rest_keeps_to_the_order()
    {
        var w = NewWorld();
        var cruiser = SpawnShip(w, Faction.Klaed, ShipClass.Battlecruiser, 1000, 1000);
        var dreadnought = Unarmed(w, SpawnShip(w, Faction.Nairan, ShipClass.Dreadnought, 1420, 1000, Math.PI));
        var fighter = Unarmed(w, SpawnShip(w, Faction.Nairan, ShipClass.Fighter, 1000, 1180));
        fighter.Pilot = new Pilot();
        Movement.IssueAttack(w, [cruiser.Id], dreadnought.Id);
        // Flak rounds by the size of what they fly at (the dreadnought's hangar launches fighters
        // too); the other shots by the ship they fly at.
        var (flak, rest) = (new List<SizeClass>(), new List<int>());
        var seen = new HashSet<int>();
        for (var i = 0; i < 60 * 4; i++)
        {
            StepWorld(w);
            foreach (var p in w.Projectiles.Where(p => p.Side == cruiser.Side && p.Prey is null && seen.Add(p.Id)))
            {
                var t = w.Entities.Get(p.TargetId)!;
                if (p.Speed == Weapons.Flak.ProjectileSpeed) flak.Add(t.Size);
                else rest.Add(AbilitySystem.OwnerOf(w, t).Id);
            }
        }
        Assert.NotEmpty(flak);
        Assert.All(flak, size => Assert.Equal(SizeClass.Small, size));
        Assert.NotEmpty(rest);
        Assert.All(rest, id => Assert.Equal(dreadnought.Id, id));
        Assert.True(fighter.Hull < fighter.MaxHull || !w.Entities.Has(fighter.Id));
    }

    /// <summary>A Klaed frigate between two Nairan scouts as far from it, which do not move nor shoot.</summary>
    static (World W, Entity Frigate, Entity A, Entity B) Between()
    {
        var w = NewWorld();
        var frigate = SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 1000, 1000);
        var a = Unarmed(w, SpawnShip(w, Faction.Nairan, ShipClass.Scout, 1000, 700));
        var b = Unarmed(w, SpawnShip(w, Faction.Nairan, ShipClass.Scout, 1000, 1300));
        a.Pilot = new Pilot();
        b.Pilot = new Pilot();
        return (w, frigate, a, b);
    }

    [Fact]
    public void A_ship_finishes_off_the_damaged_one_of_two_alike()
    {
        var (w, frigate, a, b) = Between();
        b.Hull = b.MaxHull * 0.2;
        Run(w, 0.6);
        Assert.Equal(b.Id, frigate.TargetId);
    }

    [Fact]
    public void A_ship_leaves_alone_a_target_the_shots_in_flight_will_destroy_anyway()
    {
        var (w, frigate, a, b) = Between();
        // Already a frigate's own shots on their way to the damaged one, enough to destroy it.
        b.Hull = b.MaxHull * 0.2;
        w.Projectiles.Add(new Projectile
        {
            Id = w.NextId++, Side = Faction.Klaed, Kind = WeaponKind.Laser, X = 5000, Y = 3500, Speed = 1, Damage = 500,
            TargetId = b.Id, Ttl = 60,
        });
        Run(w, 0.6);
        Assert.Equal(a.Id, frigate.TargetId);
    }

    [Fact]
    public void A_ship_joins_its_neighbours_on_their_target()
    {
        var (w, frigate, a, b) = Between();
        // Three others fight the scout further in the list, the one a tie would not give.
        foreach (var x in new[] { 1300.0, 1360, 1420 })
        {
            var other = SpawnShip(w, Faction.Klaed, ShipClass.Frigate, x, 1000);
            other.TargetId = b.Id;
            other.Retarget = 100;
        }
        Run(w, 0.6);
        Assert.Equal(b.Id, frigate.TargetId);
        var (w2, alone, a2, _) = Between();
        Run(w2, 0.6);
        Assert.Equal(a2.Id, alone.TargetId);
    }

    // Keeping away.

    [Fact]
    public void A_ship_keeps_out_of_reach_of_a_slower_one_it_outranges_and_still_hits_it()
    {
        var w = NewWorld();
        var cruiser = SpawnShip(w, Faction.Klaed, ShipClass.Battlecruiser, 1000, 1000);
        var bulwark = SpawnShip(w, Faction.Nairan, ShipClass.Bulwark, 1290, 1000, Math.PI);
        var start = bulwark.Hull + bulwark.Shield;
        var inReach = 0;
        for (var i = 0; i < 60 * 20; i++)
        {
            StepWorld(w);
            if (Dist(cruiser, bulwark) - cruiser.Radius <= bulwark.Range) inReach++;
        }
        // It turns away and opens the gap, then holds it: in the bulwark's reach a few seconds at most.
        Expect.Less(inReach, 60 * 6);
        Expect.Greater(Dist(cruiser, bulwark) - cruiser.Radius, bulwark.Range);
        Expect.Less(bulwark.Hull + bulwark.Shield, start);
    }

    [Fact]
    public void Ships_get_out_of_the_way_of_a_blow_coming_down_unless_told_where_to_go()
    {
        var w = NewWorld();
        var frigate = Unarmed(w, SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 1060, 1000));
        var told = Unarmed(w, SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 3060, 1000, Math.PI / 2));
        Movement.IssueMove(w, [told.Id], 3060, 2500);
        foreach (var x in new[] { 1000.0, 3000 })
        {
            w.Strikes.Add(new Strike { Kind = StrikeKind.Orbital, Side = Faction.Nairan, X = x, Y = 1000, X2 = x, Y2 = 1000, Radius = 150, Damage = 650, Delay = 4, Total = 4 });
        }
        var (hull, shield) = (frigate.Hull, frigate.Shield);
        Run(w, 4.2);
        Assert.Equal((hull, shield), (frigate.Hull, frigate.Shield));
        Expect.Greater(Hypot(frigate.X - 1000, frigate.Y - 1000), 150 + frigate.Radius);
        // The one told where to go keeps its course, straight down, instead of getting out eastwards.
        Expect.Less(Math.Abs(told.X - 3060), 5);
        Expect.Greater(told.Y, 1200);
    }

    [Fact]
    public void Ships_step_aside_from_a_spinal_beam_charging_up()
    {
        var w = NewWorld();
        var frigate = Unarmed(w, SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 2000, 1010));
        w.Strikes.Add(new Strike { Kind = StrikeKind.Beam, Side = Faction.Nairan, X = 1000, Y = 1000, X2 = 3000, Y2 = 1000, Radius = 40, Damage = 900, Delay = 3, Total = 3 });
        var max = frigate.Hull + frigate.Shield;
        Run(w, 3.2);
        Assert.Equal(max, frigate.Hull + frigate.Shield);
        Expect.Greater(Math.Abs(frigate.Y - 1000), 40 + frigate.Radius);
    }

    // How close they fight.

    /// <summary>Damage a ship's guns do to a big ship in a second, those in reach of `t` as it stands now, and all of them.</summary>
    static (double InReach, double All) Fire(World w, Entity s, Entity t)
    {
        double all = 0, reach = 0;
        foreach (var part in ChildrenOf(w, s.Id).Prepend(s))
        {
            foreach (var g in part.Weapons)
            {
                var rate = g.Def.Kind == WeaponKind.Beam ? g.Def.Damage : g.Def.Damage * g.Def.Burst / Math.Max(0.05, g.Def.Cooldown + (g.Def.Burst - 1) * g.Def.BurstGap);
                rate *= g.Def.Multipliers.Of(t.Size);
                all += rate;
                if (Dist(part, t) - t.Radius <= g.Def.Range) reach += rate;
            }
        }
        return (reach, all);
    }

    [Theory]
    [InlineData(ShipClass.Frigate)]
    [InlineData(ShipClass.Battlecruiser)]
    [InlineData(ShipClass.Dreadnought)]
    [InlineData(ShipClass.Titan)]
    [InlineData(ShipClass.Leviathan)]
    [InlineData(ShipClass.Colossus)]
    public void A_ship_told_to_attack_stops_where_most_of_its_fire_reaches(ShipClass cls)
    {
        var w = NewWorld();
        var s = SpawnShip(w, Faction.Klaed, cls, 500, 2000);
        s.Abilities = [];
        var t = SpawnShip(w, Faction.Nairan, ShipClass.Dreadnought, 2200, 2000, Math.PI);
        t.Stance = Stance.Passive;
        foreach (var p in ChildrenOf(w, t.Id).Prepend(t)) p.Hull = p.MaxHull = 1e9;
        t.Shield = t.MaxShield = 1e9;
        Movement.IssueAttack(w, [s.Id], t.Id);
        Run(w, 200, 1.0 / 30);
        var (reach, all) = Fire(w, s, t);
        // It used to be half of it for a leviathan, two thirds for a colossus, at 85 % of their longest gun.
        Expect.GreaterOrEqual(reach / all, 0.75);
        // Not by parking against it.
        Expect.Greater(Dist(s, t) - t.Radius, s.Radius);
    }

    [Fact]
    public void Support_ships_keep_behind_the_warships_in_a_fight_by_those_they_repair()
    {
        var w = NewWorld();
        var fleet = Enumerable.Range(0, 4).Select(i => SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 800, 1700 + i * 150)).ToList();
        var supports = Enumerable.Range(0, 2).Select(i => SpawnShip(w, Faction.Klaed, ShipClass.Support, 700, 1900 + i * 150)).ToList();
        var foes = Enumerable.Range(0, 4).Select(i => SpawnShip(w, Faction.Nairan, ShipClass.Frigate, 2000, 1700 + i * 150, Math.PI)).ToList();
        Movement.IssueMove(w, fleet.Concat(supports).Select(e => e.Id), 2000, 1900, true);
        Movement.IssueMove(w, foes.Select(e => e.Id), 800, 1900, true);
        Run(w, 14);
        double Nearest(Entity e) => foes.Where(f => w.Entities.Has(f.Id)).Min(f => Dist(e, f));
        // They used to come up level with the frigates.
        Expect.Greater(supports.Min(Nearest), fleet.Where(e => w.Entities.Has(e.Id)).Average(Nearest) + 60);
        foreach (var s in supports)
            Expect.Less(fleet.Where(e => w.Entities.Has(e.Id)).Min(e => Dist(e, s) - e.Radius), Ships.Defs[ShipClass.Support].Repair!.Range);
    }

    [Fact]
    public void A_support_ship_goes_to_the_most_damaged_ally_around_it()
    {
        var w = NewWorld();
        var support = SpawnShip(w, Faction.Klaed, ShipClass.Support, 1000, 1000);
        var healthy = Unarmed(w, SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 1000, 1200));
        var hurt = Unarmed(w, SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 1000, 500));
        healthy.Pilot = new Pilot();
        hurt.Pilot = new Pilot();
        hurt.Hull = hurt.MaxHull * 0.5;
        var foe = Unarmed(w, SpawnShip(w, Faction.Nairan, ShipClass.Scout, 1300, 1000));
        foe.Pilot = new Pilot();
        Run(w, 10);
        Expect.Less(Dist(support, hurt), Ships.Defs[ShipClass.Support].Repair!.Range + hurt.Radius);
        Expect.Greater(hurt.Hull, hurt.MaxHull * 0.5);
    }

    [Fact]
    public void A_wounded_ship_does_not_fly_to_the_front_for_a_support_ship()
    {
        var w = NewWorld();
        var wounded = SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 1000, 1000);
        // The support ship is nearer the foe it is fighting than itself.
        var support = SpawnShip(w, Faction.Klaed, ShipClass.Support, 1300, 1000);
        support.Pilot = new Pilot();
        var foe = Unarmed(w, SpawnShip(w, Faction.Nairan, ShipClass.Frigate, 1500, 1000, Math.PI));
        foe.Pilot = new Pilot();
        Run(w, 1);
        Assert.NotNull(w.Entities.Get(wounded.TargetId));
        wounded.Hull = wounded.MaxHull * 0.2;
        Run(w, 1);
        Assert.False(wounded.Mending);
        // A support ship behind it is another matter.
        var behind = SpawnShip(w, Faction.Klaed, ShipClass.Support, wounded.X - 200, 1000);
        behind.Pilot = new Pilot();
        Run(w, 1);
        Assert.True(wounded.Mending);
        Assert.Equal(behind.Id, wounded.MenderId);
    }

    // Falling back.

    [Fact]
    public void A_badly_damaged_ship_falls_back_to_a_support_ship_and_returns_to_its_post_once_mended()
    {
        var w = NewWorld();
        var frigate = Unarmed(w, SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 2000, 1000));
        var support = SpawnShip(w, Faction.Klaed, ShipClass.Support, 1100, 1000);
        support.Pilot = new Pilot();
        frigate.Hull = frigate.MaxHull * 0.2;
        StepWorld(w);
        Assert.True(frigate.Mending);
        Run(w, 16);
        Expect.Less(Dist(frigate, support), Ships.Defs[ShipClass.Support].Repair!.Range);
        Expect.Greater(frigate.Hull, frigate.MaxHull * 0.2);
        // Mended: back to where it stood.
        frigate.Hull = frigate.MaxHull * 0.85;
        Run(w, 20);
        Assert.False(frigate.Mending);
        Expect.Less(Hypot(frigate.X - 2000, frigate.Y - 1000), 60);
    }

    [Fact]
    public void An_order_hangar_craft_and_a_side_without_repairs_keep_ships_from_falling_back()
    {
        var w = NewWorld();
        SpawnShip(w, Faction.Klaed, ShipClass.Support, 1100, 1000).Pilot = new Pilot();
        var ordered = Unarmed(w, SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 2000, 1000));
        ordered.Hull = ordered.MaxHull * 0.2;
        Movement.IssueMove(w, [ordered.Id], 2000, 2500);
        var craft = Unarmed(w, SpawnShip(w, Faction.Klaed, ShipClass.Fighter, 2000, 1400));
        craft.Launched = true;
        craft.Hull = craft.MaxHull * 0.2;
        // The Nairan have nothing that repairs.
        var nairan = Unarmed(w, SpawnShip(w, Faction.Nairan, ShipClass.Frigate, 4000, 3000));
        nairan.Hull = nairan.MaxHull * 0.2;
        Run(w, 2);
        Assert.False(ordered.Mending);
        Assert.False(craft.Mending);
        Assert.False(nairan.Mending);
        Expect.Greater(ordered.Y, 1100);
    }
}
