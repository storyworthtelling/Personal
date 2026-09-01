using Codex.Domain.Music;
using Xunit;

namespace Codex.Tests.Music;

public class PlaybackTimelineTests
{
    [Fact]
    public void Fraction_Halfway()
    {
        Assert.Equal(0.5, PlaybackTimeline.Fraction(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(4)), 3);
    }

    [Fact]
    public void Fraction_ZeroDuration_IsZero()
    {
        Assert.Equal(0.0, PlaybackTimeline.Fraction(TimeSpan.FromSeconds(30), TimeSpan.Zero));
    }

    [Fact]
    public void Fraction_ClampsAboveOne()
    {
        Assert.Equal(1.0, PlaybackTimeline.Fraction(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(4)));
    }

    [Fact]
    public void SeekTarget_MapsFractionToPosition()
    {
        Assert.Equal(TimeSpan.FromMinutes(3), PlaybackTimeline.SeekTarget(0.75, TimeSpan.FromMinutes(4)));
    }

    [Theory]
    [InlineData(5, "0:05")]
    [InlineData(65, "1:05")]
    [InlineData(3661, "1:01:01")]
    public void Elapsed_FormatsClock(int seconds, string expected)
    {
        Assert.Equal(expected, PlaybackTimeline.Elapsed(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Remaining_IsNegativePrefixedAndFloorsAtZero()
    {
        Assert.Equal("-1:00", PlaybackTimeline.Remaining(TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(4)));
        Assert.Equal("-0:00", PlaybackTimeline.Remaining(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(4)));
    }
}
