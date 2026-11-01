namespace Index.Domain.Diagnostics;

/// <summary>
/// Index's key actions (LOG-LOOP-PLAN.md §4.1, O10): the taps that start background work, declared to the tap
/// watch so they are never judged dead and their failure or silence is logged. Wire names in the logs, listed by
/// <c>MauiTapWatchInstalledTests</c>, so a new one is a deliberate edit.
/// </summary>
public static class IndexKeyActions
{
    /// <summary>Connecting a bank through Plaid Hosted Link.</summary>
    public const string ConnectBank = "index.connect-bank";

    /// <summary>Diagnostics: Send logs now.</summary>
    public const string SendLogsNow = "index.send-logs-now";

    public static IReadOnlyList<string> All { get; } = [ConnectBank, SendLogsNow];
}
