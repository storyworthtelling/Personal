using FocusPhone.Domain.Audio;
using Xunit;

namespace FocusPhone.Tests.Audio;

public class PlaybackFormattingTests
{
    [Fact]
    public void TimeLeftAtSpeed_AtNormalSpeedIsRawMinutes()
    {
        Assert.Equal("40 min left", PlaybackFormatting.TimeLeftAtSpeed(TimeSpan.FromMinutes(40), 1.0f));
    }

    [Fact]
    public void TimeLeftAtSpeed_ShrinksWithSpeed()
    {
        Assert.Equal("20 min left", PlaybackFormatting.TimeLeftAtSpeed(TimeSpan.FromMinutes(40), 2.0f));
    }

    [Fact]
    public void TimeLeftAtSpeed_RoundsToNearestMinute()
    {
        // 40 / 1.5 = 26.67 -> 27
        Assert.Equal("27 min left", PlaybackFormatting.TimeLeftAtSpeed(TimeSpan.FromMinutes(40), 1.5f));
    }

    [Fact]
    public void TimeLeftAtSpeed_NeverShowsZero()
    {
        Assert.Equal("1 min left", PlaybackFormatting.TimeLeftAtSpeed(TimeSpan.FromSeconds(20), 1.0f));
    }

    [Fact]
    public void TimeLeftAtSpeed_TreatsNonPositiveSpeedAsNormal()
    {
        Assert.Equal("40 min left", PlaybackFormatting.TimeLeftAtSpeed(TimeSpan.FromMinutes(40), 0f));
    }
}
