// Building blocks of the battle HUD, drawn as the web HUD's stylesheet draws them
// (src/ui/hud.css): panels, labels, buttons with their states, and lists that update their
// nodes in place. Also the small text helpers of src/ui/dom.ts.
using System.Globalization;
using Godot;
using SandboxShip.Core;
using SandboxShip.Sim;

namespace SandboxShip.Ui;

/// <summary>
/// One entry of a list redrawn often (the selection panel, the quick bar, the tooltip): made
/// once, then updated in place while the entry at its place keeps the same key, so that its
/// buttons stay clickable and their tooltips stay open.
/// </summary>
public sealed record Part(string Key, Func<Control> Make, Action<Control>? Update = null);

public static class HudKit
{
    public static readonly Font Bold = new SystemFont
    {
        FontNames = ["DejaVu Sans Mono", "Liberation Mono", "Menlo", "Consolas", "monospace"],
        FontWeight = 700,
        Antialiasing = TextServer.FontAntialiasing.Gray,
    };

    static readonly Dictionary<Color, (StyleBoxFlat Normal, StyleBoxFlat Hover)> outlines = [];
    static readonly StyleBoxEmpty NoBox = new();

    /// <summary>A number as JavaScript writes it: "0.5", never "0,5".</summary>
    public static string N(double v) => v.ToString(CultureInfo.InvariantCulture);

    /// <summary>A share for reading, French style: "42 %", the space unbreakable.</summary>
    public static string Percent(double v) => $"{N(Js.Round(Math.Clamp(v, 0, 1) * 100))} %";

    /// <summary>Build buttons are narrow: "Station niveau 2 requise" becomes "Niveau 2".</summary>
    public static string ShortReason(string reason)
    {
        if (reason == "Crédits insuffisants") return "Trop cher";
        return System.Text.RegularExpressions.Regex.Replace(reason, @"^Station niveau (\d) requise$", "Niveau $1");
    }

    /// <summary>Adds a child and returns it, for building trees in one line.</summary>
    public static T Add<T>(this Node parent, T child) where T : Node
    {
        parent.AddChild(child);
        return child;
    }

    public static T Ignore<T>(T c) where T : Control
    {
        c.MouseFilter = Control.MouseFilterEnum.Ignore;
        return c;
    }

    /// <summary>A label, in the HUD's font; `wrap` wraps it to the width its container gives it.</summary>
    public static Label Text(string text, Color? color = null, int size = UiTheme.FontSize, bool bold = false, bool wrap = false)
    {
        var l = UiTheme.Text(text, color, size);
        if (bold) l.AddThemeFontOverride("font", Bold);
        if (wrap) l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return l;
    }

    /// <summary>A paragraph as wide as its text, up to `max` pixels, wrapped past that.</summary>
    public static Label Para(string text, Color? color = null, int size = UiTheme.FontSize, int max = 512)
    {
        var l = Text(text, color, size, wrap: true);
        var width = UiTheme.Mono.GetStringSize(text, HorizontalAlignment.Left, -1, size).X + 2;
        l.CustomMinimumSize = new Vector2(Math.Min(max, width), 0);
        return l;
    }

    /// <summary>Changes a label's color, only when it differs: a theme change lays the label out again.</summary>
    public static void Paint(Label l, Color c)
    {
        if (l.GetThemeColor("font_color") != c) l.AddThemeColorOverride("font_color", c);
    }

    public static HBoxContainer Row(int gap) => Gap(new HBoxContainer(), gap);

    public static VBoxContainer Column(int gap) => Gap(new VBoxContainer(), gap);

    public static HFlowContainer Flow(int gap)
    {
        var f = Ignore(new HFlowContainer());
        f.AddThemeConstantOverride("h_separation", gap);
        f.AddThemeConstantOverride("v_separation", gap);
        return f;
    }

    static T Gap<T>(T box, int gap) where T : BoxContainer
    {
        box.MouseFilter = Control.MouseFilterEnum.Ignore;
        box.AddThemeConstantOverride("separation", gap);
        return box;
    }

