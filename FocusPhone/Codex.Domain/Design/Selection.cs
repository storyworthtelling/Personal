namespace Codex.Domain.Design;

/// <summary>
/// What a list's picker has picked. Backs the selection mode every Score list
/// enters from its <i>Select</i> quiet button: §7 checkboxes, §8's Selected wash on
/// the picked rows, and the pinned action bar.
///
/// Pure and stateful rather than a value type, because it is the screen's working
/// state and the view mutates it as taps arrive. Two rules do the real work here:
/// items keep the order they were <b>picked</b>, since that is the order the queue
/// receives them in; and the selection is about <b>items, not visibility</b>, so
/// changing the filter never silently unpicks anything.
/// </summary>
public sealed class Selection
{
    private readonly List<string> picked = [];

    public bool IsActive { get; private set; }

    public int Count => picked.Count;

    public IReadOnlyList<string> Items => picked;

    public bool Contains(string id) => picked.Contains(id);

    /// <summary>
    /// Enter selection mode with a first pick. On an already-active selection this
    /// is just another pick, so a long-press inside the picker cannot restart it and
    /// lose what is already chosen.
    /// </summary>
    public void Begin(string id)
    {
        IsActive = true;
        Toggle(id);
    }

    /// <summary>
    /// Enter selection mode with nothing picked — the <i>Select</i> button's door, as
    /// opposed to the long-press one. The list has to change shape before the first
    /// tap or there is nothing to tap, and an already-active selection is left
    /// exactly as it is.
    /// </summary>
    public void Begin() => IsActive = true;

    /// <summary>
    /// Pick or unpick. Re-picking moves the item to the end, because the pick order
    /// is the queue order and the last thing you tapped is the last thing you meant.
    /// </summary>
    public void Toggle(string id)
    {
        if (!picked.Remove(id))
        {
            picked.Add(id);
        }
    }

    /// <summary>
    /// The action bar's <i>All</i> control, over the rows currently on screen.
    ///
    /// Picks every visible row that is not picked yet; once they all are, the same
    /// control unpicks them, because a control that stops doing anything at the end
    /// of its own job is worse than one that does two things. Picks made elsewhere —
    /// under a different filter, on a different letter — are left alone either way,
    /// since <i>all</i> means what is on screen.
    ///
    /// Picking a whole list at once has no tap order to honour, so it takes the
    /// list's: an album picked with All still queues in album order.
    /// </summary>
    public void ToggleAll(IReadOnlyCollection<string> visible)
    {
        if (AllSelected(visible))
        {
            picked.RemoveAll(visible.Contains);
            return;
        }

        foreach (var id in visible.Where(id => !picked.Contains(id)))
        {
            picked.Add(id);
        }
    }

    /// <summary>
    /// Whether every visible row is picked — what tells the All control which half of
    /// itself it currently is. Nothing on screen is not "all of it".
    /// </summary>
    public bool AllSelected(IReadOnlyCollection<string> visible) =>
        visible.Count > 0 && visible.All(picked.Contains);

    /// <summary>
    /// Drop anything that no longer exists — unsubscribed, deleted, swept — while
    /// keeping everything that does, in pick order.
    ///
    /// This is deliberately <b>not</b> "retain what is visible": filtering to
    /// something else and back must not unpick. The caller passes what still exists
    /// in the library, not what is currently on screen.
    /// </summary>
    public void RetainKnown(IEnumerable<string> known)
    {
        var survivors = new HashSet<string>(known, StringComparer.Ordinal);
        picked.RemoveAll(id => !survivors.Contains(id));
    }

    /// <summary>Leave selection mode, discarding the picks.</summary>
    public void Cancel()
    {
        picked.Clear();
        IsActive = false;
    }
}
