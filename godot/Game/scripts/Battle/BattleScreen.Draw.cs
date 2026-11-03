// What the battle view draws: ships and structures, shots, effects, overlays and the fog of
// war. Port of the drawing half of src/scenes/BattleScene.ts.
using Godot;
using SandboxShip.Core;
using SandboxShip.Render;
using SandboxShip.Sim;
using SandboxShip.Sim.Data;
using SandboxShip.Ui;

namespace SandboxShip.Battle;

public partial class BattleScreen
{
    sealed class View
    {
        public required Node2D Root;
        public CanvasItem? Engine;
        public required Look Look;
        /// <summary>Scale of the first layer, shared by the engine and destruction animations.</summary>
        public double Scale;
        /// <summary>Milliseconds before the next puff of smoke, once badly damaged.</summary>
        public double Smoke;
        /// <summary>Level it was drawn at: a platform looks its level.</summary>
        public int Level;
        /// <summary>A part of a ship (not of a station): hidden from afar, turning with its ship.</summary>
        public bool ShipPart;
        /// <summary>What turns over a building: its gun, aimed where it shoots, or a mine's drill, spinning.</summary>
        public Node2D? Gun;
        public bool Spin;
        public float GunRotation;
        // What the nodes were last given: a write that changes nothing still costs a call into
        // the engine and a transform update, for every entity every frame. Node defaults first.
        public Vector2 Position;
        public float Rotation, Alpha = 1, Grow = 1;
        public bool Shown = true, EngineOn = true;
    }

    /// <summary>A shot's sprite, and the frame it was last seen alive.</summary>
    sealed class Shot(AnimatedSprite2D img)
    {
        public readonly AnimatedSprite2D Img = img;
        public int Seen;
        public float Rotation = float.NaN;
        public bool Shown = true;
    }

    /// <summary>When each big ship came through its portal, by id: it grows out of it.</summary>
    readonly Dictionary<int, double> arrivals = [];
    const double ArrivalMs = 1400;

    /// <summary>Something shown for a moment: drawn every frame with its progress, 0 to 1.</summary>
    sealed record Transient(double Start, double Duration, Action<CanvasItem, double> Paint, Action? Done = null);

    /// <summary>Sprites face up; a sim angle of 0 points right.</summary>
    const double Facing = Math.PI / 2;
    const int MaxSparks = 120;
    /// <summary>Ability names floating up at once, at most.</summary>
    const int MaxCallouts = 10;
    /// <summary>Below this share of its hull, a ship or structure smokes.</summary>
    const double Smoking = 0.35;
    const double ShieldFlashMs = 250;
    /// <summary>World pixels per fog texel: drawn small and stretched with smoothing, its edges blur.</summary>
    const int FogCell = 16;

    /// <summary>Draw order, as the web version's depths.</summary>
    static class Depth
    {
        public const double Field = 0, Structure = 10, Hardpoint = 12, Ship = 20, Shot = 30, Fx = 40, Fog = 45, Overlay = 50;
        public static int Z(double depth) => (int)Math.Round(depth * 50);
    }

    static readonly Dictionary<WeaponKind, Sound> ShotSound = new()
    {
        [WeaponKind.Bullet] = Sound.Bullet, [WeaponKind.Laser] = Sound.Laser, [WeaponKind.Torpedo] = Sound.Torpedo, [WeaponKind.Ray] = Sound.Ray,
        // Not in the web version; a beam is heard as a held hum (SyncShots), a burst cannon's rounds by their speed.
        [WeaponKind.Missile] = Sound.Missile, [WeaponKind.Ion] = Sound.Ion, [WeaponKind.Beam] = Sound.Beam,
    };

    /// <summary>A shot's sound: a burst cannon's bullet is told from flak's by its speed, all a shot carries over the network.</summary>
    static Sound ShotSoundOf(Projectile p) =>
        p.Kind == WeaponKind.Bullet && p.Vx * p.Vx + p.Vy * p.Vy > Weapons.BurstRoundSpeed * Weapons.BurstRoundSpeed ? Sound.Burst : ShotSound[p.Kind];

    static readonly Color IonTint = Palette.Hex(0x8fe8ff);
    Node2D worldRoot = null!;
    Node2D viewsRoot = null!;
    readonly Dictionary<int, View> views = [];
    readonly Dictionary<int, Shot> shots = [];
    /// <summary>Counts SyncShots calls: shots not stamped with the current one are gone.</summary>
    int shotFrame;
    /// <summary>One batch for the shapes of every layer, filled and sent by each in turn.</summary>
    readonly ShapeBatch shapes = new();
    readonly Stack<AnimatedSprite2D> shotPool = [];
    DrawCanvas overlay = null!, lod = null!, fx = null!, trails = null!, transientsFx = null!, transientsTop = null!, lamps = null!, partLamps = null!;
    Puffs debris = null!, smoke = null!;
    readonly List<Transient> fxList = [], topList = [];
    int callouts;
    /// <summary>
    /// Sparks of the hits, drawn from a plain list rather than as moments of their own: a big
    /// battle has a hundred a second, and a moment costs a few objects to make.
    /// </summary>
    readonly List<(double X, double Y, double Start)> sparkList = [];
    const double SparkMs = 160;
    /// <summary>The one animation of a strip's frames, named once: a string where Godot wants a StringName makes a new one at every call.</summary>
    static readonly StringName DefaultAnim = "default";
    /// <summary>When each shield last took a hit, by entity id: a bubble lights up around it.</summary>
    readonly Dictionary<int, double> shieldFlash = [];
    /// <summary>Highest projectile id already heard: newer ones are new shots.</summary>
    int lastShot;
    /// <summary>Recent callouts: a group casting the same ability at once gets a single one.</summary>
    readonly List<(AbilityId Id, double X, double Y, double Time)> recent = [];
    SubViewport fogMask = null!;
    int fogFrame;
    DrawCanvas fogBrushes = null!;
    Sprite2D fogVeil = null!;

    const string FogShader = """
        shader_type canvas_item;
        uniform vec4 veil : source_color = vec4(0.0196, 0.0235, 0.0392, 0.75);
        void fragment() {
            float seen = texture(TEXTURE, UV).a;
            COLOR = vec4(veil.rgb, veil.a * (1.0 - seen));
        }
        """;

    DrawCanvas Layer(double depth, string name, Action<DrawCanvas> paint)
    {
        var c = new DrawCanvas
        {
            ZIndex = Depth.Z(depth),
            Batch = shapes,
            OnDraw = g =>
            {
                var t = StressProbe.Start();
                paint(g);
                StressProbe.Stop(name, t);
            },
        };
        worldRoot.AddChild(c);
        return c;
    }

