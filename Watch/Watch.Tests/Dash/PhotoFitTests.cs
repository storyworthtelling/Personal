using Watch.Domain.Dash;

namespace Watch.Tests.Dash;

/// <summary>
/// How a photo is shrunk on the phone before it crosses to the wrist (W-5.1.3; B5): no larger than the picture, small
/// enough that the mockup's picture (a box 1.0 by 0.6 of the radius) is filled on either watch, and a JPEG that fits
/// <see cref="DashLimits.PhotoBytes"/>, so the message stays inside the Data Layer budget every Dash message keeps to.
/// Re-encoding also leaves the picture's own metadata (where it was taken) on the phone.
/// </summary>
public class PhotoFitTests
{
    [Fact]
    public void TheSizesAreTheDecidedOnes()
    {
        Assert.Equal(240, PhotoFit.ShortSide);
        Assert.Equal(640, PhotoFit.LongSide);
        Assert.Equal(60, PhotoFit.SmallestSide);
        Assert.Equal([80, 70, 60, 50, 40], PhotoFit.Qualities);
        Assert.Equal(40 * 1024, DashLimits.PhotoBytes);
    }

    [Theory]
    [InlineData(4000, 3000, 320, 240)]
    [InlineData(3000, 4000, 240, 320)]
    [InlineData(4000, 1000, 640, 160)]
    [InlineData(1000, 4000, 160, 640)]
    [InlineData(1080, 1080, 240, 240)]
    public void APhotoIsShrunkKeepingItsShape(int width, int height, int fittedWidth, int fittedHeight) =>
        Assert.Equal((fittedWidth, fittedHeight), PhotoFit.Size(width, height));

    [Theory]
    [InlineData(200, 150)]
    [InlineData(1, 1)]
    public void ASmallPhotoIsNeverEnlarged(int width, int height) =>
        Assert.Equal((width, height), PhotoFit.Size(width, height));

    [Fact]
    public void AThinPhotoKeepsAPixel() => Assert.Equal((640, 1), PhotoFit.Size(20000, 10));

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(-1, 10)]
    public void NoPictureHasNoSize(int width, int height) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PhotoFit.Size(width, height));

    [Fact]
    public void TheFittedSizeFillsTheMockupsPictureOnEitherWatch()
    {
        // The larger watch is 456 pixels across, so the picture is 228 by 137 pixels; a fitted photo covers it whatever
        // its shape, before the wrist crops it to the box.
        foreach (var (width, height) in new[] { (4000, 3000), (3000, 4000), (1080, 1080), (4000, 1000) })
        {
            var (fittedWidth, fittedHeight) = PhotoFit.Size(width, height);
            var cover = Math.Max(228.0 / fittedWidth, 137.0 / fittedHeight);
            Assert.True(cover <= 1.0, $"{width} by {height} shrinks to {fittedWidth} by {fittedHeight}, short of the picture.");
        }
    }

    [Fact]
    public void ASizeThatWillNotFitIsHalvedDownToTheSmallest()
    {
        Assert.Equal([(320, 240), (160, 120), (80, 60)], PhotoFit.Sizes(4000, 3000));
        Assert.Equal([(100, 80)], PhotoFit.Sizes(100, 80));
    }

    [Theory]
    [InlineData(4000, 3000, 8)]
    [InlineData(640, 480, 2)]
    [InlineData(320, 240, 1)]
    [InlineData(200, 150, 1)]
    public void ThePhotoIsDecodedNoLargerThanItNeeds(int width, int height, int sample)
    {
        Assert.Equal(sample, PhotoFit.SampleFor(width, height));

        // Never smaller than the size it is shrunk to.
        var (fittedWidth, fittedHeight) = PhotoFit.Size(width, height);
        Assert.True(width / sample >= fittedWidth && height / sample >= fittedHeight);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 180)]
    [InlineData(4, 180)]
    [InlineData(5, 90)]
    [InlineData(6, 90)]
    [InlineData(7, 270)]
    [InlineData(8, 270)]
    [InlineData(0, 0)]
    [InlineData(9, 0)]
    public void APhotoIsTurnedUpright(int orientation, int degrees) => Assert.Equal(degrees, PhotoFit.TurnFor(orientation));

    [Fact]
    public void AFitIsAJpegInsideTheBudget()
    {
        Assert.True(PhotoFit.Fits(DashLimits.PhotoBytes));
        Assert.False(PhotoFit.Fits(DashLimits.PhotoBytes + 1));
        Assert.False(PhotoFit.Fits(0));
    }
}
