using Index.Domain.External;
using Index.Domain.Model;
using Index.Infrastructure.Services;
using Index.Tests.Support;
using Moq;
using Xunit;

namespace Index.Tests.Services;

public class BudgetServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 9, 0, 0, TimeSpan.Zero);

    private static BudgetService Create(TestDatabase db, IPlaidClient? plaid = null) =>
        new(db, new FakeClock(Now), plaid ?? new Mock<IPlaidClient>().Object);

    // ---- Part 1: migrating the SettingsStore scaffold ----

    [Fact]
    public async Task MigrateScaffoldIfNeeded_SeedsEveryNameInOrder_OnlyOnce()
    {
        using var db = new TestDatabase();
        var service = Create(db);

        await service.MigrateScaffoldIfNeededAsync(["Groceries", "Dining out", "Fuel"], default);
        var first = await service.GetActiveCategoriesAsync(default);
        Assert.Equal(["Groceries", "Dining out", "Fuel"], first.Select(c => c.Name));
        Assert.Equal([0, 1, 2], first.Select(c => c.SortOrder));

        // A second call, even with different names, is a no-op once any row exists.
        await service.MigrateScaffoldIfNeededAsync(["Something else"], default);
        var second = await service.GetActiveCategoriesAsync(default);
        Assert.Equal(["Groceries", "Dining out", "Fuel"], second.Select(c => c.Name));
    }

    [Fact]
    public async Task MigrateScaffoldIfNeeded_DoesNotReseed_AfterTheOwnerRemovedOne()
    {
        using var db = new TestDatabase();
        var service = Create(db);
        await service.MigrateScaffoldIfNeededAsync(["Groceries", "Dining out"], default);

        var groceries = (await service.GetActiveCategoriesAsync(default)).First(c => c.Name == "Groceries");
        await service.SetCategoryActiveAsync(groceries.Id, isActive: false, default);

        await service.MigrateScaffoldIfNeededAsync(["Groceries", "Dining out"], default);

        var active = await service.GetActiveCategoriesAsync(default);
        Assert.DoesNotContain(active, c => c.Name == "Groceries"); // stays removed, not resurrected
    }

    // ---- category CRUD (D55, D110) ----

    [Fact]
    public async Task RemoveCategory_IsSoftDelete_AndUndoRestoresIt()
    {
        using var db = new TestDatabase();
        var service = Create(db);
        var category = await service.AddCategoryAsync("Groceries", default);

        await service.SetCategoryActiveAsync(category.Id, isActive: false, default);
        Assert.Empty(await service.GetActiveCategoriesAsync(default));

        await service.SetCategoryActiveAsync(category.Id, isActive: true, default);
        Assert.Single(await service.GetActiveCategoriesAsync(default));
    }

    [Fact]
    public async Task AddCategory_AfterRemovingAMiddleOne_LandsAtTheBottom_WithNoSortOrderCollision()
    {
        // D55's soft delete leaves a gap (0/1/2 -> remove the middle -> 0/2): a new category sized by
        // a plain COUNT of active rows (2) would collide with the surviving category already at
        // SortOrder 2, rather than land after it.
        using var db = new TestDatabase();
        var service = Create(db);
        var a = await service.AddCategoryAsync("A", default);
        var b = await service.AddCategoryAsync("B", default);
        var c = await service.AddCategoryAsync("C", default);
        await service.SetCategoryActiveAsync(b.Id, isActive: false, default);

        var d = await service.AddCategoryAsync("D", default);

        var active = await service.GetActiveCategoriesAsync(default);
        Assert.Equal(["A", "C", "D"], active.Select(x => x.Name));
        Assert.True(d.SortOrder > active.First(x => x.Name == "C").SortOrder);
    }

    [Fact]
    public async Task MoveCategoryUpAndDown_ReordersActiveCategories()
    {
        using var db = new TestDatabase();
        var service = Create(db);
        var a = await service.AddCategoryAsync("A", default);
        var b = await service.AddCategoryAsync("B", default);
        await service.AddCategoryAsync("C", default);

        await service.MoveCategoryDownAsync(a.Id, default);
        var afterDown = await service.GetActiveCategoriesAsync(default);
        Assert.Equal(["B", "A", "C"], afterDown.Select(c => c.Name));

        await service.MoveCategoryUpAsync(a.Id, default);
        var afterUp = await service.GetActiveCategoriesAsync(default);
        Assert.Equal(["A", "B", "C"], afterUp.Select(c => c.Name));
    }

    // ---- Part 2: transactions across every account, auto-categorized (D58, D63, D67) ----

    private static async Task<(Guid InstitutionId, Guid AccountId)> SeedConnectedAccountAsync(TestDatabase db, string plaidAccountId)
    {
        await using var context = db.CreateDbContext();
        var institution = new Institution { Name = "Redwood Bank", AccessToken = "access-token" };
        var account = new Account { Name = "Checking", AccountId = plaidAccountId };
        institution.Accounts.Add(account);
        context.Institutions.Add(institution);
        await context.SaveChangesAsync();
        return (institution.Id, account.Id);
    }

    [Fact]
    public async Task RefreshTransactions_PullsAcrossEveryConnectedAccount_AndUpsertsWithNoDuplicates()
    {
        using var db = new TestDatabase();
        await SeedConnectedAccountAsync(db, "acc-1");

        var plaid = new Mock<IPlaidClient>();
        plaid.Setup(p => p.GetTransactionsAsync(
                "access-token", It.IsAny<IReadOnlyList<string>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new PlaidTransaction("t1", "acc-1", new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero),
                    "WHOLEFDS", "Whole Foods", 42.50, "USD", Pending: false),
            ]);

        var service = Create(db, plaid.Object);
        await service.RefreshTransactionsAsync("2026-09", default);
        await service.RefreshTransactionsAsync("2026-09", default); // a second pull must not duplicate

        await using var context = db.CreateDbContext();
        var transaction = Assert.Single(context.Transactions);
        Assert.Equal("Whole Foods", transaction.MerchantName);
        Assert.Equal(42.50m, transaction.Amount);
        Assert.Null(transaction.CategoryId); // no rule yet — uncategorized, not guessed
    }

    [Fact]
    public async Task RefreshTransactions_AutoCategorizesANewTransaction_ViaAnExistingMerchantRule()
    {
        using var db = new TestDatabase();
        await SeedConnectedAccountAsync(db, "acc-1");
        var service = Create(db);
        var groceries = await service.AddCategoryAsync("Groceries", default);

        await using (var context = db.CreateDbContext())
        {
            context.MerchantCategoryRules.Add(new MerchantCategoryRule { MerchantName = "Whole Foods", CategoryId = groceries.Id });
            await context.SaveChangesAsync();
        }

        var plaid = new Mock<IPlaidClient>();
        plaid.Setup(p => p.GetTransactionsAsync(
                "access-token", It.IsAny<IReadOnlyList<string>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new PlaidTransaction("t1", "acc-1", new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero),
                    "WHOLEFDS", "Whole Foods", 42.50, "USD", Pending: false),
            ]);

        var withRule = Create(db, plaid.Object);
        await withRule.RefreshTransactionsAsync("2026-09", default);

        await using var context2 = db.CreateDbContext();
        var transaction = Assert.Single(context2.Transactions);
        Assert.Equal(groceries.Id, transaction.CategoryId);

        var month = (await withRule.GetOrCreateMonthAsync("2026-09", default));
        await withRule.EnsureCategoryAmountsAsync(month.Id, trackingOnly: false, default);
        await withRule.RecomputeAggregatesAsync(month.Id, default);
        var amount = (await withRule.GetCategoryAmountsAsync(month.Id, default)).Single(a => a.CategoryId == groceries.Id);
        Assert.Equal(42.50m, amount.ActualAmount);
    }

    [Fact]
    public async Task Recategorize_MovesOnlyThisTransaction_AndWritesAMerchantRuleForTheFuture()
    {
        using var db = new TestDatabase();
        var (_, accountId) = await SeedConnectedAccountAsync(db, "acc-1");
        var service = Create(db);
        var dining = await service.AddCategoryAsync("Dining out", default);
        var groceries = await service.AddCategoryAsync("Groceries", default);

        Guid earlierTransactionId, movedTransactionId;
        await using (var context = db.CreateDbContext())
        {
            var earlier = new Transaction
            {
                AccountId = accountId, PlaidTransactionId = "t-earlier", MerchantName = "Costco",
                Amount = 90m, Date = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), CategoryId = dining.Id,
            };
            var moved = new Transaction
            {
                AccountId = accountId, PlaidTransactionId = "t-moved", MerchantName = "Costco",
                Amount = 176.90m, Date = new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero), CategoryId = dining.Id,
            };
            context.Transactions.AddRange(earlier, moved);
            await context.SaveChangesAsync();
            earlierTransactionId = earlier.Id;
            movedTransactionId = moved.Id;
        }

        await service.RecategorizeTransactionAsync(movedTransactionId, groceries.Id, default);

        await using var context2 = db.CreateDbContext();
        var moved2 = context2.Transactions.Single(t => t.Id == movedTransactionId);
        Assert.Equal(groceries.Id, moved2.CategoryId);
        Assert.True(moved2.IsManualOverride);

        // D62: "earlier transactions stay where they are" — the one already-placed transaction for
        // the same merchant is untouched.
        var earlier2 = context2.Transactions.Single(t => t.Id == earlierTransactionId);
        Assert.Equal(dining.Id, earlier2.CategoryId);
        Assert.False(earlier2.IsManualOverride);

        // D58: the rule now exists, for FUTURE transactions.
        var rule = Assert.Single(context2.MerchantCategoryRules);
        Assert.Equal("Costco", rule.MerchantName);
        Assert.Equal(groceries.Id, rule.CategoryId);
    }

    [Fact]
    public async Task RevertRecategorize_PutsTheTransactionAndTheRuleBackExactly()
    {
        using var db = new TestDatabase();
        var (_, accountId) = await SeedConnectedAccountAsync(db, "acc-1");
        var service = Create(db);
        var dining = await service.AddCategoryAsync("Dining out", default);
        var groceries = await service.AddCategoryAsync("Groceries", default);

        Guid transactionId;
        await using (var context = db.CreateDbContext())
        {
            var transaction = new Transaction
            {
                AccountId = accountId, PlaidTransactionId = "t1", MerchantName = "Costco",
                Amount = 176.90m, Date = new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero), CategoryId = dining.Id,
            };
            context.Transactions.Add(transaction);
            await context.SaveChangesAsync();
            transactionId = transaction.Id;
        }

        var undo = await service.RecategorizeTransactionAsync(transactionId, groceries.Id, default);
        await service.RevertRecategorizeAsync(undo, default);

        await using var context2 = db.CreateDbContext();
        var transaction2 = context2.Transactions.Single(t => t.Id == transactionId);
        Assert.Equal(dining.Id, transaction2.CategoryId);
        Assert.False(transaction2.IsManualOverride);
        Assert.Empty(context2.MerchantCategoryRules); // there was no rule before — none survives the undo
    }

    // ---- the "Uncategorized" pseudo-category (this task's own judgement call) ----

    [Fact]
    public async Task GetUncategorized_SumsOnlyTransactionsWithNoCategory_InTheGivenMonth()
    {
        using var db = new TestDatabase();
        var (_, accountId) = await SeedConnectedAccountAsync(db, "acc-1");
        var service = Create(db);
        var groceries = await service.AddCategoryAsync("Groceries", default);

        await using (var context = db.CreateDbContext())
        {
            context.Transactions.Add(new Transaction
            {
                AccountId = accountId, PlaidTransactionId = "t1", MerchantName = "Mystery Co",
                Amount = 15m, Date = new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero), CategoryId = null,
            });
            context.Transactions.Add(new Transaction
            {
                AccountId = accountId, PlaidTransactionId = "t2", MerchantName = "Whole Foods",
                Amount = 40m, Date = new DateTimeOffset(2026, 9, 6, 0, 0, 0, TimeSpan.Zero), CategoryId = groceries.Id,
            });
            await context.SaveChangesAsync();
        }

        var (total, count) = await service.GetUncategorizedAsync("2026-09", default);
        Assert.Equal(15m, total);
        Assert.Equal(1, count);
    }

    // ---- tracking-only history (D142) ----

    [Fact]
    public async Task GetCategoryHistory_ReturnsEveryRecordedMonth_NewestFirst()
    {
        using var db = new TestDatabase();
        var service = Create(db);
        var groceries = await service.AddCategoryAsync("Groceries", default);

        var july = await service.GetOrCreateMonthAsync("2026-07", default);
        var august = await service.GetOrCreateMonthAsync("2026-08", default);
        var september = await service.GetOrCreateMonthAsync("2026-09", default);
        await service.EnsureCategoryAmountsAsync(july.Id, false, default);
        await service.EnsureCategoryAmountsAsync(august.Id, false, default);
        await service.EnsureCategoryAmountsAsync(september.Id, false, default);
        await service.SetCategoryLimitAsync(july.Id, groceries.Id, 600m, default);

        var history = await service.GetCategoryHistoryAsync(groceries.Id, "2026-09", default);

        Assert.Equal(["2026-09", "2026-08", "2026-07"], history.Select(p => p.YearMonth));
        Assert.True(history.Single(p => p.YearMonth == "2026-09").IsCurrentMonth);
    }

    // ---- D88's detection: has any limit ever been set for this month ----

    [Fact]
    public async Task AnyLimitSet_IsFalseUntilACategoryGetsAPositiveLimit()
    {
        using var db = new TestDatabase();
        var service = Create(db);
        var groceries = await service.AddCategoryAsync("Groceries", default);
        var month = await service.GetOrCreateMonthAsync("2026-09", default);
        await service.EnsureCategoryAmountsAsync(month.Id, false, default);

        Assert.False(await service.AnyLimitSetAsync(month.Id, default));

        await service.SetCategoryLimitAsync(month.Id, groceries.Id, 650m, default);
        Assert.True(await service.AnyLimitSetAsync(month.Id, default));
    }

    [Fact]
    public async Task AnyLimitSet_IsTrueWhenTheFirstWorksheetCameBackWithEveryBoxUnreadable()
    {
        // Remediation 3.2 item 3: the worksheet HAS come back, it just couldn't be read anywhere. D88's
        // empty state ("the first budget is made on paper") would deny that and hide the dog-ear that
        // says the boxes are being re-read — so a flagged-unreadable row counts as the worksheet's reply.
        using var db = new TestDatabase();
        var service = Create(db);
        var groceries = await service.AddCategoryAsync("Groceries", default);
        var dining = await service.AddCategoryAsync("Dining out", default);
        var month = await service.GetOrCreateMonthAsync("2026-09", default);
        await service.EnsureCategoryAmountsAsync(month.Id, false, default);

        await service.MarkLimitUnreadableAsync(month.Id, groceries.Id, default);
        await service.MarkLimitUnreadableAsync(month.Id, dining.Id, default);

        Assert.True(await service.AnyLimitSetAsync(month.Id, default));
    }

    // ---- D49: a limit the worksheet reader couldn't make out ----

    [Fact]
    public async Task MarkLimitUnreadable_FlagsTheCategory_AndSettingANewLimitClearsIt()
    {
        using var db = new TestDatabase();
        var service = Create(db);
        var dining = await service.AddCategoryAsync("Dining out", default);
        var month = await service.GetOrCreateMonthAsync("2026-10", default);
        await service.EnsureCategoryAmountsAsync(month.Id, false, default);

        await service.MarkLimitUnreadableAsync(month.Id, dining.Id, default);
        var flagged = (await service.GetCategoryAmountsAsync(month.Id, default)).Single(a => a.CategoryId == dining.Id);
        Assert.True(flagged.LimitUnreadable);

        await service.SetCategoryLimitAsync(month.Id, dining.Id, 150m, default);
        var cleared = (await service.GetCategoryAmountsAsync(month.Id, default)).Single(a => a.CategoryId == dining.Id);
        Assert.False(cleared.LimitUnreadable);
    }
}
