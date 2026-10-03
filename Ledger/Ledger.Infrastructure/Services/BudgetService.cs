using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Index.Domain.External;
using Index.Domain.Model;
using Index.Domain.Time;
using Index.Domain.Worksheet;
using Index.Infrastructure.Persistence;

namespace Index.Infrastructure.Services;

/// <summary>One transaction, plus the account name it happened on (for the post-it's "which account"
/// line — mockup 7's "Meridian Plus", "Bluebird Visa", "Everyday Checking").</summary>
public sealed record BudgetTransactionView(Transaction Transaction, string AccountName);

/// <summary>One past (or the in-progress current) month's total for one category, newest first —
/// exactly what D66's purge leaves behind (mockup 8's "Month by month" post-it).</summary>
public sealed record CategoryMonthPoint(string YearMonth, decimal Amount, bool IsCurrentMonth);

/// <summary>What <see cref="BudgetService.RecategorizeTransactionAsync"/> captured before it changed
/// anything, so <see cref="BudgetService.RevertRecategorizeAsync"/> can put it back exactly — the same
/// capture-then-reverse shape <c>AccountService.FindAccountAsync</c>/<c>RestoreAccountAsync</c> already
/// uses for the undo-first Remove.</summary>
public sealed record RecategorizeUndoState(
    Guid TransactionId, Guid? PreviousCategoryId, bool PreviousIsManualOverride, MerchantCategoryRuleSnapshot? PreviousRule);

public sealed record MerchantCategoryRuleSnapshot(string MerchantName, Guid CategoryId);

/// <summary>
/// Release E's Budget service (INDEX-P11-PLAN.md, finding §3): categories, the current month's
/// figures, the cross-account transaction pull (D63), merchant recategorization rules (D58/D67), and
/// D66's retention purge (Guard G5). Talks to <see cref="LedgerDbContext"/> directly, never through a
/// repository layer, per the repo's architecture rules — <c>Pages.BudgetPage</c> and
/// <c>Pages.SetupPage</c>'s Budget-category group are the only callers.
/// </summary>
public sealed class BudgetService
{
    private readonly IDbContextFactory<LedgerDbContext> dbContextFactory;
    private readonly IClock clock;
    private readonly IPlaidClient plaid;

    public BudgetService(IDbContextFactory<LedgerDbContext> dbContextFactory, IClock clock, IPlaidClient plaid)
    {
        this.dbContextFactory = dbContextFactory;
        this.clock = clock;
        this.plaid = plaid;
    }

    // ---- Part 1: categories, migrated off SettingsStore's scaffold ----

