using System.Net;
using System.Text;

namespace Codex.Tests.Support;

/// <summary>
/// Returns queued responses in order and records each request's body, so a test can drive a typed
/// HttpClient through one or more calls and assert exactly what was sent. (The repo's other fake handler is
/// URL-keyed and doesn't capture bodies; the Claude clients need the body asserted.)
/// </summary>
public sealed class QueuedHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> responses = new();

    public List<HttpRequestMessage> Requests { get; } = [];
    public List<string> RequestBodies { get; } = [];

    public QueuedHttpMessageHandler Enqueue(HttpStatusCode status, string body, string mediaType = "application/json")
    {
        responses.Enqueue(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType)
        });
        return this;
    }

    public QueuedHttpMessageHandler EnqueueOk(string body) => Enqueue(HttpStatusCode.OK, body);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        RequestBodies.Add(request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken));

        if (responses.Count == 0)
        {
            throw new InvalidOperationException("No fake HTTP response was queued for this request.");
        }

        return responses.Dequeue();
    }
}
