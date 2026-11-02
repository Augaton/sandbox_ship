// The look of every panel, button and text: dark panels, amber accent, monospace, square
// corners, as in the web HUD (src/ui/hud.css).
using Godot;
using SandboxShip.Core;

namespace SandboxShip.Ui;

public static class UiTheme
{
    public const int FontSize = 15;
    public const int Radius = 3;

    public static readonly Font Mono = new SystemFont
    {
        FontNames = ["DejaVu Sans Mono", "Liberation Mono", "Menlo", "Consolas", "monospace"],
        Antialiasing = TextServer.FontAntialiasing.Gray,
    };

    static Theme? theme;

    public static StyleBoxFlat Box(Color bg, Color? border = null, int pad = 8)
    {
        var s = new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border ?? Palette.Line,
            ContentMarginLeft = pad, ContentMarginRight = pad, ContentMarginTop = pad / 2f + 2, ContentMarginBottom = pad / 2f + 2,
        };
        s.SetBorderWidthAll(border is null && bg.A == 0 ? 0 : 1);
        s.SetCornerRadiusAll(Radius);
        return s;
    }

    /// <summary>
    /// A button's face, drawn as an image: `from` on the left fading to `to` on the right, a
    /// one-pixel border fading with it, and on the left an amber `bar` when lit (the menus' buttons). Stretched to any
    /// size: the middle of the image stretches, its edges keep their width.
    /// </summary>
    public static StyleBoxTexture Shaded(Color from, Color to, Color border, Color? bar = null)
    {
        const int W = 128, H = 8, Bar = 3;
        var img = Image.CreateEmpty(W, H, false, Image.Format.Rgba8);
        for (var x = 0; x < W; x++)
        {
            var f = x / (W - 1f);
            var fill = from.Lerp(to, f);
            var line = border with { A = border.A * (1 - 0.7f * f) };
            for (var y = 0; y < H; y++)
            {
                var edge = y == 0 || y == H - 1 || x == W - 1 || (x == 0 && bar is null);
                img.SetPixel(x, y, bar is { } b && x < Bar ? b : edge ? line : fill);
            }
        }
        var s = new StyleBoxTexture
        {
            Texture = ImageTexture.CreateFromImage(img),
            TextureMarginLeft = bar is null ? 1 : Bar, TextureMarginRight = 1, TextureMarginTop = 1, TextureMarginBottom = 1,
            ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 6, ContentMarginBottom = 6,
        };
        return s;
    }

    /// <summary>The shared theme: set it on each screen's root Control.</summary>
    public static Theme Get()
    {
        if (theme is not null) return theme;
        var t = new Theme { DefaultFont = Mono, DefaultFontSize = FontSize };
        foreach (var type in new[] { "Label", "Button", "CheckBox", "OptionButton", "LineEdit", "RichTextLabel", "TooltipLabel", "ItemList" })
        {
            t.SetColor("font_color", type, Palette.Text);
        }
        t.SetColor("font_hover_color", "Button", Palette.Text);
        t.SetColor("font_pressed_color", "Button", Palette.Accent);
        t.SetColor("font_focus_color", "Button", Palette.Text);
        t.SetColor("font_disabled_color", "Button", Palette.Muted with { A = 0.6f });
        t.SetColor("default_color", "RichTextLabel", Palette.Text);
        t.SetStylebox("normal", "Button", Box(Palette.Raised));
        t.SetStylebox("hover", "Button", Box(Palette.Raised.Lightened(0.08f), Palette.Accent with { A = 0.6f }));
        t.SetStylebox("pressed", "Button", Box(Palette.Raised.Darkened(0.15f), Palette.Accent));
        t.SetStylebox("disabled", "Button", Box(Palette.Raised with { A = 0.5f }));
        t.SetStylebox("focus", "Button", Box(new Color(0, 0, 0, 0), Palette.Accent));
        t.SetStylebox("panel", "PanelContainer", Box(Palette.Panel, pad: 10));
        t.SetStylebox("panel", "Panel", Box(Palette.Panel));
        t.SetStylebox("panel", "TooltipPanel", Box(Palette.PanelSolid, Palette.Accent with { A = 0.5f }));
        t.SetStylebox("normal", "LineEdit", Box(Palette.Bg));
        t.SetStylebox("normal", "OptionButton", Box(Palette.Raised));
        t.SetStylebox("hover", "OptionButton", Box(Palette.Raised.Lightened(0.08f), Palette.Accent with { A = 0.6f }));
        t.SetStylebox("pressed", "OptionButton", Box(Palette.Raised.Darkened(0.15f), Palette.Accent));
        t.SetStylebox("focus", "OptionButton", Box(new Color(0, 0, 0, 0), Palette.Accent));
        t.SetStylebox("panel", "PopupMenu", Box(Palette.PanelSolid));
        t.SetColor("font_color", "PopupMenu", Palette.Text);
        t.SetColor("font_hover_color", "PopupMenu", Palette.Accent);
        t.SetStylebox("hover", "PopupMenu", Box(Palette.Raised));
        t.SetStylebox("grabber_area", "HSlider", Box(Palette.Accent with { A = 0.6f }, pad: 2));
        t.SetStylebox("slider", "HSlider", Box(Palette.Raised, pad: 2));
        t.SetStylebox("bg", "ProgressBar", Box(Palette.Bg, pad: 0));
        t.SetStylebox("fill", "ProgressBar", Box(Palette.Accent, Palette.Accent, pad: 0));
        t.SetStylebox("panel", "ScrollContainer", new StyleBoxEmpty());
        theme = t;
        return t;
    }

    public static Label Text(string text, Color? color = null, int size = FontSize)
    {
        var l = new Label { Text = text };
        if (color is Color c) l.AddThemeColorOverride("font_color", c);
        if (size != FontSize) l.AddThemeFontSizeOverride("font_size", size);
        return l;
    }
}
