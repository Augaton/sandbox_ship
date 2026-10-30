// Port of tests/abilities.test.ts.
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.AbilitySystem;
using static SandboxShip.Sim.Steps;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim.Tests;

public class AbilitiesTests
{
    static World NewWorld()
    {
        var w = CreateWorld(6000, 4000);
        AddFaction(w, Faction.Klaed, 100000);
        AddFaction(w, Faction.Nairan, 100000);
        AddFaction(w, Faction.Nautolan, 100000);
        return w;
    }

    static HeroId Hero(Faction f, HeroRole r) => new(f, r);

    /// <summary>No guns on the ship nor on its turrets: what a test sees is the ability's work alone.</summary>
    static Entity Disarm(World w, Entity e)
    {
        foreach (var part in ChildrenOf(w, e.Id).Prepend(e)) part.Weapons = [];
        e.Range = 0;
        return e;
    }

    /// <summary>A target that stays put and does not shoot back.</summary>
    static Entity Dummy(World w, double x, double y, Faction faction = Faction.Nairan) =>
        Disarm(w, SpawnShip(w, faction, ShipClass.Support, x, y));

    // Aimed abilities.

    [Fact]
    public void A_point_ability_is_brought_back_within_range_an_enemy_ability_needs_a_seen_enemy_in_range()
    {
        var w = NewWorld();
        var kesh = SpawnHero(w, Faction.Klaed, Hero(Faction.Klaed, HeroRole.Ace), 1000, 1000);
        var near = Dummy(w, 1500, 1000);
        var far = Dummy(w, 2500, 1000);
        Assert.Equal(Result.Fail("Choisis un ennemi"), UseAbility(w, kesh));
        Assert.Equal(Result.Fail("Cible hors de portée"), UseAbility(w, kesh, new Aim(far.X, far.Y, far.Id)));
        Assert.True(UseAbility(w, kesh, new Aim(near.X, near.Y, near.Id)).Ok);

        var vorrak = SpawnHero(w, Faction.Klaed, Hero(Faction.Klaed, HeroRole.Admiral), 1000, 2000);
        Assert.True(UseAbility(w, vorrak, new Aim(5000, 2000)).Ok);
        Expect.Close(1000 + Abilities.Defs[AbilityId.OrbitalStrike].Range!.Value, w.Strikes[0].X, 5);
    }

    [Fact]
    public void The_orbital_strike_lands_after_its_delay_on_enemies_only()
    {
        var w = NewWorld();
        var vorrak = Disarm(w, SpawnHero(w, Faction.Klaed, Hero(Faction.Klaed, HeroRole.Admiral), 1000, 1000));
        var foe = Dummy(w, 1600, 1000);
        var friend = Dummy(w, 1650, 1000, Faction.Klaed);
        var outside = Dummy(w, 1600, 1400);
        var hull = foe.Hull + foe.Shield;
        UseAbility(w, vorrak, new Aim(1600, 1000));
        Run(w, Abilities.OrbitalDelay - 0.2);
        Expect.Close(hull, foe.Hull + foe.Shield, 0);
        Run(w, 0.4);
        Expect.Less(foe.Hull + foe.Shield, hull - 600);
        Assert.Equal(friend.MaxHull, friend.Hull);
        Assert.Equal(outside.MaxHull, outside.Hull);
    }

    [Fact]
    public void The_titans_spinal_beam_strikes_everything_on_its_line_after_charging()
    {
        var w = NewWorld();
        var titan = Disarm(w, SpawnShip(w, Faction.Klaed, ShipClass.Titan, 1000, 1000));
        var onLine = Dummy(w, 1800, 1000);
        var offLine = Dummy(w, 1800, 1200);
        UseAbility(w, titan, new Aim(2000, 1000));
        Run(w, Abilities.SpinalCharge + 0.1);
        Expect.Less(onLine.Hull, onLine.MaxHull);
        Assert.Equal(offLine.MaxHull, offLine.Hull);
    }

    // Hero powers.

    [Fact]
    public void A_mark_makes_the_target_take_half_again_as_much_damage()
    {
        var w = NewWorld();
        var kesh = Disarm(w, SpawnHero(w, Faction.Klaed, Hero(Faction.Klaed, HeroRole.Ace), 1000, 1000));
        var prey = Dummy(w, 1300, 1000);
        UseAbility(w, kesh, new Aim(prey.X, prey.Y, prey.Id));
        Assert.Equal(1.5, DamageTaken(w, prey));
        Run(w, Abilities.Marked + 0.1);
        Assert.Equal(1, DamageTaken(w, prey));
    }

    [Fact]
    public void A_shield_dome_halves_the_damage_its_allies_take()
    {
        var w = NewWorld();
        var selis = SpawnHero(w, Faction.Nairan, Hero(Faction.Nairan, HeroRole.Admiral), 1000, 1000);
        var inside = Dummy(w, 1200, 1000, Faction.Nairan);
        var outside = Dummy(w, 2000, 1000, Faction.Nairan);
        UseAbility(w, selis);
        StepWorld(w);
        Assert.Equal(0.5, DamageTaken(w, inside));
        Assert.Equal(1, DamageTaken(w, outside));
    }

