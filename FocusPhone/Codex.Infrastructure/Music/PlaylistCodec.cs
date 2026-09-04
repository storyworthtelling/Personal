using System.Text.Json;
using System.Text.Json.Serialization;
using Codex.Domain.Music;

namespace Codex.Infrastructure.Music;

/// <summary>
/// JSON encode/decode for the owner's playlists, so the Android store stays a thin
/// prefs adapter and the (de)serialization is unit-tested.
///
/// Enums are written as names rather than numbers: these files outlive the code
/// that wrote them, and inserting a value into an enum would silently re-map every
/// stored rule if they were ordinals.
/// </summary>
public static class PlaylistCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Encode(IReadOnlyList<Playlist> playlists) =>
        JsonSerializer.Serialize(playlists, Options);

    /// <summary>
    /// Playlists, or none. A garbled value loses them — which is bad — but throwing
    /// takes the Music tab down on every launch, which is worse and cannot be
    /// recovered from on the device.
    /// </summary>
    public static IReadOnlyList<Playlist> Decode(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<Playlist>>(json, Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