    void BuildWorldView()
    {
        worldRoot = new Node2D();
        AddChild(worldRoot);
        DrawFields();
        viewsRoot = new Node2D();
        worldRoot.AddChild(viewsRoot);
        trails = Layer(Depth.Ship - 1, "trails", DrawTrails);
        // Over the structures and under their parts; over the parts and under the ships.
        lamps = Layer(Depth.Structure + 1, "lamps", g => DrawLamps(g, false));
        partLamps = Layer(Depth.Hardpoint + 0.5, "lamps", g => DrawLamps(g, true));
        lod = Layer(Depth.Shot, "lod", DrawLod);
        fx = Layer(Depth.Fx, "effects", DrawEffects);
        transientsFx = Layer(Depth.Fx, "transients", g =>
        {
            PaintTransients(g, fxList);
            PaintSparks(g);
        });
        transientsTop = Layer(Depth.Overlay, "transients", g => PaintTransients(g, topList));
        overlay = Layer(Depth.Overlay, "overlay", DrawOverlay);
        BuildModeView();
        debris = new Puffs
        {
            Texture = Assets.Frame("ph-debris"), LifeMin = 0.5, LifeMax = 1.4, SpeedMin = 60, SpeedMax = 320, ScaleStart = 1.1, ScaleEnd = 0.3, Spin = true,
            ZIndex = Depth.Z(Depth.Fx),
        };
        smoke = new Puffs
        {
            Texture = Assets.Frame("ph-smoke"), LifeMin = 1.6, LifeMax = 1.6, SpeedMin = 8, SpeedMax = 34, ScaleStart = 0.8, ScaleEnd = 2.4, AlphaStart = 0.7,
            ZIndex = Depth.Z(Depth.Fx - 1),
        };
        worldRoot.AddChild(debris);
        worldRoot.AddChild(smoke);
        // A dark veil over the whole map, with holes where the player's units see: the holes are
        // drawn white into a small mask, which the veil's shader turns into clear spots.
        var fogW = (int)Math.Ceiling((World.Width + 2 * Margin) / FogCell);
        var fogH = (int)Math.Ceiling((World.Height + 2 * Margin) / FogCell);
        fogMask = new SubViewport
        {
            Size = new Vector2I(fogW, fogH), TransparentBg = true, Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always, CanvasItemDefaultTextureFilter = Viewport.DefaultCanvasItemTextureFilter.Linear,
        };
        fogBrushes = new DrawCanvas
        {
            OnDraw = g =>
            {
                var t = StressProbe.Start();
                DrawFogHoles(g);
                StressProbe.Stop("fog", t);
            },
        };
        fogMask.AddChild(fogBrushes);
        AddChild(fogMask);
        fogVeil = new Sprite2D
        {
            Texture = fogMask.GetTexture(), Centered = false, Position = new Vector2((float)-Margin, (float)-Margin), Scale = new Vector2(FogCell, FogCell),
            TextureFilter = CanvasItem.TextureFilterEnum.Linear, ZIndex = Depth.Z(Depth.Fog),
            Material = new ShaderMaterial { Shader = new Shader { Code = FogShader } },
        };
        worldRoot.AddChild(fogVeil);
    }

    void RedrawLayers()
    {
        lamps.QueueRedraw();
        partLamps.QueueRedraw();
        lod.QueueRedraw();
        fx.QueueRedraw();
        trails.QueueRedraw();
        transientsFx.QueueRedraw();
        transientsTop.QueueRedraw();
        overlay.QueueRedraw();
        modeMarks?.QueueRedraw();
        fogVeil.Visible = World.Fog && !Reveal;
        // Units move a few pixels a frame under 16-pixel fog texels, blurred: redrawing the mask
        // every other frame looks the same and halves its cost. In between it keeps its picture.
        if (World.Fog && !Reveal && ++fogFrame % 2 == 0)
        {
            fogBrushes.QueueRedraw();
            fogMask.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        }
        else if (!World.Fog) fogMask.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
    }

    void DrawFields()
    {
        var keys = Assets.Asteroids;
        var rings = new DrawCanvas
        {
            ZIndex = Depth.Z(Depth.Field),
            OnDraw = g =>
            {
                foreach (var f in World.Fields) Draw.Ring(g, f.X, f.Y, f.Radius, Palette.Hex(0x8a94a6, 0.15f), 2, 1);
            },
        };
        worldRoot.AddChild(rings);
        long seed = 1;
        double Rnd() => (seed = seed * 16807 % 2147483647) / 2147483647.0;
        foreach (var f in World.Fields)
        {
            var n = (int)Js.Round(f.Radius / 14);
            for (var i = 0; i < n; i++)
            {
                var a = Rnd() * Math.PI * 2;
                var d = Math.Sqrt(Rnd()) * f.Radius * 0.9;
                var key = keys[i % keys.Count];
                var rotation = Rnd() * Math.PI * 2;
                var scale = (20 + Rnd() * 25) / Assets.OpaqueSize(key);
                worldRoot.AddChild(new Sprite2D
                {
                    Texture = Assets.Frame(key), Position = new Vector2((float)(f.X + Math.Cos(a) * d), (float)(f.Y + Math.Sin(a) * d)),
                    Rotation = (float)rotation, Scale = new Vector2((float)scale, (float)scale), ZIndex = Depth.Z(Depth.Field),
                });
            }
        }
    }

    static CanvasItem Sprite(string key, double scale, Color? tint)
    {
        CanvasItem s;
        if (Assets.Frames(key) > 1)
        {
            var a = new AnimatedSprite2D { SpriteFrames = Assets.Anim(key, true) };
            a.Play();
            s = a;
        }
        else s = new Sprite2D { Texture = Assets.Frame(key) };
        ((Node2D)s).Scale = new Vector2((float)scale, (float)scale);
        if (tint is Color c) s.Modulate = c;
        return s;
    }

    /// <summary>Scale that gives the visible part of a layer its intended size on screen.</summary>
    static double LayerScale(Entity e, string key, double size) => e.Radius * 2 * size / Assets.OpaqueSize(key);

    View CreateView(Entity e)
    {
        var look = Assets.LookFor(World, e);
        Color? tint = look.Tint ? Palette.Of(e.Race) : null;
        var scale = LayerScale(e, look.Layers[0].Key, look.Layers[0].Size);
        // Ship parts sit on top of their ship.
        var parent = World.Entities.Get(e.ParentId);
        var depth = parent?.Kind == EntityKind.Ship ? Depth.Ship + 1 + parent.Radius / 100
            : (e.Kind == EntityKind.Ship ? Depth.Ship : e.Kind == EntityKind.Hardpoint ? Depth.Hardpoint : Depth.Structure) + e.Radius / 100;
        var root = new Node2D { ZIndex = Depth.Z(depth) };
        var engine = look.Engine is string ek ? Sprite(ek, scale, null) : null;
        if (engine is not null) root.AddChild(engine);
        foreach (var l in look.Layers)
        {
            var key = l.Key;
            var s = (Node2D)Sprite(key, LayerScale(e, key, l.Size), tint);
            s.Position = new Vector2((float)(l.X * e.Radius), (float)(l.Y * e.Radius));
            root.AddChild(s);
        }
        var gun = look.Gun is { } g ? (Node2D)Sprite(g.Key, LayerScale(e, g.Key, g.Size), tint) : null;
        if (gun is not null) root.AddChild(gun);
        viewsRoot.AddChild(root);
        var view = new View
        {
            Root = root, Engine = engine, Look = look, Scale = scale, Level = e.Level, ShipPart = e.Kind == EntityKind.Hardpoint && parent?.Kind == EntityKind.Ship,
            Gun = gun, Spin = look.Spin,
        };
        views[e.Id] = view;
        return view;
    }

