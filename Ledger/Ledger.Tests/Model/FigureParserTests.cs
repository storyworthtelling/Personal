using Index.Domain.Model;
using Xunit;

namespace Index.Tests.Model;

/// <summary>
/// Guard R-G16: "6.25%" saves 6.25, and a figure that cannot be read is never turned into a null.
/// The bug: both Accounts' post-it and the Add form read unparseable APR/Payment as null — typing "abc"
/// cleared a stored APR, and the mockup's own "6.25%" (a % decimal.TryParse rejects) saved the account
/// with no APR at all, silently leaving it out of the Snowball.
/// </summary>
public class FigureParserTests
{
    [Theory]
    [InlineData("6.25%", 6.25)]
    [InlineData(" 6.25 % ", 6.25)]
    [InlineData("6.25", 6.25)]
    [InlineData("7", 7)]
    [InlineData(".5", 0.5)]
    [InlineData("7.", 7)]
    [InlineData("0", 0)]
    public void APercentReadsWithOrWithoutItsSign(string text, double expected)
    {
        var result = FigureParser.Parse(text, FigureKind.Percent);
        Assert.Equal(FigureStatus.Valid, result.Status);
        Assert.Equal((decimal)expected, result.Value);
    }

    [Theory]
    [InlineData("$1,200", 1200)]
    [InlineData("1,200.50", 1200.50)]
    [InlineData("$1,200,000.01", 1200000.01)]
    [InlineData("  250  ", 250)]
    [InlineData("$250.00", 250)]
    [InlineData("12345.67", 12345.67)]
    [InlineData("$0.5", 0.5)]
    public void MoneyReadsWithItsDollarSignAndThousandsCommas(string text, double expected)
    {
        var result = FigureParser.Parse(text, FigureKind.Money);
        Assert.Equal(FigureStatus.Valid, result.Status);
        Assert.Equal((decimal)expected, result.Value);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1,2,3")]
    [InlineData("12,34")]
    [InlineData(",500")]
    [InlineData("1234,567")]
    [InlineData("6.25.1")]
    [InlineData("1e5")]
    [InlineData("$")]
    [InlineData(".")]
    [InlineData("-")]
    [InlineData("6.25%")]       // a % on money
    [InlineData("$$5")]
    [InlineData("$ 5")]
    [InlineData("5 5")]
    [InlineData("-5")]          // negative, where the caller did not allow it
    [InlineData("99999999999999999999999999999999")]
    public void ItRejectsEverythingElseAsMoney(string text)
    {
        Assert.Equal(FigureStatus.Invalid, FigureParser.Parse(text, FigureKind.Money).Status);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("$6.25")]       // a $ on a percent
    [InlineData("1,5")]
    [InlineData("1,200")]
    [InlineData("6.25%%")]
    [InlineData("%6.25")]
    [InlineData("%")]
    [InlineData("-7")]
    public void ItRejectsEverythingElseAsAPercent(string text)
    {
        Assert.Equal(FigureStatus.Invalid, FigureParser.Parse(text, FigureKind.Percent).Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void NothingTypedIsEmptyNotInvalidAndNotZero(string? text)
    {
        Assert.Equal(FigureStatus.Empty, FigureParser.Parse(text, FigureKind.Money).Status);
        Assert.Equal(FigureStatus.Empty, FigureParser.Parse(text, FigureKind.Percent).Status);
    }

    [Fact]
    public void ANegativeBalanceReadsOnlyWhereTheCallerAllowsIt()
    {
        var overdrawn = FigureParser.Parse("-$1,200.50", FigureKind.Money, allowNegative: true);
        Assert.Equal(FigureStatus.Valid, overdrawn.Status);
        Assert.Equal(-1200.50m, overdrawn.Value);
    }

    [Fact]
    public void TheReadingDoesNotDependOnThePhonesCulture()
    {
        var before = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal(1200.50m, FigureParser.Parse("1,200.50", FigureKind.Money).Value);
            Assert.Equal(6.25m, FigureParser.Parse("6.25%", FigureKind.Percent).Value);
            Assert.Equal("6.25", FigureParser.Format(6.25m, FigureKind.Percent));
            Assert.Equal("1200.50", FigureParser.Format(1200.5m, FigureKind.Money));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = before;
        }
    }

    [Fact]
    public void TheFixHintsSayHowToFixIt()
    {
        Assert.Equal("Type a number like 6.25.", FigureParser.FixHint(FigureKind.Percent));
        Assert.Equal("Type an amount like 250 or 1,200.50.", FigureParser.FixHint(FigureKind.Money));
    }

    // ---- an invalid figure never overwrites a stored value ----

    [Theory]
    [InlineData("abc", FigureKind.Percent)]
    [InlineData("abc", FigureKind.Money)]
    [InlineData("1,2,3", FigureKind.Money)]
    [InlineData("$6.25", FigureKind.Percent)]
    public void AnUnreadableFigureIsInvalidAndNeverAWriteOfNull(string text, FigureKind kind)
    {
        var edit = FigureEdit.Decide(text, kind, stored: 6.25m);

        Assert.Equal(FigureEditOutcome.Invalid, edit.Outcome);
    }

    [Fact]
    public void EmptyKeepsAStoredValueAndLeavesAnUnsetOneUnset()
    {
        Assert.Equal(FigureEditOutcome.NoChange, FigureEdit.Decide("", FigureKind.Percent, stored: 6.25m).Outcome);
        Assert.Equal(FigureEditOutcome.NoChange, FigureEdit.Decide("  ", FigureKind.Money, stored: null).Outcome);
    }

    [Fact]
    public void AReadableFigureThatDiffersIsAWriteAndOneThatMatchesIsNot()
    {
        var write = FigureEdit.Decide("6.25%", FigureKind.Percent, stored: null);
        Assert.Equal(FigureEditOutcome.Write, write.Outcome);
        Assert.Equal(6.25m, write.Value);

        Assert.Equal(FigureEditOutcome.NoChange, FigureEdit.Decide("6.250 %", FigureKind.Percent, stored: 6.25m).Outcome);
        Assert.Equal(FigureEditOutcome.Write, FigureEdit.Decide("$1,200", FigureKind.Money, stored: 1000m).Outcome);
    }
    // ---- remediation 3.8: what a box shows after a valid figure is left ("$12,345.67", "$250.00", "6.25%") ----

    [Theory]
    [InlineData("12345.67", FigureKind.Money, "$12,345.67")]
    [InlineData("250", FigureKind.Money, "$250.00")]
    [InlineData("0", FigureKind.Money, "$0.00")]
    [InlineData("1234567.8", FigureKind.Money, "$1,234,567.80")]
    [InlineData("6.25", FigureKind.Percent, "6.25%")]
    [InlineData("5", FigureKind.Percent, "5%")]
    public void ABlurWritesTheMockupsShape(string typed, FigureKind kind, string shown)
    {
        var value = FigureParser.Parse(typed, kind).Value;

        Assert.Equal(shown, FigureParser.FormatForBox(value, kind));
    }

    [Theory]
    [InlineData(12345.67, FigureKind.Money)]
    [InlineData(-1200.5, FigureKind.Money)]
    [InlineData(0.01, FigureKind.Money)]
    [InlineData(250.005, FigureKind.Money)]   // a stray fraction of a cent is shown, not rounded away
    [InlineData(6.25, FigureKind.Percent)]
    [InlineData(6.255, FigureKind.Percent)]   // more than two places survive too
    [InlineData(29.9, FigureKind.Percent)]
    public void WhatABlurWritesReadsBackAsTheSameFigure(double typed, FigureKind kind)
    {
        var value = (decimal)typed;

        var shown = FigureParser.FormatForBox(value, kind);
        var parsed = FigureParser.Parse(shown, kind, allowNegative: true);

        Assert.Equal(FigureStatus.Valid, parsed.Status);
        Assert.Equal(value, parsed.Value);
        Assert.Equal(shown, FigureParser.FormatForBox(parsed.Value, kind)); // and a second blur changes nothing
    }

    [Fact]
    public void TheBlurFormatDoesNotDependOnThePhonesCulture()
    {
        var before = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal("$1,200.50", FigureParser.FormatForBox(1200.5m, FigureKind.Money));
            Assert.Equal("6.25%", FigureParser.FormatForBox(6.25m, FigureKind.Percent));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = before;
        }
    }
}