    /// <summary>A thin amber bar along the bottom of its parent, as long as the progress set with Fill.</summary>
    public static ColorRect ProgressBar()
    {
        var bar = Ignore(new ColorRect { Color = Palette.Accent });
        bar.SetAnchor(Godot.Side.Top, 1, true);
        bar.SetAnchor(Godot.Side.Bottom, 1, true);
        bar.SetAnchor(Godot.Side.Right, 0, true);
        bar.OffsetLeft = bar.OffsetRight = bar.OffsetBottom = 0;
        bar.OffsetTop = -3;
        return bar;
    }

    /// <summary>
    /// A progress bar's length, 0 to 1. Its offsets must stay as they are: an anchor moved
    /// alone keeps the control where it was, and the bar would never grow.
    /// </summary>
    public static void Fill(ColorRect bar, double done) => bar.SetAnchor(Godot.Side.Right, (float)Math.Clamp(done, 0, 1), true);

    /// <summary>Fades a control in that was just put on screen: a dialog appears rather than pops.</summary>
    public static void FadeIn(Control c, double seconds = 0.14)
    {
        c.Modulate = Colors.Transparent;
        c.CreateTween().TweenProperty(c, "modulate:a", 1.0, seconds);
    }

    /// <summary>A HUD panel: it takes the clicks over it, the battlefield gets the rest.</summary>
    public static PanelContainer Panel(int padX = 10, int padY = 10, Color? border = null)
    {
        var p = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        var s = UiTheme.Box(Palette.Panel, border ?? Palette.Line);
        s.ContentMarginLeft = s.ContentMarginRight = padX;
        s.ContentMarginTop = s.ContentMarginBottom = padY;
        p.AddThemeStyleboxOverride("panel", s);
        return p;
    }

