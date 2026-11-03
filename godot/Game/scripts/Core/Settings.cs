// Player preferences, kept in user://settings.json between sessions. Port of src/ui/settings.ts.
using System.Text.Json;
using Godot;

namespace SandboxShip.Core;

/// <summary>The sides' colors on screen: the ones chosen, or one per side that color-blind eyes tell apart.</summary>
public enum SideColors { Standard, Adapted }

public sealed class Settings
{
    /// <summary>Pan the battle view when the pointer touches a screen edge.</summary>
    public bool EdgePan { get; set; } = true;
    /// <summary>Show hull and shield bars on everything, not only on damaged or selected units.</summary>
    public bool AlwaysBars { get; set; }
    /// <summary>The player's ships use their abilities by themselves when it is worth it.</summary>
    public bool AutoAbilities { get; set; } = true;
    /// <summary>Sound effects volume, 0 to 1.</summary>
    public double Volume { get; set; } = 0.5;
    /// <summary>Music volume, 0 to 1.</summary>
    public double MusicVolume { get; set; } = 0.5;
    public bool Muted { get; set; }
    /// <summary>The name other players see, in a game over the network; at first, the system's user name.</summary>
    public string PlayerName { get; set; } = UserName();
    /// <summary>The last host joined by address, as "address:port".</summary>
    public string JoinAddress { get; set; } = $"127.0.0.1:{Sim.Net.Protocol.DefaultPort}";
    /// <summary>The online server, "address:port": the games it lists, and the relay of those hosted online.</summary>
    public string MasterAddress { get; set; } = DefaultMaster;
    /// <summary>The window: framed, the whole screen, or borderless over the screen (Display).</summary>
    public DisplayMode Display { get; set; } = DisplayMode.Windowed;
    /// <summary>The framed window's size, in pixels.</summary>
    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 800;
    /// <summary>The scale of what is drawn, 1 as made: the interface, and the view with it.</summary>
    public double UiScale { get; set; } = 1;
    public bool VSync { get; set; } = true;
    /// <summary>Frames a second at most; 0 for no cap.</summary>
    public int MaxFps { get; set; }
    /// <summary>Calmer motion: no growing cards at the start, no sliding menus, no shaking view.</summary>
    public bool ReduceMotion { get; set; }
    /// <summary>"fr" or "en"; empty, the system's language when it is English, French otherwise (Lang).</summary>
    public string Language { get; set; } = "";
    /// <summary>The sides' colors: as chosen in the lobby, or a palette told apart with any color blindness (Palette).</summary>
    public SideColors Colors { get; set; } = SideColors.Standard;
    /// <summary>Look once a launch, on the menu, whether a newer version is out (Updates).</summary>
    public bool CheckUpdates { get; set; } = true;
    /// <summary>The keys the player chose, by command; the others keep their default (Bindings).</summary>
    public Dictionary<string, Bindings.Combo> Keys { get; set; } = [];

    public const string DefaultMaster = "51.91.214.135:27029";

    const string Path = "user://settings.json";

    static string UserName()
    {
        var name = System.Environment.UserName;
        return name.Length > 0 ? char.ToUpperInvariant(name[0]) + name[1..] : "Joueur";
    }

    public static Settings Current { get; } = Load();

    static Settings Load()
    {
        try
        {
            var text = Godot.FileAccess.FileExists(Path) ? Godot.FileAccess.GetFileAsString(Path) : "";
            var s = text.Length > 0 ? JsonSerializer.Deserialize<Settings>(text) ?? new() : new();
            s.Sane();
            return s;
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException)
        {
            return new();
        }
    }

    /// <summary>
    /// What the file brought, brought back within what the game expects: a damaged or edited file
    /// never leaves a null or an absurd number behind it (no name at all, a scale of nothing).
    /// </summary>
    void Sane()
    {
        var d = new Settings();
        PlayerName ??= d.PlayerName;
        JoinAddress ??= d.JoinAddress;
        MasterAddress ??= d.MasterAddress;
        Language ??= d.Language;
        Keys ??= [];
        Volume = double.IsFinite(Volume) ? Math.Clamp(Volume, 0, 1) : d.Volume;
        MusicVolume = double.IsFinite(MusicVolume) ? Math.Clamp(MusicVolume, 0, 1) : d.MusicVolume;
        if (!Enum.IsDefined(Display)) Display = d.Display;
        if (!Enum.IsDefined(Colors)) Colors = d.Colors;
        WindowWidth = Math.Clamp(WindowWidth, 640, 7680);
        WindowHeight = Math.Clamp(WindowHeight, 360, 4320);
        UiScale = double.IsFinite(UiScale) ? Math.Clamp(UiScale, SandboxShip.Core.Display.Scales[0], SandboxShip.Core.Display.Scales[^1]) : d.UiScale;
        MaxFps = MaxFps == 0 ? 0 : Math.Clamp(MaxFps, 10, 1000);
    }

    public void Save()
    {
        // A capture run (--shot), or one with colors forced (--colors), leaves the player's settings as they were.
        if (Dev.Shot is not null || Dev.Colors is not null) return;
        using var f = Godot.FileAccess.Open(Path, Godot.FileAccess.ModeFlags.Write);
        // Without a writable user folder, the settings last for this session only.
        f?.StoreString(JsonSerializer.Serialize(this));
    }
}
