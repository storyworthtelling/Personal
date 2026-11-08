using Android.Content;
using Codex.Domain.Post;
using Codex.Infrastructure.Post;
using Codex.Ui.Voice;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Storyworthtelling.Voice;

namespace Post;

/// <summary>
/// Backs the ghost archive up to Drive and restores it, both on every sync.
///
/// This is the one store in Post that genuinely cannot be rebuilt. Everything else — the
/// timeline cache, the classifications, the read state — is derivable from the sources;
/// the ghost is the only copy of a message whose original was deleted at the source on
/// purpose. It survives a reboot in timeline.db and would not survive the factory reset
/// the cutover plans for.
///
/// The rules live in <see cref="ArchiveBackupSync"/>: a union merge in both directions (so a
/// restore that never ran is done on the next sync, and a new filing is added to the backup
/// rather than replacing it), sealed under the vault passphrase, with the old plaintext file
/// moved across and trashed once the sealed one is confirmed.
///
/// Best-effort throughout. No sign-in, no network, no Drive scope: filing still works and
/// the ghosts are still on the phone.
/// </summary>
internal sealed class ArchiveBackup
{
    private readonly ArchiveBackupSync sync;

    public ArchiveBackup(Func<Task<string>> accessToken, string applicationName)
    {
        Func<CancellationToken, Task<DriveService>> driveFactory = async _ =>
        {
            var token = await accessToken();
            return new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = GoogleCredential.FromAccessToken(token),
                ApplicationName = applicationName,
                HttpClientFactory = new GoogleHttpClientFactory(),
            });
        };

        sync = new ArchiveBackupSync(
            new DriveTextFile(driveFactory, ArchiveBackupSync.FileName, SharedDriveFileIds.Store),
            new DriveTextFile(driveFactory, ArchiveBackupSync.LegacyFileName, SharedDriveFileIds.Store),
            () => VaultPassphraseStore.Passphrase);
    }

    /// <summary>
    /// Restores what the backup holds and the phone does not, and backs up what the phone holds,
    /// in one pass. Called after every sync and once at launch, so a restore that failed (offline,
    /// not signed in yet, no passphrase yet) is simply done again later.
    /// </summary>
    public async Task SyncAsync(Context context, CancellationToken cancellation = default)
    {
        try
        {
            using var database = TimelineDatabase.Open(context);
            var result = await sync.SyncAsync(database.Filing, DateTimeOffset.UtcNow, cancellation);
            if (result.Restored > 0)
            {
                PostDiagnostics.LogWarning("ArchiveBackup", $"Restored {result.Restored} filed messages from Drive.");
            }
            if (result.Outcome is ArchiveSyncOutcome.Refused or ArchiveSyncOutcome.NotConfirmed or ArchiveSyncOutcome.Unreachable)
            {
                PostDiagnostics.LogWarning("ArchiveBackup", $"Archive backup {result.Outcome}: {result.Detail}");
            }
        }
        catch (Exception e)
        {
            // Filing still worked and the ghosts are still on the phone. A backup that could not
            // be written is not worth telling the owner about mid-sync; the diagnostics log is
            // where it belongs.
            Android.Util.Log.Info("Post", $"Archive backup did not run: {e.GetType().Name}");
            PostDiagnostics.LogWarning("ArchiveBackup", "Could not sync the archive with Drive", e);
        }
    }
}
