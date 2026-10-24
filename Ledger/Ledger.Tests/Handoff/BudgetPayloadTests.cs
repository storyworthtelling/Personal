using System.Globalization;
using Index.Domain.Handoff;
using Xunit;

namespace Index.Tests.Handoff;

public class BudgetPayloadTests
{
    [Fact]
    public void DecimalsAreExactText()
    {
        var payload = ConstitutionBudgetPayload.TryCreate("2026-10", 412.5m, 0.1m)!;

        Assert.Equal("412.50", payload.BudgetText);
        Assert.Equal("0.10", payload.SpentText);
        Assert.Equal("2026-10", payload.Month);
    }

    [Fact]
    public void TextIgnoresTheCurrentCulture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var payload = ConstitutionBudgetPayload.TryCreate("2026-10", 1234.5m, 7m)!;
            Assert.Equal("1234.50", payload.BudgetText);
            Assert.Equal("7.00", payload.SpentText);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Theory]
    [InlineData("2026-13")]
    [InlineData("2026-1")]
    [InlineData("26-10")]
    [InlineData("")]
    public void ABadMonthIsRefused(string month)
    {
        Assert.Null(ConstitutionBudgetPayload.TryCreate(month, 10m, 5m));
    }

    [Fact]
    public void NegativeAndOverflowAreRefused()
    {
        Assert.Null(ConstitutionBudgetPayload.TryCreate("2026-10", -1m, 5m));
        Assert.Null(ConstitutionBudgetPayload.TryCreate("2026-10", 10m, -0.01m));
        Assert.Null(ConstitutionBudgetPayload.TryCreate("2026-10", 10_000_000m, 5m));
        Assert.Null(ConstitutionBudgetPayload.TryCreate("2026-10", 10m, decimal.MaxValue));
    }

    [Fact]
    public void FractionsOfACentAreRefused()
    {
        Assert.Null(ConstitutionBudgetPayload.TryCreate("2026-10", 10.005m, 5m));
        Assert.NotNull(ConstitutionBudgetPayload.TryCreate("2026-10", 10.500m, 5m));
    }

    [Fact]
    public void FromFiguresHoldsRefundsAtZero()
    {
        var payload = ConstitutionBudgetPayload.FromFigures("2026-10", 300m, -12.34m)!;
        Assert.Equal("0.00", payload.SpentText);
    }

    [Fact]
    public void StoredTextRoundTrips()
    {
        var payload = ConstitutionBudgetPayload.TryCreate("2026-10", 412.5m, 99.99m)!;
        Assert.Equal(payload, ConstitutionBudgetPayload.TryParseStored(payload.ToStoredText()));
        Assert.Null(ConstitutionBudgetPayload.TryParseStored("garbage"));
        Assert.Null(ConstitutionBudgetPayload.TryParseStored(null));
    }
}
