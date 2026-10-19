namespace Constitution.Infrastructure.ReadBack;

/// <summary>
/// The skip ledger's file exists but cannot be read as a ledger. It is left exactly as it is: writing the next
/// settled day over it would replace whatever the damage left with a nearly empty file.
/// </summary>
public sealed class LedgerUnreadableException() : InvalidOperationException(
    "The skip ledger could not be read, so it was left untouched and yesterday's page was kept until it can be.");
