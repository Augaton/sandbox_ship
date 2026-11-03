// The game in French or in English. The code writes French; whatever a control shows goes
// through Godot's TranslationServer, which asks this translation for the English: an exact
// entry of lang/en.po, else one of its templates, whose holes ({0}, {1}...) take the values out
// of the French text and put them back in the English one, each translated in turn when it is
// a word of the game. The simulation's texts ("Station niveau 4 requise") are translated the
// same way, as they are shown: the simulation, its tests and the network keep the French.
using System.Text;
using Godot;

namespace SandboxShip.Core;

public sealed partial class Lang : Translation
{
    public const string French = "fr", English = "en";
    const string Catalog = "res://lang/en.po";

    /// <summary>
    /// A French text with holes, split around them (Parts has one more item than there are
    /// holes), and its English; Letters, how many letters are not holes: the more, the surer.
    /// </summary>
    sealed record Template(string[] Parts, string English, int Letters);

    readonly Dictionary<string, string> exact = [];
    /// <summary>Templates by the first three characters of their French text; those that begin with less than that, apart.</summary>
    readonly Dictionary<string, List<Template>> byStart = [];
    readonly List<Template> loose = [];
    /// <summary>What was asked lately, found or not (null): the panels ask the same texts ten times a second.</summary>
    readonly Dictionary<string, string?> cache = [];
    const int CacheSize = 8192;
    readonly HashSet<string> reported = [];
    readonly Lock gate = new();

    static Lang? english;

    /// <summary>The language shown: "fr" or "en".</summary>
    public static string Current { get; private set; } = French;

    /// <summary>
    /// Sets the language: "fr", "en", or anything else for the system's when it is English,
    /// French otherwise. The controls shown change at once.
    /// </summary>
    public static void Apply(string? choice)
    {
        var lang = choice is French or English ? choice : OS.GetLocaleLanguage() == English ? English : French;
        if (lang == English && english is null)
        {
            english = Load();
            TranslationServer.AddTranslation(english);
        }
        Current = lang;
        TranslationServer.SetLocale(lang);
    }

    /// <summary>A French text in the language shown, for what no control translates by itself (text drawn on the battlefield).</summary>
    public static string T(string french) => Current == English && english is not null ? english.Find(french) ?? french : french;

    public override StringName _GetMessage(StringName srcMessage, StringName context)
    {
        var found = Find(srcMessage.ToString());
        return found is null ? new StringName() : new StringName(found);
    }

    string? Find(string text)
    {
        lock (gate)
        {
            if (cache.TryGetValue(text, out var hit)) return hit;
            var found = HasLetters(text) ? Best(text, 0) : Percent(text);
            if (found is null) Report(text);
            // Translated, but with French left in a value: the catalog lacks that value.
            else if (Dev.LangReport is not null && FrenchLeft.IsMatch(found)) Report($"{text} => {found}");
            if (cache.Count >= CacheSize) cache.Clear();
            cache[text] = found;
            return found;
        }
    }

    /// <summary>
    /// The English of a text, by template or by its parts (Compose), whichever leaves no French:
    /// a loose template ("{0} contre {1}") may swallow a list a part at a time would translate.
    /// </summary>
    string? Best(string text, int depth)
    {
        var whole = Translate(text, depth);
        if (whole is not null && !FrenchLeft.IsMatch(whole)) return whole;
        var parts = Compose(text, depth);
        if (parts is not null && (whole is null || !FrenchLeft.IsMatch(parts))) return parts;
        return whole ?? parts;
    }

    /// <summary>The English of a French text: exact, else by the first template it fits; null if none does.</summary>
    string? Translate(string text, int depth)
    {
        if (!HasLetters(text)) return null;
        if (exact.TryGetValue(text, out var e)) return e;
        var key = text.Length >= 3 ? text[..3] : null;
        if (key is not null && byStart.TryGetValue(key, out var started))
        {
            foreach (var t in started)
            {
                if (Fits(t, text) is { } holes) return Fill(t, holes, depth);
            }
        }
        foreach (var t in loose)
        {
            if (Fits(t, text) is { } holes) return Fill(t, holes, depth);
        }
        return null;
    }

