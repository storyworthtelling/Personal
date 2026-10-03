namespace Index.Domain.Model;

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

    // Display name shown in the list header (e.g. "PNC", "Redwood Bank").
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

    // The owner's own order for the Accounts page (D123), not alphabetical and not recomputed: a
    // long-press action card moves a bank up/down/to the bottom, and this is what persists that
    // choice. A brand-new institution defaults to the bottom of whatever order already exists
    // (AccountService.AddInstitutionAsync); the very first institutions ever seen seed alphabetically,
    // since there is no owner choice yet to preserve. Kept as a plain, densely renumbered int rather
    // than a float/fractional-index scheme — Index.Domain.Model.InstitutionOrder renumbers the whole
    // list on every move, so gaps never accumulate and there is nothing to compact later.
    public int SortOrder { get; set; }

    // Plaid's institution `primary_color` (a hex colour string, hash-prefixed), fetched once via
    // IPlaidClient.GetInstitutionAsync and cached here rather than looked up every time a card is
    // opened (D127). Null until Release C's connect flow fetches it, or when the institution has none
    // — either way the post-it falls back to MaterialTokens.PostItHex (Index.Domain.Design.PostItTint).
    public string? PrimaryColorHex { get; set; }

    // Plaid's own id for the bank (e.g. "ins_13"), which /institutions/get_by_id needs. Captured at link
    // time from the Hosted Link session; null for a bank connected before it was captured (or restored
    // from the vault, which carries only the Item's credentials), in which case the colour lookup asks
    // Plaid for it once via /item/get.
    public string? PlaidInstitutionId { get; set; }

    // When the bank's colour was last successfully looked up — set whether or not Plaid had a colour to
    // give. This is what makes the backfill "once": a bank Plaid answered for but has no primary_color
    // is not asked again on every refresh. A failed lookup (rate limit, network) leaves it null so the
    // next refresh retries. Null for a hand-entered bank, which has no Plaid institution to ask about.
    public DateTimeOffset? ColorLookedUpAtUtc { get; set; }

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

    // Latest balance snapshot (overwritten each refresh). Amounts are `decimal`, not `double`: P11
    // (Index) is the first phase that does arithmetic with them — a snowball total across nine
    // loans, or a budget that must balance to exactly $0.00 — and 0.1 + 0.2 is not 0.3 in binary
    // floating point. This matches the convention Post's `IndexHandoff`/`MoneyMail` already set for
    // the same reason.
    public decimal? CurrentBalance { get; set; }
    public decimal? AvailableBalance { get; set; }
    public decimal? CreditLimit { get; set; }
    public DateTimeOffset? BalanceAsOfUtc { get; set; }

    // Where CurrentBalance came from (mockup 15's "From Post, Sep 12"). NULL means "not recorded", which is
    // every row stored before this column existed: it reads as ByHand for a Manual account and as Plaid for
    // a connected one (BalanceDisplay.SourceOf), so no existing row needed a data migration to be right.
    // Written by AccountService (a Post receipt -> Post; a typed balance -> ByHand) and by the refresh
    // processor (a Plaid figure -> Plaid). For a Post balance BalanceAsOfUtc is a DATE stored at UTC
    // midnight, like a due date (CalendarDate); for the other two it is an INSTANT.
    public BalanceSource? BalanceSource { get; set; }

    // Liabilities detail (credit cards + mortgages via Plaid's Liabilities product). Null when the
    // account is a plain deposit account or the institution doesn't support Liabilities.
    public decimal? LastStatementBalance { get; set; }
    public DateTimeOffset? NextPaymentDueDate { get; set; }
    public decimal? NextPaymentAmount { get; set; }
    public decimal? Apr { get; set; }

    // D39: which of Index's three data paths produced this account. Defaults to Plaid because every
    // account before Release C came from the connect flow; AccountService.AddManualAccountAsync is the
    // only place that sets Manual.
    public AccountSource Source { get; set; } = AccountSource.Plaid;

    // D130: a hand-entered account's payment cadence ("Every"). Null for a Plaid account (Plaid's own
    // Liabilities data carries no cadence) and optional for a manual one, same as Apr/NextPaymentAmount
    // (D108) — an account without a cadence still shows a balance, it just isn't in the Snowball yet.
    public PaymentCadence? PaymentCadence { get; set; }

    // D56/D159: the owner's override of whether a CONNECTED account counts as Savings. Null means "use
    // Plaid's own Subtype" (the default); set means the owner has flipped the post-it's switch either
    // way. Only meaningful when Source == Plaid — a hand-entered account has no Subtype to default
    // from, so its Savings tab is decided some other way (Release G).
    public bool? IsSavingsOverride { get; set; }
}
