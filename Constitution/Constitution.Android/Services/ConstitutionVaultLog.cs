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

    private const long MaxBytes = 32 * 1024;
    private const string Source = "Vault";

    private readonly DiagnosticLog? log;

    public ConstitutionVaultLog(Context context)
    {
        var directory = context.NoBackupFilesDir?.AbsolutePath;
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
            // The disk is full or gone; a missing trail line must not break the sync it describes.
            Android.Util.Log.Warn(Source, $"vault trail not written: {e.GetType().Name}");
        }
        catch (UnauthorizedAccessException e)
        {
            Android.Util.Log.Warn(Source, $"vault trail not written: {e.GetType().Name}");
        }
    }
}
