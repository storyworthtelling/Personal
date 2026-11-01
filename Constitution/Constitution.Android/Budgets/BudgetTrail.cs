using Storyworthtelling.Diagnostics;
using Codex.Ui;
using Android.Content;
using Codex.Domain.Diagnostics;

namespace Constitution.Budgets;

/// <summary>
/// The budget receiver's outcome trail: a capped file of short phrases in the app's private files folder, under its own name so a
/// line per broadcast cannot roll a failure history out. A phrase names the act and, for a refusal, the field or the exception's
/// type; it never carries a month or an amount.
/// </summary>
public sealed class BudgetTrail
{
    public const string FileName = "budget.log";

    /// <summary>The trail's cap; past it the older half is sealed into the log loop's outbox, not discarded.</summary>
    public const long MaxBytes = 32 * 1024;
    private const string Source = "Budget";

    private readonly DiagnosticLog? log;

    public BudgetTrail(Context context)
    {
        var directory = context.NoBackupFilesDir?.AbsolutePath;
        // The head's own instance when the Application has installed it, so one outbox counts every seal;
        // a process that never ran it (a test host) gets its own, still sealing rather than discarding.
        log = HeadDiagnostics.Current?.Trail(FileName)
            ?? (directory is null ? null : new DiagnosticLog(directory, MaxBytes, FileName, new LogOutbox(directory)));
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
            Android.Util.Log.Warn(Source, $"budget trail not written: {e.GetType().Name}");
        }
        catch (UnauthorizedAccessException e)
        {
            Android.Util.Log.Warn(Source, $"budget trail not written: {e.GetType().Name}");
        }
    }
}
