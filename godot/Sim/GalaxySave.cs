// Campaign save games, as JSON files. Battles are not saved: the fleets that were fighting
// stay face to face on the map, and the battle starts again when the game is loaded. Port of
// src/sim/galaxySave.ts; the file layout follows the web save, with its own version number.
using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SandboxShip.Sim.Data;

namespace SandboxShip.Sim;

/// <summary>
/// What the menu shows of a save: when, whose (its side and race), how many planets it holds;
/// for a campaign played over the network, the names of the other players, "Bob, Eve"; the
/// name the player gave it, if any.
/// </summary>
public sealed record SaveInfo(long SavedAt, Side Player, Faction Race, int Planets, string Others = "", string? Name = null);

public static class GalaxySave
{
    /// <summary>
    /// Bumped whenever the saved shape changes in a way older saves cannot follow: they are then
    /// ignored. A field added with a default (research queue, production loops, map extent, truces,
    /// random events) needs no bump. Sides (see Side) needed none either: they are written as
    /// numbers, and the race names older saves wrote for them read as those races' own sides.
    /// </summary>
    const int Version = 1;

    sealed class SavedGalaxy
    {
        public int Version;
        public long SavedAt;
        public double Time;
        public Side Player;
        /// <summary>Saves from before galaxy sizes have none: the medium galaxy's.</summary>
        public double W = GalaxyMap.W, H = GalaxyMap.H;
        public List<Planet> Planets = [];
        public List<Lane> Lanes = [];
        public List<Fleet> Fleets = [];
        public List<GalaxyFaction> Factions = [];
        public int NextId;
        public long Seed;
        public Side? Winner;
        /// <summary>
        /// Whether factions carry their race, team and color. Older saves had one side per race,
        /// at war with all, in its race's color: their factions get those once read.
        /// </summary>
        public bool Sides;
        /// <summary>Diplomacy and random events (not in the web version): none in older saves, whose offers are all truces.</summary>
        public List<Truce> Truces = [];
        public List<DealOffer> Offers = [];
        public List<Pact> Pacts = [];
        public List<Tribute> Tributes = [];
        public bool Incidents = true;
        public double NextIncident;
        public long IncidentSeed;
        public List<Incident> Ongoing = [];
        /// <summary>A campaign played over the network: the sides people played, by the name of each (none in older saves).</summary>
        public Dictionary<Side, string> Humans = [];
        /// <summary>The name the player gave the save (SaveSlots); none for the automatic one, nor in older saves.</summary>
        public string? Name;
    }

