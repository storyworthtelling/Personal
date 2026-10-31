using global::Google.Apis.Auth.OAuth2;
using global::Google.Apis.Drive.v3;
using global::Google.Apis.Http;
using global::Google.Apis.Services;
using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Secrets;

namespace Codex.Ui.Voice;

// The native FocusPhone equivalent of the MAUI apps' vault wiring: given a head's Drive access-token getter
// (its device-account sign-in), it builds the encrypted secrets-vault store over the same Google Drive glue
// VoiceGreeting uses and pairs it with the head's local passphrase. Heads call Service.PublishAsync to back
// up their section on save and Service.LoadAsync to restore it at launch. Writing the vault needs the full
// `drive` scope (Audio's music account and the widened VoiceGoogleAccount both carry it); without it, or
// without a passphrase, publish/restore degrade quietly and the head works from its local settings.
public sealed class SecretsVaultAccess
{
    public SecretsVaultService Service { get; }

    /// <summary>
    /// This person's own vault, in their own Drive. Separate from <see cref="Service"/>
    /// because the two answer different questions: the shared vault holds what the
    /// household owns and has exactly one writer, while this holds what is theirs and
    /// is written by the only install it belongs to.
    /// </summary>
    public PersonalVaultAccess Personal { get; }

    /// <remarks>
    /// Whether this install may write the shared vault is decided by the vault file's Drive ownership, read
    /// by the store on every load — the second phone reads a file shared with it and never writes.
    /// </remarks>
    public SecretsVaultAccess(Func<Task<string>> accessToken, string applicationName)
    {
        var driveFactory = DriveFactory(accessToken, applicationName);

        var store = new DriveSecretsVaultStore(new DriveTextFile(
            driveFactory, DriveSecretsVaultStore.FileName, SharedDriveFileIds.Store));
        Service = new SecretsVaultService(store, () => VaultPassphraseStore.Passphrase);

        Personal = new PersonalVaultAccess(
            new DrivePersonalVaultStore(new DriveTextFile(
                driveFactory, DrivePersonalVaultStore.FileName, SharedDriveFileIds.Store)),
            () => VaultPassphraseStore.Passphrase);
    }

    /// <summary>
    /// A fresh, disposable DriveService per call over the head's access token, on the Android HTTP stack —
    /// the factory shape every Drive seam here takes (the vault, the voice profile, the log folder).
    /// </summary>
    public static Func<CancellationToken, Task<DriveService>> DriveFactory(Func<Task<string>> accessToken, string applicationName) =>
        async _ =>
        {
            var token = await accessToken();
            return new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = GoogleCredential.FromAccessToken(token),
                ApplicationName = applicationName,
                HttpClientFactory = new AndroidGoogleHttpClientFactory()
            });
        };

    // Routes the Google API client's HttpClient through AndroidMessageHandler (SELinux blocks the default
    // socket handler in an app process), matching the head's other Google clients.
    private sealed class AndroidGoogleHttpClientFactory : HttpClientFactory
    {
        protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) =>
            new Xamarin.Android.Net.AndroidMessageHandler();
    }
}
