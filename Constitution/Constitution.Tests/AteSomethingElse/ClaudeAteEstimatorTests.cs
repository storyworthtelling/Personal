using System.Net;
using System.Text;
using System.Text.Json;
using Constitution.Domain.Credentials;
using Constitution.Domain.Meals;
using Constitution.Infrastructure.Meals;
using Microsoft.Extensions.Options;
using Moq;
using Storyworthtelling.Handwriting;
using Storyworthtelling.Voice.Health;
using static Constitution.Tests.AteSomethingElse.AteFixture;

namespace Constitution.Tests.AteSomethingElse;

/// <summary>The real estimate over the Messages API against a stand-in: exactly the photo and the fixed question leave, nothing else.</summary>
public class ClaudeAteEstimatorTests
{
    private sealed class Handler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string Body { get; private set; } = string.Empty;

        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Request = request;
            Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request, Body);
        }
    }

    // A minimal JPEG: start, an EXIF block carrying a place, a frame header, the scan, the end.
    private static readonly byte[] Photo =
    [
        0xFF, 0xD8,
        0xFF, 0xE1, 0x00, 0x0A, 0x45, 0x78, 0x69, 0x66, 0x00, 0x00, 0x47, 0x50,
        0xFF, 0xDB, 0x00, 0x04, 0x01, 0x02,
        0xFF, 0xDA, 0x00, 0x04, 0x03, 0x04, 0x11, 0x22,
        0xFF, 0xD9,
    ];

    private const string GoodReply =
        "{\"is_meal\":true,\"food\":\"pasta with tomato sauce\",\"kcal_low\":750,\"kcal_high\":1050,\"protein_g_low\":25,\"protein_g_high\":45,\"confidence\":\"fair\"}";

    private static HttpResponseMessage Reply(string text) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { content = new[] { new { type = "text", text } } }), Encoding.UTF8, "application/json"),
    };

    private static (ClaudeAteEstimator Estimator, Handler Handler, Mock<ICredentialHealthSink> Health) Make(
        Func<HttpRequestMessage, string, HttpResponseMessage> respond, string key = "sample-test-value", Func<string, byte[]>? reader = null)
    {
        var handler = new Handler(respond);
        var health = new Mock<ICredentialHealthSink>();
        var options = Options.Create(new ClaudeVisionOptions { ApiKey = key, Model = "test-model" });
        return (new ClaudeAteEstimator(new HttpClient(handler), options, reader ?? (_ => Photo), health.Object), handler, health);
    }

    [Fact]
    public async Task ItSendsThePhotoAndTheFixedQuestionAndNothingElse()
    {
        var (estimator, handler, _) = Make((_, _) => Reply(GoodReply));

        var outcome = await estimator.EstimateAsync("ignored.jpg", CancellationToken.None);

        Assert.IsType<AteOutcome.Read>(outcome);
        using var body = JsonDocument.Parse(handler.Body);
        var root = body.RootElement;
        Assert.Equal("test-model", root.GetProperty("model").GetString());
        Assert.Equal(AtePrompt.System, root.GetProperty("system")[0].GetProperty("text").GetString());
        var content = root.GetProperty("messages")[0].GetProperty("content");
        Assert.Equal(2, content.GetArrayLength());
        Assert.Equal("image", content[0].GetProperty("type").GetString());
        Assert.Equal("image/jpeg", content[0].GetProperty("source").GetProperty("media_type").GetString());
        Assert.Equal("text", content[1].GetProperty("type").GetString());
        Assert.Equal(AtePrompt.Question, content[1].GetProperty("text").GetString());
        Assert.Equal(["max_tokens", "messages", "model", "system"], root.EnumerateObject().Select(property => property.Name).Order().ToArray());
    }

    [Fact]
    public async Task TheKeyIsInTheHeaderOnly()
    {
        var (estimator, handler, _) = Make((_, _) => Reply(GoodReply));

        await estimator.EstimateAsync("ignored.jpg", CancellationToken.None);

        Assert.Equal("sample-test-value", Assert.Single(handler.Request!.Headers.GetValues("x-api-key")));
        Assert.DoesNotContain("sample-test-value", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoKeyIsUnavailableAndNothingIsSent()
    {
        var read = 0;
        var (estimator, handler, _) = Make((_, _) => Reply(GoodReply), key: " ", reader: _ =>
        {
            read++;
            return Photo;
        });

        var outcome = await estimator.EstimateAsync("ignored.jpg", CancellationToken.None);

        Assert.Equal(AteText.NoKeySentence, Assert.IsType<AteOutcome.Unavailable>(outcome).Sentence);
        Assert.Equal(0, handler.Calls);
        Assert.Equal(0, read);
    }

    [Fact]
    public async Task LocationIsStrippedFromThePhotoBeforeItLeaves()
    {
        var (estimator, handler, _) = Make((_, _) => Reply(GoodReply));

        await estimator.EstimateAsync("ignored.jpg", CancellationToken.None);

        using var body = JsonDocument.Parse(handler.Body);
        var sent = Convert.FromBase64String(body.RootElement.GetProperty("messages")[0].GetProperty("content")[0].GetProperty("source").GetProperty("data").GetString()!);
        Assert.False(ContainsRun(sent, [0x45, 0x78, 0x69, 0x66]), "the EXIF block went out with the photo");
        Assert.Equal(0xFF, sent[0]);
        Assert.Equal(0xD8, sent[1]);
    }

    private static bool ContainsRun(byte[] haystack, byte[] needle) =>
        Enumerable.Range(0, haystack.Length - needle.Length + 1).Any(start => haystack.AsSpan(start, needle.Length).SequenceEqual(needle));

    [Fact]
    public async Task APhotoThatIsNotAJpegIsNeverSent()
    {
        var (estimator, handler, _) = Make((_, _) => Reply(GoodReply), reader: _ => [1, 2, 3, 4]);

        var outcome = await estimator.EstimateAsync("ignored.jpg", CancellationToken.None);

        Assert.IsType<AteOutcome.Unreadable>(outcome);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task AnOversizedPhotoIsNeverSent()
    {
        var huge = new byte[ClaudeAteEstimator.LargestPhotoBytes + 1];
        huge[0] = 0xFF;
        huge[1] = 0xD8;
        var (estimator, handler, _) = Make((_, _) => Reply(GoodReply), reader: _ => huge);

        var outcome = await estimator.EstimateAsync("ignored.jpg", CancellationToken.None);

        Assert.IsType<AteOutcome.Unreadable>(outcome);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task ASenselessReplyIsCouldNotEstimate()
    {
        var (estimator, _, _) = Make((_, _) => Reply("Sorry, I can't do that."));

        Assert.IsType<AteOutcome.Unreadable>(await estimator.EstimateAsync("ignored.jpg", CancellationToken.None));
    }

    [Fact]
    public async Task ARefusalIsAFailureThatKeepsItsStatus()
    {
        var (estimator, _, health) = Make((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"error\":{\"message\":\"invalid x-api-key\"}}"),
        });

        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => estimator.EstimateAsync("ignored.jpg", CancellationToken.None));

        Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
        health.Verify(sink => sink.MarkFailing(CredentialIds.Claude, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ThrottlingDoesNotFlagTheKey()
    {
        var (estimator, _, health) = Make((_, _) => new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("{}") });

        await Assert.ThrowsAsync<HttpRequestException>(() => estimator.EstimateAsync("ignored.jpg", CancellationToken.None));

        health.Verify(sink => sink.MarkFailing(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AGoodCallMarksTheKeyWorking()
    {
        var (estimator, _, health) = Make((_, _) => Reply(GoodReply));

        await estimator.EstimateAsync("ignored.jpg", CancellationToken.None);

        health.Verify(sink => sink.MarkOk(CredentialIds.Claude), Times.Once);
    }

    [Fact]
    public void TheEstimatorHoldsNoPhotoAfterTheCall()
    {
        var held = typeof(ClaudeAteEstimator).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
            .Select(field => field.FieldType)
            .Concat(typeof(ClaudeAteEstimator).GetProperties().Select(property => property.PropertyType))
            .ToList();

        Assert.DoesNotContain(held, type => type == typeof(byte[]) || typeof(Stream).IsAssignableFrom(type) || type == typeof(Uri));
    }

    [Fact]
    public void ScrubKeepsThePictureAndDropsTheMetadata()
    {
        var scrubbed = JpegScrub.WithoutMetadata(Photo)!;

        Assert.False(ContainsRun(scrubbed, [0x45, 0x78, 0x69, 0x66]));
        Assert.True(ContainsRun(scrubbed, [0xFF, 0xDB]));
        Assert.True(ContainsRun(scrubbed, [0xFF, 0xDA, 0x00, 0x04, 0x03, 0x04, 0x11, 0x22]));
        Assert.Equal(0xD9, scrubbed[^1]);
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0xFF })]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47 })]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF })]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE1, 0xFF, 0xFF, 0x00 })]
    public void ScrubRefusesWhatIsNotAWholeJpeg(byte[] bytes)
    {
        Assert.Null(JpegScrub.WithoutMetadata(bytes));
    }
}
