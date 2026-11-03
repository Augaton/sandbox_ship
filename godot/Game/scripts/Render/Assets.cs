// Sprites and looks. assets/looks.json is written by the web version (`npm run fixtures`):
// which pack sprite each ship, structure, part and shot wears. At start, this class loads
// those sprites, draws the classes the packs lack (ShipForge) and, for every race, its stations,
// their parts and its buildings (ShipForge.Structures, in place of the web version's Space
// Breaker parts recolored), and makes the effect textures (render/placeholders.ts).
using System.Text.Json;
using Godot;
using SandboxShip.Sim;
using SandboxShip.Sim.Data;

namespace SandboxShip.Render;

/// <summary>A sprite of a look: its key, its visible size against the entity's diameter, and its offset in radii.</summary>
public sealed record Layer(string Key, double Size, double X = 0, double Y = 0);

/// <summary>
/// How an entity is drawn: its layers, its engine flames and destruction (ships), whether it
/// takes its side's color. Drawn structures add a part that turns over them (`Gun`: aimed where
/// they shoot, or `Spin`ning, a mine's drill), and lamps that live in battle (`Lamps`, their
/// places and `LampSize` in radii of the entity).
/// </summary>
public sealed record Look(IReadOnlyList<Layer> Layers, string? Engine, string? Destruction, bool Tint,
    Layer? Gun = null, bool Spin = false, IReadOnlyList<ShipForge.Lamp>? Lamps = null, double LampSize = 0);

public static class Assets
{
    sealed record Strip(Texture2D Texture, int FrameW, int FrameH, int Frames);

    static readonly Dictionary<string, Strip> strips = [];
    /// <summary>
    /// The sprites' pixels, only while the game loads: the faction copies and the opaque sizes
    /// are made from them, then they are let go; the textures alone stay, on the graphics card.
    /// </summary>
    static readonly Dictionary<string, Image> pixels = [];
    static readonly Dictionary<string, Texture2D> frame0 = [];
    /// <summary>Animations by strip, looping and pace: a key with no string to build, as a shot asks for one when it is fired.</summary>
    static readonly Dictionary<(string Key, bool Loop, double Fps), SpriteFrames> anims = [];
    static readonly Dictionary<string, double> opaque = [];
    static readonly Dictionary<(Faction, ShipClass), Look> ships = [];
    static readonly Dictionary<string, Look> structures = [];
    static readonly Dictionary<string, Look> hardpoints = [];
    /// <summary>Each faction's star lance, drawn by ShipForge.</summary>
    static readonly Dictionary<Faction, Look> lances = [];
    /// <summary>Drawn by ShipForge for every race: stations by level, parts by role and gun, buildings by type and level.</summary>
    static readonly Dictionary<(Faction, int), Look> stations = [];
    static readonly Dictionary<(Faction, HardpointRole, ShipForge.Gun), Look> parts = [];
    static readonly Dictionary<(Faction, BuildableType, int), Look> buildings = [];
    static readonly Dictionary<(Faction, WeaponKind), (string Key, bool Tint)> projectiles = [];

    public static IReadOnlyList<string> Asteroids { get; private set; } = [];
    public static string Planet { get; private set; } = "";
    public static IReadOnlyList<(string Key, double Factor)> BackdropTiles { get; private set; } = [];
    public static string? BackdropDecor { get; private set; }

    static bool loaded;

    /// <summary>Share of the loading done, 0 to 1, for the loading screen.</summary>
    public static double Progress { get; private set; }

