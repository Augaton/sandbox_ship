// Campaign save games, as JSON files. Battles are not saved: the fleets that were fighting
// stay face to face on the map, and the battle starts again when the game is loaded. Port of
// src/sim/galaxySave.ts; the file layout follows the web save, with its own version number.
using System.Text.Json;
using System.Text.Json.Serialization;
using SandboxShip.Sim.Data;

namespace SandboxShip.Sim;

public sealed record SaveInfo(long SavedAt, Faction Player, int Planets);

public static class GalaxySave
{
    /// <summary>
    /// Bumped whenever the saved shape changes in a way older saves cannot follow: they are then
    /// ignored. A field added with a default (research queue, production loops, map extent, truces,
    /// random events) needs no bump.
    /// </summary>
    const int Version = 1;

    sealed class SavedGalaxy
    {
        public int Version;
        public long SavedAt;
        public double Time;
        public Faction Player;
        /// <summary>Saves from before galaxy sizes have none: the medium galaxy's.</summary>
        public double W = GalaxyMap.W, H = GalaxyMap.H;
        public List<Planet> Planets = [];
        public List<Lane> Lanes = [];
        public List<Fleet> Fleets = [];
        public List<GalaxyFaction> Factions = [];
        public int NextId;
        public long Seed;
        public Faction? Winner;
        /// <summary>Truces, offers and random events (not in the web version): none in older saves.</summary>
        public List<Truce> Truces = [];
        public List<TruceOffer> Offers = [];
        public bool Incidents = true;
        public double NextIncident;
        public long IncidentSeed;
        public List<Incident> Ongoing = [];
    }

    static readonly JsonSerializerOptions Options = new()
    {
        IncludeFields = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase), new HeroIdConverter(), new ResearchLevelsConverter() },
    };

    public static string Serialize(Galaxy g, long? savedAt = null)
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
            Factions = [.. g.Factions.Values], NextId = g.NextId, Seed = g.Seed, Winner = g.Winner,
            Truces = g.Truces, Offers = g.Offers, Incidents = g.Incidents, NextIncident = g.NextIncident, IncidentSeed = g.IncidentSeed, Ongoing = g.Ongoing,
        };
        return JsonSerializer.Serialize(save, Options);
    }

    static SavedGalaxy? Parse(string text)
    {
        try
        {
            return JsonSerializer.Deserialize<SavedGalaxy>(text, Options);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException or FormatException)
        {
            return null;
        }
    }

    /// <summary>Null when the text is not a save of this version.</summary>
    public static Galaxy? Deserialize(string text)
    {
        var s = Parse(text);
        if (s is null || s.Version != Version || s.Planets.Count == 0) return null;
        var g = new Galaxy
        {
            Time = s.Time, Player = s.Player, W = s.W, H = s.H, Planets = s.Planets, Lanes = s.Lanes, NextId = s.NextId, Seed = s.Seed, Winner = s.Winner,
            Truces = s.Truces, Offers = s.Offers, Incidents = s.Incidents, NextIncident = s.NextIncident, IncidentSeed = s.IncidentSeed, Ongoing = s.Ongoing,
        };
        foreach (var f in s.Fleets) g.Fleets[f.Id] = f;
        foreach (var f in s.Factions) g.Factions[f.Id] = f;
        return g;
    }

    /// <summary>Returns false when the file cannot be written (disk full, no rights).</summary>
    public static bool Save(Galaxy g, string path)
    {
        try
        {
            // Written aside first, then moved over: a crash mid-write never eats the old save.
            var temp = path + ".tmp";
            File.WriteAllText(temp, Serialize(g));
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
        return new SaveInfo(s.SavedAt, s.Player, s.Planets.Count(p => p.Owner == s.Player));
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
