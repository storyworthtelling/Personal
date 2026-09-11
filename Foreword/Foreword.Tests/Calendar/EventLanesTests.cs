using Foreword.Domain.Calendar;
using Foreword.Domain.Models;
using Xunit;

namespace Foreword.Tests.Calendar;

/// <summary>
/// G4, the guard the technical finding records as <i>already red today</i>: no two event boxes
/// overlap, and every event inside a work block is drawn.
///
/// <para>Both of the owner's calendar complaints come from the same place. Overlapping events
/// (<b>B2</b>) were drawn at full column width with an opaque fill and no collision detection at
/// all, so whichever was drawn second painted over the first. The work block (<b>B3</b>) is the same
/// bug with a worse symptom: the synthetic nine-to-five is <i>appended</i> to the day's events, so
/// it draws last, as an opaque box over every meeting inside it — which is the owner's "work
/// meetings within the work block do not show".</para>
///
/// <para>So the fix has two pure parts and this file tests both: overlapping events <b>share the
/// column</b> by lane, and a work block is a <b>container</b> — drawn first, behind, and taking no
/// lane of its own. Excluding containers from the lanes is load-bearing: a nine-to-five block that
/// competed for width would squeeze every meeting in the working day to half a column.</para>
/// </summary>
public class EventLanesTests
{
    private static readonly DateOnly Day = new(2026, 9, 8);

    private static CalendarEvent At(int startHour, int endHour, string title = "Meeting",
        EventKind kind = EventKind.Normal) => new()
    {
        Title = title,
        Kind = kind,
        Start = Day.ToDateTime(new TimeOnly(startHour, 0)),
        End = Day.ToDateTime(new TimeOnly(endHour, 0)),
    };

    [Fact]
    public void OneEventTakesTheWholeColumn()
    {
        var placed = EventLanes.Place([At(9, 10)]);

        Assert.Equal(0, placed[0].Lane);
        Assert.Equal(1, placed[0].Lanes);
    }

    /// <summary>B2, at its simplest: two events at the same time each take half the column.</summary>
    [Fact]
    public void TwoOverlappingEventsSplitTheColumn()
    {
        var placed = EventLanes.Place([At(9, 10), At(9, 10, "Other")]);

        Assert.Equal(2, placed[0].Lanes);
        Assert.Equal(2, placed[1].Lanes);
        Assert.NotEqual(placed[0].Lane, placed[1].Lane);
    }

    [Fact]
    public void PartiallyOverlappingEventsStillSplit()
    {
        var placed = EventLanes.Place([At(9, 11), At(10, 12, "Other")]);

        Assert.Equal(2, placed[0].Lanes);
        Assert.NotEqual(placed[0].Lane, placed[1].Lane);
    }

    /// <summary>
    /// Events that merely touch do not overlap. A ten-o'clock meeting following a nine-to-ten is
    /// the commonest shape in a real day, and splitting the column for it would halve the width of
    /// almost everything for no reason.
    /// </summary>
    [Fact]
    public void EventsThatOnlyTouchDoNotSplit()
    {
        var placed = EventLanes.Place([At(9, 10), At(10, 11, "Next")]);

        Assert.Equal(1, placed[0].Lanes);
        Assert.Equal(1, placed[1].Lanes);
    }

    /// <summary>
    /// A lane is reused once its event has finished. Three events where only two are ever
    /// concurrent take two lanes, not three — otherwise a busy morning shrinks the whole day.
    /// </summary>
    [Fact]
    public void ALaneIsReusedOnceItsEventHasFinished()
    {
        var placed = EventLanes.Place([At(9, 11), At(10, 12, "B"), At(11, 13, "C")]);

        Assert.All(placed, p => Assert.Equal(2, p.Lanes));
        Assert.Equal(placed[0].Lane, placed[2].Lane);
    }

    /// <summary>
    /// Two separate clusters are sized independently: a crowded morning does not narrow a quiet
    /// afternoon.
    /// </summary>
    [Fact]
    public void SeparateClustersAreSizedIndependently()
    {
        var placed = EventLanes.Place([At(9, 10), At(9, 10, "B"), At(14, 15, "Afternoon")]);

        Assert.Equal(2, placed[0].Lanes);
        Assert.Equal(1, placed[2].Lanes);
    }

    /// <summary>
    /// B3. The work block is a container: it takes no lane, so the meetings inside it keep the full
    /// column, and it is placed first so everything else is drawn on top of it rather than under it.
    /// </summary>
    [Fact]
    public void AWorkBlockIsAContainerAndTakesNoLaneFromWhatIsInsideIt()
    {
        var placed = EventLanes.Place([At(10, 11), At(9, 17, "Work", EventKind.WorkBlock)]);

        var block = placed.Single(p => p.Event.Kind == EventKind.WorkBlock);
        var meeting = placed.Single(p => p.Event.Kind == EventKind.Normal);

        Assert.True(block.IsContainer);
        Assert.Equal(1, meeting.Lanes);
        Assert.Equal(1, block.Lanes);
    }

