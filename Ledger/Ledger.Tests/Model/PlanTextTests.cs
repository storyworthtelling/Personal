using System.Reflection;
using System.Text.RegularExpressions;
using Index.Domain.Household;
using Index.Domain.Model;
using Xunit;

namespace Index.Tests.Model;

/// <summary>
/// Phase 4 "Money and type" and "Copy" for Snowball, Budget and Savings (mockups 6, 7, 8, 9, 16): the words
/// and figures on the cards, held to what the mockups print. Figures are invented, as the mockups' are.
/// </summary>
public class PlanTextTests
{
    // ---- mockup 6 (Snowball) ----

    [Fact]
    public void TheDebtFreeDateIsTheMockupsAbbreviatedForm()
    {
        Assert.Equal("Sep 26, 2031", PlanText.DebtFreeDate(new DateOnly(2031, 9, 26)));
    }

    [Fact]
    public void ALoansPayoffLine_IsTheAbbreviatedDate()
    {
        Assert.Equal("Paid off Jul 16, 2027", PlanText.PaidOff(new DateOnly(2027, 7, 16)));
    }

    [Theory]
    [InlineData("465.38", "$466 a week now")]
    [InlineData("465.00", "$465 a week now")]
    [InlineData("465.01", "$466 a week now")]
    [InlineData("80", "$80 a week now")]
    public void WhatALoanIsPaidEachWeekIsWholeDollarsRoundedUp(string payment, string expected)
    {
        Assert.Equal(expected, PlanText.WeeklyNow(decimal.Parse(payment, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void TheFocusFigureOnThePostItIsWholeDollarsPerWeekRoundedUp()
    {
        Assert.Equal("$546/wk", PlanText.AsFocus(545.25m));
        Assert.Equal("$545/wk", PlanText.AsFocus(545.00m));
    }

    [Fact]
    public void ALimitAndAPaceKeepRoundingToNearestWhileAPaymentRoundsUp()
    {
        Assert.Equal("of $140", PlanText.OfLimit(140.42m));
        Assert.Equal("$305 a month", PlanText.Pace(305.42m));
        Assert.Equal("$141 a week now", PlanText.WeeklyNow(140.42m));
    }

    [Fact]
    public void TheExtraPaymentHintUsesACurlyApostrophe()
    {
        Assert.Equal("Added to the focus loan’s payment.", PlanText.ExtraHint);
    }

    // ---- mockups 7 and 8 (Budget) ----

    [Fact]
    public void ACategorysLimitIsWholeDollars()
    {
        Assert.Equal("of $625", PlanText.OfLimit(625m));
        Assert.Equal("of $140", PlanText.OfLimit(140.00m));
    }

    [Fact]
    public void TheMonthLinesAgreedTotalIsWholeDollars_WhileWhatIsSpentKeepsItsCents()
    {
        Assert.Equal(" of $1,995", PlanText.MonthOfAgreed(1995m));
        Assert.Equal("$1,629.40", PlanMoney.Balance(1629.40m));
    }

    // ---- mockups 9 and 16 (Savings) ----

    [Fact]
    public void ThePaceIsWholeDollarsAMonth()
    {
        Assert.Equal("$305 a month", PlanText.Pace(305.4m));
        Assert.Equal("$55 a month", PlanText.Pace(55m));
    }

    [Fact]
    public void TheHistoryNotePossessiveAgreesWithItsCount()
    {
        Assert.Equal("From the last 12 months’ growth.", PlanText.HistoryNote(12));
        Assert.Equal("From the last 4 months’ growth.", PlanText.HistoryNote(4));
        Assert.Equal("From the last 1 month’s growth.", PlanText.HistoryNote(1));
    }

    [Fact]
    public void TheGrowthFootnoteUsesCurlyApostrophes()
    {
        Assert.Equal(
            "Monthly, from each month’s last balance. The dotted line is the last year’s pace, carried forward.",
            PlanText.GrowthFootnote);
    }

    // ---- every sentence on these three pages ----

    [Fact]
    public void NoSentenceOnThesePagesCarriesAStraightApostropheInAWord()
    {
        var offenders = new List<string>();
        foreach (var type in new[] { typeof(PlanText), typeof(SnowballHistoryCopy), typeof(HouseholdChartCopy), typeof(SavingsGoalCopy) })
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static)
                         .Where(f => f.IsLiteral && f.FieldType == typeof(string)))
            {
                var text = (string)field.GetRawConstantValue()!;
                if (Regex.IsMatch(text, "[A-Za-z]'[A-Za-z]"))
                {
                    offenders.Add($"{type.Name}.{field.Name}: {text}");
                }
            }
        }

        Assert.True(offenders.Count == 0, "Straight apostrophes in copy (use U+2019):\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void TheHistoryFootnoteAndEmptyStateAlsoUseCurlyApostrophes()
    {
        Assert.Contains("loan’s", SnowballHistoryCopy.EmptyHistory, StringComparison.Ordinal);
        Assert.DoesNotMatch("[A-Za-z]'[A-Za-z]", SnowballHistoryCopy.Footnote(new DateOnly(2023, 6, 2)));
        Assert.DoesNotMatch("[A-Za-z]'[A-Za-z]", SnowballHistoryCopy.Footnote(null));
    }
}
