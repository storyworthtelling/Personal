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
}
