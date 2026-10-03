namespace Storyworthtelling.Maui.State;

/// <summary>
/// The functional colour a fold carries. Named by role rather than by hex so the decision stays
/// pure — the view resolves it against the palette.
/// </summary>
public enum DogEarHue
{
    Problem,
    Caution,
    Action,
    Resolved,

    /// <summary>Slate: something only worth knowing (mockup 17's Trial notice).</summary>
    Reference,

    /// <summary>Teal: waiting, not wrong (mockup 8's unreadable budget box) — the role Score's Playing
    /// area and Index's Snowball wear.</summary>
    Progress,
}

/// <summary>
/// One thing waiting behind the fold. The action is optional: most notices are things to know
/// rather than things to do.
///
/// <para><paramref name="At"/> is when the cause began — the date the Waiting page prints beside the
/// headline and orders newest-first by. It is optional and trails every older parameter, so a caller
/// written before it existed is unchanged and its notices simply print no date.</para>
/// </summary>
public sealed record DogEarNotice(
    string Headline, string Detail, DogEarHue Hue, string? ActionLabel = null, Action? OnAction = null,
    DateTimeOffset? At = null);

/// <summary>
/// What the corner shows right now. <see cref="Expires"/> is null for a notice, which is the whole
/// difference: a notice waits with no clock running.
/// </summary>
public sealed record DogEarFold(string Headline, DogEarHue Hue, bool ShowsBackArrow, DateTimeOffset? Expires);

/// <summary>
/// The dog-ear's decision, with no view in it (DESIGN.md §7, §13).
///
/// <para>The fold has two jobs and must never do both at once. As a <b>notice</b> it marks
/// something waiting — a failed send, a broken source — and you turn to it when ready. As an
/// <b>undo</b> it carries the back-turn arrow, reverses the act, and holds only for its window. So
/// an undo <b>outranks</b> anything merely waiting and takes the corner outright; the notices are
/// not discarded, and come back the moment the window closes.</para>
///
/// <para>Pulled out of the native view, where it was tangled with the host layout, a weak table of
/// pending notices and a Handler — which is why the rule above had never been tested. Time is
/// passed in rather than read, so the window's behaviour is decided here instead of by a timer.</para>
/// </summary>
public sealed class DogEarState
{
    // Keyed. Several parts of a screen raise notices independently, and a plain replace would mean
    // whichever spoke last silently erased the other. The order is NoticeOrder's: newest first by date,
    // and a notice stated again, unchanged, keeps its place.
    private readonly Dictionary<string, DogEarNotice> waiting = [];
    private readonly NoticeOrder order = new();

    private DogEarNotice? undoHint;
    private Action? onUndo;
    private DateTimeOffset undoExpires;

    /// <summary>
    /// Raised when what the Waiting page would show has changed: a notice arrived, changed its words,
    /// date or role, or was withdrawn. NOT raised when an owner states the same notice again, which
    /// screens do on every render — a page listening for this would otherwise redraw on each one.
    /// </summary>
    public event Action? NoticesChanged;

    /// <summary>
    /// Everything waiting, newest first by date (see <see cref="NoticeOrder"/>). The corner names one of
    /// them; this is the list.
    /// </summary>
    public IReadOnlyList<DogEarNotice> Notices => order.Keys.Select(key => waiting[key]).ToList();

    /// <summary>
    /// Sets or clears ONE owner's notice, leaving everyone else's alone. Passing null withdraws it.
    /// Safe to call on every render: an owner simply states what is wrong, or that nothing is.
    /// </summary>
    public void SetNotice(string key, DogEarNotice? notice)
    {
        if (notice is null)
        {
            waiting.Remove(key);
            if (order.Remove(key))
            {
                NoticesChanged?.Invoke();
            }

            return;
        }

        // The stored notice is always the latest one stated, so its action is current; whether anyone
        // is TOLD is the order's call, since a fresh lambda alone is not a change.
        waiting[key] = notice;
        var fingerprint = $"{notice.Headline}\u001f{notice.Detail}\u001f{notice.Hue}\u001f{notice.ActionLabel}";
        if (order.Set(key, notice.At, fingerprint))
        {
            NoticesChanged?.Invoke();
        }
    }

    /// <summary>Flattens the notice fold and forgets everything that was waiting.</summary>
    public void ClearNotices()
    {
        waiting.Clear();
        if (order.Clear())
        {
            NoticesChanged?.Invoke();
        }
    }

    /// <summary>
    /// Folds the corner for a destructive act. One at a time: a new act replaces a fold still
    /// showing, and the replaced act is <b>not</b> reversed — it simply becomes permanent, which is
    /// what would have happened had nobody turned it.
    /// </summary>
    public void BeginUndo(string hint, Action reverse, DateTimeOffset now, TimeSpan window)
    {
        undoHint = new DogEarNotice(hint, string.Empty, DogEarHue.Action);
        onUndo = reverse;
        undoExpires = now + window;
    }

    /// <summary>
    /// Turns the fold back. Returns whether anything was actually reversed — false for a notice
    /// fold, which has nothing to reverse, and false for an undo whose window has closed.
    /// </summary>
    public bool Turn(DateTimeOffset now)
    {
        if (!UndoIsOpen(now))
        {
            return false;
        }

        var reverse = onUndo;
        undoHint = null;
        onUndo = null;
        reverse?.Invoke();
        return true;
    }

    /// <summary>
    /// What the corner shows at <paramref name="now"/>, or null for a flat corner. One fold, no
    /// count (§13): it says something waits, not how much.
    /// </summary>
    public DogEarFold? Fold(DateTimeOffset now)
    {
        if (UndoIsOpen(now))
        {
            return new DogEarFold(undoHint!.Headline, undoHint.Hue, ShowsBackArrow: true, undoExpires);
        }

        if (order.Count == 0)
        {
            return null;
        }

        var newest = waiting[order.Keys[0]];
        return new DogEarFold(newest.Headline, newest.Hue, ShowsBackArrow: false, Expires: null);
    }

    private bool UndoIsOpen(DateTimeOffset now) => undoHint is not null && now < undoExpires;
}
