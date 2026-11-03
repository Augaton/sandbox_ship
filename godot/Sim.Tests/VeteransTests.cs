// Veterans, a rule of this version only: experience from the damage a ship deals and the foes it
// finishes, measured against its own toughness, ranks that make it a little better, and what
// teaches nothing (mines, foes of no faction, structures' guns).
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.AbilitySystem;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim.Tests;

public class VeteransTests
{
    static readonly Side Klaed = Faction.Klaed, Nairan = Faction.Nairan;

    static World NewWorld()
    {
        var w = CreateWorld(4000, 4000);
        AddFaction(w, Faction.Klaed, 0);
        AddFaction(w, Faction.Nairan, 0);
        return w;
    }

    static double Toughness(Entity e) => e.MaxHull + e.MaxShield;

    [Fact]
    public void Damage_teaches_its_share_of_the_ship_s_own_toughness_and_the_finishing_blow_more()
    {
        var w = NewWorld();
        var ship = SpawnShip(w, Klaed, ShipClass.Frigate, 500, 500);
        var foe = SpawnShip(w, Nairan, ShipClass.Scout, 900, 500);
        foe.Shield = 0;
        Hurt(w, foe, 1, Klaed, by: ship);
        Expect.Close(1 / Toughness(ship), ship.Xp, 9);
        var left = foe.Hull;
        Hurt(w, foe, 1e6, Klaed, by: ship);
        // Only the hull it had left counts, and half the foe's toughness for finishing it.
        Expect.Close((1 + left + 0.5 * Toughness(foe)) / Toughness(ship), ship.Xp, 9);
        // A wreck not yet cleared away teaches nothing more.
        var xp = ship.Xp;
        Hurt(w, foe, 100, Klaed, by: ship);
        Assert.Equal(xp, ship.Xp);
    }

    [Fact]
    public void A_frigate_downing_a_battlecruiser_learns_more_than_the_other_way_round()
    {
        var w = NewWorld();
        var frigate = SpawnShip(w, Klaed, ShipClass.Frigate, 500, 500);
        var big = SpawnShip(w, Klaed, ShipClass.Battlecruiser, 500, 900);
        Hurt(w, SpawnShip(w, Nairan, ShipClass.Battlecruiser, 900, 500), 1e7, Klaed, by: frigate);
        Hurt(w, SpawnShip(w, Nairan, ShipClass.Frigate, 900, 900), 1e7, Klaed, by: big);
        Expect.Greater(frigate.Xp, big.Xp * 4);
    }

    [Fact]
    public void Ranks_come_with_experience_and_make_a_ship_deadlier_tougher_and_quicker_to_mend()
    {
        var w = NewWorld();
        var ship = SpawnShip(w, Klaed, ShipClass.Frigate, 500, 500);
        var (hull, max) = (ship.Hull * 0.5, ship.MaxHull);
        ship.Hull = hull;
        var damage = Combat.DamageMult(w, ship);
        var kills = 0;
        while (ship.Rank < Veterans.MaxRank && kills < 100)
        {
            Hurt(w, SpawnShip(w, Nairan, ShipClass.Frigate, 900, 500), 1e6, Klaed, by: ship);
            kills++;
        }
        Assert.Equal(Veterans.MaxRank, ship.Rank);
        // Two equal foes downed for the first rank, many more for the last.
        Assert.InRange(kills, 8, 20);
        Assert.Equal([1, 2, 3], w.Events.OfType<Promoted>().Where(p => p.Id == ship.Id).Select(p => p.Rank));
        Expect.Close(max * (1 + 3 * Veterans.HullPerRank), ship.MaxHull, 9);
        Expect.Close(hull * (1 + 3 * Veterans.HullPerRank), ship.Hull, 9);
        Expect.Close(damage * (1 + 3 * Veterans.DamagePerRank), Combat.DamageMult(w, ship), 9);
        // Its shields come back faster.
        var rookie = SpawnShip(w, Klaed, ShipClass.Frigate, 500, 900);
        ship.Shield = rookie.Shield = 0;
        ship.ShieldDown = rookie.ShieldDown = 0;
        Combat.UpdateShields(w, 1);
        Expect.Close(rookie.Shield * (1 + 3 * Veterans.RegenPerRank), ship.Shield, 6);
    }

    [Fact]
    public void Mines_foes_of_no_faction_and_structures_teach_nothing()
    {
        var w = NewWorld();
        var ship = SpawnShip(w, Klaed, ShipClass.Frigate, 500, 500);
        var foe = SpawnShip(w, Nairan, ShipClass.Frigate, 900, 500);
        Hurt(w, foe, 50, Terrains.Neutral);
        Assert.Null(foe.ShooterId);
        var stray = SpawnShip(w, new Side(9), ShipClass.Frigate, 900, 900);
        Hurt(w, stray, 50, Klaed, by: ship);
        Assert.Equal(0, ship.Xp);
        var station = SpawnStation(w, Klaed, 2000, 2000);
        var gun = ChildrenOf(w, station.Id).First();
        Hurt(w, foe, 50, Klaed, by: gun);
        Assert.Null(foe.ShooterId);
        Assert.Equal((0.0, 0.0), (station.Xp, gun.Xp));
    }

    [Fact]
    public void A_turret_s_blows_and_a_craft_s_teach_the_ship_they_belong_to()
    {
        var w = NewWorld();
        var big = SpawnShip(w, Klaed, ShipClass.Battlecruiser, 500, 500);
        var turret = ChildrenOf(w, big.Id).First();
        var foe = SpawnShip(w, Nairan, ShipClass.Frigate, 900, 500);
        Hurt(w, foe, 30, Klaed, by: turret);
        Assert.Equal(big.Id, foe.ShooterId);
        Expect.Greater(big.Xp, 0);
        var xp = big.Xp;
        var craft = SpawnShip(w, Klaed, ShipClass.Fighter, 520, 520);
        craft.Launched = true;
        Movement.Assign(craft, big, craft.X, craft.Y);
        Hurt(w, foe, 30, Klaed, by: craft);
        Assert.Equal(big.Id, foe.ShooterId);
        Expect.Greater(big.Xp, xp);
        Assert.Equal(0, craft.Xp);
    }

    [Fact]
    public void In_battle_the_shots_and_beams_that_land_teach_the_ship_that_fired()
    {
        var w = NewWorld();
        var hunter = SpawnShip(w, Klaed, ShipClass.Frigate, 1000, 1000);
        var prey = SpawnShip(w, Nairan, ShipClass.Scout, 1200, 1000);
        prey.Stance = Stance.Passive;
        Steps.Run(w, 30);
        Assert.False(w.Entities.Has(prey.Id));
        Expect.Greater(hunter.Xp, 0);
        var ace = Assert.Single(w.History!.Aces[Klaed]);
        Assert.Equal((hunter.Id, 1), (ace.Id, ace.Kills));
    }
}
