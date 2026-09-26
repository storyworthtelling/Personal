using SkiaSharp;
using Xunit;

namespace Storyworthtelling.Paper.Tests;

/// <summary>
/// The owner's drawn mark: a canted oval pierced by an M. Shared now that Foreword signs its
/// morning with the same mark Folio's report covers carry, rather than a photograph.
/// </summary>
public class OwnersMarkTests
{
    private static readonly SKColor Ground = new(240, 230, 200);
    private static readonly SKColor Line = new(90, 60, 30);
    private static readonly SKColor Ink = new(20, 20, 20);

    private static SKBitmap Drawn(int edge, SKRect dest)
    {
        var bitmap = new SKBitmap(edge, edge);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        OwnersMark.Draw(canvas, dest, Ground, Line, Ink);
        return bitmap;
    }

    [Fact]
    public void TheOvalIsFilledWithTheGround()
    {
        using var bitmap = Drawn(120, new SKRect(0, 0, 120, 120));

        // Inside the oval, clear of the letter's strokes.
        Assert.Equal(Ground, bitmap.GetPixel(24, 60));
    }

    /// <summary>
    /// The legs pierce the oval — the letter's first point sits below it. A mark whose M stopped at
    /// the edge would be a different mark.
    /// </summary>
    [Fact]
    public void TheLetterPiercesTheOval()
    {
        using var bitmap = Drawn(120, new SKRect(0, 0, 120, 120));

        Assert.Equal(Ink, bitmap.GetPixel(34, 114));
    }

    [Fact]
    public void NothingIsDrawnOutsideTheMark()
    {
        using var bitmap = Drawn(120, new SKRect(0, 0, 120, 120));

        Assert.Equal(0, bitmap.GetPixel(2, 2).Alpha);
        Assert.Equal(0, bitmap.GetPixel(117, 117).Alpha);
    }

    /// <summary>It fills whatever box it is given — Folio prints it at 74pt, Foreword at 96pt.</summary>
    [Fact]
    public void ItScalesAndMovesToItsBox()
    {
        using var bitmap = Drawn(240, new SKRect(120, 120, 240, 240));

        Assert.Equal(Ground, bitmap.GetPixel(120 + 24, 120 + 60));
        Assert.Equal(0, bitmap.GetPixel(60, 60).Alpha);
    }

    /// <summary>Folio hands it packed RGB from its jacket reading; the two overloads agree.</summary>
    [Fact]
    public void PackedColoursDrawTheSameMark()
    {
        using var bitmap = new SKBitmap(120, 120);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            OwnersMark.Draw(canvas, new SKRect(0, 0, 120, 120), 0xF0E6C8, 0x5A3C1E, 0x141414);
        }

        Assert.Equal(Ground, bitmap.GetPixel(24, 60));
        Assert.Equal(Ink, bitmap.GetPixel(34, 114));
    }
}