    /// <summary>
    /// A meeting inside the work block has to LOOK inside it.
    ///
    /// <para>Taking no lane from it was only half the answer. A container and the meeting inside it
    /// are then drawn at the same width, one exactly on top of the other — so a nine-to-ten standup
    /// covers the top of the nine-to-five rather than sitting within it, and the band reads as
    /// something the meeting interrupted rather than as the day it belongs to. The mockup nests the
    /// child inside its band; that nesting is a fact about the event, so it is decided here.</para>
    /// </summary>
    [Fact]
    public void AMeetingWhollyInsideAWorkBlockIsMarkedAsNested()
    {
        var placed = EventLanes.Place([At(9, 10, "Standup"), At(9, 17, "Work", EventKind.WorkBlock)]);

        Assert.True(placed.Single(p => p.Event.Title == "Standup").IsNested);
    }

    /// <summary>
    /// An appointment that <b>breaks</b> the band is not nested — it belongs to the day rather than
    /// to the work, and the mockup draws it at full column width over the gap it makes. The
    /// distinction is containment, not overlap: a dentist that runs past the end of the block is a
    /// different thing from a meeting within it, and drawing them alike is how a day that was
    /// interrupted looks like a day that went to plan.
    /// </summary>
    [Fact]
    public void AnAppointmentThatBreaksTheBandIsNotNested()
    {
        var placed = EventLanes.Place(
        [
            At(16, 18, "Dentist"),
            At(9, 17, "Work", EventKind.WorkBlock),
        ]);

        Assert.False(placed.Single(p => p.Event.Title == "Dentist").IsNested);
    }

    [Fact]
    public void SomethingOutsideTheWorkingDayIsNotNested()
    {
        var placed = EventLanes.Place(
        [
            At(19, 20, "Dinner"),
            At(9, 17, "Work", EventKind.WorkBlock),
        ]);

        Assert.False(placed.Single(p => p.Event.Title == "Dinner").IsNested);
    }

    /// <summary>A container is not nested inside itself, or inside another one.</summary>
    [Fact]
    public void AContainerIsNeverNested()
    {
        var placed = EventLanes.Place([At(9, 17, "Work", EventKind.WorkBlock)]);

        Assert.False(placed.Single().IsNested);
    }

    /// <summary>
    /// An event exactly filling the block's hours still counts as inside it. A nine-to-five meeting
    /// on a nine-to-five day is a day spent in one meeting, which is a thing that happens.
    /// </summary>
    [Fact]
    public void AnEventFillingTheBlockExactlyIsStillInsideIt()
    {
        var placed = EventLanes.Place(
        [
            At(9, 17, "Offsite"),
            At(9, 17, "Work", EventKind.WorkBlock),
        ]);

        Assert.True(placed.Single(p => p.Event.Title == "Offsite").IsNested);
    }

    [Fact]
    public void ContainersAreDrawnBeforeEverythingElse()
    {
        var placed = EventLanes.Place(
            [At(10, 11), At(9, 17, "Work", EventKind.WorkBlock), At(12, 13, "Lunch")]);

        Assert.True(placed[0].IsContainer);
        Assert.DoesNotContain(placed.Skip(1), p => p.IsContainer);
    }

    /// <summary>
    /// The other half of B3, stated as the owner would: everything they put in the calendar is on
    /// the page. Nothing is dropped, whatever it overlaps.
    /// </summary>
    [Fact]
    public void EveryEventIsPlaced()
    {
        var events = new[]
        {
            At(9, 17, "Work", EventKind.WorkBlock),
            At(9, 10), At(9, 10, "B"), At(9, 10, "C"),
            At(13, 14, "D"),
        };

        Assert.Equal(events.Length, EventLanes.Place(events).Count);
    }

    /// <summary>
    /// The boxes genuinely do not overlap — the geometry, not just the lane numbers. This is G4
    /// stated the way the finding words it, computed from the placement rather than trusted.
    /// </summary>
    [Fact]
    public void NoTwoBoxesOverlapOnceLanesAreApplied()
    {
        const double columnWidth = 100;
        var placed = EventLanes.Place([At(9, 11), At(10, 12, "B"), At(10, 11, "C")])
            .Where(p => !p.IsContainer)
            .Select(p => (
                Left: p.Lane * (columnWidth / p.Lanes),
                Right: (p.Lane + 1) * (columnWidth / p.Lanes),
                p.Event.Start,
                p.Event.End))
            .ToList();

        foreach (var (a, b) in placed.SelectMany((a, i) => placed.Skip(i + 1).Select(b => (a, b))))
        {
            var sameTime = a.Start < b.End && b.Start < a.End;
            var sameSpace = a.Left < b.Right && b.Left < a.Right;

            Assert.False(sameTime && sameSpace,
                $"Boxes at [{a.Left},{a.Right}) and [{b.Left},{b.Right}) share time and space.");
        }
    }
}
