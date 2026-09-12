using SkiaSharp;
using Xunit;

namespace Storyworthtelling.Paper.Tests;

/// <summary>
/// Cutting a square out of a photo for the maker's mark.
///
/// <para>What is being guarded is mostly the failure paths, because the input is whatever a file
/// picker handed over and the output feeds a page that is printed while nobody is watching.</para>
/// </summary>
public class MedallionCropTests
{
    private static byte[] Photo(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);

            // A mark in the middle third, so a crop that took a corner would lose it.
            using var paint = new SKPaint { Color = SKColors.Black };
            canvas.DrawRect(
                SKRect.Create(width / 3f, height / 3f, width / 3f, height / 3f), paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        return data.ToArray();
    }

    [Fact]
    public void ItMeasuresAPhoto()
    {
        Assert.Equal((320, 200), MedallionCrop.Measure(Photo(320, 200)));
    }

    [Theory]
    [InlineData(new byte[] { 1, 2, 3, 4, 5 })]
    [InlineData(new byte[0])]
    public void SomethingThatIsNotAPhotoMeasuresAsNothing(byte[] bytes)
    {
        Assert.Null(MedallionCrop.Measure(bytes));
    }

    [Fact]
    public void ItCutsASquareAndScalesIt()
    {
        var cut = MedallionCrop.SquarePng(Photo(400, 300), sourceX: 50, sourceY: 0,
            sourceEdge: 300, targetEdge: 128);

        Assert.NotNull(cut);
        Assert.Equal((128, 128), MedallionCrop.Measure(cut!));
    }

    /// <summary>
    /// It keeps what was in the middle. A crop that silently took a corner would be a mark of
    /// somebody's shoulder, and the page would print it without complaint.
    /// </summary>
    [Fact]
    public void TheMiddleOfThePhotoSurvives()
    {
        var cut = MedallionCrop.SquarePng(Photo(400, 400), 0, 0, 400, 64)!;

        using var bitmap = SKBitmap.Decode(cut);

        Assert.Equal(SKColors.Black, bitmap.GetPixel(32, 32));
        Assert.Equal(SKColors.White, bitmap.GetPixel(2, 2));
    }

    /// <summary>
    /// A rectangle that does not fit the photo is refused rather than clamped. It means the plan was
    /// computed against a different image, and a silently different crop is worse than no mark.
    /// </summary>
    [Theory]
    [InlineData(-1, 0, 100)]
    [InlineData(0, -1, 100)]
    [InlineData(250, 0, 100)]
    [InlineData(0, 0, 400)]
    public void ARectangleThatDoesNotFitIsRefused(int x, int y, int edge)
    {
        Assert.Null(MedallionCrop.SquarePng(Photo(300, 300), x, y, edge, 64));
    }

    [Theory]
    [InlineData(0, 64)]
    [InlineData(100, 0)]
    [InlineData(-10, 64)]
    public void NothingToCutIsNothing(int sourceEdge, int targetEdge)
    {
        Assert.Null(MedallionCrop.SquarePng(Photo(300, 300), 0, 0, sourceEdge, targetEdge));
    }

    /// <summary>
    /// And bytes that are not an image at all are null rather than an exception out of a settings
    /// screen — the case that matters, because it is a file picker's ordinary Tuesday.
    /// </summary>
    [Fact]
    public void SomethingThatIsNotAPhotoCutsToNothing()
    {
        Assert.Null(MedallionCrop.SquarePng([1, 2, 3, 4, 5], 0, 0, 100, 64));
    }

    /// <summary>The result is small enough to sit in an encrypted vault as base64.</summary>
    [Fact]
    public void TheResultIsSmall()
    {
        var cut = MedallionCrop.SquarePng(Photo(2000, 1500), 250, 0, 1500, 256)!;

        Assert.True(cut.Length < 200_000, $"the mark encodes to {cut.Length} bytes");
    }
}
