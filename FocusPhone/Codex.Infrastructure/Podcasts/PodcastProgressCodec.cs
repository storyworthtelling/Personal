using System.Text.Json;
using Codex.Domain.Audio;

namespace Codex.Infrastructure.Podcasts;

/// <summary>
/// JSON encode/decode for the podcast playback-progress book, so the Android
/// SharedPreferences store stays a thin adapter and the (de)serialization is
/// unit-tested without a device.
/// </summary>
public static class PodcastProgressCodec
{
    public static string Encode(IReadOnlyCollection<PlaybackProgress> progress) =>
        JsonSerializer.Serialize(progress);

    public static IReadOnlyList<PlaybackProgress> Decode(string? json) =>
        string.IsNullOrEmpty(json)
            ? []
            : JsonSerializer.Deserialize<List<PlaybackProgress>>(json) ?? [];
}
