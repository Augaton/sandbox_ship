// Battle saves (not in the web version): a battle of a lobby played alone, kept on disk in the
// middle and taken up again exactly where it stood, its AIs, research, reserve, mode, fog,
// veterans and random generator included. The world is written whole (StateWire), compressed,
// after a head that the list of saves reads alone: when, which battle (its lobby's options), how
// long it has run, the player's view. Reading never trusts the bytes: a file that is not a save,
// of another version, cut short, changed by a byte or forged into a world the battle cannot play
// is refused with InvalidDataException or EndOfStreamException, its message for the player.
// A folder holds them, as the campaign's (SaveSlots): those saved under a name, and one automatic
// save for each battle left for the menu, the last few kept. Nothing here touches the campaign's.
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace SandboxShip.Sim;

/// <summary>
/// What the list shows of a battle save, from its head alone: the version that wrote it, when, the
/// name the player gave it (empty for none), which battle it is (BattleSaves.NewBattleId), whose,
/// whether it was played as the sandbox, the lobby's options, and how long the battle had run.
/// </summary>
public sealed record BattleSaveInfo(string Version, long SavedAt, string Name, string Battle, Side Player, bool Sandbox, SkirmishOptions Options, double Seconds);

/// <summary>The player's view when the battle was saved: the camera's center and zoom, and the control groups (digit → ships).</summary>
public sealed record BattleView(double X, double Y, double Zoom, IReadOnlyDictionary<int, int[]> Groups);

/// <summary>
/// What a save says of itself beside the world: whose, which battle, whether played as the sandbox
/// (every battle played alone has the sandbox's tools, World.SandboxTools: the mode is the
/// screen's), the view, the game's version, when, the player's name for it (empty for none).
/// </summary>
public sealed record BattleSaveHead(Side Player, string Battle, bool Sandbox, BattleView View, string Version, long SavedAt, string Name = "");

/// <summary>A battle save read whole: its head, the world as it stood, the view.</summary>
public sealed record BattleSave(BattleSaveInfo Info, World World, BattleView View);

public static class BattleSaves
{
    /// <summary>The layout below; the world's own is checked type by type (StateWire).</summary>
    public const int FormatVersion = 1;
    /// <summary>"SSBS", the file's first four bytes.</summary>
    const uint Magic = 0x53425353;
    public const string Extension = ".ssbattle";
    /// <summary>The automatic saves' ids: this, then the battle's.</summary>
    public const string AutoPrefix = "auto-";
    /// <summary>Automatic saves kept, one a battle: past these, the oldest go. Saves under a name stay until the player deletes them.</summary>
    public const int AutosKept = 5;
    public const int MaxName = 60;
    /// <summary>Steps a save read is played on a copy of its world before it is taken: one forged into a battle that breaks is refused here, not once shown.</summary>
    public const int TrialSteps = 60;
    /// <summary>The world's bytes, at most, and at most this many times what they were packed into: no file makes the game ask for more.</summary>
    const int MaxRaw = 512 << 20, MaxRatio = 50;
    /// <summary>Room around the map where an entity of a save may stand.</summary>
    const double Margin = 3000;
    const int MaxGroups = 10, MaxGroupShips = 4096;

    // What the player reads when a save is refused (the game translates them).
    public const string Unreadable = "Fichier illisible.";
    public const string NotASave = "Ce fichier n'est pas une sauvegarde de bataille.";
    public const string OtherVersion = "Sauvegarde d'une autre version du jeu, illisible par celle-ci.";
    public const string Damaged = "Sauvegarde abîmée.";

