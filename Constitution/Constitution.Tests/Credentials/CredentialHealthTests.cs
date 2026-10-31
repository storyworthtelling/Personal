using System.Net;
using Constitution.Domain.Credentials;
using Constitution.Infrastructure.Credentials;
using Storyworthtelling.Remarkable;
using Storyworthtelling.Voice.Health;

namespace Constitution.Tests.Credentials;

/// <summary>
/// C-8.2.3 — a rejected key or login shows itself. Only an auth rejection flags a credential;
/// throttling, server trouble and network failures never do, or Settings would reveal a field for a
/// key that is fine.
/// </summary>
public class CredentialHealthTests
{
    private sealed class FakeSink : ICredentialHealthSink
    {
        public List<(string Id, string? Message)> Ok { get; } = new();
        public List<(string Id, string? Message)> Failing { get; } = new();
        public void MarkOk(string credentialId) => Ok.Add((credentialId, null));
        public void MarkFailing(string credentialId, string message) => Failing.Add((credentialId, message));
    }

    private sealed class FakeInner : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> respond;
        public FakeInner(Func<HttpResponseMessage> respond) => this.respond = respond;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond());
    }

    private sealed class ThrowingInner : HttpMessageHandler
    {
        private readonly Exception error;
        public ThrowingInner(Exception error) => this.error = error;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw error;
    }

    private static HttpMessageInvoker Invoker(string id, ICredentialHealthSink? sink, HttpMessageHandler inner) =>
        new(new CredentialReportingHandler(id, sink) { InnerHandler = inner });

    private static Task<HttpResponseMessage> Call(HttpMessageInvoker invoker) =>
        invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://example.com/api"), CancellationToken.None);

    private static HttpResponseMessage Reply(HttpStatusCode status, string body = "") =>
        new(status) { Content = new StringContent(body) };

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task OnlyAuthRejectionsFlagRejectionFlags(HttpStatusCode status)
    {
        var sink = new FakeSink();
        await Call(Invoker(CredentialIds.SleepNumber, sink, new FakeInner(() => Reply(status))));
        var failure = Assert.Single(sink.Failing);
        Assert.Equal("sleep-number", failure.Id);
        Assert.Contains(((int)status).ToString(), failure.Message);
        Assert.Empty(sink.Ok);
    }

    [Fact]
    public async Task GeminiInvalidKeyIn400Flags()
    {
        var sink = new FakeSink();
        var invoker = Invoker(CredentialIds.Gemini, sink,
            new FakeInner(() => Reply(HttpStatusCode.BadRequest, "{\"error\":{\"status\":\"INVALID_ARGUMENT\",\"details\":[{\"reason\":\"API_KEY_INVALID\"}]}}")));
        var response = await Call(invoker);
        Assert.Equal("gemini", Assert.Single(sink.Failing).Id);
        // The body must still be readable by the real caller after the handler peeked at it.
        Assert.Contains("API_KEY_INVALID", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OnlyAuthRejectionsFlagOrdinaryBadRequestDoesNot()
    {
        var sink = new FakeSink();
        await Call(Invoker(CredentialIds.Gemini, sink, new FakeInner(() => Reply(HttpStatusCode.BadRequest, "missing field"))));
        Assert.Empty(sink.Failing);
        Assert.Empty(sink.Ok);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task OnlyAuthRejectionsFlagThrottlingAndServerTroubleDoNot(HttpStatusCode status)
    {
        var sink = new FakeSink();
        var response = await Call(Invoker(CredentialIds.Claude, sink, new FakeInner(() => Reply(status))));
        Assert.Equal(status, response.StatusCode);
        Assert.Empty(sink.Failing);
        Assert.Empty(sink.Ok);
    }

    [Fact]
    public async Task OnlyAuthRejectionsFlagTimeoutPropagatesAndDoesNotFlag()
    {
        var sink = new FakeSink();
        var invoker = Invoker(CredentialIds.Claude, sink, new ThrowingInner(new TaskCanceledException("timed out")));
        await Assert.ThrowsAsync<TaskCanceledException>(() => Call(invoker));
        Assert.Empty(sink.Failing);
        Assert.Empty(sink.Ok);
    }

    [Fact]
    public async Task OnlyAuthRejectionsFlagNetworkFailurePropagatesUnchangedAndDoesNotFlag()
    {
        var sink = new FakeSink();
        var original = new HttpRequestException("Name or service not known", new System.Net.Sockets.SocketException(11001));
        var invoker = Invoker(CredentialIds.Remarkable, sink, new ThrowingInner(original));
        var thrown = await Assert.ThrowsAsync<HttpRequestException>(() => Call(invoker));
        Assert.Same(original, thrown);
        Assert.Empty(sink.Failing);
        Assert.Empty(sink.Ok);
    }

    [Fact]
    public async Task OnlyAuthRejectionsFlagSuccessMarksWorking()
    {
        var sink = new FakeSink();
        await Call(Invoker(CredentialIds.Claude, sink, new FakeInner(() => Reply(HttpStatusCode.OK, "{}"))));
        Assert.Equal("claude", Assert.Single(sink.Ok).Id);
        Assert.Empty(sink.Failing);
    }

    [Fact]
    public async Task EachHandlerReportsUnderItsOwnId()
    {
        var sink = new FakeSink();
        await Call(Invoker(CredentialIds.Claude, sink, new FakeInner(() => Reply(HttpStatusCode.Unauthorized))));
        await Call(Invoker(CredentialIds.Gemini, sink, new FakeInner(() => Reply(HttpStatusCode.OK))));
        Assert.Equal("claude", Assert.Single(sink.Failing).Id);
        Assert.Equal("gemini", Assert.Single(sink.Ok).Id);
    }

    [Fact]
    public async Task ANullSinkDoesNotThrow()
    {
        var rejected = await Call(Invoker(CredentialIds.Claude, null, new FakeInner(() => Reply(HttpStatusCode.Unauthorized))));
        var fine = await Call(Invoker(CredentialIds.Claude, null, new FakeInner(() => Reply(HttpStatusCode.OK))));
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.OK, fine.StatusCode);
    }

    [Fact]
    public void ABlankIdIsRefused() =>
        Assert.Throws<ArgumentException>(() => new CredentialReportingHandler(" ", null));

    [Fact]
    public void TheIdsArePinned()
    {
        Assert.Equal("sleep-number", CredentialIds.SleepNumber);
        Assert.Equal("remarkable", CredentialIds.Remarkable);
        Assert.Equal("claude", CredentialIds.Claude);
        Assert.Equal("gemini", CredentialIds.Gemini);
        // The reMarkable id is the shared one, not a second spelling of it.
        Assert.Equal(RemarkableCredentialId.Value, CredentialIds.Remarkable);
    }

    [Theory]
    [InlineData(CredentialIds.SleepNumber)]
    [InlineData(CredentialIds.Remarkable)]
    [InlineData(CredentialIds.Claude)]
    [InlineData(CredentialIds.Gemini)]
    public async Task SettingsShowsAFieldOnlyWhenMissingOrJustRejected(string id)
    {
        var now = DateTimeOffset.UnixEpoch;
        // Missing: shown. Set and never used: hidden. Set and working: hidden.
        Assert.True(CredentialFieldState.Decide(false, CredentialHealth.Unknown).ShowEntry);
        Assert.False(CredentialFieldState.Decide(true, CredentialHealth.Unknown).ShowEntry);
        Assert.False(CredentialFieldState.Decide(true, CredentialHealth.Ok(now)).ShowEntry);

        // Rejected through the real handler, then read back as a store would: shown, as a warning.
        var sink = new FakeSink();
        await Call(Invoker(id, sink, new FakeInner(() => Reply(HttpStatusCode.Forbidden))));
        var failure = Assert.Single(sink.Failing);
        Assert.Equal(id, failure.Id);
        var state = CredentialFieldState.Decide(true, CredentialHealth.Failing(failure.Message!, now));
        Assert.True(state.ShowEntry);
        Assert.True(state.IsWarning);

        // Throttled: nothing was reported, so a set key stays hidden.
        var quiet = new FakeSink();
        await Call(Invoker(id, quiet, new FakeInner(() => Reply(HttpStatusCode.TooManyRequests))));
        Assert.Empty(quiet.Failing);
        Assert.False(CredentialFieldState.Decide(true, CredentialHealth.Unknown).ShowEntry);
    }
}
