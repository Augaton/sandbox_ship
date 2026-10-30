// The port against the web version: Fixtures/*.json are written by `npm run fixtures`
// from the TypeScript sources, and every value here must match them.
using System.Text.Json;
using SandboxShip.Sim.Data;
using Xunit;

namespace SandboxShip.Sim.Tests;

public class FixtureTests
{
    /// <summary>
    /// What this version adds or changes on purpose, which the web version lacks: three ship
    /// classes, two abilities, a lab, fighters from the shipyard, a fourth station level, sturdier
    /// stations and dearer upgrades, dearer warships, four techs and more research levels.
    /// </summary>
    static readonly HashSet<ShipClass> NewShips = [ShipClass.Bulwark, ShipClass.Carrier, ShipClass.Leviathan, ShipClass.Colossus];
    static readonly HashSet<AbilityId> NewAbilities = [AbilityId.Taunt, AbilityId.Launch, AbilityId.StarLance];
    static IEnumerable<ShipClass> WebShips => Ships.All.Where(c => !NewShips.Contains(c));
    static IEnumerable<AbilityId> WebAbilities => Enum.GetValues<AbilityId>().Where(a => !NewAbilities.Contains(a));
    /// <summary>Warships of the web version made dearer and slower to build here: small craft stay worth it longer.</summary>
    static readonly HashSet<ShipClass> Dearer = [ShipClass.Battlecruiser, ShipClass.Dreadnought, ShipClass.Titan];
    /// <summary>Techs of this version only; the web's go further here, their first three levels unchanged.</summary>
    static readonly HashSet<TechId> NewTechs = [TechId.Logistics, TechId.Repair, TechId.Fortification, TechId.Energy];
    static IEnumerable<BuildableType> WebBuildables => Buildables.All.Where(b => b is not (BuildableType.Lab or BuildableType.StarLance));

    static JsonElement Load(string name) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", $"{name}.json"))).RootElement;

    /// <summary>"heavyTurret" in TypeScript is HeavyTurret here.</summary>
    static void SameName<T>(JsonElement j, T value) where T : struct, Enum =>
        Assert.Equal(j.GetString(), value.ToString(), StringComparer.OrdinalIgnoreCase);

    static double? Optional(JsonElement j, string name) =>
        j.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    static void SameWeapons(JsonElement j, IReadOnlyList<WeaponDef> weapons)
    {
        Assert.Equal(j.GetArrayLength(), weapons.Count);
        for (var i = 0; i < weapons.Count; i++)
        {
            var e = j[i];
            var w = weapons[i];
            SameName(e.GetProperty("kind"), w.Kind);
            Assert.Equal(e.GetProperty("range").GetDouble(), w.Range);
            Assert.Equal(e.GetProperty("damage").GetDouble(), w.Damage);
            Assert.Equal(e.GetProperty("cooldown").GetDouble(), w.Cooldown);
            Assert.Equal(e.GetProperty("projectileSpeed").GetDouble(), w.ProjectileSpeed);
            Assert.Equal(e.TryGetProperty("homing", out var h) && h.GetBoolean(), w.Homing);
            var mult = e.TryGetProperty("mult", out var m) ? m : default;
            foreach (var size in Enum.GetValues<SizeClass>())
            {
                var expected = mult.ValueKind == JsonValueKind.Object ? Optional(mult, size.ToString().ToLowerInvariant()) ?? 1 : 1;
                Assert.Equal(expected, w.Multipliers.Of(size));
            }
        }
    }

    static void SameHangar(JsonElement j, HangarDef? hangar)
    {
        if (j.ValueKind != JsonValueKind.Object)
        {
            Assert.Null(hangar);
            return;
        }
        Assert.NotNull(hangar);
        Assert.Equal(j.GetProperty("cap").GetInt32(), hangar.Cap);
        Assert.Equal(j.GetProperty("interval").GetDouble(), hangar.Interval);
        var squadrons = j.GetProperty("squadrons");
        Assert.Equal(squadrons.GetArrayLength(), hangar.Squadrons.Count);
        for (var i = 0; i < hangar.Squadrons.Count; i++)
        {
            SameName(squadrons[i].GetProperty("cls"), hangar.Squadrons[i].Cls);
            Assert.Equal(squadrons[i].GetProperty("size").GetInt32(), hangar.Squadrons[i].Size);
        }
    }

