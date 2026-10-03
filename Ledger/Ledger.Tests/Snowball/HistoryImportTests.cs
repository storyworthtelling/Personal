using Index.Domain.Model;
using Index.Tests.Support;
using Xunit;

namespace Index.Tests.Snowball;

/// <summary>
/// Guard G8 (D03, D78): the import of the recorded 70-week history. The history itself is the owner's own
/// encrypted Drive file and is NOT in the repository; these tests run against an invented stand-in of the
/// same shape (<see cref="SyntheticSnowballHistory"/>), handed over a fake source. The bug this reintroduces
/// if it regresses: treating a formula week (the history's one interpolated week) as missing, or re-running
/// the import and silently doubling every row.
/// </summary>
public class HistoryImportTests
{
    private static readonly DateOnly Today = new(2026, 9, 27);
    private static readonly DateTimeOffset Now = new(Today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    private static async Task<Institution> SeedLoanAccountAsync(TestDatabase db, string name)
    {
        await using var context = db.CreateDbContext();
        var institution = new Institution
        {
            Name = name,
            AccessToken = string.Empty, // hand-entered, matching MainPage's own "empty token = manual" convention
            CreatedAtUtc = Now.UtcDateTime,
            Accounts = { new Account { Name = name, Kind = AccountKind.Loan, CurrentBalance = 0m } },
        };
        context.Institutions.Add(institution);
        await context.SaveChangesAsync();
        return institution;
    }

    private static async Task SeedAllTenAsync(TestDatabase db)
    {
        foreach (var name in SyntheticSnowballHistory.LoanNames)
        {
            await SeedLoanAccountAsync(db, name);
        }
    }

    [Fact]
    public void TheSyntheticHistoryHasTheRealOnesShape()
    {
        Assert.Equal(70, SyntheticSnowballHistory.Weeks.Count);
        Assert.All(SyntheticSnowballHistory.Weeks, w => Assert.Equal(DayOfWeek.Friday, w.DayOfWeek));
        Assert.Equal(10, SyntheticSnowballHistory.LoanNames.Count);
        Assert.Equal(700, SyntheticSnowballHistory.Rows.Count);
    }

    [Fact]
    public async Task Imports70WeeksForEveryLoan_WhenAllTenAccountsExistByExactName()
    {
        using var db = new TestDatabase();
        await SeedAllTenAsync(db);
        var harness = new HistoryImportHarness(db, Now);

        await harness.Service.ImportHistoryIfNeededAsync(default);

        await using var context = db.CreateDbContext();
        Assert.Equal(700, context.SnowballSnapshots.Count());

        // Every imported row is exactly what the history recorded, loan by loan and week by week.
        var nameById = context.Accounts.ToDictionary(a => a.Id, a => a.Name);
        var imported = context.SnowballSnapshots.AsEnumerable()
            .Select(s => (Loan: nameById[s.AccountId], s.WeekOf, s.Balance))
            .OrderBy(r => r.Loan).ThenBy(r => r.WeekOf).ToList();
        var recorded = SyntheticSnowballHistory.Rows
            .Select(r => (r.Loan, WeekOf: r.Week, r.Balance))
            .OrderBy(r => r.Loan).ThenBy(r => r.WeekOf).ToList();
        Assert.Equal(recorded, imported);
    }

    [Fact]
    public async Task TheInterpolatedWeekIsKeptAsRecorded_NotDroppedAsMissing()
    {
        using var db = new TestDatabase();
        await SeedAllTenAsync(db);
        var harness = new HistoryImportHarness(db, Now);

        await harness.Service.ImportHistoryIfNeededAsync(default);

        await using var context = db.CreateDbContext();
        var mortgageId = context.Accounts.Single(a => a.Name == SyntheticSnowballHistory.MortgageName).Id;
        var interpolated = context.SnowballSnapshots
            .Single(s => s.AccountId == mortgageId && s.WeekOf == SyntheticSnowballHistory.InterpolatedWeek);
        Assert.True(interpolated.Balance > 0m);
        Assert.Equal(SyntheticSnowballHistory.BalanceOf(SyntheticSnowballHistory.MortgageName, SyntheticSnowballHistory.InterpolatedWeek), interpolated.Balance);

        // It really is the midpoint of its two neighbours, to the cent — the shape of a formula week.
        var before = SyntheticSnowballHistory.BalanceOf(SyntheticSnowballHistory.MortgageName, SyntheticSnowballHistory.InterpolatedWeek.AddDays(-7));
        var after = SyntheticSnowballHistory.BalanceOf(SyntheticSnowballHistory.MortgageName, SyntheticSnowballHistory.InterpolatedWeek.AddDays(7));
        Assert.True(Math.Abs(interpolated.Balance - ((before + after) / 2m)) <= 0.01m);
    }

    [Fact]
    public async Task ALoanThatStartsMidHistoryIsZeroBeforeItAppearsAndPositiveFrom()
    {
        using var db = new TestDatabase();
        await SeedAllTenAsync(db);
        await new HistoryImportHarness(db, Now).Service.ImportHistoryIfNeededAsync(default);

        await using var context = db.CreateDbContext();
        var id = context.Accounts.Single(a => a.Name == SyntheticSnowballHistory.StartsMidHistoryName).Id;
        var series = context.SnowballSnapshots.Where(s => s.AccountId == id).AsEnumerable().OrderBy(s => s.WeekOf).ToList();

        Assert.Equal(70, series.Count);
        Assert.All(series.Where(s => s.WeekOf < SyntheticSnowballHistory.MidHistoryLoanStarts), s => Assert.Equal(0m, s.Balance));
        Assert.All(series.Where(s => s.WeekOf >= SyntheticSnowballHistory.MidHistoryLoanStarts), s => Assert.True(s.Balance > 0m));
    }

    [Fact]
    public async Task ALoanThatReachesZeroStaysAtZero()
    {
        using var db = new TestDatabase();
        await SeedAllTenAsync(db);
        await new HistoryImportHarness(db, Now).Service.ImportHistoryIfNeededAsync(default);

        await using var context = db.CreateDbContext();
        var id = context.Accounts.Single(a => a.Name == SyntheticSnowballHistory.PaidOffName).Id;
        var tail = context.SnowballSnapshots.Where(s => s.AccountId == id && s.WeekOf >= SyntheticSnowballHistory.PaidOffFrom).ToList();

        Assert.NotEmpty(tail);
        Assert.All(tail, s => Assert.Equal(0m, s.Balance));
    }

    [Fact]
    public async Task IsIdempotent_RunningTwiceProducesTheSameRowCount()
    {
        using var db = new TestDatabase();
        await SeedAllTenAsync(db);
        var harness = new HistoryImportHarness(db, Now);

        await harness.Service.ImportHistoryIfNeededAsync(default);
        int firstCount;
        await using (var context = db.CreateDbContext())
        {
            firstCount = context.SnowballSnapshots.Count();
        }

        await harness.Service.ImportHistoryIfNeededAsync(default);
        int secondCount;
        await using (var context = db.CreateDbContext())
        {
            secondCount = context.SnowballSnapshots.Count();
        }

        Assert.Equal(firstCount, secondCount);
        Assert.Equal(700, secondCount);
    }

    [Fact]
    public async Task SkipsAnUnmatchedHistoryLoan_WithoutThrowing_WhenOnlySomeAccountsExist()
    {
        using var db = new TestDatabase();
        // Only three of the ten loans exist on this install — the ordinary case before every real loan is
        // connected.
        await SeedLoanAccountAsync(db, SyntheticSnowballHistory.PaidOffName);
        await SeedLoanAccountAsync(db, "Meridian Card");
        await SeedLoanAccountAsync(db, SyntheticSnowballHistory.MortgageName);
        var harness = new HistoryImportHarness(db, Now);

        var exception = await Record.ExceptionAsync(() => harness.Service.ImportHistoryIfNeededAsync(default));

        Assert.Null(exception);
        await using var context = db.CreateDbContext();
        Assert.Equal(3 * 70, context.SnowballSnapshots.Count());
    }

    [Fact]
    public async Task DoesNothing_WhenNoAccountMatchesAnyHistoryLoan()
    {
        using var db = new TestDatabase();
        await SeedLoanAccountAsync(db, "Some Unrelated Loan");
        var harness = new HistoryImportHarness(db, Now);

        var exception = await Record.ExceptionAsync(() => harness.Service.ImportHistoryIfNeededAsync(default));

        Assert.Null(exception);
        await using var context = db.CreateDbContext();
        Assert.Empty(context.SnowballSnapshots);
        Assert.False(harness.Memory.Imported); // nothing came in, so the next open tries again
    }
}
