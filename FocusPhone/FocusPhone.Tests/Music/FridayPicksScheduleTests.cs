using FocusPhone.Domain.Music;
using Xunit;

namespace FocusPhone.Tests.Music;

public class FridayPicksScheduleTests
{
    // 2026-05-01 is a Friday; 2026-05-04 the following Monday; 2026-05-08 the next Friday.
    private static DateTimeOffset Day(int day) => new(2026, 5, day, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NotFriday_IsNeverDue()
    {
        Assert.False(FridayPicksSchedule.IsDue(Day(4), lastGenerated: null)); // Monday
    }

    [Fact]
    public void Friday_NeverGenerated_IsDue()
    {
        Assert.True(FridayPicksSchedule.IsDue(Day(1), lastGenerated: null));
    }

    [Fact]
    public void Friday_AlreadyGeneratedThisWeek_IsNotDue()
    {
        // Generated Tuesday of the same ISO week (2026-04-28) -> already have this week's picks.
        var tuesdaySameWeek = new DateTimeOffset(2026, 4, 28, 9, 0, 0, TimeSpan.Zero);
        Assert.False(FridayPicksSchedule.IsDue(Day(1), lastGenerated: tuesdaySameWeek));
    }

    [Fact]
    public void Friday_GeneratedLastWeek_IsDue()
    {
        Assert.True(FridayPicksSchedule.IsDue(Day(8), lastGenerated: Day(1))); // last Friday
    }

    [Fact]
    public void Friday_GeneratedEarlierSameDay_IsNotDue()
    {
        Assert.False(FridayPicksSchedule.IsDue(Day(1).AddHours(6), lastGenerated: Day(1)));
    }
}