    /// <summary>A HUD button: 40 pixels high as in the web HUD, never focused (Space and Enter stay the game's).</summary>
    public static Button Button(string text, Action? onPress = null, string tip = "", int height = 40)
    {
        var b = new Button { Text = text, TooltipText = tip, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, height) };
        if (onPress is not null) b.Pressed += onPress;
        return b;
    }

    /// <summary>Runs `act` when the button is pressed (the click sound comes from Sfx, for every button).</summary>
    public static void OnPress(BaseButton b, Action act) => b.Pressed += act;

    /// <summary>The main action of a dialog: amber, dark bold text.</summary>
    public static Button Primary(Button b)
    {
        b.AddThemeStyleboxOverride("normal", UiTheme.Box(Palette.Accent, Palette.Accent));
        b.AddThemeStyleboxOverride("hover", UiTheme.Box(Palette.Accent.Lightened(0.12f), Palette.Accent));
        b.AddThemeStyleboxOverride("pressed", UiTheme.Box(Palette.Accent.Darkened(0.12f), Palette.Accent));
        foreach (var key in new[] { "font_color", "font_hover_color", "font_pressed_color" }) b.AddThemeColorOverride(key, Palette.Bg);
        b.AddThemeFontOverride("font", Bold);
        return b;
    }

    /// <summary>
    /// A colored border, and text of the same color if `text`: a pressed toggle (amber), a
    /// selected tab, a looping production (green). Null takes it off.
    /// </summary>
    public static void Outline(Button b, Color? color, bool text = true)
    {
        var key = color is Color k ? k.ToHtml() + text : "";
        if (b.GetMeta("outline", "").AsString() == key) return;
        b.SetMeta("outline", key);
        if (color is not Color c)
        {
            foreach (var s in new[] { "normal", "hover" }) b.RemoveThemeStyleboxOverride(s);
            foreach (var s in new[] { "font_color", "font_hover_color" }) b.RemoveThemeColorOverride(s);
            return;
        }
        if (!outlines.TryGetValue(c, out var boxes))
        {
            boxes = (UiTheme.Box(Palette.Raised, c), UiTheme.Box(Palette.Raised.Lightened(0.08f), c));
            outlines[c] = boxes;
        }
        b.AddThemeStyleboxOverride("normal", boxes.Normal);
        b.AddThemeStyleboxOverride("hover", boxes.Hover);
        if (!text) return;
        b.AddThemeColorOverride("font_color", c);
        b.AddThemeColorOverride("font_hover_color", c);
    }

    /// <summary>A check box in the HUD's colors, never focused.</summary>
    public static CheckBox Check(string text, bool on, Action<bool> toggled, string tip = "", int size = UiTheme.FontSize, Color? color = null)
    {
        var c = new CheckBox { Text = text, ButtonPressed = on, TooltipText = tip, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 40) };
        if (size != UiTheme.FontSize) c.AddThemeFontSizeOverride("font_size", size);
        // The theme draws buttons, and a check box would take their box and their amber pressed state.
        foreach (var key in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled", "focus" }) c.AddThemeStyleboxOverride(key, NoBox);
        foreach (var key in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color" }) c.AddThemeColorOverride(key, color ?? Palette.Text);
        c.AddThemeIconOverride("unchecked", Unchecked);
        c.AddThemeIconOverride("checked", Checked);
        c.Toggled += v => toggled(v);
        return c;
    }

    static readonly Texture2D Unchecked = CheckIcon(false), Checked = CheckIcon(true);

    /// <summary>A check box's box, seen on a dark panel: grey outline when off, amber with a dark tick when on.</summary>
    static Texture2D CheckIcon(bool on)
    {
        const int n = 18;
        var img = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        img.Fill(on ? Palette.Accent : Palette.Muted);
        img.FillRect(new Rect2I(1, 1, n - 2, n - 2), on ? Palette.Accent : Palette.Raised);
        if (on)
        {
            // The tick: down from (4, 9) to (7, 12), then up to (13, 6), two pixels thick.
            for (var i = 0; i <= 3; i++) img.FillRect(new Rect2I(4 + i, 9 + i, 2, 2), Palette.Bg);
            for (var i = 0; i <= 6; i++) img.FillRect(new Rect2I(7 + i, 12 - i, 2, 2), Palette.Bg);
        }
        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>A scrolled area as tall as its content, up to `max` pixels.</summary>
    public static ScrollContainer Scroll(Control content)
    {
        var s = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = false };
        // A scrolled child keeps its minimum width unless it expands.
        content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        s.AddChild(content);
        return s;
    }

    public static void Fit(ScrollContainer s, double max)
    {
        if (s.GetChildCount() == 0) return;
        var h = Math.Min(s.GetChild<Control>(0).GetCombinedMinimumSize().Y, (float)Math.Max(0, max));
        if (s.CustomMinimumSize.Y != h) s.CustomMinimumSize = new Vector2(0, h);
    }

    /// <summary>Brings `box`'s children in line with `parts`: kept and updated while the keys match, made anew from the first that differs.</summary>
    public static void Sync(Container box, IReadOnlyList<Part> parts)
    {
        var kids = box.GetChildren();
        var i = 0;
        for (; i < parts.Count && i < kids.Count; i++)
        {
            if (kids[i].GetMeta("part", "").AsString() != parts[i].Key) break;
            parts[i].Update?.Invoke((Control)kids[i]);
        }
        for (var k = i; k < kids.Count; k++)
        {
            box.RemoveChild(kids[k]);
            kids[k].QueueFree();
        }
        for (; i < parts.Count; i++)
        {
            var c = parts[i].Make();
            c.SetMeta("part", parts[i].Key);
            box.AddChild(c);
            parts[i].Update?.Invoke(c);
        }
    }

    /// <summary>A line of text, wrapped unless `wrap` is off; lines of another look do not share nodes.</summary>
    public static Part TextPart(string text, Color? color = null, int size = UiTheme.FontSize, bool bold = false, bool wrap = true)
    {
        var c = color ?? Palette.Text;
        return new Part($"text:{c.ToHtml()}:{size}:{bold}:{wrap}", () => Text("", c, size, bold, wrap), n => ((Label)n).Text = text);
    }

    /// <summary>Spans side by side on one line: (text, color, bold).</summary>
    public static Part SpansPart(int size, params (string Text, Color Color, bool Bold)[] spans)
    {
        var key = "spans:" + size + string.Concat(spans.Select(s => $":{s.Color.ToHtml()}{s.Bold}"));
        return new Part(key, () =>
        {
            var row = Row(size * 3 / 5);
            foreach (var s in spans) row.AddChild(Text("", s.Color, size, s.Bold));
            return row;
        }, n =>
        {
            for (var i = 0; i < spans.Length; i++) n.GetChild<Label>(i).Text = spans[i].Text;
        });
    }

    /// <summary>A hull or shield bar: 8 pixels, a little room above and more below.</summary>
    public static Part BarPart(double ratio, Color color) => new($"bar:{color.ToHtml()}", () =>
    {
        var m = Ignore(new MarginContainer());
        m.AddThemeConstantOverride("margin_top", 4);
        m.AddThemeConstantOverride("margin_bottom", 8);
        var bar = new ProgressBar { ShowPercentage = false, MaxValue = 1, Step = 0, CustomMinimumSize = new Vector2(0, 8), MouseFilter = Control.MouseFilterEnum.Ignore };
        var fill = UiTheme.Box(color, color, pad: 0);
        fill.SetContentMarginAll(0);
        var bg = UiTheme.Box(Palette.Bg, Palette.Bg, pad: 0);
        bg.SetContentMarginAll(0);
        bar.AddThemeStyleboxOverride("fill", fill);
        bar.AddThemeStyleboxOverride("background", bg);
        m.AddChild(bar);
        return m;
    }, n => n.GetChild<ProgressBar>(0).Value = Math.Clamp(ratio, 0, 1));

    public static Part GapPart(int px) => new($"gap:{px}", () => Ignore(new Control { CustomMinimumSize = new Vector2(0, px) }));
}

