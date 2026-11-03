// Whether a newer version is out: the latest release on GitHub, asked once a launch from the menu,
// in the background. Only that request goes out, with what GitHub's API asks of every caller;
// failing or slow, it says nothing.
using System.Text.Json;
using System.Text.RegularExpressions;
using Godot;

namespace SandboxShip.Core;

public static partial class Updates
{
    const string Latest = "https://api.github.com/repos/Augaton/sandbox_ship/releases/latest";
    const string Releases = "https://github.com/Augaton/sandbox_ship/releases/tag/";
    /// <summary>Seconds before giving up.</summary>
    const double Patience = 6;
    /// <summary>The largest answer read: a release's description fits in far less.</summary>
    const int MaxBody = 1 << 20;

    static bool asked;

    /// <summary>The newer version, as its tag ("v0.6.0"), once known; null while none is.</summary>
    public static string? Newer { get; private set; }

    /// <summary>Its release page, made from its tag: no address from the answer is ever opened.</summary>
    public static string? Page => Newer is null ? null : Releases + Newer;

    /// <summary>A newer version was found: the menu shows it.</summary>
    public static event Action? Found;

    /// <summary>
    /// Asks, once a launch: not when the player turned it off, nor in a capture, nor on a server.
    /// --update-url asks there instead, even in a capture, to try it against a local stand-in.
    /// </summary>
    public static void Check(Node host)
    {
        if (asked) return;
        if (Dev.UpdateUrl is null && (!Settings.Current.CheckUpdates || Dev.Shot is not null || Dev.Snap is not null || Dev.Service)) return;
        asked = true;
        var http = new HttpRequest { Timeout = Patience, UseThreads = true, BodySizeLimit = MaxBody };
        host.AddChild(http);
        http.RequestCompleted += (result, code, _, body) =>
        {
            http.QueueFree();
            if (result != (long)HttpRequest.Result.Success || code != 200) return;
            if (Read(body) is not { } tag || !IsNewer(tag, Ui.Menu.Version)) return;
            Newer = tag;
            Found?.Invoke();
        };
        // Who calls and what it reads, as the API requires: nothing else about the player.
        if (http.Request(Dev.UpdateUrl ?? Latest, [$"User-Agent: SandboxShip/{Ui.Menu.Version}", "Accept: application/vnd.github+json"]) != Error.Ok)
            http.QueueFree();
    }

    /// <summary>The release's tag, when it is a version ("v1.2.3"); anything else is no answer.</summary>
    static string? Read(byte[] body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("tag_name", out var tag)
                && tag.ValueKind == JsonValueKind.String && Tag().IsMatch(tag.GetString()!) ? tag.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static bool IsNewer(string tag, string current) =>
        System.Version.TryParse(tag.TrimStart('v'), out var theirs) && System.Version.TryParse(current, out var ours) && theirs > ours;

    [GeneratedRegex(@"^v?\d{1,4}\.\d{1,4}\.\d{1,4}$")]
    private static partial Regex Tag();
}
