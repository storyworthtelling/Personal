namespace Codex.Domain.Audio;

/// <summary>
/// The ordered play queue of media ids (podcast episodes or music tracks). It is
/// the user's intent; the player mirrors it at runtime. Ids are unique: adding a
/// duplicate is a no-op so an episode can't sit in the queue twice.
/// </summary>
public class PlaybackQueue
{
    private readonly List<string> mediaIds;

    public PlaybackQueue(IEnumerable<string>? mediaIds = null) =>
        this.mediaIds = mediaIds is null ? [] : Dedupe(mediaIds);

    public IReadOnlyList<string> Items => mediaIds;

    public int Count => mediaIds.Count;

    public void Add(string mediaId)
    {
        if (!mediaIds.Contains(mediaId))
        {
            mediaIds.Add(mediaId);
        }
    }

    public void Remove(string mediaId) => mediaIds.Remove(mediaId);

    public void Clear() => mediaIds.Clear();

    /// <summary>
    /// Moves the item at <paramref name="fromIndex"/> to <paramref name="toIndex"/>,
    /// shifting the rest. Out-of-range indices are clamped so a drag past the ends
    /// simply parks the item at the nearest edge.
    /// </summary>
    public void Move(int fromIndex, int toIndex)
    {
        if (mediaIds.Count == 0)
        {
            return;
        }

        var from = Math.Clamp(fromIndex, 0, mediaIds.Count - 1);
        var to = Math.Clamp(toIndex, 0, mediaIds.Count - 1);
        if (from == to)
        {
            return;
        }

        var item = mediaIds[from];
        mediaIds.RemoveAt(from);
        mediaIds.Insert(to, item);
    }

    /// <summary>
    /// Replaces the whole queue with a new order (e.g. the output of an
    /// <see cref="IEpisodeQueueStrategy"/>), de-duplicating while preserving order.
    /// </summary>
    public void ReplaceWith(IEnumerable<string> orderedMediaIds)
    {
        mediaIds.Clear();
        mediaIds.AddRange(Dedupe(orderedMediaIds));
    }

    public IReadOnlyList<string> Export() => [.. mediaIds];

    private static List<string> Dedupe(IEnumerable<string> ids)
    {
        var seen = new HashSet<string>();
        var ordered = new List<string>();
        foreach (var id in ids)
        {
            if (seen.Add(id))
            {
                ordered.Add(id);
            }
        }

        return ordered;
    }
}
