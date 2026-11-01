namespace Folio.Domain.Diagnostics;

/// <summary>
/// Folio's key actions (LOG-LOOP-PLAN.md §4.1, O10): the taps that start background work, declared to the tap
/// watch so they are never judged dead and their failure or silence is logged. Wire names in the logs, listed by
/// <c>MauiTapWatchInstalledTests</c>, so a new one is a deliberate edit.
/// </summary>
public static class FolioKeyActions
{
    /// <summary>Adding a manuscript.</summary>
    public const string AddManuscript = "folio.add-manuscript";

    /// <summary>Retry now on a stopped book.</summary>
    public const string Retry = "folio.retry";

    /// <summary>Opening a finished report.</summary>
    public const string OpenReport = "folio.open-report";

    /// <summary>Diagnostics: Send logs now.</summary>
    public const string SendLogsNow = "folio.send-logs-now";

    public static IReadOnlyList<string> All { get; } = [AddManuscript, Retry, OpenReport, SendLogsNow];
}
