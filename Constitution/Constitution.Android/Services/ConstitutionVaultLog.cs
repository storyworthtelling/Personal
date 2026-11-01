using Storyworthtelling.Diagnostics;
using Codex.Ui;
using Android.Content;
using Codex.Domain.Diagnostics;
using Constitution.Infrastructure.Vault;

namespace Constitution.Services;

/// <summary>
/// The vault sync's outcome trail: a capped file of short phrases ("vault restore: restored 3 fields")
/// in the app's FilesDir, in the repo's own <see cref="DiagnosticLog"/> under its own file name so a
/// line per launch cannot roll a failure history out. The sync hands over phrases that carry no value,
/// passphrase or name, and nothing else is ever written here.
/// </summary>
public sealed class ConstitutionVaultLog : IConstitutionVaultLog
{
    /// <summary>The trail's file name, beside the failure log.</summary>
    public const string FileName = "vault.log";

    /// <summary>The trail's cap; past it the older half is sealed into the log loop's outbox, not discarded.</summary>
    public const long MaxBytes = 32 * 1024;
    private const string Source = "Vault";

    private readonly DiagnosticLog? log;

    public ConstitutionVaultLog(Context context)
    {
        var directory = context.NoBackupFilesDir?.AbsolutePath;
        // The head's own instance when the Application has installed it, so one outbox counts every seal;
        // a process that never ran it (a test host) gets its own, still sealing rather than discarding.
        log = HeadDiagnostics.Current?.Trail(FileName)
            ?? (directory is null ? null : new DiagnosticLog(directory, MaxBytes, FileName, new LogOutbox(directory)));
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
            // The disk is full or gone; a missing trail line must not break the sync it describes.
            Android.Util.Log.Warn(Source, $"vault trail not written: {e.GetType().Name}");
        }
        catch (UnauthorizedAccessException e)
        {
            Android.Util.Log.Warn(Source, $"vault trail not written: {e.GetType().Name}");
        }
    }
}
