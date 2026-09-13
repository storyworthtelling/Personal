using Folio.Domain.Analysis;
using Xunit;

namespace Folio.Tests.Analysis;

// The pure length-vs-genre-norm framing that opens the report: how far short (the owner's headline need),
// within the norm, or over. Deterministic, no model call.
public class LengthGapTests
{
    [Fact]
    public void BelowTheRange_SaysHowFarShort()
    {
        var note = LengthGap.Describe(wordCount: 72_000, targetLow: 90_000, targetHigh: 100_000);

        Assert.Contains("short", note, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("18,000", note);                 // 90,000 − 72,000, rounded to the nearest thousand
        Assert.Contains("90,000", note);
        Assert.Contains("100,000", note);
    }

    [Fact]
    public void WithinTheRange_SaysWithin()
    {
        var note = LengthGap.Describe(wordCount: 95_000, targetLow: 90_000, targetHigh: 100_000);

        Assert.Contains("within", note, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("short", note, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("over", note, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AboveTheRange_SaysHowFarOver()
    {
        var note = LengthGap.Describe(wordCount: 130_000, targetLow: 90_000, targetHigh: 100_000);

        Assert.Contains("over", note, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("30,000", note);                 // 130,000 − 100,000
    }

    [Fact]
    public void OnTheBoundary_CountsAsWithin()
    {
        Assert.Contains("within", LengthGap.Describe(90_000, 90_000, 100_000), System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("within", LengthGap.Describe(100_000, 90_000, 100_000), System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingOrInvalidRange_FallsBackToABareCount()
    {
        // No genre range supplied (the model didn't return one) — still report the count, no norm claim.
        var note = LengthGap.Describe(wordCount: 72_000, targetLow: 0, targetHigh: 0);
        Assert.Contains("72,000", note);
        Assert.DoesNotContain("short", note, System.StringComparison.OrdinalIgnoreCase);

        // An inverted range is treated as no range, not a nonsense gap.
        var inverted = LengthGap.Describe(wordCount: 72_000, targetLow: 100_000, targetHigh: 90_000);
        Assert.Contains("72,000", inverted);
        Assert.DoesNotContain("short", inverted, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoWordCount_IsEmpty()
    {
        Assert.Equal(string.Empty, LengthGap.Describe(wordCount: 0, targetLow: 90_000, targetHigh: 100_000));
    }
}
