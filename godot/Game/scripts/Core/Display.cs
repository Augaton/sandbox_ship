// The display settings at work: the window's mode and size, the scale of what is drawn, the
// vertical sync and the frame cap, applied at the start and whenever an option changes.
using Godot;

namespace SandboxShip.Core;

/// <summary>A framed window, the whole screen to itself, or a borderless window covering the screen.</summary>
public enum DisplayMode { Windowed, Fullscreen, Borderless }

public static class Display
{
    /// <summary>The frame caps offered; 0 for none.</summary>
    public static readonly int[] FpsLimits = [30, 60, 120, 0];
    /// <summary>The scales offered for what is drawn: the interface, and the view with it.</summary>
    public static readonly double[] Scales = [0.75, 0.9, 1, 1.1, 1.25, 1.5];
    /// <summary>The framed window's sizes offered, those that fit on the screen.</summary>
    static readonly Vector2I[] AllSizes = [new(1280, 720), new(1280, 800), new(1440, 900), new(1600, 900), new(1920, 1080), new(2560, 1440)];

    /// <summary>Frames a second while another window has the focus, at most: the game plays on, the machine is busy elsewhere.</summary>
    const int UnfocusedFps = 30;

    static bool focused = true;

    /// <summary>Whether the settings apply: not to a server without a window, nor to a capture, which keeps the default view.</summary>
    static bool Live => !Dev.Service && Dev.Shot is null;

    public static IReadOnlyList<Vector2I> Sizes()
    {
        var screen = DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen()).Size;
        var fit = AllSizes.Where(s => s.X <= screen.X && s.Y <= screen.Y).ToList();
        return fit.Count > 0 ? fit : [AllSizes[0]];
    }

    /// <summary>Everything, as the game starts.</summary>
    public static void Apply()
    {
        Window();
        Scale();
        Sync();
        Fps();
    }

    /// <summary>The window's mode, and its size when framed, centered on its screen.</summary>
    public static void Window()
    {
        if (!Live) return;
        var s = Settings.Current;
        var mode = s.Display switch
        {
            DisplayMode.Fullscreen => DisplayServer.WindowMode.ExclusiveFullscreen,
            DisplayMode.Borderless => DisplayServer.WindowMode.Fullscreen,
            _ => DisplayServer.WindowMode.Windowed,
        };
        if (DisplayServer.WindowGetMode() != mode) DisplayServer.WindowSetMode(mode);
        if (mode != DisplayServer.WindowMode.Windowed) return;
        var size = new Vector2I(s.WindowWidth, s.WindowHeight);
        if (DisplayServer.WindowGetSize() == size) return;
        DisplayServer.WindowSetSize(size);
        var screen = DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen());
        DisplayServer.WindowSetPosition(screen.Position + (screen.Size - size) / 2);
    }

    /// <summary>
    /// The scale of everything drawn in the window: the interface, and the battle and the galaxy
    /// with it (the wheel zooms them back out).
    /// </summary>
    public static void Scale()
    {
        if (!Live) return;
        ((SceneTree)Engine.GetMainLoop()).Root.ContentScaleFactor = (float)Math.Clamp(Settings.Current.UiScale, 0.5, 2);
    }

    public static void Sync()
    {
        if (!Live) return;
        DisplayServer.WindowSetVsyncMode(Settings.Current.VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
    }

    /// <summary>The cap chosen; behind other windows, the lower of it and UnfocusedFps.</summary>
    public static void Fps()
    {
        if (!Live) return;
        var cap = Math.Max(0, Settings.Current.MaxFps);
        Engine.MaxFps = focused ? cap : cap == 0 ? UnfocusedFps : Math.Min(cap, UnfocusedFps);
    }

    /// <summary>The window took or lost the focus.</summary>
    public static void Focus(bool on)
    {
        focused = on;
        Fps();
    }
}