    /// <summary>A gun's slow sweep with nothing to aim at, in radians either side of where it faces; each its own.</summary>
    double Sweep(Entity e) => 0.5 * Math.Sin(Now / 1000 * 0.35 + e.Id * 1.7);

    /// <summary>
    /// The lamps of the structures in view, or of their parts: each a lit pixel in a faint halo,
    /// blinking or breathing on its own beat. Not from afar, nor on what is not seen now.
    /// </summary>
    void DrawLamps(CanvasItem g, bool onParts)
    {
        if (Zoom < LodSimple) return;
        var now = Now / 1000;
        foreach (var e in World.Entities)
        {
            if (e.Kind == EntityKind.Ship || (e.Kind == EntityKind.Hardpoint) != onParts || e.Build < 1) continue;
            if (!views.TryGetValue(e.Id, out var v) || !v.Shown || v.ShipPart || v.Look.Lamps is not { Count: > 0 } lamps || !CanSee(e)) continue;
            var (r, size) = (e.Radius * v.Grow, v.Look.LampSize * e.Radius * v.Grow);
            var (c, s) = (Math.Cos(v.Rotation), Math.Sin(v.Rotation));
            foreach (var l in lamps)
            {
                var k = l.Blink ? ((now * 0.7 + l.Phase) % 1 < 0.5 ? 1 : 0.1) : 0.55 + 0.45 * Math.Sin((now * 0.8 + l.Phase) * Math.Tau);
                var (x, y) = (l.X * r, l.Y * r);
                var (wx, wy) = (e.X + x * c - y * s, e.Y + x * s + y * c);
                Draw.Rect(g, wx - size * 1.5, wy - size * 1.5, size * 3, size * 3, l.Color with { A = (float)(0.16 * k) });
                Draw.Rect(g, wx - size / 2, wy - size / 2, size, size, l.Color with { A = (float)(0.95 * k) });
            }
        }
    }

    static void Show(View v, bool shown)
    {
        if (v.Shown == shown) return;
        v.Shown = shown;
        v.Root.Visible = shown;
    }

    void SyncViews(double delta)
    {
        var z = Zoom;
        var rect = ViewRect();
        var simple = z < LodSimple;
        var icons = z < LodIcons;
        foreach (var e in World.Entities)
        {
            var v = views.TryGetValue(e.Id, out var found) ? found : CreateView(e);
            if (e.Kind == EntityKind.Structure && v.Level != e.Level)
            {
                v.Root.QueueFree();
                v = CreateView(e);
            }
            // Off screen, or a detail too small to matter at this zoom: not drawn at all.
            var r = (float)e.Radius + 40;
            var onScreen = e.X + r > rect.Position.X && e.X - r < rect.End.X && e.Y + r > rect.Position.Y && e.Y - r < rect.End.Y;
            var chevron = icons && e.Kind == EntityKind.Ship && e.Size == SizeClass.Small;
            if (!onScreen || (simple && v.ShipPart) || chevron)
            {
                Show(v, false);
                continue;
            }
            var position = new Vector2((float)e.X, (float)e.Y);
            if (position != v.Position)
            {
                v.Position = position;
                v.Root.Position = position;
            }
            var rotation = v.Rotation;
            if (e.Kind == EntityKind.Ship) rotation = (float)(e.Angle + Facing);
            else if (e.Kind == EntityKind.Hardpoint)
            {
                // Turrets track their target; ship parts otherwise turn with their ship, and a
                // station's turrets sweep slowly round the way they face.
                var t = e.Weapons.Count > 0 ? World.Entities.Get(e.TargetId) : null;
                if (t is not null) rotation = (float)(Math.Atan2(t.Y - e.Y, t.X - e.X) + Facing);
                else if (v.ShipPart) rotation = (float)(e.Angle + Facing);
                else if (e.Weapons.Count > 0) rotation = (float)(Math.Atan2(e.Oy, e.Ox) + Facing + Sweep(e));
            }
            if (v.Gun is not null)
            {
                // A building's gun aims where it shoots, else sweeps; a mine's drill keeps turning.
                var t = v.Spin ? null : World.Entities.Get(e.TargetId);
                var aim = v.Spin ? Now / 1000 * 2.2 + e.Id : t is not null ? Math.Atan2(t.Y - e.Y, t.X - e.X) + Facing : e.Id + Sweep(e) * 2.5;
                if ((float)aim != v.GunRotation)
                {
                    v.GunRotation = (float)aim;
                    v.Gun.Rotation = (float)aim;
                }
            }
            if (rotation != v.Rotation)
            {
                v.Rotation = rotation;
                v.Root.Rotation = rotation;
            }
            // Enemies out of sight are hidden; structures seen before stay, greyed out.
            var seen = CanSee(e);
            var remembered = !seen && e.Kind != EntityKind.Ship && Vision.WasSeen(World, Opts.Player, e);
            Show(v, seen || remembered);
            var cloaked = Vision.Cloaked(e);
            var alpha = remembered ? 0.45 : cloaked ? 0.35 : e.Build < 1 ? 0.4 + 0.4 * e.Build : 1;
            // Out of its portal: it grows and comes into being.
            if (arrivals.TryGetValue(e.Id, out var came))
            {
                var k = Math.Min(1, (Now - came) / ArrivalMs);
                var grow = (float)(0.25 + 0.75 * EaseOutCubic(k));
                if (grow != v.Grow)
                {
                    v.Grow = grow;
                    v.Root.Scale = Vector2.One * grow;
                }
                alpha *= k;
                if (k >= 1) arrivals.Remove(e.Id);
            }
            if ((float)alpha != v.Alpha)
            {
                v.Alpha = (float)alpha;
                v.Root.Modulate = new Color(1, 1, 1, (float)alpha);
            }
            var engineOn = e.Speed > 5 && !simple;
            if (v.Engine is not null && engineOn != v.EngineOn)
            {
                v.EngineOn = engineOn;
                v.Engine.Visible = engineOn;
            }
            if (!simple && seen && e.Kind != EntityKind.Hardpoint && e.Build >= 1 && e.Hull < e.MaxHull * Smoking)
            {
                v.Smoke -= delta * 1000;
                if (v.Smoke <= 0)
                {
                    v.Smoke = 160;
                    var sr = e.Radius * 0.5;
                    smoke.Emit(e.X + (random.NextDouble() - 0.5) * sr, e.Y + (random.NextDouble() - 0.5) * sr, e.Radius > 30 ? 2 : 1);
                }
            }
        }
        // Every entity has its view now: more views than entities means some lost theirs.
        if (views.Count <= World.Entities.Count) return;
        foreach (var (id, v) in views)
        {
            if (World.Entities.Has(id)) continue;
            v.Root.QueueFree();
            views.Remove(id);
        }
    }

