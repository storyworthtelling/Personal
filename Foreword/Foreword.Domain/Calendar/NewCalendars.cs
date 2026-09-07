namespace Foreword.Domain.Calendar;

/// <summary>
/// Which calendars have turned up since the owner last looked at the list.
///
/// <para>There is deliberately <b>no refresh button</b> on the calendar screen: the list is re-read
/// every morning anyway, so a manual one would usually show the same list — the kind of button
/// Score's settings refuses on principle, because a button that usually does nothing teaches you to
/// press it for reassurance rather than for a reason.</para>
///
/// <para>What a new calendar gets instead is this: a group at the foot that needs nothing pressed.
/// Somebody shares a calendar with you and it is simply there the next morning, named, waiting to be
/// given a column.</para>
/// </summary>
public static class NewCalendars
{
    /// <summary>
    /// The ids present now that were not present when the list was last seen — in the order the
    /// calendars came back, so the group reads the way the list does.
    /// </summary>
    public static IReadOnlyList<string> Since(
        IEnumerable<string> present, IReadOnlySet<string> lastSeen) =>
        present.Where(id => !lastSeen.Contains(id)).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>
    /// What to remember for next time. The <b>present</b> list, not the union: a calendar that has
    /// gone away should count as new if it ever comes back, and remembering it forever would mean
    /// it never did.
    /// </summary>
    public static IReadOnlySet<string> Remember(IEnumerable<string> present) =>
        present.ToHashSet(StringComparer.Ordinal);
}
