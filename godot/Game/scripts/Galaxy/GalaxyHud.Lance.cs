// The star lance in the planet panel: how far it has charged, and the button that aims it.
using Godot;
using SandboxShip.Core;
using SandboxShip.Sim;
using SandboxShip.Sim.Data;
using SandboxShip.Ui;

namespace SandboxShip.Campaign;

public partial class GalaxyHud
{
    Label? lanceLabel;
    Button? lanceFire;

    void RenderLance(Planet p)
    {
        var row = HudKit.Row(6);
        lanceLabel = Text("", wrap: true);
        lanceLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(lanceLabel);
        var id = p.Id;
        lanceFire = Btn("Tirer", () => scene.BeginLance(id));
        lanceFire.CustomMinimumSize = new Vector2(0, 32);
        lanceFire.TooltipText = $"Frappe une planète ennemie à {Galaxies.LanceReach} voies au plus : plateformes, canon ionique et dock détruits, "
            + "station abaissée d'un niveau, une mine perdue, flottes réduites de moitié. Recharge 5 min.";
        row.AddChild(lanceFire);
        body.AddChild(row);
        UpdateLance(p);
    }

    void UpdateLance(Planet? p)
    {
        if (p is null || lanceLabel is null || !IsInstanceValid(lanceLabel) || lanceFire is null) return;
        lanceLabel.Text = p.LanceCharge > 0
            ? $"Lance stellaire : charge {HudKit.Percent(1 - p.LanceCharge / Galaxies.LanceRecharge)}"
            : "Lance stellaire prête";
        lanceFire.Disabled = p.LanceCharge > 0;
    }

    /// <summary>A lance fired: told to whoever it concerns, and its beam shown on the map.</summary>
    void LanceFiredShown(LanceFired lf)
    {
        var g = G;
        var target = g.Planets[lf.Target];
        var by = NameOf(lf.Side);
        if (lf.Side == Player) Notify($"Lance stellaire : {target.Name} frappée", target.Id);
        else if (target.Owner == Player)
        {
            Notify($"{target.Name} frappée par la Lance stellaire des {by}", target.Id);
            Sfx.Play(Sound.Alert);
        }
        else if (Galaxies.Known(g, Player, target)) Notify($"Les {by} frappent {target.Name} de leur Lance stellaire", target.Id);
        Sfx.Play(Sound.Horn);
        scene.LanceShot(lf.From, lf.Target);
    }
}
