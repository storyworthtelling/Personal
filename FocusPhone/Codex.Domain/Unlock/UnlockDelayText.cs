using System.Globalization;
using System.Text.RegularExpressions;

namespace Codex.Domain.Unlock;

/// <summary>
/// Reads and writes the admin unlock delay as the owner types it: a whole number and a unit, such as
/// "1 hour", "10 days", "2 weeks" or "25 months". A bare number means hours. A month is taken as 30 days,
/// because the delay is a countdown of elapsed time, not a calendar span. Whether the value is within
/// the allowed range is decided by <see cref="UnlockStateMachine.SetDelay"/>, not here.
/// </summary>
public static class UnlockDelayText
{
    private const double HoursPerDay = 24;
    private const double HoursPerMonth = 30 * HoursPerDay;

    // Far above the allowed maximum; keeps the arithmetic below from overflowing a TimeSpan.
    private const double MaximumParsableHours = 10_000_000;

    private static readonly Regex Shape = new(@"^(\d+)\s*([a-z]*)$", RegexOptions.Compiled);

    private static readonly Dictionary<string, double> HoursPerUnit = new(StringComparer.Ordinal)
    {
        [""] = 1, ["h"] = 1, ["hr"] = 1, ["hrs"] = 1, ["hour"] = 1, ["hours"] = 1,
        ["d"] = HoursPerDay, ["day"] = HoursPerDay, ["days"] = HoursPerDay,
        ["w"] = 7 * HoursPerDay, ["wk"] = 7 * HoursPerDay, ["wks"] = 7 * HoursPerDay,
        ["week"] = 7 * HoursPerDay, ["weeks"] = 7 * HoursPerDay,
        ["mo"] = HoursPerMonth, ["mos"] = HoursPerMonth, ["month"] = HoursPerMonth, ["months"] = HoursPerMonth,
    };

    public static bool TryParse(string? text, out TimeSpan delay)
    {
        delay = TimeSpan.Zero;
        var match = Shape.Match((text ?? string.Empty).Trim().ToLowerInvariant());
        if (!match.Success
            || !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var amount)
            || !HoursPerUnit.TryGetValue(match.Groups[2].Value, out var hoursPerUnit))
        {
            return false;
        }

        var hours = amount * hoursPerUnit;
        if (hours <= 0 || hours > MaximumParsableHours)
        {
            return false;
        }
        delay = TimeSpan.FromHours(hours);
        return true;
    }

    /// <summary>The delay in the largest unit that holds it exactly, e.g. "10 days" or "25 months".</summary>
    public static string Format(TimeSpan delay)
    {
        var hours = (long)delay.TotalHours;
        if (hours % (long)HoursPerMonth == 0 && hours >= (long)HoursPerMonth)
        {
            return Plural(hours / (long)HoursPerMonth, "month");
        }
        if (hours % (long)HoursPerDay == 0 && hours >= (long)HoursPerDay)
        {
            return Plural(hours / (long)HoursPerDay, "day");
        }
        return Plural(hours, "hour");
    }

    private static string Plural(long count, string unit) => $"{count} {unit}{(count == 1 ? string.Empty : "s")}";
}
