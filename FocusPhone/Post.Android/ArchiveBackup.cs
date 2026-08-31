using Android.Content;
using Codex.Domain.Post;
using Codex.Ui.Voice;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Storyworthtelling.Voice;

namespace Post;

/// <summary>
/// Backs the ghost archive up to Drive, and restores it onto a device that has none.
///
/// This is the one store in Post that genuinely cannot be rebuilt. Everything else — the
/// timeline cache, the classifications, the read state — is derivable from the sources;
/// the ghost is the only copy of a message whose original was deleted at the source on
/// purpose. It survives a reboot in timeline.db and would not survive the factory reset
/// the cutover plans for.
///
/// One file, found by name, following the audio library exactly. The phone is the single
/// writer, so this is a mirror rather than a merge, and the rule that matters is the
/// refusal: an empty local archive never overwrites a backup, because a fresh install has
/// nothing filed and writing that up would destroy the only copy of everything the owner
/// ever filed.
///
/// Best-effort throughout. No sign-in, no network, no Drive scope: filing still works and
/// the ghosts are still on the phone.
/// </summary>
internal sealed class ArchiveBackup
{
    /// <summary>One archive backup, one file, found by name.</summary>
    public const string FileName = "storyworthtelling-post-archive.json";

    private readonly IDriveTextFile file;

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

        file = new DriveTextFile(driveFactory, FileName, SharedDriveFileIds.Store);
    }

    /// <summary>
    /// Writes the archive up when it has changed and there is something to write. Called
    /// after a sync, so the comparison is by content rather than by time — the timestamp
    /// moves every run and would make every run a write.
    /// </summary>
    public async Task BackUpAsync(Context context, CancellationToken cancellation = default)
    {
        try
        {
            using var database = TimelineDatabase.Open(context);
            var local = new ArchiveSnapshot(database.Filing.LoadArchive().All, DateTimeOffset.UtcNow);

            var backedUp = ArchiveSnapshotCodec.Decode(await file.ReadAsync(cancellation));
            if (!ArchiveSync.NeedsWriting(local, backedUp))
            {
                return;
            }

            await file.WriteAsync(ArchiveSnapshotCodec.Encode(local), cancellation);
        }
        catch (Exception e)
        {
            // Filing still worked and the ghosts are still on the phone. A backup that
            // could not be written is not worth telling the owner about mid-sync; the
            // diagnostics log is where it belongs.
            Android.Util.Log.Info("Post", $"Archive backup did not run: {e.Message}");
            PostDiagnostics.LogWarning("ArchiveBackup", "Could not back the archive up", e);
        }
    }

    /// <summary>
    /// Takes the backup down onto a device with nothing filed — a reset, or a new phone.
    /// A device that already has ghosts is the authority on its own archive and is left
    /// alone.
    /// </summary>
    public async Task RestoreIfEmptyAsync(Context context, CancellationToken cancellation = default)
    {
        try
        {
            using var database = TimelineDatabase.Open(context);
            if (!ArchiveSync.ShouldRestore(database.Filing.LoadArchive().All))
            {
                return;
            }

            if (ArchiveSnapshotCodec.Decode(await file.ReadAsync(cancellation)) is not { } backedUp)
            {
                return;
            }

            foreach (var ghost in backedUp.Ghosts)
            {
                database.Filing.SaveGhost(ghost);
            }

            PostDiagnostics.LogWarning(
                "ArchiveBackup", $"Restored {backedUp.Ghosts.Count} filed messages from Drive.");
        }
        catch (Exception e)
        {
            Android.Util.Log.Info("Post", $"Archive restore did not run: {e.Message}");
            PostDiagnostics.LogWarning("ArchiveBackup", "Could not restore the archive", e);
        }
    }
}
