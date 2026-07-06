using SkiaSharp;
using Storyboarder.Infrastructure.Imaging;
using Storyboarder.Tests.Support;

namespace Storyboarder.Tests.Imaging;

public class ThumbnailerTests
{
    [Fact]
    public void Downscale_ProducesTargetWidth_PreservingAspect()
    {
        var source = SampleImages.Png(1024, 1536);

        var thumb = Thumbnailer.Downscale(source, 256);

        using var bitmap = SKBitmap.Decode(thumb);
        Assert.Equal(256, bitmap.Width);
        Assert.Equal(384, bitmap.Height);
        Assert.True(thumb.Length < source.Length);
    }

    [Fact]
    public void Downscale_RejectsUndecodableInput()
    {
        Assert.Throws<InvalidOperationException>(() => Thumbnailer.Downscale([1, 2, 3], 256));
    }
}
