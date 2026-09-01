using System.Net;

namespace Storyworthtelling.Google.Tests.Support;

// A small recording HttpMessageHandler: hand it a canned response, drive an HttpClient through it, and read
// back the requested URLs. Mirrors the hand-rolled fake in the other Storyworthtelling.* test projects.
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode status;
    private readonly string body;

    public List<Uri> Requests { get; } = new();

    public FakeHttpMessageHandler(HttpStatusCode status, string body)
    {
        this.status = status;
        this.body = body;
    }

    public static FakeHttpMessageHandler Ok(string body) => new(HttpStatusCode.OK, body);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is not null)
            Requests.Add(request.RequestUri);
        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body),
        });
    }
}
