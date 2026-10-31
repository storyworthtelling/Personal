using Codex.Domain.Design;

namespace Constitution.Domain.Notices;

/// <summary>
/// The decision behind Constitution's one dog-ear (book p.18; D77, D83; DESIGN.md §13), with no view,
/// no clock and no storage in it — time is passed in, as in the kit's <c>DogEarState</c>.
///
/// <para><b>One fold, two jobs, never both at once.</b> As a notice it wears the colour of the most
/// serious thing waiting (<see cref="NoticeSeriousness"/>: the newest on a tie) and holds with no
/// clock. As an undo it takes the corner outright in the Action pine, with the back-turn arrow, for
/// <see cref="UndoWindow"/> — outranking every notice, which are kept, not discarded, and handed the
/// corner back the moment the window closes.</para>
///
/// <para><b>A notice goes when its cause does.</b> A notice is keyed by what raised it. Raising the
/// same cause again changes nothing (screens state their notices on every render), and
/// <see cref="Resolve"/> removes it; nothing else does, and nothing ages out, because a notice that
/// outlives its cause is the failure this type exists to prevent and one that expires on a timer is
/// the same failure the other way round.</para>
/// </summary>
public sealed class NoticeBoard
{
    private sealed record Entry(Notice Notice, long Sequence);

    /// <summary>The role of an open undo: the Action pine (D77).</summary>
    public static FoldRole UndoRole => FoldRole.Action;

    /// <summary>How long an undo holds the corner: 6 s (D50; <c>DesignTokens.Motion.UndoWindowMs</c>).</summary>
    public static TimeSpan UndoWindow { get; } = TimeSpan.FromMilliseconds(DesignTokens.Motion.UndoWindowMs);

    private readonly Dictionary<string, Entry> waiting = [];
    private long next;

    private string? undoAct;
    private DateTimeOffset undoExpires;

    /// <summary>
    /// States that something is waiting, because of <paramref name="causeKey"/>. Idempotent per cause:
    /// stating the same cause again with the same kind returns false and moves nothing, so the notice
    /// keeps its date and its place. A cause that comes back as a different kind is a new notice
    /// (dated <paramref name="now"/>), since the old judgement no longer describes it.
    /// </summary>
    public bool Raise(NoticeKind kind, string causeKey, DateTimeOffset now)
    {
        ValidateKey(causeKey);

        if (waiting.TryGetValue(causeKey, out var existing) && existing.Notice.Kind == kind)
        {
            return false;
        }

        waiting[causeKey] = new Entry(new Notice(kind, causeKey, now), next++);
        return true;
    }

    /// <summary>
    /// The cause is gone, so the notice goes. Returns whether there was one waiting. An open undo is
    /// not touched: it belongs to an act, not to a cause.
    /// </summary>
    public bool Resolve(string causeKey)
    {
        ValidateKey(causeKey);
        return waiting.Remove(causeKey);
    }

    /// <summary>
    /// Folds the corner for a destructive act. One at a time: a new act replaces a fold still showing,
    /// and the replaced act is not reversed — it simply becomes permanent. The window runs from
    /// <paramref name="now"/>.
    /// </summary>
    public void BeginUndo(string actKey, DateTimeOffset now)
    {
        ValidateKey(actKey);
        undoAct = actKey;
        undoExpires = now + UndoWindow;
    }

    /// <summary>
    /// Turns the fold back. Returns the act key the caller must now reverse, or null when there was
    /// nothing to reverse: no undo open, or its window had already closed. An undo can be turned once.
    /// </summary>
    public string? TurnUndo(DateTimeOffset now)
    {
        if (!UndoIsOpen(now))
        {
            return null;
        }

        var act = undoAct;
        undoAct = null;
        return act;
    }

    /// <summary>
    /// What the corner shows at <paramref name="now"/>, or null for a flat corner. An open undo
    /// outranks every notice; otherwise the fold wears the most serious notice waiting, the newest on
    /// a tie — one fold, no count (§13).
    /// </summary>
    public NoticeFold? Fold(DateTimeOffset now)
    {
        if (UndoIsOpen(now))
        {
            return new NoticeFold(UndoRole, IsUndo: true, Kind: null, Expires: undoExpires);
        }

        var notices = Waiting();
        var index = NoticeSeriousness.MostSerious(notices, notice => notice.Role);
        if (index < 0)
        {
            return null;
        }

        var chosen = notices[index];
        return new NoticeFold(chosen.Role, IsUndo: false, chosen.Kind, Expires: null);
    }

    /// <summary>Everything waiting, newest first; notices raised at the same moment list the latest raised first.</summary>
    public IReadOnlyList<Notice> Waiting() =>
        waiting.Values
            .OrderByDescending(entry => entry.Notice.CreatedAt)
            .ThenByDescending(entry => entry.Sequence)
            .Select(entry => entry.Notice)
            .ToList();

    private bool UndoIsOpen(DateTimeOffset now) => undoAct is not null && now < undoExpires;

    private static void ValidateKey(string key) => ArgumentException.ThrowIfNullOrWhiteSpace(key);
}
