// The campaign's save slots, a rule of this version only: the automatic save and the named ones
// in a folder, the most recent first, and the single save of older versions moved in once.
using SandboxShip.Sim.Data;
using Xunit;

namespace SandboxShip.Sim.Tests;

public sealed class SaveSlotsTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("sandbox-saves-").FullName;

    public void Dispose() => Directory.Delete(dir, true);

    static Galaxy NewGalaxy(Faction race = Faction.Klaed) =>
        Galaxies.CreateGalaxy(race, [race == Faction.Nairan ? Faction.Klaed : Faction.Nairan], false, errants: false);

    [Fact]
    public void Named_saves_never_overwrite_one_another_and_keep_their_name()
    {
        var g = NewGalaxy();
        var a = SaveSlots.SaveAs(g, dir, "Avant l'assaut", 1000);
        var b = SaveSlots.SaveAs(g, dir, "Avant l'assaut", 2000);
        Assert.Equal("avant-l-assaut", a);
        Assert.Equal("avant-l-assaut-2", b);
        var slots = SaveSlots.List(dir);
        Assert.Equal(["avant-l-assaut-2", "avant-l-assaut"], slots.Select(s => s.Id));
        Assert.All(slots, s => Assert.Equal("Avant l'assaut", s.Info.Name));
        // No name of its own may take the automatic save's file.
        Assert.Equal("sauvegarde", SaveSlots.SaveAs(g, dir, "autosave"));
        Assert.Equal("sauvegarde-2", SaveSlots.SaveAs(g, dir, "  !!  "));
    }

    [Fact]
    public void The_most_recent_save_is_the_one_to_continue_and_unreadable_files_are_left_out()
    {
        var g = NewGalaxy();
        SaveSlots.SaveAs(g, dir, "Vieille", 1000);
        Assert.True(SaveSlots.SaveAuto(g, dir));
        File.WriteAllText(SaveSlots.PathOf(dir, "abimee"), "{ pas une sauvegarde");
        Assert.Equal(SaveSlots.Auto, SaveSlots.Latest(dir)!.Id);
        Assert.Equal(2, SaveSlots.List(dir).Count);
        SaveSlots.Delete(dir, SaveSlots.Auto);
        Assert.Equal("vieille", SaveSlots.Latest(dir)!.Id);
        Assert.Empty(SaveSlots.List(Path.Combine(dir, "absent")));
    }

    [Fact]
    public void A_save_without_a_name_shows_its_race_planets_and_date()
    {
        var when = new DateTimeOffset(2026, 11, 2, 11, 42, 0, TimeSpan.Zero);
        Assert.Equal("Kla'ed, 1 planète, 02/11/2026 11:42", SaveSlots.DefaultName(Faction.Klaed, 1, when));
        Assert.Equal("Nairan, 8 planètes, 02/11/2026 11:42", SaveSlots.DefaultName(Faction.Nairan, 8, when));
        var g = NewGalaxy(Faction.Nairan);
        Assert.True(SaveSlots.SaveAuto(g, dir));
        var auto = SaveSlots.Latest(dir)!;
        Assert.True(auto.IsAuto);
        Assert.StartsWith("Sauvegarde automatique : Nairan, 1 planète, ", SaveSlots.NameOf(auto, TimeSpan.Zero));
    }

    [Fact]
    public void A_new_campaign_keeps_the_last_automatic_save_under_its_own_name()
    {
        Assert.Null(SaveSlots.KeepAuto(dir));
        Assert.True(SaveSlots.SaveAuto(NewGalaxy(), dir));
        var kept = SaveSlots.KeepAuto(dir);
        Assert.NotNull(kept);
        Assert.False(File.Exists(SaveSlots.PathOf(dir, SaveSlots.Auto)));
        Assert.StartsWith("kla-ed-1-planete-", kept);
        Assert.Equal([kept], SaveSlots.List(dir).Select(s => s.Id));
    }

    [Fact]
    public void The_old_single_save_moves_in_once_and_is_kept_aside()
    {
        var old = Path.Combine(dir, "campaign.json");
        var saves = Path.Combine(dir, "saves");
        Assert.True(GalaxySave.Save(NewGalaxy(), old));
        var text = File.ReadAllText(old);
        Assert.True(SaveSlots.Migrate(old, saves));
        Assert.Equal(text, File.ReadAllText(SaveSlots.PathOf(saves, SaveSlots.Auto)));
        Assert.False(File.Exists(old));
        Assert.Equal(text, File.ReadAllText(old + ".bak"));
        // Once moved, there is nothing left to move.
        Assert.False(SaveSlots.Migrate(old, saves));
        // An automatic save already there stays: the old one goes beside it.
        File.Copy(old + ".bak", old);
        Assert.True(SaveSlots.Migrate(old, saves));
        Assert.Equal(2, SaveSlots.List(saves).Count);
    }

    [Fact]
    public void An_old_save_this_version_cannot_read_is_left_as_it_is()
    {
        var old = Path.Combine(dir, "campaign.json");
        File.WriteAllText(old, "{\"version\": 99}");
        Assert.False(SaveSlots.Migrate(old, Path.Combine(dir, "saves")));
        Assert.True(File.Exists(old));
        Assert.False(Directory.Exists(Path.Combine(dir, "saves")));
    }
}
