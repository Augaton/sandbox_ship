// The port against the web version, end to end: Fixtures/maps.json holds generated galaxies
// and skirmishes as the TypeScript sources build and run them (`npm run fixtures`). V8's and
// .NET's sin, cos and atan2 may differ in the last bit, so positions match to a tolerance.
// Two minutes of play with the AI on every side agreed to 1e-9 until this version changed
// the rules on purpose (fleets and carriers, see FleetTests): only the maps are compared now.
using System.Text.Json;
using SandboxShip.Sim.Data;
using Xunit;

namespace SandboxShip.Sim.Tests;

public class CrossTests
{
    static readonly JsonElement Maps =
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "maps.json"))).RootElement;

    static T Named<T>(JsonElement j) where T : struct, Enum => Enum.Parse<T>(j.GetString()!, true);

    [Theory]
    [InlineData(1, 2)]
    [InlineData(42, 2)]
    [InlineData(21, 3)]
    [InlineData(12345, 2)]
    public void Generated_galaxies_match(int seed, int players)
    {
        var j = Maps.GetProperty("layouts").GetProperty($"{seed}-{players}");
        var layout = GalaxyGen.GenerateLayout(seed, players);
        var planets = j.GetProperty("planets");
        Assert.Equal(planets.GetArrayLength(), layout.Planets.Count);
        for (var i = 0; i < layout.Planets.Count; i++)
        {
            var p = planets[i];
            var q = layout.Planets[i];
            Assert.Equal(p.GetProperty("name").GetString(), q.Name);
            Assert.Equal(p.GetProperty("x").GetDouble(), q.X);
            Assert.Equal(p.GetProperty("y").GetDouble(), q.Y);
            Assert.Equal(p.GetProperty("income").GetDouble(), q.Income);
            Assert.Equal(p.GetProperty("mineSlots").GetInt32(), q.MineSlots);
            PlanetSpecial? special = p.TryGetProperty("special", out var s) && s.ValueKind == JsonValueKind.String ? Named<PlanetSpecial>(s) : null;
            Assert.Equal(special, q.Special);
        }
        Assert.Equal(j.GetProperty("lanes").EnumerateArray().Select(l => new Lane(l[0].GetInt32(), l[1].GetInt32())), layout.Lanes);
        Assert.Equal(j.GetProperty("capitals").EnumerateArray().Select(c => c.GetInt32()), layout.Capitals);
    }

    static SkirmishOptions Options(string name) => name switch
    {
        "duel" => new() { Player = Faction.Klaed, Enemies = [Faction.Nairan], EnemyAI = true, Seed = 5 },
        "classic" => new() { Player = Faction.Nautolan, Enemies = [Faction.Klaed], EnemyAI = true, Seed = 9, Map = MapKind.Classic },
        _ => new() { Player = Faction.Nairan, Enemies = [Faction.Klaed, Faction.Nautolan], EnemyAI = true, Seed = 77, Size = MapSize.Large },
    };

    /// <summary>Same entities, same order, same state; positions within `tolerance` world units.</summary>
    static void Same(JsonElement j, World world, double tolerance, string when)
    {
        Expect.Close(j.GetProperty("time").GetDouble(), world.Time, 6);
        var entities = j.GetProperty("entities");
        var ids = entities.EnumerateArray().Select(e => e.GetProperty("id").GetInt32()).ToList();
        // Labs are given last, and only here.
        var ported = world.Entities.Where(e => e.Type != "lab").ToList();
        Assert.True(ids.SequenceEqual(ported.Select(e => e.Id)), $"{when}: entities differ: TS has {ids.Count}, C# has {ported.Count}");
        var i = 0;
        foreach (var e in ported)
        {
            var t = entities[i++];
            var label = $"{when}: entity {e.Id} ({e.Type})";
            Assert.Equal(t.GetProperty("type").GetString(), e.Type);
            Assert.Equal(Named<Faction>(t.GetProperty("faction")), e.Faction);
            Assert.True(Math.Abs(t.GetProperty("x").GetDouble() - e.X) <= tolerance, $"{label}: x {e.X} vs {t.GetProperty("x").GetDouble()}");
            Assert.True(Math.Abs(t.GetProperty("y").GetDouble() - e.Y) <= tolerance, $"{label}: y {e.Y} vs {t.GetProperty("y").GetDouble()}");
            // Stations are sturdier here on purpose.
            if (e.Type != "station")
                Assert.True(Math.Abs(t.GetProperty("hull").GetDouble() - e.Hull) <= Math.Max(tolerance, 1e-6) * 10, $"{label}: hull {e.Hull} vs {t.GetProperty("hull").GetDouble()}");
            Assert.Equal(t.GetProperty("level").GetInt32(), e.Level);
        }
        var factions = j.GetProperty("factions");
        var k = 0;
        foreach (var f in world.Factions.Values)
        {
            var t = factions[k++];
            Assert.Equal(Named<Faction>(t.GetProperty("id")), f.Id);
            Assert.True(Math.Abs(t.GetProperty("credits").GetDouble() - f.Credits) <= 1e-6 + tolerance, $"{when}: {f.Id} credits {f.Credits} vs {t.GetProperty("credits").GetDouble()}");
            Assert.Equal(t.GetProperty("queue").GetInt32(), f.Queue.Count);
            Assert.Equal(t.GetProperty("buildQueue").GetInt32(), f.BuildQueue.Count);
            Assert.Equal(t.GetProperty("stats").GetProperty("built").GetInt32(), f.Stats.Built);
        }
        var fields = j.GetProperty("fields");
        Assert.Equal(fields.GetArrayLength(), world.Fields.Count);
        for (var n = 0; n < world.Fields.Count; n++)
        {
            Expect.Close(fields[n].GetProperty("x").GetDouble(), world.Fields[n].X, 6);
            Expect.Close(fields[n].GetProperty("y").GetDouble(), world.Fields[n].Y, 6);
            var mine = fields[n].GetProperty("mineId");
            Assert.Equal(mine.ValueKind == JsonValueKind.Number ? mine.GetInt32() : null, world.Fields[n].MineId);
        }
    }

    [Theory]
    [InlineData("duel")]
    [InlineData("classic")]
    [InlineData("ffa")]
    public void Skirmishes_start_the_same(string name) =>
        Same(Maps.GetProperty("skirmishes").GetProperty(name).GetProperty("start"), Scenario.CreateSkirmish(Options(name)), 1e-9, "start");
}