    /// <summary>
    /// Loads the sprites and draws the ones of our own. These, the longest part, are drawn on
    /// every core while the main thread loads the rest and the loading screen keeps moving.
    /// </summary>
    public static async Task Load()
    {
        if (loaded) return;
        loaded = true;
        var tree = (SceneTree)Engine.GetMainLoop();
        async Task NextFrame() => await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        // The neutral Errants too, every class of theirs: they have no pack.
        var jobs = Enum.GetValues<Faction>().SelectMany(f => ShipForge.Classes(f).Select(c => (F: f, C: c))).ToArray();
        var forged = new ShipForge.Sprites?[jobs.Length];
        // Stations, parts and buildings too, in the same batch.
        var works = StructureWorks();
        var built = new ShipForge.Built?[works.Count];
        var total = jobs.Length + works.Count;
        var done = 0;
        var forging = Task.Run(() => Parallel.For(0, total, i =>
        {
            if (i < jobs.Length) forged[i] = ShipForge.Forge(jobs[i].C, jobs[i].F);
            else built[i - jobs.Length] = works[i - jobs.Length].Make();
            Interlocked.Increment(ref done);
        }));
        var root = JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://assets/looks.json")).RootElement;
        var sprites = root.GetProperty("sprites").EnumerateObject().ToList();
        for (var i = 0; i < sprites.Count; i++)
        {
            var s = sprites[i];
            var file = s.Value.GetProperty("file").GetString()!;
            if (file.Length == 0) continue;
            var tex = GD.Load<Texture2D>($"res://assets/{file}");
            var img = tex.GetImage();
            img.Convert(Image.Format.Rgba8);
            strips[s.Name] = new Strip(tex, s.Value.GetProperty("frameW").GetInt32(), s.Value.GetProperty("frameH").GetInt32(), s.Value.GetProperty("frames").GetInt32());
            pixels[s.Name] = img;
            if (i % 16 != 15) continue;
            Progress = 0.3 * i / sprites.Count + 0.6 * done / total;
            await NextFrame();
        }
        foreach (var f in root.GetProperty("ships").EnumerateObject())
        {
            foreach (var c in f.Value.EnumerateObject()) ships[(Parse<Faction>(f.Name), Parse<ShipClass>(c.Name))] = ReadLook(c.Value);
        }
        foreach (var s in root.GetProperty("structures").EnumerateObject()) structures[s.Name] = ReadLook(s.Value);
        foreach (var h in root.GetProperty("hardpoints").EnumerateObject()) hardpoints[h.Name] = ReadLook(h.Value);
        foreach (var f in root.GetProperty("projectiles").EnumerateObject())
        {
            foreach (var k in f.Value.EnumerateObject())
            {
                projectiles[(Parse<Faction>(f.Name), Parse<WeaponKind>(k.Name))] = (k.Value.GetProperty("key").GetString()!, k.Value.GetProperty("tint").GetBoolean());
            }
        }
        Asteroids = root.GetProperty("asteroids").EnumerateArray().Select(a => a.GetString()!).ToList();
        Planet = root.GetProperty("planet").GetString()!;
        var b = root.GetProperty("backdrop");
        BackdropTiles = b.GetProperty("tiles").EnumerateArray().Select(t => (t.GetProperty("key").GetString()!, t.GetProperty("factor").GetDouble())).ToList();
        BackdropDecor = b.GetProperty("decor").ValueKind == JsonValueKind.String ? b.GetProperty("decor").GetString() : null;
        MakeEffects();
        while (!forging.IsCompleted)
        {
            Progress = 0.3 + 0.6 * done / total;
            await NextFrame();
        }
        await forging;
        // Textures are made here, on the main thread; they replace the packs' looks of those classes.
        for (var i = 0; i < jobs.Length; i++) Forged(jobs[i].F, jobs[i].C, forged[i]);
        foreach (var f in Enum.GetValues<Faction>())
        {
            var key = $"gen-{f.ToString().ToLowerInvariant()}-starlance";
            var img = ShipForge.ForgeLance(f);
            Add(key, img, img.GetWidth(), img.GetHeight(), 1);
            lances[f] = new Look([new Layer(key, 1.15)], null, null, false);
        }
        for (var i = 0; i < works.Count; i++)
        {
            if (built[i] is not { } made) continue;
            Add(works[i].Key, made.Image, made.Image.GetWidth(), made.Image.GetHeight(), 1);
            if (Core.Dev.Dump is string into) made.Image.SavePng($"{into}/{works[i].Key}.png");
        }
        foreach (var key in strips.Keys) OpaqueSize(key);
        StructureLooks(works, built);
        foreach (var img in pixels.Values) img.Dispose();
        pixels.Clear();
        Progress = 1;
    }

    static T Parse<T>(string s) where T : struct, Enum => Enum.Parse<T>(s, true);

    /// <summary>A drawing to make while the game loads: its key, how to make it, and what its look is built from.</summary>
    sealed record Work(string Key, Func<ShipForge.Built?> Make, Faction F, string Kind, int Level = 0, HardpointRole Role = default, ShipForge.Gun Gun = default, BuildableType Type = default);