    /// <summary>Strokes and chevrons standing in for shots and small craft when zoomed out; under the fog.</summary>
    void DrawLod(CanvasItem g)
    {
        var z = Zoom;
        if (z < LodSimple)
        {
            // Zoomed out: shots are strokes along their path, all in one batch.
            foreach (var p in World.Projectiles)
                Draw.Line(g, p.X, p.Y, p.X - p.Vx * 0.04, p.Y - p.Vy * 0.04, Palette.Of(Worlds.RaceOf(World, p.Side), 0.9f), 1.5 / z);
        }
        if (z >= LodIcons) return;
        var rect = ViewRect().Grow(40);
        foreach (var e in World.Entities)
        {
            if (e.Kind != EntityKind.Ship || e.Size != SizeClass.Small || !rect.HasPoint(new Vector2((float)e.X, (float)e.Y)) || !CanSee(e)) continue;
            // A small craft seen from afar: a chevron in its side's color, pointing where it flies.
            var s = 5 / z;
            var c = Math.Cos(e.Angle);
            var n = Math.Sin(e.Angle);
            Draw.Triangle(g,
                new Vector2((float)(e.X + c * s * 1.4), (float)(e.Y + n * s * 1.4)),
                new Vector2((float)(e.X - c * s + n * s), (float)(e.Y - n * s - c * s)),
                new Vector2((float)(e.X - c * s - n * s), (float)(e.Y - n * s + c * s)),
                Palette.Of(World, e.Side));
        }
    }

    /// <summary>
    /// Beams holding in view make one held hum, a little louder the more there are, panned toward
    /// them, quieter from afar. With none in view, the battle paused, or the fight behind the menu,
    /// it fades out: a beam is heard as long as it holds, never once a frame.
    /// </summary>
    void HumBeams()
    {
        var v = ViewRect();
        var (count, sum) = (0, 0.0);
        if (!Opts.Demo && !Paused)
        {
            foreach (var b in World.Beams)
            {
                if (World.Entities.Get(b.From) is not { } from || !CanSee(from) || !v.HasPoint(new Vector2((float)from.X, (float)from.Y))) continue;
                count++;
                sum += from.X;
            }
        }
        var level = count == 0 ? 0 : Math.Min(1, 0.45 + 0.1 * count) * Math.Min(1, 0.45 + Zoom * 0.55);
        var pan = count == 0 ? 0 : ((sum / count - v.Position.X) / v.Size.X * 2 - 1) * 0.8;
        Sfx.Hum(level, pan, GetProcessDeltaTime());
    }

    void SyncShots()
    {
        var newest = lastShot;
        foreach (var p in World.Projectiles)
        {
            if (p.Id <= lastShot) continue;
            newest = Math.Max(newest, p.Id);
            Play(ShotSoundOf(p), p.X, p.Y, p.Kind == WeaponKind.Missile ? 0.35 : 0.7);
            // The muzzle flashes where the shot is first seen, at the end of its gun.
            Spark(p.X, p.Y);
        }
        lastShot = newest;
        HumBeams();
        var frame = ++shotFrame;
        if (Zoom >= LodSimple)
        {
            // Wide enough for the longest shot sprite to leave the screen before it is hidden.
            var rect = ViewRect().Grow(100);
            foreach (var p in World.Projectiles)
            {
                if (!shots.TryGetValue(p.Id, out var shot))
                {
                    var race = Worlds.RaceOf(World, p.Side);
                    var (key, tint) = Assets.Projectile(race, p.Kind);
                    var img = shotPool.Count > 0 ? shotPool.Pop() : NewShot();
                    img.Visible = true;
                    img.SpriteFrames = Assets.Anim(key, true);
                    img.Play();
                    // This version's: missiles are small torpedoes, ion bolts pale blue lasers.
                    img.Modulate = p.Kind == WeaponKind.Ion ? IonTint : tint ? Palette.Of(race) : Colors.White;
                    img.Scale = Vector2.One * (p.Kind == WeaponKind.Missile ? 0.6f : 1);
                    shots[p.Id] = shot = new Shot(img);
                }
                shot.Seen = frame;
                // Off screen, a shot is hidden and left where it was.
                var at = new Vector2((float)p.X, (float)p.Y);
                var shown = rect.HasPoint(at);
                if (shown != shot.Shown)
                {
                    shot.Shown = shown;
                    shot.Img.Visible = shown;
                }
                if (!shown) continue;
                shot.Img.Position = at;
                // Only homing shots turn: the others keep the heading they were fired with.
                var rotation = (float)(Math.Atan2(p.Vy, p.Vx) + Facing);
                if (rotation != shot.Rotation)
                {
                    shot.Rotation = rotation;
                    shot.Img.Rotation = rotation;
                }
            }
        }
        foreach (var (id, shot) in shots)
        {
            if (shot.Seen == frame) continue;
            shots.Remove(id);
            shot.Img.Visible = false;
            shot.Img.Stop();
            shotPool.Push(shot.Img);
        }
    }

    AnimatedSprite2D NewShot()
    {
        var s = new AnimatedSprite2D { ZIndex = Depth.Z(Depth.Shot) };
        worldRoot.AddChild(s);
        return s;
    }

    // Moments: markers, ripples, booms, sparks, callouts.

    void Add(List<Transient> list, double ms, Action<CanvasItem, double> paint, Action? done = null) => list.Add(new Transient(Now, ms, paint, done));

    static void PaintTransients(CanvasItem g, List<Transient> list)
    {
        var now = Now;
        for (var i = list.Count - 1; i >= 0; i--)
        {
            var t = list[i];
            var k = (now - t.Start) / t.Duration;
            if (k >= 1)
            {
                list.RemoveAt(i);
                t.Done?.Invoke();
                continue;
            }
        }
        foreach (var t in list) t.Paint(g, Math.Max(0, (now - t.Start) / t.Duration));
    }

    static double EaseOutCubic(double k) => 1 - Math.Pow(1 - k, 3);
    static double EaseInCubic(double k) => k * k * k;

    /// <summary>Brief ring where an order was given.</summary>
    public void Marker(double x, double y, Color color)
    {
        var z = Zoom;
        Add(fxList, 350, (g, k) => Draw.Ring(g, x, y, 14 / z * (1 - 0.7 * k), color with { A = (float)(1 - k) }, 2 / z, z));
    }

    void Ripple(double x, double y, double radius, Color color)
    {
        var z = Zoom;
        Add(fxList, 600, (g, k) =>
        {
            var e = EaseOutCubic(k);
            var r = radius * (0.2 + 0.8 * e);
            var a = (float)(1 - e);
            Draw.Disc(g, x, y, r, color with { A = 0.12f * a }, z);
            Draw.Ring(g, x, y, r, color with { A = 0.9f * a }, 4 / z * (0.2 + 0.8 * e), z);
        });
    }

