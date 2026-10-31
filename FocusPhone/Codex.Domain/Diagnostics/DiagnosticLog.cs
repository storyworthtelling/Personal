using System.Text;
using Storyworthtelling.Diagnostics;

namespace Codex.Domain.Diagnostics;

/// <summary>
/// An append-only, size-capped log of non-fatal diagnostics kept in the app's
/// private directory. Unlike <see cref="CrashLogStore"/> (one latest crash,
/// shown once), this accumulates a reviewable history of background/operational
/// failures — email sync, the message bridge, timeline-store problems — that
/// never crash the app and would otherwise vanish into an inaccessible logcat
/// after cutover (CUTOVER.md §17).
///
/// When the live file exceeds the cap it rolls to a single backup
/// (<c>diagnostic.log.1</c>); the previous backup is discarded, bounding disk use
/// to roughly twice the cap on a locked-down device. <see cref="ReadAll"/> stitches
/// the backup and live file back together in chronological order.
///
/// With a <see cref="LogOutbox"/> (LOG-LOOP-PLAN.md §3.1) a full file is <b>sealed</b> into the outbox
/// instead of rolling to <c>.1</c>, so nothing is discarded before the weekly upload has a confirmed copy
/// in Drive; <see cref="ReadAll"/> then reads the sealed segments and the live file, and
/// <see cref="Clear"/> takes both. Without one the roll is unchanged, for heads not yet on the loop.
///
/// Lives in Domain (dependency-free <see cref="System.IO"/>, like
/// <see cref="CrashLogStore"/>) so it is unit-testable against a temp folder and
/// reachable from the Android head without dragging Infrastructure into the UI.
/// The directory is injected; on device it is the app's FilesDir, which is shared
/// across the UI and WorkManager processes so background writes land in the file
/// the diagnostics screen reads.
/// </summary>
public class DiagnosticLog
{
    public const string FileName = "diagnostic.log";
    private const string BackupSuffix = ".1";
    private const long DefaultMaxBytes = 128 * 1024;

    private readonly string filePath;
    private readonly string backupPath;
    private readonly long maxBytes;
    private readonly LogOutbox? outbox;
    private readonly string logName;
    private readonly object gate = new();

    public DiagnosticLog(string directory) : this(directory, DefaultMaxBytes)
    {
    }

    public DiagnosticLog(string directory, long maxBytes) : this(directory, maxBytes, FileName)
    {
    }

    /// <summary>
    /// A log under its own file name, for a trail that must not share the failure log's cap —
    /// Post's texts trail writes a line per arriving text, which would roll the failure
    /// history out within a day.
    /// </summary>
    public DiagnosticLog(string directory, long maxBytes, string fileName) : this(directory, maxBytes, fileName, null)
    {
    }

    /// <summary>
    /// A log that seals into <paramref name="outbox"/> when full rather than discarding its older half.
    /// </summary>
    public DiagnosticLog(string directory, long maxBytes, string fileName, LogOutbox? outbox)
    {
        filePath = Path.Combine(directory, fileName);
        backupPath = filePath + BackupSuffix;
        this.maxBytes = maxBytes;
        this.outbox = outbox;
        logName = fileName;
    }

    /// <summary>The live file — what the upload worker hands to <see cref="LogOutbox.SealAll"/>.</summary>
    public string LivePath => filePath;

    public bool HasEntries =>
        File.Exists(filePath) || File.Exists(backupPath) || (outbox?.SegmentsFor(logName).Count > 0);

    public void Append(DiagnosticEntry entry)
    {
        lock (gate)
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            RollIfTooLarge();
            File.AppendAllText(filePath, DiagnosticLogFormatter.FormatEntry(entry) + "\n\n");
        }
    }

    public string ReadAll()
    {
        lock (gate)
        {
            var builder = new StringBuilder();
            if (outbox is not null)
            {
                SealOldBackup();
                builder.Append(outbox.ReadSealed(logName));
            }
            if (File.Exists(backupPath))
            {
                builder.Append(File.ReadAllText(backupPath));
            }
            if (File.Exists(filePath))
            {
                builder.Append(File.ReadAllText(filePath));
            }
            return builder.ToString();
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }
            outbox?.ClearLog(logName);
        }
    }

    /// <summary>
    /// Rewrites the live file from a block of previously-read text and drops any
    /// backup — used to reverse a <see cref="Clear"/> within the undo window
    /// (DESIGN.md §7). Collapsing the rolled backup into the live file is
    /// acceptable: the cap will roll it again on the next append.
    /// </summary>
    public void RestoreRaw(string content)
    {
        lock (gate)
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }
            File.WriteAllText(filePath, content);
        }
    }

    private void RollIfTooLarge()
    {
        if (outbox is not null)
        {
            SealOldBackup();
        }
        if (!File.Exists(filePath) || new FileInfo(filePath).Length < maxBytes)
        {
            return;
        }
        if (outbox is not null)
        {
            outbox.Seal(filePath, logName);
            return;
        }
        if (File.Exists(backupPath))
        {
            File.Delete(backupPath);
        }
        File.Move(filePath, backupPath);
    }

    // A .1 backup left by the roll the outbox replaced is the older half of the record: seal it under
    // this log's name, ahead of anything newer, rather than letting it sit outside the upload or be lost.
    private void SealOldBackup()
    {
        if (outbox is not null && File.Exists(backupPath))
        {
            outbox.Seal(backupPath, logName);
        }
    }
}
