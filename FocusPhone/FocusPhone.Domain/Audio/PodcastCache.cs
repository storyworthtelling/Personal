namespace FocusPhone.Domain.Audio;

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
}
