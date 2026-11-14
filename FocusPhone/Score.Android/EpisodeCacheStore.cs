using Android.Content;
using Codex.Domain.Audio;
using Codex.Infrastructure.Podcasts;

namespace Score;

/// <summary>
/// Persists the last-fetched episodes, feed titles and each feed's validators, so the shelf paints instantly from
/// the previous run while feeds refresh in the background. A thin adapter: the JSON (de)serialization lives in the
/// unit-tested <see cref="PodcastCacheCodec"/>.
///
/// <para>In a file of its own (efficiency audit §9): show notes and all, the cache was one SharedPreferences string,
/// loaded whole with the preferences and rewritten whole as XML on every save. A cache still in the preferences is
/// read once and moved across; the preferences file keeps its frozen name.</para>
/// </summary>
public class EpisodeCacheStore
{
    private const string PreferencesName = "podcastEpisodeCache";
    private const string CacheKey = "cache";
    private const string FileName = "podcast-episodes.json";

    private readonly ISharedPreferences preferences;
    private readonly string path;

    public EpisodeCacheStore(Context context)
    {
        preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;
        path = Path.Combine(context.FilesDir!.AbsolutePath, FileName);
    }

    public PodcastCache Load()
    {
        if (File.Exists(path))
        {
            return PodcastCacheCodec.Decode(File.ReadAllText(path));
        }

        var legacy = preferences.GetString(CacheKey, null);
        var cache = PodcastCacheCodec.Decode(legacy);
        if (legacy is not null)
        {
            Write(cache);
            preferences.Edit()!.Remove(CacheKey)!.Apply();
        }
        return cache;
    }

    public void Save(IReadOnlyList<Episode> episodes, IReadOnlyDictionary<string, string> feedTitles,
        IReadOnlyDictionary<string, FeedState> feedStates) =>
        Write(new PodcastCache(episodes, feedTitles) { FeedStates = feedStates });

    // Written beside and moved over, so a process killed mid-write leaves the last good cache.
    private void Write(PodcastCache cache)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, PodcastCacheCodec.Encode(cache));
        File.Move(temporary, path, overwrite: true);
    }
}
