using Foreword.Domain.Calendar;
using Foreword.Domain.Models;
using Xunit;

namespace Foreword.Tests.Calendar;

/// <summary>
/// The other half of B2. Splitting the column stops events painting over each other, but it does
/// not make room that is not there: at four to a column a box is a few characters wide, which is a
/// different way of losing the event.
///
/// <para>The owner asked for <b>all of them, however many</b>. So beyond what stays legible the
/// extras fall to <i>Also today</i> — a list under the grid, in full — rather than being compressed
/// to nothing. Nothing is dropped; some of it is simply written down instead of drawn.</para>
/// </summary>
public class EventOverflowTests
{
    private static readonly DateOnly Day = new(2026, 9, 8);

    private static CalendarEvent At(int startHour, int endHour, string title) => new()
    {
        Title = title,
        Columns = [PersonColumn.Rowan],
        Start = Day.ToDateTime(new TimeOnly(startHour, 0)),
        End = Day.ToDateTime(new TimeOnly(endHour, 0)),
    };

    [Fact]
    public void NothingOverflowsWhenTheDayFits()
    {
        var placed = EventLanes.Place([At(9, 10, "A"), At(9, 10, "B")]);

        Assert.Equal(2, placed.Count);
        Assert.Empty(EventLanes.Overflow(placed));
    }

    /// <summary>
    /// Three at once is past what a fifth of a page can hold. The first two are drawn; the third is
    /// written down.
    /// </summary>
    [Fact]
    public void PastTheLegibleWidthTheExtrasOverflow()
    {
        var placed = EventLanes.Place([At(9, 10, "A"), At(9, 10, "B"), At(9, 10, "C")]);

        var overflow = EventLanes.Overflow(placed);

        Assert.Single(overflow);
        Assert.Equal("C", overflow[0].Event.Title);
    }

    /// <summary>
    /// The drawn boxes never exceed the legible width, whatever the overflow was. This is the point:
    /// a column of three-character boxes is the failure being avoided.
    /// </summary>
    [Fact]
    public void TheDrawnBoxesNeverExceedTheLegibleWidth()
    {
        var placed = EventLanes.Place(
            [At(9, 10, "A"), At(9, 10, "B"), At(9, 10, "C"), At(9, 10, "D"), At(9, 10, "E")]);

        Assert.All(EventLanes.Drawn(placed), p => Assert.True(p.Lanes <= EventLanes.LegibleLanes));
    }

    /// <summary>
    /// Nothing is lost. Every event is either drawn or listed — the two together are the day.
    /// </summary>
    [Fact]
    public void EveryEventIsEitherDrawnOrListed()
    {
        var events = new[]
        {
            At(9, 10, "A"), At(9, 10, "B"), At(9, 10, "C"), At(9, 10, "D"),
            At(14, 15, "Quiet afternoon"),
        };

        var placed = EventLanes.Place(events);

        Assert.Equal(
            events.Length,
            EventLanes.Drawn(placed).Count + EventLanes.Overflow(placed).Count);
    }

    /// <summary>
    /// A quiet hour is untouched by a crowded one. Overflow is decided per cluster, like the lanes
    /// themselves — otherwise one busy morning would push the whole day into a list.
    /// </summary>
    [Fact]
    public void ACrowdedHourDoesNotPushAQuietOneIntoTheList()
    {
        var placed = EventLanes.Place(
            [At(9, 10, "A"), At(9, 10, "B"), At(9, 10, "C"), At(14, 15, "Afternoon")]);

        Assert.Contains(EventLanes.Drawn(placed), p => p.Event.Title == "Afternoon");
    }

    /// <summary>
    /// A container is never listed. It is the backdrop; a work block in "Also today" would read as
    /// something that had been squeezed out of its own day.
    /// </summary>
    [Fact]
    public void AContainerIsNeverPushedIntoTheList()
    {
        var work = new CalendarEvent
        {
            Title = "Work",
            Kind = EventKind.WorkBlock,
            Columns = [PersonColumn.Rowan],
            Start = Day.ToDateTime(new TimeOnly(9, 0)),
            End = Day.ToDateTime(new TimeOnly(17, 0)),
        };

        var placed = EventLanes.Place(
            [work, At(9, 10, "A"), At(9, 10, "B"), At(9, 10, "C"), At(9, 10, "D")]);

        Assert.DoesNotContain(EventLanes.Overflow(placed), p => p.IsContainer);
    }
}
