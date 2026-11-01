using Storyworthtelling.Diagnostics;

namespace Storyworthtelling.Voice.Logs;

/// <summary>Where a head keeps the log loop's three small facts (Preferences on MAUI, SharedPreferences natively).</summary>
public interface ILogLoopState
{
    /// <summary>The random install id, made once and kept; never derived from the device.</summary>
    string? InstallId { get; set; }

    /// <summary><see cref="UploadHistory.Format"/>'s form.</summary>
    string? UploadHistory { get; set; }

    /// <summary>The last run found no vault passphrase, so nothing was uploaded.</summary>
    bool WaitingForPassphrase { get; set; }
}

/// <summary>
/// The bookkeeping around one upload (LOG-LOOP-PLAN.md §3.3): the install id, the history that says whether the
/// Saturday window is still open, the waiting-for-a-passphrase flag, and one line in the head's own log saying what
/// the run did. <see cref="LogUploader"/> decides what to upload and what to clear; this only records the answer.
/// A run never throws for a Drive or folder failure: it is a result with the reason in it.
/// </summary>
public sealed class LogLoopRun(ILogLoopState state, Action<bool, string> line, Func<DateTimeOffset>? clock = null)
{
    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.Now);

    public UploadHistory History => Diagnostics.UploadHistory.Parse(state.UploadHistory);

    /// <summary>Whether the Saturday window is open and this week's upload not yet confirmed.</summary>
    public bool Due => UploadWindow.Due(History.LastConfirmed, now());

    /// <summary>The one sentence the Diagnostics screen shows about uploads, or none.</summary>
    public string? Status =>
        LogUploadStatus.Sentence(state.WaitingForPassphrase ? LogUploadRefusal.WaitingForPassphrase : null, History.FailedWeeks);

    public LogInstall Install(string app, string phone, string appVersion, long versionCode)
    {
        if (string.IsNullOrEmpty(state.InstallId))
        {
            state.InstallId = LogInstall.NewInstallId();
        }
        return new LogInstall(app, state.InstallId, phone, appVersion, versionCode);
    }

    public async Task<LogUploadResult> RunAsync(
        IDriveLogFolder folder, LogOutbox outbox, LogInstall install, IReadOnlyList<string> livePaths, string passphrase,
        CancellationToken cancellationToken)
    {
        LogUploadResult result;
        try
        {
            result = await new LogUploader(folder, outbox, install).RunAsync(livePaths, passphrase, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            result = new LogUploadResult(0, 0, null, $"{e.GetType().Name}: {e.Message}");
        }
        Record(result);
        return result;
    }

    private void Record(LogUploadResult result)
    {
        var history = History;
        state.UploadHistory = (result.Refusal is not null ? history : result.Succeeded ? history.Confirmed(now()) : history.Failed(now())).Format();
        state.WaitingForPassphrase = result.Refusal is not null;

        if (result.Refusal is not null)
        {
            line(false, LogUploadStatus.Sentence(result.Refusal, 0)!);
        }
        else if (result.Succeeded)
        {
            line(false, $"Uploaded and confirmed {result.Confirmed} of {result.Planned} batches.");
        }
        else
        {
            line(true, $"Upload did not complete: {result.Error}");
        }
        if (result.Trashed > 0)
        {
            line(false, $"Trashed {result.Trashed} processed files of this install.");
        }
        if (result.TrashError is not null)
        {
            line(true, $"Trashing processed files did not complete: {result.TrashError}");
        }
    }
}
