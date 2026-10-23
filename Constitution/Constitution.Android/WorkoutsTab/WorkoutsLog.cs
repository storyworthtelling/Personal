using Android.Content;
using Codex.Domain.Diagnostics;

namespace Constitution.WorkoutsTab;

/// <summary>
/// The Workouts tab's outcome trail: a capped file of short phrases in the app's FilesDir, under its own name so a line per
/// open cannot roll a failure history out. A phrase names the act and the exception's type; it never carries a day, a
/// session, a person or an event.
/// </summary>
public sealed class WorkoutsLog
{
    public const string FileName = "workouts.log";

    private const long MaxBytes = 32 * 1024;
    private const string Source = "Workouts";

    private readonly DiagnosticLog? log;

    public WorkoutsLog(Context context)
    {
        var directory = context.NoBackupFilesDir?.AbsolutePath;
        log = directory is null ? null : new DiagnosticLog(directory, MaxBytes, FileName);
    }

    public void Outcome(string phrase)
    {
        if (log is null)
        {
            return;
        }

        try
        {
            log.Append(new DiagnosticEntry(DateTimeOffset.Now, DiagnosticLevel.Info, Source, phrase));
        }
        catch (IOException e)
        {
            // The disk is full or gone; a missing trail line must not break the act it describes.
            Android.Util.Log.Warn(Source, $"workouts trail not written: {e.GetType().Name}");
        }
        catch (UnauthorizedAccessException e)
        {
            Android.Util.Log.Warn(Source, $"workouts trail not written: {e.GetType().Name}");
        }
    }
}