    static List<Work> StructureWorks()
    {
        var works = new List<Work>();
        foreach (var f in Enum.GetValues<Faction>())
        {
            var race = f.ToString().ToLowerInvariant();
            for (var level = 1; level <= Station.Levels.Count; level++)
            {
                var l = level;
                works.Add(new($"gen-{race}-station-{l}", () => ShipForge.Station(f, l), f, "station", l));
            }
            foreach (var role in Enum.GetValues<HardpointRole>())
            {
                var armed = role is HardpointRole.Turret or HardpointRole.HeavyTurret;
                foreach (var gun in armed ? Enum.GetValues<ShipForge.Gun>() : [default(ShipForge.Gun)])
                {
                    var (r, g) = (role, gun);
                    works.Add(new($"gen-{race}-{r.ToString().ToLowerInvariant()}{(armed ? "-" + g.ToString().ToLowerInvariant() : "")}", () => ShipForge.Part(f, r, g), f, "part", Role: r, Gun: g));
                }
            }
            foreach (var type in Buildables.All)
            {
                if (type == BuildableType.StarLance) continue;
                for (var level = 1; level <= 1 + (Buildables.Defs[type].Tiers?.Count ?? 0); level++)
                {
                    var (t, l) = (type, level);
                    var key = $"gen-{race}-{Ids.Of(t).ToLowerInvariant()}-{l}";
                    works.Add(new(key, () => ShipForge.Building(f, t, l), f, "building", l, Type: t));
                    works.Add(new($"{key}-gun", () => ShipForge.BuildingGun(f, t, l), f, "gun", l, Type: t));
                }
            }
        }
        return works;
    }

    /// <summary>
    /// The looks of what was drawn, each layer sized so that a drawn pixel stands for
    /// ShipForge.StructurePixel world units at the radius it was designed for; its lamps in radii.
    /// </summary>
    static void StructureLooks(List<Work> works, ShipForge.Built?[] built)
    {
        Layer Sized(string key, double radius) => new(key, OpaqueSize(key) * ShipForge.StructurePixel / (2 * radius));
        List<ShipForge.Lamp> InRadii(ShipForge.Built b, double radius) =>
            [.. b.Lamps.Select(l => l with { X = (float)(l.X * ShipForge.StructurePixel / radius), Y = (float)(l.Y * ShipForge.StructurePixel / radius) })];
        for (var i = 0; i < works.Count; i++)
        {
            var w = works[i];
            if (built[i] is not { } b || w.Kind == "gun") continue;
            var radius = w.Kind switch
            {
                "station" => Station.Radius,
                "part" => Hardpoints.Defs[w.Role].Radius,
                _ => Buildables.Defs[w.Type].Radius,
            };
            var size = ShipForge.StructurePixel / radius;
            switch (w.Kind)
            {
                case "station":
                    stations[(w.F, w.Level)] = new Look([Sized(w.Key, radius)], null, null, false, Lamps: InRadii(b, radius), LampSize: size);
                    break;
                case "part":
                    parts[(w.F, w.Role, w.Gun)] = new Look([Sized(w.Key, radius)], null, null, false, Lamps: InRadii(b, radius), LampSize: size);
                    break;
                default:
                    var gun = strips.ContainsKey($"{w.Key}-gun") ? Sized($"{w.Key}-gun", radius) : null;
                    // A mine stays on its rock: the pack's asteroid under the rig.
                    List<Layer> layers = w.Type == BuildableType.Mine && structures.TryGetValue("mine", out var mine) ? [mine.Layers[0], Sized(w.Key, radius)] : [Sized(w.Key, radius)];
                    buildings[(w.F, w.Type, w.Level)] = new Look(layers, null, null, false, gun, w.Type == BuildableType.Mine, InRadii(b, radius), size);
                    break;
            }
        }
    }

    /// <summary>
    /// The first weapon of a part's slot, from its parent's data: a player's replica of a battle
    /// over the network knows its parts' places but not their weapons.
    /// </summary>
    static WeaponDef? SlotWeapon(World world, Entity e, HardpointRole role)
    {
        var parent = world.Entities.Get(e.ParentId);
        IEnumerable<HardpointSlot> slots = parent?.Kind == EntityKind.Ship ? Ships.Defs[parent.Cls].Hardpoints ?? []
            : parent?.Type == "station" ? Station.Levels.SelectMany(l => l.Hardpoints) : [];
        var slot = slots.FirstOrDefault(s => s.Role == role && Math.Abs(s.X - e.Ox) < 0.5 && Math.Abs(s.Y - e.Oy) < 0.5);
        return (slot?.Weapons ?? Hardpoints.Defs[role].Weapons).FirstOrDefault();
    }

    static Look ReadLook(JsonElement j) => new(
        j.GetProperty("layers").EnumerateArray().Select(l => new Layer(l.GetProperty("key").GetString()!, l.GetProperty("size").GetDouble())).ToList(),
        j.GetProperty("engine").ValueKind == JsonValueKind.String ? j.GetProperty("engine").GetString() : null,
        j.GetProperty("destruction").ValueKind == JsonValueKind.String ? j.GetProperty("destruction").GetString() : null,
        j.GetProperty("tint").GetBoolean());

