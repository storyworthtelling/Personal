namespace Codex.Domain.Audio;

/// <summary>
/// A queue about to be handed to the player: the items that can actually play, and
/// where in them to start.
///
/// It exists because the two steps have to happen in this order and are easy to get
/// wrong the other way round. Pruning after taking the index hands the player a
/// shorter list than the index was computed against, which starts the wrong track —
/// a bug that looks like a player fault rather than a queue one.
///
/// A miss is not an error: the rest of the queue is still good, so the pruned list
/// comes back either way and only <see cref="Found"/> says whether the thing you
/// asked for is in it.
/// </summary>
public readonly record struct QueueStart(IReadOnlyList<string> Ids, int Index)
{
    /// <summary>Whether the chosen item survived pruning and is in the queue.</summary>
    public bool Found => Index >= 0;

    /// <summary>
    /// Prunes <paramref name="queued"/> to what <paramref name="isPlayable"/> accepts,
    /// preserving order and dropping duplicates, then locates
    /// <paramref name="startId"/> in the result.
    /// </summary>
    public static QueueStart Locate(
        IEnumerable<string> queued, Func<string, bool> isPlayable, string startId)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var playable = new List<string>();
        foreach (var id in queued)
        {
            if (isPlayable(id) && seen.Add(id))
            {
                playable.Add(id);
            }
        }

        return new QueueStart(playable, playable.IndexOf(startId));
    }
}
