using global::Google.Apis.Http;

namespace Storyworthtelling.Google.Android;

/// <summary>
/// Routes the Google API client's own HttpClient through <c>AndroidMessageHandler</c>. Android's SELinux policy
/// blocks the raw sockets the default <c>SocketsHttpHandler</c> opens in an app process, so the Google SDK transport
/// must use the Java HttpsURLConnection layer. One copy (audit 01 §8); there were twelve.
/// </summary>
public sealed class GoogleHttpClientFactory : HttpClientFactory
{
    protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) =>
        new Xamarin.Android.Net.AndroidMessageHandler();
}