    /// <summary>The values in the holes, if the text fits the template: each hole as short as it can be.</summary>
    static string[]? Fits(Template t, string text)
    {
        var parts = t.Parts;
        var first = parts[0];
        var last = parts[^1];
        if (text.Length < first.Length + last.Length || !text.StartsWith(first, StringComparison.Ordinal) || !text.EndsWith(last, StringComparison.Ordinal)) return null;
        var holes = new string[parts.Length - 1];
        var at = first.Length;
        var end = text.Length - last.Length;
        for (var k = 1; k < parts.Length - 1; k++)
        {
            var found = text.IndexOf(parts[k], at, end - at, StringComparison.Ordinal);
            if (found < 0) return null;
            holes[k - 1] = text[at..found];
            at = found + parts[k].Length;
        }
        holes[^1] = text[at..end];
        return holes;
    }

    /// <summary>
    /// The English of a template with its holes filled: {n} the value, translated if it is the
    /// game's words; {n:s} "s" when the value is not empty (a French plural), {n:one|many} either.
    /// </summary>
    string Fill(Template t, string[] holes, int depth)
    {
        var s = t.English;
        var b = new StringBuilder(s.Length + 16);
        for (var i = 0; i < s.Length; i++)
        {
            var close = s[i] == '{' ? s.IndexOf('}', i) : -1;
            if (close > i)
            {
                var inner = s[(i + 1)..close];
                var colon = inner.IndexOf(':');
                if (int.TryParse(colon >= 0 ? inner[..colon] : inner, out var n) && n >= 0 && n < holes.Length)
                {
                    if (colon < 0) b.Append(Value(holes[n], depth));
                    else
                    {
                        var forms = inner[(colon + 1)..].Split('|');
                        var many = holes[n].Length > 0;
                        b.Append(forms.Length > 1 ? forms[many ? 1 : 0] : many ? forms[0] : "");
                    }
                    i = close;
                    continue;
                }
            }
            b.Append(s[i]);
        }
        return b.ToString();
    }

    /// <summary>A value put in a hole: the game's words translated (a name, a list of them, "2 × laser"), the rest as it is.</summary>
    string Value(string v, int depth)
    {
        if (!HasLetters(v)) return Percent(v) ?? v;
        if (depth >= MaxDepth) return v;
        return Best(v, depth + 1) ?? v;
    }

    /// <summary>How deep a text is taken apart: "Survie : 2 joueurs contre les vagues, carte moyenne" goes four levels down.</summary>
    const int MaxDepth = 6;

    /// <summary>
    /// Texts made of the game's words and of numbers, too plain for a template of their own: a
    /// list ("Vorrak, amiral"), a count before ("3× Éclaireur") or after ("Armement 0/5"). Null if
    /// none of their words has an English.
    /// </summary>
    string? Compose(string text, int depth)
    {
        if (depth >= MaxDepth) return null;
        // A name and what it has, French style: "Escarmouche : 2 contre 2".
        var colon = text.IndexOf(" : ", StringComparison.Ordinal);
        if (colon > 0)
        {
            string left = text[..colon], right = text[(colon + 3)..];
            string l = Value(left, depth), r = Value(right, depth);
            return l == left && r == right ? null : $"{l}: {r}";
        }
        if (text.Contains(", ", StringComparison.Ordinal))
        {
            var parts = text.Split(", ");
            var english = parts.Select(p => Value(p, depth)).ToArray();
            return english.SequenceEqual(parts) ? null : string.Join(", ", english);
        }
        int start = 0, end = text.Length;
        // A note in brackets at the end: "Soutien (3 s)".
        if (text.EndsWith(')') && text.LastIndexOf(" (", StringComparison.Ordinal) is var open and > 0) end = open;
        while (start < end && IsCount(text[start])) start++;
        while (end > start && (IsCount(text[end - 1]) || text[end - 1] == ',')) end--;
        if ((start == 0 && end == text.Length) || start >= end) return null;
        return Translate(text[start..end], depth + 1) is { } words ? text[..start] + words + text[end..] : null;
    }

