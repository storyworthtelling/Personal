using System.Net;
using System.Text;

namespace Pulp.Tests.Infrastructure;

/// <summary>A request as it was sent: what a test asserts on after the client has disposed the message.</summary>
public sealed record RecordedRequest(HttpMethod Method, Uri Uri, IReadOnlyDictionary<string, string> Headers)
{
    public string? Query(string name)
    {
        foreach (var pair in Uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (Uri.UnescapeDataString(parts[0]) == name)
            {
                return parts.Length == 2 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : string.Empty;
            }
        }

        return null;
    }
}

/// <summary>Returns queued responses in order and records every request, so no test ever reaches a real host.</summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> responses = new();

    public List<RecordedRequest> Requests { get; } = [];

    public FakeHttpMessageHandler Enqueue(HttpStatusCode status, string body, string? location = null)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (location is not null)
        {
            response.Headers.Location = new Uri(location);
        }

        responses.Enqueue(response);
        return this;
    }

    public FakeHttpMessageHandler EnqueueOk(string body) => Enqueue(HttpStatusCode.OK, body);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.ToDictionary(
            header => header.Key, header => string.Join(",", header.Value), StringComparer.OrdinalIgnoreCase);
        Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, headers));
        if (responses.Count == 0)
        {
            throw new InvalidOperationException("No fake response was queued for " + request.RequestUri!.AbsolutePath);
        }

        return Task.FromResult(responses.Dequeue());
    }
}