    static void SameSlots(JsonElement j, IReadOnlyList<HardpointSlot>? slots)
    {
        if (j.ValueKind != JsonValueKind.Array)
        {
            Assert.Null(slots);
            return;
        }
        Assert.NotNull(slots);
        Assert.Equal(j.GetArrayLength(), slots.Count);
        for (var i = 0; i < slots.Count; i++)
        {
            var e = j[i];
            var s = slots[i];
            SameName(e.GetProperty("role"), s.Role);
            Assert.Equal(e.GetProperty("x").GetDouble(), s.X);
            Assert.Equal(e.GetProperty("y").GetDouble(), s.Y);
            Assert.Equal(Optional(e, "hull"), s.Hull);
            Assert.Equal(Optional(e, "radius"), s.Radius);
            if (e.TryGetProperty("weapons", out var w)) SameWeapons(w, s.Weapons!);
            else Assert.Null(s.Weapons);
            SameHangar(e.TryGetProperty("hangar", out var h) ? h : default, s.Hangar);
        }
    }

    [Fact]
    public void Random_draws_match_bit_for_bit()
    {
        var rng = Load("rng");
        foreach (var seq in rng.GetProperty("mulberry").EnumerateObject())
        {
            var state = int.Parse(seq.Name);
            foreach (var expected in seq.Value.EnumerateArray()) Assert.Equal(expected.GetDouble(), Rng.Mulberry32(ref state));
        }
        foreach (var seq in rng.GetProperty("parkMiller").EnumerateObject())
        {
            var state = long.Parse(seq.Name);
            foreach (var expected in seq.Value.EnumerateArray()) Assert.Equal(expected.GetDouble(), Rng.ParkMiller(ref state));
        }
    }

    [Fact]
    public void Factions_match()
    {
        var factions = Load("data").GetProperty("factions");
        foreach (var f in Factions.All)
        {
            var j = factions.GetProperty(f.ToString().ToLowerInvariant());
            var d = Factions.Defs[f];
            Assert.Equal(j.GetProperty("name").GetString(), d.Name);
            Assert.Equal(j.GetProperty("color").GetUInt32(), d.Color);
            var t = j.GetProperty("trait");
            Assert.Equal(t.GetProperty("name").GetString(), d.Trait.Name);
            Assert.Equal(t.GetProperty("description").GetString(), d.Trait.Description);
            Assert.Equal(t.GetProperty("damage").GetDouble(), d.Trait.Damage);
            Assert.Equal(t.GetProperty("shield").GetDouble(), d.Trait.Shield);
            Assert.Equal(t.GetProperty("hull").GetDouble(), d.Trait.Hull);
            Assert.Equal(t.GetProperty("speed").GetDouble(), d.Trait.Speed);
            Assert.Equal(t.GetProperty("cost").GetDouble(), d.Trait.Cost);
            Assert.Equal(t.GetProperty("hullRegen").GetDouble(), d.Trait.HullRegen);
        }
    }

