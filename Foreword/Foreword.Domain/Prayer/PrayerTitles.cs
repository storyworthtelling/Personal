using System.Globalization;

namespace Foreword.Domain.Prayer;

/// <summary>
/// What the prayer document is called on the tablet, and what yesterday's was called.
///
/// <para>The second half is load-bearing for the same reason The Day's is: the morning finds
/// yesterday's sheet <b>by title</b>, transcribes the handwriting on it, and then trashes it. A
/// title that does not match finds nothing, and nothing errors — it is simply the morning the app
/// forgot what it had asked the owner to write down. Here that costs more than an event: the sheet
/// is the only copy until it has been read.</para>
/// </summary>
public static class PrayerTitles
{
    /// <summary>Sortable, so the tablet's own list puts a week of them in order.</summary>
    public static string Document(DateOnly date) =>
        $"Prayer {date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Every title yesterday's sheet might be under. One, for now — this document is new in P9, so
    /// there is no older naming to carry. The list is a list anyway, because the day there is one it
    /// must not be an afterthought.
    /// </summary>
    public static IReadOnlyList<string> ToReadBack(DateOnly date) => [Document(date)];
}
