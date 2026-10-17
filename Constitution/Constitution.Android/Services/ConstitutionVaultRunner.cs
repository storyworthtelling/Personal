using Android.Content;
using Codex.Ui.Voice;
using Constitution.Infrastructure.Vault;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Http;
using Google.Apis.Services;
using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Secrets;

namespace Constitution.Services;

/// <summary>How a runner call ended. Carries no value and no passphrase.</summary>
public enum VaultRunStatus
{
    /// <summary>No vault passphrase has been entered, so Drive was not touched.</summary>
    NoPassphrase,

    /// <summary>No Google account is signed in on this install, so Drive was not touched.</summary>
    NotSignedIn,

    /// <summary>A restore ran; see <see cref="ConstitutionVaultRunResult.Restore"/> for how each vault went.</summary>
    Restored,

    /// <summary>A publish ran; see <see cref="ConstitutionVaultRunResult.Publish"/> for how each vault went.</summary>
    Published,

    /// <summary>The run itself failed (not a vault outcome); <see cref="ConstitutionVaultRunResult.Detail"/> names the failure's type.</summary>
    Failed,
}

/// <summary>What a runner call did.</summary>
public sealed record ConstitutionVaultRunResult(
    VaultRunStatus Status,
    ConstitutionRestoreResult? Restore = null,
    ConstitutionPublishResult? Publish = null,
    string? Detail = null);

/// <summary>
/// Carries <see cref="ConstitutionVaultSync"/> to the device: builds the Drive stores over the signed-in
/// Google account the way the sibling heads do, reads the passphrase from the shared
/// <see cref="VaultPassphraseStore"/> (the one passphrase store; this adds no second), and runs the
/// sync off the UI thread. It never throws to its caller: every ending is a <see cref="ConstitutionVaultRunResult"/>.
///
/// <para>The seams are constructor arguments so the orchestration reads on its own; <see cref="Create"/>
/// is the device wiring. Drive is built with <see cref="SecretsVaultAccess"/>'s own recipe (its stores
/// are private to it, and the sync needs the stores themselves rather than the access object).</para>
/// </summary>
public sealed class ConstitutionVaultRunner
{
    private const string ApplicationName = "Constitution";

    private readonly Func<string?> passphrase;
    private readonly Func<bool> signedIn;
    private readonly Func<ConstitutionVaultSync> syncFactory;
    private readonly IConstitutionVaultLog log;

    public ConstitutionVaultRunner(
        Func<string?> passphrase,
        Func<bool> signedIn,
        Func<ConstitutionVaultSync> syncFactory,
        IConstitutionVaultLog log)
    {
        this.passphrase = passphrase;
        this.signedIn = signedIn;
        this.syncFactory = syncFactory;
        this.log = log;
    }

    /// <summary>The runner wired to this device: shared passphrase store, the head's Google account, Drive.</summary>
    public static ConstitutionVaultRunner Create(Context context)
    {
        var appContext = context.ApplicationContext ?? context;
        var log = new ConstitutionVaultLog(appContext);
        var account = new VoiceGoogleAccount();
        return new ConstitutionVaultRunner(
            () => VaultPassphraseStore.Passphrase,
            () => VoiceGoogleAccount.IsSignedIn,
            () => BuildSync(appContext, account, log),
            log);
    }

    /// <summary>
    /// The single call a launch makes: restores in the background when there is a passphrase and an
    /// account, and otherwise does nothing. Fire-and-forget by design; the result goes to the trail.
    /// </summary>
    public static void RestoreAtLaunch(Context context)
    {
        var runner = Create(context);
        _ = Task.Run(() => runner.RestoreAsync(CancellationToken.None));
    }

    /// <summary>The call a changed connection makes; same shape as <see cref="RestoreAtLaunch"/>.</summary>
    public static void PublishAfterChange(Context context)
    {
        var runner = Create(context);
        _ = Task.Run(() => runner.PublishAsync(CancellationToken.None));
    }

    /// <summary>Fills what this install is missing from the vaults. Runs on a thread-pool thread.</summary>
    public Task<ConstitutionVaultRunResult> RestoreAsync(CancellationToken cancellationToken) =>
        Task.Run(async () =>
        {
            var pass = passphrase();
            if (string.IsNullOrEmpty(pass))
            {
                return new ConstitutionVaultRunResult(VaultRunStatus.NoPassphrase);
            }

            if (!signedIn())
            {
                log.Outcome("vault restore skipped: not signed in");
                return new ConstitutionVaultRunResult(VaultRunStatus.NotSignedIn);
            }

            try
            {
                var result = await syncFactory().RestoreAsync(pass, cancellationToken);
                return new ConstitutionVaultRunResult(VaultRunStatus.Restored, Restore: result);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                return Failed("restore", e);
            }
        }, cancellationToken);

    /// <summary>Saves what this install holds to the vaults it belongs in. Runs on a thread-pool thread.</summary>
    public Task<ConstitutionVaultRunResult> PublishAsync(CancellationToken cancellationToken) =>
        Task.Run(async () =>
        {
            var pass = passphrase();
            if (string.IsNullOrEmpty(pass))
            {
                return new ConstitutionVaultRunResult(VaultRunStatus.NoPassphrase);
            }

            if (!signedIn())
            {
                log.Outcome("vault publish skipped: not signed in");
                return new ConstitutionVaultRunResult(VaultRunStatus.NotSignedIn);
            }

            try
            {
                var result = await syncFactory().PublishAsync(pass, cancellationToken);
                return new ConstitutionVaultRunResult(VaultRunStatus.Published, Publish: result);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                return Failed("publish", e);
            }
        }, cancellationToken);

    // The type name only: a message from a Drive or crypto failure is not ours to vouch for as secret-free.
    private ConstitutionVaultRunResult Failed(string verb, Exception e)
    {
        var type = e.GetType().Name;
        log.Outcome($"vault {verb} failed: {type}");
        return new ConstitutionVaultRunResult(VaultRunStatus.Failed, Detail: type);
    }

    private static ConstitutionVaultSync BuildSync(Context context, VoiceGoogleAccount account, IConstitutionVaultLog log)
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

        var shared = new DriveSecretsVaultStore(new DriveTextFile(
            driveFactory, DriveSecretsVaultStore.FileName, SharedDriveFileIds.Store));
        var personal = new DrivePersonalVaultStore(new DriveTextFile(
            driveFactory, DrivePersonalVaultStore.FileName, SharedDriveFileIds.Store));
        return new ConstitutionVaultSync(
            shared, personal, () => VoiceGoogleAccount.Email, new ConstitutionConnections(context), log);
    }

    // Routes the Google API client through AndroidMessageHandler, as the sibling heads' Drive clients do.
    private sealed class AndroidGoogleHttpClientFactory : HttpClientFactory
    {
        protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) =>
            new Xamarin.Android.Net.AndroidMessageHandler();
    }
}
