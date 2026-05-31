using DailyLoader.Domain.Models;
using DailyLoader.Infrastructure.Calendar;
using Xunit;

namespace DailyLoader.Tests.Calendar;

public class OutlookSelectorTests
{
    private readonly OutlookSelector selector = new();

    private static CalendarEvent Event(
        string title,
        PersonColumn column = PersonColumn.Matt,
        EventKind kind = EventKind.Normal,
        RecurrenceFrequency recurrence = RecurrenceFrequency.None,
        int? recurrenceCount = null) =>
        new()
        {
            Title = title,
            Column = column,
            Kind = kind,
            Recurrence = recurrence,
            RecurrenceCount = recurrenceCount,
            Start = new DateTime(2026, 6, 2, 9, 0, 0)
        };

    private bool Selected(CalendarEvent e) =>
        selector.SelectNonRoutine([e]).Any(x => x.Title == e.Title);

    [Fact]
    public void Includes_OneOffAppointment()
    {
        Assert.True(Selected(Event("Dentist")));
    }

    [Fact]
    public void Excludes_WorkBlocks()
    {
        Assert.False(Selected(Event("Work", kind: EventKind.WorkBlock)));
    }

    [Fact]
    public void Excludes_SchoolBlocks()
    {
        Assert.False(Selected(Event("School", kind: EventKind.SchoolBlock)));
    }

    [Fact]
    public void Includes_OffDays()
    {
        Assert.True(Selected(Event("Off", kind: EventKind.OffDay)));
    }

    [Fact]
    public void Excludes_WatchColumn()
    {
        Assert.False(Selected(Event("Red Sox vs Yankees", column: PersonColumn.Watch)));
    }

    [Fact]
    public void Excludes_HighFrequencyWeeklyRecurring()
    {
        Assert.False(Selected(Event("Standup", recurrence: RecurrenceFrequency.Weekly)));
    }

    [Fact]
    public void Excludes_DailyRecurring()
    {
        Assert.False(Selected(Event("Meds", recurrence: RecurrenceFrequency.Daily)));
    }

    [Fact]
    public void Includes_MonthlyRecurring()
    {
        Assert.True(Selected(Event("Book club", recurrence: RecurrenceFrequency.Monthly)));
    }

    [Fact]
    public void Includes_YearlyRecurring()
    {
        Assert.True(Selected(Event("Anniversary", recurrence: RecurrenceFrequency.Yearly)));
    }

    [Fact]
    public void Includes_WeeklyRecurringWithFewOccurrences()
    {
        // A short weekly series (<=5 occurrences) is not routine background noise.
        Assert.True(Selected(Event("Swim lessons", recurrence: RecurrenceFrequency.Weekly, recurrenceCount: 4)));
    }

    [Fact]
    public void OrdersResultsByStart()
    {
        var later = Event("Later");
        var earlier = new CalendarEvent { Title = "Earlier", Start = new DateTime(2026, 6, 1, 8, 0, 0) };

        var result = selector.SelectNonRoutine([later, earlier]);

        Assert.Equal("Earlier", result[0].Title);
        Assert.Equal("Later", result[1].Title);
    }
}
