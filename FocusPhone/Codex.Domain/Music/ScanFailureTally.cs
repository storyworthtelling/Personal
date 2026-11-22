namespace Codex.Domain.Music;

/// <summary>
/// Counts a library scan's unreadable files so the log says what went wrong without a line per file, and
/// says when to stop asking Drive at all. The first <see cref="DetailLimit"/> failures each get one short
/// line (which file of how many, the HTTP status or the failure's kind, how many attempts — never a name
/// or a Drive id); everything after is only counted, and <see cref="Summary"/> gives the totals once.
///
/// The breaker trips when a whole batch of at least <see cref="SameStatusBatchMinimum"/> files failed with
/// the same HTTP status (Drive refusing, not the files being bad), or after <see cref="StraightLimit"/>
/// failures in a row of any kind. A tripped scan stops and reports itself not drained, so the worker tries
/// again later instead of spending its whole budget on reads that fail in milliseconds.
/// </summary>
public sealed class ScanFailureTally
{
    public const int DetailLimit = 3;
    public const int StraightLimit = 16;
    public const int SameStatusBatchMinimum = 4;

    private readonly int total;
    private readonly Dictionary<string, int> kinds = new(StringComparer.Ordinal);
    private int failures;
    private int attempted;
    private int straight;
    private int batchFailures;
    private int batchSuccesses;
    private int? batchStatus;
    private bool batchMixed;

    public ScanFailureTally(int total)
    {
        this.total = total;
    }

    public bool Tripped { get; private set; }

    /// <summary>Why the breaker tripped, in a few words; null while it has not.</summary>
    public string? TripReason { get; private set; }

    public int Failures => failures;

    public void Succeeded()
    {
        attempted++;
        straight = 0;
        batchSuccesses++;
    }

    /// <param name="fileNumber">Which of the run's files this was, counting from one.</param>
    /// <param name="status">The HTTP status Drive refused the read with, when there was one.</param>
    /// <param name="kind">The failure's type name, used when there is no status.</param>
    /// <returns>The line to log for this failure, or null once the first few have been logged.</returns>
    public string? Failed(int fileNumber, int? status, string kind, int attempts)
    {
        attempted++;
        failures++;
        straight++;

        var label = Label(status, kind);
        kinds[label] = kinds.GetValueOrDefault(label) + 1;

        if (batchFailures == 0)
        {
            batchStatus = status;
        }
        else if (batchStatus != status)
        {
            batchMixed = true;
        }

        batchFailures++;

        if (straight >= StraightLimit && !Tripped)
        {
            Trip($"{StraightLimit} failures in a row");
        }

        return failures <= DetailLimit
            ? $"SCAN couldn’t read file {fileNumber} of {total}: {label} after {attempts} attempts"
            : null;
    }

    /// <summary>Closes a batch of concurrent reads, tripping the breaker if all of it failed one way.</summary>
    public void EndBatch()
    {
        if (!Tripped && batchSuccesses == 0 && batchFailures >= SameStatusBatchMinimum
            && !batchMixed && batchStatus is int status)
        {
            Trip($"a whole batch refused with HTTP {status}");
        }

        batchFailures = 0;
        batchSuccesses = 0;
        batchStatus = null;
        batchMixed = false;
    }

    /// <returns>The run's one summary line, or null when nothing failed.</returns>
    public string? Summary()
    {
        if (failures == 0)
        {
            return null;
        }

        var counts = string.Join(", ", kinds
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}×{pair.Value}"));
        var line = $"SCAN read failures {failures} of {attempted}; {counts}";
        return Tripped ? $"{line}; stopped early after {TripReason}, will try again later" : line;
    }

    private void Trip(string reason)
    {
        Tripped = true;
        TripReason = reason;
    }

    private static string Label(int? status, string kind) => status is int code ? $"HTTP {code}" : kind;
}
