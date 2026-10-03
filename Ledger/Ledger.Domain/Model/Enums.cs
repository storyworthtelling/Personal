namespace Index.Domain.Model;

// The broad category of a linked account, mapped from Plaid's account `type`. Drives how a row is
// shown (a balance you have vs. a balance you owe) and whether liabilities detail is fetched.
public enum AccountKind
{
    Depository,   // checking / savings — a positive balance you hold
    Credit,       // credit cards — a balance you owe, with a limit and (via Liabilities) a due date
    Loan,         // mortgages / student / other loans — a principal you owe
    Investment,
    Other
}

// The refresh state of one linked institution (the unit of background work). Resumability is built
// on these: the worker resumes purely from the database by querying the next due institution.
public enum WorkStatus
{
    Pending,      // never refreshed yet
    InProgress,
    Completed,    // last refresh succeeded; due again after the refresh interval
    NeedsRetry,   // transient failure (e.g. rate-limited); retried after NextAttemptAtUtc
    Failed        // terminal after MaxAttempts; needs the user to reconnect or intervene
}

// D39: which of Index's three data paths produced an account. One Account entity for all three
// (not a separate ManualAccount table) — Snowball, grouping and the burndown all read one type
// regardless of where a balance comes from. BalanceReceiver is reserved for a receipt that could not
// be matched to any existing Manual or Plaid account (Release C's BalanceReceiver only ever updates an
// existing Manual account or a stale Plaid one — see BalanceReceiverMatch — so nothing constructs this
// value yet; it is named here because D39 names it, not because this release creates one).
public enum AccountSource
{
    Plaid,
    Manual,
    BalanceReceiver
}

// D130: how often a hand-entered account is paid ("Every" on mockup 4/15) — week or month, matching
// the Snowball sheet's own weekly cadence. Null on the Account itself means "not set yet" (D108: only
// name and balance are required at creation).
public enum PaymentCadence
{
    Week,
    Month
}

// Remediation 3.3 (mockup 15): where an account's CURRENT balance came from, so its post-it can say "From
// Post, Sep 12" instead of "Updated Sep 12", and so a date-only statement day is not read as an instant.
// Plaid: a refresh brought it. ByHand: the owner typed it. Post: a statement Post read and handed over
// (D15, via AccountService.ApplyBalanceReceiptAsync).
public enum BalanceSource
{
    Plaid,
    ByHand,
    Post
}
