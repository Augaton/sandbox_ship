// This version's weapons (not in the web version): beams that hold on their target, bursts with
// a reload, swarm missiles, point defense that shoots torpedoes and missiles down, ion bolts that
// tear down shields; and what of them crosses the network.
using SandboxShip.Sim.Data;
using Xunit;
using static SandboxShip.Sim.Steps;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim.Tests;

public class WeaponsTests
{
    static World NewWorld()
    {
        var w = CreateWorld(6000, 4000);
        AddFaction(w, Faction.Klaed, 0);
        AddFaction(w, Faction.Nairan, 0);
        return w;
    }

    /// <summary>The ship's part carrying a weapon of this kind; every other weapon of the ship taken away.</summary>
    static Entity Only(World w, Entity ship, Func<WeaponDef, bool> which)
    {
        ship.Weapons = [];
        Entity? kept = null;
        foreach (var part in ChildrenOf(w, ship.Id))
        {
            if (kept is null && part.Weapons.Exists(x => which(x.Def))) kept = part;
            else part.Weapons = [];
        }
        return kept!;
    }

    static void Within(double expected, double actual, double tolerance) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"Expected {actual} within {tolerance} of {expected}");

    [Fact]
    public void A_beam_holds_on_its_target_every_step_without_a_projectile_and_lets_go_out_of_reach()
    {
        var w = NewWorld();
        var frigate = SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 1000, 1000);
        var turret = Only(w, frigate, d => d.Kind == WeaponKind.Beam);
        var mine = SpawnStructure(w, Faction.Nairan, BuildableType.Mine, 1300, 1000);
        var before = mine.Hull;
        Run(w, 3);
        Assert.Empty(w.Projectiles);
        Assert.Contains(new BeamLine(turret.Id, mine.Id, Faction.Klaed), w.Beams);
        // Damage per second, against a structure, from the first step on.
        var expected = Weapons.Beam.Damage * Combat.DamageMult(w, turret) * Weapons.Beam.Multipliers.Structure * 3;
        Within(expected, before - mine.Hull, expected * 0.05);
        Assert.Contains(w.Events, e => e is Hit { Kind: WeaponKind.Beam } h && h.TargetId == mine.Id);
        mine.X = 4000;
        StepWorld(w);
        Assert.Empty(w.Beams);
    }

    [Fact]
    public void A_burst_fires_its_rounds_close_together_then_reloads()
    {
        var w = NewWorld();
        var cruiser = SpawnShip(w, Faction.Klaed, ShipClass.Battlecruiser, 1000, 1000);
        Only(w, cruiser, d => d.Burst > 1);
        SpawnStructure(w, Faction.Nairan, BuildableType.Mine, 1300, 1000);
        var times = new List<double>();
        var seen = new HashSet<int>();
        for (var i = 0; i < 60 * 9; i++)
        {
            StepWorld(w);
            foreach (var p in w.Projectiles)
            {
                if (seen.Add(p.Id)) times.Add(w.Time);
            }
        }
        // Two bursts of six, a tenth of a second apart within each, the reload between them.
        Assert.True(times.Count >= 12, $"{times.Count} rounds");
        for (var i = 1; i < 6; i++) Within(Weapons.Burst.BurstGap, times[i] - times[i - 1], 0.02);
        Expect.Greater(times[6] - times[5], Weapons.Burst.Cooldown * 0.85);
        for (var i = 7; i < 12; i++) Within(Weapons.Burst.BurstGap, times[i] - times[i - 1], 0.02);
    }

    [Fact]
    public void Swarm_missiles_leave_as_a_salvo_of_homing_missiles_that_can_be_shot_down()
    {
        var w = NewWorld();
        var titan = SpawnShip(w, Faction.Klaed, ShipClass.Titan, 1000, 1000);
        var launcher = Only(w, titan, d => d.Kind == WeaponKind.Missile);
        // Loaded: a weapon's first shot waits for a reload, five seconds for this one.
        launcher.Weapons[0].Cooldown = 0;
        SpawnShip(w, Faction.Nairan, ShipClass.Fighter, launcher.X + 300, launcher.Y);
        List<Projectile> salvo = [];
        for (var i = 0; i < 60 * 3 && salvo.Count < 6; i++)
        {
            StepWorld(w);
            salvo = w.Projectiles.Where(p => p.Kind == WeaponKind.Missile).ToList();
        }
        Assert.Equal(6, salvo.Count);
        Assert.All(salvo, p => Assert.True(p.Homing && p.Hp > 0));
        // Side by side rather than in a string.
        Expect.Greater(salvo.Max(p => p.Y) - salvo.Min(p => p.Y), 10);
        // Made for small craft.
        Expect.Greater(Weapons.Swarm.Multipliers.Small, 1.5);
        Expect.Less(Weapons.Swarm.Multipliers.Large, 0.5);
    }

    /// <summary>A Nairan torpedo on its way to `target`, `away` pixels off, as tough as a fired one.</summary>
    static Projectile Torpedo(World w, Entity target, double away)
    {
        var p = new Projectile
        {
            Id = w.NextId++, Side = Faction.Nairan, Kind = WeaponKind.Torpedo, X = target.X + away, Y = target.Y,
            Vx = -Weapons.Torpedo.ProjectileSpeed, Speed = Weapons.Torpedo.ProjectileSpeed, Damage = Weapons.Torpedo.Damage,
            Mult = Weapons.Torpedo.Multipliers, TargetId = target.Id, Homing = true, Ttl = 5, Hp = 8,
        };
        w.Projectiles.Add(p);
        return p;
    }

    [Fact]
    public void Point_defense_shoots_a_torpedo_down_and_sends_no_more_rounds_than_it_takes()
    {
        var w = NewWorld();
        var frigates = Enumerable.Range(0, 4).Select(i => SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 1000, 900 + i * 60)).ToList();
        // Flak ready at once.
        foreach (var f in frigates) f.Weapons[0].Cooldown = 0;
        var target = frigates[1];
        var (hull, shield) = (target.Hull, target.Shield);
        var torpedo = Torpedo(w, target, 500);
        var most = 0;
        Intercepted? down = null;
        for (var i = 0; i < 60 * 3 && down is null; i++)
        {
            StepWorld(w);
            most = Math.Max(most, w.Projectiles.Count(p => p.Prey == torpedo));
            down = w.Events.OfType<Intercepted>().FirstOrDefault();
            w.Events.Clear();
        }
        Assert.NotNull(down);
        Assert.Equal(Faction.Klaed, down.Side);
        StepWorld(w);
        Assert.DoesNotContain(torpedo, w.Projectiles);
        Assert.Equal((hull, shield), (target.Hull, target.Shield));
        // 4.4 a round against 8: two rounds at a time, not one from every gun at once.
        Assert.Equal(2, most);
    }

    [Fact]
    public void A_lone_frigate_cannot_stop_every_torpedo_sent_at_it()
    {
        // Point defense thins torpedoes out; it does not make torpedo boats useless. Twenty
        // torpedoes one after the other, from a torpedo boat's reach, at a frigate's lone flak.
        var w = NewWorld();
        var frigate = SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 1000, 1000);
        foreach (var part in ChildrenOf(w, frigate.Id)) part.Weapons = [];
        var (landed, downed) = (0, 0);
        for (var n = 0; n < 20; n++)
        {
            Torpedo(w, frigate, Weapons.Torpedo.Range);
            for (var i = 0; i < 60 * 2; i++)
            {
                StepWorld(w);
                landed += w.Events.Count(e => e is Hit { Kind: WeaponKind.Torpedo });
                downed += w.Events.Count(e => e is Intercepted);
                w.Events.Clear();
            }
        }
        Assert.Equal(20, landed + downed);
        Expect.Greater(downed, 0);
        Expect.Greater(landed, downed);
    }

    [Fact]
    public void Ion_bolts_hit_shields_twice_as_hard_and_barely_scratch_the_hull()
    {
        var w = NewWorld();
        var s = SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 0, 0);
        s.Shield = 100;
        var hull = s.Hull;
        ApplyDamage(s, 40, s, Weapons.Ion.ShieldMult, Weapons.Ion.HullMult);
        Assert.Equal(20, s.Shield, 9);
        Assert.Equal(hull, s.Hull);
        // 10 of the 40 go through the last 20 of shield; the other 30 hit the hull at a fifth.
        ApplyDamage(s, 40, s, Weapons.Ion.ShieldMult, Weapons.Ion.HullMult);
        Assert.Equal(0, s.Shield, 9);
        Assert.Equal(hull - 30 * Weapons.Ion.HullMult, s.Hull, 9);

        // Fired by a dreadnought's turret, they strip a frigate's shields well before its hull suffers.
        var dreadnought = SpawnShip(w, Faction.Klaed, ShipClass.Dreadnought, 1000, 1000);
        Only(w, dreadnought, d => d.Kind == WeaponKind.Ion);
        var target = SpawnShip(w, Faction.Nairan, ShipClass.Frigate, 1400, 1000);
        target.Weapons = [];
        foreach (var part in ChildrenOf(w, target.Id)) part.Weapons = [];
        var (shield0, hull0) = (target.Shield, target.Hull);
        Run(w, 6);
        Expect.Greater(shield0 - target.Shield, 5 * (hull0 - target.Hull));
    }

    [Fact]
    public void Beams_and_torpedoes_shot_down_reach_the_players_over_the_network()
    {
        var w = NewWorld();
        var frigate = SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 1000, 1000);
        var turret = Only(w, frigate, d => d.Kind == WeaponKind.Beam);
        var mine = SpawnStructure(w, Faction.Nairan, BuildableType.Mine, 1300, 1000);
        Run(w, 1);
        var guard = SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 2000, 2000);
        guard.Weapons[0].Cooldown = 0;
        var buddy = SpawnShip(w, Faction.Klaed, ShipClass.Frigate, 2000, 2060);
        buddy.Weapons[0].Cooldown = 0;
        Torpedo(w, guard, 500);
        var events = new List<SimEvent>();
        for (var i = 0; i < 60 * 3 && !events.Exists(e => e is Intercepted); i++)
        {
            StepWorld(w);
            events.AddRange(w.Events);
            w.Events.Clear();
        }
        Assert.Contains(events, e => e is Intercepted);
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) Snapshot.Write(w, Faction.Nairan, bw, events);
        var replica = new World();
        ms.Position = 0;
        Snapshot.Read(new BinaryReader(ms), replica);
        Assert.Contains(new BeamLine(turret.Id, mine.Id, Faction.Klaed), replica.Beams);
        Assert.Contains(replica.Events, e => e is Intercepted i && i.Side == Faction.Klaed);
    }
}
