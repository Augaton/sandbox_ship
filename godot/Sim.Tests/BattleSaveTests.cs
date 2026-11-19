// Battle saves, a feature of this version only: a battle saved in the middle and read back plays
// on exactly as the one left running would, AIs, research, reserve, modes, fog, veterans and the
// random generator included.
using SandboxShip.Sim.Data;
using Xunit;
using Xunit.Abstractions;
using static SandboxShip.Sim.Worlds;

namespace SandboxShip.Sim.Tests;

public class BattleSaveTests(ITestOutputHelper output)
{
    static readonly Side Player = Faction.Klaed;

    /// <summary>A battle of `mode` where every side plays on the AI, the player's too, so that a lot happens.</summary>
    internal static World Battle(BattleMode mode, int seed, MapSize size = MapSize.Small, TerrainDensity terrain = TerrainDensity.Light)
    {
        var o = new SkirmishOptions
        {
            Mode = mode, Seed = seed, Size = size, Raid = mode is BattleMode.Standard or BattleMode.Regicide, AssaultMinutes = 6, ZoneTarget = 100, Terrain = terrain,
            Slots = mode switch
            {
                BattleMode.Assault => [new(Faction.Klaed, Faction.Klaed, Assault.Defenders, Difficulty.Normal), new(Faction.Nairan, Faction.Nairan, Assault.Attackers, Difficulty.Hard)],
                BattleMode.Survival => [new(Faction.Klaed, Faction.Klaed, 1, Difficulty.Normal), new(Faction.Nautolan, Faction.Nautolan, 1, Difficulty.Hard)],
                BattleMode.Standard => [new(Faction.Klaed, Faction.Klaed, 0, Difficulty.Normal, Personality: Personality.Technological), new(Faction.Nairan, Faction.Nairan, 0, Difficulty.Normal, Personality: Personality.Aggressive), new(Faction.Nautolan, Faction.Nautolan, 0, Difficulty.Hard)],
                _ => [new(Faction.Klaed, Faction.Klaed, 0, Difficulty.Normal), new(Faction.Nairan, Faction.Nairan, 0, Difficulty.Hard)],
            },
        };
        var w = Scenario.CreateSkirmish(o);
        Assert.True(Replays.Record(w));
        return w;
    }

    static void Run(World w, double seconds)
    {
        var steps = (int)Math.Round(seconds / Steps.Step);
        for (var i = 0; i < steps; i++)
        {
            Steps.StepWorld(w);
            w.Events.Clear();
        }
    }

    /// <summary>
    /// The strong test: N seconds played, the battle written and read back, then M more seconds on
    /// the original and on the copy: both come to the very same state, byte for byte.
    /// </summary>
    [Theory]
    [InlineData(BattleMode.Standard, 3, 420, 120)]
    [InlineData(BattleMode.Survival, 4, 330, 90)]
    [InlineData(BattleMode.Zones, 5, 300, 90)]
    [InlineData(BattleMode.Assault, 6, 240, 90)]
    [InlineData(BattleMode.Regicide, 7, 300, 90)]
    public void A_battle_read_back_plays_on_exactly_as_the_one_left_running(BattleMode mode, int seed, double before, double after)
    {
        var w = Battle(mode, seed);
        Run(w, before);
        // What the save must carry, there to be carried: a veteran, a ship in hyperspace.
        var ship = w.Entities.First(e => e.Side == Player && e.Kind == EntityKind.Ship && !e.Launched);
        Veterans.Promote(w, ship, 2, news: false);
        var bytes = StateWire.Write(w);
        var copy = StateWire.Read<World>(bytes);
        Assert.Equal(bytes, StateWire.Write(copy));
        Assert.Null(Diff(w, copy));
        Run(w, after);
        Run(copy, after);
        Assert.Null(Diff(w, copy));
        Assert.Equal(Replays.Digest(w), Replays.Digest(copy));
    }

    static readonly BattleView View = new(1234.5, 987.25, 0.75, new Dictionary<int, int[]> { [1] = [5, 6, 7], [4] = [12] });

