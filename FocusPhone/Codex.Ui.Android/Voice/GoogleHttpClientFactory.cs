using global::Google.Apis.Http;

namespace Codex.Ui.Voice;

/// <summary>
/// Routes the Google API client's own HttpClient through <c>AndroidMessageHandler</c>.
/// Android's SELinux policy blocks the raw sockets the default <c>SocketsHttpHandler</c>
/// opens in an app process, so the Google SDK transport must use the Java
/// HttpsURLConnection layer like every other client in the suite.
///
/// Shared, because it had been copied into five places by the time a sixth needed it — and
/// a transport detail that every Drive caller must get right is exactly the kind of thing
/// that should exist once. The existing copies are private nested classes and are left
/// where they are; folding them in is a cleanup of its own, not a rider on this one.
/// </summary>
public sealed class GoogleHttpClientFactory : HttpClientFactory
{
    protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) =>
        new Xamarin.Android.Net.AndroidMessageHandler();
}