    [Fact]
    public void A_cloaked_ace_cannot_be_seen_or_targeted_then_shows_again()
    {
        var w = NewWorld();
        var ilo = SpawnHero(w, Faction.Nairan, Hero(Faction.Nairan, HeroRole.Ace), 1000, 1000);
        var hunter = SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 1200, 1000);
        UseAbility(w, ilo);
        Run(w, 1);
        Assert.False(Vision.IsVisible(w, Faction.Klaed, ilo));
        Assert.NotEqual(ilo.Id, hunter.TargetId);
        Run(w, Abilities.Defs[AbilityId.Cloak].Duration);
        Assert.True(Vision.IsVisible(w, Faction.Klaed, ilo));
    }

    [Fact]
    public void An_EMP_strips_shields_and_holds_enemy_ships_still()
    {
        var w = NewWorld();
        var tavi = SpawnHero(w, Faction.Nairan, Hero(Faction.Nairan, HeroRole.Engineer), 1000, 1000);
        var a = SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 1200, 1000);
        a.Order = Order.Move(3000, 1000);
        UseAbility(w, tavi);
        Assert.Equal(0, a.Shield);
        Run(w, 1);
        Expect.Less(a.Speed, 5);
        Assert.Equal(0, a.Shield);
        Run(w, Abilities.Stunned);
        Assert.Equal(0, a.Effects.Stunned);
    }

    [Fact]
    public void Spores_eat_the_hull_through_the_shields()
    {
        var w = NewWorld();
        var kael = Disarm(w, SpawnHero(w, Faction.Nautolan, Hero(Faction.Nautolan, HeroRole.Ace), 1000, 1000));
        var prey = Dummy(w, 1300, 1000);
        UseAbility(w, kael, new Aim(prey.X, prey.Y, prey.Id));
        Run(w, 2);
        Expect.Greater(prey.Shield, 0);
        Expect.Less(prey.Hull, prey.MaxHull);
    }

    [Fact]
    public void A_graft_mends_allies_over_time()
    {
        var w = NewWorld();
        var om = SpawnHero(w, Faction.Nautolan, Hero(Faction.Nautolan, HeroRole.Engineer), 1000, 1000);
        // Beyond the reach of Om's own repair beam, within the graft.
        var hurt = Dummy(w, 1350, 1000, Faction.Nautolan);
        hurt.Hull = hurt.MaxHull * 0.3;
        UseAbility(w, om);
        Run(w, 5);
        Expect.Greater(hurt.Hull, hurt.MaxHull * 0.3 + hurt.MaxHull * 0.1);
    }

    [Fact]
    public void A_swarm_and_a_field_turret_only_last_a_while()
    {
        var w = NewWorld();
        var yshra = SpawnHero(w, Faction.Nautolan, Hero(Faction.Nautolan, HeroRole.Admiral), 1000, 1000);
        var ondra = SpawnHero(w, Faction.Klaed, Hero(Faction.Klaed, HeroRole.Engineer), 3000, 3000);
        UseAbility(w, yshra);
        UseAbility(w, ondra, new Aim(3100, 3000));
        // The mother ship's own hangar launches fighters too: count the swarm only.
        int Fighters() => w.Entities.Count(e => e.Type == "fighter" && e.Expires is not null);
        int Turrets() => w.Entities.Count(e => e.Type == "platform" && e.Faction == Faction.Klaed);
        Assert.Equal(8, Fighters());
        Assert.Equal(1, Turrets());
        Run(w, 46);
        Assert.Equal(0, Fighters());
        Assert.Equal(0, Turrets());
    }

    [Fact]
    public void An_admiral_commands_allies_around_deal_more_damage()
    {
        var w = NewWorld();
        var frigate = SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 1100, 1000);
        var before = Combat.DamageMult(w, frigate);
        SpawnHero(w, Faction.Klaed, Hero(Faction.Klaed, HeroRole.Admiral), 1000, 1000);
        StepWorld(w);
        Expect.Close(before * 1.15, Combat.DamageMult(w, frigate), 5);
    }

    [Fact]
    public void An_AI_faction_uses_its_powers_when_they_are_worth_it()
    {
        var w = NewWorld();
        w.Factions[Faction.Nairan].AutoAbilities = true;
        var tavi = SpawnHero(w, Faction.Nairan, Hero(Faction.Nairan, HeroRole.Engineer), 1000, 1000);
        SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 1200, 1000);
        Run(w, 1);
        Assert.Equal(0, tavi.Ability!.Cooldown);
        SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 1200, 1100);
        Run(w, 1);
        Expect.Greater(tavi.Ability!.Cooldown, 0);
    }

    // Titan.

    // The web version allowed one titan; this one allows two.
    [Fact]
    public void Two_titans_at_most_counting_those_in_the_queue()
    {
        var w = NewWorld();
        SpawnStation(w, Faction.Klaed, 700, 2000, 3);
        w.Factions[Faction.Klaed].Credits = 100000;
        Assert.True(Structures.EnqueueShip(w, Faction.Klaed, ShipClass.Titan).Ok);
        Assert.True(Structures.EnqueueShip(w, Faction.Klaed, ShipClass.Titan).Ok);
        Assert.Equal(Result.Fail("Titan : 2 au plus à la fois"), Structures.CanBuildShip(w, Faction.Klaed, ShipClass.Titan));
    }
}