    /// <summary>"42 %" as English writes it, "42%": French puts a space before the sign. Null for anything else.</summary>
    static string? Percent(string s) => s.Length > 2 && s[^1] == '%' && s[^2] is ' ' or ' ' ? s[..^2] + "%" : null;

    static bool IsCount(char c) => char.IsDigit(c) || c is ' ' or '×' or '/' or '+' or '-' or '.' or '(' or ')' or '%' or ' ';

    /// <summary>French small words, which no English text has: what a partial translation lets through.</summary>
    static readonly System.Text.RegularExpressions.Regex FrenchLeft = new(@"\b(de|la|le|les|des|du|une|sur|avec|pour|et|aux|au|est|dans|sans|tu|ta|ton|tes)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    static bool HasLetters(string s)
    {
        foreach (var c in s)
        {
            if (char.IsLetter(c)) return true;
        }
        return false;
    }

    /// <summary>With --lang-report=FILE, each text shown with no English is written there once: what the catalog lacks.</summary>
    void Report(string text)
    {
        // Only what has a word of three letters at least: "400 cr, 15 s" reads the same in English.
        if (Dev.LangReport is not string path || !System.Text.RegularExpressions.Regex.IsMatch(text, @"\p{L}{3}") || !reported.Add(text)) return;
        System.IO.File.AppendAllText(path, text.Replace("\n", "\\n") + "\n");
    }

    static Lang Load()
    {
        var lang = new Lang { Locale = English };
        var text = Godot.FileAccess.FileExists(Catalog) ? Godot.FileAccess.GetFileAsString(Catalog) : "";
        foreach (var (fr, en) in Entries(text))
        {
            if (en.Length == 0) continue;
            if (!fr.Contains('{'))
            {
                lang.exact[fr] = en;
                continue;
            }
            var parts = System.Text.RegularExpressions.Regex.Split(fr, @"\{\d+\}");
            var letters = parts.Sum(p => p.Count(char.IsLetter));
            // Holes and punctuation only would fit about anything.
            if (letters < 2) continue;
            var t = new Template(parts, en, letters);
            if (parts[0].Length >= 3)
            {
                var key = parts[0][..3];
                if (!lang.byStart.TryGetValue(key, out var list)) lang.byStart[key] = list = [];
                list.Add(t);
            }
            else lang.loose.Add(t);
        }
        // The surest first: the template with the most words of its own.
        foreach (var list in lang.byStart.Values) list.Sort((a, b) => b.Letters.CompareTo(a.Letters));
        lang.loose.Sort((a, b) => b.Letters.CompareTo(a.Letters));
        return lang;
    }

    /// <summary>The (msgid, msgstr) pairs of a gettext file: the header and comments skipped, split lines joined.</summary>
    static IEnumerable<(string Id, string Str)> Entries(string po)
    {
        string? id = null, str = null;
        var into = 0;
        foreach (var raw in po.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("msgid ", StringComparison.Ordinal))
            {
                if (id is { Length: > 0 }) yield return (id, str ?? "");
                (id, str, into) = (Unquote(line[6..]), null, 1);
            }
            else if (line.StartsWith("msgstr ", StringComparison.Ordinal)) (str, into) = (Unquote(line[7..]), 2);
            else if (line.StartsWith('"'))
            {
                if (into == 1) id += Unquote(line);
                else if (into == 2) str += Unquote(line);
            }
        }
        if (id is { Length: > 0 }) yield return (id, str ?? "");
    }

    static string Unquote(string q)
    {
        var b = new StringBuilder(q.Length);
        for (var i = 1; i < q.Length - 1; i++)
        {
            if (q[i] != '\\')
            {
                b.Append(q[i]);
                continue;
            }
            i++;
            b.Append(q[i] switch { 'n' => '\n', 't' => '\t', _ => q[i] });
        }
        return b.ToString();
    }
}
