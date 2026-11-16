// The short messages at the top left of a HUD, in battle and on the galaxy map, which fade out
// on their own. A message already showing is counted again (×2) and starts its time over, rather
// than stacked; an alert stays longer, in a colored frame, and offers to go and see.
using Godot;
using SandboxShip.Core;

namespace SandboxShip.Ui;

public sealed class ToastStack
{
    /// <summary>How long a message and an alert stay, then how long they fade, in milliseconds.</summary>
    const double Life = 2200, AlertLife = 4500, Fading = 400;
    const int Most = 4;
    /// <summary>A long message wraps past this width, in pixels, rather than run under a panel.</summary>
    const float Widest = 520;

    readonly Dictionary<Control, (double Fade, double End, string Text, int Count, Label Label)> times = [];

    /// <summary>The column of messages: its owner puts it where it wants it.</summary>
    public VBoxContainer Box { get; } = HudKit.Column(4);

    /// <summary>
    /// A message; with `see`, an alert with a button to go and see, in `frame` (danger by default).
    /// `more`: lines under it, smaller. `life`: how long it stays, if not as long as any other.
    /// </summary>
    public void Add(string text, Action? see = null, Color? frame = null, IReadOnlyList<string>? more = null, double? life = null)
    {
        var now = (double)Time.GetTicksMsec();
        var stay = life ?? (see is not null ? AlertLife : Life);
        if (see is null && more is null && times.FirstOrDefault(s => s.Value.Text == text).Key is { } same)
        {
            var (_, _, _, count, shown) = times[same];
            Fit(shown, $"{text} ×{++count}");
            same.Modulate = Colors.White;
            times[same] = (now + stay, now + stay + Fading, text, count, shown);
            Box.MoveChild(same, -1);
            return;
        }
        var t = HudKit.Panel(12, 6, frame ?? (see is not null ? Palette.Danger : null));
        t.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        var row = HudKit.Row(12);
        t.AddChild(row);
        var lines = HudKit.Column(0);
        lines.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        var label = Line(text, Palette.Text, more is not null);
        lines.AddChild(label);
        foreach (var line in more ?? []) lines.AddChild(Line(line, Palette.Muted, false));
        row.AddChild(lines);
        if (see is not null) row.AddChild(HudKit.Button("Voir", see, $"{Bindings.Label(Bindings.LastAlert)} : aller à la dernière alerte", 32));
        else t.MouseFilter = Control.MouseFilterEnum.Ignore;
        Box.AddChild(t);
        while (Box.GetChildCount() > Most) Remove(Box.GetChild<Control>(0));
        times[t] = (now + stay, now + stay + Fading, text, 1, label);
    }

    static Label Line(string text, Color color, bool bold)
    {
        var l = HudKit.Text("", color, bold: bold, wrap: true);
        Fit(l, text);
        return l;
    }

    /// <summary>A label's text, and as wide as it reads in the language shown, up to Widest.</summary>
    static void Fit(Label l, string text)
    {
        l.Text = text;
        var width = UiTheme.Mono.GetStringSize(Lang.T(text), HorizontalAlignment.Left, -1, UiTheme.FontSize).X + 2;
        l.CustomMinimumSize = new Vector2(Math.Min(Widest, width), 0);
    }

    void Remove(Control t)
    {
        times.Remove(t);
        Box.RemoveChild(t);
        t.QueueFree();
        // Back to the width of what is left.
        Box.Size = Vector2.Zero;
    }

    /// <summary>Each frame: what has had its time fades, then goes.</summary>
    public void Tick()
    {
        if (times.Count == 0) return;
        var now = (double)Time.GetTicksMsec();
        foreach (var (t, (fade, end, _, _, _)) in times.ToList())
        {
            if (now >= end) Remove(t);
            else if (now >= fade) t.Modulate = new Color(1, 1, 1, (float)Math.Max(0, 1 - (now - fade) / 300));
        }
    }
}
