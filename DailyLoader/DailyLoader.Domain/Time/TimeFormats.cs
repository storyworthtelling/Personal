using System.Globalization;

namespace DailyLoader.Domain.Time;

/// <summary>
/// 12-hour, analog-sensibility time formats per DESIGN.md §11:
/// lowercase meridiem, no leading zero.
/// </summary>
public static class TimeFormats
{
    public static string Clock12(DateTime moment) =>
        moment.ToString("h:mm", CultureInfo.InvariantCulture) +
        (moment.Hour < 12 ? " am" : " pm");

    public static string DateLong(DateTime moment) =>
        moment.ToString("dddd, MMMM d", CultureInfo.InvariantCulture);

    public static string Stamp(DateTime moment) =>
        moment.ToString("MMM d", CultureInfo.InvariantCulture) + ", " + Clock12(moment);
}
