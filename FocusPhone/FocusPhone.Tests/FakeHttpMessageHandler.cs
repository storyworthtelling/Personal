namespace FocusPhone.Tests;

/// <summary>
/// HttpMessageHandler returning canned responses keyed by absolute URL,
/// recording every request for assertions.
/// </summary>
public class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpResponseMessage>> responses = [];

    public List<HttpRequestMessage> Requests { get; } = [];

    public void Respond(string url, string body, string mediaType = "application/json",
        System.Net.HttpStatusCode status = System.Net.HttpStatusCode.OK)
    {
        responses[url] = () => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, mediaType),
        };
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var url = request.RequestUri!.AbsoluteUri;
        if (!responses.TryGetValue(url, out var factory))
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
            {
                Content = new StringContent($"No canned response for {url}"),
            });
        }
        return Task.FromResult(factory());
    }
}