    [Fact]
    public void Ships_match_in_order()
    {
        var data = Load("data");
        var ships = data.GetProperty("ships");
        Assert.Equal(ships.EnumerateObject().Select(p => p.Name.ToLowerInvariant()), WebShips.Select(c => c.ToString().ToLowerInvariant()));
        foreach (var cls in WebShips)
        {
            var j = ships.GetProperty(char.ToLowerInvariant(cls.ToString()[0]) + cls.ToString()[1..]);
            var d = Ships.Defs[cls];
            Assert.Equal(j.GetProperty("name").GetString(), d.Name);
            SameName(j.GetProperty("size"), d.Size);
            // Fighters came only from hangars there; the shipyard builds them here, at a price.
            var built = cls == ShipClass.Fighter;
            foreach (var (name, value) in new[] {
                ("hull", d.Hull), ("shield", d.Shield), ("shieldRegen", d.ShieldRegen), ("speed", d.Speed), ("accel", d.Accel),
                ("turnRate", d.TurnRate), ("radius", d.Radius), ("cost", built ? 0 : d.Cost), ("buildTime", built ? 0 : d.BuildTime) })
            {
                if (Dearer.Contains(cls) && name is "cost" or "buildTime") continue;
                Assert.Equal(j.GetProperty(name).GetDouble(), value);
            }
            Assert.Equal(j.GetProperty("minStationLevel").GetInt32(), d.MinStationLevel);
            Assert.Equal(j.GetProperty("source").GetString() == "hangar", d.FromHangar || built);
            SameWeapons(j.GetProperty("weapons"), d.Weapons);
            SameSlots(j.TryGetProperty("hardpoints", out var hp) ? hp : default, d.Hardpoints);
            if (j.TryGetProperty("ability", out var a)) SameName(a, d.Ability!.Value);
            else Assert.Null(d.Ability);
            Assert.Equal(j.TryGetProperty("squad", out var sq) ? sq.GetInt32() : (int?)null, built ? null : d.Squad);
            Assert.Equal(j.TryGetProperty("unique", out var u) && u.GetBoolean(), d.Unique);
            if (j.TryGetProperty("repair", out var r))
            {
                Assert.Equal(r.GetProperty("range").GetDouble(), d.Repair!.Range);
                Assert.Equal(r.GetProperty("rate").GetDouble(), d.Repair.Rate);
            }
            else Assert.Null(d.Repair);
        }
        foreach (var e in data.GetProperty("derived").EnumerateArray())
        {
            var cls = Enum.Parse<ShipClass>(e.GetProperty("cls").GetString()!, ignoreCase: true);
            // A fighter has a price and a squadron here, so its derived values differ too; so do the dearer warships'.
            if (cls == ShipClass.Fighter || Dearer.Contains(cls)) continue;
            Assert.Equal(e.GetProperty("unitValue").GetDouble(), Ships.UnitValue(cls));
            Assert.Equal(e.GetProperty("orderName").GetString(), Ships.OrderName(cls));
            foreach (var f in Factions.All) Assert.Equal(e.GetProperty("cost").GetProperty(f.ToString().ToLowerInvariant()).GetDouble(), Ships.Cost(cls, f));
        }
    }

    [Fact]
    public void Station_parts_and_buildings_match()
    {
        var data = Load("data");
        foreach (var role in Enum.GetValues<HardpointRole>())
        {
            var j = data.GetProperty("hardpoints").GetProperty(char.ToLowerInvariant(role.ToString()[0]) + role.ToString()[1..]);
            var d = Hardpoints.Defs[role];
            Assert.Equal(j.GetProperty("name").GetString(), d.Name);
            Assert.Equal(j.GetProperty("hull").GetDouble(), d.Hull);
            Assert.Equal(j.GetProperty("radius").GetDouble(), d.Radius);
            SameWeapons(j.GetProperty("weapons"), d.Weapons);
            SameHangar(j.TryGetProperty("hangar", out var h) ? h : default, d.Hangar);
        }
        var station = data.GetProperty("station");
        Assert.Equal(Station.Name, station.GetProperty("name").GetString());
        Assert.Equal(Station.Radius, station.GetProperty("radius").GetDouble());
        Assert.Equal(Station.ConstructionRadius, station.GetProperty("constructionRadius").GetDouble());
        SameWeapons(station.GetProperty("weapons"), Station.Weapons);
        var levels = station.GetProperty("levels");
        Assert.Equal(levels.GetArrayLength() + 1, Station.Levels.Count);
        for (var i = 0; i < levels.GetArrayLength(); i++)
        {
            var j = levels[i];
            var l = Station.Levels[i];
            // Upgrade costs and times, hulls and shields differ on purpose: all are higher here.
            foreach (var (name, value) in new[] { ("income", l.Income), ("shieldRegen", l.ShieldRegen) })
            {
                Assert.Equal(j.GetProperty(name).GetDouble(), value);
            }
            SameSlots(j.GetProperty("hardpoints"), l.Hardpoints);
        }
        var buildables = data.GetProperty("buildables");
        Assert.Equal(buildables.EnumerateObject().Select(p => p.Name.ToLowerInvariant()), WebBuildables.Select(b => b.ToString().ToLowerInvariant()));
        foreach (var type in WebBuildables)
        {
            var j = buildables.GetProperty(char.ToLowerInvariant(type.ToString()[0]) + type.ToString()[1..]);
            var d = Buildables.Defs[type];
            Assert.Equal(j.GetProperty("name").GetString(), d.Name);
            foreach (var (name, value) in new[] {
                ("hull", d.Hull), ("shield", d.Shield), ("shieldRegen", d.ShieldRegen), ("radius", d.Radius),
                ("cost", d.Cost), ("buildTime", d.BuildTime) })
            {
                Assert.Equal(j.GetProperty(name).GetDouble(), value);
            }
            Assert.Equal(j.GetProperty("minStationLevel").GetInt32(), d.MinStationLevel);
            SameWeapons(j.GetProperty("weapons"), d.Weapons);
            Assert.Equal(Optional(j, "income") ?? 0, d.Income);
            Assert.Equal(Optional(j, "zone"), d.Zone);
            Assert.Equal(j.TryGetProperty("nearMines", out var nm) && nm.GetBoolean(), d.NearMines);
            if (j.TryGetProperty("repair", out var r)) Assert.Equal(r.GetProperty("rate").GetDouble(), d.Repair!.Rate);
            else Assert.Null(d.Repair);
            if (!j.TryGetProperty("tiers", out var tiers))
            {
                Assert.Null(d.Tiers);
                continue;
            }
            Assert.Equal(tiers.GetArrayLength(), d.Tiers!.Count);
            for (var i = 0; i < d.Tiers.Count; i++)
            {
                var t = tiers[i];
                var tier = d.Tiers[i];
                Assert.Equal(t.GetProperty("cost").GetDouble(), tier.Cost);
                Assert.Equal(t.GetProperty("time").GetDouble(), tier.Time);
                Assert.Equal(t.GetProperty("hull").GetDouble(), tier.Hull);
                Assert.Equal(t.GetProperty("shield").GetDouble(), tier.Shield);
                Assert.Equal(Optional(t, "zone"), tier.Zone);
                if (t.TryGetProperty("weapons", out var w)) SameWeapons(w, tier.Weapons!);
            }
        }
    }

