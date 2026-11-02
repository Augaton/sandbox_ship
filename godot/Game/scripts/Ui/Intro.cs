// The opening, as the big games have it: the author's signature, what the game is made with and
// whose art it uses, then the title, which is the loading screen's logo at its place, as the dark
// lifts off it. The game loads meanwhile, under it. A key, a click or a button passes it.
using Godot;
using SandboxShip.Core;

namespace SandboxShip.Ui;

public partial class Intro : CanvasLayer
{
    /// <summary>A card fades in, holds, fades out, slowly coming closer; a moment of dark comes before each.</summary>
    const double Gap = 0.3, In = 0.8, Hold = 1.4, Out = 0.6;
    /// <summary>Then the dark lifts off the title, which stays a moment before the menu.</summary>
    const double Lift = 1.1, Title = 0.8, Passed = 0.3;

    readonly TaskCompletionSource done = new();
    Control root = null!;
    Tween? tween;
    bool passed;

    /// <summary>Over once the title has been shown, or the opening passed.</summary>
    public Task Done => done.Task;

    public override void _Ready()
    {
        Layer = 110;
        root = new Control { Theme = UiTheme.Get(), MouseFilter = Control.MouseFilterEnum.Stop };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);
        var dark = new ColorRect { Color = Palette.Bg, MouseFilter = Control.MouseFilterEnum.Ignore };
        dark.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(dark);

        tween = CreateTween();
        // With calmer motion asked for, the cards only fade in and out.
        var grow = Settings.Current.ReduceMotion ? 1f : 1.04f;
        Control[] cards = [Signature(), MadeWith(), Art()];
        for (var i = 0; i < cards.Length; i++)
        {
            var card = cards[i];
            var name = $"intro-{i + 1}";
            tween.TweenInterval(Gap);
            tween.TweenProperty(card, "modulate:a", 1.0, In).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            tween.Parallel().TweenProperty(card, "scale", Vector2.One * grow, In + Hold + Out).From(Vector2.One);
            tween.Parallel().TweenProperty(card, "modulate:a", 0.0, Out).SetDelay(In + Hold).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            tween.Parallel().TweenCallback(Callable.From(() => Main.Instance.Snap(name, 1))).SetDelay(In + Hold / 2);
        }
        tween.TweenCallback(Callable.From(() => Sfx.Play(Sound.Horn, gain: 0.5)));
        tween.TweenProperty(root, "modulate:a", 0.0, Lift).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        tween.TweenInterval(Title);
        tween.TweenCallback(Callable.From(End));
    }

    public override void _Input(InputEvent e)
    {
        if (passed || e is not (InputEventKey { Pressed: true, Echo: false } or InputEventMouseButton { Pressed: true } or InputEventJoypadButton { Pressed: true })) return;
        GetViewport().SetInputAsHandled();
        passed = true;
        tween?.Kill();
        tween = CreateTween();
        tween.TweenProperty(root, "modulate:a", 0.0, Passed * root.Modulate.A);
        tween.TweenCallback(Callable.From(End));
    }

    void End()
    {
        done.TrySetResult();
        QueueFree();
    }

    /// <summary>
    /// A card: its lines one under the other, centered on the screen, hidden until its turn; it
    /// grows from its center, smoothly filtered so that the text keeps its shape as it grows.
    /// </summary>
    Control Card(params Control[] lines)
    {
        var card = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = Colors.Transparent, TextureFilter = CanvasItem.TextureFilterEnum.Linear };
        card.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        card.Resized += () => card.PivotOffset = card.Size / 2;
        var column = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        column.AddThemeConstantOverride("separation", 10);
        foreach (var line in lines)
        {
            line.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
            column.AddChild(line);
        }
        card.AddChild(column);
        root.AddChild(card);
        return card;
    }

    static Label Line(string text, Color color, int size, bool bold = false)
    {
        var l = UiTheme.Text(text, color, size);
        l.HorizontalAlignment = HorizontalAlignment.Center;
        if (bold) l.AddThemeFontOverride("font", HudKit.Bold);
        return l;
    }

    /// <summary>Room between two groups of lines.</summary>
    static Control Space(float height) => new() { CustomMinimumSize = new Vector2(0, height), MouseFilter = Control.MouseFilterEnum.Ignore };

    /// <summary>The short amber rule under a card's main line.</summary>
    static Control Rule() => new ColorRect { Color = Palette.Accent, CustomMinimumSize = new Vector2(48, 2), MouseFilter = Control.MouseFilterEnum.Ignore };

    Control Signature()
    {
        var name = new TextureRect { Texture = Logos.Signature(7), MouseFilter = Control.MouseFilterEnum.Ignore };
        name.AccessibilityName = "Augaton";
        return Card(name, Space(4), Line("présente", Palette.Muted, 16));
    }

    /// <summary>A credit's logo (assets/credits, with their licences), or its name in text without it.</summary>
    static Control Logo(string file, string name) => ResourceLoader.Exists($"res://assets/credits/{file}")
        ? new TextureRect { Texture = GD.Load<Texture2D>($"res://assets/credits/{file}"), MouseFilter = Control.MouseFilterEnum.Ignore, AccessibilityName = name }
        : Line(name, Palette.Text, 26, true);

    Control MadeWith() => Card(
        Line("Propulsé par", Palette.Muted, 14),
        Logo("godot-logo.svg", "Godot Engine"),
        Rule(),
        Line("C# et .NET", Palette.Muted, 15));

    Control Art() => Card(
        Line("Graphismes", Palette.Muted, 14),
        Space(6),
        Logo("foozle.png", "Foozle"),
        Line("Void Fleet, Void Environment, Void Main Ship", Palette.Muted, 14),
        Space(10),
        Line("zintoki", Palette.Text, 26, true),
        Line("Space Breaker", Palette.Muted, 14),
        Space(6),
        Rule(),
        Line("Des packs offerts en CC0 : merci à eux.", Palette.Muted, 13));
}
