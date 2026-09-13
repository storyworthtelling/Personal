using Folio.Domain.Report;

namespace Folio.Tests.Report;

/// <summary>
/// G16 — the mark's tones come from the jacket, never a constant, and clear the contrast floor.
///
/// <para>Two bugs are reintroduced here, and they fail differently. Hardcoding the ground produces a
/// mark that is <i>correct on every page</i> and wrong as a product: every report in the shelf
/// carries the same stamp and the jacket has stopped meaning anything. Dropping the floor ships a
/// mark that vanishes on its own ground for exactly the jackets that are painted in one key — which
/// is most paintings — and nothing downstream can tell, because a smudge is a valid PDF.</para>
/// </summary>
public class MarkColourwayTests
{
    private static List<int> Jacket(params (int Colour, int Count)[] tones)
    {
        var pixels = new List<int>();
        foreach (var (colour, count) in tones)
        {
            pixels.AddRange(Enumerable.Repeat(colour, count));
        }
        return pixels;
    }

    [Fact]
    public void TheGroundIsTheJacketsMostProminentTone()
    {
        var colours = MarkColourway.From(Jacket((0x42464B, 700), (0xD8CEB8, 300)));

        // Quantised to its bucket's middle, not to a constant.
        Assert.InRange(colours.Ground >> 16 & 0xFF, 0x42 - 8, 0x42 + 8);
        Assert.InRange(colours.Ground >> 8 & 0xFF, 0x46 - 8, 0x46 + 8);
        Assert.InRange(colours.Ground & 0xFF, 0x4B - 8, 0x4B + 8);
    }

    [Fact]
    public void TheInkIsTheOtherProminentTone()
    {
        var colours = MarkColourway.From(Jacket((0x42464B, 700), (0xD8CEB8, 300)));

        Assert.InRange(colours.Ink >> 16 & 0xFF, 0xD8 - 8, 0xD8 + 8);
        Assert.InRange(colours.Ink >> 8 & 0xFF, 0xCE - 8, 0xCE + 8);
        Assert.InRange(colours.Ink & 0xFF, 0xB8 - 8, 0xB8 + 8);
    }

    /// <summary>Two different books, two different marks — which is the whole point of F37.</summary>
    [Fact]
    public void ADifferentJacketGivesADifferentColourway()
    {
        var marsh = MarkColourway.From(Jacket((0x42464B, 700), (0xD8CEB8, 300)));
        var orchard = MarkColourway.From(Jacket((0x7A3B2E, 700), (0xEFE3C4, 300)));

        Assert.NotEqual(marsh.Ground, orchard.Ground);
        Assert.NotEqual(marsh.Ink, orchard.Ink);
        Assert.NotEqual(marsh.Line, orchard.Line);
    }

    [Theory]
    [InlineData(0x42464B, 0xD8CEB8)]   // the drawn mark's own pair
    [InlineData(0x5A5E52, 0x63675B)]   // a jacket painted in one key: no natural pair at all
    [InlineData(0x101010, 0x141414)]   // near-black on near-black
    [InlineData(0xF4F1EA, 0xF0EDE6)]   // near-white on near-white
    [InlineData(0x8A8A8A, 0x8B8B8B)]   // mid-grey, the worst case in both directions
    public void TheMarkAlwaysClearsTheContrastFloor(int dominant, int second)
    {
        var colours = MarkColourway.From(Jacket((dominant, 700), (second, 300)));

        Assert.True(MarkColourway.Contrast(colours.Ground, colours.Ink) >= MarkColourway.ContrastFloor,
            $"The M would be a smudge: {MarkColourway.Contrast(colours.Ground, colours.Ink):0.00}:1 "
            + $"against a floor of {MarkColourway.ContrastFloor}:1.");
    }

    [Fact]
    public void AJacketOfOneToneStillProducesALegibleMark()
    {
        var colours = MarkColourway.From(Jacket((0x6B6759, 1000)));

        Assert.True(MarkColourway.Contrast(colours.Ground, colours.Ink) >= MarkColourway.ContrastFloor);
    }

    [Fact]
    public void TheOvalsOutlineIsVisibleAgainstItsOwnFill()
    {
        var colours = MarkColourway.From(Jacket((0x42464B, 700), (0xD8CEB8, 300)));

        Assert.NotEqual(colours.Ground, colours.Line);
    }

    [Fact]
    public void AJacketThatDecodedToNothingIsRefusedRatherThanGivenAConstant()
    {
        Assert.Throws<ArgumentException>(() => MarkColourway.From(Array.Empty<int>()));
    }
}
