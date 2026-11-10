using System.Net;
using System.Text.Json;
using Storyworthtelling.Handwriting;
using Xunit;

namespace Storyworthtelling.Handwriting.Tests;

/// <summary>
/// The one call that sends a written-on page to Claude (audit 01 §12). Three readers built it by hand — the shared
/// extractor, Foreword's journal transcriber and Constitution's ink read — each with the instruction cached ahead of the
/// page and the page ahead of the question. This holds that shape once.
/// </summary>
public class PageReaderTests
{
    private static readonly byte[] Page = [37, 80, 68, 70];

    [Fact]
    public async Task TheInstructionIsCachedAndThePageGoesBeforeTheQuestion()
    {
        var handler = new Recording("ink");
        var reader = new PageReader(new HttpClient(handler), () => "key", () => "model-a");

        var text = await reader.ReadAsync(Page, "Read the boxes.", "Boxes: a, b.", 512, CancellationToken.None);

        Assert.Equal("ink", text);
        using var body = JsonDocument.Parse(handler.Body!);
        var root = body.RootElement;
        Assert.Equal("model-a", root.GetProperty("model").GetString());
        Assert.Equal(512, root.GetProperty("max_tokens").GetInt32());

        var system = Assert.Single(root.GetProperty("system").EnumerateArray());
        Assert.Equal("Read the boxes.", system.GetProperty("text").GetString());
        Assert.True(system.TryGetProperty("cache_control", out _));

        var content = root.GetProperty("messages")[0].GetProperty("content").EnumerateArray().ToList();
        Assert.Equal(2, content.Count);
        Assert.Equal("document", content[0].GetProperty("type").GetString());
        Assert.Equal(Convert.ToBase64String(Page), content[0].GetProperty("source").GetProperty("data").GetString());
        Assert.Equal("Boxes: a, b.", content[1].GetProperty("text").GetString());
    }

    [Fact]
    public async Task TheModelAndKeyAreReadOnEveryCall()
    {
        var handler = new Recording("x");
        var model = "first";
        var reader = new PageReader(new HttpClient(handler), () => "key", () => model);

        await reader.ReadAsync(Page, "i", "q", 10, CancellationToken.None);
        model = "second";
        await reader.ReadAsync(Page, "i", "q", 10, CancellationToken.None);

        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("second", body.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task CancellationReachesTheRequest()
    {
        var reader = new PageReader(new HttpClient(new Recording("x")), () => "key", () => "m");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => reader.ReadAsync(Page, "i", "q", 10, cancelled.Token));
    }

    private sealed class Recording(string replyText) : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""{"content":[{"type":"text","text":{{JsonSerializer.Serialize(replyText)}}}]}"""),
            };
        }
    }
}