    void Boom(double x, double y, double scale, double grow, double ms, float alpha = 1)
    {
        var tex = Assets.Frame("ph-boom");
        Add(fxList, ms, (g, k) => Draw.Sprite(g, tex, x, y, 0, scale * (1 + (grow - 1) * k), new Color(1, 1, 1, alpha * (float)(1 - k))));
    }

    /// <summary>The ability's name rises above the ship, and an area ability ripples out to its reach.</summary>
    void AbilityShown(Entity e, AbilityId id)
    {
        var def = Abilities.Defs[id];
        var z = Zoom;
        var now = Now;
        recent.RemoveAll(r => now - r.Time >= 600);
        var echo = recent.Exists(r => r.Id == id && Worlds.Hypot(r.X - e.X, r.Y - e.Y) < 300);
        if (!echo) recent.Add((id, e.X, e.Y, now));
        if (!echo && callouts < MaxCallouts)
        {
            callouts++;
            var (x, y) = (e.X, e.Y - e.Radius - 10 / z);
            var color = Palette.Hex(def.Color);
            // Drawn on the battlefield, not by a control: translated here.
            var name = Lang.T(def.Name);
            Add(topList, 1400, (g, k) =>
            {
                var c = EaseInCubic(k);
                Draw.Text(g, UiTheme.Mono, name, x, y - 40 / z * c, 14, color with { A = (float)(1 - c) }, z, Palette.Bg with { A = (float)(1 - c) });
            }, () => callouts--);
        }
        if (def.Target == AbilityTarget.Self && def.Radius is double radius && def.Duration == 0) Ripple(e.X, e.Y, radius, Palette.Hex(def.Color));
    }

    void StrikeShown(Strike s)
    {
        if (s.Kind == StrikeKind.Stellar)
        {
            // A column of light, then the blast rolling out over the whole zone.
            Add(fxList, 900, (g, k) =>
            {
                var a = (float)(1 - EaseInCubic(k));
                Draw.Disc(g, s.X, s.Y, s.Radius * (0.3 + 0.7 * k), Palette.Hex(0xfff2d0, 0.55f * a), Zoom);
                Draw.Disc(g, s.X, s.Y, s.Radius * 0.25 * (1 - k), Palette.Hex(0xffffff, a), Zoom);
            });
            Ripple(s.X, s.Y, s.Radius * 1.3, Palette.Hex(0xfff2d0));
            Ripple(s.X, s.Y, s.Radius, Palette.Hex(0xe8a13a));
            for (var i = 0; i < 6; i++)
            {
                var a = i * Math.Tau / 6;
                Boom(s.X + Math.Cos(a) * s.Radius * 0.55, s.Y + Math.Sin(a) * s.Radius * 0.55, s.Radius / 64, 1.6, 900);
            }
            Boom(s.X, s.Y, s.Radius * 2.4 / 64, 1.4, 1100);
            Shake(700, 0.012);
            return;
        }
        if (s.Kind == StrikeKind.Orbital)
        {
            Ripple(s.X, s.Y, s.Radius, Palette.Hex(0xe05a4a));
            Boom(s.X, s.Y, s.Radius * 2 / 64, 1.4, 700);
            Shake(250, 0.004);
            return;
        }
        Add(fxList, 600, (g, k) =>
        {
            var a = (float)(1 - EaseInCubic(k));
            Draw.Line(g, s.X, s.Y, s.X2, s.Y2, Palette.Hex(0xe8a13a, 0.7f * a), s.Radius * 2);
            Draw.Line(g, s.X, s.Y, s.X2, s.Y2, Palette.Hex(0xfff2d0, a), s.Radius * 0.8);
        });
    }

    /// <summary>
    /// A big ship out of the shipyard comes through a portal: two rings turning against each
    /// other, a flash, the hull growing out of it. A titan or a leviathan is greeted by a horn.
    /// </summary>
    void Portal(Entity e)
    {
        arrivals[e.Id] = Now;
        var (x, y, r, z) = (e.X, e.Y, e.Radius * 1.7, Zoom);
        var color = Palette.Of(e.Race);
        var light = Palette.Hex(0x9fd4ff);
        Add(fxList, 2000, (g, k) =>
        {
            var open = k < 0.3 ? EaseOutCubic(k / 0.3) : 1 - EaseInCubic((k - 0.3) / 0.7) * 0.6;
            var fade = (float)(k < 0.75 ? 1 : 1 - (k - 0.75) / 0.25);
            var spin = k * Math.PI * 3;
            Draw.Disc(g, x, y, r * open, color with { A = 0.18f * fade }, z);
            for (var i = 0; i < 3; i++)
            {
                var a0 = spin + i * Math.Tau / 3;
                Draw.Arc(g, x, y, r * open, a0, a0 + 1.4, 24, light with { A = 0.8f * fade }, 4 / z);
                var b0 = -spin * 1.6 + i * Math.Tau / 3;
                Draw.Arc(g, x, y, r * open * 0.72, b0, b0 + 1.1, 20, color with { A = 0.9f * fade }, 3 / z);
            }
        });
        Boom(x, y, r * 2.2 / 64, 1.6, 900, 0.7f);
        var big = e.Cls is ShipClass.Titan or ShipClass.Leviathan;
        var mine = e.Side == Opts.Player;
        if (mine || OnScreen(x, y)) Sfx.Play(Sound.Warp, 0, 0.8);
        if (!big) return;
        Shake(600, e.Cls == ShipClass.Leviathan ? 0.008 : 0.005);
        // The horn carries: one's own is always heard, an enemy's when in sight.
        if (mine || OnScreen(x, y)) Sfx.Play(Sound.Horn, 0, 1);
        if (mine) Hud.Toast($"{Ships.Defs[e.Cls].Name} en service");
        else if (CanSee(e)) Hud.Toast($"Un {Ships.Defs[e.Cls].Name} ennemi est apparu");
    }

    void Explode(int id, double x, double y, double radius)
    {
        DebrisAt(x, y, radius);
        if (views.TryGetValue(id, out var v) && v.Look.Destruction is string anim)
        {
            var frames = Assets.Anim(anim, false);
            var count = frames.GetFrameCount(DefaultAnim);
            var (rotation, scale) = (v.Root.Rotation, v.Scale);
            Add(fxList, count * 1000.0 / 12, (g, k) =>
            {
                var tex = frames.GetFrameTexture(DefaultAnim, Math.Min(count - 1, (int)(k * count)));
                Draw.Sprite(g, tex, x, y, rotation, scale, Colors.White);
            });
            return;
        }
        Boom(x, y, radius * 2.5 / 64, 1.8, 450 + radius * 4);
        if (radius >= Station.Radius) Shake(400, 0.006);
    }

    /// <summary>Fragments and a flash: on top of the destruction animation, so that a kill reads at a glance.</summary>
    void DebrisAt(double x, double y, double radius)
    {
        if (Zoom < LodSimple || !OnScreen(x, y)) return;
        debris.Emit(x, y, (int)Js.Round(Math.Min(24, Math.Max(4, radius / 3))));
        Boom(x, y, radius * 1.6 / 64, 1.3, 260, 0.8f);
    }

