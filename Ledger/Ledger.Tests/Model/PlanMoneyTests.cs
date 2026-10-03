using Index.Domain.Model;
using Xunit;

namespace Index.Tests.Model;

/// <summary>
/// Phase 4 "Money and type" (remediation plan §4; mockups 6, 7, 9, 16): balances keep their cents, limits,
/// payments and weekly figures are whole dollars, and a negative figure wears U+2212 (a true minus) rather
/// than the accountant's brackets en-US currency formatting gives it or a hyphen-minus.
/// </summary>
public class PlanMoneyTests
{
    private const string Minus = "−";

    [Theory]
    [InlineData("7412.36", "$7,412.36")]
    [InlineData("280842.11", "$280,842.11")]
    [InlineData("0", "$0.00")]
    [InlineData("11400", "$11,400.00")]
    public void ABalanceKeepsItsCents(string value, string expected)
    {
        Assert.Equal(expected, PlanMoney.Balance(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void ANegativeBalanceWearsATrueMinus_NotBracketsOrAHyphen()
    {
        var text = PlanMoney.Balance(-12.5m);

        Assert.Equal(Minus + "$12.50", text);
        Assert.DoesNotContain("(", text, StringComparison.Ordinal);
        Assert.DoesNotContain("-", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("625", "$625")]
    [InlineData("1995", "$1,995")]
    [InlineData("465.38", "$465")]
    [InlineData("545.5", "$546")]
    [InlineData("80", "$80")]
    public void AWholeDollarFigureHasNoCents_AndRoundsAwayFromZeroAtTheHalf(string value, string expected)
    {
        Assert.Equal(expected, PlanMoney.Whole(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void ANegativeWholeFigureAlsoWearsATrueMinus()
    {
        Assert.Equal(Minus + "$3", PlanMoney.Whole(-3m));
    }

    [Theory]
    [InlineData("6250", "−$6,250")]
    [InlineData("0", "−$0")]
    [InlineData("-500", "$500")]
    public void ADeductionRowPrintsTheSignedContributionAndNeverAMinusThenAMinus(string value, string expected)
    {
        Assert.Equal(expected, PlanMoney.Deduction(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Theory]
    [InlineData("285.00", "$285")]
    [InlineData("285.01", "$286")]
    [InlineData("285.42", "$286")]
    [InlineData("285.49", "$286")]
    [InlineData("285.50", "$286")]
    [InlineData("285.99", "$286")]
    [InlineData("0", "$0")]
    [InlineData("0.01", "$1")]
    [InlineData("1995", "$1,995")]
    [InlineData("1994.01", "$1,995")]
    public void APaymentRoundsUpToTheNextWholeDollarAndAnExactDollarIsUnchanged(string value, string expected)
    {
        Assert.Equal(expected, PlanMoney.Payment(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Theory]
    [InlineData("-285.00", "−$285")]
    [InlineData("-285.42", "−$285")]
    [InlineData("-285.99", "−$285")]
    [InlineData("-3.5", "−$3")]
    public void ANegativePaymentCeilsTowardPositiveInfinityAndWearsATrueMinus(string value, string expected)
    {
        Assert.Equal(expected, PlanMoney.Payment(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("-0.30")]
    [InlineData("-0.99")]
    [InlineData("-0")]
    public void ANegativePaymentThatCeilsToZeroNeverPrintsAMinusZero(string value)
    {
        var text = PlanMoney.Payment(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal("$0", text);
        Assert.DoesNotContain(Minus, text, StringComparison.Ordinal);
    }

    [Fact]
    public void APaymentIsFormattedInvariantlyUnderANonEnglishCulture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal("$1,995", PlanMoney.Payment(1994.01m));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void ALimitAndAWeeklyBalanceFigureStillRoundToNearestWhilePaymentRoundsUp()
    {
        Assert.Equal("$285", PlanMoney.Whole(285.42m));
        Assert.Equal("$286", PlanMoney.Payment(285.42m));
    }
}
