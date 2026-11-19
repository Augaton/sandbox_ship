// The codex's counters (Counters.cs), worked out from the weapons: they must say what a player
// knows of the fleet, and say it both ways. A rule of this version only.
using SandboxShip.Sim.Data;
using Xunit;

namespace SandboxShip.Sim.Tests;

public sealed class CountersTests
{
    [Fact]
    public void A_matchup_reads_the_same_from_either_side()
    {
        foreach (var a in Ships.All)
        {
            Assert.Equal(0, Counters.Score(a, a), 9);
            foreach (var b in Ships.All) Assert.Equal(-Counters.Score(b, a), Counters.Score(a, b), 9);
        }
    }

    [Fact]
    public void Flak_screens_beat_craft_torpedoes_and_shells()
    {
        // Two flaks with point defense: small craft, torpedoes and siege shells are what it is for.
        var corvette = Counters.StrongAgainst(ShipClass.Corvette);
        Assert.Contains(ShipClass.Bomber, corvette);
        Assert.Contains(ShipClass.Artillery, corvette);
        Assert.Contains(ShipClass.Corvette, Counters.WeakAgainst(ShipClass.Bomber));
        Assert.Contains(ShipClass.Corvette, Counters.WeakAgainst(ShipClass.Torpedo));
    }

    [Fact]
    public void Torpedoes_and_siege_guns_beat_big_hulls_and_defenses_and_lose_to_small_craft()
    {
        Assert.Contains(ShipClass.Dreadnought, Counters.StrongAgainst(ShipClass.Torpedo));
        // Siege shells: ×2.5 against buildings, the clearest of all its matchups.
        Assert.Null(Counters.StrongAgainst(ShipClass.Artillery)[0]);
        Assert.Contains(Counters.WeakAgainst(ShipClass.Artillery), c => Ships.Defs[c].Size == SizeClass.Small);
        foreach (var big in new[] { ShipClass.Dreadnought, ShipClass.Titan, ShipClass.Leviathan })
            Assert.Contains(Counters.WeakAgainst(big), c => c is ShipClass.Torpedo or ShipClass.Artillery or ShipClass.Bomber);
    }

    [Fact]
    public void Ions_beat_shielded_hulls()
    {
        // Ion bolts: twice their damage to shields; a bulwark is more shield than a fighter is.
        Assert.Contains(ShipClass.Bulwark, Counters.StrongAgainst(ShipClass.Jammer));
        Assert.True(Counters.Score(ShipClass.Jammer, ShipClass.Bulwark) > Counters.Score(ShipClass.Jammer, ShipClass.Fighter));
    }

    [Fact]
    public void Every_class_has_something_said_and_never_against_itself()
    {
        foreach (var c in Ships.All)
        {
            var strong = Counters.StrongAgainst(c);
            var weak = Counters.WeakAgainst(c);
            Assert.True(strong.Count + weak.Count > 0, $"{c}: rien à dire");
            Assert.True(strong.Count <= Counters.Most && weak.Count <= Counters.Most);
            Assert.DoesNotContain((ShipClass?)c, strong);
            Assert.DoesNotContain(c, weak);
            Assert.Empty(strong.OfType<ShipClass>().Intersect(weak));
        }
    }

    [Fact]
    public void A_siege_gun_outranges_every_defense_and_a_frigate_none()
    {
        var defenses = Counters.Defenses();
        Assert.Contains(defenses, d => d.Name == Station.Name);
        Assert.Contains(defenses, d => d.Name == Buildables.Defs[BuildableType.IonCannon].Name);
        Assert.All(defenses, d => Assert.True(Counters.RangeOf(ShipClass.Artillery) > d.Range, d.Name));
        Assert.All(defenses, d => Assert.True(Counters.RangeOf(ShipClass.Frigate) <= d.Range, d.Name));
    }
}