    void Spark(double x, double y)
    {
        if (sparkList.Count >= MaxSparks || Zoom < LodSimple) return;
        sparkList.Add((x, y, Now));
    }

    /// <summary>Each spark shrinks and fades out; the spent ones are dropped first, keeping the others in order.</summary>
    void PaintSparks(CanvasItem g)
    {
        var now = Now;
        var kept = 0;
        for (var i = 0; i < sparkList.Count; i++)
        {
            if (now - sparkList[i].Start < SparkMs) sparkList[kept++] = sparkList[i];
        }
        sparkList.RemoveRange(kept, sparkList.Count - kept);
        if (sparkList.Count == 0) return;
        var tex = Assets.Frame("ph-spark");
        foreach (var (x, y, start) in sparkList)
        {
            var k = Math.Max(0, (now - start) / SparkMs);
            Draw.Sprite(g, tex, x, y, 0, 1.5 - k, new Color(1, 1, 1, (float)(1 - k)));
        }
    }

    // Every frame: what abilities do, where they act; overlays; trails; fog.

    /// <summary>Zones, blows charging up and effects running, so that every ability shows what it does.</summary>
    /// <summary>
    /// Beams holding on their targets: a glow in the side's color around a white core, both
    /// throbbing, and a bright spot on the near side of the target, where the beam meets it.
    /// </summary>
    void DrawBeams(CanvasItem g, double z, double now)
    {
        foreach (var b in World.Beams)
        {
            if (World.Entities.Get(b.From) is not { } from || World.Entities.Get(b.To) is not { } to || (!CanSee(from) && !CanSee(to))) continue;
            var len = Math.Max(1, Math.Sqrt((from.X - to.X) * (from.X - to.X) + (from.Y - to.Y) * (from.Y - to.Y)));
            var (ex, ey) = (to.X + (from.X - to.X) / len * to.Radius * 0.7, to.Y + (from.Y - to.Y) / len * to.Radius * 0.7);
            var throb = (float)(0.7 + 0.3 * Math.Sin(now / 40 + b.From));
            var color = Palette.Of(World, b.Side);
            Draw.Line(g, from.X, from.Y, ex, ey, color with { A = 0.35f * throb }, Math.Max(2 / z, 6 * throb));
            Draw.Line(g, from.X, from.Y, ex, ey, new Color(1, 1, 1, 0.85f * throb), Math.Max(1 / z, 1.8));
            Draw.Disc(g, ex, ey, 3 + 3 * throb, color with { A = 0.7f }, z);
            Draw.Disc(g, ex, ey, 1.5 + 1.5 * throb, new Color(1, 1, 1, 0.9f), z);
        }
    }

    void DrawEffects(CanvasItem g)
    {
        var z = Zoom;
        var now = Now;
        // A shield that just took a hit lights up as a bubble.
        // Removing while walking a dictionary is allowed: no copy of it every frame.
        foreach (var (id, at) in shieldFlash)
        {
            var k = 1 - (now - at) / ShieldFlashMs;
            var e = World.Entities.Get(id);
            if (k <= 0 || e is null)
            {
                shieldFlash.Remove(id);
                continue;
            }
            if (!CanSee(e)) continue;
            Draw.Disc(g, e.X, e.Y, e.Radius + 5 / z, Palette.Hex(0x7cc4ff, 0.12f * (float)k), z);
            Draw.Ring(g, e.X, e.Y, e.Radius + 5 / z, Palette.Hex(0x9fd4ff, 0.7f * (float)k), 2 / z, z);
        }
        var pulse = 0.5 + 0.5 * Math.Sin(now / 160);
        foreach (var zone in World.Zones)
        {
            // An admiral's command shows only while the admiral is selected (see the overlay).
            if (zone.Kind == ZoneKind.Command) continue;
            var color = zone.Kind == ZoneKind.Dome ? 0x5aa9e6u : 0x6fd08cu;
            Draw.Disc(g, zone.X, zone.Y, zone.R, Palette.Hex(color, 0.07f), z);
            Draw.Ring(g, zone.X, zone.Y, zone.R, Palette.Hex(color, (float)(0.45 + 0.25 * pulse)), 2 / z, z);
        }
        foreach (var s in World.Strikes)
        {
            var k = 1 - s.Delay / s.Total;
            if (s.Kind == StrikeKind.Orbital)
            {
                Draw.Disc(g, s.X, s.Y, s.Radius, Palette.Hex(0xe05a4a, (float)(0.06 + 0.18 * k)), z);
                Draw.Ring(g, s.X, s.Y, s.Radius, Palette.Hex(0xe05a4a, 0.9f), 2 / z, z);
                Draw.Ring(g, s.X, s.Y, s.Radius * (1 - k), Palette.Hex(0xe05a4a, 0.9f), 2 / z, z);
            }
            else if (s.Kind == StrikeKind.Stellar)
            {
                // The star lance's mark: a white zone brightening, rings closing in, the seconds left.
                Draw.Disc(g, s.X, s.Y, s.Radius, Palette.Hex(0xfff2d0, (float)(0.05 + 0.2 * k)), z);
                Draw.Ring(g, s.X, s.Y, s.Radius, Palette.Hex(0xfff2d0, (float)(0.6 + 0.4 * pulse)), 3 / z, z);
                Draw.Ring(g, s.X, s.Y, s.Radius * (1 - k), Palette.Hex(0xe8a13a, 0.9f), 3 / z, z);
                Draw.Ring(g, s.X, s.Y, s.Radius * (1 - k) * 0.5, Palette.Hex(0xe8a13a, 0.6f), 2 / z, z);
                Draw.Text(g, UiTheme.Mono, $"{Math.Ceiling(s.Delay)}", s.X, s.Y + 10 / z, 28, Palette.Hex(0xfff2d0), z, Palette.Bg);
            }
            else Draw.Line(g, s.X, s.Y, s.X2, s.Y2, Palette.Hex(0xe8a13a, (float)(0.25 + 0.45 * pulse)), Math.Max(1.5 / z, s.Radius * 0.5 * k));
        }
        DrawBeams(g, z, now);
        var rect = ViewRect();
        foreach (var e in World.Entities)
        {
            if (!rect.Grow((float)e.Radius).HasPoint(new Vector2((float)e.X, (float)e.Y)) || !CanSee(e)) continue;
            var ring = 7 / z;
            foreach (var a in e.Abilities)
            {
                if (a.Active <= 0) continue;
                Draw.Ring(g, e.X, e.Y, e.Radius + ring, Palette.Hex(Abilities.Defs[a.Id].Color, (float)(0.4 + 0.5 * pulse)), 2 / z, z);
                ring += 4 / z;
            }
            // A star lance shows its charge to whoever sees it: an arc filling up, a pulsing ring once ready.
            if (e.Type == "starLance" && e.Build >= 1 && e.Abilities.Count > 0)
            {
                var charge = 1 - e.Abilities[0].Cooldown / Abilities.Defs[AbilityId.StarLance].Cooldown;
                var star = Palette.Hex(0xfff2d0);
                Draw.Ring(g, e.X, e.Y, e.Radius + 14 / z, Palette.Hex(0x2a3446, 0.8f), 4 / z, z);
                if (charge >= 1)
                {
                    Draw.Ring(g, e.X, e.Y, e.Radius + 14 / z, star with { A = (float)(0.5 + 0.5 * pulse) }, 4 / z, z);
                    // Ready: the lens burns.
                    Draw.Disc(g, e.X, e.Y, e.Radius * 0.3, star with { A = (float)(0.25 + 0.3 * pulse) }, z);
                }
                else Draw.Arc(g, e.X, e.Y, e.Radius + 14 / z, -Math.PI / 2, -Math.PI / 2 + charge * Math.Tau, 48, star with { A = 0.85f }, 4 / z);
            }
            var fxs = e.Effects;
            var r = e.Radius + 11 / z;
            if (fxs.Marked > 0)
            {
                var red = Palette.Hex(0xe05a4a, 0.9f);
                Draw.Ring(g, e.X, e.Y, r, red, 2 / z, z);
                for (var i = 0; i < 4; i++)
                {
                    var c = Math.Cos(i * Math.PI / 2);
                    var n = Math.Sin(i * Math.PI / 2);
                    Draw.Line(g, e.X + c * (r - 5 / z), e.Y + n * (r - 5 / z), e.X + c * (r + 7 / z), e.Y + n * (r + 7 / z), red, 2 / z);
                }
            }
            // Crackling: a ring that flickers between two sizes.
            if (fxs.Stunned > 0) Draw.Ring(g, e.X, e.Y, r + (pulse > 0.5 ? 3 : -2) / z, Palette.Hex(0x9fd4ff, (float)(0.5 + 0.5 * pulse)), 2 / z, z);
            if (fxs.Corroded > 0) Draw.Disc(g, e.X, e.Y, e.Radius, Palette.Hex(0x6fd08c, (float)(0.25 + 0.2 * pulse)), z);
            // Taunted: a red thread to the bulwark it cannot help shooting at.
            if (fxs.Taunted > 0 && World.Entities.Get(fxs.TauntedBy) is { } taunter)
                Draw.Line(g, e.X, e.Y, taunter.X, taunter.Y, Palette.Hex(0xe05a4a, (float)(0.15 + 0.15 * pulse)), 1.5 / z);
        }
    }

