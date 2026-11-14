namespace Codex.Domain.Audio;

/// <summary>
/// The last-fetched podcast episodes and their feed titles, persisted between runs so
/// the shelf can paint instantly from the previous fetch while the network refresh
/// happens in the background — rather than opening on a blank "Fetching episodes…".
/// Unlike the Drive library backup, this is a purely local performance cache of state
/// that IS re-derivable from the feeds; it just saves the cold-start wait.
/// </summary>
public sealed record PodcastCache(
    IReadOnlyList<Episode> Episodes,
    IReadOnlyDictionary<string, string> FeedTitles)
{
    public static PodcastCache Empty { get; } = new([], new Dictionary<string, string>());

    /// <summary>Each feed's validators and when it was last asked, so the next ask is conditional and at most hourly
    /// (efficiency audit §9). Empty in a cache written before it existed.</summary>
    public IReadOnlyDictionary<string, FeedState> FeedStates { get; init; } = new Dictionary<string, FeedState>();
}

/// <summary>What a feed said last time: its ETag and Last-Modified, and when it was asked.</summary>
public sealed record FeedState(string? ETag, string? LastModified, DateTimeOffset FetchedAtUtc);

/// <summary>Whether a feed is due to be asked again: at most once an hour (efficiency audit §9).</summary>
public static class FeedRefresh
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    public static bool Due(FeedState? state, DateTimeOffset now) =>
        state is null || now < state.FetchedAtUtc || now - state.FetchedAtUtc >= Interval;
}
