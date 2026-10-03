using Index.Domain.Model;
using Xunit;

namespace Index.Tests.Model;

/// <summary>
/// Remediation 3.2 item 5 (mockup 8 frame 2, <c>.mh-m</c>): the history row's month cell is the month
/// name with a small "so far" line BENEATH it for the month in progress — not "September so far" run
/// together on one line in a column too narrow to hold it — and a month from another year carries its
/// year ("Sep '25"), because without it a row past a year boundary reads as the wrong month.
/// </summary>
public class HistoryRowTextTests
{
    [Fact]
    public void TheMonthInProgress_IsTheMonthName_WithSoFarBeneathIt()
    {
        var text = HistoryRowText.For("2026-09", isCurrentMonth: true, currentYear: 2026);

        Assert.Equal("September", text.Month);
        Assert.Equal("so far", text.SubLine);
    }

    [Fact]
    public void AnEarlierMonthInTheSameYear_IsJustTheName()
    {
        var text = HistoryRowText.For("2026-08", isCurrentMonth: false, currentYear: 2026);

        Assert.Equal("August", text.Month);
        Assert.Null(text.SubLine);
    }

    [Theory]
    [InlineData("2025-12", "Dec '25")]
    [InlineData("2025-09", "Sep '25")]
    [InlineData("2024-01", "Jan '24")]
    public void AMonthFromAnotherYear_CarriesItsYearInTheAxisLabelStyle(string yearMonth, string expected)
    {
        var text = HistoryRowText.For(yearMonth, isCurrentMonth: false, currentYear: 2026);

        Assert.Equal(expected, text.Month);
        Assert.Null(text.SubLine);
    }

    [Fact]
    public void TheYearLabelIsTheAxisHelpersOwn_NotASecondFormatting()
    {
        var text = HistoryRowText.For("2025-09", false, 2026);

        Assert.Equal(ChartAxis.MonthYearLabel(new DateOnly(2025, 9, 1)), text.Month);
    }
}
