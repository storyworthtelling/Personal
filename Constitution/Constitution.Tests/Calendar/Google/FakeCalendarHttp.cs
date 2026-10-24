using System.Net;
using System.Text;
using Google.Apis.Calendar.v3;
using Google.Apis.Http;
using Google.Apis.Services;

namespace Constitution.Tests.Calendar.Google;

/// <summary>One request the Google client made: its method, its full URL with the query, and the body it sent.</summary>
internal sealed record SentRequest(HttpMethod Method, Uri Url, string Body)
{
    public string Query => Uri.UnescapeDataString(Url.Query);

    public string Path => Url.AbsolutePath;
}

/// <summary>
/// Google's Calendar API as the adapters meet it: a message handler under a real <see cref="CalendarService"/>, so the client library builds the
/// real URLs and reads the real JSON, and a test sees exactly what went over the wire. Replies are chosen by a function of the request; the
/// default answer is an empty page.
/// </summary>
internal sealed class FakeCalendarHttp : HttpMessageHandler
{
    public const string EmptyPage = "{\"kind\":\"calendar#events\",\"items\":[]}";

    public List<SentRequest> Requests { get; } = [];

    /// <summary>Chooses the reply for a request. Return null to answer with <see cref="EmptyPage"/>.</summary>
    public Func<SentRequest, HttpResponseMessage?> Reply { get; set; } = _ => null;

    public Exception? Throws { get; set; }

    public CalendarService Service()
    {
        var factory = new Factory(this);
        return new CalendarService(new BaseClientService.Initializer { HttpClientFactory = factory, ApplicationName = "Constitution tests" });
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Error(HttpStatusCode status) =>
        Json("{\"error\":{\"code\":" + (int)status + ",\"message\":\"The reply a test gave.\"}}", status);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await ReadBody(request.Content, cancellationToken);
        var sent = new SentRequest(request.Method, request.RequestUri!, body);
        Requests.Add(sent);
        if (Throws is not null)
        {
            throw Throws;
        }

        return Reply(sent) ?? Json(EmptyPage);
    }

    // The Google client compresses what it sends; a test reads the JSON it sent, not the bytes on the wire.
    private static async Task<string> ReadBody(HttpContent content, CancellationToken cancellationToken)
    {
        var bytes = await content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length > 2 && bytes[0] == 0x1f && bytes[1] == 0x8b)
        {
            using var zipped = new System.IO.Compression.GZipStream(new MemoryStream(bytes), System.IO.Compression.CompressionMode.Decompress);
            using var plain = new StreamReader(zipped, Encoding.UTF8);
            return await plain.ReadToEndAsync(cancellationToken);
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private sealed class Factory(HttpMessageHandler handler) : HttpClientFactory
    {
        protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) => handler;
    }
}
