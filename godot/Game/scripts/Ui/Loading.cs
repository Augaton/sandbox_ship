// The loading screen: over everything while the game loads, then for a moment between two
// screens while the next one is built. The boot splash (boot.png) is the same logo at the same
// spot, so that the start reads as one screen; a tip of the game waits at the bottom.
using Godot;
using SandboxShip.Core;

namespace SandboxShip.Ui;

public partial class Loading : CanvasLayer
{
    /// <summary>Logo scale: boot.png is drawn at this scale, and the boot splash shows it unscaled.</summary>
    public const int LogoScale = 8;
    const double FadeIn = 0.15, FadeOut = 0.3;

    static readonly string[] Tips =
    [
        "Un bouclier tombé à zéro reste coupé quelques secondes : c'est le moment de concentrer le feu.",
        "Chaque vaisseau abattu rapporte 20 % de sa valeur à celui qui l'achève.",
        "Le Colosse des Errants garde un coin de la carte : 8 000 crédits et un niveau de recherche à qui l'abat.",
        "Le Rempart provoque les ennemis proches : ils le visent au lieu de tes vaisseaux fragiles.",
        "Les appareils d'un transporteur attaquent la cible de leur porteur.",
        "Une flotte avance au pas de son vaisseau le plus lent : les lourds devant, les petits derrière.",
        "Détruire les points d'attache d'une station la désarme : chantier, hangar, bouclier, tourelles.",
        "Ctrl + 1 à 9 crée un groupe ; le chiffre seul le rappelle, deux fois le centre.",
        "Il faut un laboratoire pour lancer une recherche.",
        "Plateformes de défense : 3 autour de la station, 2 par avant-poste, 1 par mine.",
        "En campagne, un clic droit sur un vaisseau à produire le met en boucle sur la planète.",
        "Retour arrière ramène la vue sur la dernière alerte.",
    ];

    Control root = null!;
    Label status = null!, tip = null!;
    Control bar = null!;
    Tween? fade;
    /// <summary>Null: no measure of progress, a segment runs along the bar.</summary>
    Func<double>? progress;

    public bool Covering => Visible && root.Modulate.A >= 1;

    public override void _Ready()
    {
        Layer = 100;
        root = new Control { Theme = UiTheme.Get(), MouseFilter = Control.MouseFilterEnum.Stop };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);
        var bg = new ColorRect { Color = Palette.Bg, MouseFilter = Control.MouseFilterEnum.Ignore };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(bg);
        var logo = new TextureRect { Texture = Logos.Logo(LogoScale), MouseFilter = Control.MouseFilterEnum.Ignore };
        root.AddChild(logo);
        bar = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(280, 3) };
        bar.Draw += DrawBar;
        root.AddChild(bar);
        status = UiTheme.Text("", Palette.Muted, 14);
        status.HorizontalAlignment = HorizontalAlignment.Center;
        root.AddChild(status);
        tip = UiTheme.Text("", Palette.Muted, 14);
        tip.HorizontalAlignment = HorizontalAlignment.Center;
        tip.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        root.AddChild(tip);
        // Placed by hand: the logo must land on the boot splash's pixels, which Godot centers and floors.
        void Place()
        {
            var size = root.Size;
            var l = logo.Texture.GetSize();
            var at = ((size - l) / 2).Floor();
            logo.Position = at;
            logo.Size = l;
            bar.Position = new Vector2(Mathf.Floor((size.X - 280) / 2), at.Y + l.Y + 36);
            bar.Size = new Vector2(280, 3);
            status.Position = new Vector2(0, bar.Position.Y + 14);
            status.Size = new Vector2(size.X, 20);
            var width = Math.Min(640, size.X - 32);
            tip.Position = new Vector2(Mathf.Floor((size.X - width) / 2), size.Y - 96);
            tip.Size = new Vector2(width, 60);
        }
        root.Resized += Place;
        Place();
    }

    public override void _Process(double delta)
    {
        if (Visible) bar.QueueRedraw();
    }

    void DrawBar()
    {
        var w = bar.Size.X;
        bar.DrawRect(new Rect2(0, 0, w, 3), Palette.Line);
        if (progress is not null)
        {
            bar.DrawRect(new Rect2(0, 0, (float)(w * Math.Clamp(progress(), 0, 1)), 3), Palette.Accent);
            return;
        }
        // A segment sweeping across, eased at both ends.
        var t = Godot.Time.GetTicksMsec() % 1400 / 1400.0;
        var x = (float)((0.5 - 0.5 * Math.Cos(t * Math.PI)) * (w + 80) - 80);
        bar.DrawRect(new Rect2(Math.Max(0, x), 0, Math.Min(x + 80, w) - Math.Max(0, x), 3), Palette.Accent);
    }

    /// <summary>Shows the screen at once, opaque: the start of the game, over the boot splash.</summary>
    public void Begin(string text, Func<double>? measure = null)
    {
        fade?.Kill();
        Set(text, measure);
        root.Modulate = Colors.White;
        Visible = true;
    }

    /// <summary>Fades the screen in over what is shown, then waits for it to be drawn: the next screen can be built behind it.</summary>
    public async Task Cover(string text)
    {
        // Already covering (the start of the game): what it shows stays.
        if (!Covering)
        {
            Set(text, null);
            fade?.Kill();
            if (!Visible) root.Modulate = Colors.Transparent;
            Visible = true;
            fade = CreateTween();
            fade.TweenProperty(root, "modulate:a", 1.0, FadeIn * (1 - root.Modulate.A));
            await ToSignal(fade, Tween.SignalName.Finished);
        }
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    /// <summary>Fades the screen out over the new one.</summary>
    public void Reveal()
    {
        fade?.Kill();
        fade = CreateTween();
        fade.TweenProperty(root, "modulate:a", 0.0, FadeOut);
        fade.TweenCallback(Callable.From(() => Visible = false));
    }

    void Set(string text, Func<double>? measure)
    {
        status.Text = text;
        progress = measure;
        tip.Text = $"Astuce : {Tips[Random.Shared.Next(Tips.Length)]}";
    }
}
