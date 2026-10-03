using Microsoft.EntityFrameworkCore;
using Index.Domain.Model;
using Index.Domain.Time;
using Index.Infrastructure.Persistence;

namespace Index.Infrastructure.Services;

/// <summary>One savings account on the Accounts side (mockup 9): its facts, its goal if it has one,
/// and — when it has both a goal and enough history — the pace behind the projected date.</summary>
public sealed record SavingsAccountView(
    Guid AccountId,
    string InstitutionName,
    string AccountName,
    decimal Balance,
    decimal? GoalTargetAmount,
    decimal MonthlyPace,
    int MonthsOfHistory,
    SavingsReachesIt? ReachesIt,
    string? BankPrimaryColorHex = null);

/// <summary>The Accounts side's header total (mockup 9's <c>.sb-now</c>) plus every savings account.</summary>
public sealed record SavingsAccountsSummary(decimal TotalSaved, IReadOnlyList<SavingsAccountView> Accounts);

/// <summary>One month's recorded balance, for the Growth side's chart.</summary>
public sealed record SavingsHistoryPoint(DateOnly MonthOf, decimal Balance);

/// <summary>One savings account's whole recorded history, plus its goal/projection if it has one — the
/// Growth chart's own line, dotted projection and dashed goal reference (mockup 9's second frame).</summary>
public sealed record SavingsAccountGrowth(
    Guid AccountId,
    string InstitutionName,
    string AccountName,
    IReadOnlyList<SavingsHistoryPoint> Points,
    decimal? GoalTargetAmount,
    SavingsReachesIt? ReachesIt);

/// <summary>
/// D50/D56/D81/D82/D83: the Savings tab's own service — which accounts count as savings
/// (<see cref="AccountSavings.IsEffectivelySavings"/>, read here but never re-decided), the monthly
/// snapshot writer, and the goal/pace/projection arithmetic (<see cref="SavingsPace"/>). Talks to
/// <see cref="LedgerDbContext"/> directly, never through a repository layer, per the repo's
/// architecture rules; <c>Pages.SavingsPage</c> is the only caller.
/// </summary>
public sealed class SavingsService
{
    private readonly IDbContextFactory<LedgerDbContext> dbContextFactory;
    private readonly IClock clock;

    public SavingsService(IDbContextFactory<LedgerDbContext> dbContextFactory, IClock clock)
    {
        this.dbContextFactory = dbContextFactory;
        this.clock = clock;
    }

    private static DateOnly FirstOfMonth(DateOnly day) => new(day.Year, day.Month, 1);

    // ---- which accounts are savings, and their current facts (mockup 9's Accounts side) ----

    public async Task<SavingsAccountsSummary> GetAccountsAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var institutions = await db.Institutions.Include(i => i.Accounts).AsNoTracking().ToListAsync(cancellationToken);
        var savingsAccounts = institutions
            .SelectMany(institution => institution.Accounts
                .Where(AccountSavings.IsEffectivelySavings)
                .Select(account => (Institution: institution, Account: account)))
            .OrderBy(x => x.Institution.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Account.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (savingsAccounts.Count == 0)
        {
            return new SavingsAccountsSummary(0m, []);
        }

        var accountIds = savingsAccounts.Select(x => x.Account.Id).ToList();
        var goalsByAccountId = (await db.SavingsGoals
                .Where(g => accountIds.Contains(g.AccountId))
                .AsNoTracking()
                .ToListAsync(cancellationToken))
            .ToDictionary(g => g.AccountId);
        var snapshotsByAccountId = (await db.SavingsSnapshots
                .Where(s => accountIds.Contains(s.AccountId))
                .AsNoTracking()
                .ToListAsync(cancellationToken))
            .ToLookup(s => s.AccountId);

        var asOf = DateOnly.FromDateTime(clock.UtcNow.LocalDateTime.Date);
        var views = savingsAccounts.Select(x =>
        {
            var goal = goalsByAccountId.GetValueOrDefault(x.Account.Id);
            var (pace, months) = SavingsPace.ComputePace(snapshotsByAccountId[x.Account.Id].ToList());
            var reachesIt = goal is null
                ? (SavingsReachesIt?)null
                : SavingsPace.ReachesIt(x.Account.CurrentBalance ?? 0m, goal.TargetAmount, pace, asOf);

            return new SavingsAccountView(
                x.Account.Id, x.Institution.Name, x.Account.Name, x.Account.CurrentBalance ?? 0m,
                goal?.TargetAmount, pace, months, reachesIt, x.Institution.PrimaryColorHex);
        }).ToList();

        var total = views.Sum(v => v.Balance);
        return new SavingsAccountsSummary(total, views);
    }

    // ---- the goal post-it (D111, D159, D160, mockup 16) ----

