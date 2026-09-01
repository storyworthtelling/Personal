using System.Text.Json;
using Codex.Domain.Audio;

namespace Codex.Infrastructure.Podcasts;

/// <summary>
/// JSON encode/decode for <see cref="PlaybackSettingsSnapshot"/> — the pure,
/// unit-tested half of the Android settings store.
/// </summary>
public static class PlaybackSettingsCodec
{
    public static string Encode(PlaybackSettingsSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot);

    public static PlaybackSettingsSnapshot Decode(string? json) =>
        string.IsNullOrEmpty(json)
            ? PlaybackSettingsSnapshot.Empty()
            : JsonSerializer.Deserialize<PlaybackSettingsSnapshot>(json) ?? PlaybackSettingsSnapshot.Empty();
}
