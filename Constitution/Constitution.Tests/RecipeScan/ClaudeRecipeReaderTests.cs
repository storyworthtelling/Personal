using System.Net;
using System.Text;
using System.Text.Json;
using Constitution.Domain.Credentials;
using Constitution.Domain.Recipes.Screens;
using Constitution.Infrastructure.Recipes;
using Microsoft.Extensions.Options;
using Moq;
using Storyworthtelling.Handwriting;
using Storyworthtelling.Voice.Health;

namespace Constitution.Tests.RecipeScan;

/// <summary>The recipe read over the Messages API against a stand-in (D176): the pages, in order, and the fixed words; nothing else.</summary>
public class ClaudeRecipeReaderTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string Body { get; private set; } = string.Empty;

        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Request = request;
            Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }

    // A minimal JPEG with an EXIF block (a place), a frame header, the scan and the end; the last scan byte tells pages apart.
    private static byte[] Page(byte mark) =>
    [
        0xFF, 0xD8,
        0xFF, 0xE1, 0x00, 0x0A, 0x45, 0x78, 0x69, 0x66, 0x00, 0x00, 0x47, 0x50,
        0xFF, 0xDB, 0x00, 0x04, 0x01, 0x02,
        0xFF, 0xDA, 0x00, 0x04, 0x03, 0x04, 0x11, mark,
        0xFF, 0xD9,
    ];

    private const string GoodReply =
        "{\"is_recipe\":true,\"title\":\"Lemon Lentil Soup\",\"ingredients\":[\"1 cup red lentils\"],\"steps\":[\"Simmer for 20 minutes.\"]}";

    private static HttpResponseMessage Reply(string text, string stop = "end_turn") => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(new { content = new[] { new { type = "text", text } }, stop_reason = stop }),
            Encoding.UTF8,
            "application/json"),
    };

    private static (ClaudeRecipeReader Reader, Handler Handler, Mock<ICredentialHealthSink> Health) Make(
        Func<HttpRequestMessage, HttpResponseMessage> respond, string key = "sample-test-value")
    {
        var handler = new Handler(respond);
        var health = new Mock<ICredentialHealthSink>();
        var options = Options.Create(new ClaudeVisionOptions { ApiKey = key, Model = "test-model" });
        return (new ClaudeRecipeReader(new HttpClient(handler), options, health.Object), handler, health);
    }

    [Fact]
    public async Task ItSendsEveryPageInOrderThenTheQuestion()
    {
        var (reader, handler, _) = Make(_ => Reply(GoodReply));

        var read = await reader.ReadAsync([Page(0x21), Page(0x22)], CancellationToken.None);

        Assert.Equal("Lemon Lentil Soup", Assert.IsType<RecipePhotoRead.Read>(read).Payload.Title);
        using var body = JsonDocument.Parse(handler.Body);
        var root = body.RootElement;
        Assert.Equal("test-model", root.GetProperty("model").GetString());
        Assert.Equal(RecipePhotoPrompt.System, root.GetProperty("system")[0].GetProperty("text").GetString());
        var content = root.GetProperty("messages")[0].GetProperty("content");
        Assert.Equal(3, content.GetArrayLength());
        Assert.Equal(0x21, PageAt(content, 0)[^3]);
        Assert.Equal(0x22, PageAt(content, 1)[^3]);
        Assert.Equal(RecipePhotoPrompt.Question(2), content[2].GetProperty("text").GetString());
        Assert.Equal(["max_tokens", "messages", "model", "system"], root.EnumerateObject().Select(property => property.Name).Order().ToArray());
    }

    private static byte[] PageAt(JsonElement content, int index)
    {
        Assert.Equal("image", content[index].GetProperty("type").GetString());
        Assert.Equal("image/jpeg", content[index].GetProperty("source").GetProperty("media_type").GetString());
        return Convert.FromBase64String(content[index].GetProperty("source").GetProperty("data").GetString()!);
    }

    [Fact]
    public async Task LocationIsStrippedFromEveryPageBeforeItLeaves()
    {
        var (reader, handler, _) = Make(_ => Reply(GoodReply));

        await reader.ReadAsync([Page(1), Page(2)], CancellationToken.None);

        using var body = JsonDocument.Parse(handler.Body);
        var content = body.RootElement.GetProperty("messages")[0].GetProperty("content");
        foreach (var index in new[] { 0, 1 })
        {
            var sent = PageAt(content, index);
            Assert.False(ContainsRun(sent, [0x45, 0x78, 0x69, 0x66]), "the EXIF block went out with a page");
        }
    }

    private static bool ContainsRun(byte[] haystack, byte[] needle) =>
        Enumerable.Range(0, haystack.Length - needle.Length + 1).Any(start => haystack.AsSpan(start, needle.Length).SequenceEqual(needle));

    [Fact]
    public async Task TheKeyIsInTheHeaderOnly()
    {
        var (reader, handler, _) = Make(_ => Reply(GoodReply));

        await reader.ReadAsync([Page(1)], CancellationToken.None);

        Assert.Equal("sample-test-value", Assert.Single(handler.Request!.Headers.GetValues("x-api-key")));
        Assert.DoesNotContain("sample-test-value", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoKeyIsUnavailableAndNothingIsSent()
    {
        var (reader, handler, _) = Make(_ => Reply(GoodReply), key: " ");

        var read = await reader.ReadAsync([Page(1)], CancellationToken.None);

        Assert.Equal(RecipePhotoText.NoKeySentence, Assert.IsType<RecipePhotoRead.Unavailable>(read).Sentence);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task APageThatIsNotAJpegStopsTheReadBeforeAnythingIsSent()
    {
        var (reader, handler, _) = Make(_ => Reply(GoodReply));

        var read = await reader.ReadAsync([Page(1), [1, 2, 3, 4]], CancellationToken.None);

        Assert.Equal(RecipePhotoText.PageUnreadableSentence, Assert.IsType<RecipePhotoRead.Unreadable>(read).Sentence);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task AnOversizedPageIsNeverSent()
    {
        var huge = new byte[ClaudeRecipeReader.LargestPageBytes + 1];
        huge[0] = 0xFF;
        huge[1] = 0xD8;
        var (reader, handler, _) = Make(_ => Reply(GoodReply));

        Assert.IsType<RecipePhotoRead.Unreadable>(await reader.ReadAsync([huge], CancellationToken.None));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task NoPagesSendsNothing()
    {
        var (reader, handler, _) = Make(_ => Reply(GoodReply));

        Assert.IsType<RecipePhotoRead.Unreadable>(await reader.ReadAsync([], CancellationToken.None));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task AReplyCutOffAtTheLimitIsNotTakenAsTheRecipe()
    {
        var (reader, _, _) = Make(_ => Reply("{\"is_recipe\":true,\"title\":\"Soup\",\"ingredients\":[\"1 onion\"]", "max_tokens"));

        var read = await reader.ReadAsync([Page(1)], CancellationToken.None);

        Assert.Equal(RecipePhotoText.TooLongSentence, Assert.IsType<RecipePhotoRead.Unreadable>(read).Sentence);
    }

    [Fact]
    public async Task ASenselessReplyIsUnreadable()
    {
        var (reader, _, _) = Make(_ => Reply("Sorry, I can't do that."));

        Assert.IsType<RecipePhotoRead.Unreadable>(await reader.ReadAsync([Page(1)], CancellationToken.None));
    }

    [Fact]
    public async Task ARefusalIsAFailureThatKeepsItsStatusAndFlagsTheKey()
    {
        var (reader, _, health) = Make(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"error\":{\"message\":\"invalid x-api-key\"}}"),
        });

        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => reader.ReadAsync([Page(1)], CancellationToken.None));

        Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
        health.Verify(sink => sink.MarkFailing(CredentialIds.Claude, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task AGoodCallMarksTheKeyWorking()
    {
        var (reader, _, health) = Make(_ => Reply(GoodReply));

        await reader.ReadAsync([Page(1)], CancellationToken.None);

        health.Verify(sink => sink.MarkOk(CredentialIds.Claude), Times.Once);
    }

    [Fact]
    public void TheReaderHoldsNoPageAfterTheCall()
    {
        var held = typeof(ClaudeRecipeReader)
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
            .Select(field => field.FieldType)
            .ToList();

        Assert.DoesNotContain(typeof(byte[]), held);
        Assert.DoesNotContain(typeof(IReadOnlyList<byte[]>), held);
    }
}
