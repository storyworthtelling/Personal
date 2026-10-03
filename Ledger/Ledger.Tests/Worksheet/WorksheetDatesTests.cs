using System.Globalization;
using Index.Infrastructure.Worksheet;
using Xunit;

namespace Index.Tests.Worksheet;

/// <summary>
/// Phase 4 (cosmetic drift), worksheet pages: the words a printed page uses for months and dates come
/// from the month it was built for, never from a literal ("September's figure" is October's previous
/// month; January's is December's) and never from the machine's culture (a page printed on a phone set to
/// another language still says "Sep 30" — the whole page is English).
/// </summary>
public class WorksheetDatesTests
{
    [Theory]
    [InlineData("2026-10", "September")]
    [InlineData("2026-01", "December")]
    [InlineData("2026-03", "February")]
    [InlineData("2027-06", "May")]
    public void PreviousMonthWord_IsDerivedFromTheTargetMonth(string yearMonth, string expected) =>
        Assert.Equal(expected, WorksheetDates.PreviousMonthWord(yearMonth));

    [Theory]
    [InlineData("2026-10", "Sept")]
    [InlineData("2026-01", "Dec")]
    [InlineData("2026-07", "June")]
    [InlineData("2026-06", "May")]
    [InlineData("2026-03", "Feb")]
    public void PreviousMonthShort_IsTheMockupsSeptStyleAbbreviation(string yearMonth, string expected) =>
        Assert.Equal(expected, WorksheetDates.PreviousMonthShort(yearMonth));

    [Fact]
    public void MonthWordAndMonthYear_NameTheTargetMonth()
    {
        Assert.Equal("October", WorksheetDates.MonthWord("2026-10"));
        Assert.Equal("October 2026", WorksheetDates.MonthYear("2026-10"));
    }

    [Fact]
    public void PrintedOn_IsInvariantWhateverTheCurrentCultureIs()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            var odd = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            string[] months = ["jan.", "fév.", "mar.", "avr.", "mai", "juin", "juil.", "août", "sept.", "oct.", "nov.", "déc.", ""];
            odd.DateTimeFormat.AbbreviatedMonthNames = months;
            odd.DateTimeFormat.AbbreviatedMonthGenitiveNames = months; // "MMM d" reads the genitive table
            odd.DateTimeFormat.MonthDayPattern = "d MMM";
            CultureInfo.CurrentCulture = odd;

            Assert.Equal("Sep 30", WorksheetDates.PrintedOn(new DateOnly(2026, 9, 30)));
            Assert.Equal("Oct 2", WorksheetDates.PrintedOn(new DateOnly(2026, 10, 2)));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
