// Pixel-art logos, drawn from character grids so that they stay easy to edit:
// '.' transparent, 'k' outline, 'a' faction color, 'b' lighter tone, 'w' white.
// Port of src/ui/logos.ts.
using Godot;
using SandboxShip.Core;
using SandboxShip.Sim.Data;

namespace SandboxShip.Ui;

public static class Logos
{
    public static readonly IReadOnlyDictionary<Faction, string[]> Emblems = new Dictionary<Faction, string[]>
    {
        // Kla'ed, the assault faction: a double arrowhead.
        [Faction.Klaed] =
        [
            "................",
            ".......kk.......",
            "......kbbk......",
            ".....kbaabk.....",
            "....kbaaaabk....",
            "...kbaakkaabk...",
            "..kbaak..kaabk..",
            ".kbaak....kaabk.",
            ".kaak.kbbk.kaak.",
            ".kak.kbaabk.kak.",
            ".kk.kbaaaabk.kk.",
            "...kbaakkaabk...",
            "..kbaak..kaabk..",
            "..kaak....kaak..",
            "..kkk......kkk..",
            "................",
        ],
        // The Errants, the neutral guardians: an eye in a crown.
        [Faction.Errant] =
        [
            "................",
            "..k....kk....k..",
            ".kak..kaak..kak.",
            ".kaak.kaak.kaak.",
            ".kaaakkaakkaaak.",
            ".kaaaaaaaaaaaak.",
            "..kkkkkkkkkkkk..",
            "...kbbbbbbbbk...",
            "..kbb.kkkk.bbk..",
            ".kbb.kbwwbk.bbk.",
            ".kbb.kbwwbk.bbk.",
            "..kbb.kkkk.bbk..",
            "...kbbbbbbbbk...",
            "....kkkkkkkk....",
            "................",
            "................",
        ],
        // Nairan, the technology faction: a ring around a bright core.
        [Faction.Nairan] =
        [
            "................",
            ".....kkkkkk.....",
            "...kkaaaaaakk...",
            "..kaabbbbbbaak..",
            ".kabk......kbak.",
            ".kab..kkkk..bak.",
            "kab..kbwwbk..bak",
            "kab.kbwwwwbk.bak",
            "kab.kbwwwwbk.bak",
            "kab..kbwwbk..bak",
            ".kab..kkkk..bak.",
            ".kabk......kbak.",
            "..kaabbbbbbaak..",
            "...kkaaaaaakk...",
            ".....kkkkkk.....",
            "................",
        ],
        // Nautolan, the biomechanical faction: three claws on one body.
        [Faction.Nautolan] =
        [
            "................",
            ".kk....kk....kk.",
            ".kbk..kbbk..kbk.",
            ".kbak.kbbk.kabk.",
            "..kbakbaabkabk..",
            "..kbaabaabaabk..",
            "...kbaaaaaabk...",
            "...kbaaaaaabk...",
            "....kbaaaabk....",
            "....kbawwabk....",
            "....kbaaaabk....",
            ".....kbaabk.....",
            ".....kbaabk.....",
            "......kaak......",
            ".......kk.......",
            "................",
        ],
    };

    /// <summary>5x7 pixel font, only the letters of the title and of the signature.</summary>
    public static readonly IReadOnlyDictionary<char, string[]> Glyphs = new Dictionary<char, string[]>
    {
        ['S'] = [".###.", "#...#", "#....", ".###.", "....#", "#...#", ".###."],
        ['A'] = [".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"],
        ['N'] = ["#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#", "#...#"],
        ['D'] = ["####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####."],
        ['B'] = ["####.", "#...#", "#...#", "####.", "#...#", "#...#", "####."],
        ['O'] = [".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
        ['X'] = ["#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#"],
        ['H'] = ["#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"],
        ['I'] = ["#####", "..#..", "..#..", "..#..", "..#..", "..#..", "#####"],
        ['P'] = ["####.", "#...#", "#...#", "####.", "#....", "#....", "#...."],
        ['U'] = ["#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
        ['G'] = [".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".###."],
        ['T'] = ["#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.."],
    };

    static readonly Color Outline = Palette.Bg;

    static Color Lighten(uint c, float t) => Palette.Hex(c).Lerp(Colors.White, t);

    static Image Paint(string[] grid, Func<char, int, int, Color?> color, int scale)
    {
        var img = Image.CreateEmpty(grid[0].Length * scale, grid.Length * scale, false, Image.Format.Rgba8);
        for (var y = 0; y < grid.Length; y++)
        {
            for (var x = 0; x < grid[y].Length; x++)
            {
                if (color(grid[y][x], x, y) is Color fill) img.FillRect(new Rect2I(x * scale, y * scale, scale, scale), fill);
            }
        }
        return img;
    }

    /// <summary>A race's emblem, in `color` (0xRRGGBB): a side's, or by default the race's own.</summary>
    public static Image EmblemImage(Faction faction, int scale = 4, uint? color = null)
    {
        var b = color ?? Factions.Defs[faction].Color;
        var palette = new Dictionary<char, Color> { ['k'] = Outline, ['a'] = Palette.Hex(b), ['b'] = Lighten(b, 0.45f), ['w'] = Colors.White };
        return Paint(Emblems[faction], (ch, _, _) => palette.TryGetValue(ch, out var c) ? c : null, scale);
    }

    /// <summary>"SANDBOX SHIP": light letters fading to amber, with a dark outline.</summary>
    public static Image LogoImage(int scale = 4) => WordsImage(["SANDBOX", "SHIP"], scale);

    /// <summary>Words in the title's letters: light fading to amber, with a dark outline.</summary>
    static Image WordsImage(string[] words, int scale)
    {
        // One empty row and column all around leaves room for the outline.
        var rows = new string[9];
        for (var y = 0; y < 9; y++)
        {
            var line = string.Join("...", words.Select(w => string.Join(".", w.Select(l => y == 0 || y == 8 ? "....." : Glyphs[l][y - 1]))));
            rows[y] = $".{line}.";
        }
        // Outline: every empty pixel next to a letter pixel.
        bool Lit(int x, int y) => y >= 0 && y < rows.Length && x >= 0 && x < rows[y].Length && rows[y][x] == '#';
        var grid = rows.Select((row, y) => new string(row.Select((ch, x) =>
        {
            if (ch == '#') return '#';
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (Lit(x + dx, y + dy)) return 'k';
                }
            }
            return '.';
        }).ToArray())).ToArray();
        return Paint(grid, (ch, _, y) => ch == 'k' ? Outline : ch == '#' ? (y <= 4 ? Palette.Hex(0xe9edf3) : Palette.Accent) : null, scale);
    }

    static readonly Dictionary<string, Texture2D> textures = [];

    static Texture2D Cached(string key, Func<Image> make)
    {
        if (!textures.TryGetValue(key, out var t)) textures[key] = t = ImageTexture.CreateFromImage(make());
        return t;
    }

    /// <summary>A race's emblem, 16 pixels a side times `scale`, in `color` (a side's) or the race's own.</summary>
    public static Texture2D Emblem(Faction faction, int scale = 4, uint? color = null) =>
        Cached($"{faction}:{scale}:{color}", () => EmblemImage(faction, scale, color));

    public static Texture2D Logo(int scale = 4) => Cached($"logo:{scale}", () => LogoImage(scale));

    /// <summary>The author's name, in the title's letters, for the opening.</summary>
    public static Texture2D Signature(int scale = 4) => Cached($"signature:{scale}", () => WordsImage(["AUGATON"], scale));
}
