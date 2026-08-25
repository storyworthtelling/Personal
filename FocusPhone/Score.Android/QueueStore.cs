using Android.Content;
using Codex.Domain.Audio;
using Codex.Infrastructure.Podcasts;

namespace Score;

/// <summary>
/// Persists a play queue — the ordered list of media ids the owner has lined up —
/// so a queue built by hand or by auto-arrange survives closing the app. A thin
/// <see cref="ISharedPreferences"/> adapter over <see cref="QueueCodec"/> (order
/// matters, so it is one JSON string, not a StringSet).
///
/// There are <b>two</b>, one per medium, in separate preference files. That is the
/// whole point: putting an album on must not cost the owner the run of episodes
/// they had assembled, and each queue has to still be there when they come back to
/// it. One store class rather than two because the shape is identical — only the
/// file name differs, and each is frozen, since renaming one orphans a queue the
/// owner built by hand.
///
/// Local only: a queue is re-derivable intent tied to what is on this device, so —
/// like the episode cache — it is deliberately excluded from the Drive backup.
/// </summary>
public class QueueStore
{
    private const string PodcastPreferencesName = "podcast-queue";
    private const string MusicPreferencesName = "music-queue";
    private const string QueueKey = "orderedEpisodeIds";
    private const string PinnedKey = "pinnedMediaIds";
    private const string ArrangementKey = "arrangement";

    private readonly ISharedPreferences preferences;

    private QueueStore(Context context, string preferencesName)
    {
        preferences = context.GetSharedPreferences(preferencesName, FileCreationMode.Private)!;
    }

    /// <summary>The run of episodes, which survives any amount of music.</summary>
    public static QueueStore ForPodcasts(Context context) => new(context, PodcastPreferencesName);

    /// <summary>The run of tracks, which survives any amount of podcast.</summary>
    public static QueueStore ForMusic(Context context) => new(context, MusicPreferencesName);

    public IReadOnlyList<string> Load() =>
        QueueCodec.Decode(preferences.GetString(QueueKey, null));

    public void Save(IReadOnlyList<string> ids) =>
        preferences.Edit()!.PutString(QueueKey, QueueCodec.Encode(ids))!.Apply();

    /// <summary>
    /// What the owner put in this queue by hand, in the order they did it. Kept apart
    /// from the queue itself because a standing arrangement rebuilds the queue and
    /// must not be able to throw these away.
    /// </summary>
    public IReadOnlyList<string> LoadPinned() =>
        QueueCodec.Decode(preferences.GetString(PinnedKey, null));

    public void SavePinned(IReadOnlyList<string> ids) =>
        preferences.Edit()!.PutString(PinnedKey, QueueCodec.Encode(ids))!.Apply();

    /// <summary>
    /// The standing rule that rebuilds this queue, or <see cref="QueueArrangement.Manual"/>
    /// for a queue that is only ever what was put in it. Music leaves this at Manual.
    /// </summary>
    public QueueArrangement LoadArrangement() =>
        (QueueArrangement)preferences.GetInt(ArrangementKey, (int)QueueArrangement.Manual);

    public void SaveArrangement(QueueArrangement arrangement) =>
        preferences.Edit()!.PutInt(ArrangementKey, (int)arrangement)!.Apply();
}
