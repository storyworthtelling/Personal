using Foreword.Domain.Models;

namespace Foreword.Domain.Calendar;

/// <summary>
/// One event's place in its column: which lane it sits in, and how many lanes that stretch of the
/// day was divided into. A box's width is the column's width divided by <see cref="Lanes"/>.
/// </summary>
/// <param name="IsContainer">
/// Whether this is a backdrop the rest of the day is drawn <i>inside</i> — a work block — rather
/// than something competing for space.
/// </param>
public readonly record struct PlacedEvent(CalendarEvent Event, int Lane, int Lanes, bool IsContainer);

/// <summary>
/// Where the events in one column go, so that two of them never land on top of each other
/// (bugs B2 and B3).
///
/// <para>Every event used to be drawn at the full column width with an opaque fill and no
/// collision detection, so whichever was drawn second painted over the first. The work block was
/// the same bug with a worse symptom: the synthetic nine-to-five was appended to the day's events,
/// so it drew <b>last</b> — an opaque box over every meeting inside it.</para>
///
/// <para>So overlapping events share the column by lane, and a <b>container</b> — a work block — is
/// drawn first, behind, and takes no lane. That exclusion is load-bearing rather than a detail: a
/// nine-to-five that competed for width would squeeze every meeting in the working day into half a
/// column, which is a different wrong answer rather than a fix.</para>
///
/// <para>Pure, and in Domain, because it is the part of the calendar page that can be checked
/// without a device — and because "no two boxes overlap" is a statement about geometry, not about
/// drawing.</para>
/// </summary>
public static class EventLanes
{
    /// <summary>
    /// Places every event, containers first and in draw order. Nothing is ever dropped: an event
    /// the owner put in the calendar is on the page, whatever it overlaps.
    /// </summary>
    public static IReadOnlyList<PlacedEvent> Place(IReadOnlyList<CalendarEvent> events)
    {
        var containers = events.Where(IsContainer)
            .Select(e => new PlacedEvent(e, Lane: 0, Lanes: 1, IsContainer: true));

        // Sorted by start, then by the longer event first, so a meeting that begins with a longer
        // one sits beside it rather than pushing it over. Ties break on title to keep the output
        // stable — an unstable order would redraw the same day differently on consecutive mornings.
        var rest = events.Where(e => !IsContainer(e))
            .OrderBy(e => e.Start)
            .ThenByDescending(e => e.End)
            .ThenBy(e => e.Title, StringComparer.Ordinal)
            .ToList();

        return [.. containers, .. Lanes(rest)];
    }

    private static bool IsContainer(CalendarEvent e) =>
        e.Kind is EventKind.WorkBlock or EventKind.SchoolBlock;

    private static List<PlacedEvent> Lanes(List<CalendarEvent> ordered)
    {
        var placed = new List<PlacedEvent>(ordered.Count);

        // A cluster is a run of events that transitively overlap. It is closed the moment an event
        // starts at or after everything so far has ended — and only then is the lane count known,
        // which is why the cluster's members are back-filled rather than written as they are laid
        // out. Sizing each cluster on its own is what stops a crowded morning narrowing a quiet
        // afternoon.
        var cluster = new List<(CalendarEvent Event, int Lane)>();
        var laneEnds = new List<DateTime>();

        foreach (var e in ordered)
        {
            if (laneEnds.Count > 0 && laneEnds.All(end => end <= e.Start))
            {
                Flush(cluster, laneEnds.Count, placed);
                cluster.Clear();
                laneEnds.Clear();
            }

            // The lowest lane whose previous event has finished; a new one if none has. Touching is
            // not overlapping: a ten o'clock meeting after a nine-to-ten reuses the lane, and
            // splitting the column for it would halve almost every box in a normal day.
            var lane = laneEnds.FindIndex(end => end <= e.Start);
            if (lane < 0)
            {
                lane = laneEnds.Count;
                laneEnds.Add(e.End);
            }
            else
            {
                laneEnds[lane] = e.End;
            }

            cluster.Add((e, lane));
        }

        Flush(cluster, laneEnds.Count, placed);
        return placed;
    }

    private static void Flush(
        List<(CalendarEvent Event, int Lane)> cluster, int lanes, List<PlacedEvent> placed)
    {
        foreach (var (e, lane) in cluster)
        {
            placed.Add(new PlacedEvent(e, lane, Math.Max(lanes, 1), IsContainer: false));
        }
    }
}
