using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Index.Domain.Household;
using Index.Domain.Model;
using Index.Domain.Time;
using Index.Infrastructure.Persistence;
using Index.Infrastructure.Snowball;

namespace Index.Infrastructure.Services;

/// <summary>One loan on the Loans side (mockup 6): its own facts, its place in the payoff order, and
/// the post-it's projected numbers for once it becomes the focus.</summary>
public sealed record SnowballLoanView(
    Guid AccountId,
    string Name,
    int? DisplayNumber,
    bool IsFocus,
    bool IsPaidOff,
    decimal Balance,
    decimal Apr,
    decimal WeeklyPaymentNow,
    decimal FocusedWeeklyPayment,
    int WeeksBeforeFocused,
    decimal AmountBeforeFocused,
    int WeeksToPayoff,
    DateOnly PayoffDate,
    string? BankPrimaryColorHex = null);

/// <summary>The Loans side's header facts plus every loan, in DISPLAY order (active loans first, in
/// payoff order; paid-off loans last).</summary>
public sealed record SnowballSummary(
    IReadOnlyList<SnowballLoanView> LoansInDisplayOrder,
    DateOnly DebtFreeDate,
    int TotalWeeks,
    decimal TotalOwed,
    decimal ExtraPaymentAmount);

/// <summary>One week's balance for one loan, for the History charts.</summary>
public sealed record SnowballHistoryPoint(DateOnly WeekOf, decimal Balance);

/// <summary>One loan's whole recorded history — every week it has a snapshot for, plus its own peak
/// balance (each small-multiple panel runs $0 to its OWN loan's peak, mockup 6's own foot note).</summary>
public sealed record SnowballLoanHistory(
    Guid AccountId, string Name, IReadOnlyList<SnowballHistoryPoint> Points, decimal Peak, bool IsPaidOff,
    bool IsMortgage = false);

/// <summary>The whole History side: every recorded week, and every loan's series across them, in the
/// same FOCUS order as the Loans side (D138: the mortgage — last in focus order — naturally sits at the
/// foot of the stacked bars and gets the last small-multiple panel, with no separate flag needed).</summary>
public sealed record SnowballHistory(IReadOnlyList<DateOnly> Weeks, IReadOnlyList<SnowballLoanHistory> LoansInFocusOrder)
{
    /// <summary>Nothing to draw: no recorded week, or no loan to draw one for.</summary>
    public bool IsEmpty => Weeks.Count == 0 || LoansInFocusOrder.Count == 0;
}

/// <summary>
/// D134/D03/D78: the Snowball's own service — the sheet's formulas (<see cref="SnowballEngine"/>) run
/// against the owner's real loan accounts and focus order, the one-time history import, and the weekly
/// snapshot writer. Talks to <see cref="LedgerDbContext"/> directly, never through a repository layer,
/// per the repo's architecture rules; <c>Pages.SnowballPage</c> is the only caller.
/// </summary>
public sealed class SnowballService
{
    private readonly IDbContextFactory<LedgerDbContext> dbContextFactory;
    private readonly IClock clock;
    private readonly SnowballHistoryImport? historyImport;
    private readonly ILogger<SnowballService>? logger;

    public SnowballService(
        IDbContextFactory<LedgerDbContext> dbContextFactory, IClock clock,
        SnowballHistoryImport? historyImport = null, ILogger<SnowballService>? logger = null)
    {
        this.dbContextFactory = dbContextFactory;
        this.clock = clock;
        this.historyImport = historyImport;
        this.logger = logger;
    }

    // D04: an account is "in the Snowball" once it has both an APR and a payment set — the exact
    // condition MainPage's manual post-it already states in words ("Not in the Snowball until it has
    // an APR and a payment."). A Plaid-connected card/loan qualifies the same way once Liabilities has
    // reported both.
    private static bool IsSnowballLoan(Account account) =>
        account.Kind is AccountKind.Credit or AccountKind.Loan
        && account.Apr is not null
        && account.NextPaymentAmount is not null;

    // ---- focus order (D04, D43) ----

