using Constitution.Domain.Notices;
using Constitution.Domain.Placement;

namespace Constitution.Domain.Workouts.Screens;

/// <summary>A skip whose window has closed: what to record and what to take off the calendar.</summary>
public sealed record SkipCommit(PendingSkip Skip)
{
    /// <summary>The ledger with this skip in it. A skip is a session that ended unticked, so it goes in as exactly that.</summary>
    public SkipLedger Record(SkipLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        var target = "skip-by-hand:" + Skip.Day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        return ledger.Settle(
            new SessionMarks(Skip.Person, Skip.Day, Skip.Kind, [target]),
            [new Constitution.Domain.Ink.InkResolution.Skipped(target)]);
    }
}

/// <summary>
/// Skip, with undo (C-5.4.4): the skip happens at once on the page and folds the shared dog-ear for six seconds. Nothing is
/// recorded and nothing leaves the calendar until the window closes; a turn of the fold inside it undoes the skip as though
/// it had never been asked. Time is passed in.
///
/// <para><b>A skip is let go of when it has been written down, not when its window closes</b> (S2). Closing the window moves it from
/// <see cref="Pending"/> to the skips still <see cref="Unrecorded"/>, and it stays on show and owed until the caller says
/// <see cref="Recorded"/>; a write that failed, or a tab that had not finished loading, therefore costs the skip a delay and not its
/// existence.</para>
/// </summary>
public sealed class SkipUndo
{
    private readonly List<PendingSkip> closed = [];

    /// <summary>The skip inside its undo window, if there is one.</summary>
    public PendingSkip? Pending { get; private set; }

    /// <summary>The skips whose window has closed and which have not been written down: still owed to the ledger, still shown skipped.</summary>
    public IReadOnlyList<SkipCommit> Unrecorded => closed.Select(skip => new SkipCommit(skip)).ToList();

    /// <summary>Every skip the tab shows as skipped before the ledger says so: those owed and the one in its window.</summary>
    public IReadOnlyList<PendingSkip> Shown => Pending is null ? [.. closed] : [.. closed, Pending];

    /// <summary>The skip has been written down and no longer needs to be shown or owed. Anything else is left as it was.</summary>
    public void Recorded(SkipCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        closed.RemoveAll(skip => skip.Day == commit.Skip.Day && string.Equals(skip.Person, commit.Skip.Person, StringComparison.OrdinalIgnoreCase));
    }

    public static string ActKey(DateOnly day) =>
        "workout-skip:" + day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Starts a skip and folds the corner. A skip already waiting becomes permanent, and is returned for the caller to commit.</summary>
    public SkipCommit? Begin(NoticeBoard board, string person, PlannedSession session, PlacedTime? onCalendar, PlacementKey? key, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(session);
        var replaced = Pending is null ? null : new SkipCommit(Pending);
        if (Pending is not null)
        {
            closed.Add(Pending);
        }

        Pending = new PendingSkip(person, session.Day, session.Session.Kind, session, onCalendar, key, now + NoticeBoard.UndoWindow);
        board.BeginUndo(ActKey(session.Day), now);
        return replaced;
    }

    /// <summary>The fold was turned and the board gave back this act key: the skip is undone. Returns it, or null when it was not ours.</summary>
    public PendingSkip? Reverse(string actKey)
    {
        if (Pending is not { } waiting || actKey != ActKey(waiting.Day))
        {
            return null;
        }

        Pending = null;
        return waiting;
    }

    /// <summary>The window has closed: the skip to commit, once, or null. It stays shown and in <see cref="Unrecorded"/> until <see cref="Recorded"/>.</summary>
    public SkipCommit? Due(DateTimeOffset now)
    {
        if (Pending is not { } waiting || now < waiting.Expires)
        {
            return null;
        }

        Pending = null;
        closed.Add(waiting);
        return new SkipCommit(waiting);
    }
}
