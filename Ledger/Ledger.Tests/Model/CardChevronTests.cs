using Index.Domain.Model;
using Xunit;

namespace Index.Tests.Model;

/// <summary>
/// Phase 4, "Chevrons": the cards' disclosure mark is a drawn down/up chevron (mockups 2 and 3's
/// <c>.ix-cv</c>, a 14 x 14 stroke), not the "▸/▾" text glyphs a font may draw as anything.
/// </summary>
public class CardChevronTests
{
    [Fact]
    public void ACollapsedCardPointsDown_AnOpenOneUp()
    {
        Assert.Equal(CardChevron.Down, CardChevron.For(expanded: false));
        Assert.Equal(CardChevron.Up, CardChevron.For(expanded: true));
    }

    [Fact]
    public void TheShapesAreTheMockupsOwnPaths()
    {
        Assert.Equal("M4 5.5 7 8.5 10 5.5", CardChevron.PathOf(CardChevron.Down));
        Assert.Equal("M4 8.5 7 5.5 10 8.5", CardChevron.PathOf(CardChevron.Up));
    }

    [Fact]
    public void TheMarkIsFourteenSquare_StrokedAtOnePointSix()
    {
        Assert.Equal(14, CardChevron.SizeDp);
        Assert.Equal(1.6m, CardChevron.StrokeDp);
    }

    [Theory]
    [InlineData("▸", CardChevron.Down)]   // the old collapsed glyph
    [InlineData("▾", CardChevron.Up)]     // the old expanded glyph
    [InlineData(CardChevron.Down, CardChevron.Down)]
    [InlineData(CardChevron.Up, CardChevron.Up)]
    public void AnOldGlyphOrANewNameResolvesToAShape(string token, string expected) =>
        Assert.Equal(expected, CardChevron.Resolve(token));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("more")]
    public void AnythingElseIsNotAChevron(string? token) => Assert.Null(CardChevron.Resolve(token));
}