    static byte[] File(World w, string name = "", string battle = "0123456789abcdef", long savedAt = 1_800_000_000_000, bool sandbox = false)
    {
        using var ms = new MemoryStream();
        BattleSaves.Write(ms, w, new BattleSaveHead(Player, battle, sandbox, View, "test", savedAt, name));
        return ms.ToArray();
    }

    [Fact]
    public void A_save_file_holds_its_head_its_view_and_the_battle_as_it_stood()
    {
        var w = Battle(BattleMode.Zones, 11);
        Run(w, 150);
        var bytes = File(w, "Avant l'assaut");
        var info = BattleSaves.ReadInfo(new MemoryStream(bytes));
        Assert.Equal(("test", 1_800_000_000_000L, "Avant l'assaut", "0123456789abcdef", Player, false), (info.Version, info.SavedAt, info.Name, info.Battle, info.Player, info.Sandbox));
        Assert.Equal((BattleMode.Zones, 11, MapSize.Small), (info.Options.Mode, info.Options.Seed, info.Options.Size));
        Assert.Equal(w.Origin!.Slots, info.Options.Slots);
        Assert.Equal(w.Time, info.Seconds);
        var save = BattleSaves.Read(new MemoryStream(bytes));
        Assert.Null(Diff(w, save.World));
        Assert.Equal((View.X, View.Y, View.Zoom), (save.View.X, save.View.Y, save.View.Zoom));
        Assert.Equal(View.Groups[1], save.View.Groups[1]);
        Assert.Equal(View.Groups[4], save.View.Groups[4]);
        // Read back, the battle goes on as the one left running.
        Run(w, 30);
        Run(save.World, 30);
        Assert.Null(Diff(w, save.World));
    }

    [Fact]
    public void The_sandbox_saves_too_with_what_its_tools_made()
    {
        var w = Scenario.CreateSkirmish(new SkirmishOptions { Seed = 21, Size = MapSize.Small, Credits = 50000, Fog = false, Slots = [new(Faction.Klaed, Faction.Klaed), new(Faction.Nairan, Faction.Nairan)] });
        w.SandboxTools = true;
        Assert.True(Replays.Record(w));
        Assert.True(Commands.Apply(w, Player, new Command.Spawn(Faction.Nairan, "dreadnought", 1500, 1500, 0)).Ok);
        Assert.True(Commands.Apply(w, Player, new Command.SetAI(Faction.Nairan, Difficulty.Hard)).Ok);
        Run(w, 20);
        var save = BattleSaves.Read(new MemoryStream(File(w, sandbox: true)));
        Assert.True(save.Info.Sandbox && save.World.SandboxTools);
        Run(w, 20);
        Run(save.World, 20);
        Assert.Null(Diff(w, save.World));
    }

    /// <summary>
    /// The battle's replay goes on in the saved one: its commands from the very start are in the save,
    /// and the battle read back, played on with more commands, is the one its replay plays again.
    /// </summary>
    [Fact]
    public void The_replay_being_recorded_goes_on_in_the_battle_read_back()
    {
        var w = Battle(BattleMode.Standard, 8);
        Assert.True(Commands.Apply(w, Player, new Command.EnqueueShip(ShipClass.Frigate, 2)).Ok);
        Run(w, 60);
        var save = BattleSaves.Read(new MemoryStream(File(w))).World;
        Assert.Single(save.Log!.Commands);
        var ships = save.Entities.Where(e => e.Side == Player && e.Kind == EntityKind.Ship).Select(e => e.Id).ToArray();
        Assert.True(Commands.Apply(save, Player, new Command.Move(ships, save.Width / 2, save.Height / 2, true)).Ok);
        Run(save, 60);
        var replay = Replays.Of(save, Player, "test", 1)!;
        Assert.Equal(2, replay.Commands.Count);
        var player = new ReplayPlayer(replay);
        player.SkipTo(long.MaxValue);
        Assert.True(player.Faithful);
    }

