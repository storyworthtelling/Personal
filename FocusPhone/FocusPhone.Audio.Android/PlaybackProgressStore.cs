using Android.Content;
using Codex.Domain.Audio;
using Codex.Infrastructure.Podcasts;

namespace FocusPhone.Audio;

/// <summary>
/// Persists per-episode playback positions (the resume book) in SharedPreferences.
/// A thin adapter: the JSON (de)serialization lives in the unit-tested
/// <see cref="PodcastProgressCodec"/>.
/// </summary>
public class PlaybackProgressStore
{
    private const string PreferencesName = "podcastProgress";
    private const string ProgressKey = "progress";

    private readonly ISharedPreferences preferences;

    public PlaybackProgressStore(Context context) =>
        preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;

    public IReadOnlyList<PlaybackProgress> Load() =>
        PodcastProgressCodec.Decode(preferences.GetString(ProgressKey, null));

    public void Save(IReadOnlyCollection<PlaybackProgress> progress) =>
        preferences.Edit()!.PutString(ProgressKey, PodcastProgressCodec.Encode(progress))!.Apply();
}
