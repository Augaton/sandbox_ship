// Drawing helpers for the battle and galaxy views. Circles get as many segments as their size
// on screen needs (render/shapes.ts): a big ring stays round up close, a small one stays cheap.
// On a batched canvas, untextured shapes are gathered and sent to the renderer in one call.
using Godot;

namespace SandboxShip.Render;

public static class Draw
{
    /// <summary>The batch being filled, and the canvas it belongs to: shapes drawn on that canvas go into it.</summary>
    static ShapeBatch? batch;
    static CanvasItem? batchOn;

    static int Segments(double r, double zoom) => (int)Math.Clamp(Math.Round(r * zoom / 2.5), 12, 64);

    static ShapeBatch? BatchOf(CanvasItem g) => ReferenceEquals(batchOn, g) ? batch : null;

    /// <summary>From here on, untextured shapes drawn on `g` gather in `b` until End.</summary>
    internal static void Begin(CanvasItem g, ShapeBatch b)
    {
        batch = b;
        batchOn = g;
    }

    internal static void End(CanvasItem g)
    {
        Flush(g);
        batch = null;
        batchOn = null;
    }

    /// <summary>Sends the shapes gathered so far: anything drawn directly on `g` must come after them.</summary>
    public static void Flush(CanvasItem g) => BatchOf(g)?.Flush(g);

    public static void Ring(CanvasItem g, double x, double y, double r, Color color, double width, double zoom)
    {
        var n = Segments(r, zoom);
        if (BatchOf(g) is { } b) b.Arc(new Vector2((float)x, (float)y), (float)r, 0, Mathf.Tau, n + 1, color, (float)width, true);
        else g.DrawArc(new Vector2((float)x, (float)y), (float)r, 0, Mathf.Tau, n + 1, color, (float)width);
    }

    /// <summary>Part of a ring, from one angle to another, through `points` points.</summary>
    public static void Arc(CanvasItem g, double x, double y, double r, double from, double to, int points, Color color, double width)
    {
        if (BatchOf(g) is { } b) b.Arc(new Vector2((float)x, (float)y), (float)r, (float)from, (float)to, points, color, (float)width, false);
        else g.DrawArc(new Vector2((float)x, (float)y), (float)r, (float)from, (float)to, points, color, (float)width);
    }

    public static void Disc(CanvasItem g, double x, double y, double r, Color color, double zoom)
    {
        var n = Segments(r, zoom);
        Span<Vector2> pts = stackalloc Vector2[n];
        for (var i = 0; i < n; i++)
        {
            var a = i * Math.Tau / n;
            pts[i] = new Vector2((float)(x + Math.Cos(a) * r), (float)(y + Math.Sin(a) * r));
        }
        if (BatchOf(g) is { } b) b.Convex(pts, color);
        else g.DrawColoredPolygon(pts, color, ReadOnlySpan<Vector2>.Empty, null);
    }

    public static void Triangle(CanvasItem g, Vector2 a, Vector2 b, Vector2 c, Color color)
    {
        if (BatchOf(g) is { } batch) batch.Triangle(a, b, c, color);
        else g.DrawColoredPolygon([a, b, c], color);
    }

    /// <summary>
    /// A side's mark, `r` across its half, on a dark edge: the second cue after its color for those
    /// who tell colors apart badly. One shape per side, so two sides of the same race differ too:
    /// a triangle, a disc, a square, a diamond (the Errants), a cross, a triangle upside down, a hexagon.
    /// </summary>
    public static void Mark(CanvasItem g, Side side, double x, double y, double r, Color color, double zoom)
    {
        Shape(g, side.Id, x, y, r + 1.2 / zoom, Core.Palette.Bg, zoom);
        Shape(g, side.Id, x, y, r, color, zoom);
    }