    [Fact]
    public void A_damaged_cut_or_stranger_file_is_refused_with_the_reason()
    {
        var w = Battle(BattleMode.Standard, 9);
        Run(w, 30);
        var bytes = File(w);
        string Refusal(byte[] b)
        {
            var e = Record.Exception(() => BattleSaves.Read(new MemoryStream(b)));
            Assert.True(e is InvalidDataException or EndOfStreamException, e?.ToString());
            return e is InvalidDataException ? e.Message : BattleSaves.Damaged;
        }
        // A byte changed in the packed world: its digest no longer matches.
        var flipped = (byte[])bytes.Clone();
        flipped[^100] ^= 0x10;
        Assert.Equal(BattleSaves.Damaged, Refusal(flipped));
        Assert.Equal(BattleSaves.Damaged, Refusal(bytes[..^1]));
        Assert.Equal(BattleSaves.NotASave, Refusal([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]));
        var other = (byte[])bytes.Clone();
        other[4]++;
        Assert.Equal(BattleSaves.OtherVersion, Refusal(other));
        Assert.Throws<InvalidDataException>(() => BattleSaves.ReadInfo(new MemoryStream(other)));
    }

    [Fact]
    public void A_world_of_another_version_is_refused_rather_than_read_into_the_wrong_fields()
    {
        var w = Battle(BattleMode.Standard, 9);
        var raw = StateWire.Write(w);
        // A field named otherwise, as the next version might: the table says so before anything is read.
        var name = System.Text.Encoding.UTF8.GetBytes("ShieldRegen");
        var at = raw.AsSpan().IndexOf(name);
        Assert.True(at > 0);
        raw[at] = (byte)'X';
        var e = Assert.Throws<InvalidDataException>(() => StateWire.Read<World>(raw));
        Assert.Contains("autre version", e.Message);
    }

    /// <summary>How deep a type's name nests, as StateWire reads it: one level per collection's items and per array.</summary>
    static int Depth(string name)
    {
        int deepest = 0, open = 0;
        foreach (var c in name)
        {
            if (c == '<') deepest = Math.Max(deepest, ++open);
            else if (c == '>') open--;
        }
        return deepest + (name.Length - name.Replace("[]", "").Length) / 2;
    }

