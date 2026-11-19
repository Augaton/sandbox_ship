// The campaign tutorial's galaxy (TutorialGalaxy.cs): what each of its steps needs is there, the
// rival leaves the player alone meanwhile, and above all it never writes over nor clears a save of
// the player's: a real one was lost so once. A rule of this version only.
using SandboxShip.Sim.Data;
using Xunit;

namespace SandboxShip.Sim.Tests;

public sealed class TutorialGalaxyTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("sandbox-tutorial-").FullName;

    public void Dispose() => Directory.Delete(dir, true);

    static Side Player => TutorialGalaxy.Race;

    [Fact]
    public void Its_galaxy_is_the_classic_one_with_all_each_step_needs()
    {
        var g = TutorialGalaxy.Create();
        Assert.True(g.Tutorial);
        Assert.Equal(GalaxyMap.Planets.Count, g.Planets.Count);
        Assert.Equal(Player, g.Player);
        Assert.Equal(Player, g.Planets[TutorialGalaxy.Capital].Owner);
        // Production, then research: a laboratory at the capital, and the credits for it all.
        Assert.True(Commands.Apply(g, Player, new GalaxyCommand.EnqueueBuild(TutorialGalaxy.Capital, GalaxyBuild.Ship(ShipClass.Frigate))).Ok);
        Assert.True(Commands.Apply(g, Player, new GalaxyCommand.StartTech(TechId.Weapons)).Ok);
        // Two independent neighbours: one to take by arms, one to win over.
        foreach (var p in new[] { TutorialGalaxy.Target, TutorialGalaxy.Courted })
        {
            Assert.Equal(Galaxies.Independent, g.Planets[p].Owner);
            Assert.Contains(p, Galaxies.Neighbours(g, TutorialGalaxy.Capital));
        }
        Assert.True(Commands.Apply(g, Player, new GalaxyCommand.Court(TutorialGalaxy.Courted)).Ok);
        // A rival to talk to, at peace with the player for longer than the tutorial lasts.
        Assert.NotNull(g.Factions[TutorialGalaxy.Rival].AI);
        Assert.Contains(g.Truces, t => t.Opening && t.Remaining >= TutorialGalaxy.Peace);
    }

    [Fact]
    public void Its_first_battle_leaves_a_reserve_under_the_cap()
    {
        var g = TutorialGalaxy.Create();
        var fleet = Galaxies.FleetsAt(g, TutorialGalaxy.Capital, Player).Single();
        Assert.True(Battle.CountShips(fleet.Ships) > 0);
        var points = Data.Ships.All.Sum(c => Reserves.Points(c) * fleet.Ships.Of(c));
        Assert.True(points > Reserves.CapPoints(TutorialGalaxy.Cap) + 10, $"{points} points");
        Assert.True(Commands.Apply(g, Player, new GalaxyCommand.SendFleet(fleet.Id, TutorialGalaxy.Target)).Ok);
        for (var t = 0; t < 120 && !Galaxies.WaitingForPlayer(g); t++) Galaxies.RunGalaxy(g, 1);
        var b = Assert.Single(g.Battles, x => x.PlanetId == TutorialGalaxy.Target);
        Assert.Equal(BattleStatus.Pending, b.Status);
        Assert.True(Commands.Apply(g, Player, new GalaxyCommand.ChooseBattle(b.Id, BattleStatus.Tactical)).Ok);
        Assert.Equal(Reserves.CapPoints(TutorialGalaxy.Cap), b.World.FleetCap);
        Assert.NotEmpty(b.World.Factions[Player].Reserve);
    }

    [Fact]
    public void The_rival_leaves_the_player_alone_while_they_learn()
    {
        var g = TutorialGalaxy.Create();
        Galaxies.RunGalaxy(g, 20 * 60);
        Assert.Null(g.Winner);
        Assert.Equal(Player, g.Planets[TutorialGalaxy.Capital].Owner);
        Assert.DoesNotContain(g.Battles, b => b.Attacker == TutorialGalaxy.Rival && b.Defender == Player);
        Assert.NotEqual(TutorialGalaxy.Rival, g.Planets[TutorialGalaxy.Courted].Owner);
        Assert.NotEqual(TutorialGalaxy.Rival, g.Planets[TutorialGalaxy.Target].Owner);
    }

    [Fact]
    public void It_never_writes_over_nor_clears_a_save_of_the_player()
    {
        // The player's own campaign: its automatic save and one kept under a name.
        var mine = Galaxies.CreateGalaxy(Faction.Nautolan, [Faction.Klaed], false, errants: false);
        Assert.True(SaveSlots.SaveAuto(mine, dir));
        Assert.NotNull(SaveSlots.SaveAs(mine, dir, "Ma campagne", 1000));
        var before = Snapshot();

        var tutorial = TutorialGalaxy.Create();
        Assert.False(SaveSlots.SaveAuto(tutorial, dir));
        Assert.Null(SaveSlots.SaveAs(tutorial, dir, "Didacticiel"));
        Assert.False(GalaxySave.Save(tutorial, SaveSlots.PathOf(dir, SaveSlots.Auto)));
        Assert.False(GalaxySave.Save(tutorial, Path.Combine(dir, "ailleurs.json")));
        // Played on, as the screen does: it would save every 20 s and when its battles end.
        for (var t = 0; t < 30; t++)
        {
            Galaxies.RunGalaxy(tutorial, 20);
            Assert.False(SaveSlots.SaveAuto(tutorial, dir));
        }
        Assert.Equal(before, Snapshot());
        Assert.Equal(Faction.Nautolan, SaveSlots.Latest(dir)!.Info.Race);

        // Whatever the run, the screen gets no file for it: none to save in, none to move aside
        // before it starts (KeepAuto), none to clear when it ends.
        foreach (var network in new[] { false, true })
            foreach (var replica in new[] { false, true })
                foreach (var capture in new[] { false, true })
                    foreach (var own in new[] { false, true })
                        Assert.Null(SaveSlots.CampaignFile(true, network, replica, capture, own, dir, Path.Combine(dir, "net.json")));
        // Other campaigns keep theirs.
        Assert.Equal(SaveSlots.PathOf(dir, SaveSlots.Auto), SaveSlots.CampaignFile(false, false, false, false, false, dir, "net.json"));
        Assert.Null(SaveSlots.CampaignFile(false, false, false, true, false, dir, "net.json"));
        Assert.Equal("net.json", SaveSlots.CampaignFile(false, true, false, false, false, dir, "net.json"));
        Assert.Equal("net.json", SaveSlots.CampaignFile(false, true, false, true, true, dir, "net.json"));
        Assert.Null(SaveSlots.CampaignFile(false, true, true, false, false, dir, "net.json"));
    }

    /// <summary>Every file of the save folder, by name, with its bytes.</summary>
    string Snapshot() => string.Join("\n", Directory.EnumerateFiles(dir).Order(StringComparer.Ordinal)
        .Select(f => $"{Path.GetFileName(f)}:{Convert.ToBase64String(File.ReadAllBytes(f))}"));
}
