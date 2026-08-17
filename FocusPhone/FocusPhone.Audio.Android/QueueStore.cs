using Android.Content;
using Codex.Infrastructure.Podcasts;

namespace FocusPhone.Audio;

/// <summary>
/// Persists the podcast play queue — the ordered list of episode ids the user has
/// lined up — so a queue built by hand or by auto-arrange survives closing the app.
/// A thin <see cref="ISharedPreferences"/> adapter over <see cref="QueueCodec"/>
/// (order matters, so it is one JSON string, not a StringSet). Local only: the
/// queue is re-derivable intent tied to the local feeds, so — like the episode
/// cache — it is deliberately excluded from the Drive backup.
/// </summary>
public class QueueStore
{
    private const string PreferencesName = "podcast-queue";
    private const string QueueKey = "orderedEpisodeIds";

    private readonly ISharedPreferences preferences;

    public QueueStore(Context context)
    {
        preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;
    }

    public IReadOnlyList<string> Load() =>
        QueueCodec.Decode(preferences.GetString(QueueKey, null));

    public void Save(IReadOnlyList<string> ids) =>
        preferences.Edit()!.PutString(QueueKey, QueueCodec.Encode(ids))!.Apply();
}
