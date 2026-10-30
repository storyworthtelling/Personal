using Foreword.Domain.Models;

namespace Foreword.Domain.Calendar;

/// <summary>A calendar that could not be read this morning, by the name the owner knows it as.</summary>
public sealed record UnreadableCalendar(string Name, string Reason);

/// <summary>The events that could be read, and the calendars that could not.</summary>
public sealed record CalendarRead(IReadOnlyList<CalendarEvent> Events, IReadOnlyList<UnreadableCalendar> Unreadable)
{
    public static CalendarRead Nothing { get; } = new([], []);
}

/// <summary>
/// Reads every calendar in the account's list, one at a time, and keeps going past a calendar that
/// fails.
///
/// <para>One unreadable calendar used to fail the whole read, so a calendar the account still listed
/// but could no longer open — shared and then unshared, or deleted while still listed — cost the
/// morning its calendar, its prayer and its paper. It is now skipped, and named, so the day goes out
/// and the owner is told what is missing.</para>
/// </summary>
public static class CalendarReads
{
    public static async Task<CalendarRead> CollectAsync(
        IEnumerable<(string Id, string Name)> calendars,
        Func<string, Task<IReadOnlyList<CalendarEvent>>> readOne)
    {
        var events = new List<CalendarEvent>();
        var unreadable = new List<UnreadableCalendar>();

        foreach (var (id, name) in calendars)
        {
            try
            {
                events.AddRange(await readOne(id));
            }
            catch (Exception ex)
            {
                unreadable.Add(new UnreadableCalendar(name, ex.Message));
            }
        }

        return new CalendarRead(events, unreadable);
    }
}
