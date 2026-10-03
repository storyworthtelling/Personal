using Index.Domain.Model;
using Index.Domain.Time;
using Xunit;

namespace Index.Tests.Model;

/// <summary>
/// Remediation 3.3 items 4, 5, 7 and 8 (mockups 2 and 15): the small words on the Accounts page that the
/// build either left blank or got wrong — "No payment yet", "By hand", "Checking ····7301" and the absolute
/// "As of Sep 23". All pure, so they are decided here rather than inside the page.
/// </summary>
public class AccountCardTextTests
{
    // Midday UTC, so the calendar day is the same in every zone from UTC-11 to UTC+11.
    private static DateTimeOffset Noon(int month, int day, int year = 2026) =>
        new(year, month, day, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Now = Noon(10, 3);

    // ---- item 4: "No payment yet" ----

    [Fact]
    public void ALoanWithNoDueDateSaysNoPaymentYet_MockupFifteen() =>
        Assert.Equal("No payment yet", AccountCardText.DueLine(due: null, paymentText: null));

    [Fact]
    public void ALoanWithNoDueDateSaysNoPaymentYet_EvenIfAPaymentAmountIsKnown() =>
        Assert.Equal("No payment yet", AccountCardText.DueLine(due: null, paymentText: "$250.00"));

    [Fact]
    public void ADueDateWithAPaymentReadsDueDateDotAmount() =>
        Assert.Equal("Due Oct 15 · $250.00",
            AccountCardText.DueLine(CalendarDate.ToStored(new DateOnly(2026, 10, 15)), "$250.00"));

    [Fact]
    public void ADueDateAloneReadsJustTheDate() =>
        Assert.Equal("Due Oct 3", AccountCardText.DueLine(CalendarDate.ToStored(new DateOnly(2026, 10, 3)), null));

    [Fact]
    public void TheDueDateIsTheStoredDay_NotShiftedByTheOffsetItIsPresentedIn()
    {
        var presentedInUsZone = CalendarDate.ToStored(new DateOnly(2026, 10, 3)).ToOffset(TimeSpan.FromHours(-7));
        Assert.Equal("Due Oct 3", AccountCardText.DueLine(presentedInUsZone, null));
    }

    // ---- item 5: "By hand" ----

    [Fact]
    public void AHandEnteredBankSaysByHand_NotHandEntered()
    {
        var note = AccountCardText.HeadingNote(failed: false, lastRefreshed: Noon(9, 23), handEntered: true, Now);

        Assert.NotNull(note);
        Assert.Equal("By hand", note!.Text);
        Assert.False(note.IsProblem);
        Assert.Equal("By hand", AccountCardText.ByHand);
    }

    [Fact]
    public void AConnectedHealthyBankSaysNothing() =>
        Assert.Null(AccountCardText.HeadingNote(failed: false, lastRefreshed: Noon(9, 23), handEntered: false, Now));

    // ---- item 8: the absolute "As of Sep 23" ----

    [Fact]
    public void ABrokenConnectionSaysAsOfTheAbsoluteDate_NotARelativePhrase()
    {
        var note = AccountCardText.HeadingNote(failed: true, lastRefreshed: Noon(9, 23), handEntered: false, Now);

        Assert.NotNull(note);
        Assert.Equal("As of Sep 23", note!.Text); // mockup 2 / D32 — not "As of 10d ago" or "As of Wednesday"
        Assert.True(note.IsProblem);
    }

    private static TimeZoneInfo Zone(int hours) =>
        TimeZoneInfo.CreateCustomTimeZone($"UTC{hours:+0;-0}", TimeSpan.FromHours(hours), $"UTC{hours:+0;-0}", $"UTC{hours:+0;-0}");

    [Theory]
    [InlineData(-7, "As of Sep 23")] // 03:00 UTC on Sep 24 is the evening of Sep 23 on a US phone
    [InlineData(0, "As of Sep 24")]
    [InlineData(10, "As of Sep 24")]
    public void TheAsOfIsAnInstantAndIsShownOnThePhonesDay(int hours, string expected)
    {
        var lastGood = new DateTimeOffset(2026, 9, 24, 3, 0, 0, TimeSpan.Zero);

        var note = AccountCardText.HeadingNote(failed: true, lastGood, handEntered: false, Now, Zone(hours));

        Assert.Equal(expected, note!.Text);
    }

    [Fact]
    public void AsOfFromAnotherYearCarriesTheYear()
    {
        var note = AccountCardText.HeadingNote(failed: true, lastRefreshed: Noon(9, 23, 2025), handEntered: false, Now);

        Assert.Equal("As of Sep 23, 2025", note!.Text);
    }

    [Fact]
    public void ABrokenConnectionWithNoFiguresYetSaysNothing_ThereIsNoDateToGive() =>
        Assert.Null(AccountCardText.HeadingNote(failed: true, lastRefreshed: null, handEntered: false, Now));

    [Fact]
    public void AsOfOutranksByHand_ForAHandEnteredBankThatNeverFails()
    {
        // A hand-entered bank is never marked Failed (AccountNotices guards the same), but if the flag were
        // ever set the date is the more urgent thing to say.
        var note = AccountCardText.HeadingNote(failed: true, lastRefreshed: Noon(9, 23), handEntered: true, Now);

        Assert.Equal("As of Sep 23", note!.Text);
    }

    // ---- item 7: subtype labels ----

    [Theory]
    [InlineData("checking", "Checking")]
    [InlineData("savings", "Savings")]
    [InlineData("cd", "CD")]
    [InlineData("money market", "Money market")]
    [InlineData("CHECKING", "Checking")]   // Plaid's strings are lowercase, but the match must not depend on it
    [InlineData("cash management", "Cash management")]
    [InlineData("hsa", "HSA")]
    [InlineData("paypal", "PayPal")]
    [InlineData("prepaid", "Prepaid")]
    public void ADepositAccountIsNamedByItsPlaidSubtype(string subtype, string expected) =>
        Assert.Equal(expected, AccountCardText.KindLabel(AccountKind.Depository, subtype));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("something plaid adds next year")]
    public void AnUnknownOrMissingSubtypeFallsBackToAccount(string? subtype) =>
        Assert.Equal("Account", AccountCardText.KindLabel(AccountKind.Depository, subtype));

