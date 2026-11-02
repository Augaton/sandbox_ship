// Sprites and looks. assets/looks.json is written by the web version (`npm run fixtures`):
// which pack sprite each ship, structure, part and shot wears. At start, this class loads
// those sprites, composes the titans (render/capitals.ts) and, the same way, the classes the
// web version lacks (bulwark, carrier, leviathan), turns the red trim of the parts
// to each faction's color (render/recolor.ts) and makes the effect textures
// (render/placeholders.ts).
using System.Text.Json;
using Godot;
using SandboxShip.Sim;
using SandboxShip.Sim.Data;

namespace SandboxShip.Render;

/// <summary>A sprite of a look: its key, its visible size against the entity's diameter, and its offset in radii.</summary>
public sealed record Layer(string Key, double Size, double X = 0, double Y = 0);

public sealed record Look(IReadOnlyList<Layer> Layers, string? Engine, string? Destruction, bool Tint);

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
    /// <summary>Platforms by level, 1 to 3, and the lab: looks of this version only.</summary>
    static readonly Dictionary<int, Look> platformTiers = [];
    static Look? lab;
    /// <summary>Each faction's star lance, drawn by ShipForge.</summary>
    static readonly Dictionary<Faction, Look> lances = [];
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
        var done = 0;
        var forging = Task.Run(() => Parallel.For(0, jobs.Length, i =>
        {
            forged[i] = ShipForge.Forge(jobs[i].C, jobs[i].F);
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
            Progress = 0.3 * i / sprites.Count + 0.6 * done / jobs.Length;
            await NextFrame();
        }
        foreach (var f in root.GetProperty("ships").EnumerateObject())
        {
            foreach (var c in f.Value.EnumerateObject()) ships[(Parse<Faction>(f.Name), Parse<ShipClass>(c.Name))] = ReadLook(c.Value);
        }
        foreach (var s in root.GetProperty("structures").EnumerateObject()) structures[s.Name] = ReadLook(s.Value);
        foreach (var h in root.GetProperty("hardpoints").EnumerateObject()) hardpoints[h.Name] = ReadLook(h.Value);
        ExtraLooks(root.GetProperty("extra"));
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
        foreach (var part in root.GetProperty("parts").EnumerateArray()) RecolorPart(part.GetString()!);
        MakeEffects();
        while (!forging.IsCompleted)
        {
            Progress = 0.3 + 0.6 * done / jobs.Length;
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
        foreach (var key in strips.Keys) OpaqueSize(key);
        foreach (var img in pixels.Values) img.Dispose();
        pixels.Clear();
        Progress = 1;
    }

    static T Parse<T>(string s) where T : struct, Enum => Enum.Parse<T>(s, true);

    static Look ReadLook(JsonElement j) => new(
        j.GetProperty("layers").EnumerateArray().Select(l => new Layer(l.GetProperty("key").GetString()!, l.GetProperty("size").GetDouble())).ToList(),
        j.GetProperty("engine").ValueKind == JsonValueKind.String ? j.GetProperty("engine").GetString() : null,
        j.GetProperty("destruction").ValueKind == JsonValueKind.String ? j.GetProperty("destruction").GetString() : null,
        j.GetProperty("tint").GetBoolean());

    public static Look LookFor(Entity e) => e.Kind switch
    {
        EntityKind.Ship => ships[(e.Race, e.Cls)],
        EntityKind.Hardpoint => hardpoints[e.Type],
        _ when e.Type == "platform" && platformTiers.TryGetValue(e.Level, out var tier) => tier,
        _ when e.Type == "lab" && lab is not null => lab,
        _ when e.Type == "starLance" && lances.TryGetValue(e.Race, out var lance) => lance,
        _ => structures[e.Type],
    };

    /// <summary>
    /// Platforms that look their level, from a lone gun tower to a gun deck with three heavy
    /// turrets, and the lab: a station hull with a dish.
    /// </summary>
    static void ExtraLooks(JsonElement extra)
    {
        string? Key(string name) => extra.TryGetProperty(name, out var k) && k.ValueKind == JsonValueKind.String && strips.ContainsKey(k.GetString()!) ? k.GetString() : null;
        Look? Of(params (string Part, double Size, double X, double Y)[] layers)
        {
            var keys = layers.Select(l => Key(l.Part)).ToList();
            if (keys.Any(k => k is null)) return null;
            return new Look(layers.Select((l, i) => new Layer(keys[i]!, l.Size, l.X, l.Y)).ToList(), null, null, false);
        }
        if (Of(("tower", 1, 0, 0), ("laserSmall1", 0.45, 0, 0)) is { } t1) platformTiers[1] = t1;
        if (Of(("tower", 1.15, 0, 0), ("laserMedium2", 0.6, 0, 0)) is { } t2) platformTiers[2] = t2;
        if (Of(("baseMedium", 1.8, 0, 0), ("tower", 0.95, 0, 0), ("laserMedium3", 0.6, 0, -0.78),
                ("cannonMedium3", 0.55, -0.75, 0.6), ("missileMedium3", 0.55, 0.75, 0.6)) is { } t3) platformTiers[3] = t3;
        lab = Of(("labBody", 1.3, 0, 0), ("plasmaMedium1", 0.55, 0, -0.1), ("plasmaSmall2", 0.3, 0, 0.45));
    }

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

    /// <summary>Texture of a part in a faction's colors, when one was made; the part itself otherwise.</summary>
    public static string FactionKey(string key, Faction f) => strips.ContainsKey($"{key}~{f}") ? $"{key}~{f}" : key;

    static void Add(string key, Image img, int frameW, int frameH, int frames)
    {
        strips[key] = new Strip(ImageTexture.CreateFromImage(img), frameW, frameH, frames);
        pixels[key] = img;
    }

    // Faction colors on the parts.

    /// <summary>Hues this close to red (in degrees) count as trim.</summary>
    const double Trim = 35;

    static (double H, double S, double L) Hsl(double r, double g, double b)
    {
        r /= 255;
        g /= 255;
        b /= 255;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        if (max == min) return (0, 0, l);
        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        var h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h * 60, s, l);
    }

    static (byte R, byte G, byte B) Rgb(double h, double s, double l)
    {
        var c = (1 - Math.Abs(2 * l - 1)) * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = l - c / 2;
        var (r, g, b) = h < 60 ? (c, x, 0.0) : h < 120 ? (x, c, 0.0) : h < 180 ? (0.0, c, x) : h < 240 ? (0.0, x, c) : h < 300 ? (x, 0.0, c) : (c, 0.0, x);
        static byte B8(double v) => (byte)Math.Clamp(Js.Round(v * 255), 0, 255);
        return (B8(r + m), B8(g + m), B8(b + m));
    }

    static double HueOf(uint color) => Hsl((color >> 16) & 255, (color >> 8) & 255, color & 255).H;

    /// <summary>Makes the faction copies of a part; red factions keep the original.</summary>
    static void RecolorPart(string key)
    {
        if (!strips.TryGetValue(key, out var s) || !pixels.TryGetValue(key, out var img)) return;
        foreach (var f in Enum.GetValues<Faction>())
        {
            var toHue = HueOf(Factions.Defs[f].Color);
            if (Math.Min(toHue, 360 - toHue) <= Trim) continue;
            var data = img.GetData();
            for (var i = 0; i < data.Length; i += 4)
            {
                if (data[i + 3] == 0) continue;
                var (h, sat, l) = Hsl(data[i], data[i + 1], data[i + 2]);
                if (sat < 0.35 || Math.Min(h, 360 - h) > Trim) continue;
                (data[i], data[i + 1], data[i + 2]) = Rgb(toHue, sat, l);
            }
            Add($"{key}~{f}", Image.CreateFromData(img.GetWidth(), img.GetHeight(), false, Image.Format.Rgba8, data), s.FrameW, s.FrameH, s.Frames);
        }
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
