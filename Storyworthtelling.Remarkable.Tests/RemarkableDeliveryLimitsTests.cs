using System.Net;
using Microsoft.Extensions.Options;
using Storyworthtelling.Remarkable.Tests.Support;
using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Health;

namespace Storyworthtelling.Remarkable.Tests;

// The safety limits every app's delivery shares (audit 01 §11). They lived in Folio alone, so Foreword, Index,
// Constitution and Plate would hold a 173 MB report in memory twice and try again after running out.
public class RemarkableDeliveryLimitsTests
{
    [Fact]
    public void TheLimitsAreTheTabletsAndOneTimeoutForEveryHead()
    {
        Assert.Equal(100_000_000, RemarkableLimits.MaxBytes);
        Assert.Equal(TimeSpan.FromMinutes(5), RemarkableLimits.Timeout);
    }

    [Theory]
    [InlineData(100_000_000, false)]
    [InlineData(100_000_001, true)]
    public void AFileOverTheLimitIsRefusedBeforeItIsRead(long bytes, bool refused) =>
        Assert.Equal(refused, RemarkableLimits.TooLarge(bytes) is not null);

    [Fact]
    public void ARefusalSaysWhyInPlainWords()
    {
        Assert.Equal(
            "the PDF is 173 MB, over the tablet's 100 MB limit",
            RemarkableLimits.TooLarge(173_000_000)!.Sentence);
        Assert.Equal(
            "the phone ran out of memory sending it (173 MB)",
            new RemarkableRefusedException(RemarkableRefusal.OutOfMemory, 173_000_000).Sentence);
    }

    [Fact]
    public async Task AnOversizedUploadSendsNothing()
    {
        var handler = new RecordingHandler();
        var client = new RemarkableStorageClient(new HttpClient(handler), new StubAuthProvider());

        var refused = await Assert.ThrowsAsync<RemarkableRefusedException>(
            () => client.Upload("Big", new byte[100_000_001], null, CancellationToken.None));

        Assert.Equal(RemarkableRefusal.TooLarge, refused.Reason);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task RunningOutOfMemoryIsARefusalNotARetry()
    {
        var client = new RemarkableStorageClient(
            new HttpClient(new ThrowingHandler(new InvalidOperationException("Failed to allocate a 90000016 byte allocation"))),
            new StubAuthProvider());

        var refused = await Assert.ThrowsAsync<RemarkableRefusedException>(
            () => client.Upload("Report", [1, 2, 3], null, CancellationToken.None));

        Assert.Equal(RemarkableRefusal.OutOfMemory, refused.Reason);
        Assert.IsType<InvalidOperationException>(refused.InnerException);
    }

    [Fact]
    public async Task AnyOtherFailureIsLeftAsItWas()
    {
        var client = new RemarkableStorageClient(
            new HttpClient(new ThrowingHandler(new HttpRequestException("connection reset"))), new StubAuthProvider());

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.Upload("Report", [1, 2, 3], null, CancellationToken.None));
    }

    [Fact]
    public async Task TheUploadHonoursCancellation()
    {
        var client = new RemarkableStorageClient(new HttpClient(new RecordingHandler()), new StubAuthProvider());
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.Upload("Report", [1], null, cancelled.Token));
    }

    [Fact]
    public async Task TheConnectionIsOneClientForAuthAndStorageAndReportsHealth()
    {
        var handler = new RecordingHandler(RecordingHandler.Ok(Jwt()), RecordingHandler.Ok());
        var health = new RecordingSink();
        var client = RemarkableConnection.Create(
            new HttpClient(handler), Options.Create(new RemarkableOptions { DeviceToken = "device" }), health);

        await client.Upload("Report", [1], null, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("token", handler.Requests[0].RequestUri!.ToString());
        Assert.Contains("doc/v2/files", handler.Requests[1].RequestUri!.ToString());
        Assert.Equal([RemarkableCredentialId.Value], health.Ok);
    }

    [Fact]
    public async Task ARePairIsReadLiveNotFromASnapshot()
    {
        var token = "first";
        var handler = new RecordingHandler(
            RecordingHandler.Ok(Jwt()), RecordingHandler.Ok(), RecordingHandler.Ok(Jwt()), RecordingHandler.Ok());
        var client = RemarkableConnection.Create(
            new HttpClient(handler), new LiveOptions<RemarkableOptions>(() => new RemarkableOptions { DeviceToken = token }));

        await client.Upload("One", [1], null, CancellationToken.None);
        token = "second";
        await client.Upload("Two", [1], null, CancellationToken.None);

        var exchanges = handler.Requests.Where(r => r.RequestUri!.ToString().Contains("token")).ToList();
        Assert.Equal(2, exchanges.Count);
        Assert.Equal("second", exchanges[1].Headers.Authorization!.Parameter);
    }

    // A JWT whose payload expires far in the future, so the exchange is cached.
    private static string Jwt()
    {
        static string Part(string json) =>
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{Part("{\"alg\":\"none\"}")}.{Part("{\"sub\":\"u\",\"exp\":4102444800}")}.sig";
    }

    private sealed class ThrowingHandler(Exception failure) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw failure;
    }

    private sealed class RecordingSink : ICredentialHealthSink
    {
        public List<string> Ok { get; } = [];
        public void MarkOk(string credentialId) => Ok.Add(credentialId);
        public void MarkFailing(string credentialId, string message) { }
    }
}