    /// <summary>A world's bytes whose table holds one type, named so, and nothing after it.</summary>
    static byte[] Table(string name)
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, true))
        {
            bw.Write7BitEncodedInt(1);
            bw.Write(name);
        }
        return ms.ToArray();
    }

    [Fact]
    public void A_type_name_nested_past_the_bound_is_refused_before_the_stack_overflows()
    {
        // The names the battle state really writes go three deep at most: well within the bound.
        var raw = StateWire.Write(Battle(BattleMode.Regicide, 4));
        var r = new BinaryReader(new MemoryStream(raw));
        var names = new List<string>();
        for (var n = r.Read7BitEncodedInt(); n > 0; n--)
        {
            names.Add(r.ReadString());
            for (var f = r.Read7BitEncodedInt(); f > 0; f--) names.Add(r.ReadString() + " " + r.ReadString());
        }
        Assert.InRange(names.Max(Depth), 1, 3);
        static string Lists(int depth) => string.Concat(Enumerable.Repeat("List<", depth)) + "System.Int32" + new string('>', depth);
        // Forged within the 64 Kio a name may take: ten thousand lists, or thirty thousand arrays, one inside the other.
        string[] forged = [Lists(10_000), "System.Int32" + string.Concat(Enumerable.Repeat("[]", 30_000)), Lists(StateWire.MaxNameDepth + 1)];
        foreach (var name in forged)
        {
            Assert.True(name.Length < 1 << 16);
            var e = Assert.Throws<InvalidDataException>(() => StateWire.Read<World>(Table(name)));
            Assert.Contains("trop imbriqué", e.Message);
        }
        // At the bound, the name reads (the bytes then end, which is another refusal).
        var atBound = Record.Exception(() => StateWire.Read<World>(Table(Lists(StateWire.MaxNameDepth))));
        Assert.True(atBound is EndOfStreamException or InvalidDataException && !atBound.Message.Contains("trop imbriqué"), atBound?.ToString());
        // A name the assembly would read by a grammar of its own is no type of the table.
        Assert.Throws<InvalidDataException>(() => StateWire.Read<World>(Table("SandboxShip.Sim.World, System.Private.CoreLib")));
        Assert.Throws<InvalidDataException>(() => StateWire.Read<World>(Table("SandboxShip.Sim.Order[[System.Int32]]")));
    }

    [Fact]
    public void Only_a_battle_made_from_a_lobby_and_undecided_is_saved()
    {
        Assert.False(BattleSaves.CanSave(CreateWorld(2000, 2000)));
        var w = Battle(BattleMode.Standard, 9);
        Assert.True(BattleSaves.CanSave(w));
        w.Winner = Player;
        Assert.False(BattleSaves.CanSave(w));
        Assert.Throws<ArgumentException>(() => File(w));
    }

    [Fact]
    public void The_folder_keeps_named_saves_one_automatic_save_a_battle_and_never_another_file()
    {
        var dir = Directory.CreateTempSubdirectory("battle-saves").FullName;
        try
        {
            var w = Battle(BattleMode.Standard, 9);
            Run(w, 5);
            // A campaign's save next to them is never listed nor touched.
            System.IO.File.WriteAllText(Path.Combine(dir, "autosave.json"), "{}");
            var first = BattleSaves.SaveAs(dir, w, new BattleSaveHead(Player, "aaaa", false, View, "test", 1000, "Ma bataille"));
            var second = BattleSaves.SaveAs(dir, w, new BattleSaveHead(Player, "aaaa", false, View, "test", 2000, "Ma bataille"));
            Assert.Equal(("ma-bataille", "ma-bataille-2"), (first, second));
            // A name that looks like an automatic save's never takes its place.
            Assert.Equal("bataille-auto-aaaa", BattleSaves.SaveAs(dir, w, new BattleSaveHead(Player, "aaaa", false, View, "test", 2500, "auto aaaa")));
            Assert.True(BattleSaves.SaveAuto(dir, w, new BattleSaveHead(Player, "aaaa", false, View, "test", 3000)));
            Assert.True(BattleSaves.SaveAuto(dir, w, new BattleSaveHead(Player, "aaaa", false, View, "test", 4000)));
            var slots = BattleSaves.List(dir);
            Assert.Equal(["auto-aaaa", "bataille-auto-aaaa", "ma-bataille-2", "ma-bataille"], slots.Select(s => s.Id));
            Assert.True(slots[0].IsAuto && slots[0].Info!.SavedAt == 4000);
            // Each battle left has its automatic save; past the last few, the oldest go.
            for (var i = 0; i < BattleSaves.AutosKept + 2; i++) Assert.True(BattleSaves.SaveAuto(dir, w, new BattleSaveHead(Player, $"b{i}", false, View, "test", 5000 + i)));
            var autos = BattleSaves.List(dir).Where(s => s.IsAuto).Select(s => s.Id).ToList();
            Assert.Equal(BattleSaves.AutosKept, autos.Count);
            Assert.DoesNotContain("auto-aaaa", autos);
            // A battle that ends loses its automatic save; the named ones stay.
            BattleSaves.DeleteAuto(dir, "b6");
            Assert.DoesNotContain(BattleSaves.List(dir), s => s.Id == "auto-b6");
            Assert.Contains(BattleSaves.List(dir), s => s.Id == "ma-bataille");
            // A broken file is listed, with why.
            System.IO.File.WriteAllBytes(BattleSaves.PathOf(dir, "cassee"), [1, 2, 3]);
            var broken = BattleSaves.List(dir).Single(s => s.Id == "cassee");
            Assert.Equal((null, BattleSaves.NotASave), (broken.Info, broken.Problem));
            Assert.Null(BattleSaves.Load(BattleSaves.PathOf(dir, "cassee"), out var problem));
            Assert.Equal(BattleSaves.NotASave, problem);
            Assert.NotNull(BattleSaves.Load(BattleSaves.PathOf(dir, "ma-bataille"), out _));
            Assert.Equal("{}", System.IO.File.ReadAllText(Path.Combine(dir, "autosave.json")));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void A_saved_battle_counts_once_and_not_at_all_once_taken_back()
    {
        var p = new Profile();
        // Saved at 600, left at 900 (its automatic save), taken up again at 900: it still counts.
        Profiles.MarkBattle(p, "aa", 600);
        Profiles.MarkBattle(p, "aa", 900);
        Assert.True(Profiles.ResumeBattle(p, "aa", 900));
        // Going on, it moves its mark (a minute at a time, and when left); a save from before that is a second try.
        Profiles.MarkBattle(p, "aa", 1500, save: false);
        Assert.False(Profiles.ResumeBattle(p, "aa", 900));
        // Rewound for good: even its furthest save no longer counts.
        Assert.False(Profiles.ResumeBattle(p, "aa", 1500));
        // A battle whose end was counted never counts again.
        Profiles.MarkBattle(p, "bb", 300);
        Profiles.EndBattle(p, "bb");
        Assert.False(Profiles.ResumeBattle(p, "bb", 300));
        // One this profile never saw saved: another profile's, or a file from elsewhere.
        Assert.False(Profiles.ResumeBattle(p, "cc", 10));
        // Going on never makes a mark for a battle never saved; ending one leaves none either.
        Profiles.MarkBattle(p, "dd", 50, save: false);
        Profiles.EndBattle(p, "dd");
        Assert.DoesNotContain(p.Battles, m => m.Id == "dd");
        for (var i = 0; i < Profiles.MarksKept + 10; i++) Profiles.MarkBattle(p, $"{i:x}0", i);
        Assert.Equal(Profiles.MarksKept, p.Battles.Count);
        Assert.DoesNotContain(p.Battles, m => m.Id == "aa");
    }

    [Fact]
    public void The_profile_reads_its_battle_marks_bounded()
    {
        var p = new Profile();
        Profiles.MarkBattle(p, "0123abcd", 600);
        Profiles.ResumeBattle(p, "0123abcd", 100);
        var read = Profiles.Parse(Profiles.Serialize(p));
        Assert.Equal(("0123abcd", 600L, true, false), (read.Battles[0].Id, read.Battles[0].Step, read.Battles[0].Rewound, read.Battles[0].Ended));
        var forged = """{"Version":1,"Battles":[null,{"Id":"../../x","Step":5},{"Id":"ab","Step":-9},{"Id":"ab","Step":7,"Ended":true}]}""";
        var marks = Profiles.Parse(forged).Battles;
        Assert.Equal(("ab", 7L, true), (marks.Single().Id, marks.Single().Step, marks.Single().Ended));
    }

    /// <summary>
    /// What a long battle costs, played again from its start against read from its save (a measure,
    /// not a check, run when asked): BATTLESAVE=1 dotnet test Sim.Tests -c Release --filter Measures_a_long_battle_s_save
    /// --logger "console;verbosity=detailed". MINUTES=60 by default.
    /// </summary>
    [Fact]
    public void Measures_a_long_battle_s_save()
    {
        if (Environment.GetEnvironmentVariable("BATTLESAVE") is null) return;
        var minutes = double.Parse(Environment.GetEnvironmentVariable("MINUTES") ?? "60", System.Globalization.CultureInfo.InvariantCulture);
        Slot Seat(int side, Faction race, Difficulty d) => new(new Side(side), race, 0, d);
        var w = Scenario.CreateSkirmish(new SkirmishOptions
        {
            Seed = 11, Size = MapSize.Immense, Terrain = TerrainDensity.Light, Raid = true,
            Slots = [Seat(0, Faction.Klaed, Difficulty.Normal), Seat(1, Faction.Nairan, Difficulty.Normal), Seat(2, Faction.Nautolan, Difficulty.Normal),
                Seat(4, Faction.Klaed, Difficulty.Hard), Seat(5, Faction.Nairan, Difficulty.Hard), Seat(6, Faction.Nautolan, Difficulty.Hard)],
        });
        Replays.Record(w);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (w.Time < minutes * 60 && w.Winner is null)
        {
            Steps.StepWorld(w);
            w.Events.Clear();
        }
        var played = clock.Elapsed.TotalSeconds;
        Measure(w, $"6 IA, carte immense, {w.Time / 60:0} min", played);
        // The measuring battle (Stress): three fleets of more than a thousand entities in a fight, made as a lobby's would be.
        var stress = Stress.Create(7, TerrainDensity.None);
        stress.Origin = new SkirmishOptions { Slots = [new(Faction.Klaed, Faction.Klaed), new(Faction.Nairan, Faction.Nairan)] };
        Run(stress, 15);
        Measure(stress, "bataille de mesure à 15 s", double.NaN);
    }

    void Measure(World w, string what, double played)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var bytes = File(w);
        var write = clock.Elapsed.TotalMilliseconds;
        var raw = StateWire.Write(w).Length;
        // The best of three reads: the first pays for the code compiled on the way.
        var read = double.MaxValue;
        for (var i = 0; i < 3; i++)
        {
            clock.Restart();
            BattleSaves.Read(new MemoryStream(bytes));
            read = Math.Min(read, clock.Elapsed.TotalMilliseconds);
        }
        output.WriteLine($"{what} : {w.Entities.Count} entités, {w.Projectiles.Count} tirs, {w.Log?.Commands.Count ?? 0} ordres ; fichier {bytes.Length / 1024.0:0} Kio "
            + $"({raw / 1024.0:0} Kio avant compression) ; écriture {write:0} ms, lecture complète (vérifications et essai compris) {read:0} ms"
            + (double.IsNaN(played) ? "" : $" ; rejouée depuis le début : {played:0} s"));
    }

    /// <summary>
    /// Where two states first differ, as a path of fields ("Entities.order[12].Hull: 40 / 38"); null
    /// when they do not. Lists in their order; dictionaries and sets by their keys: a dictionary that
    /// lost entries fills their places with the next ones, and one read back has no such places, so
    /// the two may list their keys apart without the battle being any different. Were the battle to
    /// walk such a dictionary in its order, the values would part, and that shows.
    /// </summary>
    internal static string? Diff(object a, object b)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        string? Walk(object? x, object? y, string path, int depth)
        {
            if (x is null || y is null) return x is null && y is null ? null : $"{path}: {x ?? "null"} / {y ?? "null"}";
            var t = x.GetType();
            if (t.IsPrimitive || t.IsEnum || x is string) return x.Equals(y) ? null : $"{path}: {x} / {y}";
            if (!t.IsValueType && !seen.Add(x)) return null;
            if (depth > 60) return null;
            if (x is System.Collections.IDictionary dx && y is System.Collections.IDictionary dy)
            {
                if (dx.Count != dy.Count) return $"{path}.Count: {dx.Count} / {dy.Count}";
                foreach (var k in dx.Keys)
                {
                    if (!dy.Contains(k)) return $"{path}[{k}]: absent de la copie";
                    if (Walk(dx[k], dy[k], $"{path}[{k}]", depth + 1) is { } d) return d;
                }
                return null;
            }
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(HashSet<>))
            {
                var sx = ((System.Collections.IEnumerable)x).Cast<object>().ToHashSet();
                var sy = ((System.Collections.IEnumerable)y).Cast<object>().ToHashSet();
                return sx.SetEquals(sy) ? null : $"{path}: {string.Join(",", sx.Except(sy))} / {string.Join(",", sy.Except(sx))}";
            }
            // A read-only sequence comes back as a list (StateWire.AsList): compared item by item.
            if (x is System.Collections.IEnumerable ex && y is System.Collections.IEnumerable ey && (t.Assembly != typeof(World).Assembly || t != y.GetType()))
            {
                var lx = ex.Cast<object?>().ToList();
                var ly = ey.Cast<object?>().ToList();
                if (lx.Count != ly.Count) return $"{path}.Count: {lx.Count} / {ly.Count}";
                for (var i = 0; i < lx.Count; i++)
                {
                    if (Walk(lx[i], ly[i], $"{path}[{i}]", depth + 1) is { } d) return d;
                }
                return null;
            }
            if (t != y.GetType()) return $"{path}: {t.Name} / {y.GetType().Name}";
            for (var bt = t; bt is not null && bt != typeof(object); bt = bt.BaseType)
            {
                foreach (var f in bt.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
                {
                    if (Walk(f.GetValue(x), f.GetValue(y), $"{path}.{f.Name}", depth + 1) is { } d) return d;
                }
            }
            return null;
        }
        return Walk(a, b, "world", 0);
    }
}
