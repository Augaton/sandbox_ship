// The main menu's backdrop fight: random sides that meet and fight it out to the end.
using SandboxShip.Sim.Data;
using Xunit;

namespace SandboxShip.Sim.Tests;

public class ShowcaseTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Random_fleets_meet_and_fight_until_one_side_is_left(int seed)
    {
        var w = Showcase.Create(seed);
        var sides = Showcase.Standing(w);
        Assert.InRange(sides, 2, 3);
        Assert.Equal(sides, w.Factions.Count);
        Assert.False(w.Fog);
        var start = w.Entities.Count(e => e.Kind == EntityKind.Ship);
        Expect.Greater(start, 10);
        // As the menu does: steps, and the idle sent back into the fight every two seconds.
        while (w.Time < 240 && Showcase.Standing(w) > 1)
        {
            Steps.Run(w, 2);
            Showcase.Steer(w);
        }
        Assert.Equal(1, Showcase.Standing(w));
        Assert.Null(w.Winner);
    }

    [Fact]
    public void The_same_seed_gives_the_same_fight()
    {
        var a = Showcase.Create(9);
        var b = Showcase.Create(9);
        Assert.Equal(a.Factions.Keys, b.Factions.Keys);
        Assert.Equal(a.Entities.Select(e => (e.Type, e.X, e.Y)), b.Entities.Select(e => (e.Type, e.X, e.Y)));
    }
}
