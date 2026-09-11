using Foreword.Domain.Calendar;
using Foreword.Domain.Models;
using Xunit;

namespace Foreword.Tests.Calendar;

/// <summary>
/// D32: a shared event draws two ways, and which one is not a style choice.
///
/// <para><b>One box spanning the columns</b> when the people are side by side and alone in their
/// hour — it reads as one thing that several people are doing, which is what it is.</para>
///
/// <para><b>A box in each column otherwise</b>, at its true time and full height, with a line
/// naming who else is there. This mode exists because two groups at the same hour interleave across
/// the columns and there is no horizontal room left: Rowan and Wren doing one thing while Jordan and
/// Linden do another cannot both span. Stacking them vertically was tried and rejected — it draws a
/// two-hour event as a one-hour one, which is a lie about the day rather than a compromise about
/// the drawing.</para>
/// </summary>
public class SharedEventModeTests
{
    private static readonly DateOnly Day = new(2026, 9, 8);

    private static CalendarEvent Event(string title, int startHour, int endHour, params PersonColumn[] columns) =>
        new()
        {
            Title = title,
            Columns = columns,
            Start = Day.ToDateTime(new TimeOnly(startHour, 0)),
            End = Day.ToDateTime(new TimeOnly(endHour, 0)),
        };

    [Fact]
    public void AnEventForOnePersonIsNeverSpanned()
    {
        var swimming = Event("Swimming", 9, 10, PersonColumn.Wren);

        Assert.False(SharedEventMode.Spans(swimming, [swimming]));
    }

    /// <summary>
    /// The good case: two people side by side, nothing else in the hour. One box, one thing.
    /// </summary>
    [Fact]
    public void AdjacentPeopleAloneInTheirHourGetOneBox()
    {
        var swimming = Event("Swimming", 9, 10, PersonColumn.Wren, PersonColumn.Linden);

        Assert.True(SharedEventMode.Spans(swimming, [swimming]));
    }

    /// <summary>
    /// The owner's own objection, and the reason the second mode exists: Rowan and Wren doing one
    /// thing while Jordan and Linden do another. The columns interleave, so neither can span without
    /// crossing the other.
    /// </summary>
    [Fact]
    public void TwoInterleavedGroupsInTheSameHourEachDrawPerColumn()
    {
        var ours = Event("Museum", 10, 12, PersonColumn.Rowan, PersonColumn.Wren);
        var theirs = Event("Football", 10, 12, PersonColumn.Jordan, PersonColumn.Linden);

        Assert.False(SharedEventMode.Spans(ours, [ours, theirs]));
        Assert.False(SharedEventMode.Spans(theirs, [ours, theirs]));
    }

    /// <summary>
    /// People who are not side by side cannot share a box without swallowing the column between
    /// them — a box over Rowan and Wren would silently cover Jordan's ten o'clock.
    /// </summary>
    [Fact]
    public void PeopleWhoAreNotSideBySideDrawPerColumn()
    {
        var e = Event("Errand", 9, 10, PersonColumn.Rowan, PersonColumn.Wren);

        Assert.False(SharedEventMode.Spans(e, [e]));
    }

    /// <summary>
    /// Anything else in one of the spanned columns during the hour takes the span away, because the
    /// span occupies the whole width and would paint over it.
    /// </summary>
    [Fact]
    public void SomethingElseInOneOfTheColumnsTakesTheSpanAway()
    {
        var swimming = Event("Swimming", 9, 11, PersonColumn.Wren, PersonColumn.Linden);
        var piano = Event("Piano", 10, 11, PersonColumn.Linden);

        Assert.False(SharedEventMode.Spans(swimming, [swimming, piano]));
    }

    /// <summary>
    /// Something in a column the event does not touch is irrelevant. A busy Rowan does not break the
    /// girls' swimming lesson.
    /// </summary>
    [Fact]
    public void SomethingInAnUnrelatedColumnDoesNotTakeTheSpanAway()
    {
        var swimming = Event("Swimming", 9, 10, PersonColumn.Wren, PersonColumn.Linden);
        var standup = Event("Standup", 9, 10, PersonColumn.Rowan);

        Assert.True(SharedEventMode.Spans(swimming, [swimming, standup]));
    }

    /// <summary>
    /// An event that only touches — ending as the other begins — is not in the hour and does not
    /// take the span away.
    /// </summary>
    [Fact]
    public void AnEventThatOnlyTouchesDoesNotTakeTheSpanAway()
    {
        var swimming = Event("Swimming", 9, 10, PersonColumn.Wren, PersonColumn.Linden);
        var piano = Event("Piano", 10, 11, PersonColumn.Linden);

        Assert.True(SharedEventMode.Spans(swimming, [swimming, piano]));
    }

    /// <summary>
    /// A whole-family event spans all four, which is the widest and most useful case: one box
    /// across the page saying the family is doing this.
    /// </summary>
    [Fact]
    public void AWholeFamilyEventSpansAllFour()
    {
        var dinner = Event("Dinner", 18, 19, [.. ColumnSet.People]);

        Assert.True(SharedEventMode.Spans(dinner, [dinner]));
    }

    /// <summary>
    /// Who else is there, for the line the per-column mode carries. It names the others, not
    /// everyone — a box in Wren's column saying "with Wren" would be telling her about herself.
    /// </summary>
    [Fact]
    public void TheWithLineNamesTheOthersNotTheReader()
    {
        var swimming = Event("Swimming", 9, 10, PersonColumn.Wren, PersonColumn.Linden);

        Assert.Equal("with Linden", SharedEventMode.WithLine(swimming, PersonColumn.Wren));
        Assert.Equal("with Wren", SharedEventMode.WithLine(swimming, PersonColumn.Linden));
    }

    [Fact]
    public void TheWithLineIsEmptyForAnEventNobodyElseIsAt()
    {
        var dentist = Event("Dentist", 9, 10, PersonColumn.Rowan);

        Assert.Equal(string.Empty, SharedEventMode.WithLine(dentist, PersonColumn.Rowan));
    }

    [Fact]
    public void TheWithLineNamesSeveralOthers()
    {
        var dinner = Event("Dinner", 18, 19, [.. ColumnSet.People]);

        Assert.Equal("with Jordan, Wren, Linden", SharedEventMode.WithLine(dinner, PersonColumn.Rowan));
    }
}
