// An achievement just earned: a card at the top right, over every screen, its icon, its name and
// what earned it. It slides in, stays a few seconds, fades out; several come one after the other.
using Godot;
using SandboxShip.Core;
using SandboxShip.Sim;

namespace SandboxShip.Ui;

public partial class AchievementToast : CanvasLayer
{
    const double In = 0.35, Stay = 4.5, Out = 0.6;
    /// <summary>Below the battle's top bar, off the right edge.</summary>
    const float Top = 72, Margin = 16, CardWidth = 340;

    static AchievementToast? instance;
    readonly Queue<AchievementDef> waiting = new();
    bool showing;

    /// <summary>Shows an achievement, after those already waiting.</summary>
    public static void Show(AchievementDef a)
    {
        if (instance is null || !IsInstanceValid(instance))
        {
            instance = new AchievementToast();
            Main.Instance.AddChild(instance);
        }
        instance.waiting.Enqueue(a);
        if (!instance.showing) instance.Next();
    }

    public override void _Ready() => Layer = 115;

    void Next()
    {
        if (!waiting.TryDequeue(out var a))
        {
            showing = false;
            return;
        }
        showing = true;
        var card = Card(a);
        AddChild(card);
        Sfx.Play(Sound.Built, 0, 0.6);
        // Laid out first, then placed off the screen's right edge and slid in.
        var screen = GetViewport().GetVisibleRect().Size;
        var size = card.GetCombinedMinimumSize();
        card.Size = size;
        var x = screen.X - size.X - Margin;
        card.Position = new Vector2(screen.X + 8, Top);
        card.Modulate = Colors.Transparent;
        var t = CreateTween();
        t.TweenProperty(card, "position:x", x, In).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        t.Parallel().TweenProperty(card, "modulate:a", 1.0, In);
        t.TweenInterval(Stay);
        t.TweenProperty(card, "modulate:a", 0.0, Out);
        t.TweenCallback(Callable.From(() =>
        {
            card.QueueFree();
            Next();
        }));
    }

    static Control Card(AchievementDef a)
    {
        var p = HudKit.Panel(12, 10, Palette.Accent with { A = 0.7f });
        p.Theme = UiTheme.Get();
        p.MouseFilter = Control.MouseFilterEnum.Ignore;
        p.CustomMinimumSize = new Vector2(CardWidth, 0);
        var row = HudKit.Row(12);
        p.AddChild(row);
        var icon = new TextureRect
        {
            Texture = AchievementIcons.Of(a, true), CustomMinimumSize = new Vector2(48, 48),
            StretchMode = TextureRect.StretchModeEnum.KeepCentered, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        };
        row.AddChild(icon);
        var col = HudKit.Column(2);
        col.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        col.AddChild(UiTheme.Text("Succès débloqué", Palette.Accent, 13));
        var name = UiTheme.Text(a.Name, Palette.Text, 17);
        name.AddThemeFontOverride("font", HudKit.Bold);
        col.AddChild(name);
        var text = UiTheme.Text(a.Text, Palette.Muted, 13);
        text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        text.CustomMinimumSize = new Vector2(CardWidth - 96, 0);
        col.AddChild(text);
        row.AddChild(col);
        return p;
    }
}
