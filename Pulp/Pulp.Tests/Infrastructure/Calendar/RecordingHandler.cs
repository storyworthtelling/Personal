using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Pulp.Tests.Infrastructure.Calendar;

/// <summary>A request with its body read before the client disposes it.</summary>
public sealed record SentRequest(HttpMethod Method, Uri Uri, string? Authorization, string Body)
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

    public JsonNode? Json => string.IsNullOrWhiteSpace(Body) ? null : JsonNode.Parse(Body);
}

/// <summary>Answers queued responses in order and records every request with its body.</summary>
public sealed class RecordingHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> responses = new();

    public List<SentRequest> Requests { get; } = [];

    public RecordingHandler Enqueue(HttpStatusCode status, string body = "{}")
    {
        responses.Enqueue(() => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        return this;
    }

    public RecordingHandler EnqueueFailure(Exception exception)
    {
        responses.Enqueue(() => throw exception);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new SentRequest(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), body));
        if (responses.Count == 0)
        {
            throw new InvalidOperationException("No fake response was queued for " + request.RequestUri!.AbsolutePath);
        }

        return responses.Dequeue()();
    }
}