    /// <summary>
    /// Every eligible loan account, in the owner's own focus order — auto-creating a
    /// <see cref="SnowballPlan"/> row for any that doesn't have one yet (a brand-new manual loan, or an
    /// existing account that just gained its APR/payment), always at the BOTTOM of whatever order
    /// already exists, exactly the rule <c>AccountService.AddInstitutionAsync</c> already applies to a
    /// brand-new bank (D123, extended here to loans) — never re-inserted alphabetically or by balance.
    /// </summary>
    public async Task<IReadOnlyList<(Account Account, SnowballPlan Plan)>> GetLoansInFocusOrderAsync(
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var loanAccounts = await db.Accounts
            .Where(a => a.Kind == AccountKind.Credit || a.Kind == AccountKind.Loan)
            .ToListAsync(cancellationToken);
        var eligible = loanAccounts.Where(IsSnowballLoan).ToList();

        var plans = await db.SnowballPlans.ToListAsync(cancellationToken);
        var plansByLoanId = plans.ToDictionary(p => p.LoanAccountId);

        var missing = eligible.Where(a => !plansByLoanId.ContainsKey(a.Id)).ToList();
        if (missing.Count > 0)
        {
            // Every existing row is kept in sync (SetExtraPaymentAmountAsync), so any one of them
            // already carries the plan's one overall figure — a brand-new row simply inherits it,
            // rather than silently resetting the owner's typed-in extra back to zero.
            var extra = plans.Count > 0 ? plans[0].ExtraPaymentAmount : 0m;
            var nextOrder = plans.Count;
            foreach (var account in missing)
            {
                var plan = new SnowballPlan { LoanAccountId = account.Id, FocusOrder = nextOrder++, ExtraPaymentAmount = extra };
                db.SnowballPlans.Add(plan);
                plansByLoanId[account.Id] = plan;
            }
            await db.SaveChangesAsync(cancellationToken);
        }

        return eligible
            .OrderBy(a => plansByLoanId[a.Id].FocusOrder)
            .Select(a => (a, plansByLoanId[a.Id]))
            .ToList();
    }

    /// <summary>D43/D123: the long-press action card's three moves, delegated to
    /// <see cref="InstitutionOrder"/>'s pure list arithmetic directly — it already operates on a plain
    /// sequence of <see cref="Guid"/>s with no dependency on <see cref="Institution"/>, so the identical
    /// renumber-the-whole-list rule that governs bank order governs loan focus order with no second,
    /// duplicated implementation.</summary>
    public Task MoveLoanUpAsync(Guid loanAccountId, CancellationToken cancellationToken) =>
        ApplyFocusOrderAsync(loanAccountId, InstitutionOrder.MoveUp, cancellationToken);

    public Task MoveLoanDownAsync(Guid loanAccountId, CancellationToken cancellationToken) =>
        ApplyFocusOrderAsync(loanAccountId, InstitutionOrder.MoveDown, cancellationToken);

    public Task MoveLoanToBottomAsync(Guid loanAccountId, CancellationToken cancellationToken) =>
        ApplyFocusOrderAsync(loanAccountId, InstitutionOrder.MoveToBottom, cancellationToken);

