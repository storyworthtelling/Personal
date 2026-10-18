using Android.Content;
using Codex.Domain.Diagnostics;
using Constitution.Domain.Body;
using Constitution.Infrastructure.SleepNumber;

namespace Constitution.Body;

/// <summary>
/// Body's outcome trail: a capped file of short phrases ("body read weight: ok, 2 readings") in the app's FilesDir, in the
/// repo's own <see cref="DiagnosticLog"/> under its own file name so a line per launch cannot roll a failure history out.
/// Platform outcomes land here as the owner's later device test will need them: permission state, record counts and
/// exception type names. A phrase carries no reading, no package name and no person's name, and nothing else is written.
/// It also takes the bed's phrases, which have the same rule.
/// </summary>
public sealed class BodyLog : IBodyLog, ISleepIqLog
{
    /// <summary>The trail's file name, beside the failure log and the vault trail.</summary>
    public const string FileName = "body.log";

    private const long MaxBytes = 32 * 1024;
    private const string Source = "Body";

    private readonly DiagnosticLog? log;

    public BodyLog(Context context)
    {
        var directory = context.FilesDir?.AbsolutePath;
        log = directory is null ? null : new DiagnosticLog(directory, MaxBytes, FileName);
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
            log.Append(new DiagnosticEntry(DateTimeOffset.Now, DiagnosticLevel.Info, Source, phrase));
        }
        catch (IOException e)
        {
            // The disk is full or gone; a missing trail line must not break the read it describes.
            Android.Util.Log.Warn(Source, $"body trail not written: {e.GetType().Name}");
        }
        catch (UnauthorizedAccessException e)
        {
            Android.Util.Log.Warn(Source, $"body trail not written: {e.GetType().Name}");
        }
    }
}
