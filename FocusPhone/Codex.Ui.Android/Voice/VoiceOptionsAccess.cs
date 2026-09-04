using global::Google.Apis.Auth.OAuth2;
using global::Google.Apis.Drive.v3;
using global::Google.Apis.Http;
using global::Google.Apis.Services;
using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Secrets;

namespace Codex.Ui.Voice;

/// <summary>
/// Builds the shared Claude <see cref="DriveVoiceOptions"/> for a native head — the same
/// vault-backed key <see cref="VoiceGreeting"/> uses — so other Claude features in a head (the
/// shelf-glyph maker) read the one key without their own Drive/vault plumbing. Call
/// <c>RefreshAsync</c> on the result before use to pull the key; with no passphrase or no key it
/// yields an empty key and the caller degrades. Kept here (not the head) because this layer already
/// owns the Google Drive client glue.
/// </summary>
public static class VoiceOptionsAccess
{
    public static DriveVoiceOptions ForHead(Func<Task<string>> accessToken, string applicationName)
    {
        Func<CancellationToken, Task<DriveService>> driveFactory = async _ =>
        {
            var token = await accessToken();
            return new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = GoogleCredential.FromAccessToken(token),
                ApplicationName = applicationName,
                HttpClientFactory = new AndroidGoogleHttpClientFactory(),
            });
        };

        var vaultStore = new DriveSecretsVaultStore(new DriveTextFile(
            driveFactory, DriveSecretsVaultStore.FileName, SharedDriveFileIds.Store));
        return new DriveVoiceOptions(new VaultVoiceConfigStore(vaultStore, () => VaultPassphraseStore.Passphrase));
    }

    private sealed class AndroidGoogleHttpClientFactory : HttpClientFactory
    {
        protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) =>
            new Xamarin.Android.Net.AndroidMessageHandler();
    }
}
