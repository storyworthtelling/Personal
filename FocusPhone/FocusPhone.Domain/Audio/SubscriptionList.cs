namespace FocusPhone.Domain.Audio;

/// <summary>
/// The user's ordered subscriptions, with add / undo-able remove / reorder — none
/// of which the old URL-set store allowed. List position is the source of truth;
/// <see cref="PodcastSubscription.SortOrder"/> only carries that order across
/// export/restore.
/// </summary>
public class SubscriptionList
{
    private readonly List<PodcastSubscription> subscriptions;

    public SubscriptionList(IEnumerable<PodcastSubscription>? saved = null) =>
        subscriptions = saved is null ? [] : [.. saved.OrderBy(subscription => subscription.SortOrder)];

    public IReadOnlyList<PodcastSubscription> Ordered => subscriptions;

    /// <summary>Adds a show; a feed URL already present is a no-op (dedupe by URL).</summary>
    public void Add(string feedUrl, string title, DateTimeOffset addedUtc)
    {
        if (subscriptions.Any(subscription => subscription.FeedUrl == feedUrl))
        {
            return;
        }

        subscriptions.Add(new PodcastSubscription(feedUrl, title, addedUtc, subscriptions.Count));
    }

    public void Remove(string feedUrl) =>
        subscriptions.RemoveAll(subscription => subscription.FeedUrl == feedUrl);

    public void Move(int fromIndex, int toIndex)
    {
        if (subscriptions.Count == 0)
        {
            return;
        }

        var from = Math.Clamp(fromIndex, 0, subscriptions.Count - 1);
        var to = Math.Clamp(toIndex, 0, subscriptions.Count - 1);
        if (from == to)
        {
            return;
        }

        var moved = subscriptions[from];
        subscriptions.RemoveAt(from);
        subscriptions.Insert(to, moved);
    }

    /// <summary>
    /// The shows worth showing: those with at least one unplayed episode. This is
    /// the hide-fully-read behaviour the user relies on — finished shows drop out
    /// of the list automatically.
    /// </summary>
    public IReadOnlyList<PodcastSubscription> WithUnplayed(IReadOnlyDictionary<string, int> unplayedByFeed) =>
        [.. subscriptions.Where(subscription =>
            unplayedByFeed.TryGetValue(subscription.FeedUrl, out var count) && count > 0)];

    public IReadOnlyList<PodcastSubscription> Export() =>
        [.. subscriptions.Select((subscription, index) => subscription with { SortOrder = index })];
}
