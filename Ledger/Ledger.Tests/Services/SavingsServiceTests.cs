using Index.Domain.Model;
using Index.Infrastructure.Services;
using Index.Tests.Support;
using Xunit;

namespace Index.Tests.Services;

public class SavingsServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private static SavingsService Create(TestDatabase db) => new(db, new FakeClock(Now));

    private static async Task<(Institution Institution, Account Account)> SeedAsync(
        TestDatabase db, string institutionName, string accountName, decimal balance,
        string? subtype = "savings", bool? savingsOverride = null)
    {
        await using var context = db.CreateDbContext();
        var institution = new Institution { Name = institutionName, AccessToken = string.Empty, CreatedAtUtc = Now.UtcDateTime };
        var account = new Account
        {
            Name = accountName,
            Kind = AccountKind.Depository,
            Subtype = subtype,
            IsSavingsOverride = savingsOverride,
            CurrentBalance = balance,
        };
        institution.Accounts.Add(account);
        context.Institutions.Add(institution);
        await context.SaveChangesAsync();
        return (institution, account);
    }

    [Fact]
    public async Task GetAccounts_OnlyIncludesEffectivelySavingsAccounts_InInstitutionThenAccountOrder()
    {
        using var db = new TestDatabase();
        await SeedAsync(db, "Lakeside CU", "Share savings", 1_350m);
        await SeedAsync(db, "Redwood Bank", "Savings", 8_200m);
        await SeedAsync(db, "Redwood Bank", "Checking", 900m, subtype: "checking"); // not savings — excluded

        var service = Create(db);
        var summary = await service.GetAccountsAsync(default);

        Assert.Equal(["Share savings", "Savings"], summary.Accounts.Select(a => a.AccountName));
        Assert.Equal(9_550m, summary.TotalSaved);
    }

    [Fact]
    public async Task GetAccounts_AManualAccount_CountsAsSavingsOnlyViaTheOwnerOverride()
    {
        using var db = new TestDatabase();
        // A hand-entered account has no Plaid Subtype (D56's own remark) — only the owner's override
        // can put it on this tab.
        await SeedAsync(db, "Cash", "Envelope", 300m, subtype: null, savingsOverride: null);
        await SeedAsync(db, "Cash", "Vacation fund", 500m, subtype: null, savingsOverride: true);

        var service = Create(db);
        var summary = await service.GetAccountsAsync(default);

        Assert.Equal(["Vacation fund"], summary.Accounts.Select(a => a.AccountName));
    }

    [Fact]
    public async Task NoGoalYet_HasNullGoalAndNoProjection()
    {
        using var db = new TestDatabase();
        await SeedAsync(db, "Lakeside CU", "Share savings", 1_350m);

        var service = Create(db);
        var summary = await service.GetAccountsAsync(default);

        var view = Assert.Single(summary.Accounts);
        Assert.Null(view.GoalTargetAmount);
        Assert.Null(view.ReachesIt);
    }

    [Fact]
    public async Task SetGoal_ThenGetAccounts_ProjectsFromRecordedHistory()
    {
        using var db = new TestDatabase();
        var (_, account) = await SeedAsync(db, "Redwood Bank", "Savings", 8_200m);

        await using (var context = db.CreateDbContext())
        {
            context.SavingsSnapshots.Add(new SavingsSnapshot { AccountId = account.Id, MonthOf = new DateOnly(2026, 6, 1), Balance = 7_480m });
            context.SavingsSnapshots.Add(new SavingsSnapshot { AccountId = account.Id, MonthOf = new DateOnly(2026, 9, 1), Balance = 8_200m });
            await context.SaveChangesAsync();
        }

        var service = Create(db);
        await service.SetGoalAsync(account.Id, 15_000m, default);

        var summary = await service.GetAccountsAsync(default);
        var view = Assert.Single(summary.Accounts);

        Assert.Equal(15_000m, view.GoalTargetAmount);
        Assert.Equal(3, view.MonthsOfHistory);
        Assert.Equal((8_200m - 7_480m) / 3, view.MonthlyPace);
        Assert.NotNull(view.ReachesIt);
        Assert.False(view.ReachesIt!.Value.IsNotAtThisPace);
    }

    [Fact]
    public async Task SetGoal_TwiceOnTheSameAccount_UpdatesInPlace_NeverASecondRow()
    {
        using var db = new TestDatabase();
        var (_, account) = await SeedAsync(db, "Redwood Bank", "Savings", 8_200m);

        var service = Create(db);
        await service.SetGoalAsync(account.Id, 15_000m, default);
        await service.SetGoalAsync(account.Id, 18_000m, default);

        await using var context = db.CreateDbContext();
        var goals = context.SavingsGoals.Where(g => g.AccountId == account.Id).ToList();
        Assert.Single(goals);
        Assert.Equal(18_000m, goals[0].TargetAmount);
    }

    [Fact]
    public async Task RemoveGoal_ThenRestore_BringsBackTheExactRow_AccountUntouched()
    {
        using var db = new TestDatabase();
        var (_, account) = await SeedAsync(db, "Redwood Bank", "Savings", 8_200m);

        var service = Create(db);
        await service.SetGoalAsync(account.Id, 15_000m, default);
        var captured = await service.FindGoalAsync(account.Id, default);
        Assert.NotNull(captured);

        await service.RemoveGoalAsync(account.Id, default);
        Assert.Null(await service.FindGoalAsync(account.Id, default));

        await service.RestoreGoalAsync(captured!, default);
        var restored = await service.FindGoalAsync(account.Id, default);
        Assert.NotNull(restored);
        Assert.Equal(15_000m, restored!.TargetAmount);
        Assert.Equal(captured.Id, restored.Id);

        // The account itself was never touched by the removal.
        var summary = await service.GetAccountsAsync(default);
        Assert.Single(summary.Accounts);
    }

    [Fact]
    public async Task RecordCurrentMonthSnapshot_CalledTwiceInTheSameMonth_WritesOnlyOneRow()
    {
        using var db = new TestDatabase();
        await SeedAsync(db, "Redwood Bank", "Savings", 8_200m);

        var service = Create(db);
        await service.RecordCurrentMonthSnapshotAsync(default);
        await service.RecordCurrentMonthSnapshotAsync(default);

        await using var context = db.CreateDbContext();
        Assert.Single(context.SavingsSnapshots);
        Assert.Equal(new DateOnly(2026, 9, 1), context.SavingsSnapshots.Single().MonthOf);
    }

    [Fact]
    public async Task RecordCurrentMonthSnapshot_OnlyEverForEffectivelySavingsAccounts()
    {
        using var db = new TestDatabase();
        await SeedAsync(db, "Redwood Bank", "Checking", 900m, subtype: "checking");

        var service = Create(db);
        await service.RecordCurrentMonthSnapshotAsync(default);

        await using var context = db.CreateDbContext();
        Assert.Empty(context.SavingsSnapshots);
    }

    [Fact]
    public async Task GetGrowth_ReturnsPointsInChronologicalOrder_ForEveryEffectivelySavingsAccount()
    {
        using var db = new TestDatabase();
        var (_, account) = await SeedAsync(db, "Redwood Bank", "Savings", 8_200m);

        await using (var context = db.CreateDbContext())
        {
            context.SavingsSnapshots.Add(new SavingsSnapshot { AccountId = account.Id, MonthOf = new DateOnly(2026, 9, 1), Balance = 8_200m });
            context.SavingsSnapshots.Add(new SavingsSnapshot { AccountId = account.Id, MonthOf = new DateOnly(2026, 6, 1), Balance = 7_480m });
            await context.SaveChangesAsync();
        }

        var service = Create(db);
        var growth = await service.GetGrowthAsync(default);

        var series = Assert.Single(growth);
        Assert.Equal([new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 1)], series.Points.Select(p => p.MonthOf));
        Assert.Null(series.GoalTargetAmount); // no goal set — a plain line, no dashed reference (D109's sibling rule)
    }

    private const string TintedColour = "plaid-primary-colour"; // opaque: PostItTint parses it, not this

    // 1.3c: the goal post-it is tinted from the bank that holds the account, so the view carries it.
    [Fact]
    public async Task GetAccounts_EachViewCarriesItsBanksPrimaryColour()
    {
        using var db = new TestDatabase();
        var (institution, _) = await SeedAsync(db, "Redwood Bank", "Savings", 8_200m);
        await SeedAsync(db, "Lakeside CU", "Share savings", 1_350m);
        await using (var context = db.CreateDbContext())
        {
            context.Institutions.Single(i => i.Id == institution.Id).PrimaryColorHex = TintedColour;
            await context.SaveChangesAsync();
        }

        var summary = await Create(db).GetAccountsAsync(default);

        Assert.Equal(TintedColour, summary.Accounts.Single(a => a.InstitutionName == "Redwood Bank").BankPrimaryColorHex);
        Assert.Null(summary.Accounts.Single(a => a.InstitutionName == "Lakeside CU").BankPrimaryColorHex);
    }
}