    /// <summary>Engine trails behind moving ships, under the ships themselves.</summary>
    void DrawTrails(CanvasItem g)
    {
        var z = Zoom;
        if (z < LodSimple) return;
        var rect = ViewRect();
        foreach (var e in World.Entities)
        {
            if (e.Kind != EntityKind.Ship || e.Speed < 20 || !rect.HasPoint(new Vector2((float)e.X, (float)e.Y)) || !CanSee(e)) continue;
            var c = Math.Cos(e.Angle);
            var n = Math.Sin(e.Angle);
            var sx = e.X - c * e.Radius * 0.7;
            var sy = e.Y - n * e.Radius * 0.7;
            var len = e.Speed * 0.45;
            var w = Math.Max(1.5 / z, e.Radius * 0.22);
            Draw.Line(g, sx, sy, sx - c * len * 0.5, sy - n * len * 0.5, Palette.Hex(0x9fd4ff, 0.3f), w);
            Draw.Line(g, sx - c * len * 0.5, sy - n * len * 0.5, sx - c * len, sy - n * len, Palette.Hex(0x9fd4ff, 0.12f), w * 0.6);
        }
    }

    void DrawOverlay(CanvasItem g)
    {
        var z = Zoom;
        var rect = ViewRect();
        var player = Opts.Player;
        var green = Palette.Hex(0x6fd08c);
        var red = Palette.Hex(0xe05a4a);
        var amber = Palette.Hex(0xe8a13a);
        // Green for the player's own, red for a foe, grey for an ally: neither to command nor to strike.
        Color Whose(Entity e) => e.Side == player ? green : Worlds.Foes(World, e.Side, player) ? red : Palette.Muted;
        DrawFlagships(g, rect, z);
        foreach (var e in World.Entities)
        {
            var r = e.Radius + 12 / z;
            if (e.X + r < rect.Position.X || e.X - r > rect.End.X || e.Y + r < rect.Position.Y || e.Y - r > rect.End.Y) continue;
            if (!CanSee(e)) continue;
            var sel = Selected.Contains(e.Id);
            if (e.Hero is HeroId hero)
            {
                Draw.Ring(g, e.X, e.Y, e.Radius + 9 / z, amber with { A = 0.8f }, 1.5 / z, z);
                if (z >= LodIcons || e.Size != SizeClass.Small) Draw.Text(g, UiTheme.Mono, Heroes.Defs[hero].Name, e.X, e.Y - e.Radius - 14 / z, 13, amber, z);
            }
            // Structures and station parts wear their side's ring; ship parts go without, their ship shows.
            var fixedPart = e.Kind == EntityKind.Structure || (e.Kind == EntityKind.Hardpoint && World.Entities.Get(e.ParentId)?.Kind != EntityKind.Ship);
            if (fixedPart) Draw.Ring(g, e.X, e.Y, e.Radius + 2 / z, Palette.Of(World, e.Side, 0.45f), 1.5 / z, z);
            if (sel) Draw.Ring(g, e.X, e.Y, e.Radius + 5 / z, Whose(e) with { A = 0.9f }, 2 / z, z);
            if (sel && e.Order is Order o)
            {
                IPoint? t = o.Type == OrderType.Attack ? World.Entities.Get(o.TargetId) : new Point(o.X, o.Y);
                var c = o.Type == OrderType.Move ? green : o.Type == OrderType.Attack ? red : amber;
                if (t is not null) Draw.Line(g, e.X, e.Y, t.X, t.Y, c with { A = 0.5f }, 1 / z);
            }
            var damaged = e.Hull < e.MaxHull || e.Shield < e.MaxShield;
            if (!sel && !damaged && e.Build >= 1 && !Settings.Current.AlwaysBars) continue;
            Bars(g, e, z);
            // With adapted colors, whose it is shows by a shape too, left of the bars; a part shows its ship's.
            if (Palette.Marked && e.Kind != EntityKind.Hardpoint)
            {
                var w = Math.Max(28 / z, e.Radius * 1.6);
                Draw.Mark(g, e.Side, e.X - w / 2 - 6 / z, e.Y - e.Radius - 6 / z, 4 / z, Palette.Of(World, e.Side), z);
            }
        }
        // Ring under the cursor: green for your units, red for a target, grey for an ally's.
        var ctl = Controls;
        var over = ctl.Mode is SelectMode && ctl.PilotId is null && ctl.Box is null && ctl.PointerOver ? ctl.Hovered() : null;
        if (over is not null && !Selected.Contains(over.Id) && CanSee(over))
            Draw.Ring(g, over.X, over.Y, over.Radius + 5 / z, Whose(over) with { A = 0.5f }, 1.5 / z, z);
        var piloted = World.Entities.Get(ctl.PilotId);
        if (piloted?.Pilot is Pilot p)
        {
            var t = Combat.AimedTarget(World, piloted, p, piloted.Range);
            Draw.Ring(g, piloted.X, piloted.Y, piloted.Radius + 6 / z, green with { A = 0.9f }, 2 / z, z);
            var cross = amber with { A = 0.9f };
            Draw.Ring(g, p.AimX, p.AimY, 10 / z, cross, 1.5 / z, z);
            Draw.Line(g, p.AimX - 16 / z, p.AimY, p.AimX - 6 / z, p.AimY, cross, 1.5 / z);
            Draw.Line(g, p.AimX + 6 / z, p.AimY, p.AimX + 16 / z, p.AimY, cross, 1.5 / z);
            Draw.Line(g, p.AimX, p.AimY - 16 / z, p.AimX, p.AimY - 6 / z, cross, 1.5 / z);
            Draw.Line(g, p.AimX, p.AimY + 6 / z, p.AimX, p.AimY + 16 / z, cross, 1.5 / z);
            if (t is not null) Draw.Ring(g, t.X, t.Y, t.Radius + 6 / z, red with { A = p.Firing ? 1 : 0.6f }, 2 / z, z);
        }
        if (ctl.Mode is AbilityMode am) DrawAiming(g, am.Id, z);
        // An admiral's command, around the selected admirals.
        foreach (var id in Selected)
        {
            var e = World.Entities.Get(id);
            if (e?.Hero is HeroId h && h.Role == HeroRole.Admiral) Draw.Ring(g, e.X, e.Y, Abilities.CommandRadius, amber with { A = 0.3f }, 1.5 / z, z);
        }
        var station = Worlds.StationOf(World, player);
        var ghost = ctl.PlacementGhost();
        if (ctl.Mode is PlaceMode pm && pm.Type != BuildableType.Mine)
        {
            foreach (var zone in Structures.BuildZones(World, player, pm.Type)) Draw.Ring(g, zone.X, zone.Y, zone.R, amber with { A = 0.35f }, 1.5 / z, z);
        }
        else if (station is not null && Selected.Contains(station.Id))
        {
            Draw.Ring(g, station.X, station.Y, Station.ConstructionRadius, amber with { A = 0.35f }, 1.5 / z, z);
        }
        if (station is not null && Selected.Contains(station.Id))
        {
            var f = World.Factions[player];
            Draw.Line(g, station.X, station.Y, f.RallyX, f.RallyY, green with { A = 0.6f }, 1 / z);
            Draw.Disc(g, f.RallyX, f.RallyY, 5 / z, green with { A = 0.8f }, z);
        }
        if (ghost is { } gh)
        {
            var c = gh.Ok ? green : red;
            Draw.Disc(g, gh.X, gh.Y, gh.Radius, c with { A = 0.2f }, z);
            Draw.Ring(g, gh.X, gh.Y, gh.Radius, c with { A = 0.9f }, 2 / z, z);
        }
        if (ctl.Box is Rect2 b)
        {
            var box = b.Abs();
            Draw.Rect(g, box.Position.X, box.Position.Y, box.Size.X, box.Size.Y, green with { A = 0.08f });
            Draw.Frame(g, box.Position.X, box.Position.Y, box.Size.X, box.Size.Y, green with { A = 0.8f }, 1 / z);
        }
    }