/// <summary>
/// A button holding labels instead of one text, sized to them: the web HUD's buttons with a
/// name and smaller lines (small) under it or beside it. Disabled, every line turns grey;
/// marked (a pressed toggle), the first line turns amber.
/// </summary>
public partial class LabelButton : Button
{
    readonly BoxContainer box;
    readonly List<Color> colors = [];
    readonly int padX, padY;
    /// <summary>Size the button keeps at least, whatever its labels.</summary>
    public Vector2 MinSize;
    public bool Marked;

    public LabelButton() : this(true, 8, 4) { }

    public LabelButton(bool vertical, int padX, int padY)
    {
        this.padX = padX;
        this.padY = padY;
        FocusMode = FocusModeEnum.None;
        box = vertical ? new VBoxContainer() : new HBoxContainer();
        box.Alignment = BoxContainer.AlignmentMode.Center;
        box.MouseFilter = MouseFilterEnum.Ignore;
        box.AddThemeConstantOverride("separation", vertical ? 0 : 6);
        box.SetAnchorsPreset(LayoutPreset.FullRect);
        box.OffsetLeft = padX;
        box.OffsetRight = -padX;
        box.OffsetTop = padY;
        box.OffsetBottom = -padY;
        AddChild(box);
        box.MinimumSizeChanged += Fit;
    }

    public Label Add(string text, Color color, int size = UiTheme.FontSize, bool wrap = false)
    {
        var l = HudKit.Text(text, color, size, wrap: wrap);
        l.VerticalAlignment = VerticalAlignment.Center;
        colors.Add(color);
        box.AddChild(l);
        Fit();
        return l;
    }

    public Label Line(int i) => box.GetChild<Label>(i);

    /// <summary>The color a line has while the button is enabled.</summary>
    public void SetColor(int line, Color c) => colors[line] = c;

    /// <summary>Refused only for want of credits: its price line stays readable, in red.</summary>
    public bool Poor;

    /// <summary>Shows the state: disabled (grey, faded), marked, and each line's color.</summary>
    public void Paint()
    {
        var off = Disabled;
        Modulate = off ? new Color(1, 1, 1, 0.7f) : Colors.White;
        for (var i = 0; i < colors.Count; i++)
        {
            HudKit.Paint(Line(i), off ? (Poor && i == 1 ? Palette.Danger : Palette.Muted) : i == 0 && Marked ? Palette.Accent : colors[i]);
        }
    }

    void Fit()
    {
        var m = box.GetCombinedMinimumSize();
        var size = new Vector2(Math.Max(MinSize.X, m.X + 2 * padX), Math.Max(MinSize.Y, m.Y + 2 * padY));
        if (CustomMinimumSize != size) CustomMinimumSize = size;
    }

    public void Refit() => Fit();
}
