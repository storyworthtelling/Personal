namespace Codex.Tests;

/// <summary>
/// HttpMessageHandler returning canned responses keyed by absolute URL,
/// recording every request for assertions.
/// </summary>
public class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpResponseMessage>> responses = [];
    private readonly Dictionary<string, Queue<Func<HttpResponseMessage>>> sequences = [];

    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>Each request's body, read eagerly here because the client typically
    /// disposes its <see cref="HttpRequestMessage"/> (and its content with it) before
    /// a test gets to assert on it. Null for a request with no content.</summary>
    public List<string?> RequestBodies { get; } = [];

    public void Respond(string url, string body, string mediaType = "application/json",
        System.Net.HttpStatusCode status = System.Net.HttpStatusCode.OK)
    {
        responses[url] = () => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, mediaType),
        };
    }

    /// <summary>Queues one response per call to the same URL, in order — for a client
    /// that polls the same endpoint and is expected to see it change over time.</summary>
    public void RespondSequence(string url, params (string Body, System.Net.HttpStatusCode Status)[] replies)
    {
        sequences[url] = new Queue<Func<HttpResponseMessage>>(replies.Select(reply =>
            (Func<HttpResponseMessage>)(() => new HttpResponseMessage(reply.Status)
            {
                Content = new StringContent(reply.Body, System.Text.Encoding.UTF8, "application/json"),
            })));
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        RequestBodies.Add(request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken));

        var url = request.RequestUri!.AbsoluteUri;

        if (sequences.TryGetValue(url, out var queue) && queue.Count > 0)
        {
            return queue.Dequeue()();
        }

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
