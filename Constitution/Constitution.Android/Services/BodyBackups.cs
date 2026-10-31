using Android.Content;
using Codex.Ui.Voice;
using Constitution.Body;
using Constitution.Domain.Body;
using Constitution.Infrastructure.Backup;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Http;
using Google.Apis.Services;
using Storyworthtelling.Voice;

namespace Constitution.Services;

/// <summary>
/// Carries <see cref="BodyBackupRunner"/> to the device: builds the Body backup's Drive file over the
/// signed-in Google account the way <see cref="ConstitutionVaults"/> builds the vaults' (the account's own
/// Drive, found by the file's name, with the shared id store keyed by that name) and reads the passphrase
/// from the shared <see cref="VaultPassphraseStore"/> on each call, never keeping it. Every decision, which
/// is what the tests exercise, lives in the runner; this is only the wiring, and the only place in the head
/// that builds the backup (a guard holds it to that).
///
/// <para>One runner serves the whole process, so the app and the overnight worker share one debouncer, one
/// settled launch restore and one backup whose calls are serialised.</para>
///
/// <para>Device-gated: nothing here has met a Drive. The head names Body types only to hand them to the
/// runner; it has no way off the phone of its own and writes only the runner's outcome phrases to the trail.</para>
/// </summary>
public static class BodyBackups
{
    private const string ApplicationName = "Constitution";

    private static readonly object Gate = new();
    private static readonly BodyBackupDebouncer Debouncer = new();
    private static BodyBackupRunner? runner;

    /// <summary>
    /// The one call a launch makes: restores the household's connections from the vault, and only then the
    /// Body history from its backup, in one background task. The order matters because the passphrase that
    /// opens the backup is the one the vault restore may have just brought back into reach. Fire-and-forget by
    /// design; the results go to the trail, and <paramref name="onRestored"/> runs only when history landed.
    /// </summary>
    /// <param name="context">Any context; the application context is used.</param>
    /// <param name="onRestored">Called on a background thread after a restore that landed; the screen draws what arrived.</param>
    public static void RestoreAtLaunch(Context context, Action onRestored)
    {
        var appContext = context.ApplicationContext ?? context;
        _ = Task.Run(async () =>
        {
            try
            {
                await ConstitutionVaults.Create(appContext).RestoreAsync(CancellationToken.None);

                var store = BodyServices.Create(appContext).Store;
                await RunnerFor(appContext).RestoreAtLaunchAsync(store, onRestored, CancellationToken.None);
            }
            catch (Exception e)
            {
                // The runners report their own failures as results; this is what is left (no files folder, a
                // store of an unexpected kind). The next launch tries again, and the trail says why this one did not.
                new ConstitutionVaultLog(appContext).Outcome($"body restore at launch failed: {e.GetType().Name}");
            }
        });
    }

    /// <summary>
    /// The app is leaving the foreground: writes any change still inside the backup's quiet window now, in the
    /// background, without waiting. Nothing pending touches nothing; the runner reports its own failures, and what
    /// is left is recorded by type name only.
    /// </summary>
    public static void FlushOnPause(Context context)
    {
        var appContext = context.ApplicationContext ?? context;
        _ = Task.Run(async () =>
        {
            try
            {
                await RunnerFor(appContext).FlushAsync(BodyServices.CreateStore(appContext), CancellationToken.None);
            }
            catch (Exception e)
            {
                new ConstitutionVaultLog(appContext).Outcome($"body backup failed: {e.GetType().Name}");
            }
        });
    }

    /// <summary>After the app's open-time read: notes the history and backs it up once it has been quiet.</summary>
    public static Task<BodyBackupRunResult> AfterOpenSyncAsync(Context context, BodySyncResult sync, IBodyStore store, CancellationToken cancellationToken) =>
        RunnerFor(context).AfterSyncAsync(sync, store, BodyBackupTiming.WhenDue, cancellationToken);

    /// <summary>
    /// After the overnight worker's read: backs up at once. The worker has no later turn in which a quiet
    /// window could pass, so debouncing it would be never writing.
    /// </summary>
    public static Task<BodyBackupRunResult> AfterWorkerSyncAsync(Context context, BodySyncResult sync, IBodyStore store, CancellationToken cancellationToken) =>
        RunnerFor(context).AfterSyncAsync(sync, store, BodyBackupTiming.Now, cancellationToken);

    private static BodyBackupRunner RunnerFor(Context context)
    {
        var appContext = context.ApplicationContext ?? context;
        lock (Gate)
        {
            return runner ??= BuildRunner(appContext);
        }
    }

    private static BodyBackupRunner BuildRunner(Context appContext)
    {
        var log = new ConstitutionVaultLog(appContext);
        var account = ConstitutionGoogle.Create();
        return new BodyBackupRunner(
            () => VaultPassphraseStore.Passphrase,
            () => VoiceGoogleAccount.IsSignedIn,
            () => new BodyBackup(BuildDriveFile(account), log),
            () => DateTimeOffset.UtcNow,
            Debouncer,
            log);
    }

    private static DriveTextFile BuildDriveFile(VoiceGoogleAccount account)
    {
        Func<CancellationToken, Task<DriveService>> driveFactory = async _ =>
        {
            var token = await account.GetAccessToken();
            return new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = GoogleCredential.FromAccessToken(token),
                ApplicationName = ApplicationName,
                HttpClientFactory = new AndroidGoogleHttpClientFactory(),
            });
        };

        return new DriveTextFile(driveFactory, BodyBackup.FileName, SharedDriveFileIds.Store);
    }

    // Routes the Google API client through AndroidMessageHandler, as the vaults' Drive client does.
    private sealed class AndroidGoogleHttpClientFactory : HttpClientFactory
    {
        protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) =>
            new Xamarin.Android.Net.AndroidMessageHandler();
    }
}