    /// <summary>Seeds <see cref="BudgetCategory"/> from the Settings scaffold's JSON-list names, but
    /// only the very first time — once any row exists (seeded or owner-added since), this is a no-op,
    /// so a category the owner already renamed/removed/reordered is never silently re-seeded.</summary>
    public async Task MigrateScaffoldIfNeededAsync(IReadOnlyList<string> scaffoldNames, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (await db.BudgetCategories.AnyAsync(cancellationToken))
        {
            return;
        }

        var order = 0;
        foreach (var name in scaffoldNames)
        {
            db.BudgetCategories.Add(new BudgetCategory { Name = name, IsActive = true, SortOrder = order++ });
        }
        if (order > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<BudgetCategory>> GetActiveCategoriesAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.BudgetCategories.Where(c => c.IsActive).OrderBy(c => c.SortOrder).AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task<BudgetCategory> AddCategoryAsync(string name, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        // The next SortOrder after the highest ACTIVE one, not a plain count: D55's soft delete leaves
        // gaps (three categories at 0/1/2, the middle one removed, leaves 0/2), and a new category
        // sized by count alone would collide with an existing SortOrder rather than land at the bottom.
        var maxSortOrder = await db.BudgetCategories.Where(c => c.IsActive)
            .Select(c => (int?)c.SortOrder).MaxAsync(cancellationToken);
        var category = new BudgetCategory { Name = name.Trim(), IsActive = true, SortOrder = (maxSortOrder ?? -1) + 1 };
        db.BudgetCategories.Add(category);
        await db.SaveChangesAsync(cancellationToken);
        return category;
    }

    public async Task RenameCategoryAsync(Guid categoryId, string newName, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var category = await db.BudgetCategories.FirstOrDefaultAsync(c => c.Id == categoryId, cancellationToken);
        if (category is null)
        {
            return;
        }
        category.Name = newName.Trim();
        await db.SaveChangesAsync(cancellationToken);
    }

    // D55: never deletes — a removed category keeps its past months (mockup 8's own note). Undo sets
    // it straight back to active; nothing about its SortOrder or historical BudgetCategoryAmount rows
    // ever changes.
    public Task SetCategoryActiveAsync(Guid categoryId, bool isActive, CancellationToken cancellationToken) =>
        SetCategoryActiveInternalAsync(categoryId, isActive, cancellationToken);

    private async Task SetCategoryActiveInternalAsync(Guid categoryId, bool isActive, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var category = await db.BudgetCategories.FirstOrDefaultAsync(c => c.Id == categoryId, cancellationToken);
        if (category is null)
        {
            return;
        }
        category.IsActive = isActive;
        await db.SaveChangesAsync(cancellationToken);
    }

    // D110's reorder, delegated to the same pure list arithmetic AccountService/SnowballService
    // already use for their own owner-ordered lists — moves among ACTIVE categories only, since a
    // soft-deleted one is never shown to reorder against.
    public Task MoveCategoryUpAsync(Guid categoryId, CancellationToken cancellationToken) =>
        ApplyCategoryOrderAsync(categoryId, InstitutionOrder.MoveUp, cancellationToken);

    public Task MoveCategoryDownAsync(Guid categoryId, CancellationToken cancellationToken) =>
        ApplyCategoryOrderAsync(categoryId, InstitutionOrder.MoveDown, cancellationToken);

    private async Task ApplyCategoryOrderAsync(
        Guid categoryId,
        Func<IReadOnlyList<Guid>, Guid, IReadOnlyList<(Guid Id, int SortOrder)>> move,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var active = await db.BudgetCategories.Where(c => c.IsActive).OrderBy(c => c.SortOrder).ToListAsync(cancellationToken);
        var newOrder = move(active.Select(c => c.Id).ToList(), categoryId);

        var bySortOrder = newOrder.ToDictionary(entry => entry.Id, entry => entry.SortOrder);
        foreach (var category in active)
        {
            category.SortOrder = bySortOrder[category.Id];
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    // ---- Part 1: months and category amounts ----

    public async Task<BudgetMonth?> FindMonthAsync(string yearMonth, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.BudgetMonths.AsNoTracking().FirstOrDefaultAsync(m => m.YearMonth == yearMonth, cancellationToken);
    }

    public async Task<BudgetMonth> GetOrCreateMonthAsync(string yearMonth, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var month = await db.BudgetMonths.FirstOrDefaultAsync(m => m.YearMonth == yearMonth, cancellationToken);
        if (month is not null)
        {
            return month;
        }

        month = new BudgetMonth { YearMonth = yearMonth, Status = BudgetMonthStatus.Draft };
        db.BudgetMonths.Add(month);
        await db.SaveChangesAsync(cancellationToken);
        return month;
    }

    public async Task<IReadOnlyList<BudgetCategoryAmount>> GetCategoryAmountsAsync(Guid budgetMonthId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.BudgetCategoryAmounts.Where(a => a.BudgetMonthId == budgetMonthId).AsNoTracking().ToListAsync(cancellationToken);
    }

    /// <summary>Makes sure every currently-active category has a row for this month (a brand-new
    /// category, or the first time this month has been touched at all), so nothing has to null-check
    /// its way through "no row yet" everywhere else in this service.</summary>
    public async Task<IReadOnlyList<BudgetCategoryAmount>> EnsureCategoryAmountsAsync(
        Guid budgetMonthId, bool trackingOnly, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var categories = await db.BudgetCategories.Where(c => c.IsActive).ToListAsync(cancellationToken);
        var existing = await db.BudgetCategoryAmounts.Where(a => a.BudgetMonthId == budgetMonthId).ToListAsync(cancellationToken);
        var byCategory = existing.ToDictionary(a => a.CategoryId);

        var added = false;
        foreach (var category in categories)
        {
            if (byCategory.ContainsKey(category.Id))
            {
                continue;
            }
            var amount = new BudgetCategoryAmount { BudgetMonthId = budgetMonthId, CategoryId = category.Id, TrackingOnly = trackingOnly };
            db.BudgetCategoryAmounts.Add(amount);
            byCategory[category.Id] = amount;
            added = true;
        }
        if (added)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        return byCategory.Values.ToList();
    }

    /// <summary>D88's detection: "has the first worksheet come back" is "does ANY category row for this
    /// month carry a limit" — or carry a box that was read and could not be made out (D49). The second
    /// counts because a worksheet whose every box was illegible HAS come back: calling that month empty
    /// would show "the first budget is made on paper" over a sheet that is on the tablet being re-read, and
    /// hide the dog-ear that says so (remediation 3.2 item 3).</summary>
    public async Task<bool> AnyLimitSetAsync(Guid budgetMonthId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.BudgetCategoryAmounts.AnyAsync(
            a => a.BudgetMonthId == budgetMonthId && (a.LimitAmount > 0m || a.LimitUnreadable), cancellationToken);
    }

    public async Task SetCategoryLimitAsync(Guid budgetMonthId, Guid categoryId, decimal limitAmount, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var amount = await db.BudgetCategoryAmounts.FirstOrDefaultAsync(
            a => a.BudgetMonthId == budgetMonthId && a.CategoryId == categoryId, cancellationToken);
        if (amount is null)
        {
            return;
        }
        amount.LimitAmount = limitAmount;
        amount.LimitUnreadable = false;
        await db.SaveChangesAsync(cancellationToken);
    }

    // D49: the worksheet reader couldn't make out this category's figure — held on the category, not
    // asked on the phone (mockup 8's own rule).
    public async Task MarkLimitUnreadableAsync(Guid budgetMonthId, Guid categoryId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var amount = await db.BudgetCategoryAmounts.FirstOrDefaultAsync(
            a => a.BudgetMonthId == budgetMonthId && a.CategoryId == categoryId, cancellationToken);
        if (amount is null)
        {
            return;
        }
        amount.LimitUnreadable = true;
        await db.SaveChangesAsync(cancellationToken);
    }

    // ---- Part 2: transactions across every account (D58, D62, D63, D67) ----

    // The page size RefreshTransactionsAsync asks Plaid for. A pull that comes back FULL proves nothing
    // about what is missing from it (Plaid returns newest first and stops at the count), so the
    // "pending row Plaid no longer reports" cleanup below only runs on a pull that came back short.
    private const int TransactionPullCount = 250;

    /// <summary>Pulls this month's transactions from every Plaid-connected institution (hand-entered
    /// ones have nothing to pull — same exclusion <c>AccountNotices</c>/<c>BalanceRefreshProcessor</c>
    /// already apply), upserts them, and recomputes the month's category totals. A single
    /// institution's failure is logged nowhere yet and simply skipped — best-effort, matching this
    /// app's existing "never let one remote failure block the rest of the screen" idiom.
    ///
    /// <para><b>First category (R8).</b> A new transaction is placed by, in order: the owner's merchant rule
    /// (D58 — always first), then <see cref="PlaidCategoryMap"/> on Plaid's own category, then nothing
    /// (Uncategorized). An EXISTING transaction with no category and no manual override is placed by the
    /// map when Plaid's category is now known (rows stored before the column existed) — unless a merchant
    /// rule matches its merchant, in which case it is left alone: the rule outranks the map, and D62 says a
    /// rule written later does not reach back over earlier transactions. A category the owner chose is
    /// never touched by a re-sync.</para>
    ///
    /// <para><b>Pending (R-G18).</b> Plaid gives a transaction a NEW id when it posts and points back with
    /// <c>pending_transaction_id</c>. The pending row is re-keyed in place to the posted id, so the owner's
    /// category and override survive and only one row is ever stored; if both rows are already stored (a
    /// double count from before this), the pending one is dropped and its owner-chosen category carried
    /// over. A stored pending row that a complete pull no longer reports was reversed by the bank and is
    /// dropped. Pending rows count toward "spent" like any other — Accounts' register lists them (marked
    /// "pending") and the owner sees the same figure at the till — which is only safe because they are
    /// replaced, not added to, when they post.</para></summary>
    public async Task RefreshTransactionsAsync(string yearMonth, CancellationToken cancellationToken)
    {
        var (start, end) = BudgetPeriod.Range(yearMonth);
        var (startOffset, endOffset) = BudgetPeriod.OffsetRange(yearMonth);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var institutions = await db.Institutions.Include(i => i.Accounts).ToListAsync(cancellationToken);
        var rules = await db.MerchantCategoryRules.ToListAsync(cancellationToken);
        var activeCategories = await db.BudgetCategories.Where(c => c.IsActive).ToListAsync(cancellationToken);
        var activeByName = activeCategories
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        MerchantCategoryRule? RuleFor(string merchantName) => rules.FirstOrDefault(
            r => string.Equals(r.MerchantName, merchantName, StringComparison.OrdinalIgnoreCase));

        foreach (var institution in institutions)
        {
            if (string.IsNullOrEmpty(institution.AccessToken) || institution.Accounts.Count == 0)
            {
                continue; // hand-entered, or nothing to pull transactions for
            }

            IReadOnlyList<PlaidTransaction> pulled;
            try
            {
                pulled = await plaid.GetTransactionsAsync(
                    institution.AccessToken, institution.Accounts.Select(a => a.AccountId).ToList(),
                    start, end, count: TransactionPullCount, cancellationToken);
            }
            catch
            {
                continue;
            }

            var seenIds = new HashSet<string>();
            foreach (var transaction in pulled)
            {
                var account = institution.Accounts.FirstOrDefault(a => a.AccountId == transaction.AccountId);
                if (account is null)
                {
                    continue;
                }
                seenIds.Add(transaction.TransactionId);

                var existing = await db.Transactions.FirstOrDefaultAsync(
                    t => t.PlaidTransactionId == transaction.TransactionId, cancellationToken);
                var predecessor = string.IsNullOrEmpty(transaction.PendingTransactionId)
                    || transaction.PendingTransactionId == transaction.TransactionId
                    ? null
                    : await db.Transactions.FirstOrDefaultAsync(
                        t => t.PlaidTransactionId == transaction.PendingTransactionId, cancellationToken);

                var merchantName = string.IsNullOrEmpty(transaction.MerchantName) ? transaction.Name : transaction.MerchantName!;
                var rule = RuleFor(merchantName);

                if (existing is null && predecessor is not null)
                {
                    // The pending row IS this transaction, posted: same row, new id.
                    predecessor.PlaidTransactionId = transaction.TransactionId;
                    existing = predecessor;
                }
                else if (existing is not null && predecessor is not null)
                {
                    // Both are already stored (a double count from before pending was handled): keep the
                    // posted row, carry over whatever the owner decided on the pending one, drop it.
                    if (predecessor.IsManualOverride && !existing.IsManualOverride)
                    {
                        existing.CategoryId = predecessor.CategoryId;
                        existing.IsManualOverride = true;
                    }
                    else if (existing.CategoryId is null && predecessor.CategoryId is not null)
                    {
                        existing.CategoryId = predecessor.CategoryId;
                    }
                    db.Transactions.Remove(predecessor);
                }

                if (existing is null)
                {
                    db.Transactions.Add(new Transaction
                    {
                        AccountId = account.Id,
                        PlaidTransactionId = transaction.TransactionId,
                        MerchantName = merchantName,
                        Amount = (decimal)transaction.Amount,
                        Date = transaction.Date,
                        Pending = transaction.Pending,
                        PlaidCategoryPrimary = transaction.CategoryPrimary,
                        PlaidCategoryDetailed = transaction.CategoryDetailed,
                        CategoryId = ChooseCategory(rule, transaction.CategoryPrimary, transaction.CategoryDetailed, activeByName),
                    });
                }
                else
                {
                    // Refresh what Plaid can still change (amount/date settle as a pending transaction
                    // posts) but never take a category the owner (D58) or a merchant rule already chose.
                    existing.MerchantName = merchantName;
                    existing.Amount = (decimal)transaction.Amount;
                    existing.Date = transaction.Date;
                    existing.Pending = transaction.Pending;
                    existing.PlaidCategoryPrimary = transaction.CategoryPrimary;
                    existing.PlaidCategoryDetailed = transaction.CategoryDetailed;

                    if (existing.CategoryId is null && !existing.IsManualOverride && rule is null)
                    {
                        existing.CategoryId = ChooseCategory(null, transaction.CategoryPrimary, transaction.CategoryDetailed, activeByName);
                    }
                }
            }

            if (pulled.Count < TransactionPullCount)
            {
                var accountIds = institution.Accounts.Select(a => a.Id).ToList();
                var stillPending = await db.Transactions
                    .Where(t => accountIds.Contains(t.AccountId) && t.Date >= startOffset && t.Date <= endOffset)
                    .ToListAsync(cancellationToken);
                foreach (var gone in stillPending.Where(t =>
                             t.Pending && !seenIds.Contains(t.PlaidTransactionId) && db.Entry(t).State != EntityState.Deleted))
                {
                    db.Transactions.Remove(gone);
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        var month = await db.BudgetMonths.FirstOrDefaultAsync(m => m.YearMonth == yearMonth, cancellationToken);
        if (month is not null)
        {
            await RecomputeAggregatesAsync(month.Id, cancellationToken);
        }
    }

    // R-G20's whole rule in one place: the owner's merchant rule, else the Plaid map (onto an ACTIVE
    // category of that name), else Uncategorized (null). NotSpending and Unmapped both fall through to null.
    private static Guid? ChooseCategory(
        MerchantCategoryRule? rule, string? primary, string? detailed, IReadOnlyDictionary<string, Guid> activeByName)
    {
        if (rule is not null)
        {
            return rule.CategoryId;
        }

        var decision = PlaidCategoryMap.Decide(primary, detailed);
        return decision.Kind == PlaidCategoryKind.Category && activeByName.TryGetValue(decision.CategoryName!, out var categoryId)
            ? categoryId
            : null;
    }

    /// <summary>Recomputes every category's <see cref="BudgetCategoryAmount.ActualAmount"/> for one
    /// month from the live <see cref="Transaction"/> rows dated inside it — the single place "what has
    /// this category spent this month" is calculated, called after every transaction pull and after
    /// every recategorize so the screen is never one step behind the data it just changed.</summary>
    public async Task RecomputeAggregatesAsync(Guid budgetMonthId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var month = await db.BudgetMonths.FirstOrDefaultAsync(m => m.Id == budgetMonthId, cancellationToken);
        if (month is null)
        {
            return;
        }

        var (start, end) = BudgetPeriod.OffsetRange(month.YearMonth);
        var amounts = await db.BudgetCategoryAmounts.Where(a => a.BudgetMonthId == budgetMonthId).ToListAsync(cancellationToken);
        var transactions = await db.Transactions
            .Where(t => t.CategoryId != null && t.Date >= start && t.Date <= end)
            .ToListAsync(cancellationToken);

        var totals = transactions
            .GroupBy(t => t.CategoryId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));

        foreach (var amount in amounts)
        {
            amount.ActualAmount = totals.GetValueOrDefault(amount.CategoryId, 0m);
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Every SPENDING transaction with no category, this month — the minimal "Uncategorized"
    /// pseudo-category (neither mockup draws one; it is kept so money Plaid's map could not place is never
    /// invisible). Spending only (D140, R-G19): income, transfers and loan/card payments are not a line on
    /// Budget, so they are not in here either.</summary>
    public async Task<(decimal Total, int Count)> GetUncategorizedAsync(string yearMonth, CancellationToken cancellationToken)
    {
        var uncategorized = await GetUncategorizedSpendingAsync(yearMonth, cancellationToken);
        return (uncategorized.Sum(t => t.Amount), uncategorized.Count);
    }

    private async Task<List<Transaction>> GetUncategorizedSpendingAsync(string yearMonth, CancellationToken cancellationToken)
    {
        var (start, end) = BudgetPeriod.OffsetRange(yearMonth);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var uncategorized = await db.Transactions
            .Where(t => t.CategoryId == null && t.Date >= start && t.Date <= end)
            .OrderByDescending(t => t.Date)
            .ToListAsync(cancellationToken);
        return uncategorized
            .Where(t => !PlaidCategoryMap.IsNotSpending(t.PlaidCategoryPrimary, t.PlaidCategoryDetailed))
            .ToList();
    }

    /// <summary>Everything the month screen draws, in one call: the active categories, their amounts, the
    /// spending-only Uncategorized figure, and the totals that agree with exactly those cards (R-G17).</summary>
    public async Task<BudgetMonthFigures> GetMonthFiguresAsync(BudgetMonth month, CancellationToken cancellationToken)
    {
        var categories = await GetActiveCategoriesAsync(cancellationToken);
        var amounts = await GetCategoryAmountsAsync(month.Id, cancellationToken);
        var (uncategorizedTotal, uncategorizedCount) = await GetUncategorizedAsync(month.YearMonth, cancellationToken);
        var lastMonth = await GetLastMonthSpendAsync(month.YearMonth, cancellationToken);
        return BudgetMonthFigures.Compute(categories, amounts, uncategorizedTotal, uncategorizedCount, lastMonth);
    }

    /// <summary>D142: each category's recorded spend for the calendar month before <paramref name="yearMonth"/>
    /// — the same <see cref="BudgetCategoryAmount.ActualAmount"/> rows <see cref="GetCategoryHistoryAsync"/>
    /// reads (D66's kept totals; spending only, because they are the sums the cards themselves show). A
    /// category with no row for that month has no entry: it was not measured, which is not a zero.</summary>
    public async Task<IReadOnlyDictionary<Guid, decimal>> GetLastMonthSpendAsync(string yearMonth, CancellationToken cancellationToken)
    {
        var previous = await FindMonthAsync(BudgetPeriod.PreviousYearMonth(yearMonth), cancellationToken);
        if (previous is null)
        {
            return new Dictionary<Guid, decimal>();
        }
        var amounts = await GetCategoryAmountsAsync(previous.Id, cancellationToken);
        return amounts.ToDictionary(a => a.CategoryId, a => a.ActualAmount);
    }

    public async Task<IReadOnlyList<BudgetTransactionView>> GetTransactionsForCategoryAsync(
        Guid? categoryId, string yearMonth, CancellationToken cancellationToken)
    {
        var (start, end) = BudgetPeriod.OffsetRange(yearMonth);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var transactions = await db.Transactions
            .Where(t => t.CategoryId == categoryId && t.Date >= start && t.Date <= end)
            .OrderByDescending(t => t.Date)
            .ToListAsync(cancellationToken);
        if (categoryId is null)
        {
            // The Uncategorized post-it lists exactly what the Uncategorized card adds up (R-G19).
            transactions = transactions
                .Where(t => !PlaidCategoryMap.IsNotSpending(t.PlaidCategoryPrimary, t.PlaidCategoryDetailed))
                .ToList();
        }

        var accountIds = transactions.Select(t => t.AccountId).Distinct().ToList();
        var accountNames = await db.Accounts
            .Where(a => accountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken);

        return transactions
            .Select(t => new BudgetTransactionView(t, accountNames.GetValueOrDefault(t.AccountId, string.Empty)))
            .ToList();
    }

    /// <summary>
    /// Remediation 3.3 item 1 (mockup 3): the OWNER's category name for each of Plaid's live transactions, keyed
    /// by Plaid's transaction id — what the Accounts post-it prints beside the date. Joined to the stored
    /// <see cref="Transaction"/> by that id; a posted transaction whose stored row is still under its PENDING id
    /// (Budget re-keys on its next pull) is found through <see cref="PlaidTransaction.PendingTransactionId"/>.
    /// A transaction with no stored row, or an uncategorized one, simply has no entry — the row then shows no
    /// category rather than a guess. A category since removed still names what was filed under it (D55:
    /// categories are soft-deleted so history keeps reading).
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> GetCategoryNamesAsync(
        IReadOnlyList<PlaidTransaction> transactions, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>();
        if (transactions.Count == 0)
        {
            return result;
        }

        var ids = transactions
            .SelectMany(t => new[] { t.TransactionId, t.PendingTransactionId })
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!)
            .Distinct()
            .ToList();

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Chunked: a long list in one IN (...) would exceed SQLite's bound-parameter limit.
        var stored = new Dictionary<string, Guid>();
        foreach (var chunk in ids.Chunk(500))
        {
            var rows = await db.Transactions
                .Where(t => t.CategoryId != null && chunk.Contains(t.PlaidTransactionId))
                .Select(t => new { t.PlaidTransactionId, CategoryId = t.CategoryId!.Value })
                .ToListAsync(cancellationToken);
            foreach (var row in rows)
            {
                stored[row.PlaidTransactionId] = row.CategoryId;
            }
        }

        if (stored.Count == 0)
        {
            return result;
        }

        var categoryIds = stored.Values.Distinct().ToList();
        var names = await db.BudgetCategories
            .Where(c => categoryIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        foreach (var transaction in transactions)
        {
            if (stored.TryGetValue(transaction.TransactionId, out var categoryId)
                || (!string.IsNullOrEmpty(transaction.PendingTransactionId)
                    && stored.TryGetValue(transaction.PendingTransactionId, out categoryId)))
            {
                if (names.TryGetValue(categoryId, out var name))
                {
                    result[transaction.TransactionId] = name;
                }
            }
        }

        return result;
    }

    // D58/D62: moves one transaction now (a one-off manual override) and writes/updates the standing
    // merchant rule that decides every transaction from this merchant from now on. Returns what to
    // reverse for the undo dog-ear.
    public async Task<RecategorizeUndoState> RecategorizeTransactionAsync(
        Guid transactionId, Guid newCategoryId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var transaction = await db.Transactions.FirstOrDefaultAsync(t => t.Id == transactionId, cancellationToken)
            ?? throw new InvalidOperationException("The transaction no longer exists.");

        var previousCategoryId = transaction.CategoryId;
        var previousIsManualOverride = transaction.IsManualOverride;

        var rules = await db.MerchantCategoryRules.ToListAsync(cancellationToken);
        var existingRule = rules.FirstOrDefault(
            r => string.Equals(r.MerchantName, transaction.MerchantName, StringComparison.OrdinalIgnoreCase));
        var previousRule = existingRule is null
            ? null
            : new MerchantCategoryRuleSnapshot(existingRule.MerchantName, existingRule.CategoryId);

        if (existingRule is null)
        {
            db.MerchantCategoryRules.Add(new MerchantCategoryRule { MerchantName = transaction.MerchantName, CategoryId = newCategoryId });
        }
        else
        {
            existingRule.CategoryId = newCategoryId;
        }

        transaction.CategoryId = newCategoryId;
        transaction.IsManualOverride = true;
        await db.SaveChangesAsync(cancellationToken);

        var month = await db.BudgetMonths.FirstOrDefaultAsync(
            m => m.YearMonth == BudgetPeriod.YearMonthOf(transaction.Date), cancellationToken);
        if (month is not null)
        {
            await RecomputeAggregatesAsync(month.Id, cancellationToken);
        }

        return new RecategorizeUndoState(transactionId, previousCategoryId, previousIsManualOverride, previousRule);
    }

    public async Task RevertRecategorizeAsync(RecategorizeUndoState state, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var transaction = await db.Transactions.FirstOrDefaultAsync(t => t.Id == state.TransactionId, cancellationToken);
        if (transaction is null)
        {
            return;
        }

        transaction.CategoryId = state.PreviousCategoryId;
        transaction.IsManualOverride = state.PreviousIsManualOverride;

        var rules = await db.MerchantCategoryRules.ToListAsync(cancellationToken);
        var current = rules.FirstOrDefault(
            r => string.Equals(r.MerchantName, transaction.MerchantName, StringComparison.OrdinalIgnoreCase));

        if (state.PreviousRule is null)
        {
            if (current is not null)
            {
                db.MerchantCategoryRules.Remove(current);
            }
        }
        else if (current is null)
        {
            db.MerchantCategoryRules.Add(new MerchantCategoryRule
            {
                MerchantName = state.PreviousRule.MerchantName,
                CategoryId = state.PreviousRule.CategoryId,
            });
        }
        else
        {
            current.CategoryId = state.PreviousRule.CategoryId;
        }

        await db.SaveChangesAsync(cancellationToken);

        var month = await db.BudgetMonths.FirstOrDefaultAsync(
            m => m.YearMonth == BudgetPeriod.YearMonthOf(transaction.Date), cancellationToken);
        if (month is not null)
        {
            await RecomputeAggregatesAsync(month.Id, cancellationToken);
        }
    }

    // ---- Part 4: tracking-only history (D142) ----

    /// <summary>Every month this category has a recorded total for, newest first — precisely the rows
    /// D66's purge leaves behind (this release's own current month included, since it is written by
    /// <see cref="RecomputeAggregatesAsync"/> long before any approval purges its transactions).</summary>
    public async Task<IReadOnlyList<CategoryMonthPoint>> GetCategoryHistoryAsync(
        Guid categoryId, string currentYearMonth, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var months = await db.BudgetMonths.OrderByDescending(m => m.YearMonth).ToListAsync(cancellationToken);
        var amounts = await db.BudgetCategoryAmounts
            .Where(a => a.CategoryId == categoryId)
            .ToDictionaryAsync(a => a.BudgetMonthId, cancellationToken);

        var points = new List<CategoryMonthPoint>();
        foreach (var month in months)
        {
            if (amounts.TryGetValue(month.Id, out var amount))
            {
                points.Add(new CategoryMonthPoint(month.YearMonth, amount.ActualAmount, month.YearMonth == currentYearMonth));
            }
        }
        return points;
    }

    // ---- Release F: the worksheet round-trip (D52/D60/D89/D155). Extends this service rather than a
    // second one, per the repo's architecture rules (no repository layer above LedgerDbContext) and
    // per this task's own brief. WorksheetDeliveryService/WorksheetReadBackService (Ledger.Infrastructure/
    // Worksheet) are BudgetService's only callers for these, the same way Pages.BudgetPage/SetupPage are
    // its only callers above. ----

    /// <summary>Records that this month's worksheet was just delivered: the category ids, in the EXACT
    /// order they were printed (Guard G18 — this is what lets a later read-back reconstruct which
    /// <c>WorksheetRegions</c> box belonged to which category even if the live list has since changed),
    /// and the D66 status stage this now occupies.
    ///
    /// <para>Release H's own additions — <paramref name="setAsideKeysInPrintedOrder"/> and
    /// <paramref name="printedZeroBasedTargetWholeDollars"/> — are optional and default to "none
    /// printed", so a plan built before Release H (or any existing caller/test) records exactly what it
    /// always has, and the balance check that reads these back simply never runs for that month.</para>
    /// </summary>
    public async Task RecordWorksheetDeliveryAsync(
        Guid budgetMonthId,
        IReadOnlyList<Guid> categoryIdsInPrintedOrder,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? setAsideKeysInPrintedOrder = null,
        long? printedZeroBasedTargetWholeDollars = null,
        long? printedExtraPaymentLastMonthWholeDollars = null)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var month = await db.BudgetMonths.FirstOrDefaultAsync(m => m.Id == budgetMonthId, cancellationToken);
        if (month is null)
        {
            return;
        }
        month.Status = BudgetMonthStatus.DeliveredToRemarkable;
        month.WorksheetCategoryOrderJson = JsonSerializer.Serialize(categoryIdsInPrintedOrder);
        month.WorksheetSetAsideOrderJson = setAsideKeysInPrintedOrder is null
            ? null
            : JsonSerializer.Serialize(setAsideKeysInPrintedOrder);
        month.PrintedZeroBasedTargetWholeDollars = printedZeroBasedTargetWholeDollars;
        month.PrintedExtraPaymentLastMonthWholeDollars = printedExtraPaymentLastMonthWholeDollars;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The category ids in the order this month's worksheet was printed, or null when this
    /// month was never delivered as a worksheet (nothing to read back against).</summary>
    public async Task<IReadOnlyList<Guid>?> GetWorksheetCategoryOrderAsync(Guid budgetMonthId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var json = await db.BudgetMonths.AsNoTracking().Where(m => m.Id == budgetMonthId)
            .Select(m => m.WorksheetCategoryOrderJson).FirstOrDefaultAsync(cancellationToken);
        return string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<List<Guid>>(json);
    }

    /// <summary>Release H: the set-aside box keys in the order this month's worksheet was printed, or
    /// null when none were printed (a plan built before Release H, or income wasn't confident).</summary>
    public async Task<IReadOnlyList<string>?> GetWorksheetSetAsideOrderAsync(Guid budgetMonthId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var json = await db.BudgetMonths.AsNoTracking().Where(m => m.Id == budgetMonthId)
            .Select(m => m.WorksheetSetAsideOrderJson).FirstOrDefaultAsync(cancellationToken);
        return string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<List<string>>(json);
    }

    /// <summary>D155: "a figure written there wins for the month" — the owner's hand-written correction
    /// to the projected income, read back off the worksheet's income-correction box.</summary>
    public async Task SetIncomeOverrideAsync(Guid budgetMonthId, decimal amount, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var month = await db.BudgetMonths.FirstOrDefaultAsync(m => m.Id == budgetMonthId, cancellationToken);
        if (month is null)
        {
            return;
        }
        month.IncomeOverride = amount;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>D66's third stage: the worksheet has been read back and its figures applied — the zero
    /// check itself (moving to <see cref="BudgetMonthStatus.Approved"/>) is Release H's own job, not
    /// this one's.</summary>
    public async Task MarkAwaitingConfirmationAsync(Guid budgetMonthId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var month = await db.BudgetMonths.FirstOrDefaultAsync(m => m.Id == budgetMonthId, cancellationToken);
        if (month is null)
        {
            return;
        }
        month.Status = BudgetMonthStatus.AwaitingConfirmation;
        await db.SaveChangesAsync(cancellationToken);
    }

    // ---- Release H: zero-based (D98/D99/D100/D153/D156, Guards G4/G21) ----

    /// <summary>D98/D99: one row per savings goal set aside on this month's worksheet, upserted the same
    /// shape <see cref="EnsureCategoryAmountsAsync"/> uses for categories — called once per month before
    /// any read-back can apply a limit to it.
    ///
    /// <para>Each new row starts at the figure the sheet PRINTED beside the line (Guard R-G14), not at
    /// zero. The page tells the owner "an empty box keeps last month's figure", so a box left blank is
    /// an owner who agreed — and the balance check must then sum that printed figure. A row seeded at
    /// zero sums to zero for every savings line the owner agreed with, and the check reports "under" by
    /// exactly the savings they accepted. A row that already exists is left alone: its limit may be
    /// something the owner wrote.</para></summary>
    public async Task EnsureSavingsSetAsideAmountsAsync(
        Guid budgetMonthId, IReadOnlyList<(Guid SavingsAccountId, decimal PrintedFigure)> savingsLines, CancellationToken cancellationToken)
    {
        if (savingsLines.Count == 0)
        {
            return;
        }
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.WorksheetSetAsideAmounts
            .Where(a => a.BudgetMonthId == budgetMonthId).Select(a => a.SavingsAccountId).ToListAsync(cancellationToken);
        var existingSet = existing.ToHashSet();

        var added = false;
        foreach (var (accountId, printedFigure) in savingsLines)
        {
            if (!existingSet.Add(accountId))
            {
                continue;
            }
            db.WorksheetSetAsideAmounts.Add(new WorksheetSetAsideAmount
            {
                BudgetMonthId = budgetMonthId,
                SavingsAccountId = accountId,
                LimitAmount = printedFigure,
            });
            added = true;
        }
        if (added)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<WorksheetSetAsideAmount>> GetSavingsSetAsideAmountsAsync(
        Guid budgetMonthId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.WorksheetSetAsideAmounts.Where(a => a.BudgetMonthId == budgetMonthId).AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task SetSavingsSetAsideLimitAsync(
        Guid budgetMonthId, Guid savingsAccountId, decimal limitAmount, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var amount = await db.WorksheetSetAsideAmounts.FirstOrDefaultAsync(
            a => a.BudgetMonthId == budgetMonthId && a.SavingsAccountId == savingsAccountId, cancellationToken);
        if (amount is null)
        {
            return;
        }
        amount.LimitAmount = limitAmount;
        amount.LimitUnreadable = false;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkSavingsSetAsideLimitUnreadableAsync(
        Guid budgetMonthId, Guid savingsAccountId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var amount = await db.WorksheetSetAsideAmounts.FirstOrDefaultAsync(
            a => a.BudgetMonthId == budgetMonthId && a.SavingsAccountId == savingsAccountId, cancellationToken);
        if (amount is null)
        {
            return;
        }
        amount.LimitUnreadable = true;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Guard G4's own sum: every category's written limit plus every savings set-aside limit
    /// for this month — NOT the extra debt payment, which lives in <c>SnowballPlan.ExtraPaymentAmount</c>
    /// and is the caller's (<c>WorksheetReadBackService</c>'s) own job to add, since it talks to
    /// <c>SnowballService</c> directly.</summary>
    public async Task<IReadOnlyList<decimal>> GetWrittenLimitsForBalanceCheckAsync(Guid budgetMonthId, CancellationToken cancellationToken)
    {
        var categoryAmounts = await GetCategoryAmountsAsync(budgetMonthId, cancellationToken);
        var savingsAmounts = await GetSavingsSetAsideAmountsAsync(budgetMonthId, cancellationToken);
        return categoryAmounts.Select(a => a.LimitAmount).Concat(savingsAmounts.Select(a => a.LimitAmount)).ToList();
    }

    /// <summary>D100's zero check passed: the worksheet balances, and there is nothing left pending for
    /// this month.</summary>
    public async Task MarkBalancedAsync(Guid budgetMonthId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var month = await db.BudgetMonths.FirstOrDefaultAsync(m => m.Id == budgetMonthId, cancellationToken);
        if (month is null)
        {
            return;
        }
        month.Status = BudgetMonthStatus.Approved;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>D153: "repeats each morning until the figures balance" — the one month (there is only
    /// ever ONE zero-based cycle active at a time) whose worksheet went through a REAL balance check
    /// (it carries a printed target) and has not yet balanced. Null once nothing is pending.</summary>
    public async Task<BudgetMonth?> FindMonthNeedingFollowUpAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.BudgetMonths.AsNoTracking()
            .Where(m => m.Status == BudgetMonthStatus.AwaitingConfirmation && m.PrintedZeroBasedTargetWholeDollars != null)
            .OrderBy(m => m.YearMonth)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>D49, Guard R-G25: the month whose worksheet has been read and left a limit nobody could
    /// make out — a visible category's box, or a savings set-aside box (<c>LimitUnreadable</c>) — and so
    /// owes the owner a page asking for it again. Independent of the zero check on purpose: a month that
    /// BALANCED (status Approved) is invisible to <see cref="FindMonthNeedingFollowUpAsync"/>, and without
    /// this the unread box on a balanced month would be asked for by nothing, ever.
    ///
    /// <para>Only months that have been read (AwaitingConfirmation or Approved — an unread sheet has no
    /// flags yet) and only from <paramref name="currentYearMonth"/> on: the worksheet is printed for the
    /// month AHEAD and read once that month has begun, and a month that is over has nothing left to
    /// correct (the Budget tab only ever draws the current one). The latest such month wins.</para></summary>
    public async Task<BudgetMonth?> FindMonthWithUnreadableLimitsAsync(string currentYearMonth, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var read = await db.BudgetMonths.AsNoTracking()
            .Where(m => m.WorksheetCategoryOrderJson != null
                && (m.Status == BudgetMonthStatus.AwaitingConfirmation || m.Status == BudgetMonthStatus.Approved))
            .ToListAsync(cancellationToken);

        var activeCategoryIds = await db.BudgetCategories.Where(c => c.IsActive).Select(c => c.Id).ToListAsync(cancellationToken);
        foreach (var month in read
            .Where(m => string.CompareOrdinal(m.YearMonth, currentYearMonth) >= 0)
            .OrderByDescending(m => m.YearMonth, StringComparer.Ordinal))
        {
            var categoryUnreadable = await db.BudgetCategoryAmounts.AnyAsync(
                a => a.BudgetMonthId == month.Id && a.LimitUnreadable && activeCategoryIds.Contains(a.CategoryId), cancellationToken);
            var savingsUnreadable = await db.WorksheetSetAsideAmounts.AnyAsync(
                a => a.BudgetMonthId == month.Id && a.LimitUnreadable, cancellationToken);
            if (categoryUnreadable || savingsUnreadable)
            {
                return month;
            }
        }
        return null;
    }

    /// <summary>D153, Guard R-G13: the sheet on the tablet that nobody has read yet — the most recently
    /// delivered worksheet, if (and only if) it is still at <see cref="BudgetMonthStatus.DeliveredToRemarkable"/>.
    /// "Most recent" because a worksheet replaces the last on the reMarkable (D60), so an older month
    /// that was never read can no longer be read at all. The status is the whole bookkeeping: the first
    /// read-back moves the month on to AwaitingConfirmation or Approved, so a sheet is read once.</summary>
    public async Task<BudgetMonth?> FindWorksheetAwaitingFirstReadBackAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var latest = await db.BudgetMonths.AsNoTracking()
            .Where(m => m.WorksheetCategoryOrderJson != null)
            .OrderByDescending(m => m.YearMonth)
            .FirstOrDefaultAsync(cancellationToken);
        return latest is { Status: BudgetMonthStatus.DeliveredToRemarkable } ? latest : null;
    }

    // ---- Part 1/D66: retention (Guard G5) ----

    /// <summary>Approves this month and, per D66's exact wording ("kept only until the NEXT budget is
    /// approved"), purges the PREVIOUS calendar month's <see cref="Transaction"/> rows — never this
    /// month's own. <see cref="BudgetCategoryAmount.ActualAmount"/> is a different table this delete
    /// never touches, so the previous month's category totals survive unchanged.</summary>
    public async Task ApproveMonthAsync(Guid budgetMonthId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var month = await db.BudgetMonths.FirstOrDefaultAsync(m => m.Id == budgetMonthId, cancellationToken);
        if (month is null)
        {
            return;
        }

        month.Status = BudgetMonthStatus.Approved;
        await db.SaveChangesAsync(cancellationToken);

        var previousYearMonth = BudgetPeriod.PreviousYearMonth(month.YearMonth);
        var (start, end) = BudgetPeriod.OffsetRange(previousYearMonth);

        var stale = await db.Transactions.Where(t => t.Date >= start && t.Date <= end).ToListAsync(cancellationToken);
        if (stale.Count > 0)
        {
            db.Transactions.RemoveRange(stale);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
