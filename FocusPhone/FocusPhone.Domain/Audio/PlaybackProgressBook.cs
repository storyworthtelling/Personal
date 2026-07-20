namespace FocusPhone.Domain.Audio;

/// <summary>
/// Remembers where playback stopped for every media item, so an episode or track
/// resumes exactly where it was left. Pure state, unit-testable; the playback
/// service records positions into it and reads a resume point back out.
/// </summary>
public class PlaybackProgressBook
{
    private readonly Dictionary<string, PlaybackProgress> progress;

    public PlaybackProgressBook(IEnumerable<PlaybackProgress>? saved = null) =>
        progress = saved is null
            ? []
            : saved.ToDictionary(entry => entry.MediaId);

    public void Record(string mediaId, TimeSpan position, TimeSpan? duration, DateTimeOffset updatedUtc) =>
        progress[mediaId] = new PlaybackProgress(mediaId, position, duration, updatedUtc);

    /// <summary>
    /// The raw position to resume at, or null when the item is unknown or already
    /// finished (finished items should start over, not resume near the end).
    /// </summary>
    public TimeSpan? Resume(string mediaId) =>
        progress.TryGetValue(mediaId, out var entry) && !entry.IsComplete
            ? entry.Position
            : null;

    /// <summary>
    /// The resume position with the catch-up rewind already applied, based on how
    /// long ago the item was last played. Never returns a negative position.
    /// </summary>
    public TimeSpan? ResumeWithRewind(string mediaId, DateTimeOffset now)
    {
        if (!progress.TryGetValue(mediaId, out var entry) || entry.IsComplete)
        {
            return null;
        }

        var resumed = entry.Position - SmartRewind.Amount(now - entry.UpdatedUtc);
        return resumed < TimeSpan.Zero ? TimeSpan.Zero : resumed;
    }

    public bool IsComplete(string mediaId) =>
        progress.TryGetValue(mediaId, out var entry) && entry.IsComplete;

    /// <summary>
    /// Migrates the legacy binary listened ids into finished progress entries so
    /// no listened state is lost when upgrading from the old model.
    /// </summary>
    public void ImportListened(IEnumerable<string> listenedIds, DateTimeOffset at)
    {
        foreach (var id in listenedIds)
        {
            if (!progress.ContainsKey(id))
            {
                // A nominal 1s position at a 1s duration reads as complete (position
                // is past duration - 30s) with a positive, non-zero duration.
                var marker = TimeSpan.FromSeconds(1);
                progress[id] = new PlaybackProgress(id, marker, marker, at);
            }
        }
    }

    public IReadOnlyCollection<PlaybackProgress> Export() => [.. progress.Values];
}