    /// <summary>Reach of the casters, and where the ability would land under the cursor.</summary>
    void DrawAiming(CanvasItem g, AbilityId id, double z)
    {
        var def = Abilities.Defs[id];
        var color = Palette.Hex(def.Color);
        var h = Controls.Hover;
        foreach (var s in Controls.OwnShips())
        {
            if (!s.Abilities.Exists(a => a.Id == id)) continue;
            if (def.Range is double range)
            {
                Draw.Ring(g, s.X, s.Y, range, color with { A = 0.35f }, 1.5 / z, z);
                if (id == AbilityId.Spinal)
                {
                    var a = Math.Atan2(h.Y - s.Y, h.X - s.X);
                    Draw.Line(g, s.X, s.Y, s.X + Math.Cos(a) * range, s.Y + Math.Sin(a) * range, color with { A = 0.15f }, 80);
                }
            }
        }
        if (def.Target == AbilityTarget.Point && def.Radius is double radius) Draw.Ring(g, h.X, h.Y, radius, color with { A = 0.8f }, 2 / z, z);
        if (def.Target == AbilityTarget.Enemy && Controls.Hovered() is { } t && Worlds.Foes(World, t.Side, Opts.Player))
            Draw.Ring(g, t.X, t.Y, t.Radius + 10 / z, color with { A = 0.9f }, 2 / z, z);
    }

    /// <summary>Hull bar (green to red), shield bar above it, construction progress below.</summary>
    static void Bars(CanvasItem g, Entity e, double z)
    {
        var w = Math.Max(28 / z, e.Radius * 1.6);
        var h = 4 / z;
        var x = e.X - w / 2;
        var y = e.Y - e.Radius - 8 / z;
        var hull = Math.Max(0, e.Hull / e.MaxHull);
        var back = Palette.Hex(0x0b0d12, 0.8f);
        Draw.Rect(g, x - 1 / z, y - 1 / z, w + 2 / z, h + 2 / z, back);
        Draw.Rect(g, x, y, w * hull, h, Palette.Hex(hull > 0.5 ? 0x6fd08cu : hull > 0.25 ? 0xe8c14au : 0xe05a4au));
        if (e.MaxShield > 0)
        {
            y -= h + 1 / z;
            Draw.Rect(g, x - 1 / z, y - 1 / z, w + 2 / z, h + 1 / z, back);
            Draw.Rect(g, x, y, w * (e.Shield / e.MaxShield), h, Palette.Hex(0x5aa9e6));
            // Knocked down: the bar shows the time before the shield starts to come back, in red.
            if (e.ShieldDown > 0) Draw.Rect(g, x, y, w * Math.Min(1, e.ShieldDown / Worlds.ShieldBreak(e.Size)), h, Palette.Hex(0xe05a4a, 0.8f));
        }
        if (e.Build < 1)
        {
            var by = e.Y + e.Radius + 6 / z;
            Draw.Rect(g, x, by, w, h, back);
            Draw.Rect(g, x, by, w * e.Build, h, Palette.Hex(0xe8a13a));
        }
    }

    /// <summary>Clear spots in the fog mask, where the player's ships and structures see.</summary>
    void DrawFogHoles(CanvasItem g)
    {
        var brush = Assets.Frame("fog-brush");
        foreach (var e in World.Entities)
        {
            // Allies' eyes clear the fog as the player's own do.
            if (!Worlds.Allies(World, e.Side, Opts.Player)) continue;
            var r = Vision.VisionOf(e);
            if (r <= 0) continue;
            var size = (float)(2 * r / FogCell);
            g.DrawTextureRect(brush, new Rect2((float)((e.X + Margin) / FogCell) - size / 2, (float)((e.Y + Margin) / FogCell) - size / 2, size, size), false);
        }
    }
}
