using Index.Domain.External;
using Index.Domain.Household;
using Index.Domain.Model;
using Index.Infrastructure.Household;
using Index.Infrastructure.Services;
using Index.Tests.Support;
using Moq;
using Xunit;

namespace Index.Tests.Household;

/// <summary>
/// Remediation 3.2 item 6 (mockup 11, "everything not listed looks and behaves as in mockups 2-10"):
/// her Budget is the owner's Budget read from the household file, so the two phones must say the same
/// thing. They did not. Her month total summed the categories only, where the owner's also carries the
/// spending nobody has categorised yet; the unreadable-limit card appeared on her phone but nothing
/// raised the dog-ear for it; and tracking-only had no last-month comparison to publish.
///
/// <para>The proof is end to end: one database, the owner's figures read straight from
/// <see cref="BudgetService.GetMonthFiguresAsync"/>, hers read from the file the publisher wrote, and the
/// two compared.</para>
/// </summary>
public class HouseholdBudgetParityTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 9, 0, 0, TimeSpan.Zero);
    private static DateTimeOffset Sep(int day) => new(2026, 9, day, 0, 0, 0, TimeSpan.Zero);
    private const string Passphrase = "correct horse battery staple";

    private sealed record Rig(
        TestDatabase Db, BudgetService Budget, HouseholdSnapshotPublisher Publisher, HouseholdSnapshotReader Reader,
        BudgetMonth September, BudgetCategory Groceries, BudgetCategory Dining) : IDisposable
    {
        public void Dispose() => Db.Dispose();
    }

    private static async Task<Rig> CreateAsync(bool trackingOnly = false)
    {
        var db = new TestDatabase();
        var clock = new FakeClock(Now);
        var budget = new BudgetService(db, clock, new Mock<IPlaidClient>().Object);
        var file = new InMemoryDriveTextFile();
        var publisher = new HouseholdSnapshotPublisher(
            new AccountService(db, clock), new SnowballService(db, clock), budget, new SavingsService(db, clock), file, clock);

        var groceries = await budget.AddCategoryAsync("Groceries", default);
        var dining = await budget.AddCategoryAsync("Dining out", default);
        var august = await budget.GetOrCreateMonthAsync("2026-08", default);
        var september = await budget.GetOrCreateMonthAsync("2026-09", default);
        await budget.EnsureCategoryAmountsAsync(august.Id, trackingOnly, default);
        await budget.EnsureCategoryAmountsAsync(september.Id, trackingOnly, default);

        Guid accountId;
        await using (var context = db.CreateDbContext())
        {
            var aug = context.BudgetCategoryAmounts.Where(a => a.BudgetMonthId == august.Id).ToList();
            aug.Single(a => a.CategoryId == groceries.Id).ActualAmount = 548.30m;
            aug.Single(a => a.CategoryId == dining.Id).ActualAmount = 164.60m;

            var sep = context.BudgetCategoryAmounts.Where(a => a.BudgetMonthId == september.Id).ToList();
            var sepGroceries = sep.Single(a => a.CategoryId == groceries.Id);
            sepGroceries.ActualAmount = 361.45m;
            sepGroceries.LimitAmount = trackingOnly ? 0m : 650m;
            sep.Single(a => a.CategoryId == dining.Id).ActualAmount = 183.20m;

            var institution = new Institution { Name = "Redwood Bank", AccessToken = "t" };
            var account = new Account { Name = "Checking", AccountId = "acc-1" };
            institution.Accounts.Add(account);
            context.Institutions.Add(institution);
            await context.SaveChangesAsync();
            accountId = account.Id;

            // Spending nobody has placed yet — on the owner's month total, so it must be on hers.
            context.Transactions.Add(new Transaction
            {
                AccountId = accountId, PlaidTransactionId = "u1", MerchantName = "Hardware store", Amount = 75m, Date = Sep(12),
            });
            // Income is not spending (D140): in neither phone's figure.
            context.Transactions.Add(new Transaction
            {
                AccountId = accountId, PlaidTransactionId = "u2", MerchantName = "Payroll", Amount = -3_000m, Date = Sep(13),
                PlaidCategoryPrimary = "INCOME", PlaidCategoryDetailed = "INCOME_WAGES",
            });
            await context.SaveChangesAsync();
        }

        if (!trackingOnly)
        {
            await budget.MarkLimitUnreadableAsync(september.Id, dining.Id, default);
        }

        return new Rig(db, budget, publisher, new HouseholdSnapshotReader(file), september, groceries, dining);
    }

    private static async Task<HouseholdSnapshot> PublishedAsync(Rig rig)
    {
        Assert.True((await rig.Publisher.PublishAsync(Passphrase, "owner@example.com", default)).Ok);
        return (await rig.Reader.ReadAsync(Passphrase, default)).Snapshot!;
    }

    [Fact]
    public async Task HerMonthLine_SaysWhatTheOwnersSays_UncategorisedSpendingIncluded()
    {
        using var rig = await CreateAsync();
        var owner = await rig.Budget.GetMonthFiguresAsync(rig.September, default);

        var snapshot = await PublishedAsync(rig);
        var hers = HouseholdBudgetView.From(snapshot.Budget, new DateOnly(2026, 9, 27)).Figures;

        Assert.Equal(75m, owner.UncategorizedTotal); // the premise: there IS uncategorised spending
        Assert.Equal(owner.TotalSpent, hers.TotalSpent);
        Assert.Equal(owner.TotalAgreed, hers.TotalAgreed);
        Assert.Equal(owner.UncategorizedTotal, hers.UncategorizedTotal);
        Assert.Equal(owner.UncategorizedCount, hers.UncategorizedCount);
    }

    [Fact]
    public async Task HerUnreadableLimits_AreTheOwners_SoTheSameDogEarCanBeRaised()
    {
        using var rig = await CreateAsync();
        var owner = await rig.Budget.GetMonthFiguresAsync(rig.September, default);

        var snapshot = await PublishedAsync(rig);
        var hers = HouseholdBudgetView.From(snapshot.Budget, new DateOnly(2026, 9, 27)).Figures;

        Assert.Equal(["Dining out"], owner.UnreadableLimits.Select(c => c.Name));
        Assert.Equal(owner.UnreadableLimits.Select(c => c.Name), hers.UnreadableLimits.Select(c => c.Name));
    }

    [Fact]
    public async Task LastMonthTravelsInTheFile_SoHerTrackingCardsCanCompareToo()
    {
        using var rig = await CreateAsync(trackingOnly: true);
        var owner = await rig.Budget.GetMonthFiguresAsync(rig.September, default);

        var snapshot = await PublishedAsync(rig);
        var hers = HouseholdBudgetView.From(snapshot.Budget, new DateOnly(2026, 9, 27)).Figures;

        Assert.Equal(548.30m, owner.LastMonthByCategory![rig.Groceries.Id]);
        var herGroceries = hers.Categories.Single(c => c.Name == "Groceries");
        Assert.Equal(548.30m, hers.LastMonthByCategory![herGroceries.Id]);
        var herDining = hers.Categories.Single(c => c.Name == "Dining out");
        Assert.Equal(164.60m, hers.LastMonthByCategory[herDining.Id]);
    }

    [Fact]
    public async Task ACategoryWithNoRecordedLastMonth_CarriesNone_NotAZero()
    {
        using var rig = await CreateAsync(trackingOnly: true);
        var kids = await rig.Budget.AddCategoryAsync("Kids", default); // added this month: August has no row for it
        await rig.Budget.EnsureCategoryAmountsAsync(rig.September.Id, true, default);

        var owner = await rig.Budget.GetMonthFiguresAsync(rig.September, default);
        var snapshot = await PublishedAsync(rig);

        Assert.False(owner.LastMonthByCategory!.ContainsKey(kids.Id));
        Assert.Null(snapshot.Budget.Categories.Single(c => c.Name == "Kids").LastMonthAmount);
    }

    [Fact]
    public async Task TheFileCarriesTheUncategorisedFigure_AndCountIsOnlyWhatTheOwnersCardCounts()
    {
        using var rig = await CreateAsync();

        var snapshot = await PublishedAsync(rig);

        Assert.Equal(75m, snapshot.Budget.UncategorizedTotal);
        Assert.Equal(1, snapshot.Budget.UncategorizedCount); // the payroll deposit is not spending
    }

    [Theory]
    [InlineData("2026-09", 2026, 9, 26, 4)]   // mid-month: the days left in the snapshot's own month
    [InlineData("2026-09", 2026, 10, 2, 0)]   // the month has rolled: it is over, not 29 days from October's end
    [InlineData("2026-09", 2026, 10, 30, 0)]
    [InlineData("2026-09", 2026, 9, 30, 0)]
    public void DaysLeftIsMeasuredAgainstTheSnapshotsOwnMonth_NotTheMonthTodayIsIn(
        string snapshotMonth, int year, int month, int day, int expected)
    {
        var summary = new HouseholdBudgetSummary(snapshotMonth, false, []);

        var view = HouseholdBudgetView.From(summary, new DateOnly(year, month, day));

        Assert.Equal(expected, view.DaysLeft);
    }
}
