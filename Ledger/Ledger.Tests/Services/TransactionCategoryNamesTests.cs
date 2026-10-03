using Index.Domain.External;
using Index.Domain.Model;
using Index.Infrastructure.Services;
using Index.Tests.Support;
using Moq;
using Xunit;

namespace Index.Tests.Services;

/// <summary>
/// Remediation 3.3 item 1: the Accounts post-it lists Plaid's LIVE transactions, and the category it prints
/// beside each is the OWNER's — the one Budget stored against the same transaction. The join is by Plaid's
/// transaction id; where nothing is stored (or the stored row is uncategorized) there is no entry, and the
/// row shows no category rather than a guess.
/// </summary>
public class TransactionCategoryNamesTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Day = new(2026, 9, 22, 0, 0, 0, TimeSpan.Zero);

    private static BudgetService Create(TestDatabase db) =>
        new(db, new FakeClock(Now), new Mock<IPlaidClient>().Object);

    private static PlaidTransaction Live(string id, string? pendingId = null) =>
        new(id, "acc-1", Day, "WHOLEFDS", "Whole Foods", 86.42, "USD", Pending: false, PendingTransactionId: pendingId);

    private static async Task StoreAsync(TestDatabase db, string plaidId, Guid? categoryId)
    {
        await using var context = db.CreateDbContext();
        context.Transactions.Add(new Transaction
        {
            AccountId = Guid.NewGuid(), PlaidTransactionId = plaidId, MerchantName = "Whole Foods",
            Amount = 86.42m, Date = Day, CategoryId = categoryId,
        });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task AStoredTransactionResolvesToTheOwnersCategoryName()
    {
        using var db = new TestDatabase();
        var service = Create(db);
        var groceries = await service.AddCategoryAsync("Groceries", default);
        await StoreAsync(db, "t1", groceries.Id);

        var names = await service.GetCategoryNamesAsync([Live("t1")], default);

        Assert.Equal("Groceries", names["t1"]);
    }

    [Fact]
    public async Task AnUncategorizedOrUnstoredTransactionHasNoEntry_NothingIsGuessed()
    {
        using var db = new TestDatabase();
        var service = Create(db);
        await StoreAsync(db, "t1", categoryId: null);

        var names = await service.GetCategoryNamesAsync([Live("t1"), Live("never-stored")], default);

        Assert.Empty(names);
    }

    [Fact]
    public async Task APostedTransactionStillOnItsPendingRowFindsTheOwnersCategoryThere()
    {
        // Plaid issues a brand-new id when a pending transaction posts, and Budget re-keys its row on its next
        // pull. Until then the stored row is under the PENDING id — which the live row names.
        using var db = new TestDatabase();
        var service = Create(db);
        var groceries = await service.AddCategoryAsync("Groceries", default);
        await StoreAsync(db, "pending-1", groceries.Id);

        var names = await service.GetCategoryNamesAsync([Live("posted-1", pendingId: "pending-1")], default);

        Assert.Equal("Groceries", names["posted-1"]);
    }

    [Fact]
    public async Task ACategoryTheOwnerHasSinceRemovedStillNamesTheTransactionsFiledUnderIt()
    {
        // Categories are soft-deleted so past months keep reading correctly (D55).
        using var db = new TestDatabase();
        var service = Create(db);
        var fuel = await service.AddCategoryAsync("Fuel", default);
        await StoreAsync(db, "t1", fuel.Id);
        await service.SetCategoryActiveAsync(fuel.Id, isActive: false, default);

        var names = await service.GetCategoryNamesAsync([Live("t1")], default);

        Assert.Equal("Fuel", names["t1"]);
    }

    [Fact]
    public async Task NoTransactionsIsNoQuery()
    {
        using var db = new TestDatabase();

        Assert.Empty(await Create(db).GetCategoryNamesAsync([], default));
    }
}
