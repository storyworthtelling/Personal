using Android.Content;
using FocusPhone.Domain.Audio;
using FocusPhone.Infrastructure.Podcasts;

namespace FocusPhone.Audio;

/// <summary>
/// Persists the last-fetched episodes and feed titles in SharedPreferences, so the
/// shelf paints instantly from the previous run while feeds refresh in the background.
/// A thin adapter: the JSON (de)serialization lives in the unit-tested
/// <see cref="PodcastCacheCodec"/>.
/// </summary>
public class EpisodeCacheStore
{
    private const string PreferencesName = "podcastEpisodeCache";
    private const string CacheKey = "cache";

    private readonly ISharedPreferences preferences;

    public EpisodeCacheStore(Context context) =>
        preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;

    public PodcastCache Load() =>
        PodcastCacheCodec.Decode(preferences.GetString(CacheKey, null));

    public void Save(IReadOnlyList<Episode> episodes, IReadOnlyDictionary<string, string> feedTitles) =>
        preferences.Edit()!
            .PutString(CacheKey, PodcastCacheCodec.Encode(new PodcastCache(episodes, feedTitles)))!
            .Apply();
}
