// Whether a newer version is out (not in the web version): what the game takes from GitHub's answer
// about its latest release, which Game/scripts/Core/Updates.cs asks for. Only a tag that is a
// version is kept: the release page opened is built from it, never from an address in the answer.
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SandboxShip.Sim;

public static partial class Releases
{
    /// <summary>The release's tag, when it is a version ("v1.2.3"); anything else is no answer.</summary>
    public static string? Tag(byte[] body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("tag_name", out var tag)
                && tag.ValueKind == JsonValueKind.String && Version().IsMatch(tag.GetString()!) ? tag.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static bool IsNewer(string tag, string current) =>
        System.Version.TryParse(tag.TrimStart('v'), out var theirs) && System.Version.TryParse(current, out var ours) && theirs > ours;

    /// <summary>ASCII digits only, to the very end: `\d` takes any script's digits, and `$` a line break after.</summary>
    [GeneratedRegex(@"^v?[0-9]{1,4}\.[0-9]{1,4}\.[0-9]{1,4}\z")]
    private static partial Regex Version();
}
