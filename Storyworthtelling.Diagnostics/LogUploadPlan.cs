using System.Globalization;

namespace Storyworthtelling.Diagnostics;

/// <summary>Whether the phone holds what it needs to seal a batch.</summary>
public enum LogKeyState
{
    /// <summary>No vault passphrase: the post-reset state. Nothing is uploaded and nothing is cleared.</summary>
    Missing,
    Ready,
}

/// <summary>Why a run did nothing at all.</summary>
public enum LogUploadRefusal
{
    WaitingForPassphrase,
}

/// <summary>One Drive file to be: its name and exactly the segments it carries.</summary>
public sealed record PlannedBatch(string Name, IReadOnlyList<SealedSegment> Segments);

/// <summary>A run's batches, oldest first, or the reason there are none.</summary>
public sealed record LogUploadRun(IReadOnlyList<PlannedBatch> Batches, LogUploadRefusal? Refusal);

/// <summary>A file already in the folder under this batch's name or one of its revisions.</summary>
/// <param name="Sha256">The SHA-256 of its decrypted content, or null when it could not be read.</param>
public sealed record RemoteCopy(string Name, string? Sha256);

public enum BatchActionKind
{
    /// <summary>Create the file under <see cref="BatchAction.Name"/>, then read it back.</summary>
    Create,

    /// <summary>A copy with this exact content is already there (a crash between upload and delete).</summary>
    AlreadyUploaded,
}

public sealed record BatchAction(BatchActionKind Kind, string Name)
{
    /// <summary>An existing copy was already read back and matched; it needs no second read.</summary>
    public bool ClearsWithoutReadBack => Kind == BatchActionKind.AlreadyUploaded;
}

/// <summary>How one batch ended: confirmed by a matching read-back (or an existing match), or not.</summary>
public sealed record BatchOutcome(PlannedBatch Batch, bool Confirmed);

/// <summary>
/// The upload's decisions (LOG-LOOP-PLAN.md §3.3), pure so that "when is it safe to delete a log" exists
/// exactly once. The worker carries them out:
/// <list type="number">
/// <item>No key: stop. Upload nothing, clear nothing; never plaintext, never an empty key.</item>
/// <item>Name each batch <c>swt-log.&lt;app&gt;.&lt;installId&gt;.&lt;first&gt;-&lt;last&gt;.json</c> in
/// <see cref="FolderName"/>.</item>
/// <item>If that name already holds the same content, skip to clearing. If it holds something else, write
/// <c>….r2.json</c> (then r3, …). Never overwrite.</item>
/// <item>Create, read back by id, decrypt, compare SHA-256.</item>
/// <item>Clear exactly that batch's segments, and only after the comparison matched.</item>
/// </list>
/// At most <see cref="MaxBatchesPerRun"/> batches a run, so a phone back from a long trip drains over a
/// few days rather than an hour on the radio.
/// </summary>
public static class LogUploadPlan
{
    public const string FolderName = "storyworthtelling-logs";
    public const string NamePrefix = "swt-log.";
    public const int MaxBatchesPerRun = 40;

    public static LogUploadRun Decide(IReadOnlyList<SealedSegment> segments, LogKeyState key, string app, string installId)
    {
        if (key == LogKeyState.Missing)
        {
            return new LogUploadRun([], LogUploadRefusal.WaitingForPassphrase);
        }

        var batches = new List<PlannedBatch>();
        var current = new List<SealedSegment>();
        long size = 0;
        foreach (var segment in segments.OrderBy(s => s.Sequence))
        {
            if (current.Count > 0 && size + segment.Bytes > LogBatch.MaxPlaintextBytes)
            {
                batches.Add(Planned(app, installId, current));
                if (batches.Count == MaxBatchesPerRun)
                {
                    return new LogUploadRun(batches, null);
                }
                current = [];
                size = 0;
            }
            current.Add(segment);
            size += segment.Bytes;
        }
        if (current.Count > 0 && batches.Count < MaxBatchesPerRun)
        {
            batches.Add(Planned(app, installId, current));
        }
        return new LogUploadRun(batches, null);
    }

    public static string NameFor(string app, string installId, IReadOnlyList<SealedSegment> segments) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{NamePrefix}{app}.{installId}.{segments.Min(s => s.Sequence):000000}-{segments.Max(s => s.Sequence):000000}.json");

    /// <summary><c>….json</c> becomes <c>….r&lt;n&gt;.json</c>.</summary>
    public static string RevisionName(string baseName, int revision) =>
        string.Create(CultureInfo.InvariantCulture, $"{baseName[..^".json".Length]}.r{revision}.json");

    /// <summary>
    /// What to do with one batch, given every copy already in the folder under its name or a revision of
    /// it. A copy with the same content means it is already there; otherwise the first free name is
    /// created, so nothing in Drive is ever overwritten.
    /// </summary>
    public static BatchAction ForBatch(string baseName, string localSha256, IReadOnlyList<RemoteCopy> existing)
    {
        var match = existing.FirstOrDefault(c => c.Sha256 is not null
            && string.Equals(c.Sha256, localSha256, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            return new BatchAction(BatchActionKind.AlreadyUploaded, match.Name);
        }

        var taken = existing.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        if (!taken.Contains(baseName))
        {
            return new BatchAction(BatchActionKind.Create, baseName);
        }
        var revision = 2;
        while (taken.Contains(RevisionName(baseName, revision)))
        {
            revision++;
        }
        return new BatchAction(BatchActionKind.Create, RevisionName(baseName, revision));
    }

    /// <summary>A read-back confirms the upload only when it decrypted and hashed to the same content.</summary>
    public static bool Confirmed(string localSha256, string? readBackSha256) =>
        readBackSha256 is not null && string.Equals(localSha256, readBackSha256, StringComparison.OrdinalIgnoreCase);

    /// <summary>The segments that may be deleted: those of confirmed batches, and no others.</summary>
    public static IReadOnlyList<SealedSegment> ToClear(IEnumerable<BatchOutcome> outcomes) =>
        outcomes.Where(o => o.Confirmed).SelectMany(o => o.Batch.Segments).OrderBy(s => s.Sequence).ToList();

    private static PlannedBatch Planned(string app, string installId, List<SealedSegment> segments) =>
        new(NameFor(app, installId, segments), segments.ToList());
}

/// <summary>The one status sentence the Diagnostics screen shows about uploads, or none.</summary>
public static class LogUploadStatus
{
    /// <summary>Failures stay quiet until this many weeks in a row.</summary>
    public const int FailedWeeksBeforeSaying = 3;

    public static string? Sentence(LogUploadRefusal? refusal, int consecutiveFailedWeeks)
    {
        if (refusal == LogUploadRefusal.WaitingForPassphrase)
        {
            return "Logs are waiting for the vault passphrase.";
        }
        return consecutiveFailedWeeks >= FailedWeeksBeforeSaying
            ? string.Create(CultureInfo.InvariantCulture, $"Logs have not reached Drive for {consecutiveFailedWeeks} weeks.")
            : null;
    }
}
