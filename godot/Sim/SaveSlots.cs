// The campaign's saves, in a folder: the automatic one, written as the campaign goes, and those
// the player saves under a name of their own, each in a file of its own. "Continuer la campagne"
// takes the most recent of them. The single save of older versions is moved in once.
using System.Globalization;
using System.Text;
using SandboxShip.Sim.Data;

namespace SandboxShip.Sim;

public static class SaveSlots
{
    /// <summary>The automatic save's id, and file name without its extension.</summary>
    public const string Auto = "autosave";

    /// <summary>A save on disk: its id (its file name without the extension), its path, what it holds.</summary>
    public sealed record Slot(string Id, string Path, SaveInfo Info)
    {
        public bool IsAuto => Id == Auto;
    }

    public static string PathOf(string dir, string id) => System.IO.Path.Combine(dir, id + ".json");

    /// <summary>The saves that can be read, the most recent first; the others (another version, damaged) are left out.</summary>
    public static List<Slot> List(string dir)
    {
        if (!Directory.Exists(dir)) return [];
        var slots = new List<Slot>();
        foreach (var path in Directory.EnumerateFiles(dir, "*.json"))
        {
            if (GalaxySave.Info(path) is { } info) slots.Add(new Slot(System.IO.Path.GetFileNameWithoutExtension(path), path, info));
        }
        return [.. slots.OrderByDescending(s => s.Info.SavedAt).ThenBy(s => s.Id, StringComparer.Ordinal)];
    }

    /// <summary>The save "Continuer la campagne" takes: the most recent one.</summary>
    public static Slot? Latest(string dir) => List(dir).FirstOrDefault();

    /// <summary>What a save is called by default: its race, its planets, when; "Kla'ed, 8 planètes, 02/11/2026 11:42".</summary>
    public static string DefaultName(Faction race, int planets, DateTimeOffset when) =>
        $"{Factions.Defs[race].Name}, {planets} planète{(planets > 1 ? "s" : "")}, {when.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)}";

    /// <summary>The name a save shows: the player's, else its default one; the automatic save says that it is one.</summary>
    public static string NameOf(Slot s, TimeSpan offset)
    {
        var when = DateTimeOffset.FromUnixTimeMilliseconds(s.Info.SavedAt).ToOffset(offset);
        var name = string.IsNullOrWhiteSpace(s.Info.Name) ? DefaultName(s.Info.Race, s.Info.Planets, when) : s.Info.Name.Trim();
        return s.IsAuto ? $"Sauvegarde automatique : {name}" : name;
    }

    /// <summary>
    /// Saves under the player's name for it, in a file of its own: never over another save, even
    /// one of the same name. Returns its id, or null when it cannot be written.
    /// </summary>
    public static string? SaveAs(Galaxy g, string dir, string name, long? savedAt = null)
    {
        try
        {
            Directory.CreateDirectory(dir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        var id = NewId(dir, name);
        return GalaxySave.Save(g, PathOf(dir, id), name.Trim(), savedAt) ? id : null;
    }

    /// <summary>Writes the automatic save; false when it cannot be written.</summary>
    public static bool SaveAuto(Galaxy g, string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        return GalaxySave.Save(g, PathOf(dir, Auto));
    }

    public static void Delete(string dir, string id) => GalaxySave.Clear(PathOf(dir, id));

    /// <summary>
    /// Before another campaign takes the automatic save, the one there moves to a file of its
    /// own, under its default name: a new campaign never wipes out the last one. Returns its new
    /// id, or null when there was none to keep.
    /// </summary>
    public static string? KeepAuto(string dir)
    {
        var auto = PathOf(dir, Auto);
        if (GalaxySave.Info(auto) is not { } info) return null;
        var when = DateTimeOffset.FromUnixTimeMilliseconds(info.SavedAt);
        var id = NewId(dir, DefaultName(info.Race, info.Planets, when));
        try
        {
            File.Move(auto, PathOf(dir, id));
            return id;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Brings the single save of older versions (`oldPath`) into the folder: as the automatic save,
    /// or beside it when there is one already. The copy is read back before the old file is put
    /// aside, renamed with ".bak": nothing is ever deleted. An old file this version cannot read
    /// is left as it is. Returns whether a save was brought in.
    /// </summary>
    public static bool Migrate(string oldPath, string dir)
    {
        if (GalaxySave.Info(oldPath) is not { } info) return false;
        try
        {
            Directory.CreateDirectory(dir);
            var target = File.Exists(PathOf(dir, Auto))
                ? PathOf(dir, NewId(dir, DefaultName(info.Race, info.Planets, DateTimeOffset.FromUnixTimeMilliseconds(info.SavedAt))))
                : PathOf(dir, Auto);
            File.Copy(oldPath, target);
            if (GalaxySave.Info(target) is null)
            {
                File.Delete(target);
                return false;
            }
            File.Move(oldPath, oldPath + ".bak", true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>A file name made from `name`, free in `dir`: plain letters and digits, never the automatic save's.</summary>
    static string NewId(string dir, string name)
    {
        var slug = Slug(name);
        if (slug.Length == 0 || slug == Auto) slug = "sauvegarde";
        var id = slug;
        for (var n = 2; File.Exists(PathOf(dir, id)); n++) id = $"{slug}-{n}";
        return id;
    }

    /// <summary>"Kla'ed, 8 planètes" → "kla-ed-8-planetes": accents dropped, anything else a dash, at most 48 characters.</summary>
    static string Slug(string name)
    {
        var plain = new StringBuilder();
        foreach (var c in name.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            plain.Append(c is >= 'a' and <= 'z' or >= '0' and <= '9' ? c : c is >= 'A' and <= 'Z' ? char.ToLowerInvariant(c) : '-');
        }
        var slug = string.Join('-', plain.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length > 48 ? slug[..48].TrimEnd('-') : slug;
    }
}
