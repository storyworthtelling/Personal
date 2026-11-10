using System.Collections.Concurrent;
using Xamarin.Android.Net;

namespace Codex.Ui.Http;

/// <summary>
/// The one place a native head gets an HttpClient (audit 01 §19). A client is kept per timeout for the life of the
/// process, always on Android's own message handler, so no call site leaks sockets by building one per request or
/// lands on the default handler by leaving it out.
/// <para>
/// A client handed out here is shared: never dispose it and never change its <c>DefaultRequestHeaders</c>,
/// <c>BaseAddress</c> or <c>Timeout</c>. Put a per-call header on the request message instead.
/// </para>
/// </summary>
public static class HeadHttp
{
    /// <summary>HttpClient's own default, kept for the calls that never set one.</summary>
    public static readonly TimeSpan StandardTimeout = TimeSpan.FromSeconds(100);

    private static readonly ConcurrentDictionary<TimeSpan, HttpClient> Clients = new();

    /// <summary>The shared client at the standard timeout.</summary>
    public static HttpClient Shared => WithTimeout(StandardTimeout);

    /// <summary>The shared client for this timeout, made on first use.</summary>
    public static HttpClient WithTimeout(TimeSpan timeout) =>
        Clients.GetOrAdd(timeout, static t => new HttpClient(new AndroidMessageHandler()) { Timeout = t });
}
