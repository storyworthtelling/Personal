using Folio.Domain.Shelf;
using Xunit;

namespace Folio.Tests.Shelf;

/// <summary>
/// The shelf's one piece of data, and the jitter that keeps it readable as books.
///
/// <para>The anchors are read back out of the approved mockup rather than invented. It gives the
/// range outright — <i>The Turning Tide</i> at 42dp is "a long book", <i>Letters Home</i> at 22dp
/// "a short one" — and exactly one spine in the frame carries a stated length: <i>The Salt Line</i>,
/// 94,200 words, drawn at 34dp. The anchors below are the round pair that reproduces that number
/// instead of landing near it, and the cases here pin all three.</para>
/// </summary>
public class SpineSizingTests
{
    [Theory]
    [InlineData(10_000, 22)]
    [InlineData(150_000, 42)]
    [InlineData(94_200, 34)]   // The Salt Line, as drawn
    public void WidthCarriesWordCount(int words, int expectedDp)
    {
        Assert.Equal(expectedDp, SpineSizing.WidthDp(words));
    }

    // A novella and a doorstop both have to sit on the shelf and stay legible.
    [Theory]
    [InlineData(0)]
    [InlineData(500)]
    [InlineData(400_000)]
    [InlineData(int.MaxValue)]
    public void WidthStaysWithinTheShelfsRange(int words)
    {
        var width = SpineSizing.WidthDp(words);
        Assert.InRange(width, SpineSizing.MinWidthDp, SpineSizing.MaxWidthDp);
    }

    [Fact]
    public void AThickerBookIsNeverNarrower()
    {
        var widths = new[] { 0, 10_000, 50_000, 90_000, 120_000, 200_000 }
            .Select(SpineSizing.WidthDp).ToList();

        for (var i = 1; i < widths.Count; i++)
        {
            Assert.True(widths[i] >= widths[i - 1], $"Width went backwards at index {i}.");
        }
    }

    /// <summary>
    /// Height is deliberately NOT data. The mockup varies it "so the row reads as books rather than a
    /// bar chart", and says only width carries meaning — so a reader must not be able to infer
    /// anything from a taller spine.
    /// </summary>
    [Theory]
    [InlineData("The Salt Line")]
    [InlineData("Letters Home")]
    [InlineData("")]
    public void HeightStaysInsideTheBand(string title)
    {
        var height = SpineSizing.HeightDp(title);
        Assert.InRange(height, SpineSizing.MinHeightDp, SpineSizing.MaxHeightDp);
    }

    // Stable across launches: a shelf that reshuffled its heights every time it was drawn would
    // read as movement, and nothing has moved.
    [Fact]
    public void HeightIsTheSameEveryTimeForTheSameBook()
    {
        Assert.Equal(SpineSizing.HeightDp("The Turning Tide"), SpineSizing.HeightDp("The Turning Tide"));
    }

    [Fact]
    public void HeightDoesNotTrackLength()
    {
        var heights = new[] { "A", "AB", "ABC", "ABCD", "ABCDE", "ABCDEF" }
            .Select(SpineSizing.HeightDp).ToList();

        Assert.True(heights.Distinct().Count() > 1, "The jitter produced one height for every title.");
        Assert.False(
            heights.Zip(heights.Skip(1)).All(pair => pair.Second >= pair.First),
            "Height rises with title length — that is a measure, and height must carry nothing.");
    }
}
