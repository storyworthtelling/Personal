using System.Globalization;

namespace Foreword.Domain.Prayer;

/// <summary>
/// What <c>From before</c> puts back on the prayer page.
///
/// <para><b>It selects purely by date</b> — yesterday, a week ago, and this date in each past year.
/// Nothing reads the journal to decide what is worth resurfacing, and that is the whole design: the
/// calendar decides, so the page can have a memory without anything interpreting it, and the
/// archive can stay private because a date is not a reading.</para>
/// </summary>
public static class Resurfacing
{
    private const int AWeek = 7;

    public static IReadOnlyList<SurfacedEntry> From(
        IReadOnlyList<JournalEntry> archive, DateOnly today)
    {
        var yesterday = today.AddDays(-1);
        var aWeekAgo = today.AddDays(-AWeek);

        // Each entry is asked once which window it belongs to, so nothing can be printed twice and
        // two entries from one morning both come back, in the order they were written.
        var placed = new List<(int Rank, int Position, SurfacedEntry Entry)>();

        for (var i = 0; i < archive.Count; i++)
        {
            var entry = archive[i];

            if (Window(entry.Date, today, yesterday, aWeekAgo) is not { } window)
            {
                continue;
            }

            placed.Add((window.Rank, i, new SurfacedEntry(window.Label, entry.Text)));
        }

        return [.. placed
            .OrderBy(item => item.Rank)
            .ThenBy(item => item.Position)
            .Select(item => item.Entry)];
    }

    /// <summary>
    /// The one window a date falls in, or nothing. Past years are ranked newest first so the oldest
    /// entry lands at the bottom, where a reader expects it.
    /// </summary>
    private static (int Rank, string Label)? Window(
        DateOnly written, DateOnly today, DateOnly yesterday, DateOnly aWeekAgo)
    {
        if (written == yesterday)
        {
            return (0, "Yesterday");
        }

        if (written == aWeekAgo)
        {
            return (1, "A week ago");
        }

        // "This date" in a past year. Compared by month and day rather than by constructing last
        // year's date, because the 29th of February does not exist in most years and constructing
        // it throws — which would take the whole prayer page down on one morning in four.
        if (written.Year < today.Year && written.Month == today.Month && written.Day == today.Day)
        {
            return (
                2 + (today.Year - written.Year - 1),
                $"This date, {written.Year.ToString(CultureInfo.InvariantCulture)}");
        }

        return null;
    }
}
