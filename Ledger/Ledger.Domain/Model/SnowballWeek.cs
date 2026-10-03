namespace Index.Domain.Model;

/// <summary>
/// Which of the sheet's weeks a given day belongs to (D78). The Snowball sheet's "Loans Over Time" tab
/// has one column per week, and every column of the imported history is dated on a FRIDAY — 70 of 70,
/// from 30 May 2025 to 25 Sep 2026 (<c>SnowballWeekTests</c> reads the fixture to prove it rather than
/// assume it). A week is therefore "the Friday on or before" the day in question: Friday through the
/// following Thursday are one week, and the next Friday begins another.
///
/// <para>This is the key <c>SnowballService.RecordCurrentWeekSnapshotAsync</c> dedupes on. It used to be
/// the day itself, so History gained a "week" for every day the app was opened, and nothing failed — each
/// row was valid and the unique index held; the chart just filled with days.</para>
///
/// <para>Pure and calendar-only: it takes the owner's LOCAL date (the caller reads it off the clock) and
/// knows nothing about time zones, so the same day always maps to the same Friday.</para>
/// </summary>
public static class SnowballWeek
{
    /// <summary>The Friday on or before <paramref name="day"/>: the day itself when it is a Friday,
    /// otherwise one to six days back.</summary>
    public static DateOnly Of(DateOnly day)
    {
        var daysSinceFriday = ((int)day.DayOfWeek - (int)DayOfWeek.Friday + 7) % 7;
        return day.AddDays(-daysSinceFriday);
    }
}
