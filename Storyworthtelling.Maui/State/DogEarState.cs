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
}

/// <summary>
/// One thing waiting behind the fold. The action is optional: most notices are things to know
/// rather than things to do.
/// </summary>
public sealed record DogEarNotice(
    string Headline, string Detail, DogEarHue Hue, string? ActionLabel = null, Action? OnAction = null);

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
    // Keyed, and newest first. Several parts of a screen raise notices independently, and a plain
    // replace would mean whichever spoke last silently erased the other.
    private readonly List<(string Key, DogEarNotice Notice)> waiting = [];

    private DogEarNotice? undoHint;
    private Action? onUndo;
    private DateTimeOffset undoExpires;

    /// <summary>Everything waiting, newest first. The corner names one of them; this is the list.</summary>
    public IReadOnlyList<DogEarNotice> Notices => waiting.Select(entry => entry.Notice).ToList();

    /// <summary>
    /// Sets or clears ONE owner's notice, leaving everyone else's alone. Passing null withdraws it.
    /// Safe to call on every render: an owner simply states what is wrong, or that nothing is.
    /// </summary>
    public void SetNotice(string key, DogEarNotice? notice)
    {
        waiting.RemoveAll(entry => entry.Key == key);
        if (notice is not null)
        {
            waiting.Insert(0, (key, notice));
        }
    }

    /// <summary>Flattens the notice fold and forgets everything that was waiting.</summary>
    public void ClearNotices() => waiting.Clear();

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

        return waiting.Count == 0
            ? null
            : new DogEarFold(waiting[0].Notice.Headline, waiting[0].Notice.Hue, ShowsBackArrow: false, Expires: null);
    }

    private bool UndoIsOpen(DateTimeOffset now) => undoHint is not null && now < undoExpires;
}
