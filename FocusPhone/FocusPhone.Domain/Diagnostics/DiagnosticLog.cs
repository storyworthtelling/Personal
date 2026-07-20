using System.Text;

namespace FocusPhone.Domain.Diagnostics;

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
    private readonly object gate = new();

    public DiagnosticLog(string directory) : this(directory, DefaultMaxBytes)
    {
    }

    public DiagnosticLog(string directory, long maxBytes)
    {
        filePath = Path.Combine(directory, FileName);
        backupPath = filePath + BackupSuffix;
        this.maxBytes = maxBytes;
    }

    public bool HasEntries => File.Exists(filePath) || File.Exists(backupPath);

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
        if (!File.Exists(filePath) || new FileInfo(filePath).Length < maxBytes)
        {
            return;
        }
        if (File.Exists(backupPath))
        {
            File.Delete(backupPath);
        }
        File.Move(filePath, backupPath);
    }
}
