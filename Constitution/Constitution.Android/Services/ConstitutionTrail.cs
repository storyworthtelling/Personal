using Android.Content;
using Codex.Domain.Diagnostics;
using Codex.Ui;
using Constitution.Domain.Diagnostics;
using Storyworthtelling.Diagnostics;

namespace Constitution.Services;

/// <summary>
/// One of Constitution's outcome trails: a capped file of short, value-free phrases in the app's no-backup folder, under
/// its own name so a line per run cannot roll a failure history out. There were four copies of this class; they are one
/// now, and a phrase that names a failure is written at WARN rather than INFO (<see cref="TrailLevel"/>, audit 08 §10).
/// </summary>
public abstract class ConstitutionTrail
{
    private readonly DiagnosticLog? log;
    private readonly string fileName;
    private readonly string source;

    protected ConstitutionTrail(Context context, string fileName, long maxBytes, string source)
    {
        this.fileName = fileName;
        this.source = source;
        var directory = context.NoBackupFilesDir?.AbsolutePath;
        // The head's own instance when the Application has installed it, so one outbox counts every seal;
        // a process that never ran it (a test host) gets its own, still sealing rather than discarding.
        log = HeadDiagnostics.Current?.Trail(fileName)
            ?? (directory is null ? null : new DiagnosticLog(directory, maxBytes, fileName, new LogOutbox(directory)));
    }

    /// <summary>Everything the trail holds, oldest first; empty when nothing was recorded.</summary>
    public string ReadAll() => log?.ReadAll() ?? string.Empty;

    public void Outcome(string phrase)
    {
        if (log is null)
        {
            return;
        }

        try
        {
            var level = TrailLevel.IsProblem(phrase) ? DiagnosticLevel.Warning : DiagnosticLevel.Info;
            log.Append(new DiagnosticEntry(DateTimeOffset.Now, level, source, phrase));
        }
        catch (IOException e)
        {
            // The disk is full or gone; a missing trail line must not break the run it describes.
            Android.Util.Log.Warn(source, $"{fileName} not written: {e.GetType().Name}");
        }
        catch (UnauthorizedAccessException e)
        {
            Android.Util.Log.Warn(source, $"{fileName} not written: {e.GetType().Name}");
        }
    }
}
