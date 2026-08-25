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