    static void Shape(CanvasItem g, int id, double x, double y, double r, Color color, double zoom)
    {
        Vector2 At(double dx, double dy) => new((float)(x + dx), (float)(y + dy));
        switch (Math.Abs(id) % 7)
        {
            case 0:
                Triangle(g, At(0, -r), At(r * 0.95, r * 0.75), At(-r * 0.95, r * 0.75), color);
                break;
            case 1:
                Disc(g, x, y, r * 0.85, color, zoom);
                break;
            case 2:
                Rect(g, x - r * 0.75, y - r * 0.75, r * 1.5, r * 1.5, color);
                break;
            case 3:
                Triangle(g, At(0, -r), At(r * 0.8, 0), At(0, r), color);
                Triangle(g, At(0, -r), At(0, r), At(-r * 0.8, 0), color);
                break;
            case 4:
                Rect(g, x - r, y - r * 0.32, r * 2, r * 0.64, color);
                Rect(g, x - r * 0.32, y - r, r * 0.64, r * 2, color);
                break;
            case 5:
                Triangle(g, At(0, r), At(-r * 0.95, -r * 0.75), At(r * 0.95, -r * 0.75), color);
                break;
            default:
                for (var i = 0; i < 6; i++)
                {
                    var (a, b) = (i * Math.Tau / 6, (i + 1) * Math.Tau / 6);
                    Triangle(g, At(0, 0), At(Math.Cos(a) * r, Math.Sin(a) * r), At(Math.Cos(b) * r, Math.Sin(b) * r), color);
                }
                break;
        }
    }

    public static void Line(CanvasItem g, double x0, double y0, double x1, double y1, Color color, double width)
    {
        if (BatchOf(g) is { } b) b.Line(new Vector2((float)x0, (float)y0), new Vector2((float)x1, (float)y1), color, (float)width);
        else g.DrawLine(new Vector2((float)x0, (float)y0), new Vector2((float)x1, (float)y1), color, (float)width);
    }

    public static void Rect(CanvasItem g, double x, double y, double w, double h, Color color)
    {
        if (BatchOf(g) is { } b) b.Rect(new Rect2((float)x, (float)y, (float)w, (float)h), color);
        else g.DrawRect(new Rect2((float)x, (float)y, (float)w, (float)h), color);
    }

    public static void Frame(CanvasItem g, double x, double y, double w, double h, Color color, double width)
    {
        Flush(g);
        g.DrawRect(new Rect2((float)x, (float)y, (float)w, (float)h), color, false, (float)width);
    }

    /// <summary>Text centered on (x, y) at its bottom, `size` pixels high on screen whatever the zoom.</summary>
    public static void Text(CanvasItem g, Font font, string text, double x, double y, int size, Color color, double zoom, Color? outline = null)
    {
        Flush(g);
        var s = 1 / (float)zoom;
        var w = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        g.DrawSetTransform(new Vector2((float)x, (float)y), 0, new Vector2(s, s));
        var at = new Vector2(-w / 2, -font.GetDescent(size));
        if (outline is Color o) g.DrawStringOutline(font, at, text, HorizontalAlignment.Left, -1, size, 4, o);
        g.DrawString(font, at, text, HorizontalAlignment.Left, -1, size, color);
        g.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    /// <summary>A texture centered on (x, y), turned and scaled.</summary>
    public static void Sprite(CanvasItem g, Texture2D tex, double x, double y, double rotation, double scale, Color modulate)
    {
        Flush(g);
        var size = tex.GetSize();
        g.DrawSetTransform(new Vector2((float)x, (float)y), (float)rotation, new Vector2((float)scale, (float)scale));
        g.DrawTexture(tex, -size / 2, modulate);
        g.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }
}

/// <summary>
/// Untextured shapes gathered into one list of triangles, sent with a single call: a frame's
/// thousands of rings, bars and strokes would otherwise cost a call each, and a ring or a disc
/// its own vertex buffer. The shapes are those Godot's draw_arc, draw_line, draw_rect and
/// draw_colored_polygon make, in the order they were drawn, so the picture is the same.
/// </summary>
public sealed class ShapeBatch
{
    Vector2[] points = new Vector2[4096];
    Color[] colors = new Color[4096];
    int[] indices = new int[6144];
    int pointCount, indexCount;

    /// <summary>Room for `p` more points and `i` more indices; returns the first new point's index.</summary>
    int Reserve(int p, int i)
    {
        if (pointCount + p > points.Length)
        {
            var size = Math.Max(points.Length * 2, pointCount + p);
            Array.Resize(ref points, size);
            Array.Resize(ref colors, size);
        }
        if (indexCount + i > indices.Length) Array.Resize(ref indices, Math.Max(indices.Length * 2, indexCount + i));
        return pointCount;
    }

    void Point(Vector2 p, Color color)
    {
        points[pointCount] = p;
        colors[pointCount++] = color;
    }

    void Tri(int a, int b, int c)
    {
        indices[indexCount++] = a;
        indices[indexCount++] = b;
        indices[indexCount++] = c;
    }

