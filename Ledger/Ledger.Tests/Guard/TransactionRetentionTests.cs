using Index.Domain.External;
using Index.Domain.Model;
using Index.Infrastructure.Services;
using Index.Tests.Support;
using Moq;
using Xunit;

namespace Index.Tests.Guard;

/// <summary>
/// Guard G5 (INDEX-P11-PLAN.md §4, D66): "transaction line items kept only until the NEXT budget is
/// approved, then only category totals survive" — approving THIS month purges the PREVIOUS calendar
/// month's <see cref="Transaction"/> rows, never this month's own, and never touches
/// <see cref="BudgetCategoryAmount.ActualAmount"/> (a separate table the purge's date-ranged delete
/// cannot reach). Written first and watched to fail against a naive "keep everything" implementation
/// (see the class remarks below for exactly what was reverted to see it fail).
/// </summary>
public class TransactionRetentionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 9, 0, 0, TimeSpan.Zero);

    private static BudgetService Create(TestDatabase db) => new(db, new FakeClock(Now), new Mock<IPlaidClient>().Object);

    [Fact]
    public async Task ApprovingAMonth_PurgesThePreviousMonthsTransactions_ButKeepsItsCategoryTotals()
    {
        using var db = new TestDatabase();
        var service = Create(db);

        var category = await service.AddCategoryAsync("Groceries", default);
        var august = await service.GetOrCreateMonthAsync("2026-08", default);
        var september = await service.GetOrCreateMonthAsync("2026-09", default);
        await service.EnsureCategoryAmountsAsync(august.Id, trackingOnly: false, default);
        await service.EnsureCategoryAmountsAsync(september.Id, trackingOnly: false, default);

        Guid accountId;
        await using (var context = db.CreateDbContext())
        {
            var institution = new Institution { Name = "Bank", AccessToken = string.Empty };
            var account = new Account { Name = "Checking" };
            institution.Accounts.Add(account);
            context.Institutions.Add(institution);
            context.Transactions.Add(new Transaction
            {
                AccountId = account.Id,
                PlaidTransactionId = "t-aug-1",
                MerchantName = "Whole Foods",
                Amount = 42.50m,
                Date = new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero),
                CategoryId = category.Id,
            });
            await context.SaveChangesAsync();
            accountId = account.Id;
        }

        await service.RecomputeAggregatesAsync(august.Id, default);
        var beforeApproval = (await service.GetCategoryAmountsAsync(august.Id, default)).Single();
        Assert.Equal(42.50m, beforeApproval.ActualAmount);

        // September's own transaction, seeded so this test also proves THIS month's rows are left alone.
        await using (var context = db.CreateDbContext())
        {
            context.Transactions.Add(new Transaction
            {
                AccountId = accountId,
                PlaidTransactionId = "t-sep-1",
                MerchantName = "Trader Joe's",
                Amount = 30.00m,
                Date = new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero),
                CategoryId = category.Id,
            });
            await context.SaveChangesAsync();
        }
        await service.RecomputeAggregatesAsync(september.Id, default);

        // Approving SEPTEMBER purges AUGUST's transactions (D66: the NEXT month's approval purges the
        // PREVIOUS month, not its own).
        await service.ApproveMonthAsync(september.Id, default);

        await using (var context = db.CreateDbContext())
        {
            var augustTransactions = context.Transactions.Where(t => t.PlaidTransactionId == "t-aug-1").ToList();
            Assert.Empty(augustTransactions); // the previous month's line item is gone

            var septemberTransactions = context.Transactions.Where(t => t.PlaidTransactionId == "t-sep-1").ToList();
            Assert.Single(septemberTransactions); // this month's own line item survives
        }

        var afterApproval = (await service.GetCategoryAmountsAsync(august.Id, default)).Single();
        Assert.Equal(42.50m, afterApproval.ActualAmount); // August's category total is untouched
    }

    [Fact]
    public async Task ApprovingAMonth_NeverTouchesMerchantCategoryRules()
    {
        // D67: a MerchantCategoryRule outlives any individual Transaction row — the purge above must
        // never cascade into this table.
        using var db = new TestDatabase();
        var service = Create(db);

        var category = await service.AddCategoryAsync("Groceries", default);
        var august = await service.GetOrCreateMonthAsync("2026-08", default);
        var september = await service.GetOrCreateMonthAsync("2026-09", default);

        await using (var context = db.CreateDbContext())
        {
            context.MerchantCategoryRules.Add(new MerchantCategoryRule { MerchantName = "Costco", CategoryId = category.Id });
            await context.SaveChangesAsync();
        }

        await service.ApproveMonthAsync(september.Id, default);

        await using var verify = db.CreateDbContext();
        Assert.Single(verify.MerchantCategoryRules);
    }
}
