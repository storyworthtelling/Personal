namespace Index.Domain.Model;

/// <summary>
/// The owner's own order for the Accounts page (D123) — pure list arithmetic over
/// <see cref="Institution.SortOrder"/>, so the three moves the long-press action card offers (mockup
/// 2: Move up / Move down / Move to the bottom) are decided and tested away from the database.
/// <c>AccountService</c> reads the current order, asks this for the new one, and writes the returned
/// <c>SortOrder</c> values back — it is the only thing that touches EF Core.
///
/// <para>Every move renumbers the WHOLE list to a dense 0..N-1 sequence rather than nudging one
/// value, so gaps never accumulate and nothing needs a later compaction pass.</para>
/// </summary>
public static class InstitutionOrder
{
    /// <summary>Assigns dense, contiguous sort orders to a sequence, preserving its order.</summary>
    public static IReadOnlyList<(Guid Id, int SortOrder)> Renumber(IEnumerable<Guid> orderedIds) =>
        orderedIds.Select((id, index) => (id, index)).ToList();

    /// <summary>The alphabetical first-run seed (D123): no owner choice exists yet to preserve.</summary>
    public static IReadOnlyList<(Guid Id, int SortOrder)> SeedAlphabetically(
        IEnumerable<(Guid Id, string Name)> institutions) =>
        Renumber(institutions
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .Select(i => i.Id));

    public static IReadOnlyList<(Guid Id, int SortOrder)> MoveUp(IReadOnlyList<Guid> currentOrder, Guid id) =>
        Move(currentOrder, id, delta: -1);

    public static IReadOnlyList<(Guid Id, int SortOrder)> MoveDown(IReadOnlyList<Guid> currentOrder, Guid id) =>
        Move(currentOrder, id, delta: 1);

    /// <summary>Moves one institution to the end — the same action a "Disconnect" leaves nothing
    /// behind to undo, so the resulting list simply omits the id if it were removed elsewhere; this
    /// method only reorders, it never removes.</summary>
    public static IReadOnlyList<(Guid Id, int SortOrder)> MoveToBottom(IReadOnlyList<Guid> currentOrder, Guid id)
    {
        var list = currentOrder.ToList();
        var index = list.IndexOf(id);
        if (index < 0)
        {
            return Renumber(list);
        }

        list.RemoveAt(index);
        list.Add(id);
        return Renumber(list);
    }

    /// <summary>Where a brand-new institution joins (D123): the bottom of whatever order already
    /// exists, never re-alphabetized in with the rest.</summary>
    public static IReadOnlyList<(Guid Id, int SortOrder)> AppendNew(
        IReadOnlyList<Guid> currentOrder, Guid newId) =>
        Renumber(currentOrder.Append(newId));

    private static IReadOnlyList<(Guid Id, int SortOrder)> Move(IReadOnlyList<Guid> currentOrder, Guid id, int delta)
    {
        var list = currentOrder.ToList();
        var index = list.IndexOf(id);
        if (index < 0)
        {
            return Renumber(list);
        }

        var target = Math.Clamp(index + delta, 0, list.Count - 1);
        if (target == index)
        {
            return Renumber(list);
        }

        list.RemoveAt(index);
        list.Insert(target, id);
        return Renumber(list);
    }
}
