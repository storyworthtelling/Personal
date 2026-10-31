using Constitution.Domain.Meals;
using static Constitution.Tests.MealTaste.TasteData;

namespace Constitution.Tests.MealTaste;

/// <summary>The rating record: three verdicts, the words they are stored and printed in, and the optional written reason.</summary>
public class TasteRatingTests
{
    [Fact]
    public void TheVerdictWordsAreFrozen()
    {
        Assert.Equal("loved", TasteWords.Verdicts.Word(TasteVerdict.Loved));
        Assert.Equal("fine", TasteWords.Verdicts.Word(TasteVerdict.Fine));
        Assert.Equal("never-again", TasteWords.Verdicts.Word(TasteVerdict.NeverAgain));
    }

    [Theory]
    [InlineData("Loved", TasteVerdict.Loved)]
    [InlineData("  fine ", TasteVerdict.Fine)]
    [InlineData("Never again", TasteVerdict.NeverAgain)]
    [InlineData("never   AGAIN", TasteVerdict.NeverAgain)]
    [InlineData("NEVER-AGAIN", TasteVerdict.NeverAgain)]
    public void APrintedLaneIsReadAsItsVerdict(string printed, TasteVerdict expected) =>
        Assert.Equal(expected, TasteWords.FromPrinted(printed));

    [Theory]
    [InlineData("Meh")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Loved it")]
    public void AnythingElseIsNoVerdict(string? printed) => Assert.Null(TasteWords.FromPrinted(printed));

    [Fact]
    public void TheReasonIsTrimmedAndBlankIsNone()
    {
        Assert.Equal("too dry", Rate(Sam, Risotto, TasteVerdict.NeverAgain, reason: "  too dry ").Reason);
        Assert.Null(Rate(Sam, Risotto, TasteVerdict.Fine, reason: "   ").Reason);
        Assert.Null(Rate(Sam, Risotto, TasteVerdict.Fine).Reason);
    }

    [Fact]
    public void AReasonHasALength()
    {
        Assert.Equal(TasteRating.MaxReasonLength, Rate(Sam, Risotto, TasteVerdict.Fine, reason: new string('a', TasteRating.MaxReasonLength)).Reason!.Length);
        Assert.Throws<ArgumentException>(() => Rate(Sam, Risotto, TasteVerdict.Fine, reason: new string('a', TasteRating.MaxReasonLength + 1)));
    }

    [Fact]
    public void AWithExpressionIsCheckedBeforeItIsWritten()
    {
        var rating = Rate(Sam, Risotto, TasteVerdict.Fine) with { Reason = new string('a', TasteRating.MaxReasonLength + 1) };

        Assert.Throws<ArgumentException>(rating.EnsureValid);
    }

    [Fact]
    public void ToStringNamesNoReasonAndNoDish()
    {
        var text = Rate(Sam, Risotto, TasteVerdict.NeverAgain, reason: "far too salty").ToString();

        Assert.DoesNotContain("salty", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Risotto.Value, text, StringComparison.Ordinal);
        Assert.DoesNotContain("sam", text, StringComparison.OrdinalIgnoreCase);
    }
}
