using FocusPhone.Domain.Design;
using FocusPhone.Domain.Unlock;
using Xunit;

namespace FocusPhone.Tests.Unlock;

public class InlineCountdownTests
{
    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);
    }

    private static readonly TimeSpan Window = TimeSpan.FromSeconds(DesignTokens.Motion.CutoverCountdownSeconds);

    [Fact]
    public void StartsIdle_ShowingTheFullWindow()
    {
        var countdown = new InlineCountdown(new TestClock(), Window);
        Assert.False(countdown.IsRunning);
        Assert.False(countdown.HasExpired);
        Assert.Equal(Window, countdown.Remaining);
    }

    [Fact]
    public void CountsDown_AndExpiresOnlyAfterTheFullWindow()
    {
        var clock = new TestClock();
        var countdown = new InlineCountdown(clock, Window);
        countdown.Start();

        clock.UtcNow += TimeSpan.FromSeconds(4);
        Assert.Equal(TimeSpan.FromSeconds(6), countdown.Remaining);
        Assert.False(countdown.HasExpired);

        clock.UtcNow += TimeSpan.FromSeconds(6);
        Assert.True(countdown.HasExpired);
        Assert.Equal(TimeSpan.Zero, countdown.Remaining);
    }

    [Fact]
    public void Cancel_StopsTheCountdown_EvenAtTheLastMoment()
    {
        var clock = new TestClock();
        var countdown = new InlineCountdown(clock, Window);
        countdown.Start();
        clock.UtcNow += Window - TimeSpan.FromMilliseconds(1);

        countdown.Cancel();
        clock.UtcNow += TimeSpan.FromMinutes(5);

        Assert.False(countdown.IsRunning);
        Assert.False(countdown.HasExpired);
    }
}