    static readonly JsonSerializerOptions Options = new()
    {
        IncludeFields = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Enums by name only: a number would read as a race or a class that does not exist.
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false), new SideConverter(), new HeroIdConverter(), new ResearchLevelsConverter() },
    };

    public static string Serialize(Galaxy g, long? savedAt = null, string? name = null)
    {
        var save = new SavedGalaxy
        {
            Version = Version, SavedAt = savedAt ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Time = g.Time, Player = g.Player,
            W = g.W, H = g.H, Planets = g.Planets, Lanes = g.Lanes,
            Fleets = g.Fleets.Values.Select(f =>
            {
                var copy = f.Copy();
                copy.BattleId = null;
                return copy;
            }).ToList(),
            Factions = [.. g.Factions.Values], NextId = g.NextId, Seed = g.Seed, Winner = g.Winner, Sides = true,
            Truces = g.Truces, Offers = g.Offers, Pacts = g.Pacts, Tributes = g.Tributes,
            Incidents = g.Incidents, NextIncident = g.NextIncident, IncidentSeed = g.IncidentSeed, Ongoing = g.Ongoing,
            Humans = g.Humans, Name = name,
        };
        return JsonSerializer.Serialize(save, Options);
    }

    static SavedGalaxy? Parse(string text)
    {
        try
        {
            return JsonSerializer.Deserialize<SavedGalaxy>(text, Options) is { } s && Complete(s) ? s : null;
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException or FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether a save as read holds no null where the code expects none, and no enum value that
    /// does not exist, at any depth: a damaged or forged file is refused as it is read, rather than
    /// crashing the list of saves or the game later. The fields are walked as the serializer filled
    /// them, each checked against what its declaration says (the simulation's nullable annotations).
    /// </summary>
    static bool Complete(SavedGalaxy root)
    {
        var context = new NullabilityInfoContext();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<object>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var o = pending.Pop();
            var type = o.GetType();
            if (type.Namespace?.StartsWith("SandboxShip", StringComparison.Ordinal) != true || (!type.IsValueType && !seen.Add(o))) continue;
            foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!Fits(f.GetValue(o), context.Create(f), pending)) return false;
            }
        }
        return true;
    }

    /// <summary>A value against its declaration: null only where it may be, an enum one of its values, a collection's items as their type says; ours are walked next.</summary>
    static bool Fits(object? v, NullabilityInfo? declared, Stack<object> pending)
    {
        if (v is null) return declared is null || declared.ReadState != NullabilityState.NotNull;
        if (v is string) return true;
        if (v is Enum e) return e.GetType().IsDefined(typeof(FlagsAttribute), false) || Enum.IsDefined(e.GetType(), e);
        if (v is IDictionary d)
        {
            var value = declared?.GenericTypeArguments is [_, var second] ? second : null;
            foreach (DictionaryEntry entry in d)
            {
                if (!Fits(entry.Key, null, pending) || !Fits(entry.Value, value, pending)) return false;
            }
            return true;
        }
        if (v is IEnumerable items)
        {
            var item = declared?.ElementType ?? (declared?.GenericTypeArguments is [var only] ? only : null);
            foreach (var i in items)
            {
                if (!Fits(i, item, pending)) return false;
            }
            return true;
        }
        pending.Push(v);
        return true;
    }

    /// <summary>Null when the text is not a save of this version.</summary>
    public static Galaxy? Deserialize(string text)
    {
        var s = Parse(text);
        if (s is null || s.Version != Version || s.Planets.Count == 0) return null;
        var g = new Galaxy
        {
            Time = s.Time, Player = s.Player, W = s.W, H = s.H, Planets = s.Planets, Lanes = s.Lanes, NextId = s.NextId, Seed = s.Seed, Winner = s.Winner,
            Truces = s.Truces, Offers = s.Offers, Pacts = s.Pacts, Tributes = s.Tributes,
            Incidents = s.Incidents, NextIncident = s.NextIncident, IncidentSeed = s.IncidentSeed, Ongoing = s.Ongoing,
            Humans = s.Humans,
        };
        foreach (var f in s.Fleets) g.Fleets[f.Id] = f;
        foreach (var f in s.Factions)
        {
            if (!s.Sides)
            {
                f.Race = (Faction)f.Id.Id;
                f.Color = Worlds.DefaultColor(f.Id, f.Race);
            }
            g.Factions[f.Id] = f;
        }
        return g;
    }

    /// <summary>Returns false when the file cannot be written (disk full, no rights). `name`: the one the player gave it, if any.</summary>
    public static bool Save(Galaxy g, string path, string? name = null, long? savedAt = null)
    {
        try
        {
            // Written aside first, then moved over: a crash mid-write never eats the old save.
            var temp = path + ".tmp";
            File.WriteAllText(temp, Serialize(g, savedAt, name));
            File.Move(temp, path, true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    static string? Read(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static Galaxy? Load(string path) => Read(path) is string text ? Deserialize(text) : null;

    public static SaveInfo? Info(string path)
    {
        var s = Read(path) is string text ? Parse(text) : null;
        if (s is null || s.Version != Version) return null;
        var race = s.Sides ? s.Factions.Find(f => f.Id == s.Player)?.Race ?? (Faction)s.Player.Id : (Faction)s.Player.Id;
        var others = string.Join(", ", s.Humans.Where(h => h.Key != s.Player).Select(h => h.Value));
        return new SaveInfo(s.SavedAt, s.Player, race, s.Planets.Count(p => p.Owner == s.Player), others, s.Name);
    }

    public static void Clear(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Nothing to clear, or nothing we may clear: either way the save stays unreadable to us.
        }
    }

    /// <summary>
    /// Sides are written as their number, as values and as dictionary keys. Older saves wrote a
    /// race's name, "klaed": that race's own side.
    /// </summary>
    sealed class SideConverter : JsonConverter<Side>
    {
        static Side Parse(string? s)
        {
            if (int.TryParse(s, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id)) return new Side(id);
            if (Enum.TryParse<Faction>(s, true, out var race) && Enum.IsDefined(race)) return race;
            throw new JsonException($"Camp inconnu : {s}");
        }

        public override Side Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
        {
            JsonTokenType.Number when reader.TryGetInt32(out var id) && id >= 0 => new Side(id),
            JsonTokenType.String => Parse(reader.GetString()),
            _ => throw new JsonException("Camp illisible"),
        };

        public override void Write(Utf8JsonWriter writer, Side value, JsonSerializerOptions options) => writer.WriteNumberValue(value.Id);
        public override Side ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => Parse(reader.GetString());
        public override void WriteAsPropertyName(Utf8JsonWriter writer, Side value, JsonSerializerOptions options) => writer.WritePropertyName(value.ToString());
    }

    /// <summary>Heroes are written "klaed-admiral", as values and as dictionary keys.</summary>
    sealed class HeroIdConverter : JsonConverter<HeroId>
    {
        static HeroId Parse(string? s)
        {
            var parts = (s ?? "").Split('-');
            if (parts.Length != 2) throw new JsonException($"Héros inconnu : {s}");
            return new HeroId(Enum.Parse<Faction>(parts[0], true), Enum.Parse<HeroRole>(parts[1], true));
        }

        public override HeroId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => Parse(reader.GetString());
        public override void Write(Utf8JsonWriter writer, HeroId value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
        public override HeroId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => Parse(reader.GetString());
        public override void WriteAsPropertyName(Utf8JsonWriter writer, HeroId value, JsonSerializerOptions options) => writer.WritePropertyName(value.ToString());
    }

    /// <summary>Research levels as { "weapons": 1, ... }.</summary>
    sealed class ResearchLevelsConverter : JsonConverter<ResearchLevels>
    {
        static string Name(TechId t) => JsonNamingPolicy.CamelCase.ConvertName(t.ToString());

        public override ResearchLevels Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var levels = new ResearchLevels();
            using var doc = JsonDocument.ParseValue(ref reader);
            foreach (var t in Techs.All)
            {
                if (doc.RootElement.TryGetProperty(Name(t), out var v)) levels[t] = Math.Clamp(v.GetInt32(), 0, Techs.Defs[t].MaxLevel);
            }
            return levels;
        }

        public override void Write(Utf8JsonWriter writer, ResearchLevels value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach (var t in Techs.All) writer.WriteNumber(Name(t), value[t]);
            writer.WriteEndObject();
        }
    }
}
