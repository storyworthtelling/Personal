using Codex.Domain.Music;

namespace Codex.Domain.Audio;

/// <summary>
/// The syncable slice of audio state — the shows you follow, what you've marked
/// played, where each episode was left off, and your playback/equalizer preferences —
/// captured as one plain value so it can be mirrored to the owner's Drive for backup
/// and restore across a factory reset or a new device. Episode metadata is deliberately
/// excluded: it is re-fetched from the feeds, so only state that cannot be re-derived
/// travels. Settings and the equalizer are optional so an older backup (which carried
/// only the podcast library) still round-trips.
/// </summary>
public sealed record AudioLibrarySnapshot(
    IReadOnlyList<PodcastSubscription> Subscriptions,
    IReadOnlyList<string> ListenedEpisodeIds,
    IReadOnlyList<PlaybackProgress> Progress,
    PlaybackSettingsSnapshot? Settings = null,
    EqualizerLibrary? Equalizer = null,
    int LoudnessMillibels = 0)
{
    public static AudioLibrarySnapshot Empty { get; } = new([], [], []);

    /// <summary>Shows are the anchor: a device with none is a fresh one, the cue to restore.</summary>
    public bool HasSubscriptions => Subscriptions.Count > 0;

    public bool IsEmpty =>
        Subscriptions.Count == 0 && ListenedEpisodeIds.Count == 0 && Progress.Count == 0;

    /// <summary>Playback settings, defaulted so a null (older backup or fresh local) compares cleanly.</summary>
    private PlaybackSettingsSnapshot EffectiveSettings => Settings ?? PlaybackSettingsSnapshot.Empty();

    /// <summary>
    /// Value comparison of the actual state, order-insensitive, since the default record
    /// equality on the list/dictionary members is by reference. Used to skip a Drive write
    /// when the mirror already matches.
    /// </summary>
    public bool ContentEquals(AudioLibrarySnapshot other) =>
        Subscriptions
            .OrderBy(subscription => subscription.FeedUrl)
            .SequenceEqual(other.Subscriptions.OrderBy(subscription => subscription.FeedUrl))
        && SameSet(ListenedEpisodeIds, other.ListenedEpisodeIds)
        && Progress
            .OrderBy(entry => entry.MediaId)
            .SequenceEqual(other.Progress.OrderBy(entry => entry.MediaId))
        && SameSettings(EffectiveSettings, other.EffectiveSettings)
        && SameEqualizer(Equalizer, other.Equalizer)
        && LoudnessMillibels == other.LoudnessMillibels;

    private static bool SameSet<T>(IEnumerable<T> a, IEnumerable<T> b) =>
        a.OrderBy(item => item, Comparer<T>.Default)
            .SequenceEqual(b.OrderBy(item => item, Comparer<T>.Default));

    private static bool SameSettings(PlaybackSettingsSnapshot a, PlaybackSettingsSnapshot b)
    {
        if (!a.Default.Equals(b.Default) || a.PerFeed.Count != b.PerFeed.Count)
        {
            return false;
        }

        // PlaybackSettings is a record (value-equal on its scalar fields), so per-key equality is structural.
        return a.PerFeed.All(pair =>
            b.PerFeed.TryGetValue(pair.Key, out var value) && value.Equals(pair.Value));
    }

    private static bool SameEqualizer(EqualizerLibrary? a, EqualizerLibrary? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        return a.Active.Enabled == b.Active.Enabled
            && a.Active.BandGains.SequenceEqual(b.Active.BandGains)
            && SamePresets(a.Presets, b.Presets);
    }

    private static bool SamePresets(IReadOnlyList<EqualizerPreset> a, IReadOnlyList<EqualizerPreset> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        var ordered = a.OrderBy(preset => preset.Name).ToList();
        var otherOrdered = b.OrderBy(preset => preset.Name).ToList();
        return ordered.Zip(otherOrdered).All(pair =>
            pair.First.Name == pair.Second.Name
            && pair.First.BandGains.SequenceEqual(pair.Second.BandGains));
    }
}
