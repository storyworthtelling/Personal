using System.Globalization;
using Constitution.Domain.Budgets;

namespace Constitution.Tests.Budgets;

/// <summary>
/// The payload is money, so it travels as text and is read as text: a double would turn a figure Index typed into one it
/// did not, and a locale would turn a point into a comma. Invented figures throughout.
/// </summary>
public class BudgetPayloadTests
{
    private static Func<string, string?> Extras(string? month, string? budget, string? spent) =>
        key => key switch
        {
            "month" => month,
            "budget" => budget,
            "spent" => spent,
            _ => null,
        };

    [Fact]
    public void DecimalsAreExactText()
    {
        // More digits than a double holds: read through a double this figure comes back as 1E+15.
        const string Huge = "999999999999999.999999";
        Assert.NotEqual(Huge, double.Parse(Huge, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture));

        var parsed = BudgetPayload.Parse(Extras("2026-10", Huge, "412.50"));

        Assert.True(parsed.IsValid);
        Assert.Equal(Huge, parsed.Payload!.BudgetText);
        Assert.Equal("412.50", parsed.Payload.SpentText);
        Assert.Equal(999999999999999.999999m, parsed.Payload.Budget);
        Assert.Equal("0.3", BudgetPayload.FormatAmount(0.1m + 0.2m));
    }

    [Fact]
    public void TheScaleOfTheTextIsKept()
    {
        Assert.Equal("412.50", BudgetPayload.FormatAmount(412.50m));
        Assert.Equal("412.5", BudgetPayload.FormatAmount(412.5m));
        Assert.Equal("0", BudgetPayload.FormatAmount(0m));
    }

    [Fact]
    public void TextNeverFollowsTheCulture()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var parsed = BudgetPayload.Parse(Extras("2026-10", "1234.50", "7.25"));

            Assert.True(parsed.IsValid);
            Assert.Equal("1234.50", parsed.Payload!.BudgetText);
            Assert.Equal("7.25", BudgetPayload.FormatAmount(7.25m));
            Assert.False(BudgetPayload.TryParseAmount("1234,50", out _));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("-0")]
    [InlineData("-0.01")]
    [InlineData("+5")]
    [InlineData("1,5")]
    [InlineData("1,000.00")]
    [InlineData("1.5e3")]
    [InlineData("1e3")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1.")]
    [InlineData(".5")]
    [InlineData("1..5")]
    [InlineData("1_000")]
    [InlineData("0x10")]
    [InlineData("١٢")]
    [InlineData("1234567890123456")]
    [InlineData("1.1234567")]
    public void AMalformedOrNegativeAmountIsRefused(string text)
    {
        Assert.False(BudgetPayload.TryParseAmount(text, out _));

        var parsed = BudgetPayload.Parse(Extras("2026-10", text, "1.00"));
        Assert.False(parsed.IsValid);
        Assert.Equal("budget", parsed.RefusedField);
    }

    [Fact]
    public void ANullAmountIsRefused()
    {
        Assert.False(BudgetPayload.TryParseAmount(null, out _));
        Assert.Equal("spent", BudgetPayload.Parse(Extras("2026-10", "1.00", null)).RefusedField);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.00")]
    [InlineData("7")]
    [InlineData("412.50")]
    [InlineData("999999999999999.999999")]
    public void ARealAmountIsKeptAsWritten(string text)
    {
        Assert.True(BudgetPayload.TryParseAmount(text, out var amount));
        Assert.Equal(text, BudgetPayload.FormatAmount(amount));
    }

    [Theory]
    [InlineData("2026-10", true)]
    [InlineData("2027-01", true)]
    [InlineData("2026-12", true)]
    [InlineData("2026-1", false)]
    [InlineData("2026-13", false)]
    [InlineData("2026-00", false)]
    [InlineData("26-10", false)]
    [InlineData("2026/10", false)]
    [InlineData("2026-10-01", false)]
    [InlineData(" 2026-10", false)]
    [InlineData("2026-10 ", false)]
    [InlineData("October 2026", false)]
    [InlineData("٢٠٢٦-١٠", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TheMonthIsStrict(string? text, bool valid)
    {
        Assert.Equal(valid, BudgetPayload.IsMonth(text));
        var parsed = BudgetPayload.Parse(Extras(text, "10.00", "1.00"));
        Assert.Equal(valid, parsed.IsValid);
        if (!valid)
        {
            Assert.Equal("month", parsed.RefusedField);
        }
    }

    [Fact]
    public void ARefusalNamesTheFieldAndNeverTheValue()
    {
        var parsed = BudgetPayload.Parse(Extras("2026-10", "10.00", "-77.77"));

        Assert.Equal("spent", parsed.RefusedField);
        Assert.DoesNotContain("77", parsed.RefusedField, StringComparison.Ordinal);
        Assert.Null(parsed.Payload);
    }

    [Fact]
    public void AValidBroadcastIsRead()
    {
        var parsed = BudgetPayload.Parse(Extras("2026-10", "600.00", "412.50"));

        Assert.True(parsed.IsValid);
        Assert.Null(parsed.RefusedField);
        Assert.Equal("2026-10", parsed.Payload!.Month);
        Assert.Equal(600.00m, parsed.Payload.Budget);
        Assert.Equal(412.50m, parsed.Payload.Spent);
    }

    [Fact]
    public void TheConstructorRefusesWhatTheWireRefuses()
    {
        Assert.ThrowsAny<ArgumentException>(() => new BudgetPayload("2026-13", 1m, 1m));
        Assert.ThrowsAny<ArgumentException>(() => new BudgetPayload("2026-10", -1m, 1m));
        Assert.ThrowsAny<ArgumentException>(() => new BudgetPayload("2026-10", 1m, -0.01m));
    }

    [Fact]
    public void TheExtrasAreTheFrozenWords()
    {
        Assert.Equal(["month", "budget", "spent"], BudgetContract.Extras);
    }
}
