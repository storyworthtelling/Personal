using System.Globalization;

namespace Codex.Domain.Time;

/// <summary>
/// 12-hour, analog-sensibility time formats per DESIGN.md §11:
/// lowercase meridiem, no leading zero.
/// </summary>
public static class TimeFormats
{
    public static string Clock12(DateTime moment) =>
        moment.ToString("h:mm", CultureInfo.InvariantCulture) +
        (moment.Hour < 12 ? " am" : " pm");

    /// <summary>
    /// The hour alone — "3pm", "10am", "12pm". For places with room for a time but not for a time
    /// and its minutes: a chart axis, or a sentence naming when the day peaks.
    ///
    /// <para>The format string is "%h", not "h": a single-character format string is read as a
    /// STANDARD specifier rather than a custom one, and "h" is not a standard specifier, so the
    /// plain version throws a FormatException at the moment it is asked for a time.</para>
    /// </summary>
    public static string Hour12(DateTime moment) =>
        moment.ToString("%h", CultureInfo.InvariantCulture) + (moment.Hour < 12 ? "am" : "pm");

    public static string DateLong(DateTime moment) =>
        moment.ToString("dddd, MMMM d", CultureInfo.InvariantCulture);

    public static string Stamp(DateTime moment) =>
        moment.ToString("MMM d", CultureInfo.InvariantCulture) + ", " + Clock12(moment);
}
