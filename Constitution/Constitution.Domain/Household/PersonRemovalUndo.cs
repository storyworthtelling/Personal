using Constitution.Domain.Notices;

namespace Constitution.Domain.Household;

/// <summary>
/// Removing a person, undo-first (DESIGN.md section 5): the removal is written at once and folds the shared dog-ear for the undo
/// window; a turn of the fold inside it hands back what was taken, for the caller to write back the way it wrote the removal
/// (<see cref="RosterRemoval.Restore"/>). No confirm dialog. Time is passed in, and after the window the removal stands: the handle
/// is let go and a late turn gets nothing. A removal that begins while another waits makes the first permanent, as the board does
/// for any act.
/// </summary>
public sealed class PersonRemovalUndo
{
    private DateTimeOffset expires;

    /// <summary>The removal inside its undo window, if there is one.</summary>
    public RemovedPerson? Pending { get; private set; }

    public static string ActKey(PersonId id) => "person-remove:" + id.Value;

    public static string RemovedSentence(RemovedPerson removed)
    {
        ArgumentNullException.ThrowIfNull(removed);
        return $"{removed.Name} is off the roster. Turn the corner to bring them back.";
    }

    public static string RestoredSentence(RemovedPerson removed)
    {
        ArgumentNullException.ThrowIfNull(removed);
        return $"{removed.Name} is back on the roster.";
    }

    /// <summary>What the list says once the window has closed and the removal stands.</summary>
    public static string StandsSentence(RemovedPerson removed)
    {
        ArgumentNullException.ThrowIfNull(removed);
        return $"{removed.Name} was removed from the roster.";
    }

    /// <summary>Folds the corner for this removal. The removal itself has been, or is being, written by the caller.</summary>
    public void Begin(NoticeBoard board, RemovedPerson removed, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(removed);
        Pending = removed;
        expires = now + NoticeBoard.UndoWindow;
        board.BeginUndo(ActKey(removed.Id), now);
    }

    /// <summary>The fold was turned and the board gave back this act key: what to put back, or null when it was not ours or the window had closed.</summary>
    public RemovedPerson? Reverse(string actKey, DateTimeOffset now)
    {
        if (Pending is not { } waiting || actKey != ActKey(waiting.Id) || now >= expires)
        {
            return null;
        }

        Pending = null;
        return waiting;
    }

    /// <summary>The window has closed: the removal stands and the handle is let go. Returns whether there was one to let go.</summary>
    public bool Expire(DateTimeOffset now)
    {
        if (Pending is null || now < expires)
        {
            return false;
        }

        Pending = null;
        return true;
    }
}
