namespace Codex.Domain.Music;

/// <summary>
/// What an incremental library scan found: how many tracks were added, changed, or
/// removed. Pure and unit-tested. <see cref="Describe"/> turns it into a plain, factual
/// sentence — the <c>context</c> handed to the owner's-voice composer
/// (<c>VoicedMessenger</c>/<c>VoiceGreeting</c>), never the greeting text itself, which
/// stays whatever the model actually writes (or nothing, per that stack's degrade-quietly
/// rule).
/// </summary>
public readonly record struct RescanOutcome(int Added, int Changed, int Removed)
{
    public static readonly RescanOutcome None = new(0, 0, 0);

    public bool IsEmpty => Added == 0 && Changed == 0 && Removed == 0;

    /// <summary>Folds another run's findings into this one. A large backlog can need several
    /// chained WorkManager executions to fully drain (see <c>MusicScanner</c>); the caller
    /// combines each run's contribution and only shows the total once the scan is fully
    /// drained.</summary>
    public RescanOutcome Combine(RescanOutcome other) =>
        new(Added + other.Added, Changed + other.Changed, Removed + other.Removed);

    public string Describe()
    {
        if (IsEmpty)
        {
            return "Last night's library scan found nothing new — no tracks added, changed, or removed.";
        }

        var parts = new List<string>();
        if (Added > 0)
        {
            parts.Add($"{Added} new track{(Added == 1 ? "" : "s")}");
        }

        if (Changed > 0)
        {
            parts.Add($"{Changed} changed track{(Changed == 1 ? "" : "s")}");
        }

        if (Removed > 0)
        {
            parts.Add($"{Removed} removed track{(Removed == 1 ? "" : "s")}");
        }

        return $"Last night's library scan found {string.Join(", ", parts)}.";
    }
}

/// <summary>One WorkManager execution's contribution to an incremental scan: whether it
/// drained everything <c>MusicScanPlan</c> called for, and what it actually changed while
/// doing so. A single logical scan can span more than one of these when a large backlog
/// needs several chained runs; the caller (<c>RescanOutcomeStore</c>) combines them with
/// <see cref="RescanOutcome.Combine"/> and only publishes the total once
/// <see cref="FullyDrained"/> is true, so a still-in-flight scan never reads as the day's
/// finding.</summary>
public readonly record struct MusicScanRunResult(bool FullyDrained, RescanOutcome Outcome);
