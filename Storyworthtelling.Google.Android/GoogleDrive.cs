using global::Google.Apis.Auth.OAuth2;
using global::Google.Apis.Drive.v3;
using global::Google.Apis.Services;

namespace Storyworthtelling.Google.Android;

/// <summary>
/// The one way a head builds a Drive service (audit 01 §8): over its device-account token, on the Android HTTP
/// stack. There were nineteen <c>new DriveService(</c> sites.
/// </summary>
public static class GoogleDrive
{
    public static DriveService Service(string accessToken, string applicationName) =>
        new(new BaseClientService.Initializer
        {
            HttpClientInitializer = GoogleCredential.FromAccessToken(accessToken),
            ApplicationName = applicationName,
            HttpClientFactory = new GoogleHttpClientFactory(),
        });

    /// <summary>A fresh, disposable service per call: the factory shape every Drive seam takes.</summary>
    public static Func<CancellationToken, Task<DriveService>> Factory(Func<Task<string>> accessToken, string applicationName) =>
        async _ => Service(await accessToken(), applicationName);
}