    /// <summary>A new battle's id: 16 hexadecimal digits, drawn from the system (never from the battle's generator).</summary>
    public static string NewBattleId() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));

    /// <summary>Whether a text is a battle id as NewBattleId makes them: it names files.</summary>
    public static bool IsBattleId(string? s) => s is { Length: > 0 and <= 32 } && s.All(char.IsAsciiHexDigitLower);

    /// <summary>Whether a world can be saved: one made from a lobby's options (Scenario.CreateSkirmish), still undecided.</summary>
    public static bool CanSave(World w) => w.Origin is not null && w.Winner is null;

    public static void Write(Stream s, World w, BattleSaveHead h)
    {
        if (!CanSave(w) || !IsBattleId(h.Battle)) throw new ArgumentException("Bataille impossible à sauvegarder.");
        var raw = StateWire.Write(w);
        var packed = Pack(raw);
        using var bw = new BinaryWriter(s, Encoding.UTF8, leaveOpen: true);
        bw.Write(Magic);
        bw.Write(FormatVersion);
        bw.Write(h.Version);
        bw.Write(h.SavedAt);
        bw.Write(h.Name.Trim() is var n && n.Length > MaxName ? n[..MaxName] : n);
        bw.Write(h.Battle);
        bw.Write7BitEncodedInt(h.Player.Id);
        bw.Write(h.Sandbox);
        Replays.WriteOptions(bw, w.Origin!);
        bw.Write(w.Time);
        var view = h.View;
        bw.Write(view.X);
        bw.Write(view.Y);
        bw.Write(view.Zoom);
        var groups = view.Groups.Where(g => g.Key is >= 0 and < MaxGroups).OrderBy(g => g.Key).ToList();
        bw.Write7BitEncodedInt(groups.Count);
        foreach (var (digit, ships) in groups)
        {
            bw.Write7BitEncodedInt(digit);
            bw.Write7BitEncodedInt(Math.Min(ships.Length, MaxGroupShips));
            foreach (var id in ships.Take(MaxGroupShips)) bw.Write7BitEncodedInt(id);
        }
        bw.Write7BitEncodedInt(raw.Length);
        bw.Write(SHA256.HashData(raw));
        bw.Write7BitEncodedInt(packed.Length);
        bw.Write(packed);
    }

    static byte[] Pack(byte[] raw)
    {
        using var ms = new MemoryStream();
        using (var z = new BrotliStream(ms, CompressionLevel.Fastest, true)) z.Write(raw);
        return ms.ToArray();
    }

    /// <summary>The head: what the list shows, then the view. InvalidDataException for anything that is not one.</summary>
    static BattleSaveInfo Head(BinaryReader r, out BattleView view)
    {
        if (r.BaseStream.Length < 8 || r.ReadUInt32() != Magic) throw new InvalidDataException(NotASave);
        if (r.ReadInt32() != FormatVersion) throw new InvalidDataException(OtherVersion);
        var version = r.ReadText(64);
        var savedAt = r.ReadInt64();
        var name = r.ReadText(MaxName * 4);
        var battle = r.ReadText(64);
        var player = r.ReadSide();
        var sandbox = r.ReadBoolean();
        var options = Replays.ReadOptions(r, Replays.FormatVersion);
        var seconds = r.ReadDouble();
        var (x, y, zoom) = (r.ReadDouble(), r.ReadDouble(), r.ReadDouble());
        var count = r.ReadVarint();
        if (!IsBattleId(battle) || !double.IsFinite(seconds) || seconds < 0 || count is < 0 or > MaxGroups) throw new InvalidDataException(Damaged);
        var groups = new Dictionary<int, int[]>();
        for (var i = 0; i < count; i++)
        {
            var digit = r.ReadVarint();
            var n = r.ReadVarint();
            if (digit is < 0 or >= MaxGroups || n is < 0 or > MaxGroupShips || groups.ContainsKey(digit)) throw new InvalidDataException(Damaged);
            var ships = new int[n];
            for (var k = 0; k < n; k++) ships[k] = r.ReadVarint();
            groups[digit] = ships;
        }
        // The view is only where the camera was: anything odd puts it back on the player's station.
        view = new BattleView(double.IsFinite(x) ? x : double.NaN, double.IsFinite(y) ? y : double.NaN, double.IsFinite(zoom) ? zoom : 1, groups);
        return new BattleSaveInfo(version, savedAt, name, battle, player, sandbox, options, seconds);
    }

    /// <summary>The head alone, for the list of saves.</summary>
    public static BattleSaveInfo ReadInfo(Stream s)
    {
        using var r = new BinaryReader(s, Encoding.UTF8, leaveOpen: true);
        return Head(r, out _);
    }

    /// <summary>A whole save: its head, then its world, unpacked, checked against its digest, read, checked, and tried.</summary>
    public static BattleSave Read(Stream s)
    {
        using var r = new BinaryReader(s, Encoding.UTF8, leaveOpen: true);
        var info = Head(r, out var view);
        var length = r.ReadVarint();
        var digest = r.ReadBytes(32);
        var packedLength = r.ReadVarint();
        if (digest.Length != 32 || packedLength < 0 || packedLength > r.BaseStream.Length - r.BaseStream.Position
            || length < 0 || length > MaxRaw || length > (long)packedLength * MaxRatio) throw new InvalidDataException(Damaged);
        var raw = Unpack(r.ReadBytes(packedLength), length);
        if (!SHA256.HashData(raw).AsSpan().SequenceEqual(digest)) throw new InvalidDataException(Damaged);
        var world = StateWire.Read<World>(raw);
        Check(world, info);
        Try(raw);
        return new BattleSave(info, world, view);
    }

    /// <summary>The packed bytes back, exactly `length` of them: more or fewer is a damaged file.</summary>
    static byte[] Unpack(byte[] packed, int length)
    {
        try
        {
            using var z = new BrotliStream(new MemoryStream(packed, false), CompressionMode.Decompress);
            using var raw = new MemoryStream();
            var buffer = new byte[81920];
            int n;
            while ((n = z.Read(buffer)) > 0)
            {
                if (raw.Length + n > length) throw new InvalidDataException(Damaged);
                raw.Write(buffer, 0, n);
            }
            return raw.Length == length ? raw.ToArray() : throw new InvalidDataException(Damaged);
        }
        catch (InvalidOperationException)
        {
            // What Brotli says of bytes that do not unpack.
            throw new InvalidDataException(Damaged);
        }
    }

    /// <summary>
    /// What the battle takes for granted of its world, which a forged save could break: no null where
    /// none goes, the map's size, every side owning something there, entities on the map with a known
    /// type, found under their own id. A game of another version was refused before (StateWire).
    /// </summary>
    static void Check(World w, BattleSaveInfo info)
    {
        static bool Finite(double v) => double.IsFinite(v);
        bool OnMap(double x, double y) => Finite(x) && Finite(y) && x >= -Margin && y >= -Margin && x <= w.Width + Margin && y <= w.Height + Margin;
        bool Known(Entity e) => e.Kind switch
        {
            EntityKind.Ship => Ids.IsShip(e.Type),
            EntityKind.Hardpoint => Ids.IsRole(e.Type),
            _ => e.Type == "station" || Ids.IsBuildable(e.Type, out _),
        };
        var sane = GalaxySave.Complete(w) && w.Origin is not null
            && w.Width is >= 1000 and <= 50_000 && w.Height is >= 1000 and <= 50_000 && Finite(w.Time) && w.Time >= 0 && w.StepCount >= 0
            && w.Factions.ContainsKey(info.Player) && w.Factions.All(f => f.Key == f.Value.Id && Finite(f.Value.Credits))
            && w.Entities.Consistent() && w.Entities.All(e => e.Id > 0 && e.Id < w.NextId && Known(e) && w.Factions.ContainsKey(e.Side)
                && OnMap(e.X, e.Y) && e.Radius is > 0 and <= 2000 && Finite(e.Angle) && Finite(e.Speed) && Finite(e.Hull) && Finite(e.MaxHull) && Finite(e.Shield))
            && w.Projectiles.TrueForAll(p => OnMap(p.X, p.Y) && Finite(p.Vx) && Finite(p.Vy) && Finite(p.Speed))
            && w.Jumps.TrueForAll(j => OnMap(j.X, j.Y) && w.Factions.ContainsKey(j.Side))
            && w.Fields.TrueForAll(f => OnMap(f.X, f.Y) && f.Radius is > 0 and <= 5000)
            && w.Terrain.TrueForAll(t => OnMap(t.X, t.Y) && OnMap(t.X2, t.Y2) && t.Radius is > 0 and <= 5000);
        if (!sane) throw new InvalidDataException(Damaged);
    }

    /// <summary>A copy of the world read again from its bytes, played a second: a save whose battle breaks in it is refused.</summary>
    static void Try(byte[] raw)
    {
        try
        {
            var copy = StateWire.Read<World>(raw);
            for (var i = 0; i < TrialSteps; i++)
            {
                Steps.StepWorld(copy);
                copy.Events.Clear();
            }
        }
        catch (Exception e) when (e is not (InvalidDataException or EndOfStreamException or OutOfMemoryException))
        {
            throw new InvalidDataException(Damaged);
        }
    }

    // The folder.

    /// <summary>A save on disk: its id (its file name without the extension), its path, its head, or why it cannot be read.</summary>
    public sealed record Slot(string Id, string Path, BattleSaveInfo? Info, string? Problem)
    {
        public bool IsAuto => Id.StartsWith(AutoPrefix, StringComparison.Ordinal);
        /// <summary>When it was saved: its head's time, else the file's.</summary>
        public long SavedAt { get; init; }
    }

    public static string PathOf(string dir, string id) => System.IO.Path.Combine(dir, id + Extension);

    public static string AutoId(string battle) => AutoPrefix + battle;

    /// <summary>The saves, the most recent first; one that cannot be read is listed with why, at the time of its file.</summary>
    public static List<Slot> List(string dir)
    {
        if (!Directory.Exists(dir)) return [];
        var slots = new List<Slot>();
        foreach (var path in Directory.EnumerateFiles(dir, "*" + Extension))
        {
            var id = System.IO.Path.GetFileNameWithoutExtension(path);
            try
            {
                using var file = File.OpenRead(path);
                var info = ReadInfo(file);
                slots.Add(new Slot(id, path, info, null) { SavedAt = info.SavedAt });
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                var problem = e is InvalidDataException ? e.Message : e is EndOfStreamException ? Damaged : Unreadable;
                slots.Add(new Slot(id, path, null, problem) { SavedAt = Written(path) });
            }
        }
        return [.. slots.OrderByDescending(s => s.SavedAt).ThenBy(s => s.Id, StringComparer.Ordinal)];
    }

    static long Written(string path)
    {
        try
        {
            return new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeMilliseconds();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>A save read whole; null with the reason the player reads when it cannot be.</summary>
    public static BattleSave? Load(string path, out string? problem)
    {
        problem = null;
        try
        {
            using var file = File.OpenRead(path);
            return Read(file);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            problem = e is InvalidDataException ? e.Message : e is EndOfStreamException ? Damaged : Unreadable;
            return null;
        }
    }

    /// <summary>Writes a save aside, then moves it in place: a crash mid-write never eats the one there. False when it cannot be written.</summary>
    public static bool Save(string path, World w, BattleSaveHead h)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            using (var file = File.Create(temp)) Write(file, w, h);
            File.Move(temp, path, true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Saves under the player's name for it (the head's), in a file of its own, never over another. Returns its id, or null when it cannot be written.</summary>
    public static string? SaveAs(string dir, World w, BattleSaveHead h)
    {
        try
        {
            Directory.CreateDirectory(dir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        var slug = SaveSlots.Slug(h.Name);
        if (slug.Length == 0 || slug.StartsWith(AutoPrefix, StringComparison.Ordinal)) slug = "bataille-" + slug;
        var id = slug.TrimEnd('-');
        for (var n = 2; File.Exists(PathOf(dir, id)); n++) id = $"{slug.TrimEnd('-')}-{n}";
        return Save(PathOf(dir, id), w, h) ? id : null;
    }

    /// <summary>Writes the battle's automatic save (nameless), over its last one, then lets the oldest automatic saves of other battles go. False when it cannot be written.</summary>
    public static bool SaveAuto(string dir, World w, BattleSaveHead h)
    {
        if (!Save(PathOf(dir, AutoId(h.Battle)), w, h with { Name = "" })) return false;
        foreach (var old in List(dir).Where(s => s.IsAuto).Skip(AutosKept)) Delete(dir, old.Id);
        return true;
    }

    /// <summary>A battle that ended has nothing left to take up: its automatic save goes. Those saved under a name stay.</summary>
    public static void DeleteAuto(string dir, string battle) => Delete(dir, AutoId(battle));

    public static void Delete(string dir, string id)
    {
        try
        {
            File.Delete(PathOf(dir, id));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Nothing to delete, or nothing we may delete: the list says what is left.
        }
    }

    /// <summary>What a save is called by default: its mode, the player's race, how long, when; "Escarmouche, Kla'ed, 23 min, 19/11/2026 14:05".</summary>
    public static string DefaultName(string mode, Data.Faction race, double seconds, DateTimeOffset when) =>
        $"{mode}, {Data.Factions.Defs[race].Name}, {(int)(seconds / 60)} min, {when.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)}";
}
