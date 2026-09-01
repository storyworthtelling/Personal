using SkiaSharp;
using Storyworthtelling.Paper;

namespace Storyworthtelling.Paper.Tests;

public class PdfVolumeComposerTests
{
    private static byte[] Png(int w, int h)
    {
        using var surface = SKSurface.Create(new SKImageInfo(w, h));
        surface.Canvas.Clear(SKColors.White);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public void ComposeWrapsEveryImageIntoAPdf()
    {
        var composer = new PdfVolumeComposer();
        var pdf = composer.Compose([Png(120, 160), Png(120, 160)], dpi: 150);

        Assert.NotEmpty(pdf);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public void ComposeWithNoImagesStillProducesAValidPdf()
    {
        var composer = new PdfVolumeComposer();
        var pdf = composer.Compose([], dpi: 150);

        Assert.NotEmpty(pdf);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public void AppendPageGrowsAnExistingPdf()
    {
        var composer = new PdfVolumeComposer();
        var first = composer.AppendPage(existingPdf: null, Png(120, 160), dpi: 150);
        var second = composer.AppendPage(first, Png(120, 160), dpi: 150);

        Assert.True(second.Length > first.Length);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(second, 0, 4));
    }
}
