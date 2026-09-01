using System.Globalization;

namespace Codex.Domain.Music;

/// <summary>
/// Decides whether the weekly "New for you" picks are due — pure date logic, so the WorkManager worker
/// stays a thin trigger. Picks are due on Fridays (owner-local), at most once per ISO week: generating
/// earlier the same week (or already this Friday) means this week's picks are in hand.
/// </summary>
public static class FridayPicksSchedule
{
    public static bool IsDue(DateTimeOffset now, DateTimeOffset? lastGenerated)
    {
        if (now.DayOfWeek != DayOfWeek.Friday)
        {
            return false;
        }

        return lastGenerated is not { } last || !SameIsoWeek(last.DateTime, now.DateTime);
    }

    private static bool SameIsoWeek(DateTime a, DateTime b) =>
        ISOWeek.GetYear(a) == ISOWeek.GetYear(b) && ISOWeek.GetWeekOfYear(a) == ISOWeek.GetWeekOfYear(b);
}
