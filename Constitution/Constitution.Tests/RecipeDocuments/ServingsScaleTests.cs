using Constitution.Domain.Documents;
using SkiaSharp;
using Storyworthtelling.Paper;

namespace Constitution.Tests.RecipeDocuments;

/// <summary>
/// C-2.5.1: quantities are written text ("¾ cup"), and a wrong number at the counter is worse than an
/// unscaled one, so anything the scaler cannot read exactly is left as the card has it, and said so.
/// </summary>
public class ServingsScaleTests
{
    private static ScaledQuantity Scale(string? quantity, int from, int to) => ServingsScale.Scale(quantity, from, to);

    [Theory]
    [InlineData("2 lb", 2, 4, "4 lb")]
    [InlineData("3 tbsp", 6, 4, "2 tbsp")]
    [InlineData("12 oz", 4, 6, "18 oz")]
    [InlineData("1", 6, 3, "½")]
    [InlineData("4", 4, 8, "8")]
    public void ScalesWholeNumbers(string quantity, int from, int to, string expected)
    {
        var scaled = Scale(quantity, from, to);

        Assert.Equal(expected, scaled.Text);
        Assert.Equal(ScaleKind.Scaled, scaled.Kind);
    }

    [Theory]
    [InlineData("¾ cup", 4, 8, "1½ cups")]
    [InlineData("1/2 tsp", 2, 6, "1½ tsp")]
    [InlineData("1 1/2 cups", 2, 4, "3 cups")]
    [InlineData("1½ tsp", 6, 4, "1 tsp")]
    [InlineData("1 ½ tsp", 6, 4, "1 tsp")]
    [InlineData("2¼ tsp", 6, 4, "1½ tsp")]
    [InlineData("0.75 cup", 4, 8, "1½ cups")]
    [InlineData(".5 tsp", 2, 4, "1 tsp")]
    [InlineData("1.5 lb", 4, 2, "¾ lb")]
    public void ReadsFractionsMixedNumbersAndDecimals(string quantity, int from, int to, string expected)
    {
        Assert.Equal(expected, Scale(quantity, from, to).Text);
    }

    [Theory]
    [InlineData("1 cup", 2, 4, "2 cups")]
    [InlineData("2 cups", 4, 2, "1 cup")]
    [InlineData("1 cup", 4, 3, "¾ cup")]
    [InlineData("1 can", 2, 6, "3 cans")]
    [InlineData("2 cloves garlic", 4, 2, "1 clove garlic")]
    [InlineData("1 tbsp", 2, 6, "3 tbsp")]
    public void PluralisesAKnownUnitByTheNewAmount(string quantity, int from, int to, string expected)
    {
        Assert.Equal(expected, Scale(quantity, from, to).Text);
    }

    [Theory]
    [InlineData("1 tsp", 6, 4, "2/3 tsp")]
    [InlineData("1 cup", 8, 1, "⅛ cup")]
    [InlineData("1 cup", 2, 3, "1½ cups")]
    [InlineData("1 cup", 3, 1, "1/3 cup")]
    [InlineData("1 cup", 3, 4, "1 1/3 cups")]
    [InlineData("3 cups", 8, 1, "⅜ cup")]
    [InlineData("5 cups", 8, 1, "⅝ cup")]
    [InlineData("7 cups", 8, 1, "⅞ cup")]
    public void WritesTheFriendlyFractionsExactly(string quantity, int from, int to, string expected)
    {
        Assert.Equal(expected, Scale(quantity, from, to).Text);
    }

    [Fact]
    public void RoundsToTheNearestFriendlyFractionWhenCloseEnough()
    {
        // 5/7 of a cup is 0.714: three quarters is 5 percent away, which a measuring cup cannot tell.
        Assert.Equal("¾ cup", Scale("1 cup", 7, 5).Text);
    }

    [Fact]
    public void WritesAnHonestDecimalWhenNoFriendlyFractionIsClose()
    {
        // 1/7 of a cup is 0.143; the nearest eighth is 12 percent off, so the number is written as it is.
        var scaled = Scale("1 cup", 7, 1);

        Assert.Equal("0.14 cup", scaled.Text);
        Assert.Equal(ScaleKind.Scaled, scaled.Kind);
    }

    [Theory]
    [InlineData("a pinch")]
    [InlineData("to taste")]
    [InlineData("a handful")]
    [InlineData("about 1 cup")]
    [InlineData("1-2 tsp")]
    [InlineData("1–2 tsp")]
    [InlineData("2 to 3 cloves")]
    [InlineData("1 or more cloves")]
    [InlineData("2 and a half cups")]
    [InlineData("1 or 2 limes")]
    [InlineData("1 cup (240 ml)")]
    [InlineData("1 (14 oz) can")]
    [InlineData("1,5 cups")]
    [InlineData("1.2.3 cups")]
    [InlineData("1 2 cups")]
    [InlineData("1/0 cup")]
    [InlineData("½-¾ cup")]
    [InlineData("2x")]
    [InlineData("99999999999 lb")]
    public void LeavesWhatItCannotReadExactlyAsTheCardHasIt(string quantity)
    {
        var scaled = Scale(quantity, 6, 4);

        Assert.Equal(quantity, scaled.Text);
        Assert.Equal(ScaleKind.NotScaled, scaled.Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void AnIngredientWithNoQuantityHasNothingToScale(string? quantity)
    {
        var scaled = Scale(quantity, 6, 4);

        Assert.Equal(string.Empty, scaled.Text);
        Assert.Equal(ScaleKind.AsWritten, scaled.Kind);
    }

    [Fact]
    public void TheSameServingsLeavesEveryQuantityAsWritten()
    {
        foreach (var quantity in new[] { "2 lb", "¾ cup", "a pinch", "1-2 tsp" })
        {
            var scaled = Scale(quantity, 4, 4);

            Assert.Equal(quantity, scaled.Text);
            Assert.Equal(ScaleKind.AsWritten, scaled.Kind);
        }
    }

    [Fact]
    public void TrimsTheCardsSpacing()
    {
        Assert.Equal("4 lb", Scale("  2   lb ", 2, 4).Text);
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(4, 0)]
    [InlineData(-1, 4)]
    public void RefusesServingsThatCannotBeScaledBy(int from, int to)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Scale("1 cup", from, to));
    }

    [Fact]
    public void ScalingTwiceThereAndBackReturnsTheCard()
    {
        foreach (var quantity in new[] { "2 lb", "¾ cup", "1½ tsp", "3 tbsp" })
        {
            var up = Scale(quantity, 4, 8);
            var back = Scale(up.Text, 8, 4);

            Assert.Equal(quantity, back.Text);
        }
    }

    [Fact]
    public void EveryGlyphTheScalerWritesIsInTheMonoFace()
    {
        var face = EmbeddedTypefaces.Mono;
        using var font = new SKFont(face, PaperTypeScale.Mono);

        foreach (var glyph in ServingsScale.GlyphsUsed)
        {
            Assert.True(font.ContainsGlyph(glyph), $"Cascadia Code has no glyph for '{glyph}' (U+{(int)glyph:X4}); the quantity column would print a box.");
        }
    }

    [Fact]
    public void TheGlyphsUsedAreTheCascadiaFractions()
    {
        Assert.Equal(new HashSet<char>("⅛¼⅜½⅝¾⅞"), ServingsScale.GlyphsUsed.ToHashSet());
    }
}
