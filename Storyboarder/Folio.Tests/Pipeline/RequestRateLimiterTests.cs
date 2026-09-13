using Folio.Domain.Pipeline;
using Folio.Tests.Support;
using Xunit;

namespace Folio.Tests.Pipeline;

public class RequestRateLimiterTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AllowsUpToPerMinute_ThenBlocks()
    {
        var clock = new FakeClock(Start);
        var limiter = new RequestRateLimiter(perMinute: 2, perDay: 1000, clock);

        Assert.True(limiter.TryAcquire());
        Assert.True(limiter.TryAcquire());
        Assert.False(limiter.TryAcquire());
    }

    [Fact]
    public void TimeUntilNext_IsOneMinute_WhenMinuteBudgetExhausted()
    {
        var clock = new FakeClock(Start);
        var limiter = new RequestRateLimiter(perMinute: 2, perDay: 1000, clock);
        limiter.TryAcquire();
        limiter.TryAcquire();

        Assert.Equal(TimeSpan.FromMinutes(1), limiter.TimeUntilNext());
    }

    [Fact]
    public void Allows_AfterMinuteWindowSlides()
    {
        var clock = new FakeClock(Start);
        var limiter = new RequestRateLimiter(perMinute: 2, perDay: 1000, clock);
        limiter.TryAcquire();
        limiter.TryAcquire();
        Assert.False(limiter.TryAcquire());

        clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(TimeSpan.Zero, limiter.TimeUntilNext());
        Assert.True(limiter.TryAcquire());
    }

    [Fact]
    public void EnforcesPerDayBudget()
    {
        var clock = new FakeClock(Start);
        var limiter = new RequestRateLimiter(perMinute: 1000, perDay: 2, clock);

        Assert.True(limiter.TryAcquire());
        Assert.True(limiter.TryAcquire());
        Assert.False(limiter.TryAcquire());
        Assert.Equal(TimeSpan.FromDays(1), limiter.TimeUntilNext());
    }

    [Fact]
    public void Allows_AfterDayWindowSlides()
    {
        var clock = new FakeClock(Start);
        var limiter = new RequestRateLimiter(perMinute: 1000, perDay: 2, clock);
        limiter.TryAcquire();
        limiter.TryAcquire();

        clock.Advance(TimeSpan.FromDays(1));

        Assert.True(limiter.TryAcquire());
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    public void Constructor_RejectsNonPositiveBudgets(int perMinute, int perDay)
    {
        var clock = new FakeClock(Start);
        Assert.Throws<ArgumentOutOfRangeException>(() => new RequestRateLimiter(perMinute, perDay, clock));
    }
}