    private async Task ApplyFocusOrderAsync(
        Guid loanAccountId,
        Func<IReadOnlyList<Guid>, Guid, IReadOnlyList<(Guid Id, int SortOrder)>> move,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var plans = await db.SnowballPlans.OrderBy(p => p.FocusOrder).ToListAsync(cancellationToken);
        var newOrder = move(plans.Select(p => p.LoanAccountId).ToList(), loanAccountId);

        var bySortOrder = newOrder.ToDictionary(entry => entry.Id, entry => entry.SortOrder);
        foreach (var plan in plans)
        {
            plan.FocusOrder = bySortOrder[plan.LoanAccountId];
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    // ---- the one overall extra payment (D05/D73) ----

    public async Task<decimal> GetExtraPaymentAmountAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.SnowballPlans.Select(p => p.ExtraPaymentAmount).FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>Writes the plan's ONE overall figure to every loan's row atomically, per
    /// <see cref="SnowballPlan"/>'s own remarks on why the value is kept in sync across the whole
    /// table rather than privileged on any one row.</summary>
    public async Task SetExtraPaymentAmountAsync(decimal amount, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var plans = await db.SnowballPlans.ToListAsync(cancellationToken);
        foreach (var plan in plans)
        {
            plan.ExtraPaymentAmount = amount;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    // ---- the Loans side (mockup 6) ----

    public async Task<SnowballSummary> GetSummaryAsync(CancellationToken cancellationToken)
    {
        var loansInFocusOrder = await GetLoansInFocusOrderAsync(cancellationToken);
        var extra = loansInFocusOrder.Count > 0 ? loansInFocusOrder[0].Plan.ExtraPaymentAmount : 0m;
        return await BuildSummaryAsync(loansInFocusOrder, extra, cancellationToken);
    }

    /// <summary>
    /// The same summary, but computed against <paramref name="liveExtraOverride"/> rather than the
    /// persisted plan — never written to the database. Mockup 6's own note is "every date recalculates
    /// as you type": <c>SnowballPage</c> calls this on every keystroke in the Extra field and only
    /// calls <see cref="SetExtraPaymentAmountAsync"/> (which DOES persist) once the field loses focus,
    /// so a keystroke never triggers a database write.
    /// </summary>
    public async Task<SnowballSummary> GetSummaryWithLiveExtraAsync(decimal liveExtraOverride, CancellationToken cancellationToken)
    {
        var loansInFocusOrder = await GetLoansInFocusOrderAsync(cancellationToken);
        return await BuildSummaryAsync(loansInFocusOrder, liveExtraOverride, cancellationToken);
    }

    private async Task<SnowballSummary> BuildSummaryAsync(
        IReadOnlyList<(Account Account, SnowballPlan Plan)> loansInFocusOrder, decimal extra, CancellationToken cancellationToken)
    {
        // Each loan's post-it is tinted from the bank that holds it (1.3c), so the view carries the
        // institution's colour. A loan has only the institution's id, so the small id → colour map is
        // read here rather than widening GetLoansInFocusOrderAsync's tuple, which a dozen callers destructure.
        Dictionary<Guid, string?> colourByInstitutionId;
        await using (var db = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            colourByInstitutionId = await db.Institutions.AsNoTracking()
                .ToDictionaryAsync(i => i.Id, i => i.PrimaryColorHex, cancellationToken);
        }
        string? ColourOf(Account account) => colourByInstitutionId.GetValueOrDefault(account.InstitutionId);

        var inputs = loansInFocusOrder
            .Select(x => new SnowballLoanInput(x.Account.Name, x.Account.CurrentBalance ?? 0m, x.Account.Apr ?? 0m, x.Account.NextPaymentAmount ?? 0m))
            .ToList();

        // The sheet's own anchor (M1) was the date of its last recorded payment; a live app has no
        // such fixed date to read, so "now" is the anchor every time the page opens — which is exactly
        // what lets every date recalculate live as the extra payment is typed (mockup 6's own note).
        var anchorDate = DateOnly.FromDateTime(clock.UtcNow.LocalDateTime.Date);
        var rows = SnowballEngine.Calculate(inputs, extra, anchorDate);

        var debtFreeDate = rows.Count > 0 ? rows[^1].PayoffDate : anchorDate;
        var totalWeeks = rows.Count > 0 ? rows[^1].WeeksBeforeFocused + rows[^1].WeeksToPayoff : 0;
        var totalOwed = rows.Sum(r => r.Balance);

        // Display order (D12): paid-off loans sit at the end, crossed out. The ENGINE already consumed
        // every loan in its raw FOCUS order above (a paid-off loan contributes 0 weeks wherever it
        // sits, which is what lets the loan actually last in focus order — the mortgage — be the one
        // whose payoff date is the debt-free date) — this only reorders how the cards are DRAWN.
        var paired = loansInFocusOrder.Zip(rows, (loan, row) => (loan.Account, row)).ToList();
        var active = paired.Where(x => !AccountStatus.IsPaidOff(x.Account)).ToList();
        var paidOff = paired.Where(x => AccountStatus.IsPaidOff(x.Account)).ToList();

        var views = new List<SnowballLoanView>(paired.Count);
        for (var i = 0; i < active.Count; i++)
        {
            var (account, row) = active[i];
            var isFocus = i == 0;
            // D137: the card leads with NOW. The currently-focused loan's own weekly figure already
            // includes everything rolled down onto it (E); every other active loan shows just its own
            // weekly payment (D) — the post-it is where the EVENTUAL, once-it's-the-focus numbers live.
            var weeklyPaymentNow = isFocus ? row.FocusedWeeklyPayment : row.WeeklyPayment;
            views.Add(new SnowballLoanView(
                account.Id, account.Name, DisplayNumber: i + 1, isFocus, IsPaidOff: false,
                row.Balance, row.Apr, weeklyPaymentNow,
                row.FocusedWeeklyPayment, row.WeeksBeforeFocused, row.AmountBeforeFocused, row.WeeksToPayoff, row.PayoffDate,
                ColourOf(account)));
        }
        foreach (var (account, row) in paidOff)
        {
            views.Add(new SnowballLoanView(
                account.Id, account.Name, DisplayNumber: null, IsFocus: false, IsPaidOff: true,
                row.Balance, row.Apr, WeeklyPaymentNow: 0m,
                row.FocusedWeeklyPayment, row.WeeksBeforeFocused, row.AmountBeforeFocused, row.WeeksToPayoff, row.PayoffDate,
                ColourOf(account)));
        }

        return new SnowballSummary(views, debtFreeDate, totalWeeks, totalOwed, extra);
    }

    // ---- the one-time history import (D03, D78, Guard G8) ----

    /// <summary>
    /// Brings the owner's recorded weeks (the sheet's "Loans Over Time") onto this install, from the
    /// owner's own encrypted Drive file (<see cref="DriveSnowballHistorySource"/>) — the real figures are
    /// not in the repository or the app.
    ///
    /// <para><b>The gate</b> is "no snapshot dated on or before the history's last recorded week"
    /// (<see cref="SnowballHistoryGate"/>), not "no snapshot at all": the page writes this week's snapshot
    /// right after trying this import, and when the read failed that snapshot must not close the gate for
    /// good. Once an import has succeeded — or the rows are found to be here already — it is remembered
    /// (<see cref="ISnowballHistoryImportMemory"/>) and later opens never ask Drive again.</para>
    ///
    /// <para><b>Never throws into the page.</b> A missing file, no passphrase, a wrong passphrase or a
    /// failed read is logged (a status and a reason — never the payload, never the passphrase), leaves the
    /// flag unset, and is retried on a later open, no more than once per pause
    /// (<see cref="SnowballHistoryBackoff"/>). A fixture loan name that matches no account is skipped and
    /// logged, never guessed at: a fresh or partially-connected database simply gets fewer rows. Her
    /// install never reads the file.</para>
    ///
    /// <para><b>Returns whether rows were written</b>, so a caller that ran this behind an already-painted
    /// page (<see cref="SnowballOpening"/>) knows to redraw. It is not meant to be awaited before the first
    /// paint: Drive can take up to the read cap to answer.</para>
    /// </summary>
    public async Task<bool> ImportHistoryIfNeededAsync(CancellationToken cancellationToken)
    {
        if (historyImport is null || historyImport.Role() == InstallRole.Hers || historyImport.Memory.Imported)
        {
            return false;
        }

        var passphrase = historyImport.Passphrase();
        var now = clock.UtcNow;
        if (!historyImport.Backoff.MayAttempt(now, passphrase))
        {
            return false;
        }

        var read = await ReadHistoryAsync(passphrase, cancellationToken);
        if (read.Status != SnowballHistoryStatus.Loaded)
        {
            logger?.LogWarning("Snowball history import: not imported ({Status}). {Reason}", read.Status, read.Reason);
            // A missing passphrase costs no network, so it is not held back: typing one in and coming back
            // must try at once. Every outcome that touched Drive is.
            if (read.Status != SnowballHistoryStatus.NeedsPassphrase)
            {
                historyImport.Backoff.RecordAttempt(now, passphrase);
            }
            return false;
        }

        var history = read.History!;
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var earliest = await db.SnowballSnapshots
            .OrderBy(s => s.WeekOf)
            .Select(s => (DateOnly?)s.WeekOf)
            .FirstOrDefaultAsync(cancellationToken);
        if (SnowballHistoryGate.IsAlreadyPresent(earliest, history.LastWeek))
        {
            historyImport.Memory.MarkImported();
            return false;
        }

        var institutions = await db.Institutions.Include(i => i.Accounts).ToListAsync(cancellationToken);
        var candidates = institutions
            .SelectMany(institution => institution.Accounts.Select(account =>
                new SnowballHistoryCandidate(account.Id, account.Name, institution.Name)))
            .ToList();

        var matchedAccountIdByLoanName = new Dictionary<string, Guid>();
        for (var position = 0; position < history.Loans.Count; position++)
        {
            var loanName = history.Loans[position];
            var matched = SnowballHistoryMatch.Resolve(loanName, candidates);
            if (matched is null)
            {
                // Logged by position: a loan's name is the owner's finances and does not belong in a log.
                logger?.LogWarning(
                    "Snowball history import: no account matches history loan {LoanNumber} — skipped.", position + 1);
                continue;
            }
            matchedAccountIdByLoanName[loanName] = matched.Value;
        }

        if (matchedAccountIdByLoanName.Count == 0)
        {
            // Nothing on this install matches any history loan yet — degrade quietly and try again later,
            // once the owner's loans are connected.
            historyImport.Backoff.RecordAttempt(now, passphrase);
            return false;
        }

        var added = new HashSet<(Guid, DateOnly)>();
        foreach (var row in history.Rows)
        {
            if (!matchedAccountIdByLoanName.TryGetValue(row.Loan, out var accountId))
            {
                continue; // this row's loan didn't match anything — skipped along with the rest of it
            }
            if (!added.Add((accountId, row.Week)))
            {
                continue; // two history loans resolved to one account: its first series wins
            }

            db.SnowballSnapshots.Add(new SnowballSnapshot
            {
                WeekOf = row.Week,
                AccountId = accountId,
                Balance = row.Balance,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        historyImport.Memory.MarkImported();
        return true;
    }

    private async Task<SnowballHistoryReadResult> ReadHistoryAsync(string? passphrase, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(historyImport!.ReadTimeout);
        try
        {
            return await historyImport.Source.ReadAsync(passphrase, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Not the caller giving up: Drive did not answer in time. The page opens; the next open retries.
            return SnowballHistoryReadResult.Unreadable("Drive did not answer in time.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A source is meant not to throw; if one does, the page still opens. The failure is carried to
            // the log with its type and message — never with anything the source was holding.
            return SnowballHistoryReadResult.Unreadable($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    // ---- the weekly snapshot writer (D78), after the one-time import ----

    /// <summary>Dedupe-at-write (D78): writes the current balance for every Snowball loan, but only for an
    /// (AccountId, WeekOf) pair that doesn't already have a row — so calling this more than once in the
    /// same week is a no-op, never a duplicate. WeekOf is the sheet's week, the Friday on or before the
    /// owner's local date (<see cref="SnowballWeek"/>), NOT today: the page calls this on every open, and
    /// keying on the day wrote a new History "week" for every day the app was used. The first open of a
    /// week fixes that week's balance; later opens that week do not revise it (the sheet's own columns
    /// are likewise one reading each).</summary>
    public async Task RecordCurrentWeekSnapshotAsync(CancellationToken cancellationToken)
    {
        var weekOf = SnowballWeek.Of(DateOnly.FromDateTime(clock.UtcNow.LocalDateTime.Date));

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var loanAccounts = (await db.Accounts
                .Where(a => a.Kind == AccountKind.Credit || a.Kind == AccountKind.Loan)
                .ToListAsync(cancellationToken))
            .Where(IsSnowballLoan)
            .ToList();

        var alreadyRecorded = (await db.SnowballSnapshots
                .Where(s => s.WeekOf == weekOf)
                .Select(s => s.AccountId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var added = false;
        foreach (var account in loanAccounts)
        {
            if (!alreadyRecorded.Add(account.Id))
            {
                continue;
            }
            db.SnowballSnapshots.Add(new SnowballSnapshot { WeekOf = weekOf, AccountId = account.Id, Balance = account.CurrentBalance ?? 0m });
            added = true;
        }

        if (added)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    // ---- the History side (mockup 6) ----

    public async Task<SnowballHistory> GetHistoryAsync(CancellationToken cancellationToken)
    {
        var loansInFocusOrder = await GetLoansInFocusOrderAsync(cancellationToken);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var snapshots = await db.SnowballSnapshots.AsNoTracking().ToListAsync(cancellationToken);

        var weeks = snapshots.Select(s => s.WeekOf).Distinct().OrderBy(w => w).ToList();
        var byAccount = snapshots.ToLookup(s => s.AccountId);

        var series = loansInFocusOrder.Select(x =>
        {
            var balanceByWeek = byAccount[x.Account.Id].ToDictionary(s => s.WeekOf, s => s.Balance);
            var points = weeks.Select(week => new SnowballHistoryPoint(week, balanceByWeek.GetValueOrDefault(week))).ToList();
            var peak = points.Count > 0 ? points.Max(p => p.Balance) : 0m;
            return new SnowballLoanHistory(x.Account.Id, x.Account.Name, points, peak, AccountStatus.IsPaidOff(x.Account),
                AccountStatus.IsMortgage(x.Account));
        }).ToList();

        return new SnowballHistory(weeks, series);
    }
}
