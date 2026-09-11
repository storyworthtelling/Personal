using System.Globalization;

namespace Foreword.Domain.Document;

/// <summary>
/// What The Day is called on the tablet, and what yesterday's was called.
///
/// <para>The second half is the load-bearing one. The morning read-back finds yesterday's document
/// <b>by title</b> — downloads it, extracts the handwriting, files what it can read, and trashes
/// it. Merging two documents into one and giving the result a new name does not break that: it
/// makes it find nothing. Yesterday's calendar and checklist are still sitting on the tablet under
/// the names the old build gave them, so the handwriting on them is never read, the events written
/// on them are never filed, and they are never trashed. Nothing errors. It would simply be the one
/// morning the app forgot what it had asked the owner to write on.</para>
/// </summary>
public static class DayTitles
{
    /// <summary>Sortable, so the tablet's own list puts a week of them in order.</summary>
    public static string Document(DateOnly date) =>
        $"The Day {date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Every title yesterday's document might be under, newest naming first. The two old names stay
    /// here through the transition and cost nothing once the tablet has stopped carrying them: a
    /// lookup that finds nothing is a lookup that finds nothing.
    /// </summary>
    public static IReadOnlyList<string> ToReadBack(DateOnly date) =>
    [
        Document(date),
        $"Daily Calendar {date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}",
        $"Daily Checklist {date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}",
    ];

    /// <summary>
    /// The heading on page 1 — a different job from the file name. One is read by a person on a
    /// page and the other is sorted by a machine in a list.
    /// </summary>
    public static string PageHeading(DateOnly date) =>
        date.ToDateTime(TimeOnly.MinValue).ToString("dddd, MMMM d", CultureInfo.InvariantCulture);
}
