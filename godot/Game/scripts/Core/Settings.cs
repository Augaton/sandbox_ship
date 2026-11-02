// Player preferences, kept in user://settings.json between sessions. Port of src/ui/settings.ts.
using System.Text.Json;
using Godot;

namespace SandboxShip.Core;

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
            return text.Length > 0 ? JsonSerializer.Deserialize<Settings>(text) ?? new() : new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    public void Save()
    {
        // A capture run (--shot) leaves the player's settings as they were.
        if (Dev.Shot is not null) return;
        using var f = Godot.FileAccess.Open(Path, Godot.FileAccess.ModeFlags.Write);
        // Without a writable user folder, the settings last for this session only.
        f?.StoreString(JsonSerializer.Serialize(this));
    }
}
