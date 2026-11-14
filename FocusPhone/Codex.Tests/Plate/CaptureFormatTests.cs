using Codex.Domain.Plate;
using Xunit;

namespace Codex.Tests.Plate;

/// <summary>
/// What the viewfinder is allowed to show. The rule this exists to hold is one line long
/// and easy to break by accident: <b>the window always carries the ratio the sensor is
/// actually capturing.</b> Never letterboxed inside itself, never stretched, never cropped
/// to fill the well — the alternative is a preview that lies about the frame being composed.
/// </summary>
public class CaptureFormatTests
{
    [Theory]
    [InlineData(CaptureRatio.ThreeFour, 3, 4)]
    [InlineData(CaptureRatio.NineSixteen, 9, 16)]
    public void TheWindowIsExactlyTheRatioBeingCaptured(CaptureRatio ratio, int width, int height)
    {
        var window = CaptureFormat.Window(ratio);

        Assert.Equal(width, window.Width);
        Assert.Equal(height, window.Height);
    }

    [Fact]
    public void EveryRatioIsARealSensorReadout()
    {
        // 3:4 is the full 4:3 readout in portrait and 9:16 is the 16:9 crop. A ratio that is
        // neither would have to be produced by cropping the preview, which is the thing the
        // rule forbids.
        foreach (var ratio in Enum.GetValues<CaptureRatio>())
        {
            var window = CaptureFormat.Window(ratio);
            Assert.True(window.Width > 0 && window.Height > window.Width,
                $"{ratio} is not a portrait window.");
        }
    }

    [Fact]
    public void TheDefaultIsTheTallerOne()
    {
        // At this screen width a 3:4 window leaves roughly 210dp of dead space under the
        // controls. 9:16 leaves about 78dp and is still a real capture ratio, so it is what
        // the camera opens on; switching back to 3:4 makes the window shorter rather than
        // padding it out.
        Assert.Equal(CaptureRatio.NineSixteen, CaptureFormat.Default);
    }

    [Theory]
    [InlineData(Lens.Back, Lens.Front)]
    [InlineData(Lens.Front, Lens.Back)]
    public void LensIsATwoWaySwitchToo(Lens from, Lens expected)
    {
        Assert.Equal(expected, CaptureFormat.NextLens(from));
    }

    [Fact]
    public void TheFrontLensHasNoFlashToOffer()
    {
        // The Pixel has no front-facing flash. Offering the control there would be a button
        // that does nothing, which is worse than one that is not there.
        Assert.False(CaptureFormat.HasFlash(Lens.Front));
        Assert.True(CaptureFormat.HasFlash(Lens.Back));
    }
}
