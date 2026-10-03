using Index.Domain.Model;
using Index.Infrastructure.Services;
using Index.Tests.Support;
using Xunit;

namespace Index.Tests.Snowball;

/// <summary>
/// R-G9 (D78, mockup 6). The History side is the sheet's "Loans Over Time" tab: one column per WEEK,
/// every imported column dated on a Friday. <c>RecordCurrentWeekSnapshotAsync</c> used to key on TODAY,
/// so History grew a "week" for every day the app was opened — and nothing failed: each row was valid,
/// the unique index held, the chart simply filled with days. The bug is only visible as a count.
/// </summary>
public class SnowballWeekTests
{
    // A Friday, standing in for an imported week; the history's own weeks are all Fridays.
    private static readonly DateOnly Friday = new(2026, 9, 25);

    [Theory]
    [InlineData(2026, 9, 25, 2026, 9, 25)] // Friday is its own week
    [InlineData(2026, 9, 26, 2026, 9, 25)] // Saturday — the day after the sheet's Friday
    [InlineData(2026, 9, 27, 2026, 9, 25)] // Sunday
    [InlineData(2026, 9, 28, 2026, 9, 25)] // Monday
    [InlineData(2026, 9, 29, 2026, 9, 25)] // Tuesday
    [InlineData(2026, 9, 30, 2026, 9, 25)] // Wednesday
    [InlineData(2026, 10, 1, 2026, 9, 25)] // Thursday — the last day before the next Friday
    [InlineData(2026, 10, 2, 2026, 10, 2)] // the next Friday begins a new week, across a month boundary
    [InlineData(2027, 1, 1, 2027, 1, 1)]   // a Friday that is also New Year's Day
    [InlineData(2027, 1, 3, 2027, 1, 1)]   // Sunday: the Friday is in the previous year's last week of data
    [InlineData(2026, 1, 1, 2025, 12, 26)] // Thursday: the Friday is in the previous year
    public void Of_IsTheFridayOnOrBefore(int y, int m, int d, int expectedYear, int expectedMonth, int expectedDay)
    {
        var week = SnowballWeek.Of(new DateOnly(y, m, d));

        Assert.Equal(new DateOnly(expectedYear, expectedMonth, expectedDay), week);
        Assert.Equal(DayOfWeek.Friday, week.DayOfWeek);
    }

    [Fact]
    public void Of_EveryDayOfAYear_LandsOnAFridayNoMoreThanSixDaysBack()
    {
        for (var day = new DateOnly(2026, 1, 1); day.Year == 2026; day = day.AddDays(1))
        {
            var week = SnowballWeek.Of(day);
            Assert.Equal(DayOfWeek.Friday, week.DayOfWeek);
            Assert.InRange(day.DayNumber - week.DayNumber, 0, 6);
        }
    }

    [Fact]
    public void Of_MatchesEveryWeekTheSheetItselfImports()
    {
        // The history is the sheet's own columns (here an invented stand-in of the same shape). If any were
        // not a Friday the premise of the helper would be wrong, so this is the evidence for it rather than
        // an assumption.
        var weeks = SyntheticSnowballHistory.Weeks;

        Assert.NotEmpty(weeks);
        Assert.All(weeks, w => Assert.Equal(w, SnowballWeek.Of(w)));
    }

    private static async Task<Account> SeedLoanAsync(TestDatabase db, string name, decimal balance)
    {
        await using var context = db.CreateDbContext();
        var institution = new Institution { Name = name, AccessToken = string.Empty, CreatedAtUtc = Friday.ToDateTime(TimeOnly.MinValue) };
        var account = new Account { Name = name, Kind = AccountKind.Loan, CurrentBalance = balance, Apr = 0.10m, NextPaymentAmount = 100m };
        institution.Accounts.Add(account);
        context.Institutions.Add(institution);
        await context.SaveChangesAsync();
        return account;
    }

    // Noon UTC, so the machine's local date is the same calendar day in any zone the suite is likely to run in.
    private static FakeClock ClockOn(DateOnly day) => new(new DateTimeOffset(day.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero));

    [Fact]
    public async Task TenOpensAcrossOneWeek_WriteExactlyOneRowPerLoan_DatedOnThatWeeksFriday()
    {
        using var db = new TestDatabase();
        await SeedLoanAsync(db, "A", 1000m);
        await SeedLoanAsync(db, "B", 2000m);
        var clock = ClockOn(Friday);
        var service = new SnowballService(db, clock);

        // Friday itself through Thursday, with a second open on three of the days: ten opens in all.
        var opens = 0;
        for (var offset = 0; offset < 7; offset++)
        {
            await service.RecordCurrentWeekSnapshotAsync(default);
            opens++;
            if (offset is 1 or 3 or 5)
            {
                await service.RecordCurrentWeekSnapshotAsync(default);
                opens++;
            }
            clock.Advance(TimeSpan.FromDays(1));
        }

        Assert.Equal(10, opens);
        await using var context = db.CreateDbContext();
        Assert.Equal(2, context.SnowballSnapshots.Count());
        Assert.All(context.SnowballSnapshots, s => Assert.Equal(Friday, s.WeekOf));
    }

    [Fact]
    public async Task AnOpenAfterTheNextFriday_WritesASecondRowPerLoan()
    {
        using var db = new TestDatabase();
        await SeedLoanAsync(db, "A", 1000m);
        var clock = ClockOn(Friday.AddDays(3)); // Monday of the first week
        var service = new SnowballService(db, clock);

        await service.RecordCurrentWeekSnapshotAsync(default);
        clock.Advance(TimeSpan.FromDays(3)); // Thursday — still the first week
        await service.RecordCurrentWeekSnapshotAsync(default);
        clock.Advance(TimeSpan.FromDays(1)); // the next Friday
        await service.RecordCurrentWeekSnapshotAsync(default);

        await using var context = db.CreateDbContext();
        Assert.Equal([Friday, Friday.AddDays(7)], context.SnowballSnapshots.Select(s => s.WeekOf).OrderBy(w => w).ToList());
    }

    [Fact]
    public async Task OpeningTheAppOnTheDayAfterTheLastImportedFriday_DoesNotAddAWeek()
    {
        // The real situation: the import's last column is a Friday; the owner opens on the Saturday after it.
        using var db = new TestDatabase();
        var loan = await SeedLoanAsync(db, "A", 1000m);
        await using (var context = db.CreateDbContext())
        {
            context.SnowballSnapshots.Add(new SnowballSnapshot { AccountId = loan.Id, WeekOf = Friday, Balance = 1100m });
            await context.SaveChangesAsync();
        }

        await new SnowballService(db, ClockOn(Friday.AddDays(1))).RecordCurrentWeekSnapshotAsync(default);

        await using var verify = db.CreateDbContext();
        var snapshot = Assert.Single(verify.SnowballSnapshots);
        Assert.Equal(1100m, snapshot.Balance); // the imported row is untouched
    }
}
