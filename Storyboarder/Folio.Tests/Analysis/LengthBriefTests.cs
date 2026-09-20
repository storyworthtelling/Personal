using Folio.Domain.Analysis;

namespace Folio.Tests.Analysis;

/// <summary>
/// The screen's version of the length line. Shorter than the printed one because a phone line is
/// shorter than a page's, and built from the same three numbers so the two can only ever agree
/// about the book — the printed page says "≈6,000 words short of the 80,000–100,000 genre norm" and
/// the screen says "6,000 short of the norm", which is the same claim at the size it fits.
/// </summary>
public class LengthBriefTests
{
    [Fact]
    public void InsideTheNormSaysSo() =>
        Assert.Equal("94,200 words, inside the genre norm", LengthGap.Brief(94_200, 80_000, 100_000));

    [Fact]
    public void ShortOfTheNormSaysHowFarShort() =>
        Assert.Equal("74,200 words, 6,000 short of the norm", LengthGap.Brief(74_200, 80_000, 100_000));

    [Fact]
    public void OverTheNormSaysHowFarOver() =>
        Assert.Equal("112,000 words, 12,000 over the norm", LengthGap.Brief(112_000, 80_000, 100_000));

    /// <summary>
    /// Rounded to the thousand, exactly as the printed line rounds it. A gap stated to the word
    /// implies the genre norm is known to the word, which it is not — a model supplied it.
    /// </summary>
    [Fact]
    public void TheGapIsRoundedTheSameWayThePrintedLineRoundsIt() =>
        Assert.Equal("74,340 words, 6,000 short of the norm", LengthGap.Brief(74_340, 80_000, 100_000));

    /// <summary>
    /// No range means no claim. The expansion read has not run, so the screen says how long the
    /// book is and stays quiet about whether that is right.
    /// </summary>
    [Fact]
    public void WithoutARangeItIsJustACount()
    {
        Assert.Equal("94,200 words", LengthGap.Brief(94_200, 0, 0));
        Assert.Equal("94,200 words", LengthGap.Brief(94_200, 100_000, 80_000));
    }

    [Fact]
    public void NoCountIsNoLine() =>
        Assert.Equal(string.Empty, LengthGap.Brief(0, 80_000, 100_000));
}
