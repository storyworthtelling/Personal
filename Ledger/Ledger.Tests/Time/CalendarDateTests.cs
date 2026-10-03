using Microsoft.Extensions.Options;
using Index.Domain.Model;
using Index.Domain.Time;
using Index.Infrastructure.Configuration;
using Index.Infrastructure.External;
using Index.Infrastructure.Services;
using Index.Tests.Support;
using Xunit;

namespace Index.Tests.Time;

/// <summary>
/// Guard R-G10: a date-only value shows the same calendar day wherever the phone is. The test project
/// cannot change the machine's zone, so each zone is a <see cref="TimeZoneInfo.CreateCustomTimeZone"/>
/// (no tzdata needed) and the value is converted INTO it the way <c>.LocalDateTime</c> does — which is
/// exactly what the display sites used to do before reading the day.
/// </summary>
public class CalendarDateTests
{
    private static readonly DateTimeOffset StoredOct3 = new(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);

    private static TimeZoneInfo Zone(int hours) =>
        TimeZoneInfo.CreateCustomTimeZone($"UTC{hours:+0;-0}", TimeSpan.FromHours(hours), $"UTC{hours:+0;-0}", $"UTC{hours:+0;-0}");

    public static TheoryData<int> Offsets => new() { -7, 0, 10 };

    [Theory]
    [MemberData(nameof(Offsets))]
    public void AStoredDateNamesTheSameDayInAnyZone(int hours)
    {
        // What a UTC-7 phone's ".LocalDateTime" is handed: the same instant, expressed in the viewer's zone.
        var presented = TimeZoneInfo.ConvertTime(StoredOct3, Zone(hours));

        Assert.Equal(new DateOnly(2026, 10, 3), CalendarDate.Of(presented));
        Assert.Equal("Oct 3", CalendarDate.Short(presented));
    }

    [Fact]
    public void TheBugThisReplacesIsRealInAUtcMinusSevenZone()
    {
        // Characterises the old behaviour so the zone fixture is known to bite: converting the stored
        // instant into UTC-7 and reading the day gives Oct 2 — "Due Oct 2" for an Oct 3 bill.
        var presented = TimeZoneInfo.ConvertTime(StoredOct3, Zone(-7));

        Assert.Equal("Oct 2", presented.DateTime.ToString("MMM d", System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("Oct 3", CalendarDate.Short(presented));
    }

    [Theory]
    [InlineData(-7, 2026, 9, 26)] // 02:00 UTC on Sep 27 is still Sep 26 evening on a US phone
    [InlineData(0, 2026, 9, 27)]
    [InlineData(10, 2026, 9, 27)]
    public void PhoneDayIsTheDayAnInstantFallsOnForThePersonHoldingThePhone(int hours, int year, int month, int day)
    {
        // The counterpart of Of(): a stored DATE must not be converted, a MOMENT must be (remediation 3.3 —
        // a receipt with no statement date is dated the phone's day, not UTC's).
        var instant = new DateTimeOffset(2026, 9, 27, 2, 0, 0, TimeSpan.Zero);

        Assert.Equal(new DateOnly(year, month, day), CalendarDate.PhoneDay(instant, Zone(hours)));
    }

    [Theory]
    [MemberData(nameof(Offsets))]
    public void AHandEnteredDateIsStoredAndReadBackAsTheDayPicked(int hours)
    {
        var picked = new DateTime(2026, 10, 3); // what a DatePicker returns: a local calendar day, kind Unspecified

        var stored = CalendarDate.ToStored(CalendarDate.FromPicker(picked));

        Assert.Equal(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero), stored);
        Assert.Equal(new DateOnly(2026, 10, 3), CalendarDate.Of(TimeZoneInfo.ConvertTime(stored, Zone(hours))));
    }

    [Theory]
    [MemberData(nameof(Offsets))]
    public void ThePickerIsHandedTheStoredDayNotItsLocalInstant(int hours)
    {
        // Re-saving a card used to write the picker's value back, so a day-early picker walked the
        // stored date one day earlier on every save.
        var presented = TimeZoneInfo.ConvertTime(StoredOct3, Zone(hours));

        var forPicker = CalendarDate.ForPicker(presented);

        Assert.Equal(new DateTime(2026, 10, 3), forPicker);
        Assert.Equal(DateTimeKind.Unspecified, forPicker.Kind);
    }

    [Theory]
    [MemberData(nameof(Offsets))]
    public async Task AHandEnteredDueDateSurvivesTheDatabaseAndShowsTheSameDay(int hours)
    {
        using var db = new TestDatabase();
        var service = new AccountService(db, new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero)));

        await service.AddManualAccountAsync(
            "Solar loan", "Evergreen Home Lending", AccountKind.Loan, 9000m, 6.25m, 250m, PaymentCadence.Month,
            CalendarDate.ToStored(new DateOnly(2026, 10, 3)), default);

        var account = Assert.Single((await service.GetInstitutionsAsync(default)).Single().Accounts);
        var viewed = TimeZoneInfo.ConvertTime(account.NextPaymentDueDate!.Value, Zone(hours));
        Assert.Equal("Oct 3", CalendarDate.Short(viewed));
    }

    [Theory]
    [MemberData(nameof(Offsets))]
    public async Task APlaidDateShowsTheSameDayInAnyZone(int hours)
    {
        var handler = new FakeHttpMessageHandler();
        var client = new PlaidClient(
            new HttpClient(handler),
            Options.Create(new PlaidOptions { ClientId = "c", Secret = "s", Environment = "sandbox" }));
        handler.EnqueueOk("""
        { "liabilities": { "credit": [ { "account_id": "card1", "aprs": [], "last_statement_balance": 1.0,
          "minimum_payment_amount": 25.0, "next_payment_due_date": "2026-10-03" } ], "mortgage": [], "student": [] },
          "request_id": "r" }
        """);
        var liability = Assert.Single(await client.GetLiabilitiesAsync("token", CancellationToken.None));

        var viewed = TimeZoneInfo.ConvertTime(liability.NextPaymentDueDate!.Value, Zone(hours));

        Assert.Equal("Oct 3", CalendarDate.Short(viewed));
    }
}
