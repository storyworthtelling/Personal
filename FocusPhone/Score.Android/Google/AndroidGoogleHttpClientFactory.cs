using global::Google.Apis.Http;

namespace Score.Google;

// Routes the Google API client's own HttpClient through AndroidMessageHandler. Android's SELinux
// policy blocks the raw sockets the default SocketsHttpHandler opens in an app process, so the
// Google SDK transport must use the Java HttpsURLConnection layer like the app's other clients.
public sealed class AndroidGoogleHttpClientFactory : HttpClientFactory
{
    protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) =>
        new Xamarin.Android.Net.AndroidMessageHandler();
}
