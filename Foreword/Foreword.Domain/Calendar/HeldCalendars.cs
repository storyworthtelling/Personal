using Foreword.Domain.Models;

namespace Foreword.Domain.Calendar;

/// <summary>What reached the page, and which calendars were kept off it.</summary>
public sealed record CalendarSplit(
    IReadOnlyList<CalendarEvent> Printed, IReadOnlyList<string> Held);

/// <summary>
/// Holds back the events of a calendar nobody has mapped (D34), and names it.
///
/// <para>Never silently printed, never silently dropped — both silent outcomes are bad in their own
/// way, and the old behaviour was the first: an unmapped calendar fell to the default column, so a
/// calendar the owner subscribed to started printing into Rowan's column without anything having
/// asked. Dropping it instead would be worse: they would be looking for something that never
/// arrived, with nothing anywhere to say why.</para>
///
/// <para>An event with no source calendar is printed, not held. It did not come from a calendar
/// anyone could map — a note read back off the paper is the case — so holding it would be asking
/// the owner to fix something that is not broken.</para>
/// </summary>
public static class HeldCalendars
{
    public static CalendarSplit Split(
        IReadOnlyList<CalendarEvent> events, IReadOnlySet<string> known)
    {
        var printed = new List<CalendarEvent>();
        var held = new List<string>();

        foreach (var e in events)
        {
            if (string.IsNullOrEmpty(e.SourceCalendarId) || known.Contains(e.SourceCalendarId))
            {
                printed.Add(e);
                continue;
            }

            // Named once, however many events it brought: the owner needs to know which calendar to
            // map, not how busy it is.
            if (!held.Contains(e.SourceCalendarId))
            {
                held.Add(e.SourceCalendarId);
            }
        }

        return new CalendarSplit(printed, held);
    }
}
