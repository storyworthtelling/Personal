namespace Codex.Domain.Audio;

/// <summary>
/// Turns a parsed OPML export into a checklist the user confirms before anything is
/// subscribed — the fix for the old "import silently adds everything" dead-end. New
/// shows are pre-checked; shows already subscribed are shown but unchecked so a
/// re-import doesn't nag. <see cref="Apply"/> folds the chosen feeds into a fresh
/// <see cref="SubscriptionList"/> without mutating the current one. Pure — no I/O.
/// </summary>
public class OpmlImportSelection
{
    private readonly SubscriptionList current;

    public OpmlImportSelection(IReadOnlyList<OpmlSubscription> imported, SubscriptionList current)
    {
        this.current = current;
        var subscribed = new HashSet<string>(
            current.Ordered.Select(subscription => subscription.FeedUrl));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<Item>();
        foreach (var subscription in imported)
        {
            if (!seen.Add(subscription.FeedUrl))
            {
                continue;
            }

            var already = subscribed.Contains(subscription.FeedUrl);
            items.Add(new Item(subscription.Title, subscription.FeedUrl, already, Checked: !already));
        }

        Items = items;
    }

    /// <summary>One row in the import checklist.</summary>
    /// <param name="AlreadySubscribed">The user is already subscribed to this feed.</param>
    /// <param name="Checked">Whether the row is selected by default (new shows are).</param>
    public record Item(string Title, string FeedUrl, bool AlreadySubscribed, bool Checked);

    public IReadOnlyList<Item> Items { get; }

    /// <summary>
    /// Returns a new list = the current subscriptions plus the selected feeds, in OPML
    /// order. Selecting an already-subscribed feed is a no-op (dedupe by URL).
    /// </summary>
    public SubscriptionList Apply(IEnumerable<string> selectedFeedUrls, DateTimeOffset addedUtc)
    {
        var selected = new HashSet<string>(selectedFeedUrls);
        var result = new SubscriptionList(current.Export());
        foreach (var item in Items)
        {
            if (selected.Contains(item.FeedUrl))
            {
                result.Add(item.FeedUrl, item.Title, addedUtc);
            }
        }

        return result;
    }
}
