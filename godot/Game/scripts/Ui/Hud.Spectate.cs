// A spectator's battle (not in the web version): no seat and no order, but a choice of eyes. A
// bar under the top one picks whose the snapshots show the battle through: every side's at
// once, without fog, or one side's, as its team sees it.
using Godot;
using SandboxShip.Core;
using SandboxShip.Network;
using SandboxShip.Sim;
using SandboxShip.Sim.Data;

namespace SandboxShip.Ui;

public partial class Hud
{
    SpectatorBar? spectating;

    /// <summary>Over the network, for a spectator: the bar of views, shown once.</summary>
    public void ShowSpectating(World world)
    {
        if (spectating is null) AddChild(spectating = new SpectatorBar(world));
    }
}

/// <summary>Whose eyes a spectator watches through: a button for everyone's, then one a side; the one chosen outlined.</summary>
public partial class SpectatorBar(World world) : CanvasLayer
{
    readonly Dictionary<int, Button> buttons = [];
    int shown = int.MinValue;

    public override void _Ready()
    {
        Layer = 8;
        var root = new Control { Theme = UiTheme.Get(), MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);
        var bar = HudKit.Row(6);
        bar.AddChild(HudKit.Text("Spectateur, vue :", Palette.Muted, 14));
        Choice(bar, -1, "Tout voir");
        foreach (var f in world.Factions.Values.Where(f => f.Race != Faction.Errant).OrderBy(f => f.Id.Id))
        {
            Choice(bar, f.Id.Id, Factions.Defs[f.Race].Name);
        }
        var panel = HudKit.Panel(10, 6);
        panel.AddChild(bar);
        root.AddChild(panel);
        void Place() => panel.Position = new Vector2(Mathf.Floor((root.Size.X - panel.Size.X) / 2), 64);
        root.Resized += Place;
        panel.Resized += Place;
        Place();
    }

    void Choice(HBoxContainer bar, int side, string name)
    {
        var b = HudKit.Button(name, () => Session.Current?.Spectate(side), side < 0 ? "Toute la bataille, sans brouillard" : $"La bataille comme les {name} la voient", 32);
        buttons[side] = b;
        bar.AddChild(b);
    }

    public override void _Process(double delta)
    {
        var viewing = Session.Current?.Viewing ?? -1;
        if (viewing == shown) return;
        shown = viewing;
        foreach (var (side, b) in buttons) HudKit.Outline(b, side == viewing ? Palette.Accent : null);
    }
}
