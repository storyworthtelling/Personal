using Storyworthtelling.Maui.Time;

namespace Storyworthtelling.Maui.State;

/// <summary>
/// The date printed beside a notice on the Waiting page (mockup 17), in the suite's relative-time rule
/// (DESIGN.md §14: "just now" → "5m ago" → "2h ago" → "yesterday" → "Tuesday" → "Sep 26"). A notice
/// with no date prints none.
///
/// <para>The mockup draws all three notices past the week boundary, hence "Sep 26" rather than a
/// weekday; a notice raised an hour ago would read "1h ago", which is the rule rather than a departure
/// from the drawing.</para>
/// </summary>
public static class NoticeDates
{
    public static string? Label(DateTimeOffset? at, DateTimeOffset now) =>
        at is { } when ? RelativeTime.Format(when.LocalDateTime, now.LocalDateTime) : null;
}