    public static Look LookFor(World world, Entity e) => e.Kind switch
    {
        EntityKind.Ship => ships[(e.Race, e.Cls)],
        EntityKind.Hardpoint when Enum.TryParse<HardpointRole>(e.Type, true, out var role)
            && parts.TryGetValue((e.Race, role, ShipForge.GunOf(SlotWeapon(world, e, role), role)), out var part) => part,
        EntityKind.Hardpoint => hardpoints[e.Type],
        _ when e.Type == "station" && stations.TryGetValue((e.Race, Math.Clamp(e.Level, 1, Station.Levels.Count)), out var station) => station,
        _ when Ids.IsBuildable(e.Type, out var type) && buildings.TryGetValue((e.Race, type, Math.Max(1, e.Level)), out var building) => building,
        _ when e.Type == "starLance" && lances.TryGetValue(e.Race, out var lance) => lance,
        _ => structures[e.Type],
    };

    /// <summary>
    /// A shot's sprite. This version's kinds borrow the packs': a missile flies as a small
    /// torpedo, an ion bolt as a laser bolt the view tints (BattleScreen.SyncShots).
    /// </summary>
    public static (string Key, bool Tint) Projectile(Faction f, WeaponKind k)
    {
        k = k switch { WeaponKind.Missile => WeaponKind.Torpedo, WeaponKind.Ion or WeaponKind.Beam => WeaponKind.Laser, _ => k };
        return projectiles.TryGetValue((f, k), out var p) ? p : projectiles[(Faction.Nairan, k)];
    }

    /// <summary>The pack sprite of a ship class, for pictures outside the battle (the menu); null for a placeholder.</summary>
    public static string? ShipSprite(Faction f, ShipClass c) => ships[(f, c)].Tint ? null : ships[(f, c)].Layers[0].Key;

    public static bool Has(string key) => strips.ContainsKey(key);

    public static int Frames(string key) => strips.TryGetValue(key, out var s) ? s.Frames : 1;

    /// <summary>The first frame of a sprite, alone.</summary>
    public static Texture2D Frame(string key)
    {
        if (frame0.TryGetValue(key, out var t)) return t;
        var s = strips[key];
        t = s.Frames > 1 ? new AtlasTexture { Atlas = s.Texture, Region = new Rect2(0, 0, s.FrameW, s.FrameH) } : s.Texture;
        frame0[key] = t;
        return t;
    }

    /// <summary>The frames of a strip as an animation, at 12 frames a second unless told otherwise.</summary>
    public static SpriteFrames Anim(string key, bool loop, double fps = 12)
    {
        var name = (key, loop, fps);
        if (anims.TryGetValue(name, out var a)) return a;
        var s = strips[key];
        a = new SpriteFrames();
        a.SetAnimationLoopMode("default", loop ? SpriteFrames.LoopMode.Linear : SpriteFrames.LoopMode.None);
        a.SetAnimationSpeed("default", fps);
        for (var i = 0; i < s.Frames; i++) a.AddFrame("default", new AtlasTexture { Atlas = s.Texture, Region = new Rect2(i * s.FrameW, 0, s.FrameW, s.FrameH) });
        anims[name] = a;
        return a;
    }

    /// <summary>
    /// Longest side of the opaque part of a sprite's first frame, in texture pixels. Pack
    /// sprites leave a lot of empty room around the ship, so sizes are matched on this. Worked
    /// out for every sprite as the game loads, while their pixels are at hand.
    /// </summary>
    public static double OpaqueSize(string key)
    {
        if (opaque.TryGetValue(key, out var hit)) return hit;
        var s = strips[key];
        if (!pixels.TryGetValue(key, out var img)) return opaque[key] = Math.Max(s.FrameW, s.FrameH);
        using var frame = img.GetRegion(new Rect2I(0, 0, s.FrameW, s.FrameH));
        var data = frame.GetData();
        var stride = s.FrameW;
        int minX = s.FrameW, minY = s.FrameH, maxX = -1, maxY = -1;
        for (var y = 0; y < s.FrameH; y++)
        {
            for (var x = 0; x < s.FrameW; x++)
            {
                if (data[(y * stride + x) * 4 + 3] <= 16) continue;
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }
        }
        double size = maxX < 0 ? Math.Max(s.FrameW, s.FrameH) : Math.Max(maxX - minX + 1, maxY - minY + 1);
        opaque[key] = size;
        return size;
    }


