// A tutorial's card: one thing taught at a time, each step done by doing it (seen in the commands
// the player sends, or in the game), each one passable, the tutorial left at any moment. The
// battle's (Tutorial.cs) and the campaign's (CampaignTutorial.cs) fill it with their steps.
using Godot;
using SandboxShip.Core;

namespace SandboxShip.Ui;

public abstract partial class Guide : CanvasLayer
{
    /// <summary>What a step asks, and how the tutorial knows it is done.</summary>
    protected sealed record Step(string Title, string Text, Func<bool> Done);

    protected readonly List<Step> Steps = [];
    /// <summary>What the player has done, by name, from the commands that went through.</summary>
    protected readonly HashSet<string> Did = [];
    int current;
    protected PanelContainer Card { get; private set; } = null!;
    Label count = null!, title = null!, text = null!;
    Button pass = null!;

    /// <summary>The card's last words, once every step is done.</summary>
    protected abstract string Ending { get; }

    /// <summary>"Quitter le didacticiel": back to the menu.</summary>
    protected abstract void Leave();

    /// <summary>Every step is done.</summary>
    protected virtual void Finished() { }

    /// <summary>Where the card ends, down the screen: the battle's toasts come under it.</summary>
    public float Bottom => Card.Position.Y + Card.Size.Y;

    /// <summary>Builds the card, `width` pixels wide, top left under the top bar; the steps are in place by then.</summary>
    protected void BuildCard(float width = 400)
    {
        Layer = 2;
        var root = new Control { Theme = UiTheme.Get(), MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);
        Card = HudKit.Panel(16, 12, Palette.Accent with { A = 0.6f });
        Card.Position = new Vector2(8, 64);
        Card.CustomMinimumSize = new Vector2(width, 0);
        root.AddChild(Card);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        Card.AddChild(column);
        count = HudKit.Text("", Palette.Muted, 13);
        column.AddChild(count);
        title = HudKit.Text("", Palette.Accent, 17, bold: true);
        column.AddChild(title);
        text = HudKit.Text("", wrap: true);
        text.CustomMinimumSize = new Vector2(width - 32, 0);
        column.AddChild(text);
        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        column.AddChild(buttons);
        pass = HudKit.Button("Passer", Next, "Passer cette étape", 34);
        buttons.AddChild(pass);
        buttons.AddChild(HudKit.Button("Quitter le didacticiel", Leave, "Retour au menu", 34));
        ShowStep();
    }

    public override void _Process(double delta)
    {
        if (current < Steps.Count && Steps[current].Done()) Next();
    }

    /// <summary>Shows step `index` (from 0) at once, those before it left behind: a dev run's start further on.</summary>
    protected void SkipTo(int index)
    {
        current = Math.Clamp(index, 0, Steps.Count);
        ShowStep();
    }

    void Next()
    {
        if (current >= Steps.Count) return;
        current++;
        Sfx.Play(Sound.Built, 0, 0.5);
        ShowStep();
    }

    void ShowStep()
    {
        if (current >= Steps.Count)
        {
            count.Text = "Didacticiel terminé";
            Finished();
            title.Text = "Bravo";
            text.Text = Ending;
            pass.Visible = false;
        }
        else
        {
            count.Text = $"Didacticiel, étape {current + 1} sur {Steps.Count}";
            title.Text = Steps[current].Title;
            text.Text = Steps[current].Text;
        }
        // As tall as this step's text, no taller: the campaign's card stands on the bottom of the screen.
        Card.ResetSize();
        HudKit.FadeIn(Card, 0.2);
    }
}
