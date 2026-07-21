using System.Text.Json;
using FocusPhone.Domain.Audio;

namespace FocusPhone.Infrastructure.Podcasts;

/// <summary>
/// The persisted playback settings: a global default plus per-feed overrides
/// (keyed by feed URL, which is the episode FeedId). Per-show speed/skip-silence
/// lives here.
/// </summary>
public record PlaybackSettingsSnapshot(
    PlaybackSettings Default,
    Dictionary<string, PlaybackSettings> PerFeed)
{
    public static PlaybackSettingsSnapshot Empty() => new(new PlaybackSettings(), []);

    /// <summary>The effective settings for a feed: its override, else the default.</summary>
    public PlaybackSettings For(string feedId) =>
        PerFeed.TryGetValue(feedId, out var settings) ? settings : Default;

    public PlaybackSettingsSnapshot With(string feedId, PlaybackSettings settings)
    {
        var perFeed = new Dictionary<string, PlaybackSettings>(PerFeed) { [feedId] = settings };
        return this with { PerFeed = perFeed };
    }
}

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
