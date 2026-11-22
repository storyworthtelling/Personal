using System.Net;
using Codex.Infrastructure.Music;
using Xunit;

namespace Codex.Tests.Music;

/// <summary>
/// A refused range read has to say what Drive answered: TagLib rethrows whatever the stream threw wrapped in
/// its own exception, and the status is lost unless the stream keeps it.
/// </summary>
public class DriveSeekableStreamTests
{
    [Fact]
    public void ARefusedReadThrowsTheStatusAndKeepsIt()
    {
        var handler = new RangeHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        using var stream = new DriveSeekableStream(
            "file-one", 100, () => Task.FromResult("token-a"), _ => { }, new HttpClient(handler));

        var thrown = Assert.Throws<DriveReadException>(() => stream.Read(new byte[10], 0, 10));

        Assert.Equal(403, thrown.Status);
        Assert.Same(thrown, stream.LastFailure);
        Assert.DoesNotContain("file-one", thrown.Message);
    }

    [Fact]
    public void AGoodReadReturnsTheBytes()
    {
        var handler = new RangeHandler(_ => Partial([1, 2, 3, 4]));
        using var stream = new DriveSeekableStream(
            "file-one", 4, () => Task.FromResult("token-a"), _ => { }, new HttpClient(handler));

        var buffer = new byte[4];
        Assert.Equal(4, stream.Read(buffer, 0, 4));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, buffer);
        Assert.Null(stream.LastFailure);
    }

    [Fact]
    public void ARejectedTokenIsForgottenAndTheRangeAskedAgainWithAFreshOne()
    {
        var handler = new RangeHandler(call => call == 1
            ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
            : Partial([5, 6, 7]));
        var issued = new Queue<string>(["token-a", "token-b"]);
        var forgotten = new List<string>();
        using var stream = new DriveSeekableStream(
            "file-one", 3, () => Task.FromResult(issued.Dequeue()), forgotten.Add, new HttpClient(handler));

        var buffer = new byte[3];
        Assert.Equal(3, stream.Read(buffer, 0, 3));

        Assert.Equal(new byte[] { 5, 6, 7 }, buffer);
        Assert.Equal(["token-a"], forgotten);
        Assert.Equal(["token-a", "token-b"], handler.Tokens);
        Assert.Null(stream.LastFailure);
    }

    [Fact]
    public void ASecondRejectionThrowsUnauthorized()
    {
        var handler = new RangeHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var issued = new Queue<string>(["token-a", "token-b"]);
        var forgotten = new List<string>();
        using var stream = new DriveSeekableStream(
            "file-one", 3, () => Task.FromResult(issued.Dequeue()), forgotten.Add, new HttpClient(handler));

        var thrown = Assert.Throws<DriveReadException>(() => stream.Read(new byte[3], 0, 3));

        Assert.Equal(401, thrown.Status);
        Assert.Equal(2, handler.Tokens.Count);
        Assert.Equal(["token-a", "token-b"], forgotten);
    }

    internal static HttpResponseMessage Partial(byte[] bytes) =>
        new(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(bytes) };

    /// <summary>Answers each request through a function and records the bearer token it carried.</summary>
    internal sealed class RangeHandler(Func<int, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public List<string?> Tokens { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Tokens.Add(request.Headers.Authorization?.Parameter);
            return Task.FromResult(reply(Tokens.Count));
        }
    }
}