    [Theory]
    [InlineData(AccountKind.Credit, "credit card", "Credit card")]
    [InlineData(AccountKind.Loan, "auto", "Loan")]
    [InlineData(AccountKind.Loan, null, "Loan")]
    [InlineData(AccountKind.Investment, "401k", "Investment")]
    [InlineData(AccountKind.Other, null, "Account")]
    public void TheOtherKindsKeepTheirOwnWords(AccountKind kind, string? subtype, string expected) =>
        Assert.Equal(expected, AccountCardText.KindLabel(kind, subtype));

    [Fact]
    public void TheMaskedLabelReadsSubtypeThenFourDots()
    {
        Assert.Equal("Checking ····7301", AccountCardText.MaskedLabel(AccountKind.Depository, "checking", "7301"));
        Assert.Equal("Savings ····7302", AccountCardText.MaskedLabel(AccountKind.Depository, "savings", "7302"));
        Assert.Equal("Credit card ····7306", AccountCardText.MaskedLabel(AccountKind.Credit, "credit card", "7306"));
        Assert.Equal("Checking", AccountCardText.MaskedLabel(AccountKind.Depository, "checking", null));
        Assert.Equal("Account", AccountCardText.MaskedLabel(AccountKind.Depository, null, ""));
    }
    // ---- remediation 3.8: a hand-entered card's limit reads exactly as a Plaid card's (D125) ----

    [Fact]
    public void ALimitLineReadsOfTheLimit()
    {
        Assert.Equal("of $5,000.00", AccountCardText.LimitLine("$5,000.00"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoLimitIsNoLine(string? none)
    {
        Assert.Equal(string.Empty, AccountCardText.LimitLine(none));
    }
}
