using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Storyworthtelling.Diagnostics;

/// <summary>One full log file sealed into the outbox, waiting for Drive.</summary>
/// <param name="Sequence">The per-install counter value; monotonic, never reused.</param>
/// <param name="LogName">The live file it was sealed from (<c>diagnostic.log</c>, <c>texts.log</c>).</param>
/// <param name="FilePath">Where the sealed segment lives now.</param>
/// <param name="Bytes">Its size, which is its plaintext size in a batch.</param>
/// <param name="SealedUtc">When it was last written — the newest line it holds.</param>
public sealed record SealedSegment(long Sequence, string LogName, string FilePath, long Bytes, DateTimeOffset SealedUtc);

/// <summary>
/// Where a full log goes instead of the bin (LOG-LOOP-PLAN.md §3.1, G8). A log that reaches its cap is
/// renamed in here as <c>NNNNNN.&lt;log&gt;.sealed</c>, under a per-install counter kept in <c>seq</c>; the
/// upload worker seals every live log at once with <see cref="SealAll"/>; a segment is deleted only once
/// Drive has a confirmed copy (<see cref="LogUploadPlan"/>). The Diagnostics screens read live + sealed, so
/// nothing disappears from the phone until it is in Drive.
/// <para>
/// A rename is atomic on one filesystem, so a writer elsewhere (the WorkManager process opens its log by
/// path on every append) simply starts a new live file: no line is lost or split across the cut.
/// </para>
/// <para>
/// Past the cap (8 MB per app) the oldest segment is dropped and the segment that now opens the record
/// starts with <c>DROPPED n segments (oldest &lt;time&gt;)</c>, so the reader knows the record has a hole.
/// The newest segment is always kept.
/// </para>
/// </summary>
public sealed partial class LogOutbox
{
    public const string DirectoryName = "log-outbox";
    public const string SequenceFileName = "seq";
    public const long DefaultCapBytes = 8L * 1024 * 1024;
    private const string SealedSuffix = ".sealed";
    private const string DroppedPrefix = "DROPPED ";

    // One lock per outbox folder for the whole process, so two DiagnosticLog instances over one FilesDir
    // never hand out the same sequence number.
    private static readonly ConcurrentDictionary<string, object> Locks = new(StringComparer.Ordinal);

    private readonly long capBytes;
    private readonly object gate;

    public LogOutbox(string filesDirectory, long capBytes = DefaultCapBytes)
    {
        Directory = Path.GetFullPath(Path.Combine(filesDirectory, DirectoryName));
        this.capBytes = capBytes;
        gate = Locks.GetOrAdd(Directory, _ => new object());
    }

    /// <summary>The outbox folder itself.</summary>
    public string Directory { get; }

    /// <summary>
    /// Seals one live log into the outbox. Returns null when there is nothing to seal (no file, or an
    /// empty one), so an idle log costs no segment.
    /// </summary>
    public SealedSegment? Seal(string liveFilePath)
    {
        lock (gate)
        {
            var segment = SealLocked(liveFilePath);
            if (segment is not null)
            {
                EnforceCap();
            }
            return segment is null ? null : Describe(segment.FilePath) ?? segment;
        }
    }

    /// <summary>Seals every named live log in one pass, under the outbox's lock.</summary>
    public IReadOnlyList<SealedSegment> SealAll(IEnumerable<string> liveFilePaths)
    {
        lock (gate)
        {
            var sealedNow = liveFilePaths
                .Select(SealLocked)
                .OfType<SealedSegment>()
                .ToList();
            if (sealedNow.Count > 0)
            {
                EnforceCap();
            }
            return sealedNow.Select(s => Describe(s.FilePath)).OfType<SealedSegment>().ToList();
        }
    }

    /// <summary>Every sealed segment, oldest first.</summary>
    public IReadOnlyList<SealedSegment> Segments()
    {
        lock (gate)
        {
            return ListLocked();
        }
    }

    /// <summary>One log's sealed segments, oldest first.</summary>
    public IReadOnlyList<SealedSegment> SegmentsFor(string logName) =>
        Segments().Where(s => s.LogName == logName).ToList();

    /// <summary>One log's sealed text, oldest first — what a Diagnostics screen shows before the live file.</summary>
    public string ReadSealed(string logName) =>
        string.Concat(SegmentsFor(logName).Select(ReadText));

    public string ReadText(SealedSegment segment) =>
        File.Exists(segment.FilePath) ? File.ReadAllText(segment.FilePath) : string.Empty;

