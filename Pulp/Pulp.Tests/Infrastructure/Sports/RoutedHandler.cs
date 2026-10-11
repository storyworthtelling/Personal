using System.Net;
using System.Text;

namespace Pulp.Tests.Infrastructure.Sports;

/// <summary>
/// Answers by what was asked rather than in queue order, since a schedule is several requests whose
/// order is the adapter's business. A route matches when the request's path-and-query contains its
/// text; the longest matching text wins (a later route beats an earlier one of equal length), so "/teams" and "/teams?x" can both be given. Records every
/// request and never reaches a real host.
/// </summary>
public sealed class RoutedHandler : HttpMessageHandler
{
    private readonly List<(string Contains, HttpStatusCode Status, string Body)> routes = [];

    public List<RecordedRequest> Requests { get; } = [];

    public RoutedHandler Route(string contains, string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        routes.Add((contains, status, body));
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.ToDictionary(
            header => header.Key, header => string.Join(",", header.Value), StringComparer.OrdinalIgnoreCase);
        Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, headers));
        var target = request.RequestUri!.PathAndQuery;
        var match = Enumerable.Reverse(routes)
            .Where(route => target.Contains(route.Contains, StringComparison.Ordinal))
            .OrderByDescending(route => route.Contains.Length)
            .Select(route => ((string, HttpStatusCode, string)?)route)
            .FirstOrDefault();
        if (match is null)
        {
            throw new InvalidOperationException("No route was given for " + target);
        }

        return Task.FromResult(new HttpResponseMessage(match.Value.Item2)
        {
            Content = new StringContent(match.Value.Item3, Encoding.UTF8, "application/json"),
        });
    }
}
