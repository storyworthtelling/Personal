using Codex.Domain.Apps;
using Xunit;

namespace Codex.Tests.Apps;

public class SvgGlyphTests
{
    [Fact]
    public void Parse_AClosedSquare_YieldsFiveCornerPoints()
    {
        var subpaths = SvgGlyph.Parse("M0 0 L100 0 100 100 0 100 Z");

        var poly = Assert.Single(subpaths!);
        Assert.Equal(
            new[] { (0f, 0f), (100f, 0f), (100f, 100f), (0f, 100f), (0f, 0f) },
            poly.Select(p => (p.X, p.Y)));
    }

    [Fact]
    public void Parse_HandlesHorizontalAndVerticalCommands()
    {
        var subpaths = SvgGlyph.Parse("M0 0 H100 V100");

        var poly = Assert.Single(subpaths!);
        Assert.Equal(new[] { (0f, 0f), (100f, 0f), (100f, 100f) }, poly.Select(p => (p.X, p.Y)));
    }

    [Fact]
    public void Parse_HandlesRelativeCommands()
    {
        var subpaths = SvgGlyph.Parse("m10 10 l20 0 l0 20");

        var poly = Assert.Single(subpaths!);
        Assert.Equal(new[] { (10f, 10f), (30f, 10f), (30f, 30f) }, poly.Select(p => (p.X, p.Y)));
    }

    [Fact]
    public void Parse_FlattensACubicCurve_StartAndEndExact()
    {
        var subpaths = SvgGlyph.Parse("M0 0 C0 100 100 100 100 0");

        var poly = Assert.Single(subpaths!);
        Assert.Equal((0f, 0f), (poly[0].X, poly[0].Y));
        Assert.Equal((100f, 0f), (poly[^1].X, poly[^1].Y));
        Assert.True(poly.Count > 3, "a flattened curve has intermediate points");
    }

    [Fact]
    public void Parse_SeparatesMultipleSubpaths()
    {
        var subpaths = SvgGlyph.Parse("M0 0 L10 0 M50 50 L60 50");

        Assert.Equal(2, subpaths!.Count);
        Assert.Equal((50f, 50f), (subpaths[1][0].X, subpaths[1][0].Y));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("M0 0 A50 50 0 0 1 100 0")] // arc — unsupported, falls back to monogram
    [InlineData("L10 10")]                  // no leading move
    public void Parse_ReturnsNull_ForUnsupportedOrMalformed(string? d)
    {
        Assert.Null(SvgGlyph.Parse(d));
    }
}
