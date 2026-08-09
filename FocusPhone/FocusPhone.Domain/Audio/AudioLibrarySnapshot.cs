namespace FocusPhone.Domain.Audio;

/// <summary>
/// The syncable slice of podcast state — the shows you follow, what you've marked
/// played, and where each episode was left off — captured as one plain value so it
/// can be mirrored to the owner's Drive for backup/restore across a factory reset or
/// a new device. Episode metadata is deliberately excluded: it is re-fetched from the
/// feeds, so only state that cannot be re-derived travels.
/// </summary>
public sealed record AudioLibrarySnapshot(
    IReadOnlyList<PodcastSubscription> Subscriptions,
    IReadOnlyList<string> ListenedEpisodeIds,
    IReadOnlyList<PlaybackProgress> Progress)
{
    public static AudioLibrarySnapshot Empty { get; } = new([], [], []);

    /// <summary>Shows are the anchor: a device with none is a fresh one, the cue to restore.</summary>
    public bool HasSubscriptions => Subscriptions.Count > 0;

    public bool IsEmpty =>
        Subscriptions.Count == 0 && ListenedEpisodeIds.Count == 0 && Progress.Count == 0;

    /// <summary>
    /// Value comparison of the actual state, order-insensitive, since the default
    /// record equality on the list members is by reference. Used to skip a Drive
    /// write when the mirror already matches — subscriptions and progress are records
    /// (value-equal on their fields), so only ordering has to be normalized away.
    /// </summary>
    public bool ContentEquals(AudioLibrarySnapshot other)
    {
        static bool SameSet<T>(IEnumerable<T> a, IEnumerable<T> b) =>
            a.OrderBy(item => item, Comparer<T>.Default).SequenceEqual(
                b.OrderBy(item => item, Comparer<T>.Default));

        return Subscriptions
                .OrderBy(subscription => subscription.FeedUrl)
                .SequenceEqual(other.Subscriptions.OrderBy(subscription => subscription.FeedUrl))
            && SameSet(ListenedEpisodeIds, other.ListenedEpisodeIds)
            && Progress
                .OrderBy(entry => entry.MediaId)
                .SequenceEqual(other.Progress.OrderBy(entry => entry.MediaId));
    }
}
