using Index.Domain.Model;
using Index.Domain.Time;
using Xunit;

namespace Index.Tests.Model;

/// <summary>
/// Remediation 3.3 item 1 (mockup 3): each transaction on the post-it reads "Yesterday · Groceries",
/// "Tuesday · Fuel", "Sep 21 · Household" — a relative day inside the week, else the date, then the OWNER's
/// category name. The build printed "MMM d" and nothing else.
///
/// <para>A transaction's posted date is a date-only value stored at UTC midnight, so the day is read
/// through <see cref="CalendarDate"/> and never converted to the phone's zone; "today" is the phone's
/// local day, handed in as a <see cref="DateOnly"/> so these tests do not depend on the machine's zone.</para>
/// </summary>
public class TransactionRowTextTests
{
    // A Wednesday, as the mockup is drawn.
    private static readonly DateOnly Today = new(2026, 9, 23);

    private static DateTimeOffset Stored(int month, int day, int year = 2026) =>
        CalendarDate.ToStored(new DateOnly(year, month, day));

    [Theory]
    [InlineData(9, 23, "Today")]
    [InlineData(9, 22, "Yesterday")]   // mockup 3: "Yesterday · Groceries"
    [InlineData(9, 21, "Monday")]      // inside the week: the weekday
    [InlineData(9, 17, "Thursday")]    // six days back is still "this week"
    [InlineData(9, 16, "Sep 16")]      // seven days back: the date, as mockup 3's "Sep 21"
    [InlineData(8, 30, "Aug 30")]
    public void TheDayIsRelativeInsideTheWeekAndTheDateBeyondIt(int month, int day, string expected) =>
        Assert.Equal(expected, TransactionRowText.Day(new DateOnly(2026, month, day), Today));

    [Fact]
    public void ADayInAnotherYearCarriesItsYear() =>
        Assert.Equal("Dec 30, 2025", TransactionRowText.Day(new DateOnly(2025, 12, 30), Today));

    [Fact]
    public void ADayAfterTodayIsTheDate_NotAnElapsedTimePhrase() =>
        Assert.Equal("Sep 25", TransactionRowText.Day(new DateOnly(2026, 9, 25), Today));

    [Fact]
    public void TheRowIsTheDayThenTheOwnersCategory() =>
        Assert.Equal("Yesterday · Groceries",
            TransactionRowText.WhenAndCategory(Stored(9, 22), Today, pending: false, "Groceries"));

    [Fact]
    public void AWeekdayRowCarriesItsCategory() =>
        Assert.Equal("Monday · Fuel",
            TransactionRowText.WhenAndCategory(Stored(9, 21), Today, pending: false, "Fuel"));

    [Fact]
    public void AnOlderRowCarriesTheDateAndItsCategory() =>
        Assert.Equal("Sep 16 · Household",
            TransactionRowText.WhenAndCategory(Stored(9, 16), Today, pending: false, "Household"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ATransactionWithNoStoredCategoryShowsNoCategory_RatherThanAGuess(string? categoryName) =>
        Assert.Equal("Yesterday",
            TransactionRowText.WhenAndCategory(Stored(9, 22), Today, pending: false, categoryName));

    [Fact]
    public void PendingIsStillMarked_AfterTheCategory() =>
        Assert.Equal("Today · Groceries · pending",
            TransactionRowText.WhenAndCategory(Stored(9, 23), Today, pending: true, "Groceries"));

    [Fact]
    public void PendingIsStillMarked_WithNoCategory() =>
        Assert.Equal("Today · pending",
            TransactionRowText.WhenAndCategory(Stored(9, 23), Today, pending: true, categoryName: null));

    [Fact]
    public void TheStoredMidnightIsReadAsItsOwnCalendarDay_NotShiftedIntoALocalZone()
    {
        // UTC midnight on Sep 22 is Sep 21 at 5 pm in UTC-7. The day is the stored day.
        var asStoredByPlaid = new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.Zero);
        var asRead = asStoredByPlaid.ToOffset(TimeSpan.FromHours(-7));

        Assert.Equal("Yesterday",
            TransactionRowText.WhenAndCategory(asRead, Today, pending: false, categoryName: null));
    }
}
