using global::Google.Apis.Http;

namespace Storyworthtelling.Google.Android;

/// <summary>
/// Routes the Google API client's own HttpClient through <c>AndroidMessageHandler</c>. Android's SELinux policy
/// blocks the raw sockets the default <c>SocketsHttpHandler</c> opens in an app process, so the Google SDK transport
/// must use the Java HttpsURLConnection layer. One copy (audit 01 §8); there were twelve.
///
/// <para>One handler for the whole process (efficiency audit §3, §16): every Drive operation builds and disposes a
/// <c>DriveService</c>, and each used to bring a handler of its own. They now share one, behind a wrapper that a
/// disposed service cannot dispose, so connections are reused across operations.</para>
/// </summary>
public sealed class GoogleHttpClientFactory : HttpClientFactory
{
    private static readonly Xamarin.Android.Net.AndroidMessageHandler Transport = new();

    protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) => new KeptOpen(Transport);

    /// <summary>Passes every request to the shared transport and leaves it open when the service is disposed.</summary>
    private sealed class KeptOpen(HttpMessageHandler transport) : DelegatingHandler(transport)
    {
        protected override void Dispose(bool disposing)
        {
            // Deliberately not base.Dispose: that would dispose the process's one transport.
        }
    }
}
