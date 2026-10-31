using Constitution.Domain.Diagnostics;

namespace Constitution.Domain.Body;

/// <summary>What started a sync.</summary>
public enum BodySyncTrigger
{
    /// <summary>The Body sync worker, overnight.</summary>
    Worker,

    /// <summary>The app opening.</summary>
    AppOpen,
}

/// <summary>What was missing from a sync that Diagnostics should be able to say (R5). A kind, not a value.</summary>
public enum BodySyncGap
{
    /// <summary>Nothing was missing.</summary>
    None,

    /// <summary>Background reads are not granted, so the morning values are only as fresh as the last time the app opened.</summary>
    BackgroundNotGranted,
}

/// <summary>What one sync did.</summary>
public sealed record BodySyncResult(BodySyncTrigger Trigger, bool Ran, BodySyncGap Gap, SignalReadReport? Report)
{
    /// <summary>
    /// How Diagnostics reports it, in the existing closed outcomes: nothing granted yet is "never ran"; every
    /// signal refused is "refused"; every signal failing is "unreachable"; a mix, or a pass that could only
    /// run because the app was open, is "partial"; otherwise it finished.
    /// </summary>
    public DiagnosticsOutcome Outcome
    {
        get
        {
            if (!Ran || Report is null)
            {
                return DiagnosticsOutcome.NeverRan;
            }

            var read = Report.Count(SignalReadStatus.Read);
            var refused = Report.Count(SignalReadStatus.Denied) + Report.Count(SignalReadStatus.Unsupported);
            var failed = Report.Count(SignalReadStatus.Failed);

            if (read == 0)
            {
                if (refused > 0)
                {
                    return DiagnosticsOutcome.Refused;
                }

                return failed > 0 ? DiagnosticsOutcome.Unreachable : DiagnosticsOutcome.NeverRan;
            }

            return refused + failed > 0 || Gap != BodySyncGap.None ? DiagnosticsOutcome.Partial : DiagnosticsOutcome.Succeeded;
        }
    }
}

/// <summary>Where the last sync is remembered, for Diagnostics to read.</summary>
public interface IBodySyncStatus
{
    /// <summary>Remembers a sync that ran.</summary>
    void Record(BodySyncResult result);
}

/// <summary>
/// Decides when Body reads (C-7.1.3). With <c>READ_HEALTH_DATA_IN_BACKGROUND</c> the worker reads overnight so
/// the morning recovery check is current; without it the worker reads nothing (the platform would refuse),
/// and the values are refreshed each time the app opens instead, with the gap recorded (R5).
/// </summary>
public sealed class BodySync
{
    private readonly SignalReader reader;
    private readonly IHealthRecordSource source;
    private readonly IBodySyncStatus status;
    private readonly IBodyLog log;

    public BodySync(SignalReader reader, IHealthRecordSource source, IBodySyncStatus status, IBodyLog log)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(log);
        this.reader = reader;
        this.source = source;
        this.status = status;
        this.log = log;
    }

    /// <summary>
    /// The worker's turn. Without background permission it reads nothing and records nothing (the platform would
    /// refuse, and a skipped pass must not overwrite what the app learned the last time it opened); the gap is
    /// logged and returned.
    /// </summary>
    public async Task<BodySyncResult> RunInBackgroundAsync(CancellationToken cancellationToken)
    {
        if (!source.BackgroundReadsGranted)
        {
            log.Outcome("body sync worker skipped: background read not granted");
            return new BodySyncResult(BodySyncTrigger.Worker, false, BodySyncGap.BackgroundNotGranted, null);
        }

        return await RunAsync(BodySyncTrigger.Worker, BodySyncGap.None, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The app opening: reads whatever is granted, always, so values are current when the owner looks. Where
    /// background reads are not granted, that is the only refresh there is, and the gap is recorded.
    /// </summary>
    public Task<BodySyncResult> OpenTimeReadsAsync(CancellationToken cancellationToken) =>
        RunAsync(
            BodySyncTrigger.AppOpen,
            source.BackgroundReadsGranted ? BodySyncGap.None : BodySyncGap.BackgroundNotGranted,
            cancellationToken);

    private async Task<BodySyncResult> RunAsync(BodySyncTrigger trigger, BodySyncGap gap, CancellationToken cancellationToken)
    {
        var report = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        var result = new BodySyncResult(trigger, true, gap, report);
        status.Record(result);
        log.Outcome($"body sync {(trigger == BodySyncTrigger.Worker ? "worker" : "on open")}: {result.Outcome.ToString().ToLowerInvariant()}"
            + (gap == BodySyncGap.BackgroundNotGranted ? ", gap: background read not granted" : string.Empty));
        return result;
    }
}
