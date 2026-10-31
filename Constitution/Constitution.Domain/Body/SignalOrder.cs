namespace Constitution.Domain.Body;

/// <summary>The order Body lists its charts in: the honest news first.</summary>
public static class SignalOrder
{
    /// <summary>
    /// Worsening signals first (D70: a rising weight is worsening), each group by size of movement,
    /// biggest first. The rest follow, by size of movement too, so a steady signal is last. Signals that
    /// move equally keep the order they arrived in. Nothing is dropped.
    /// </summary>
    public static IReadOnlyList<SignalChange> Order(IEnumerable<SignalChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var all = changes.ToList();
        return all.Where(change => change.IsWorsening).OrderByDescending(change => change.Movement)
            .Concat(all.Where(change => !change.IsWorsening).OrderByDescending(change => change.Movement))
            .ToList();
    }
}
