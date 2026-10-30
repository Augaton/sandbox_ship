// The game's colors, from the web HUD's stylesheet (src/ui/hud.css) and the battle view.
using Godot;

namespace SandboxShip.Core;

public static class Palette
{
    public static readonly Color Bg = Hex(0x0b0d12);
    public static readonly Color Panel = new(18 / 255f, 22 / 255f, 32 / 255f, 0.94f);
    public static readonly Color PanelSolid = Hex(0x121620);
    public static readonly Color Raised = Hex(0x1a2030);
    public static readonly Color Line = Hex(0x2a3446);
    public static readonly Color Text = Hex(0xd9dee7);
    public static readonly Color Muted = Hex(0x949eb0);
    public static readonly Color Accent = Hex(0xe8a13a);
    public static readonly Color Ok = Hex(0x6fd08c);
    public static readonly Color Danger = Hex(0xe8705f);
    public static readonly Color Shield = Hex(0x5aa9e6);

    /// <summary>A 0xRRGGBB color, as the data and the web version write them.</summary>
    public static Color Hex(uint rgb, float alpha = 1) =>
        new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, alpha);

    /// <summary>A race's color: its looks (tints, shots), and its card in the menu.</summary>
    public static Color Of(SandboxShip.Sim.Data.Faction f, float alpha = 1) => Hex(SandboxShip.Sim.Data.Factions.Defs[f].Color, alpha);

    /// <summary>A side's color in a battle: what marks whose a unit is (rings, chevrons, minimap).</summary>
    public static Color Of(SandboxShip.Sim.World world, Side side, float alpha = 1) =>
        Hex(world.Factions.TryGetValue(side, out var f) ? f.Color : SandboxShip.Sim.Worlds.DefaultColor(side, SandboxShip.Sim.Worlds.RaceOf(world, side)), alpha);

    /// <summary>A side's color on the galaxy map: planet rings, fleet markers, emblems.</summary>
    public static Color Of(SandboxShip.Sim.Galaxy g, Side side, float alpha = 1) =>
        Hex(g.Factions.TryGetValue(side, out var f) ? f.Color : SandboxShip.Sim.Worlds.DefaultColor(side, SandboxShip.Sim.Galaxies.RaceOf(g, side)), alpha);
}
