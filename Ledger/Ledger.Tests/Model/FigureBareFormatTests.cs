using Index.Domain.Model;
using Xunit;

namespace Index.Tests.Model;

/// <summary>
/// Mockup 15's boxed figures draw their "$" and "%" as the box's own prefix and suffix, so the text inside
/// is the bare number (grouped, with its cents) and reads back through the parser as the same figure.
/// </summary>
public class FigureBareFormatTests
{
    [Theory]
    [InlineData(18640.52, FigureKind.Money, "18,640.52")]
    [InlineData(420, FigureKind.Money, "420.00")]
    [InlineData(6.75, FigureKind.Percent, "6.75")]
    [InlineData(21.5, FigureKind.Percent, "21.5")]
    public void TheBoxShowsTheNumberWithoutItsSymbol(double value, FigureKind kind, string expected) =>
        Assert.Equal(expected, FigureParser.FormatBare((decimal)value, kind));

    [Fact]
    public void ANegativeBalanceKeepsItsSign() =>
        Assert.Equal("-1,200.50", FigureParser.FormatBare(-1200.5m, FigureKind.Money));

    [Theory]
    [InlineData(18640.52, FigureKind.Money)]
    [InlineData(6.75, FigureKind.Percent)]
    [InlineData(0.125, FigureKind.Percent)]
    public void WhatIsShownReadsBackAsTheSameFigure(double value, FigureKind kind)
    {
        var shown = FigureParser.FormatBare((decimal)value, kind);
        var parsed = FigureParser.Parse(shown, kind);

        Assert.Equal(FigureStatus.Valid, parsed.Status);
        Assert.Equal((decimal)value, parsed.Value);
    }

    [Fact]
    public void ASecondFormatChangesNothing()
    {
        var once = FigureParser.FormatBare(18640.52m, FigureKind.Money);
        var again = FigureParser.FormatBare(FigureParser.Parse(once, FigureKind.Money).Value, FigureKind.Money);
        Assert.Equal(once, again);
    }

    [Fact]
    public void TheAffixesAreTheMockupsOwn()
    {
        Assert.Equal("$", FigureAffix.PrefixOf(FigureKind.Money));
        Assert.Null(FigureAffix.PrefixOf(FigureKind.Percent));
        Assert.Equal("%", FigureAffix.SuffixOf(FigureKind.Percent));
        Assert.Null(FigureAffix.SuffixOf(FigureKind.Money));
    }
}
