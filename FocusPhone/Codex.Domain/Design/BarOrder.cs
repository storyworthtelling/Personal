namespace Codex.Domain.Design;

/// <summary>
/// Where each button on a pinned bar goes (DESIGN.md §7): the primary last, on the right, taking the width
/// that is left; the rest in the order given, sized to their words. With no primary the width is shared.
///
/// <para>Decided here rather than by each caller's argument order, because the native bars were written
/// both ways round and nothing could tell (audit W8.3b, report 14 §4).</para>
/// </summary>
public static class BarOrder
{
    /// <param name="isPrimary">One flag per button, in the order the caller gave them.</param>
    /// <returns>The indices in drawing order, and which one is widest (null when the width is shared).</returns>
    public static BarPlan Arrange(IReadOnlyList<bool> isPrimary)
    {
        var rest = Enumerable.Range(0, isPrimary.Count).Where(i => !isPrimary[i]);
        var primaries = Enumerable.Range(0, isPrimary.Count).Where(i => isPrimary[i]).ToList();
        int? widest = primaries.Count > 0 ? primaries[^1] : null;
        return new BarPlan([.. rest, .. primaries], widest);
    }
}

/// <summary>A bar's drawing order and its widest button.</summary>
public sealed record BarPlan(IReadOnlyList<int> Order, int? Widest);
