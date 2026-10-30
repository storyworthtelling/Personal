using Foreword.Domain.Calendar;
using Foreword.Domain.Models;
using Xunit;

namespace Foreword.Tests.Calendar;

public class CalendarReadsTests
{
    private static CalendarEvent Event(string title, string calendarId) => new()
    {
        Title = title,
        SourceCalendarId = calendarId,
        Start = new DateTime(2026, 10, 25, 9, 0, 0),
        End = new DateTime(2026, 10, 25, 10, 0, 0),
    };

    private static readonly (string Id, string Name)[] Calendars =
    [
        ("work", "Work"),
        ("shared", "Shared with me"),
        ("family", "Family"),
    ];

    [Fact]
    public async Task EveryCalendarReadableMeansEveryEventAndNothingUnreadable()
    {
        var read = await CalendarReads.CollectAsync(Calendars, id =>
            Task.FromResult<IReadOnlyList<CalendarEvent>>([Event(id, id)]));

        Assert.Equal(3, read.Events.Count);
        Assert.Empty(read.Unreadable);
    }

    // A calendar the account still lists but can no longer read must not sink the morning: the others
    // still go out, and the failing one is named so the owner can see what was skipped.
    [Fact]
    public async Task AnUnreadableCalendarIsSkippedAndNamedWhileTheRestStillArrive()
    {
        var read = await CalendarReads.CollectAsync(Calendars, id => id == "shared"
            ? throw new InvalidOperationException("Not found")
            : Task.FromResult<IReadOnlyList<CalendarEvent>>([Event(id, id)]));

        Assert.Equal(["work", "family"], read.Events.Select(e => e.SourceCalendarId).ToArray());
        var skipped = Assert.Single(read.Unreadable);
        Assert.Equal("Shared with me", skipped.Name);
        Assert.Equal("Not found", skipped.Reason);
    }

    [Fact]
    public async Task EveryCalendarUnreadableLeavesNoEventsButNamesEachOne()
    {
        var read = await CalendarReads.CollectAsync(Calendars, _ =>
            throw new InvalidOperationException("Not found"));

        Assert.Empty(read.Events);
        Assert.Equal(3, read.Unreadable.Count);
    }

    [Fact]
    public async Task NoCalendarsMeansNothingToReadOrSkip()
    {
        var read = await CalendarReads.CollectAsync([], _ =>
            Task.FromResult<IReadOnlyList<CalendarEvent>>([]));

        Assert.Empty(read.Events);
        Assert.Empty(read.Unreadable);
    }
}
