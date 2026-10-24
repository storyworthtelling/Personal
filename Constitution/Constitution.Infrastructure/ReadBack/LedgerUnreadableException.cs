namespace Constitution.Infrastructure.ReadBack;

/// <summary>
/// The skip ledger's file exists but cannot be read in full: as a ledger at all, or with entries this version does not
/// know. It is left exactly as it is: writing the next settled day over it would replace whatever the damage left, or
/// whatever the unknown entries held, with a file that no longer has them.
/// </summary>
public sealed class LedgerUnreadableException() : InvalidOperationException(
    "The skip ledger could not be read in full, so it was left untouched and yesterday's page was kept until it can be.");
