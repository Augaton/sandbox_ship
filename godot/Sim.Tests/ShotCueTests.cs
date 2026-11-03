// What the view reads from a shot to give it its sound: a burst cannon's bullets fly faster than
// Weapons.BurstRoundSpeed, every other bullet slower (BattleScreen.ShotSoundOf). A player over the
// network only knows a shot's kind and speed, so a weapon changed across that line would sound wrong.
using SandboxShip.Sim.Data;
using Xunit;

namespace SandboxShip.Sim.Tests;

public class ShotCueTests
{
    /// <summary>Every weapon in the game: ships' hulls and turrets, parts, stations, buildings and their tiers.</summary>
    static IEnumerable<WeaponDef> AllWeapons() =>
        Ships.Defs.Values.SelectMany(d => d.Weapons.Concat(d.Hardpoints?.SelectMany(h => h.Weapons ?? Hardpoints.Defs[h.Role].Weapons) ?? []))
            .Concat(Hardpoints.Defs.Values.SelectMany(h => h.Weapons))
            .Concat(Station.Weapons)
            .Concat(Station.Levels.SelectMany(l => l.Hardpoints.SelectMany(h => h.Weapons ?? Hardpoints.Defs[h.Role].Weapons)))
            .Concat(Buildables.Defs.Values.SelectMany(b => b.Weapons.Concat(b.Tiers?.SelectMany(t => t.Weapons ?? []) ?? [])));

    [Fact]
    public void Burst_rounds_and_other_bullets_fly_on_either_side_of_the_line()
    {
        var bullets = AllWeapons().Where(w => w.Kind == WeaponKind.Bullet).ToList();
        Assert.Contains(bullets, w => w.Burst > 1);
        Assert.All(bullets, w => Assert.True(w.Burst > 1 == w.ProjectileSpeed > Weapons.BurstRoundSpeed,
            $"balle à {w.ProjectileSpeed} (rafale de {w.Burst}) du mauvais côté de {Weapons.BurstRoundSpeed}"));
    }
}
