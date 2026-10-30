// Parallax layers behind the battle, on their own canvas layer so that zoom leaves them
// alone. Port of src/scenes/BackgroundScene.ts.
using Godot;
using SandboxShip.Render;

namespace SandboxShip.Battle;

public partial class Backdrop : CanvasLayer
{
    /// <summary>On-screen size of the backdrop planet, in pixels.</summary>
    const double PlanetSize = 190;

    readonly List<(Sprite2D Sprite, double Factor, float TileScale)> layers = [];
    Sprite2D? decor;
    Node2D planet = null!;

    public override void _Ready()
    {
        Layer = -1;
        foreach (var (key, factor) in Assets.BackdropTiles)
        {
            var tex = Assets.Frame(key);
            // Small pixel-art layers are doubled so that their stars do not look like dust.
            var scale = tex.GetWidth() < 512 ? 2f : 1f;
            var s = new Sprite2D
            {
                Texture = tex, Centered = false, RegionEnabled = true, TextureRepeat = CanvasItem.TextureRepeatEnum.Enabled,
                Scale = new Vector2(scale, scale),
            };
            AddChild(s);
            layers.Add((s, factor, scale));
        }
        if (Assets.BackdropDecor is string d)
        {
            decor = new Sprite2D { Texture = Assets.Frame(d), Scale = new Vector2(0.6f, 0.6f), Modulate = new Color(1, 1, 1, 0.45f) };
            AddChild(decor);
        }
        var key2 = Assets.Planet;
        var size = (float)(PlanetSize / Assets.OpaqueSize(key2));
        if (Assets.Frames(key2) > 1)
        {
            var a = new AnimatedSprite2D { SpriteFrames = Assets.Anim(key2, true, 8), Scale = new Vector2(size, size) };
            a.Play();
            planet = a;
        }
        else planet = new Sprite2D { Texture = Assets.Frame(key2), Scale = new Vector2(size, size) };
        AddChild(planet);
    }

    /// <summary>Follows the battle camera (its top-left corner), each layer moving less than the one in front of it.</summary>
    public void Sync(Vector2 scroll)
    {
        var size = GetViewport().GetVisibleRect().Size;
        foreach (var (s, factor, scale) in layers)
        {
            s.RegionRect = new Rect2(scroll * (float)factor / scale, size / scale);
        }
        decor?.SetPosition(new Vector2(size.X * 0.25f, size.Y * 0.6f) - scroll * 0.04f);
        planet.Position = new Vector2(size.X * 0.7f, size.Y * 0.3f) - scroll * 0.08f;
    }
}