    /// <summary>Deletes exactly these segments — the ones Drive has confirmed — and nothing else.</summary>
    public void Delete(IEnumerable<SealedSegment> segments)
    {
        lock (gate)
        {
            foreach (var segment in segments)
            {
                if (File.Exists(segment.FilePath))
                {
                    File.Delete(segment.FilePath);
                }
            }
        }
    }

    /// <summary>The owner's own Clear on a Diagnostics screen: that log's sealed segments go too.</summary>
    public void ClearLog(string logName) => Delete(SegmentsFor(logName));

    private SealedSegment? SealLocked(string liveFilePath)
    {
        if (!File.Exists(liveFilePath) || new FileInfo(liveFilePath).Length == 0)
        {
            return null;
        }

        System.IO.Directory.CreateDirectory(Directory);
        var sequence = NextSequence();
        var logName = Path.GetFileName(liveFilePath);
        var target = Path.Combine(Directory, $"{sequence:000000}.{logName}{SealedSuffix}");
        File.Move(liveFilePath, target);
        return Describe(target);
    }

    // The counter is the larger of the stored value and the highest segment on disk, so a lost or stale
    // seq file can never make a number come round again.
    private long NextSequence()
    {
        var path = Path.Combine(Directory, SequenceFileName);
        long stored = 0;
        if (File.Exists(path))
        {
            long.TryParse(File.ReadAllText(path).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out stored);
        }
        var highest = ListLocked().Select(s => s.Sequence).DefaultIfEmpty(0).Max();
        var next = Math.Max(stored, highest) + 1;
        File.WriteAllText(path, next.ToString(CultureInfo.InvariantCulture));
        return next;
    }

    private List<SealedSegment> ListLocked()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return [];
        }
        return System.IO.Directory.EnumerateFiles(Directory, "*" + SealedSuffix)
            .Select(Describe)
            .OfType<SealedSegment>()
            .OrderBy(s => s.Sequence)
            .ToList();
    }

    private static SealedSegment? Describe(string path)
    {
        var match = SegmentName().Match(Path.GetFileName(path));
        if (!match.Success || !File.Exists(path))
        {
            return null;
        }
        var info = new FileInfo(path);
        return new SealedSegment(
            long.Parse(match.Groups["seq"].Value, CultureInfo.InvariantCulture),
            match.Groups["log"].Value,
            path,
            info.Length,
            new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero));
    }

    private void EnforceCap()
    {
        var segments = ListLocked();
        var total = segments.Sum(s => s.Bytes);
        var dropped = new List<SealedSegment>();
        while (total > capBytes && segments.Count - dropped.Count > 1)
        {
            var oldest = segments[dropped.Count];
            dropped.Add(oldest);
            total -= oldest.Bytes;
        }
        if (dropped.Count == 0)
        {
            return;
        }

        // A dropped segment that itself opened with a DROPPED line carries an earlier hole; fold it in so
        // the count is the whole of what is missing, not just the latest loss.
        var count = dropped.Count;
        var oldestTime = dropped[0].SealedUtc;
        foreach (var segment in dropped)
        {
            var earlier = ReadDroppedLine(segment);
            if (earlier is not null)
            {
                count += earlier.Value.Count;
                if (earlier.Value.Oldest < oldestTime)
                {
                    oldestTime = earlier.Value.Oldest;
                }
            }
            File.Delete(segment.FilePath);
        }

        var opening = segments[dropped.Count];
        var text = File.ReadAllText(opening.FilePath);
        var existing = ReadDroppedLine(opening);
        if (existing is not null)
        {
            count += existing.Value.Count;
            if (existing.Value.Oldest < oldestTime)
            {
                oldestTime = existing.Value.Oldest;
            }
            text = text[(text.IndexOf('\n') + 1)..];
        }
        var line = $"{DroppedPrefix}{count} segments (oldest {oldestTime.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)})\n";
        var stamp = File.GetLastWriteTimeUtc(opening.FilePath);
        File.WriteAllText(opening.FilePath, line + text);
        File.SetLastWriteTimeUtc(opening.FilePath, stamp);
    }

    private static (int Count, DateTimeOffset Oldest)? ReadDroppedLine(SealedSegment segment)
    {
        using var reader = new StreamReader(segment.FilePath);
        var first = reader.ReadLine();
        if (first is null)
        {
            return null;
        }
        var match = DroppedLine().Match(first);
        if (!match.Success)
        {
            return null;
        }
        return (
            int.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(match.Groups["t"].Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal));
    }

    [GeneratedRegex(@"^(?<seq>\d{6,})\.(?<log>.+)\.sealed$")]
    private static partial Regex SegmentName();

    [GeneratedRegex(@"^DROPPED (?<n>\d+) segments \(oldest (?<t>[^)]+)\)$")]
    private static partial Regex DroppedLine();
}
