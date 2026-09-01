using Storyboarder.Domain.Pipeline;
using Xunit;

namespace Storyboarder.Tests.Pipeline;

public class BackoffSchedulerTests
{
    private static BackoffScheduler NoJitter(double seconds = 2, double maxSeconds = 60) =>
        new(TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(maxSeconds), jitterFactor: 0.25, nextUnit: () => 0.0);

    [Fact]
    public void FirstRetry_UsesBaseDelay()
    {
        var scheduler = NoJitter();
        Assert.Equal(TimeSpan.FromSeconds(2), scheduler.NextDelay(0));
    }

    [Fact]
    public void Delay_GrowsExponentially()
    {
        var scheduler = NoJitter();
        Assert.Equal(TimeSpan.FromSeconds(2), scheduler.NextDelay(0));
        Assert.Equal(TimeSpan.FromSeconds(4), scheduler.NextDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(8), scheduler.NextDelay(2));
        Assert.Equal(TimeSpan.FromSeconds(16), scheduler.NextDelay(3));
    }

    [Fact]
    public void Delay_CapsAtMax()
    {
        var scheduler = NoJitter(seconds: 2, maxSeconds: 60);
        Assert.Equal(TimeSpan.FromSeconds(60), scheduler.NextDelay(20));
    }

    [Fact]
    public void Jitter_StaysWithinBounds()
    {
        var maxJitter = new BackoffScheduler(
            TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(60), jitterFactor: 0.25, nextUnit: () => 0.999);

        var delay = maxJitter.NextDelay(0);

        Assert.InRange(delay.TotalSeconds, 2.0, 2.5);
    }

    [Fact]
    public void RetryAfter_IsHonored_WhenLargerThanComputed()
    {
        var scheduler = NoJitter();
        Assert.Equal(TimeSpan.FromSeconds(30), scheduler.NextDelay(0, TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void RetryAfter_IsIgnored_WhenSmallerThanComputed()
    {
        var scheduler = NoJitter();
        Assert.Equal(TimeSpan.FromSeconds(2), scheduler.NextDelay(0, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void NegativeAttempt_Throws()
    {
        var scheduler = NoJitter();
        Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.NextDelay(-1));
    }

    [Fact]
    public void Constructor_RejectsMaxLessThanBase()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BackoffScheduler(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5)));
    }
}