    public async Task<SavingsGoal?> FindGoalAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.SavingsGoals.AsNoTracking().FirstOrDefaultAsync(g => g.AccountId == accountId, cancellationToken);
    }

    /// <summary>D50/D82: one goal per account — upserts by <c>AccountId</c> rather than ever creating a
    /// second row, since the goal is an amount attached to the account, not a history of goals.</summary>
    public async Task SetGoalAsync(Guid accountId, decimal targetAmount, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var goal = await db.SavingsGoals.FirstOrDefaultAsync(g => g.AccountId == accountId, cancellationToken);
        if (goal is null)
        {
            db.SavingsGoals.Add(new SavingsGoal { AccountId = accountId, TargetAmount = targetAmount });
        }
        else
        {
            goal.TargetAmount = targetAmount;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>D112/D160: undo-first Remove — the account stays, only this row goes. Mirrors
    /// <c>AccountService.RemoveAccountAsync</c>/<c>RestoreAccountAsync</c>'s own capture-before-delete
    /// shape exactly, so <c>SavingsPage</c>'s dog-ear reverses it the same way MainPage's does.</summary>
    public async Task RemoveGoalAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var goal = await db.SavingsGoals.FirstOrDefaultAsync(g => g.AccountId == accountId, cancellationToken);
        if (goal is null)
        {
            return;
        }
        db.SavingsGoals.Remove(goal);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The undo's reverse: re-inserts the captured row exactly as it was, including its
    /// original Id — a no-op if the window somehow closed twice (defensive; DogEarState only ever
    /// fires one reverse per fold), and a no-op if a new goal was set on the account in the meantime
    /// (the owner's later action wins over a stale undo).</summary>
    public async Task RestoreGoalAsync(SavingsGoal goal, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (await db.SavingsGoals.AnyAsync(g => g.Id == goal.Id || g.AccountId == goal.AccountId, cancellationToken))
        {
            return;
        }
        db.SavingsGoals.Add(goal);
        await db.SaveChangesAsync(cancellationToken);
    }

    // ---- the monthly snapshot writer (D81), dedupe-at-write ----

    /// <summary>Writes this calendar month's balance for every effectively-savings account, but only
    /// for an (AccountId, MonthOf) pair that doesn't already have a row — opening the Savings tab more
    /// than once in the same month is a no-op, never a duplicate (the same rule
    /// <c>SnowballService.RecordCurrentWeekSnapshotAsync</c> applies at its own, weekly cadence).</summary>
    public async Task RecordCurrentMonthSnapshotAsync(CancellationToken cancellationToken)
    {
        var monthOf = FirstOfMonth(DateOnly.FromDateTime(clock.UtcNow.LocalDateTime.Date));

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var savingsAccounts = (await db.Accounts.ToListAsync(cancellationToken))
            .Where(AccountSavings.IsEffectivelySavings)
            .ToList();

        var alreadyRecorded = (await db.SavingsSnapshots
                .Where(s => s.MonthOf == monthOf)
                .Select(s => s.AccountId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var added = false;
        foreach (var account in savingsAccounts)
        {
            if (!alreadyRecorded.Add(account.Id))
            {
                continue;
            }
            db.SavingsSnapshots.Add(new SavingsSnapshot { MonthOf = monthOf, AccountId = account.Id, Balance = account.CurrentBalance ?? 0m });
            added = true;
        }

        if (added)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    // ---- the Growth side (mockup 9's second frame) ----

    public async Task<IReadOnlyList<SavingsAccountGrowth>> GetGrowthAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var institutions = await db.Institutions.Include(i => i.Accounts).AsNoTracking().ToListAsync(cancellationToken);
        var savingsAccounts = institutions
            .SelectMany(institution => institution.Accounts
                .Where(AccountSavings.IsEffectivelySavings)
                .Select(account => (Institution: institution, Account: account)))
            .OrderBy(x => x.Institution.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Account.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (savingsAccounts.Count == 0)
        {
            return [];
        }

        var accountIds = savingsAccounts.Select(x => x.Account.Id).ToList();
        var goalsByAccountId = (await db.SavingsGoals
                .Where(g => accountIds.Contains(g.AccountId))
                .AsNoTracking()
                .ToListAsync(cancellationToken))
            .ToDictionary(g => g.AccountId);
        var snapshotsByAccountId = (await db.SavingsSnapshots
                .Where(s => accountIds.Contains(s.AccountId))
                .AsNoTracking()
                .ToListAsync(cancellationToken))
            .ToLookup(s => s.AccountId);

        var asOf = DateOnly.FromDateTime(clock.UtcNow.LocalDateTime.Date);

        return savingsAccounts.Select(x =>
        {
            var history = snapshotsByAccountId[x.Account.Id].OrderBy(s => s.MonthOf).ToList();
            var points = history.Select(s => new SavingsHistoryPoint(s.MonthOf, s.Balance)).ToList();

            var goal = goalsByAccountId.GetValueOrDefault(x.Account.Id);
            SavingsReachesIt? reachesIt = null;
            if (goal is not null)
            {
                var (pace, _) = SavingsPace.ComputePace(history);
                reachesIt = SavingsPace.ReachesIt(x.Account.CurrentBalance ?? 0m, goal.TargetAmount, pace, asOf);
            }

            return new SavingsAccountGrowth(
                x.Account.Id, x.Institution.Name, x.Account.Name, points, goal?.TargetAmount, reachesIt);
        }).ToList();
    }
}