    /// <summary>A quad given corner after corner, as two triangles.</summary>
    void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
    {
        var i = Reserve(4, 6);
        Point(a, color);
        Point(b, color);
        Point(c, color);
        Point(d, color);
        Tri(i, i + 1, i + 2);
        Tri(i, i + 2, i + 3);
    }

    public void Triangle(Vector2 a, Vector2 b, Vector2 c, Color color)
    {
        var i = Reserve(3, 3);
        Point(a, color);
        Point(b, color);
        Point(c, color);
        Tri(i, i + 1, i + 2);
    }

    /// <summary>A convex polygon, as a fan from its first corner.</summary>
    public void Convex(ReadOnlySpan<Vector2> polygon, Color color)
    {
        var n = polygon.Length;
        if (n < 3) return;
        var i = Reserve(n, (n - 2) * 3);
        foreach (var p in polygon) Point(p, color);
        for (var k = 1; k < n - 1; k++) Tri(i, i + k, i + k + 1);
    }

    /// <summary>A stroke `width` wide, as draw_line makes it.</summary>
    public void Line(Vector2 from, Vector2 to, Color color, float width)
    {
        var t = (from - to).Orthogonal().Normalized() * width * 0.5f;
        Quad(from + t, to + t, to - t, from - t, color);
    }

    public void Rect(Rect2 r, Color color)
    {
        r = r.Abs();
        Quad(r.Position, new Vector2(r.End.X, r.Position.Y), r.End, new Vector2(r.Position.X, r.End.Y), color);
    }

    /// <summary>
    /// The stroke draw_arc makes through `count` points from angle `from` to `to`: the corners
    /// mitred so that it keeps its width. A full ring is closed without a seam.
    /// </summary>
    public void Arc(Vector2 center, float radius, float from, float to, int count, Color color, float width, bool closed)
    {
        if (count < 2) return;
        var delta = Math.Clamp(to - from, -Mathf.Tau, Mathf.Tau);
        Vector2 Dir(int k)
        {
            var theta = k / (count - 1f) * delta + from;
            return new Vector2(Mathf.Cos(theta), Mathf.Sin(theta));
        }
        var half = width * 0.5f;
        // Across a corner the stroke widens by 1 / cos(half the turn), to keep its width.
        var miter = half / Mathf.Cos(delta / (count - 1) / 2);
        var i = Reserve(count * 2, (count - 1) * 6);
        for (var k = 0; k < count; k++)
        {
            var dir = Dir(k);
            var p = center + dir * radius;
            var offset = dir * miter;
            if (!closed && (k == 0 || k == count - 1))
            {
                // Open ends are cut square across their segment, turned outwards like the corners.
                var other = center + Dir(k == 0 ? 1 : k - 1) * radius;
                offset = (other - p).Normalized().Orthogonal() * half;
                if (offset.Dot(dir) < 0) offset = -offset;
            }
            Point(p + offset, color);
            Point(p - offset, color);
        }
        for (var k = 0; k < count - 1; k++)
        {
            var a = i + k * 2;
            Tri(a, a + 1, a + 2);
            Tri(a + 1, a + 3, a + 2);
        }
    }

    /// <summary>Sends what was gathered to `g`, then starts empty.</summary>
    public void Flush(CanvasItem g)
    {
        if (indexCount == 0) return;
        RenderingServer.CanvasItemAddTriangleArray(g.GetCanvasItem(), new ReadOnlySpan<int>(indices, 0, indexCount),
            new ReadOnlySpan<Vector2>(points, 0, pointCount), new ReadOnlySpan<Color>(colors, 0, pointCount),
            ReadOnlySpan<Vector2>.Empty, ReadOnlySpan<int>.Empty, ReadOnlySpan<float>.Empty, default, -1);
        pointCount = 0;
        indexCount = 0;
    }
}

/// <summary>A node that draws whatever its callback draws, every frame it is asked to.</summary>
public partial class DrawCanvas : Node2D
{
    public Action<DrawCanvas>? OnDraw;
    /// <summary>Set: its untextured shapes go to the renderer in one batch (see ShapeBatch).</summary>
    public ShapeBatch? Batch;

    public override void _Draw()
    {
        if (Batch is null)
        {
            OnDraw?.Invoke(this);
            return;
        }
        Render.Draw.Begin(this, Batch);
        OnDraw?.Invoke(this);
        Render.Draw.End(this);
    }
}
