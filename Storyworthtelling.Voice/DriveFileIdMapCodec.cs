using System.Text.Json;

namespace Storyworthtelling.Voice;

/// <summary>
/// JSON encode/decode for the "well-known Drive file name → stable id" map, so an app can persist the
/// <see cref="InMemoryDriveFileIdStore"/> snapshot to its local settings and seed from it at startup —
/// making a captured id survive a restart (not just the next vault publish). Absent/garbled → empty.
/// </summary>
public static class DriveFileIdMapCodec
{
    public static string Encode(IReadOnlyDictionary<string, string> map) => JsonSerializer.Serialize(map);

    public static IReadOnlyDictionary<string, string> Decode(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return new Dictionary<string, string>();
        }
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }
}
