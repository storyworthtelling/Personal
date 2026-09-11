using Foreword.Domain.Models;

namespace Foreword.Domain.Calendar;

/// <summary>
/// A drive that has to happen before an event, and whether there is time for it.
/// </summary>
/// <param name="Leave">When you would have to leave.</param>
/// <param name="Unreachable">
/// True when the previous event is still running at <see cref="Leave"/> — the two places cannot
/// both be reached, whatever the calendar says.
/// </param>
public readonly record struct Travel(
    CalendarEvent Event, DateTime Leave, int Minutes, bool Unreachable);

/// <summary>
/// The travel bands on a column, and the clashes they expose (Gate 1 · Q74).
///
/// <para>Drawing the drive is not decoration: it is what makes the day's <b>real occupancy</b>
/// visible. A four o'clock that is really a twenty-five-minute drive from a three-thirty is a
/// conflict the grid otherwise shows as two comfortable boxes with a gap between them.</para>
///
/// <para>The clash is called separately from the band, because they answer different questions. The
/// band says "this hour is not as free as it looks"; the clash says "these two cannot both
/// happen" — and only the second is worth interrupting the page for.</para>
/// </summary>
public static class TravelPlan
{
    /// <summary>
    /// Where the bands go for one column's day. <paramref name="minutes"/> answers how long the
    /// drive to an event's location takes, or null when nobody knows — an unknown drive draws
    /// nothing rather than a guess, because a band of invented length is worse than no band.
    /// </summary>
    public static IReadOnlyList<Travel> For(
        IReadOnlyList<CalendarEvent> day, Func<CalendarEvent, int?> minutes)
    {
        var ordered = day.Where(e => !e.IsAllDay).OrderBy(e => e.Start).ToList();
        var travels = new List<Travel>();

        for (var i = 0; i < ordered.Count; i++)
        {
            var e = ordered[i];
            if (minutes(e) is not { } drive || drive <= 0)
            {
                continue;
            }

            var leave = e.Start.AddMinutes(-drive);

            // Unreachable when something before it is still running at the moment you would have to
            // leave. Compared against everything earlier rather than only the one immediately
            // before, because the previous event by START time is not always the one that runs
            // latest — a long morning block can overlap a short meeting after it.
            var unreachable = ordered.Take(i).Any(previous => previous.End > leave);

            travels.Add(new Travel(e, leave, drive, unreachable));
        }

        return travels;
    }
}
