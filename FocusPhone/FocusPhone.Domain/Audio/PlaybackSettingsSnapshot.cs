namespace FocusPhone.Domain.Audio;

/// <summary>
/// The persisted playback settings: a global default plus per-feed overrides
/// (keyed by feed URL, which is the episode FeedId). Per-show speed/skip-silence
/// lives here. A Domain model like its siblings (<see cref="PodcastSubscription"/>,
/// <see cref="PlaybackProgress"/>); its JSON codec stays in Infrastructure.
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
