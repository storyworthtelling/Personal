using Foreword.Domain.Models;

namespace Foreword.Domain.Calendar;

/// <summary>
/// How a shared event is drawn (D32), and it is not a style choice.
///
/// <para><b>One box spanning the columns</b> when the people are side by side and alone in their
/// hour: it reads as one thing several people are doing, which is what it is.</para>
///
/// <para><b>A box in each column otherwise</b>, at its true time and full height, with a line naming
/// who else is there. That second mode exists because two groups at the same hour interleave across
/// the columns and there is no horizontal room left — Alex and Riley doing one thing while Sam and
/// Jo do another cannot both span. Stacking them vertically was tried and rejected: it draws a
/// two-hour event as a one-hour one, which is a lie about the day rather than a compromise about the
/// drawing. A notch and a tie rule were tried too; the tie read as a strikethrough.</para>
/// </summary>
public static class SharedEventMode
{
    /// <summary>
    /// Whether <paramref name="e"/> may be drawn as one box across its columns.
    ///
    /// <para>Two conditions, and both are about not painting over something. The columns must be
    /// <b>contiguous</b>, because a box over Alex and Riley would silently cover Sam's ten
    /// o'clock between them. And nothing else may overlap it in any of those columns, because a
    /// span occupies the full width and has nowhere to move.</para>
    /// </summary>
    public static bool Spans(
        CalendarEvent e, IReadOnlyList<CalendarEvent> sameDay, IReadOnlyList<PersonColumn> printed)
    {
        var columns = Indices(e, printed);
        if (columns.Count < 2)
        {
            return false;   // one person is not a shared event
        }

        if (columns[^1] - columns[0] != columns.Count - 1)
        {
            return false;   // a gap: somebody else's column sits between them
        }

        return !sameDay.Any(other =>
            !ReferenceEquals(other, e)
            && Overlaps(e, other)
            && Indices(other, printed).Any(columns.Contains));
    }

    /// <summary>
    /// Who else is at this event, for the line the per-column mode carries. It names the
    /// <b>others</b>: a box in Riley's column reading "with Riley" would be telling her about herself.
    /// Empty when nobody else is there, so the caller draws nothing rather than an empty label.
    /// </summary>
    public static string WithLine(CalendarEvent e, PersonColumn reader)
    {
        var others = e.Columns.Where(c => c != reader).ToList();

        return others.Count == 0 ? string.Empty : "with " + string.Join(", ", others);
    }

    /// <summary>
    /// The event's columns as ordered indices, in the order the page draws them, so "contiguous"
    /// means what it looks like on the page. A column this account does not print has no place.
    /// </summary>
    private static List<int> Indices(CalendarEvent e, IReadOnlyList<PersonColumn> printed) =>
        e.Columns.Select(c => IndexOf(printed, c)).Where(i => i >= 0).Distinct().Order().ToList();

    private static int IndexOf(IReadOnlyList<PersonColumn> printed, PersonColumn column)
    {
        for (var i = 0; i < printed.Count; i++)
        {
            if (printed[i] == column)
            {
                return i;
            }
        }

        return -1;
    }

    // Touching is not overlapping: a ten o'clock that begins as a nine-to-ten ends is not in its
    // hour, and taking the span away for it would cost the page a box it could have had.
    private static bool Overlaps(CalendarEvent a, CalendarEvent b) =>
        a.Start < b.End && b.Start < a.End;
}
