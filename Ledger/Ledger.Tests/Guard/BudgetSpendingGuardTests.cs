using Index.Domain.External;
using Index.Domain.Model;
using Index.Infrastructure.Services;
using Index.Tests.Support;
using Moq;
using Xunit;

namespace Index.Tests.Guard;

/// <summary>
/// Phase 2.3 / 2.4 guards (INDEX-P11-REMEDIATION-PLAN.md §2.3, §2.4, §10), each written before the code it
/// holds and watched to fail against the bug named on it:
/// <list type="bullet">
/// <item><b>R-G17</b> — the month line agrees with the visible cards; a removed category's amounts neither
/// count nor throw. Reintroduce: sum every amount row, not just the active categories'.</item>
/// <item><b>R-G18</b> — a pending transaction and the posted one that replaces it are ONE row. Reintroduce:
/// key only on <c>PlaidTransactionId</c>, so Plaid's new id on posting stores a second row.</item>
/// <item><b>R-G19</b> — spending only: income, transfers and loan/card payments are in neither a category,
/// the Uncategorized card nor the month total (D140). Reintroduce: sum every stored transaction.</item>
/// <item><b>R-G20</b> — a merchant rule beats the Plaid map, which beats Uncategorized. Reintroduce: ask
/// the map before the rule.</item>
/// </list>
/// </summary>
public class BudgetSpendingGuardTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 9, 0, 0, TimeSpan.Zero);
    private static DateTimeOffset Sep(int day) => new(2026, 9, day, 0, 0, 0, TimeSpan.Zero);

    private static BudgetService Create(TestDatabase db, Mock<IPlaidClient>? plaid = null) =>
        new(db, new FakeClock(Now), (plaid ?? new Mock<IPlaidClient>()).Object);

    private static Mock<IPlaidClient> PlaidReturning(params PlaidTransaction[] transactions) =>
        PlaidReturning(() => transactions);

    private static Mock<IPlaidClient> PlaidReturning(Func<PlaidTransaction[]> transactions)
    {
        var plaid = new Mock<IPlaidClient>();
        plaid.Setup(p => p.GetTransactionsAsync(
                "access-token", It.IsAny<IReadOnlyList<string>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => transactions());
        return plaid;
    }

    private static PlaidTransaction Tx(
        string id, double amount, string merchant, string? primary = null, string? detailed = null,
        bool pending = false, string? pendingId = null, int day = 20) =>
        new(id, "acc-1", Sep(day), merchant.ToUpperInvariant(), merchant, amount, "USD", pending, pendingId, primary, detailed);

    private static async Task<Guid> SeedConnectedAccountAsync(TestDatabase db)
    {
        await using var context = db.CreateDbContext();
        var institution = new Institution { Name = "Redwood Bank", AccessToken = "access-token" };
        var account = new Account { Name = "Checking", AccountId = "acc-1" };
        institution.Accounts.Add(account);
        context.Institutions.Add(institution);
        await context.SaveChangesAsync();
        return account.Id;
    }

    private static async Task<BudgetMonth> OpenMonthAsync(BudgetService service)
    {
        var month = await service.GetOrCreateMonthAsync("2026-09", default);
        await service.EnsureCategoryAmountsAsync(month.Id, trackingOnly: false, default);
        return month;
    }

    // ---- R-G17 ----

    [Fact]
    public async Task RG17_AMonthLineOnlyCountsTheCardsThatAreDrawn_AndARemovedCategoryDoesNotThrow()
    {
        using var db = new TestDatabase();
        var accountId = await SeedConnectedAccountAsync(db);
        var service = Create(db);
        var groceries = await service.AddCategoryAsync("Groceries", default);
        var dining = await service.AddCategoryAsync("Dining out", default);
        var month = await OpenMonthAsync(service);

        await service.SetCategoryLimitAsync(month.Id, groceries.Id, 650m, default);
        await service.SetCategoryLimitAsync(month.Id, dining.Id, 150m, default);
        await service.MarkLimitUnreadableAsync(month.Id, dining.Id, default);

        await using (var context = db.CreateDbContext())
        {
            context.Transactions.Add(new Transaction
            {
                AccountId = accountId, PlaidTransactionId = "g1", MerchantName = "Whole Foods", Amount = 100m, Date = Sep(10), CategoryId = groceries.Id,
            });
            context.Transactions.Add(new Transaction
            {
                AccountId = accountId, PlaidTransactionId = "d1", MerchantName = "Bistro", Amount = 40m, Date = Sep(11), CategoryId = dining.Id,
            });
            await context.SaveChangesAsync();
        }
        await service.RecomputeAggregatesAsync(month.Id, default);

        // The owner removes Dining out (D55: soft delete — its amounts stay, its card goes).
        await service.SetCategoryActiveAsync(dining.Id, isActive: false, default);

        var figures = await service.GetMonthFiguresAsync(month, default);

        Assert.Equal(["Groceries"], figures.Categories.Select(c => c.Name));
        Assert.Equal(650m, figures.TotalAgreed);   // not 800: the removed category's 150 has no card
        Assert.Equal(100m, figures.TotalSpent);    // not 140
        Assert.Empty(figures.UnreadableLimits);    // the removed category's unreadable flag names no card
    }

    // ---- R-G18 ----

    [Fact]
    public async Task RG18_APendingTransactionAndItsPostedSuccessorAreOneRow_KeepingTheOwnersCategory()
    {
        using var db = new TestDatabase();
        await SeedConnectedAccountAsync(db);
        var returned = new[] { Tx("pending-1", 50, "Costco", pending: true) };
        var plaid = PlaidReturning(() => returned);
        var service = Create(db, plaid);
        var groceries = await service.AddCategoryAsync("Groceries", default);
        var household = await service.AddCategoryAsync("Household", default);
        var month = await OpenMonthAsync(service);

        await service.RefreshTransactionsAsync("2026-09", default);
        Guid pendingRowId;
        await using (var context = db.CreateDbContext())
        {
            pendingRowId = Assert.Single(context.Transactions).Id;
        }
        // The owner moves the still-pending Costco into Household.
        await service.RecategorizeTransactionAsync(pendingRowId, household.Id, default);

        // Plaid posts it: a NEW transaction_id, the amount settled, pointing back at the pending one.
        returned = [Tx("posted-1", 52.10, "Costco", pending: false, pendingId: "pending-1")];
        await service.RefreshTransactionsAsync("2026-09", default);

        await using var verify = db.CreateDbContext();
        var row = Assert.Single(verify.Transactions);   // two rows here is the double count
        Assert.Equal("posted-1", row.PlaidTransactionId);
        Assert.Equal(52.10m, row.Amount);
        Assert.False(row.Pending);
        Assert.Equal(household.Id, row.CategoryId);     // the owner's choice survives the posting
        Assert.True(row.IsManualOverride);

        var amounts = await service.GetCategoryAmountsAsync(month.Id, default);
        Assert.Equal(52.10m, amounts.Single(a => a.CategoryId == household.Id).ActualAmount);
        Assert.Equal(0m, amounts.Single(a => a.CategoryId == groceries.Id).ActualAmount);
    }

    [Fact]
    public async Task RG18_ADoubleCountAlreadyStored_IsCleanedUpWhenThePostedRowNextArrives()
    {
        using var db = new TestDatabase();
        var accountId = await SeedConnectedAccountAsync(db);
        var household = await Create(db).AddCategoryAsync("Household", default);
        await using (var context = db.CreateDbContext())
        {
            context.Transactions.Add(new Transaction
            {
                AccountId = accountId, PlaidTransactionId = "pending-1", MerchantName = "Costco", Amount = 50m, Date = Sep(20),
                Pending = true, CategoryId = household.Id, IsManualOverride = true,
            });
            context.Transactions.Add(new Transaction
            {
                AccountId = accountId, PlaidTransactionId = "posted-1", MerchantName = "Costco", Amount = 52.10m, Date = Sep(20),
            });
            await context.SaveChangesAsync();
        }

        var service = Create(db, PlaidReturning(Tx("posted-1", 52.10, "Costco", pendingId: "pending-1")));
        await service.RefreshTransactionsAsync("2026-09", default);

        await using var verify = db.CreateDbContext();
        var row = Assert.Single(verify.Transactions);
        Assert.Equal("posted-1", row.PlaidTransactionId);
        Assert.Equal(household.Id, row.CategoryId);
        Assert.True(row.IsManualOverride);
    }

    [Fact]
    public async Task RG18_APendingRowPlaidNoLongerReports_IsDropped_OnlyWhenThePullWasComplete()
    {
        using var db = new TestDatabase();
        var accountId = await SeedConnectedAccountAsync(db);
        await using (var context = db.CreateDbContext())
        {
            context.Transactions.Add(new Transaction
            {
                AccountId = accountId, PlaidTransactionId = "pending-gone", MerchantName = "Gas hold", Amount = 75m, Date = Sep(22), Pending = true,
            });
            await context.SaveChangesAsync();
        }

        // A full page (the 250-row cap) proves nothing about what is missing: keep the row.
        var full = Enumerable.Range(0, 250).Select(i => Tx($"p{i}", 1, "Coffee", day: 1 + i % 20)).ToArray();
        await Create(db, PlaidReturning(full)).RefreshTransactionsAsync("2026-09", default);
        await using (var context = db.CreateDbContext())
        {
            Assert.Contains(context.Transactions, t => t.PlaidTransactionId == "pending-gone");
        }

        // A short page is the whole month: the pending row that is not in it was reversed.
        await Create(db, PlaidReturning(Tx("only", 3, "Coffee"))).RefreshTransactionsAsync("2026-09", default);
        await using var verify = db.CreateDbContext();
        Assert.DoesNotContain(verify.Transactions, t => t.PlaidTransactionId == "pending-gone");
        Assert.Contains(verify.Transactions, t => t.PlaidTransactionId == "only");
    }

    // ---- R-G19 ----

    [Fact]
    public async Task RG19_IncomeTransfersAndLoanAndCardPaymentsAreNotSpending()
    {
        using var db = new TestDatabase();
        await SeedConnectedAccountAsync(db);
        var plaid = PlaidReturning(
            Tx("pay", -2400, "Employer", "INCOME", "INCOME_WAGES"),
            Tx("card", 300, "Bluebird Visa", "LOAN_PAYMENTS", "LOAN_PAYMENTS_CREDIT_CARD_PAYMENT"),
            Tx("car", 410, "Toyota Financial", "LOAN_PAYMENTS", "LOAN_PAYMENTS_CAR_PAYMENT"),
            Tx("sweep", 500, "Savings", "TRANSFER_OUT", "TRANSFER_OUT_SAVINGS"),
            Tx("back", -500, "From savings", "TRANSFER_IN", "TRANSFER_IN_SAVINGS"),
            Tx("food", 40, "Whole Foods", "FOOD_AND_DRINK", "FOOD_AND_DRINK_GROCERIES"),
            Tx("doc", 25, "Dr Smith", "MEDICAL", "MEDICAL_PRIMARY_CARE"));
        var service = Create(db, plaid);
        var groceries = await service.AddCategoryAsync("Groceries", default);
        var month = await OpenMonthAsync(service);

        await service.RefreshTransactionsAsync("2026-09", default);

        var (uncategorized, count) = await service.GetUncategorizedAsync("2026-09", default);
        Assert.Equal(25m, uncategorized);   // only the doctor: unmapped spending, not the payments
        Assert.Equal(1, count);

        var posted = await service.GetTransactionsForCategoryAsync(null, "2026-09", default);
        Assert.Equal(["Dr Smith"], posted.Select(v => v.Transaction.MerchantName));

        var figures = await service.GetMonthFiguresAsync(month, default);
        Assert.Equal(65m, figures.TotalSpent);  // 40 groceries + 25 uncategorized; nothing from the other five
        Assert.Equal(40m, figures.Amounts.Single(a => a.CategoryId == groceries.Id).ActualAmount);
    }

    // ---- R-G20 ----

    [Fact]
    public async Task RG20_AMerchantRuleBeatsThePlaidMap()
    {
        using var db = new TestDatabase();
        await SeedConnectedAccountAsync(db);
        var service = Create(db, PlaidReturning(Tx("t1", 42.50, "Whole Foods", "FOOD_AND_DRINK", "FOOD_AND_DRINK_GROCERIES")));
        var groceries = await service.AddCategoryAsync("Groceries", default);
        var household = await service.AddCategoryAsync("Household", default);
        await using (var context = db.CreateDbContext())
        {
            context.MerchantCategoryRules.Add(new MerchantCategoryRule { MerchantName = "Whole Foods", CategoryId = household.Id });
            await context.SaveChangesAsync();
        }

        await service.RefreshTransactionsAsync("2026-09", default);

        await using var verify = db.CreateDbContext();
        var row = Assert.Single(verify.Transactions);
        Assert.Equal(household.Id, row.CategoryId);       // the rule, not Plaid's "groceries"
        Assert.NotEqual(groceries.Id, row.CategoryId);
    }

    [Fact]
    public async Task RG20_TheMapBeatsUncategorized_AndAnUnmappedCategoryStaysUncategorized()
    {
        using var db = new TestDatabase();
        await SeedConnectedAccountAsync(db);
        var service = Create(db, PlaidReturning(
            Tx("t1", 42.50, "Whole Foods", "FOOD_AND_DRINK", "FOOD_AND_DRINK_GROCERIES"),
            Tx("t2", 30, "Shell", "TRANSPORTATION", "TRANSPORTATION_GAS"),       // no Fuel category exists
            Tx("t3", 12, "Mystery", "GENERAL_SERVICES", "GENERAL_SERVICES_OTHER"),
            Tx("t4", 9, "No category sent")));
        var groceries = await service.AddCategoryAsync("Groceries", default);

        await service.RefreshTransactionsAsync("2026-09", default);

        await using var verify = db.CreateDbContext();
        var rows = verify.Transactions.ToDictionary(t => t.PlaidTransactionId);
        Assert.Equal(groceries.Id, rows["t1"].CategoryId);
        Assert.Null(rows["t2"].CategoryId);   // the map names Fuel; this household has no active "Fuel"
        Assert.Null(rows["t3"].CategoryId);
        Assert.Null(rows["t4"].CategoryId);
        Assert.Equal("FOOD_AND_DRINK_GROCERIES", rows["t1"].PlaidCategoryDetailed);   // stored, for later
    }

    [Fact]
    public async Task RG20_AnExistingUncategorizedRowIsMappedOnTheNextPull_ButAnOverrideAndARuledMerchantAreNot()
    {
        using var db = new TestDatabase();
        var accountId = await SeedConnectedAccountAsync(db);
        var service = Create(db, PlaidReturning(
            Tx("old-plain", 20, "Safeway", "FOOD_AND_DRINK", "FOOD_AND_DRINK_GROCERIES"),
            Tx("old-moved", 20, "Trader Joes", "FOOD_AND_DRINK", "FOOD_AND_DRINK_GROCERIES"),
            Tx("old-ruled", 20, "Costco", "FOOD_AND_DRINK", "FOOD_AND_DRINK_GROCERIES")));
        var groceries = await service.AddCategoryAsync("Groceries", default);
        var household = await service.AddCategoryAsync("Household", default);
        await using (var context = db.CreateDbContext())
        {
            // Stored before the Plaid category existed: no category, no stored Plaid category.
            context.Transactions.Add(new Transaction { AccountId = accountId, PlaidTransactionId = "old-plain", MerchantName = "Safeway", Amount = 20m, Date = Sep(5) });
            context.Transactions.Add(new Transaction
            {
                AccountId = accountId, PlaidTransactionId = "old-moved", MerchantName = "Trader Joes", Amount = 20m, Date = Sep(5),
                CategoryId = household.Id, IsManualOverride = true,
            });
            context.Transactions.Add(new Transaction { AccountId = accountId, PlaidTransactionId = "old-ruled", MerchantName = "Costco", Amount = 20m, Date = Sep(5) });
            // A rule written AFTER that row came in: D62, earlier transactions stay put.
            context.MerchantCategoryRules.Add(new MerchantCategoryRule { MerchantName = "Costco", CategoryId = household.Id });
            await context.SaveChangesAsync();
        }

        await service.RefreshTransactionsAsync("2026-09", default);

        await using var verify = db.CreateDbContext();
        var rows = verify.Transactions.ToDictionary(t => t.PlaidTransactionId);
        Assert.Equal(groceries.Id, rows["old-plain"].CategoryId);
        Assert.Equal(household.Id, rows["old-moved"].CategoryId);
        Assert.True(rows["old-moved"].IsManualOverride);
        Assert.Null(rows["old-ruled"].CategoryId);    // neither the rule (D62) nor the map (the rule outranks it)
    }
}
