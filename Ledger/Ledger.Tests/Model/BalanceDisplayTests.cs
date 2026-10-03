using Index.Domain.Model;
using Index.Domain.Time;
using Xunit;

namespace Index.Tests.Model;

/// <summary>
/// Remediation 3.3 item 3 (mockup 15): a loan Post can read a statement for "updates itself and says 'From
/// Post, Sep 12' instead" of "Updated Sep 12". The account therefore has to know where its balance came
/// from (<see cref="BalanceSource"/>), and the date has to be read the right way for each source: a
/// statement's date is a DATE (stored at UTC midnight, like a due date) and must not be pushed back a day
/// by the phone's zone, while a hand-typed balance's as-of is an INSTANT and is correctly shown in local time.
/// </summary>
public class BalanceDisplayTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    // A statement dated Sep 12, stored the way ApplyBalanceReceiptAsync stores it.
    private static readonly DateTimeOffset StatementDay = CalendarDate.ToStored(new DateOnly(2026, 9, 12));

    // ---- which source an account has ----

    [Fact]
    public void ARowWithNoRecordedSourceIsReadFromItsAccountSource_SoOldRowsAreStillRight()
    {
        Assert.Equal(BalanceSource.ByHand, BalanceDisplay.SourceOf(new Account { Source = AccountSource.Manual }));
        Assert.Equal(BalanceSource.Plaid, BalanceDisplay.SourceOf(new Account { Source = AccountSource.Plaid }));
    }

    [Fact]
    public void ARecordedSourceWins()
    {
        Assert.Equal(BalanceSource.Post, BalanceDisplay.SourceOf(
            new Account { Source = AccountSource.Manual, BalanceSource = BalanceSource.Post }));
        Assert.Equal(BalanceSource.Post, BalanceDisplay.SourceOf(
            new Account { Source = AccountSource.Plaid, BalanceSource = BalanceSource.Post }));
    }

    // ---- the line on the post-it ----

    [Fact]
    public void APostBalanceSaysFromPostAndTheStatementsDay_MockupFifteen()
    {
        var account = new Account { BalanceSource = BalanceSource.Post, BalanceAsOfUtc = StatementDay };

        Assert.Equal("From Post, Sep 12", BalanceDisplay.UpdatedLine(account, Now));
    }

    [Fact]
    public void AStatementsDayIsNotShiftedBackADayOnAUsPhone()
    {
        // The stored value is Sep 12 at UTC midnight. A phone in UTC-7 that converted it would print Sep 11.
        // The Post line reads the stored day (CalendarDate), whatever offset the value is presented in.
        var account = new Account
        {
            BalanceSource = BalanceSource.Post,
            BalanceAsOfUtc = StatementDay.ToOffset(TimeSpan.FromHours(-7)),
        };

        Assert.Equal("From Post, Sep 12", BalanceDisplay.UpdatedLine(account, Now));
    }

    [Fact]
    public void AHandTypedBalanceSaysUpdatedAndItsLocalDay()
    {
        // An instant: noon UTC is the same calendar day in every zone from UTC-11 to UTC+11.
        var typed = new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);
        var account = new Account { Source = AccountSource.Manual, BalanceAsOfUtc = typed };

        Assert.Equal("Updated Sep 12", BalanceDisplay.UpdatedLine(account, Now));
    }

    private static TimeZoneInfo Zone(int hours) =>
        TimeZoneInfo.CreateCustomTimeZone($"UTC{hours:+0;-0}", TimeSpan.FromHours(hours), $"UTC{hours:+0;-0}", $"UTC{hours:+0;-0}");

    [Theory]
    [InlineData(-7)]
    [InlineData(0)]
    [InlineData(10)]
    public void APostStatementDayIsTheSameDayInAnyZone(int hours)
    {
        // The test machine's own zone is UTC, so the phone's zone is stood in for: a date read through the
        // phone's zone (the bug) prints Sep 11 at UTC-7; the stored day does not move.
        var account = new Account { BalanceSource = BalanceSource.Post, BalanceAsOfUtc = StatementDay };

        Assert.Equal("From Post, Sep 12", BalanceDisplay.UpdatedLine(account, Now, Zone(hours)));
    }

    [Theory]
    [InlineData(-7, "Updated Sep 12")] // 03:00 UTC on Sep 13 is the evening of Sep 12 on a US phone
    [InlineData(0, "Updated Sep 13")]
    [InlineData(10, "Updated Sep 13")]
    public void ATypedBalanceIsAnInstantAndIsShownOnThePhonesDay(int hours, string expected)
    {
        var typed = new DateTimeOffset(2026, 9, 13, 3, 0, 0, TimeSpan.Zero);
        var account = new Account { Source = AccountSource.Manual, BalanceAsOfUtc = typed };

        Assert.Equal(expected, BalanceDisplay.UpdatedLine(account, Now, Zone(hours)));
    }

    [Fact]
    public void NoAsOfMeansNoLine() =>
        Assert.Null(BalanceDisplay.UpdatedLine(new Account { BalanceSource = BalanceSource.Post }, Now));

    [Fact]
    public void ADateInAnotherYearCarriesItsYear()
    {
        var account = new Account { BalanceSource = BalanceSource.Post, BalanceAsOfUtc = CalendarDate.ToStored(new DateOnly(2025, 12, 30)) };

        Assert.Equal("From Post, Dec 30, 2025", BalanceDisplay.UpdatedLine(account, Now));
    }

    // ---- the stale-balance notice (Phase 3.5) knows about the source ----

    private static Institution HandEntered(Account account) => new()
    {
        Name = "Evergreen Home Lending",
        AccessToken = string.Empty,
        Accounts = [account],
    };

    [Fact]
    public void AFreshPostBalanceRaisesNoStaleNotice()
    {
        var account = new Account
        {
            Name = "Home improvement loan", Source = AccountSource.Manual,
            BalanceSource = BalanceSource.Post, BalanceAsOfUtc = StatementDay, // two weeks old
        };

        Assert.Empty(AccountNotices.Build([HandEntered(account)], Now, lastRefreshFailed: false));
    }

    [Fact]
    public void AFreshPostBalanceDatedToday_RaisesNoStaleNotice()
    {
        var account = new Account
        {
            Name = "Home improvement loan", Source = AccountSource.Manual,
            BalanceSource = BalanceSource.Post, BalanceAsOfUtc = CalendarDate.ToStored(new DateOnly(2026, 9, 26)),
        };

        Assert.Empty(AccountNotices.Build([HandEntered(account)], Now, lastRefreshFailed: false));
    }

    [Fact]
    public void AStalePostBalanceStillRaisesTheNotice_AndDoesNotClaimItWasSetByHand()
    {
        var oldStatement = CalendarDate.ToStored(new DateOnly(2026, 8, 12));
        var account = new Account
        {
            Name = "Home improvement loan", Source = AccountSource.Manual,
            BalanceSource = BalanceSource.Post, BalanceAsOfUtc = oldStatement,
        };

        var notice = Assert.Single(AccountNotices.Build([HandEntered(account)], Now, lastRefreshFailed: false));

        Assert.Equal("Evergreen Home Lending is a month old", notice.Headline);
        Assert.DoesNotContain("by hand", notice.Detail, StringComparison.Ordinal);
        Assert.Contains("Aug 12", notice.Detail, StringComparison.Ordinal);
        Assert.Contains("Post", notice.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AStalePostBalanceNamesTheStatementsDay_NotADayEarlierInAUsZone()
    {
        // Aug 12 stored at UTC midnight; AccountNotices used to render it through .LocalDateTime, which
        // reads Aug 11 in any UTC-minus zone. Presented here at UTC-7 to prove the day survives the offset.
        var oldStatement = CalendarDate.ToStored(new DateOnly(2026, 8, 12)).ToOffset(TimeSpan.FromHours(-7));
        var account = new Account
        {
            Name = "Home improvement loan", Source = AccountSource.Manual,
            BalanceSource = BalanceSource.Post, BalanceAsOfUtc = oldStatement,
        };

        var notice = Assert.Single(AccountNotices.Build([HandEntered(account)], Now, lastRefreshFailed: false));

        Assert.Contains("Aug 12", notice.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("Aug 11", notice.Detail, StringComparison.Ordinal);
    }
}
