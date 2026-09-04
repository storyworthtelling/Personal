namespace Ledger.Domain.Model;

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
