namespace Foreword.Domain.Diagnostics;

/// <summary>
/// Foreword's key actions (LOG-LOOP-PLAN.md §4.1, O10): the taps that start background work, declared to the tap
/// watch so they are never judged dead and their failure or silence is logged. Wire names in the logs, listed by
/// <c>MauiTapWatchInstalledTests</c>, so a new one is a deliberate edit.
/// </summary>
public static class ForewordKeyActions
{
    /// <summary>Deliver now.</summary>
    public const string DeliverNow = "foreword.deliver-now";

    /// <summary>Pairing the reMarkable.</summary>
    public const string PairRemarkable = "foreword.pair-remarkable";

    /// <summary>Diagnostics: Send logs now.</summary>
    public const string SendLogsNow = "foreword.send-logs-now";

    public static IReadOnlyList<string> All { get; } = [DeliverNow, PairRemarkable, SendLogsNow];
}
