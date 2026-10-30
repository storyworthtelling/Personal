using Foreword.Domain.Calendar;
using Xunit;

namespace Foreword.Tests.Calendar;

public class UnreadableCalendarNoticeTests
{
    private static UnreadableCalendar Skipped(string name) => new(name, "Not found");

    // Nothing unreadable means no notice at all: the dog-ear is withdrawn, not shown empty.
    [Fact]
    public void NothingUnreadableMeansNoNotice()
    {
        Assert.Null(UnreadableCalendarNotice.Detail([]));
        Assert.Empty(UnreadableCalendarNotice.Names(UnreadableCalendarNotice.Store([])));
    }

    // The stored value round-trips the names, so a notice raised by the worker is the one the page shows.
    [Fact]
    public void TheStoredNamesRoundTrip()
    {
        var stored = UnreadableCalendarNotice.Store([Skipped("Shared with me"), Skipped("Family")]);

        Assert.Equal(["Shared with me", "Family"], UnreadableCalendarNotice.Names(stored));
    }

    // The owner is told which calendars were skipped and that the rest of the day went out, in one
    // sentence each, without a number or a technical reason.
    [Fact]
    public void TheDetailNamesEveryCalendarSkippedAndSaysTheRestWentOut()
    {
        var detail = UnreadableCalendarNotice.Detail(["Shared with me", "Family"]);

        Assert.Equal(
            "Could not read Shared with me, Family. Skipped this morning; the rest of the day went out. "
            + "It will be tried again tomorrow.",
            detail);
    }
}
