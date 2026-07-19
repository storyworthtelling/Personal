namespace Tally.Domain.Model;

// A connected financial institution (one Plaid "Item" = one login). This is the unit of background
// work: the refresh worker advances the oldest due institution each cycle, re-fetching its balances.
// All state lives in the database so a refresh resumes across wakeups and reboots.
public sealed class Institution
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Plaid identifiers. The access token is the long-lived credential for calling Plaid on this
    // Item's behalf; it lives in the app-private database (same protection as MAUI Preferences).
    public string ItemId { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;

    // Display name shown in the list header (e.g. "PNC", "Chase").
    public string Name { get; set; } = string.Empty;

    public WorkStatus RefreshStatus { get; set; } = WorkStatus.Pending;

    // Consecutive failed refresh attempts; reset to zero on success. Drives backoff and the move to
    // Failed after MaxAttempts.
    public int Attempts { get; set; }

    // The earliest the worker should refresh this institution again. On success it is set to the next
    // daily slot; on a transient failure, to the backoff time. Null means "due now".
    public DateTimeOffset? NextAttemptAtUtc { get; set; }

    public DateTimeOffset? LastRefreshedUtc { get; set; }

    // A human-readable reason the last refresh failed, surfaced on the account list. Null when healthy.
    public string? LastError { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public List<Account> Accounts { get; set; } = [];
}

// One account within an institution (a checking account, a credit card, a loan). Holds the latest
// balance snapshot, refreshed in place each cycle, plus optional liabilities detail for cards/loans.
public sealed class Account
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InstitutionId { get; set; }

    // Plaid's stable identifier for this account, used to match refreshed data to the stored row.
    public string AccountId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string? OfficialName { get; set; }

    // The last few digits Plaid exposes (e.g. "1234"), shown as "••1234".
    public string? Mask { get; set; }

    public AccountKind Kind { get; set; }
    public string? Subtype { get; set; }
    public string? IsoCurrencyCode { get; set; }

    // Latest balance snapshot (overwritten each refresh). Amounts are provider-reported doubles; a
    // balance checker only displays them, so no high-precision arithmetic is performed on them.
    public double? CurrentBalance { get; set; }
    public double? AvailableBalance { get; set; }
    public double? CreditLimit { get; set; }
    public DateTimeOffset? BalanceAsOfUtc { get; set; }

    // Liabilities detail (credit cards + mortgages via Plaid's Liabilities product). Null when the
    // account is a plain deposit account or the institution doesn't support Liabilities.
    public double? LastStatementBalance { get; set; }
    public DateTimeOffset? NextPaymentDueDate { get; set; }
    public double? NextPaymentAmount { get; set; }
    public double? Apr { get; set; }
}
