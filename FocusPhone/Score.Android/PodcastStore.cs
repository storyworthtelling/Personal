using Android.Content;

namespace Score;

/// <summary>
/// Persists podcast subscriptions (feed URLs) and listened episode ids.
/// Episode metadata itself is fetched fresh from the feeds; only state that
/// cannot be re-derived lives here.
/// </summary>
public class PodcastStore
{
    private const string PreferencesName = "podcasts";
    private const string FeedsKey = "feedUrls";
    private const string ListenedKey = "listenedEpisodeIds";

    private readonly ISharedPreferences preferences;

    public PodcastStore(Context context)
    {
        preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;
    }

    public IReadOnlyCollection<string> FeedUrls =>
        preferences.GetStringSet(FeedsKey, [])!.ToList();

    public void AddFeed(string url)
    {
        var feeds = preferences.GetStringSet(FeedsKey, [])!.ToHashSet();
        feeds.Add(url);
        preferences.Edit()!.PutStringSet(FeedsKey, feeds)!.Apply();
    }

    public IReadOnlyCollection<string> ListenedIds =>
        preferences.GetStringSet(ListenedKey, [])!.ToList();

    public void SaveListenedIds(IReadOnlyCollection<string> ids) =>
        preferences.Edit()!.PutStringSet(ListenedKey, ids.ToList())!.Apply();
}
