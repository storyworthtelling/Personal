namespace Codex.Tests;

/// <summary>
/// HttpMessageHandler returning canned responses keyed by absolute URL,
/// recording every request for assertions.
/// </summary>
public class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpResponseMessage>> responses = [];

    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>
    /// Each request's body, captured while the request is still alive and keyed by its
    /// index in <see cref="Requests"/>. A client that disposes its HttpRequestMessage —
    /// which a well-behaved one does — takes the content with it, so reading the body off
    /// a recorded request afterwards throws ObjectDisposedException.
    /// </summary>
    public List<string> Bodies { get; } = [];

    public void Respond(string url, string body, string mediaType = "application/json",
        System.Net.HttpStatusCode status = System.Net.HttpStatusCode.OK)
    {
        responses[url] = () => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, mediaType),
        };
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken));

        var url = request.RequestUri!.AbsoluteUri;
        if (!responses.TryGetValue(url, out var factory))
        {
            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
            {
                Content = new StringContent($"No canned response for {url}"),
            };
        }
        return factory();
    }
}
