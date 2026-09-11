using Foreword.Domain.Models;

namespace Foreword.Domain.Calendar;

/// <summary>
/// A drive that has to happen before an event, and whether there is time for it.
/// </summary>
/// <param name="From">Where the drive starts — where this person's day has left them.</param>
/// <param name="Leave">When you would have to leave.</param>
/// <param name="Unreachable">
/// True when the previous event is still running at <see cref="Leave"/> — the two places cannot
/// both be reached, whatever the calendar says.
/// </param>
public readonly record struct Travel(
    CalendarEvent Event, string From, DateTime Leave, int Minutes, bool Unreachable);

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
///
/// <para><b>A drive has two ends.</b> The event carries where it is; where the person is coming
/// from is not on the event at all, and it is not the same for everyone on it. It is wherever that
/// person's own day has left them — the last place their column put them — and the house at the
/// start of the morning, since a family all leaves from one address. That is why this is computed
/// per column rather than once per event: the parent driving in from work and the child coming out
/// of school are making two different drives to the same field, and a single number on the event
/// would give them both the same one.</para>
/// </summary>
public static class TravelPlan
{
    /// <summary>
    /// Where the bands go for one column's day. <paramref name="minutes"/> answers how long the
    /// drive from one place to another takes, or null when nobody knows — an unknown drive draws
    /// nothing rather than a guess, because a band of invented length is worse than no band.
    /// </summary>
    /// <param name="home">Where the day starts, before anything has said otherwise.</param>
    public static IReadOnlyList<Travel> For(
        IReadOnlyList<CalendarEvent> day, string home, Func<string, string, int?> minutes)
    {
        var ordered = day.Where(e => !e.IsAllDay).OrderBy(e => e.Start).ToList();
        var travels = new List<Travel>();

        // Where the person was last known to be. An event with no place does not move it: somebody
        // who was at the office and then has an hour of "Call" is still driving from the office
        // afterwards, and sending them home for it would invent a round trip nobody made.
        var at = home;

        for (var i = 0; i < ordered.Count; i++)
        {
            var e = ordered[i];
            if (string.IsNullOrWhiteSpace(e.Location))
            {
                continue;
            }

            var destination = e.Location.Trim();
            if (string.Equals(destination, at, StringComparison.OrdinalIgnoreCase))
            {
                // Already there. Two things in one building are not a drive apart.
                continue;
            }

            var from = at;
            at = destination;

            if (minutes(from, destination) is not { } drive || drive <= 0)
            {
                continue;
            }

            var leave = e.Start.AddMinutes(-drive);

            // Unreachable when something before it is still running at the moment you would have to
            // leave. Compared against everything earlier rather than only the one immediately
            // before, because the previous event by START time is not always the one that runs
            // latest — a long morning block can overlap a short meeting after it.
            var unreachable = ordered.Take(i).Any(previous => previous.End > leave);

            travels.Add(new Travel(e, from, leave, drive, unreachable));
        }

        return travels;
    }
}
