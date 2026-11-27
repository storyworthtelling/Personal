using Watch.Domain.Kit;

namespace Watch.Tests.Kit;

/// <summary>
/// The watch's relative times (WD §5, A37; mock: People waiting's "2 min", "1 h", "3 h", Mail's "Yesterday"): the
/// shortest words that fit the corner of a row, read in the watch's own zone.
/// </summary>
public class WhenWordsTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("Watch", TimeSpan.FromHours(-5), "Watch", "Watch");

    // 4:20 pm on the watch's clock.
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 21, 20, 0, TimeSpan.Zero);

    [Fact]
    public void UnderAMinuteIsJustNow()
    {
        Assert.Equal("just now", WhenWords.Relative(Now.AddSeconds(-59), Now, Zone));
        Assert.Equal("just now", WhenWords.Relative(Now, Now, Zone));
    }

    [Fact]
    public void ALaterClockIsJustNow()
    {
        // The phone's clock can run ahead of the watch's.
        Assert.Equal("just now", WhenWords.Relative(Now.AddMinutes(3), Now, Zone));
    }

    [Theory]
    [InlineData(1, "1 min")]
    [InlineData(2, "2 min")]
    [InlineData(59, "59 min")]
    public void MinutesAreMin(int minutes, string expected) =>
        Assert.Equal(expected, WhenWords.Relative(Now.AddMinutes(-minutes), Now, Zone));

    [Theory]
    [InlineData(60, "1 h")]
    [InlineData(179, "2 h")]
    [InlineData(16 * 60, "16 h")]
    public void EarlierTodayIsHours(int minutes, string expected) =>
        Assert.Equal(expected, WhenWords.Relative(Now.AddMinutes(-minutes), Now, Zone));

    [Fact]
    public void BeforeMidnightIsYesterday()
    {
        // 11:50 pm the day before, on the watch's clock: under a day ago, but not today.
        Assert.Equal("Yesterday", WhenWords.Relative(new DateTimeOffset(2026, 11, 21, 4, 50, 0, TimeSpan.Zero), Now, Zone));
    }

    [Fact]
    public void ThisWeekIsItsDay() =>
        Assert.Equal("Tuesday", WhenWords.Relative(Now.AddDays(-4), Now, Zone));

    [Fact]
    public void EarlierIsTheDate()
    {
        Assert.Equal("Nov 3", WhenWords.Relative(new DateTimeOffset(2026, 11, 3, 17, 0, 0, TimeSpan.Zero), Now, Zone));
        Assert.Equal("Dec 30, 2025", WhenWords.Relative(new DateTimeOffset(2025, 12, 30, 17, 0, 0, TimeSpan.Zero), Now, Zone));
    }

    [Fact]
    public void TheDayIsTheWatchsDay()
    {
        // 1 am UTC is 8 pm the day before on the watch's clock: yesterday there, though the same UTC day as now's 9 pm.
        var nowEarly = new DateTimeOffset(2026, 11, 22, 3, 0, 0, TimeSpan.Zero);
        Assert.Equal("7 h", WhenWords.Relative(new DateTimeOffset(2026, 11, 21, 20, 0, 0, TimeSpan.Zero), nowEarly, Zone));
        Assert.Equal("Yesterday", WhenWords.Relative(new DateTimeOffset(2026, 11, 21, 4, 0, 0, TimeSpan.Zero), nowEarly, Zone));
    }
}
