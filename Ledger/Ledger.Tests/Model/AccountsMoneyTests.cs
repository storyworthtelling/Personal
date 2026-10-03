using Index.Domain.Model;
using Xunit;

namespace Index.Tests.Model;

/// <summary>
/// Phase 4, "Money and type" for Accounts (mockups 2, 3, 14, 15): a balance keeps its cents, a limit or a
/// payment is whole dollars ("of $23,000", "Due Oct 6 · $285"), and a minus is the real minus sign
/// (U+2212), which mockup 3 draws before a payment's amount. Invented figures throughout.
/// </summary>
public class AccountsMoneyTests
{
    private const string Minus = "−";

    [Theory]
    [InlineData(3846.20, "$3,846.20")]
    [InlineData(11400, "$11,400.00")]
    [InlineData(0, "$0.00")]
    [InlineData(7412.36, "$7,412.36")]
    public void ABalanceKeepsItsCents(double value, string expected) =>
        Assert.Equal(expected, AccountsMoney.Cents((decimal)value, "USD"));

    [Fact]
    public void ANegativeAmountUsesTheRealMinusSign_NotAHyphen()
    {
        var text = AccountsMoney.Cents(-520m, "USD");

        Assert.Equal(Minus + "$520.00", text);
        Assert.DoesNotContain("-", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ANoCurrencyCodeIsDollars_AndAnotherCodeIsNamed()
    {
        Assert.Equal("$12.00", AccountsMoney.Cents(12m, null));
        Assert.Equal("$12.00", AccountsMoney.Cents(12m, ""));
        Assert.Equal("CAD $12.00", AccountsMoney.Cents(12m, "CAD"));
    }

    [Theory]
    [InlineData(285, "$285")]
    [InlineData(23000, "$23,000")]
    [InlineData(16500, "$16,500")]
    [InlineData(0, "$0")]
    public void ALimitOrAPaymentIsWholeDollars(double value, string expected) =>
        Assert.Equal(expected, AccountsMoney.Whole((decimal)value, "USD"));

    [Theory]
    [InlineData(285.40, "$285")]
    [InlineData(285.50, "$286")]
    [InlineData(285.99, "$286")]
    public void CentsAreRoundedToTheNearestDollarNotShown(double value, string expected) =>
        Assert.Equal(expected, AccountsMoney.Whole((decimal)value, "USD"));

    [Fact]
    public void WholeDollarsAlsoUseTheRealMinus_AndNeverPrintAMinusZero()
    {
        Assert.Equal(Minus + "$520", AccountsMoney.Whole(-520m, "USD"));
        Assert.Equal("$0", AccountsMoney.Whole(-0.30m, "USD"));
    }

    [Fact]
    public void WholeDollarsNameAnotherCurrencyTheSameWayCentsDo() =>
        Assert.Equal("CAD $285", AccountsMoney.Whole(285m, "CAD"));
}