    [Fact]
    public void Abilities_heroes_and_techs_match()
    {
        var data = Load("data");
        var abilities = data.GetProperty("abilities");
        Assert.Equal(abilities.EnumerateObject().Select(p => p.Name.ToLowerInvariant()), WebAbilities.Select(a => a.ToString().ToLowerInvariant()));
        foreach (var id in WebAbilities)
        {
            var j = abilities.GetProperty(char.ToLowerInvariant(id.ToString()[0]) + id.ToString()[1..]);
            var d = Abilities.Defs[id];
            Assert.Equal(j.GetProperty("name").GetString(), d.Name);
            Assert.Equal(j.GetProperty("description").GetString(), d.Description);
            Assert.Equal(j.GetProperty("cooldown").GetDouble(), d.Cooldown);
            Assert.Equal(j.GetProperty("duration").GetDouble(), d.Duration);
            SameName(j.GetProperty("target"), d.Target);
            Assert.Equal(j.GetProperty("color").GetUInt32(), d.Color);
            Assert.Equal(Optional(j, "range"), d.Range);
            Assert.Equal(Optional(j, "radius"), d.Radius);
        }
        var heroes = data.GetProperty("heroes");
        Assert.Equal(heroes.EnumerateObject().Select(p => p.Name), Heroes.All.Select(h => h.Id.ToString()));
        foreach (var h in Heroes.All)
        {
            var j = heroes.GetProperty(h.Id.ToString());
            Assert.Equal(j.GetProperty("name").GetString(), h.Name);
            Assert.Equal(j.GetProperty("title").GetString(), h.Title);
            Assert.Equal(j.GetProperty("description").GetString(), h.Description);
            SameName(j.GetProperty("base"), h.Base);
            SameName(j.GetProperty("ability"), h.Ability);
            foreach (var (name, value) in new[] {
                ("hull", h.Hull), ("shield", h.Shield), ("damage", h.Damage), ("speed", h.Speed),
                ("cost", h.Cost), ("buildTime", h.BuildTime), ("respawn", h.Respawn) })
            {
                Assert.Equal(j.GetProperty(name).GetDouble(), value);
            }
            Assert.Equal(j.GetProperty("minStationLevel").GetInt32(), h.MinStationLevel);
        }
        var techs = data.GetProperty("techs");
        foreach (var id in Techs.All.Where(t => !NewTechs.Contains(t)))
        {
            var j = techs.GetProperty(id.ToString().ToLowerInvariant());
            var d = Techs.Defs[id];
            Assert.Equal(j.GetProperty("name").GetString(), d.Name);
            Assert.Equal(j.GetProperty("effect").GetString(), d.Effect);
            var costs = j.GetProperty("costs").EnumerateArray().Select(c => c.GetDouble()).ToList();
            Assert.Equal(costs, d.Costs.Take(costs.Count));
            var times = j.GetProperty("times").EnumerateArray().Select(c => c.GetDouble()).ToList();
            Assert.Equal(times, d.Times.Take(times.Count));
        }
    }
}
