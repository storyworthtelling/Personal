using Codex.Domain.Music;
using Xunit;

namespace Codex.Tests.Music;

public class FirstOpenOfDayTests
{
    [Fact]
    public void NeverShown_IsFirst()
    {
        Assert.True(FirstOpenOfDay.IsFirst(new DateOnly(2026, 9, 19), lastShown: null));
    }

    [Fact]
    public void ShownEarlierToday_IsNotFirst()
    {
        var today = new DateOnly(2026, 9, 19);

        Assert.False(FirstOpenOfDay.IsFirst(today, lastShown: today));
    }

    [Fact]
    public void ShownYesterday_IsFirst()
    {
        var today = new DateOnly(2026, 9, 19);
        var yesterday = today.AddDays(-1);

        Assert.True(FirstOpenOfDay.IsFirst(today, lastShown: yesterday));
    }

    [Fact]
    public void ShownInTheFuture_IsNotFirst()
    {
        // A clock rolled back (or a stored date ahead of "today" some other way) must not
        // repeat a greeting that already went out for a later date.
        var today = new DateOnly(2026, 9, 19);
        var tomorrow = today.AddDays(1);

        Assert.False(FirstOpenOfDay.IsFirst(today, lastShown: tomorrow));
    }
}
