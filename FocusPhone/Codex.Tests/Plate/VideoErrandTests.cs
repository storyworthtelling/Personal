using Codex.Domain.Plate;
using Xunit;

namespace Codex.Tests.Plate;

/// <summary>
/// A video another app asks for (audit W7.4, owner's ruling to build it): the caller's limits are honoured,
/// and a recording stopped by one of them is a finished video, not a failure.
/// </summary>
public class VideoErrandTests
{
    [Fact]
    public void TheCallersLimitsAreRead()
    {
        var limits = VideoErrand.Limits(durationSeconds: 30, sizeBytes: 5_000_000);

        Assert.Equal(TimeSpan.FromSeconds(30), limits.Duration);
        Assert.Equal(5_000_000, limits.SizeBytes);
    }

    [Theory]
    [InlineData(0, 0L)]
    [InlineData(-1, -1L)]
    public void NoLimitGivenIsNoLimit(int duration, long size)
    {
        var limits = VideoErrand.Limits(duration, size);

        Assert.Null(limits.Duration);
        Assert.Null(limits.SizeBytes);
    }

    [Theory]
    [InlineData(VideoRecordingEnd.Finished, true)]
    [InlineData(VideoRecordingEnd.DurationLimit, true)]
    [InlineData(VideoRecordingEnd.SizeLimit, true)]
    [InlineData(VideoRecordingEnd.Failed, false)]
    [InlineData(VideoRecordingEnd.NothingRecorded, false)]
    public void ARecordingStoppedByTheCallersLimitIsStillReturned(VideoRecordingEnd end, bool returned)
    {
        Assert.Equal(returned, VideoErrand.IsUsable(end));
    }

    [Fact]
    public void TheIntentExtrasAreThePlatformsNames()
    {
        Assert.Equal("android.intent.extra.durationLimit", VideoErrand.ExtraDurationLimit);
        Assert.Equal("android.intent.extra.sizeLimit", VideoErrand.ExtraSizeLimit);
    }
}
