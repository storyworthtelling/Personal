using Microsoft.EntityFrameworkCore;
using Index.Domain.External;
using Index.Domain.Model;
using Index.Domain.Time;
using Index.Infrastructure.Persistence;

namespace Index.Infrastructure.Services;

/// <summary>The thin, Infrastructure half of D101–D104: fetches whatever transactions
/// <see cref="IncomeDetector"/> needs and hands them straight to it, exactly the same
/// pure-logic/service-layer split <c>AccountNotices</c>/<c>BudgetNotices</c> already draw against
/// <c>AccountService</c>/<c>BudgetService</c>. Holds no algorithm of its own.</summary>
public interface IIncomeDetectionService
{
    Task<IncomeEstimate> DetectAsync(CancellationToken cancellationToken);
}

/// <summary>
/// D104's scoping happens here, structurally: only <see cref="Institution"/> rows this install
/// actually connected via Plaid (a non-empty <see cref="Institution.AccessToken"/>) are ever
/// queried, the same exclusion <c>AccountNotices</c>/<c>BudgetService.RefreshTransactionsAsync</c>
/// already apply for the same reason — a hand-entered account has no Plaid Item behind it to pull
/// transactions from, and an account nobody connected at all is never in this table to begin with.
/// <see cref="IncomeDetector"/> itself never sees, and never has to filter, anything outside that set.
/// </summary>
public sealed class IncomeDetectionService : IIncomeDetectionService
{
    // D101–D103 need enough history to observe a monthly paycheck recur at least
    // IncomeDetector.MinOccurrencesForConfidence times (3 × ~30 days), plus slack for a slow refresh
    // cadence or a payday landing a few days either side of the window's edge. 180 days covers a
    // monthly earner three-and-a-bit times over, and a biweekly/weekly earner many times over, without
    // asking Plaid's Transactions product for more history than this detector can use.
    private const int LookbackDays = 180;

    private readonly IDbContextFactory<LedgerDbContext> dbContextFactory;
    private readonly IClock clock;
    private readonly IPlaidClient plaid;

    public IncomeDetectionService(IDbContextFactory<LedgerDbContext> dbContextFactory, IClock clock, IPlaidClient plaid)
    {
        this.dbContextFactory = dbContextFactory;
        this.clock = clock;
        this.plaid = plaid;
    }

    public async Task<IncomeEstimate> DetectAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var institutions = await db.Institutions.Include(i => i.Accounts).ToListAsync(cancellationToken);

        var end = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var start = end.AddDays(-LookbackDays);

        var all = new List<PlaidTransaction>();
        foreach (var institution in institutions)
        {
            if (string.IsNullOrEmpty(institution.AccessToken) || institution.Accounts.Count == 0)
            {
                continue; // hand-entered (D104): nothing at Plaid to pull deposits from
            }

            IReadOnlyList<PlaidTransaction> pulled;
            try
            {
                pulled = await plaid.GetTransactionsAsync(
                    institution.AccessToken, institution.Accounts.Select(a => a.AccountId).ToList(),
                    start, end, count: 500, cancellationToken);
            }
            catch
            {
                // Same best-effort idiom BudgetService.RefreshTransactionsAsync already uses: one
                // institution's Transactions product not being ready yet must not block a projection
                // built from every OTHER connected account.
                continue;
            }

            all.AddRange(pulled);
        }

        return IncomeDetector.Detect(all);
    }
}
