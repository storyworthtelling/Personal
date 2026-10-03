using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Index.Domain.External;
using Index.Domain.Model;
using Index.Domain.Pipeline;
using Index.Domain.Time;
using Index.Infrastructure.Configuration;
using Index.Infrastructure.Persistence;
using Index.Infrastructure.Services;

namespace Index.Infrastructure.Pipeline;

// Advances the refresh pipeline by one institution per call, idempotently and resumably. State lives
// entirely in the database, so the worker resumes after pauses, rate limits, or restarts simply by
// querying the next due institution. A single worker calls ProcessNextAsync in a loop.
public sealed class BalanceRefreshProcessor : IPipelineStep
{
    private readonly IDbContextFactory<LedgerDbContext> dbContextFactory;
    private readonly IPlaidClient plaid;
    private readonly IClock clock;
    private readonly ILogger<BalanceRefreshProcessor> logger;
    private readonly RefreshOptions options;
    private readonly BackoffScheduler backoff;

    public BalanceRefreshProcessor(
        IDbContextFactory<LedgerDbContext> dbContextFactory,
        IPlaidClient plaid,
        IClock clock,
        IOptions<RefreshOptions> refreshOptions,
        ILogger<BalanceRefreshProcessor> logger)
    {
        this.dbContextFactory = dbContextFactory;
        this.plaid = plaid;
        this.clock = clock;
        this.logger = logger;
        options = refreshOptions.Value;
        backoff = new BackoffScheduler(
            TimeSpan.FromSeconds(options.BaseDelaySeconds), TimeSpan.FromSeconds(options.MaxDelaySeconds));
    }

    public async Task<ProcessResult> ProcessNextAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Refresh the oldest institution that is actually due. A not-yet-due institution must not hog the
        // worker; it is recorded as the soonest Waiting so the cycle can defer instead of spinning.
        // Failed institutions are skipped until the user reconnects. This mirrors the resumable dispatch
        // in the repo's other on-device pipelines.
        // A hand-entered institution (Release C, empty AccessToken — AccountService.AddManualAccountAsync's
        // own convention for "no Plaid Item behind this") is never selected here: calling Plaid with an
        // empty access token would fail every cycle and eventually flip it to Failed, which would then
        // wrongly raise AccountNotices' "needs reconnecting" for an account that was never connected.
        var institutions = await db.Institutions
            .Where(i => i.RefreshStatus != WorkStatus.Failed && i.AccessToken != string.Empty)
            .OrderBy(i => i.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        ProcessResult? soonestWait = null;
        foreach (var institution in institutions)
        {
            var now = clock.UtcNow;
            if (institution.NextAttemptAtUtc is { } next && next > now)
            {
                var wait = next - now;
                if (soonestWait is null || wait < soonestWait.Value.Wait)
                    soonestWait = ProcessResult.Waiting(wait, institution.Id);
                continue;
            }

            await RefreshAsync(db, institution, cancellationToken);
            return ProcessResult.DidWork(institution.Id);
        }

        return soonestWait ?? ProcessResult.Idle;
    }

