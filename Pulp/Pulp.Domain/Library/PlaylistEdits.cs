namespace Pulp.Domain.Library;

/// <summary>
/// Taking one entry off a playlist, undo-first (§13). It is applied to the list as it stands when the
/// write lands and remembers where the entry stood then, so its <see cref="Undo"/> is the inverse edit:
/// the entry goes back at that place in whatever the list has become — keeping a move or an addition
/// made while the dog-ear was up — rather than a copy of the list from before being written back.
/// One instance is one removal and its one undo, applied in that order on one write queue.
/// </summary>
public sealed class EntryRemoval
{
    private int? stood;

    public EntryRemoval(PlaylistEntry entry) =>
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));

    public PlaylistEntry Entry { get; }

    public Playlist Apply(Playlist current)
    {
        ArgumentNullException.ThrowIfNull(current);
        var at = PlaylistEdits.IndexOf(current, Entry);
        stood = at < 0 ? null : at;
        return at < 0 ? current : current.Remove(Entry);
    }

    /// <summary>
    /// Puts the entry back where it stood, or at the bottom when the list has since shrunk past that
    /// place or the removal found it already gone (the box was ticked, so its undo leaves it on). An
    /// entry already back on the list stays where it is.
    /// </summary>
    public Playlist Undo(Playlist current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (PlaylistEdits.IndexOf(current, Entry) >= 0)
        {
            return current;
        }

        var added = current.Add(Entry);
        var place = Math.Min(stood ?? added.Entries.Count - 1, added.Entries.Count - 1);
        return added.Move(Entry, place);
    }
}

/// <summary>
/// Adding one entry to a playlist, with its inverse: the undo takes the entry off only when this
/// addition is what put it there, so undoing an add of something already on the list leaves it.
/// </summary>
public sealed class EntryAddition
{
    private bool added;

    public EntryAddition(PlaylistEntry entry) =>
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));

    public PlaylistEntry Entry { get; }

    public Playlist Apply(Playlist current)
    {
        ArgumentNullException.ThrowIfNull(current);
        added = PlaylistEdits.IndexOf(current, Entry) < 0;
        return current.Add(Entry);
    }

    public Playlist Undo(Playlist current)
    {
        ArgumentNullException.ThrowIfNull(current);
        return added ? current.Remove(Entry) : current;
    }
}

/// <summary>Edits that are applied to the list as it stands when they land, so they never throw over a list that moved meanwhile.</summary>
public static class PlaylistEdits
{
    /// <summary>
    /// A hand-order move (D103) applied to the current list: an entry no longer on it, or a move that
    /// no longer changes anything there, leaves the list as it is.
    /// </summary>
    public static Playlist Move(Playlist current, PlaylistEntry entry, PlaylistMove move)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(entry);
        return IndexOf(current, entry) >= 0 && PlaylistPageMoves.Offered(current, entry).Contains(move)
            ? PlaylistPageMoves.Apply(current, entry, move)
            : current;
    }

    internal static int IndexOf(Playlist playlist, PlaylistEntry entry)
    {
        for (var i = 0; i < playlist.Entries.Count; i++)
        {
            if (playlist.Entries[i] == entry)
            {
                return i;
            }
        }

        return -1;
    }
}
