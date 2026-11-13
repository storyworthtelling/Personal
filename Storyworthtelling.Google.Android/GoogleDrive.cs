using global::Google.Apis.Auth.OAuth2;
using global::Google.Apis.Drive.v3;
using global::Google.Apis.Http;
using global::Google.Apis.Services;

namespace Storyworthtelling.Google.Android;

public static class GoogleDrive
{
    /// <param name="onRejected">Told the token when Drive answers 401, so the token source forgets it (audit 11 §4);
    /// by default whichever <see cref="DeviceAccountTokens"/> handed it out.</param>
    public static DriveService Service(string accessToken, string applicationName, Action<string>? onRejected = null) =>
        new(new BaseClientService.Initializer
        {
            HttpClientInitializer = new RejectedTokenWatch(
                GoogleCredential.FromAccessToken(accessToken), accessToken, onRejected ?? DeviceAccountTokens.ForgetEverywhere),
            ApplicationName = applicationName,
            HttpClientFactory = new GoogleHttpClientFactory(),
        });

    public static Func<CancellationToken, Task<DriveService>> Factory(
        Func<Task<string>> accessToken, string applicationName, Action<string>? onRejected = null) =>
        async _ => Service(await accessToken(), applicationName, onRejected);

    /// <summary>
    /// The credential as before, plus a 401 watch: a token Drive rejects is reported once so it is not used again. The
    /// request itself still fails (no retry here); the next call takes a fresh token.
    /// </summary>
    private sealed class RejectedTokenWatch(IConfigurableHttpClientInitializer credential, string token, Action<string> onRejected)
        : IConfigurableHttpClientInitializer, IHttpUnsuccessfulResponseHandler
    {
        public void Initialize(ConfigurableHttpClient httpClient)
        {
            credential.Initialize(httpClient);
            httpClient.MessageHandler.AddUnsuccessfulResponseHandler(this);
        }

        public Task<bool> HandleResponseAsync(HandleUnsuccessfulResponseArgs args)
        {
            if (args.Response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                onRejected(token);
            }
            return Task.FromResult(false);
        }
    }
}
