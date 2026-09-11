using Foreword.Domain.Brief;
using Foreword.Domain.Interfaces;
using Foreword.Infrastructure.Calendar;

namespace Foreword.Android;

/// <summary>
/// What the Sources screen edits: how many stories the paper contributes, and the feed list.
///
/// <para>Written through the same codec the calendar and task screens use, so the stored shape is
/// decided in one place. There is no Save (D35) — every change commits as it is made.</para>
/// </summary>
public sealed class BriefSettings : IBriefPreferences
{
    private List<Feed> feeds = [];

    public void Reload() =>
        feeds = CalendarMappingCodec.Read<List<Feed>>(CredentialStore.FeedsJson) ?? [];

    public IReadOnlyList<Feed> Feeds
    {
        get
        {
            // Read on demand, because the delivery worker holds this for the length of a morning
            // and the owner may have edited the list from the screen in between.
            Reload();
            return feeds;
        }
    }

    public int SeenComic
    {
        get => CredentialStore.SeenComic;
        set => CredentialStore.SeenComic = value;
    }

    /// <summary>
    /// The reading profile. Empty until the Drive-backed profile lands — an empty profile changes
    /// nothing, which is the right behaviour on a phone with no evidence yet rather than a reason to
    /// hold the feature back.
    /// </summary>
    public ReadingTaste Taste =>
        CalendarMappingCodec.Read<ReadingTaste>(CredentialStore.TasteJson) ?? ReadingTaste.Empty;

    public void FeedsAfterAMorning(IReadOnlyCollection<string> failed, DateOnly today)
    {
        Reload();
        AfterAMorning(failed, today);
    }

    public int NewsCount
    {
        get => CredentialStore.NewsCount;
        set => CredentialStore.NewsCount = value;
    }

    public int OpinionCount
    {
        get => CredentialStore.OpinionCount;
        set => CredentialStore.OpinionCount = value;
    }

    public void AddFeed(string url)
    {
        if (feeds.Any(feed => string.Equals(feed.Url, url, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        // The name is the address until the first fetch replaces it with the feed's own title —
        // asking for a title the feed already carries is asking the owner to do the app's typing.
        feeds.Add(new Feed { Name = url, Url = url });
        Write();
    }

    public void SetCap(string url, int cap) => Update(url, feed => feed with { Cap = cap });

    public void Follow(string url) => Update(url, feed => feed.Followed());

    /// <summary>
    /// The only thing that removes a feed. Nothing else on the screen does, however long it fails —
    /// a feed disappearing on its own is indistinguishable from one the owner forgot they added.
    /// </summary>
    public void Remove(string url)
    {
        feeds.RemoveAll(feed => string.Equals(feed.Url, url, StringComparison.OrdinalIgnoreCase));
        Write();
    }

    /// <summary>Records what a morning's fetching found, marking failures and clearing recoveries.</summary>
    public void AfterAMorning(IReadOnlyCollection<string> failed, DateOnly today)
    {
        feeds = [.. FeedList.AfterAMorning(feeds, failed, today)];
        Write();
    }

    private void Update(string url, Func<Feed, Feed> change)
    {
        var index = feeds.FindIndex(feed =>
            string.Equals(feed.Url, url, StringComparison.OrdinalIgnoreCase));

        if (index < 0)
        {
            return;
        }

        feeds[index] = change(feeds[index]);
        Write();
    }

    private void Write() => CredentialStore.FeedsJson = CalendarMappingCodec.Write(feeds);
}