    private async Task RefreshAsync(LedgerDbContext db, Institution institution, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var accounts = await db.Accounts
            .Where(a => a.InstitutionId == institution.Id)
            .ToListAsync(cancellationToken);

        var refreshed = false;
        try
        {
            var balances = await plaid.GetBalancesAsync(institution.AccessToken, cancellationToken);
            foreach (var incoming in balances)
                UpsertBalance(db, institution.Id, accounts, incoming, now);

            // Liabilities detail only exists for cards and loans; fetch it only when the Item has one.
            if (balances.Any(b => MapKind(b.Type) is AccountKind.Credit or AccountKind.Loan))
                await MergeLiabilitiesAsync(institution.AccessToken, accounts, cancellationToken);

            institution.RefreshStatus = WorkStatus.Completed;
            institution.Attempts = 0;
            institution.LastError = null;
            institution.LastRefreshedUtc = now;
            institution.NextAttemptAtUtc = now.AddHours(options.RefreshIntervalHours);
            logger.LogInformation("Refreshed {Name}: {Count} accounts.", institution.Name, balances.Count);
            refreshed = true;
        }
        catch (RateLimitException ex)
        {
            // Transient throttle: reschedule without counting toward the terminal-failure ceiling.
            institution.RefreshStatus = WorkStatus.NeedsRetry;
            institution.LastError = ex.Message;
            institution.NextAttemptAtUtc = now + (ex.RetryAfter ?? backoff.NextDelay(institution.Attempts));
            logger.LogWarning("Rate limited refreshing {Name}; retrying later.", institution.Name);
        }
        catch (Exception ex)
        {
            institution.Attempts++;
            institution.LastError = ex.Message;
            if (institution.Attempts >= options.MaxAttempts)
            {
                institution.RefreshStatus = WorkStatus.Failed;
                institution.NextAttemptAtUtc = null;
                logger.LogError(ex, "Refresh of {Name} failed permanently after {Attempts} attempts.",
                    institution.Name, institution.Attempts);
            }
            else
            {
                institution.RefreshStatus = WorkStatus.NeedsRetry;
                institution.NextAttemptAtUtc = now + backoff.NextDelay(institution.Attempts - 1);
                logger.LogWarning(ex, "Refresh of {Name} failed (attempt {Attempts}); will retry.",
                    institution.Name, institution.Attempts);
            }
        }

        // The once-only colour backfill (1.3 / R-G8). Deliberately outside the try above: a lookup that
        // is throttled or fails must neither be mistaken for a failed refresh nor reschedule it. It runs
        // only after a refresh that worked, so a bank Plaid is already refusing is not also asked about
        // its colour, and it never throws (InstitutionColorLookup logs and returns).
        if (refreshed)
            await InstitutionColorLookup.ResolveAsync(institution, plaid, now, logger, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task MergeLiabilitiesAsync(
        string accessToken, List<Account> accounts, CancellationToken cancellationToken)
    {
        IReadOnlyList<PlaidLiability> liabilities;
        try
        {
            liabilities = await plaid.GetLiabilitiesAsync(accessToken, cancellationToken);
        }
        catch (RateLimitException)
        {
            throw; // let the caller reschedule the whole refresh
        }
        catch (Exception ex)
        {
            // The institution reports card/loan accounts but doesn't support the Liabilities product
            // (or none are ready yet). Balances already succeeded, so keep them and skip the detail.
            logger.LogInformation("Liabilities unavailable for this institution: {Message}", ex.Message);
            return;
        }

        foreach (var liability in liabilities)
        {
            var account = accounts.FirstOrDefault(a => a.AccountId == liability.AccountId);
            if (account is null)
                continue;
            account.LastStatementBalance = liability.LastStatementBalance;
            account.NextPaymentDueDate = liability.NextPaymentDueDate;
            account.NextPaymentAmount = liability.NextPaymentAmount;
            account.Apr = liability.Apr;
        }
    }

    private static void UpsertBalance(
        LedgerDbContext db, Guid institutionId, List<Account> accounts, PlaidAccount incoming, DateTimeOffset now)
    {
        var account = accounts.FirstOrDefault(a => a.AccountId == incoming.AccountId);
        if (account is null)
        {
            account = new Account { InstitutionId = institutionId, AccountId = incoming.AccountId };
            db.Accounts.Add(account);
            accounts.Add(account);
        }

        account.Name = incoming.Name;
        account.OfficialName = incoming.OfficialName;
        account.Mask = incoming.Mask;
        account.Kind = MapKind(incoming.Type);
        account.Subtype = incoming.Subtype;
        account.IsoCurrencyCode = incoming.IsoCurrencyCode;
        account.CurrentBalance = incoming.Current;
        account.AvailableBalance = incoming.Available;
        account.CreditLimit = incoming.Limit;
        account.BalanceAsOfUtc = now;
        account.BalanceSource = BalanceSource.Plaid; // Plaid's own figure again, e.g. after a reconnect that Post covered for

        // Clear any prior liabilities detail; it is re-applied below only if still reported.
        account.LastStatementBalance = null;
        account.NextPaymentDueDate = null;
        account.NextPaymentAmount = null;
        account.Apr = null;
    }

    private static AccountKind MapKind(string plaidType) => plaidType switch
    {
        "depository" => AccountKind.Depository,
        "credit" => AccountKind.Credit,
        "loan" => AccountKind.Loan,
        "investment" => AccountKind.Investment,
        _ => AccountKind.Other
    };
}
