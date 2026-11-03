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

    /// <summary>
    /// The sides' colors for color-blind players, by side: Okabe and Ito's palette, made to stay
    /// apart with each of the common color blindnesses (red, green or blue), and all at least 5:1
    /// against the dark background. Side 3 is the Errants'.
    /// </summary>
    public static readonly uint[] Adapted = [0xd55e00, 0x56b4e9, 0x009e73, 0xf0e442, 0xcc79a7, 0xe69f00, 0xdddddd];

    /// <summary>
    /// The color a side shows: the one it chose, or with adapted colors its side's own, the same on
    /// every machine and never the same for two sides.
    /// </summary>
    public static uint SideRgb(Side side, uint chosen) =>
        Settings.Current.Colors == SideColors.Adapted ? Adapted[Math.Abs(side.Id) % Adapted.Length] : chosen;

    /// <summary>Whether sides wear their mark (Draw.Mark) as well as their color: with adapted colors.</summary>
    public static bool Marked => Settings.Current.Colors == SideColors.Adapted;

    /// <summary>A race's color: its looks (tints, shots), and its card in the menu; with adapted colors, its own side's.</summary>
    public static Color Of(SandboxShip.Sim.Data.Faction f, float alpha = 1) =>
        Hex(SideRgb(f, SandboxShip.Sim.Data.Factions.Defs[f].Color), alpha);

    /// <summary>A side's color in a battle: what marks whose a unit is (rings, chevrons, minimap).</summary>
    public static Color Of(SandboxShip.Sim.World world, Side side, float alpha = 1) => Hex(Rgb(world, side), alpha);

    /// <summary>A side's color in a battle, as 0xRRGGBB: for the emblems tinted with it.</summary>
    public static uint Rgb(SandboxShip.Sim.World world, Side side) =>
        SideRgb(side, world.Factions.TryGetValue(side, out var f) ? f.Color : SandboxShip.Sim.Worlds.DefaultColor(side, SandboxShip.Sim.Worlds.RaceOf(world, side)));

    /// <summary>A side's color on the galaxy map: planet rings, fleet markers, emblems.</summary>
    public static Color Of(SandboxShip.Sim.Galaxy g, Side side, float alpha = 1) => Hex(Rgb(g, side), alpha);

    /// <summary>A side's color on the galaxy map, as 0xRRGGBB.</summary>
    public static uint Rgb(SandboxShip.Sim.Galaxy g, Side side) =>
        SideRgb(side, g.Factions.TryGetValue(side, out var f) ? f.Color : SandboxShip.Sim.Worlds.DefaultColor(side, SandboxShip.Sim.Galaxies.RaceOf(g, side)));
}
