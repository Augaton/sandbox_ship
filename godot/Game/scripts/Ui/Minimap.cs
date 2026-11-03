// The battle's minimap: asteroid fields, units and structures the player sees (structures
// once seen stay), the view rectangle, and recent alerts pinging where they happened. Left
// click or drag: look there; right click: send the selection there (Ctrl: attack-move). Port
// of the minimap of src/ui/hud.ts.
using Godot;
using SandboxShip.Battle;
using SandboxShip.Core;
using SandboxShip.Render;
using SandboxShip.Sim;
using SandboxShip.Sim.Data;

namespace SandboxShip.Ui;

public partial class Minimap : Control
{
    /// <summary>How long an alert pings on the minimap.</summary>
    const double PingMs = 4000;

    readonly BattleScreen scene = null!;
    /// <summary>The units' squares, a thousand or more in a big battle, go to the renderer in one call.</summary>
    readonly ShapeBatch squares = new();

    public Minimap() { }

    public Minimap(BattleScreen scene)
    {
        this.scene = scene;
        CustomMinimumSize = new Vector2(240, 160);
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.Cross;
        ClipContents = true;
    }

    Vector2 ToWorld(Vector2 at) => new(at.X / Size.X * (float)scene.World.Width, at.Y / Size.Y * (float)scene.World.Height);

    public override void _GuiInput(InputEvent ev)
    {
        switch (ev)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } mb:
            {
                var p = ToWorld(mb.Position);
                scene.Controls.MoveSelection(p.X, p.Y, mb.CtrlPressed);
                AcceptEvent();
                break;
            }
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb:
                Jump(mb.Position);
                AcceptEvent();
                break;
            // A press keeps the pointer: the drag follows it out of the minimap too.
            case InputEventMouseMotion mm when mm.ButtonMask.HasFlag(MouseButtonMask.Left):
                Jump(mm.Position);
                AcceptEvent();
                break;
        }
    }

    void Jump(Vector2 at)
    {
        var p = ToWorld(at);
        scene.CenterOn(p.X, p.Y);
    }

    public override void _Draw()
    {
        var t = StressProbe.Start();
        var w = scene.World;
        var player = scene.Opts.Player;
        var sx = Size.X / w.Width;
        var sy = Size.Y / w.Height;
        DrawRect(new Rect2(Vector2.Zero, Size), Palette.Bg);
        foreach (var f in w.Fields)
        {
            DrawCircle(new Vector2((float)(f.X * sx), (float)(f.Y * sy)), (float)Math.Max(2, f.Radius * sx), Palette.Line);
        }
        BattleScreen.DrawTerrainMap(this, w.Terrain, sx, sy);
        foreach (var e in w.Entities)
        {
            if (e.Kind == EntityKind.Hardpoint) continue;
            if (!Vision.IsVisible(w, player, e) && !(e.Kind == EntityKind.Structure && Vision.WasSeen(w, player, e))) continue;
            var s = e.Kind == EntityKind.Ship ? (e.Size == SizeClass.Small ? 1.5 : 2.5) : e.Type == "station" ? 7 : 4;
            // With adapted colors, stations and buildings wear their side's shape; ships, a pixel or two, stay dots.
            if (Palette.Marked && e.Kind == EntityKind.Structure) Render.Draw.Mark(this, e.Side, e.X * sx, e.Y * sy, s * 0.7 + 0.5, Palette.Of(w, e.Side), 1);
            else squares.Rect(new Rect2((float)(e.X * sx - s / 2), (float)(e.Y * sy - s / 2), (float)s, (float)s), Palette.Of(w, e.Side));
        }
        squares.Flush(this);
        var v = scene.ViewRect();
        DrawRect(new Rect2(v.Position.X * (float)sx, v.Position.Y * (float)sy, v.Size.X * (float)sx, v.Size.Y * (float)sy), Palette.Text, false, 1);
        // The raid site is known from the start: a slow gold pulse where the colossus waits.
        var now = BattleScreen.Now;
        foreach (var e in scene.World.Entities)
        {
            if (e.Type != "colossus") continue;
            var pulse = (float)(0.5 + 0.5 * Math.Sin(now / 400));
            var at = new Vector2((float)(e.GuardX * sx), (float)(e.GuardY * sy));
            DrawArc(at, 6 + 2 * pulse, 0, Mathf.Tau, 24, Palette.Of(w, e.Side, 0.5f + 0.4f * pulse), 2);
            DrawCircle(at, 2, Palette.Of(w, e.Side));
        }
        scene.DrawModeMarks(this, sx, sy);
        // Recent alerts ping where they happened.
        foreach (var a in scene.Alerts)
        {
            var age = (now - a.Time) / PingMs;
            if (age >= 1) continue;
            DrawArc(new Vector2((float)(a.X * sx), (float)(a.Y * sy)), (float)(4 + age * 18), 0, Mathf.Tau, 33, Palette.Danger with { A = (float)(1 - age) }, 2);
        }
        StressProbe.Stop("minimap", t);
    }
}