    static void Add(string key, Image img, int frameW, int frameH, int frames)
    {
        strips[key] = new Strip(ImageTexture.CreateFromImage(img), frameW, frameH, frames);
        pixels[key] = img;
    }

    /// <summary>
    /// The classes the packs lack, and all of the Errants', drawn from scratch in the packs' style
    /// (ShipForge): hull, engine flames and destruction, registered as the class's look for that
    /// faction.
    /// </summary>
    static void Forged(Faction f, ShipClass c, ShipForge.Sprites? sprites)
    {
        if (sprites is not { } s) return;
        var key = $"gen-{f.ToString().ToLowerInvariant()}-{c.ToString().ToLowerInvariant()}";
        var (w, h) = (s.Hull.GetWidth(), s.Hull.GetHeight());
        Add($"{key}-base", s.Hull, w, h, 1);
        Add($"{key}-engine", s.Engine, w, h, s.EngineFrames);
        Add($"{key}-destruction", s.Destruction, w, h, s.DestructionFrames);
        if (Core.Dev.Dump is string dir)
        {
            foreach (var part in new[] { "base", "engine", "destruction" }) pixels[$"{key}-{part}"].SavePng($"{dir}/{key}-{part}.png");
        }
        ships[(f, c)] = new Look([new Layer($"{key}-base", 1.25)], $"{key}-engine", $"{key}-destruction", false);
    }

    // Effect textures, white or colored, drawn once.

    static Image Radial(int size, params (double At, Color Color)[] stops)
    {
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        var r = size / 2.0;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var t = Math.Min(1, Math.Sqrt((x + 0.5 - r) * (x + 0.5 - r) + (y + 0.5 - r) * (y + 0.5 - r)) / r);
                var k = 1;
                while (k < stops.Length - 1 && stops[k].At < t) k++;
                var (a, b) = (stops[k - 1], stops[k]);
                img.SetPixel(x, y, a.Color.Lerp(b.Color, (float)Math.Clamp((t - a.At) / (b.At - a.At), 0, 1)));
            }
        }
        return img;
    }

    static Image Shot(int w, int h)
    {
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        var r = Math.Min(w, h) / 2.0;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                // A rounded rectangle: the corners outside their quarter circle stay clear.
                var cx = Math.Clamp(x + 0.5, r, w - r);
                var cy = Math.Clamp(y + 0.5, r, h - r);
                if ((x + 0.5 - cx) * (x + 0.5 - cx) + (y + 0.5 - cy) * (y + 0.5 - cy) <= r * r) img.SetPixel(x, y, Colors.White);
            }
        }
        return img;
    }

    static void MakeEffects()
    {
        var white = Colors.White;
        var clear = new Color(1, 1, 1, 0);
        Add("fog-brush", Radial(256, (0, white), (0.8, white), (1, clear)), 256, 256, 1);
        Add("ph-spark", Radial(8, (0, white), (1, clear)), 8, 8, 1);
        // The terrain's clouds (Battle/BattleScreen.Terrain.cs), tinted and drawn smoothed.
        Add("ph-cloud", Radial(96, (0, white), (0.45, new Color(1, 1, 1, 0.55f)), (1, clear)), 96, 96, 1);
        Add("ph-smoke", Radial(32, (0, new Color(70 / 255f, 70 / 255f, 76 / 255f, 0.95f)), (0.5, new Color(90 / 255f, 90 / 255f, 96 / 255f, 0.6f)), (1, new Color(90 / 255f, 90 / 255f, 96 / 255f, 0))), 32, 32, 1);
        Add("ph-boom", Radial(64, (0, new Color(1, 1, 230 / 255f, 1)), (0.35, new Color(1, 190 / 255f, 90 / 255f, 0.9f)), (1, new Color(1, 80 / 255f, 20 / 255f, 0))), 64, 64, 1);
        var debris = Image.CreateEmpty(6, 4, false, Image.Format.Rgba8);
        debris.Fill(new Color(0xb8b2a7ff));
        debris.FillRect(new Rect2I(0, 0, 2, 2), new Color(0xffb35cff));
        Add("ph-debris", debris, 6, 4, 1);
        Add("ph-shot-bullet", Shot(3, 6), 3, 6, 1);
        Add("ph-shot-laser", Shot(2, 14), 2, 14, 1);
        Add("ph-shot-torpedo", Shot(5, 10), 5, 10, 1);
        Add("ph-shot-ray", Shot(4, 24), 4, 24, 1);
    }
}
