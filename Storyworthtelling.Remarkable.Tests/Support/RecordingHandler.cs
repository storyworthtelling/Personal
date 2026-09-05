namespace Storyworthtelling.Remarkable.Tests.Support;

// A test HttpMessageHandler that records every request (and its body) and returns queued responses in order,
// so a multi-call flow (list-then-create) can assert exactly what was sent.
public sealed class RecordingHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> responses;

    public List<HttpRequestMessage> Requests { get; } = [];
    public List<string> Bodies { get; } = [];

    public RecordingHandler(params HttpResponseMessage[] responses)
    {
        this.responses = new Queue<HttpResponseMessage>(responses);
    }

    public static HttpResponseMessage Ok(string body = "{}") =>
        new(System.Net.HttpStatusCode.OK) { Content = new StringContent(body) };

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
        return responses.Count > 0 ? responses.Dequeue() : RecordingHandler.Ok();
    }
}

// A fixed user token, so the client tests don't exercise the auth exchange.
public sealed class StubAuthProvider(string token = "fake-user-token") : IRemarkableAuthProvider
{
    public Task<string> GetUserToken() => Task.FromResult(token);
}
